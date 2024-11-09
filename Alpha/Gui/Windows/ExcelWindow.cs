using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Alpha.Services;
using Alpha.Services.Excel;
using Alpha.Services.Excel.Cells;
using Alpha.Utils;
using Hexa.NET.ImGui;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Alpha.Gui.Windows;

[Window("Excel")]
public class ExcelWindow : Window, IDisposable {
    private AlphaSheet? selectedSheet;

    private string sidebarFilter = string.Empty;
    private List<string>? filteredSheets;
    private float sidebarWidth = 300f;

    private string contentFilter = string.Empty;
    private List<uint>? filteredRows;
    private CancellationTokenSource? filterCts;

    private (string, int?)? queuedOpen;
    private int? highlightRow;
    private int? tempScroll;
    private bool painted;
    private float? itemHeight = 0;

    private readonly Dictionary<AlphaSheet, Dictionary<(int, int), CachedCell>> cellCache = new();

    private CancellationTokenSource? scriptToken;
    private CancellationTokenSource? sidebarToken;
    private string? scriptError;

    private readonly ExcelService excel;
    private readonly Config config;
    private readonly GameDataService gameData;
    private readonly ILogger<ExcelWindow> logger;

    public ExcelWindow(ExcelService excel, Config config, GameDataService gameData, ILogger<ExcelWindow> logger) {
        this.excel = excel;
        this.config = config;
        this.gameData = gameData;
        this.logger = logger;
        this.gameData.OnGameDataChanged += this.GameDataChanged;

        this.InitialSize = new Vector2(800, 600);
    }

    public void Dispose() {
        this.gameData.OnGameDataChanged -= this.GameDataChanged;
    }

