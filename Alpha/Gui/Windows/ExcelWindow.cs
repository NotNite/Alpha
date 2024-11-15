using System.Numerics;
using System.Reflection;
using Alpha.Game;
using Alpha.Services;
using Alpha.Services.Excel;
using Alpha.Services.Excel.Cells;
using Alpha.Utils;
using Hexa.NET.ImGui;
using Lumina;
using Lumina.Excel;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Alpha.Gui.Windows;

[Window("Excel")]
public class ExcelWindow : Window {
    private IAlphaSheet? selectedSheet;

    private (string, (uint Row, ushort? Subrow)?)? queuedOpen;
    private (uint Row, ushort? Subrow)? highlightRow;
    private (uint Row, ushort? Subrow)? tempScroll;
    private bool painted;
    private float? itemHeight = 0;

    private readonly Dictionary<IAlphaSheet, Dictionary<(uint, uint), CachedCell>> cellCache = new();
    private readonly List<(uint Row, ushort? Subrow)> rowMap = new();

    private string sidebarFilter = string.Empty;
    private bool fullTextSearch;
    private List<string>? filteredSheets;
    private CancellationTokenSource? sidebarFilterCts;
    private float sidebarWidth = 300f;

    private string contentFilter = string.Empty;
    private List<(uint Row, ushort? Subrow)>? filteredRows;
    private CancellationTokenSource? contentFilterCts;
    private string? contentFilterError;

    private readonly GameDataService gameDataService;
    private readonly ExcelService excel;
    private readonly Config config;
    private readonly ILogger<ExcelWindow> logger;

    public ExcelWindow(
        GameDataService gameDataService, ExcelService excel, Config config, AlphaGameData gameData,
        ILogger<ExcelWindow> logger
    ) {
        this.gameDataService = gameDataService;
        this.excel = excel;
        this.config = config;
        this.GameData = gameData;
        this.logger = logger;
        this.excel.GameData = gameData;
        this.InitialSize = new Vector2(800, 600);
    }

    private void GameDataChanged() {
        this.selectedSheet = null;
        this.filteredSheets = null;
        this.filteredRows = null;

        this.sidebarFilter = string.Empty;
        this.contentFilter = string.Empty;
        this.sidebarFilterCts?.Cancel();
        this.sidebarFilterCts?.Dispose();

        this.sidebarFilterCts = null;
        this.contentFilterCts?.Cancel();
        this.contentFilterCts?.Dispose();
        this.contentFilterCts = null;

        this.ResolveSidebarFilter();

        this.queuedOpen = null;
        this.cellCache.Clear();
        this.rowMap.Clear();
        this.highlightRow = null;
        this.tempScroll = null;
        this.painted = false;
        this.itemHeight = 0;
    }

    protected override void Draw() {
        if (this.queuedOpen is not null) this.ProcessQueuedOpen();

        this.DrawSidebar();

        ImGui.BeginGroup();

        if (this.selectedSheet is not null) {
            var width = ImGui.GetContentRegionAvail().X;
            this.DrawContentFilter(width);
            this.DrawSheet(width);
        }

        ImGui.EndGroup();

        // Clear unused cached cells
        const int seconds = 5;
        var cutoff = ImGui.GetTime() < seconds ? 0 : ImGui.GetTime() - seconds;
        foreach (var (_, dict) in this.cellCache) {
            foreach (var (key, value) in dict) {
                if (value.LastUsed < cutoff) {
                    dict.Remove(key);
                }
            }
        }
    }

    private void DrawSidebar() {
        var temp = ImGui.GetCursorPosY();

        Components.DrawFakeHamburger(() => {
            if (Components.DrawGameDataPicker(this.gameDataService, this.GameData!) is { } newGameData) {
                this.logger.LogDebug("Game data changed to {NewGameData}", newGameData.GamePath);
                this.GameData = newGameData;
                this.excel.SetGameData(newGameData);
                this.GameDataChanged();
            }
        });

        ImGui.SameLine();

        {
            ImGui.SetNextItemWidth(this.sidebarWidth - ImGui.GetCursorPosY());

            var shouldOrange = this.fullTextSearch;
            if (shouldOrange) ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 0.5f, 0f, 0.5f));

            if (ImGui.InputText("##ExcelFilter", ref this.sidebarFilter, 1024,
                    ImGuiInputTextFlags.EnterReturnsTrue)) {
                this.ResolveSidebarFilter();
            }

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                var filterMode = this.fullTextSearch ? "Full text search" : "Name search";
                ImGui.TextUnformatted(
                    $"Current filter mode: {filterMode}\n"
                    + "Right click to change the filter mode.");