    private void GameDataChanged() {
        this.selectedSheet = null;
        this.filteredSheets = null;
        this.filteredRows = null;
        this.cellCache.Clear();
        this.queuedOpen = null;
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
        {
            ImGui.SetNextItemWidth(this.sidebarWidth);

            // TODO: full text search

            if (ImGui.InputText("##ExcelFilter", ref this.sidebarFilter, 1024)) {
                this.ResolveSidebarFilter();
            }
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

        var shouldRed = this.scriptError is not null;
        if (shouldRed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
        var shouldOrange = this.scriptToken is not null && !shouldRed;
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
                ImGui.TextUnformatted(this.scriptError ?? "Unknown error");
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

    public void OpenSheet(string sheetName, int? scrollTo = null) {
        this.queuedOpen = (sheetName, scrollTo);
    }

    public void OpenSheet(AlphaSheet sheet, int? scrollTo = null) => this.OpenSheet(sheet.Name, scrollTo);

    private void ProcessQueuedOpen() {
        var (sheetName, scrollTo) = this.queuedOpen!.Value;
        this.queuedOpen = null;

        this.cellCache.Clear();

        this.highlightRow = scrollTo;
        this.tempScroll = scrollTo ?? 0;
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
    }

    private void ResolveSidebarFilter() {
        this.logger.LogDebug("Resolving sidebar filter...");

        if (string.IsNullOrEmpty(this.sidebarFilter)) {
            this.filteredSheets = null;
            return;
        }

        var filter = this.sidebarFilter.ToLower();
        this.filteredSheets = this.excel.Sheets
            .Where(x => x.ToLower().Contains(filter))
            .ToList();
    }

    private void ResolveContentFilter() {
        Log.Debug("Resolving content filter...");

        if (this.scriptToken is not null && !this.scriptToken.IsCancellationRequested) {
            this.scriptToken.Cancel();
            this.scriptToken.Dispose();
            this.scriptToken = null;
        }

        this.scriptError = null;

        if (string.IsNullOrEmpty(this.contentFilter)) {
            this.filteredRows = null;
            return;
        }

        if (this.selectedSheet is null) {
            this.filteredRows = null;
            return;
        }

        if (this.filterCts is not null) {
            this.filterCts.Cancel();
            this.filterCts.Dispose();
            this.filterCts = null;
        }

        this.filterCts = new CancellationTokenSource();
        if (this.contentFilter.StartsWith('$')) {
            this.ContentFilterScript(this.contentFilter[1..]);
        } else {
            this.ContentFilterSimple(this.contentFilter);
        }

        this.itemHeight = 0;
        Log.Debug("Filter resolved!");
    }

    private void ContentFilterSimple(string filter) {
        Task.Run(() => {
            this.filteredRows = new();
            var colCount = this.selectedSheet!.Sheet.Columns.Count;

            for (var i = 0u; i < this.selectedSheet.Sheet.Count; i++) {
                if (this.filterCts?.Token.IsCancellationRequested == true) return;

                var row = this.selectedSheet.GetRow(i);
                if (row is null) continue;

                var rowStr = row.Value.RowId.ToString();
                if (rowStr.Contains(filter, StringComparison.CurrentCultureIgnoreCase)) {
                    this.filteredRows!.Add(i);
                    continue;
                }

                for (var col = 0; col < colCount; col++) {
                    var obj = row.Value.ReadColumn(col);
                    var str = obj.ToString();

                    if (str?.ToLower().Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true) {
                        this.filteredRows!.Add(i);
                        break;
                    }
                }
            }
        }, this.filterCts!.Token);
    }

    private void ContentFilterScript(string script) {
        this.scriptError = null;
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
        var sheetInstance = genericMethod?.Invoke(this.gameData.GameData, [null, null]);

        var ct = new CancellationTokenSource();
        Task.Run(async () => {
            try {
                var globalsType = sheetRow != null
                                      ? typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow)
                                      : null;
                var expr = CSharpScript.Create<bool>(script, globalsType: globalsType);
                expr.Compile(ct.Token);

                for (var i = 0u; i < this.selectedSheet!.Sheet.Count; i++) {
                    if (ct.IsCancellationRequested) {
                        this.logger.LogDebug("Filter script cancelled - aborting");
                        return;
                    }

                    var row = this.selectedSheet.GetRow(i);
                    if (row is null) continue;

                    async void SimpleEval() {
                        try {
                            var res = await expr.RunAsync(cancellationToken: ct.Token);
                            if (res.ReturnValue) this.filteredRows?.Add(i);
                        } catch (Exception e) {
                            this.scriptError = e.Message;
                        }
                    }

                    if (sheetRow is null) {
                        SimpleEval();
                    } else {
                        var getRow = sheetInstance?.GetType().GetMethod("GetRow", [typeof(uint)]);
                        var instance = getRow?.Invoke(sheetInstance, [row.Value.RowId]);

                        // new ExcelScriptingGlobal<ExcelRow>(sheet, row);
                        var excelScriptingGlobal = typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow);
                        var globals = Activator.CreateInstance(excelScriptingGlobal, sheetInstance, instance);
                        if (globals is null) {
                            SimpleEval();
                        } else {
                            try {
                                var res = await expr.RunAsync(globals, ct.Token);
                                if (res.ReturnValue) {
                                    this.filteredRows?.Add(i);
                                }
                            } catch (Exception e) {
                                this.scriptError = e.Message;
                            }
                        }
                    }
                }
            } catch (Exception e) {
                this.logger.LogError(e, "Filter script failed");
                this.scriptError = e.Message;
            }

            this.logger.LogDebug("Filter script finished");
            this.scriptToken = null;
        }, ct.Token);

        this.scriptToken = ct;
    }

    private void DrawSheet(float width) {
        ImGui.SetNextItemWidth(width);

        // Wait for the sheet definition request to finish before drawing the sheet
        // This does *not* mean sheets with no definitions will be skipped
        if (!this.excel.SheetDefinitions.TryGetValue(this.selectedSheet!.Name, out var sheetDefinition)) {
            return;
        }

        var rowCount = this.selectedSheet.Sheet.Count;
        var colCount = this.selectedSheet.Sheet.Columns.Count;
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

        var colMappings = new int[colCount];
        if (this.config.SortByOffsets) {
            var colOffsets = new Dictionary<int, uint>();

            for (var i = 0; i < colCount; i++) {
                var col = this.selectedSheet.Sheet.Columns[i];
                colOffsets[i] = col.Offset;
            }

            colOffsets = colOffsets
                .OrderBy(x => x.Value)
                .ToDictionary(x => x.Key, x => x.Value);

            for (var i = 0; i < colCount; i++) colMappings[i] = colOffsets.ElementAt(i).Key;
        } else {
            for (var i = 0; i < colCount; i++) colMappings[i] = i;
        }

        for (var i = 0; i < colCount; i++) {
            var colId = colMappings[i];
            var colName = sheetDefinition?.GetNameForColumn(colId) ?? colId.ToString();

            var col = this.selectedSheet.Sheet.Columns[colId];
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
        }

        var actualRowCount = this.filteredRows?.Count ?? (int) rowCount;
        var clipper = new ListClipper(actualRowCount, itemHeight: this.itemHeight ?? 0);

        // Sheets can have non-linear row IDs, so we use the index the row appears in the sheet instead of the row ID
        var newHeight = 0f;
        var shouldPopColor = false;
        foreach (var i in clipper.Rows) {
            var rowId = i;
            if (this.filteredRows is not null) {
                rowId = (int) this.filteredRows[i];
            }

            // TODO: probably slow as hell, cache this
            var row = this.selectedSheet.GetRow((uint) rowId);
            if (row is null) {
                ImGui.TableNextRow();
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var highlighted = this.highlightRow == rowId && this.config.HighlightLinks;
            if (highlighted) {
                var newBg = new Vector4(1f, 0.5f, 0f, 0.5f);
                ImGui.PushStyleColor(ImGuiCol.TableRowBg, newBg);
                ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, newBg);
            }

            if (shouldPopColor) ImGui.PopStyleColor(2);
            if (highlighted) shouldPopColor = true;

            var str = row.Value.RowId.ToString();
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

                this.DrawCell(this.selectedSheet, rowId, colMappings[col]);

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

        if (this.itemHeight is not null && newHeight > this.itemHeight) {
            this.itemHeight = newHeight;
        }

        // I don't know why I need to do this but I really don't care, it's 12 AM and I want sleep
        // seems to crash if you scroll immediately, seems to do nothing if you scroll too little
        // stupid tick hack works for now lol
        if (this.tempScroll is not null && this.painted) {
            ImGuiP.SetScrollY(this.tempScroll * this.itemHeight ?? 0);
            this.tempScroll = null;
        }

        clipper.End();
        ImGui.EndTable();

        this.painted = true;
    }

    private Cell GetCell(AlphaSheet sheet, int rowId, int colId) {
        if (!this.cellCache.TryGetValue(sheet, out var realCellCache)) {
            this.cellCache[sheet] = realCellCache = new();
        }

        if (realCellCache.TryGetValue((rowId, colId), out var cachedCell)) {
            return cachedCell.Value;
        }

        var row = sheet.GetRow((uint) rowId);
        var data = row?.ReadColumn(colId);
        var cell = this.excel.GetCell(sheet, rowId, colId, data);
        var cached = new CachedCell(cell);
        realCellCache[(rowId, colId)] = cached;
        return cached.Value;
    }

    public void DrawCell(AlphaSheet sheet, int rowId, int colId, bool inAnotherDraw = false) {
        var cell = this.GetCell(sheet, rowId, colId);
        cell.Draw(this, inAnotherDraw);
    }

    // This is bad but I'm lazy
    public ExcelService GetExcelService() => this.excel;
}