                if (Util.IsMouseClicked(ImGuiMouseButton.Right)) {
                    this.fullTextSearch = !this.fullTextSearch;
                }

                ImGui.EndTooltip();
            }

            if (shouldOrange) ImGui.PopStyleColor();
        }

        var cra = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##ExcelModule_Sidebar", cra with {X = this.sidebarWidth}, ImGuiChildFlags.Borders);

        var sheets = this.filteredSheets?.ToArray() ?? this.excel.Sheets;
        foreach (var sheet in sheets) {
            if (ImGui.Selectable(sheet, sheet == this.selectedSheet?.Name)) {
                this.OpenSheet(sheet);
            }

            if (ImGui.BeginPopupContextItem($"##ExcelModule_Sidebar_{sheet}")) {
                if (ImGui.Selectable("Open in new window")) {
                    this.excel.OpenNewWindow(sheet);
                }

                ImGui.EndPopup();
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        Components.DrawHorizontalSplitter(ref this.sidebarWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);
    }

    private void DrawContentFilter(float width) {
        ImGui.SetNextItemWidth(width);

        var shouldRed = this.contentFilterError is not null;
        if (shouldRed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
        var shouldOrange = this.contentFilterCts is not null && !shouldRed;
        if (shouldOrange) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.5f, 0f, 1f));

        if (ImGui.InputText("##ExcelContentFilter", ref this.contentFilter, 1024,
                ImGuiInputTextFlags.EnterReturnsTrue)) {
            this.ResolveContentFilter();
        }

        // Disable filter on right click
        if (ImGui.IsItemHovered() && Util.IsMouseClicked(ImGuiMouseButton.Right)) {
            this.contentFilter = string.Empty;
            this.ResolveContentFilter();
        }

        if (shouldRed) {
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(this.contentFilterError ?? "Unknown error");
                ImGui.EndTooltip();
            }
        }

        if (shouldOrange) {
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(
                    "A script is currently running on each row. This may impact performance.\n"
                    + "To stop the script, empty or right click the input box.");
                ImGui.EndTooltip();
            }
        }
    }

    public void OpenSheet(string sheetName, (uint Row, ushort? Subrow)? scrollTo = null) {
        this.queuedOpen = (sheetName, scrollTo);
    }

    public void OpenSheet(IAlphaSheet sheet, (uint Row, ushort? Subrow)? scrollTo = null) =>
        this.OpenSheet(sheet.Name, scrollTo);

    private void ProcessQueuedOpen() {
        var (sheetName, scrollTo) = this.queuedOpen!.Value;
        this.queuedOpen = null;

        this.cellCache.Clear();

        this.highlightRow = scrollTo;
        this.tempScroll = scrollTo ?? (0, null);
        this.painted = false;

        var sheet = this.excel.GetSheet(sheetName);
        if (sheet is null) {
            this.logger.LogWarning("Tried to open sheet that doesn't exist: {SheetName}", sheetName);
            return;
        }

        this.logger.LogDebug("Opening sheet: {SheetName} {ScrollTo}", sheetName, scrollTo);
        this.selectedSheet = sheet;
        this.filteredRows = null;
        this.itemHeight = 0;

        this.contentFilter = string.Empty;
        this.ResolveContentFilter();

        this.rowMap.Clear();
        foreach (var row in this.selectedSheet.GetRows()) {
            this.rowMap.Add((row.Row, row.Subrow));
        }
    }

    private void ResolveSidebarFilter() {
        this.logger.LogDebug("Resolving sidebar filter...");

        if (string.IsNullOrEmpty(this.sidebarFilter)) {
            this.filteredSheets = null;
            return;
        }

        if (this.sidebarFilterCts is not null) {
            this.sidebarFilterCts.Cancel();
            this.sidebarFilterCts.Dispose();
            this.sidebarFilterCts = null;
        }
        this.sidebarFilterCts = new CancellationTokenSource();

        this.filteredSheets = new();
        var filter = this.sidebarFilter;
        var fullText = this.fullTextSearch;
        Task.Run(() => {
            foreach (var sheet in this.excel.Sheets) {
                if (this.sidebarFilterCts?.Token.IsCancellationRequested == true) break;

                if (fullText) {
                    var raw = this.excel.GetSheet(sheet, resolveDefinition: false);
                    if (raw is null) break;

                    var found = false;
                    foreach (var row in raw.GetRows()) {
                        if (this.sidebarFilterCts?.Token.IsCancellationRequested == true || found) break;
                        for (var i = 0u; i < raw.Columns.Count; i++) {
                            if (this.sidebarFilterCts?.Token.IsCancellationRequested == true) break;

                            try {
                                var obj = row.ReadColumn(i);
                                if (obj.ToString()?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ==
                                    true) {
                                    lock (this.filteredSheets) {
                                        if (!this.filteredSheets.Contains(sheet)) {
                                            this.filteredSheets.Add(sheet);
                                            this.filteredSheets.Sort();
                                        }
                                    }

                                    found = true;
                                    break;
                                }
                            } catch {
                                // Some sheets have invalid SeString
                            }
                        }
                    }
                } else {
                    if (sheet.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) {
                        this.filteredSheets.Add(sheet);
                    }
                }
            }

            this.logger.LogDebug("Sidebar filter resolved!");
            this.sidebarFilterCts?.Dispose();
            this.sidebarFilterCts = null;
        }, this.sidebarFilterCts!.Token);
    }

    private void ResolveContentFilter() {
        this.logger.LogDebug("Resolving content filter...");

        this.contentFilterError = null;
        if (this.contentFilterCts is not null) {
            this.contentFilterCts.Cancel();
            this.contentFilterCts.Dispose();
            this.contentFilterCts = null;
        }

        if (string.IsNullOrEmpty(this.contentFilter)) {
            this.filteredRows = null;
            return;
        }

        if (this.selectedSheet is null) {
            this.filteredRows = null;
            return;
        }

        if (this.contentFilterCts is not null) {
            this.contentFilterCts.Cancel();
            this.contentFilterCts.Dispose();
            this.contentFilterCts = null;
        }
        this.contentFilterCts = new CancellationTokenSource();

        if (this.contentFilter.StartsWith('$')) {
            this.ContentFilterScript(this.contentFilter[1..]);
        } else {
            this.ContentFilterSimple(this.contentFilter);
        }

        this.itemHeight = 0;
        this.logger.LogDebug("Content filter resolved!");
    }

    private void ContentFilterSimple(string filter) {
        Task.Run(() => {
            this.filteredRows = new();
            var colCount = this.selectedSheet!.Columns.Count;

            foreach (var row in this.selectedSheet.GetRows()) {
                if (this.contentFilterCts?.Token.IsCancellationRequested == true) return;

                var rowStr = row.Row.ToString();
                if (row.Subrow is not null) rowStr += $".{row.Subrow}";
                if (rowStr.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) {
                    this.filteredRows!.Add((row.Row, row.Subrow));
                    continue;
                }

                for (var col = 0u; col < colCount; col++) {
                    var obj = row.ReadColumn(col);
                    var str = obj.ToString();

                    if (str?.ToLower().Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true) {
                        this.filteredRows!.Add((row.Row, row.Subrow));
                        break;
                    }
                }
            }

            this.contentFilterCts?.Dispose();
            this.contentFilterCts = null;
        }, this.contentFilterCts!.Token);
    }

    private void ContentFilterScript(string script) {
        this.contentFilterError = null;
        this.filteredRows = new();

        // picked a random type for this, doesn't really matter
        var luminaTypes = Assembly.GetAssembly(typeof(Lumina.Excel.Sheets.Addon))?.GetTypes()
            .Where(t => t.Namespace == "Lumina.Excel.Sheets")
            .ToList();
        var sheets = luminaTypes?
            .Where(t => t.GetCustomAttributes(typeof(SheetAttribute), false).Length > 0)
            .ToDictionary(t => ((SheetAttribute) t.GetCustomAttributes(typeof(SheetAttribute), false)[0]).Name!);

        Type? sheetRow = null;
        if (sheets?.TryGetValue(this.selectedSheet!.Name, out var sheetType) == true) {
            sheetRow = sheetType;
        }

        // GameData.GetExcelSheet<T>();
        var getExcelSheet = typeof(GameData).GetMethods().FirstOrDefault(x => x.Name == "GetExcelSheet");
        var genericMethod = sheetRow is not null ? getExcelSheet?.MakeGenericMethod(sheetRow) : null;
        var sheetInstance = genericMethod?.Invoke(this.GameData?.GameData, [null, null]);

        Task.Run(async () => {
            try {
                var globalsType = sheetRow != null
                                      ? typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow)
                                      : null;
                var expr = CSharpScript.Create<bool>(script, globalsType: globalsType);
                expr.Compile(this.contentFilterCts!.Token);

                for (var i = 0u; i < this.selectedSheet!.Count; i++) {
                    if (this.contentFilterCts?.Token.IsCancellationRequested == true) {
                        this.logger.LogDebug("Filter script cancelled - aborting");
                        return;
                    }

                    var row = this.selectedSheet.GetRow(i);
                    if (row is null) continue;

                    async void SimpleEval() {
                        try {
                            var res = await expr.RunAsync(cancellationToken: this.contentFilterCts!.Token);
                            if (res.ReturnValue) this.filteredRows?.Add((row.Row, row.Subrow));
                        } catch (Exception e) {
                            this.contentFilterError = e.Message;
                        }
                    }

                    if (sheetRow is null) {
                        SimpleEval();
                    } else {
                        var getRow = sheetInstance?.GetType().GetMethod("GetRow", [typeof(uint)]);
                        var instance = getRow?.Invoke(sheetInstance, [row.Row]);

                        // new ExcelScriptingGlobal<ExcelRow>(sheet, row);
                        var excelScriptingGlobal = typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow);
                        var globals = Activator.CreateInstance(excelScriptingGlobal, sheetInstance, instance);
                        if (globals is null) {
                            SimpleEval();
                        } else {
                            try {
                                var res = await expr.RunAsync(globals, this.contentFilterCts!.Token);
                                if (res.ReturnValue) {
                                    this.filteredRows?.Add((row.Row, row.Subrow));
                                }
                            } catch (Exception e) {
                                this.contentFilterError = e.Message;
                            }
                        }
                    }
                }
            } catch (Exception e) {
                this.logger.LogError(e, "Filter script failed");
                this.contentFilterError = e.Message;
            }

            this.logger.LogDebug("Filter script finished");
            this.contentFilterCts?.Dispose();
            this.contentFilterCts = null;
        }, this.contentFilterCts!.Token);
    }

    private void DrawSheet(float width) {
        ImGui.SetNextItemWidth(width);

        // Wait for the sheet definition request to finish before drawing the sheet
        // This does *not* mean sheets with no definitions will be skipped
        if (!this.excel.SheetDefinitions.TryGetValue(this.selectedSheet!.Name, out var sheetDefinition)) {
            return;
        }

        var rowCount = this.selectedSheet.Count;
        var colCount = this.selectedSheet.Columns.Count;
        colCount = Math.Min(colCount, 2048 - 1); // I think this is an ImGui limitation?

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders
                                      | ImGuiTableFlags.NoSavedSettings
                                      | ImGuiTableFlags.RowBg
                                      | ImGuiTableFlags.Resizable
                                      | ImGuiTableFlags.ScrollX
                                      | ImGuiTableFlags.ScrollY;

        // +1 here for the row ID column
        if (!ImGui.BeginTable("##ExcelTable", (int) (colCount + 1), flags)) {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);

        ImGui.TableHeadersRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TableHeader("Row");

        var colMappings = new uint[colCount];
        if (this.config.SortByOffsets) {
            var colOffsets = new Dictionary<uint, uint>();

            for (var i = 0u; i < colCount; i++) {
                var col = this.selectedSheet.Columns[(int) i];
                colOffsets[i] = col.Offset;
            }

            colOffsets = colOffsets
                .OrderBy(x => x.Value)
                .ToDictionary(x => x.Key, x => x.Value);

            for (var i = 0; i < colCount; i++) colMappings[i] = colOffsets.ElementAt(i).Key;
        } else {
            for (var i = 0u; i < colCount; i++) colMappings[i] = i;
        }

        for (var i = 0; i < colCount; i++) {
            ImGui.PushID(i);
            var colId = colMappings[i];
            var colName = sheetDefinition?.GetNameForColumn(colId) ?? colId.ToString();

            var col = this.selectedSheet.Columns[(int) colId];
            var offset = col.Offset;
            var offsetStr = $"Offset: {offset} (0x{offset:X})\nIndex: {colId}\nData type: {col.Type.ToString()}";

            if (this.config.AlwaysShowOffsets) colName += "\n" + offsetStr;

            ImGui.TableSetColumnIndex(i + 1);
            ImGui.TableHeader(colName);

            if (ImGui.IsItemHovered() && !this.config.AlwaysShowOffsets) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(offsetStr);
                ImGui.EndTooltip();
            }
            ImGui.PopID();
        }

        var actualRowCount = this.filteredRows?.Count ?? (int) rowCount;
        var clipper = new ListClipper(actualRowCount, itemHeight: this.itemHeight ?? 0);

        // Sheets can have non-linear row IDs, so we use the index the row appears in the sheet instead of the row ID
        var newHeight = 0f;
        var shouldPopColor = false;
        foreach (var i in clipper.Rows) {
            var rowData = this.rowMap[i];
            if (this.filteredRows is not null) {
                rowData = this.filteredRows[i];
            }
            var (rowId, subrowId) = rowData;

            // TODO: probably slow as hell, cache this
            var row = this.selectedSheet.GetRow(rowId, subrowId);
            if (row is null) {
                ImGui.TableNextRow();
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var highlighted = this.highlightRow is not null
                              && this.highlightRow.Value.Row == rowId
                              && this.highlightRow.Value.Subrow == subrowId
                              && this.config.HighlightLinks;
            if (highlighted) {
                var newBg = new Vector4(1f, 0.5f, 0f, 0.5f);
                ImGui.PushStyleColor(ImGuiCol.TableRowBg, newBg);
                ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, newBg);
            }

            if (shouldPopColor) {
                ImGui.PopStyleColor(2);
                shouldPopColor = false;
            }
            if (highlighted) shouldPopColor = true;

            var str = row.Row.ToString();
            if (row.Subrow is not null) {
                str += $".{row.Subrow}";
            }
            ImGui.TextUnformatted(str);
            if (ImGui.BeginPopupContextItem($"##ExcelModule_Row_{rowId}")) {
                if (ImGui.Selectable("Copy row ID")) {
                    ImGui.SetClipboardText(str);
                }

                ImGui.EndPopup();
            }

            ImGui.TableNextColumn();

            for (var col = 0; col < colCount; col++) {
                var prev = ImGui.GetCursorPosY();

                this.DrawCell(this.selectedSheet, rowId, subrowId, colMappings[col]);

                var next = ImGui.GetCursorPosY();
                if (this.itemHeight is not null) {
                    var spacing = ImGui.GetStyle().ItemSpacing.Y;
                    var height = next - prev;
                    var needed = this.itemHeight.Value - (height + spacing);
                    if (needed > 0) {
                        ImGui.Dummy(new Vector2(0, needed));
                    }

                    if (height > newHeight) newHeight = height;
                }

                if (col < colCount - 1) ImGui.TableNextColumn();
            }
        }
        if (shouldPopColor) ImGui.PopStyleColor(2);

        if (this.itemHeight is not null && newHeight > this.itemHeight) {
            this.itemHeight = newHeight;
        }

        // I don't know why I need to do this but I really don't care, it's 12 AM and I want sleep
        // seems to crash if you scroll immediately, seems to do nothing if you scroll too little
        // stupid tick hack works for now lol
        if (this.tempScroll is not null && this.painted) {
            var pos = this.rowMap.IndexOf(this.tempScroll.Value);
            ImGuiP.SetScrollY(pos * this.itemHeight ?? 0);
            this.tempScroll = null;
        }

        clipper.End();
        ImGui.EndTable();

        this.painted = true;
    }

    private Cell GetCell(IAlphaSheet sheet, uint rowId, ushort? subrowId, uint colId) {
        if (!this.cellCache.TryGetValue(sheet, out var realCellCache)) {
            this.cellCache[sheet] = realCellCache = new();
        }

        if (realCellCache.TryGetValue((rowId, colId), out var cachedCell)) {
            return cachedCell.Value;
        }

        var row = sheet.GetRow(rowId);
        var data = row?.ReadColumn(colId);
        var cell = this.excel.GetCell(sheet, rowId, subrowId, colId, data);
        var cached = new CachedCell(cell);
        realCellCache[(rowId, colId)] = cached;
        return cached.Value;
    }

    public void DrawCell(IAlphaSheet sheet, uint rowId, ushort? subrowId, uint colId, bool inAnotherDraw = false) {
        var cell = this.GetCell(sheet, rowId, subrowId, colId);
        cell.Draw(this, inAnotherDraw);
    }

    // This is bad but I'm lazy
    public ExcelService GetExcelService() => this.excel;
}
