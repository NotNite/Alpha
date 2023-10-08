using System.Numerics;
using System.Reflection;
using Alpha.Core;
using Alpha.Modules.Excel;
using Alpha.Utils;
using ImGuiNET;
using Lumina;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Serilog;
using ExcelModule = Alpha.Modules.Excel.ExcelModule;

namespace Alpha.Windows;

public class ExcelWindow : Window {
    private readonly ExcelModule module;
    private RawExcelSheet? selectedSheet;
    private Dictionary<uint, (uint, uint?)> rowMapping = new(); // Working around a Lumina bug to map index to row

    private float sidebarWidth = 300f;
    private bool fullTextSearch;

    private string sidebarFilter = string.Empty;
    private string contentFilter = string.Empty;
    private List<string>? filteredSheets;
    private List<uint>? filteredRows;

    private CancellationTokenSource? scriptToken;
    private CancellationTokenSource? sidebarToken;
    private string? scriptError;

    private int? tempScroll;
    private int paintTicksLeft = -1;
    private float? itemHeight = 0;

    public ExcelWindow(ExcelModule module) {
        this.Name = "Excel Browser";
        this.module = module;
        this.InitialSize = new(960, 540);
    }

    public void Reload() {
        // Re-fetch the sheet for language changes
        if (this.selectedSheet is null) return;
        this.selectedSheet = this.module.GetSheet(this.selectedSheet!.Name);
    }

    public void OpenSheet(string sheetName, int? scrollTo = null) {
        this.tempScroll = scrollTo;

        var sheet = this.module.GetSheet(sheetName);
        if (sheet is null) {
            Log.Warning("Tried to open sheet that doesn't exist: {SheetName}", sheetName);
            return;
        }

        Log.Debug("Opening sheet: {SheetName}", sheetName);
        this.selectedSheet = sheet;
        this.filteredRows = null;
        this.itemHeight = 0;

        this.rowMapping = this.SetupRows(sheet);
        this.ResolveContentFilter();
    }

    // TODO deduplicate this code from fs module
    protected override void Draw() {
        this.DrawSidebar();

        ImGui.BeginGroup();

        if (this.selectedSheet is not null) {
            var width = ImGui.GetContentRegionAvail().X;
            this.DrawContentFilter(width);
            this.DrawSheet(width);
        }

        ImGui.EndGroup();
    }

    private void DrawSidebar() {
        var temp = ImGui.GetCursorPosY();
        this.DrawSidebarFilter(this.sidebarWidth);

        var cra = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##ExcelModule_Sidebar", cra with {X = this.sidebarWidth}, true);

        var sheets = this.filteredSheets?.ToArray() ?? this.module.Sheets;
        foreach (var sheet in sheets) {
            if (ImGui.Selectable(sheet, sheet == this.selectedSheet?.Name)) {
                this.OpenSheet(sheet);
            }

            if (ImGui.BeginPopupContextItem($"##ExcelModule_Sidebar_{sheet}")) {
                if (ImGui.Selectable("Open in new window")) {
                    this.module.OpenNewWindow(sheet);
                }

                ImGui.EndPopup();
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        UiUtils.HorizontalSplitter(ref this.sidebarWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);
    }

    private void DrawSidebarFilter(float width) {
        ImGui.SetNextItemWidth(width);

        var shouldOrange = this.fullTextSearch;
        if (shouldOrange) ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(1f, 0.5f, 0f, 0.5f));

        var flags = this.fullTextSearch ? ImGuiInputTextFlags.EnterReturnsTrue : ImGuiInputTextFlags.None;
        if (ImGui.InputText("##ExcelFilter", ref this.sidebarFilter, 1024, flags)) {
            this.ResolveSidebarFilter();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.BeginTooltip();
            var filterMode = this.fullTextSearch ? "Full text search" : "Name search";
            ImGui.TextUnformatted(
                $"Current filter mode: {filterMode}\n"
                + "Right click to change the filter mode.");

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                this.fullTextSearch = !this.fullTextSearch;
            }

            ImGui.EndTooltip();
        }

        if (shouldOrange) ImGui.PopStyleColor();
    }

    private void DrawContentFilter(float width) {
        ImGui.SetNextItemWidth(width);

        var shouldRed = this.scriptError is not null;
        if (shouldRed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
        var shouldOrange = this.scriptToken is not null && !shouldRed;
        if (shouldOrange) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.5f, 0f, 1f));

        var flags = this.contentFilter.StartsWith("$")
                        ? ImGuiInputTextFlags.EnterReturnsTrue
                        : ImGuiInputTextFlags.None;
        if (ImGui.InputText("##ExcelContentFilter", ref this.contentFilter, 1024, flags)) {
            this.ResolveContentFilter();
        }

        // Disable filter on right click
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
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

    private void DrawSheet(float width) {
        ImGui.SetNextItemWidth(width);

        // Wait for the sheet definition request to finish before drawing the sheet
        // This does *not* mean sheets with no definitions will be skipped
        if (!this.module.SheetDefinitions.TryGetValue(this.selectedSheet!.Name, out var sheetDefinition)) {
            return;
        }

        var rowCount = this.selectedSheet.RowCount;
        var colCount = this.selectedSheet.ColumnCount;
        colCount = Math.Min(colCount, 2048 - 1); // I think this is an ImGui limitation?

        var flags = ImGuiTableFlags.Borders
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

        for (var i = 0; i < colCount; i++) {
            var colName = sheetDefinition?.GetNameForColumn(i) ?? i.ToString();

            var col = this.selectedSheet.Columns[i];
            var offset = col.Offset;
            var offsetStr = $"Offset: {offset} (0x{offset:X})\nIndex: {i}\nData type: {col.Type.ToString()}";

            if (Services.Configuration.AlwaysShowOffsets) colName += "\n" + offsetStr;

            ImGui.TableSetColumnIndex(i + 1);
            ImGui.TableHeader(colName);

            if (ImGui.IsItemHovered() && !Services.Configuration.AlwaysShowOffsets) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(offsetStr);
                ImGui.EndTooltip();
            }
        }

        var actualRowCount = this.filteredRows?.Count ?? (int) rowCount;
        var clipper = new ListClipper(actualRowCount, itemHeight: this.itemHeight ?? 0);

        // Sheets can have non-linear row IDs, so we use the index the row appears in the sheet instead of the row ID
        var newHeight = 0f;
        foreach (var i in clipper.Rows) {
            var rowId = i;
            if (this.filteredRows is not null) {
                rowId = (int) this.filteredRows[i];
            }

            var row = this.GetRow(this.selectedSheet, this.rowMapping, (uint) rowId);
            if (row is null) {
                ImGui.TableNextRow();
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var str = row.RowId.ToString();
            if (row.SubRowId != 0) str += $".{row.SubRowId}";
            ImGui.TextUnformatted(str);
            if (ImGui.BeginPopupContextItem($"##ExcelModule_Row_{rowId}")) {
                if (ImGui.Selectable("Copy row ID")) {
                    ImGui.SetClipboardText(str);
                }

                ImGui.EndPopup();
            }

            ImGui.TableNextColumn();

            for (var col = 0; col < colCount; col++) {
                var obj = row.ReadColumnRaw(col);
                var prev = ImGui.GetCursorPosY();

                if (obj != null) {
                    var converter = sheetDefinition?.GetConverterForColumn(col);

                    this.module.DrawEntry(
                        this,
                        this.selectedSheet,
                        rowId,
                        col,
                        obj,
                        converter
                    );
                }

                var next = ImGui.GetCursorPosY();
                if (this.itemHeight is not null) {
                    var height = next - prev + ImGui.GetStyle().ItemSpacing.Y;
                    var needed = this.itemHeight.Value - height;
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
        if (this.tempScroll is not null & this.paintTicksLeft == -1) {
            this.paintTicksLeft = 5;
        } else if (this.paintTicksLeft <= 0) {
            this.tempScroll = null;
            this.paintTicksLeft = -1;
        } else if (this.tempScroll is not null) {
            ImGui.SetScrollY(this.tempScroll.Value * clipper.ItemsHeight);
            this.paintTicksLeft--;
        }

        clipper.End();
        ImGui.EndTable();
    }

    // Mapping index to row/subrow ID, since they are not linear
    private Dictionary<uint, (uint, uint?)> SetupRows(RawExcelSheet sheet) {
        var rowMapping = new Dictionary<uint, (uint, uint?)>();

        var currentRow = 0u;
        foreach (var page in sheet.DataPages) {
            foreach (var row in page.File.RowData.Values) {
                var parser = new RowParser(sheet, page.File);
                parser.SeekToRow(row.RowId);

                if (sheet.Header.Variant == ExcelVariant.Subrows) {
                    for (uint i = 0; i < parser.RowCount; i++) {
                        rowMapping[currentRow] = (row.RowId, i);
                        currentRow++;
                    }
                } else {
                    rowMapping[currentRow] = (row.RowId, null);
                    currentRow++;
                }
            }
        }

        return rowMapping;
    }

    // Building a new RowParser every time is probably not the best idea, but doesn't seem to impact performance that hard
    private RowParser? GetRow(
        RawExcelSheet sheet,
        Dictionary<uint, (uint, uint?)> rowMapping,
        uint index
    ) {
        var (row, subrow) = rowMapping[index];
        var page = sheet.DataPages.FirstOrDefault(x => x.File.RowData.ContainsKey(row));
        if (page is null) return null;

        var parser = new RowParser(sheet, page.File);
        if (subrow is not null) {
            parser.SeekToRow(row, subrow.Value);
        } else {
            parser.SeekToRow(row);
        }

        return parser;
    }

    private void ResolveContentFilter() {
        Log.Debug("Resolving content filter...");

        // clean up scripts
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

        this.filteredRows = new();
        if (this.contentFilter.StartsWith("$")) {
            var script = this.contentFilter[1..];
            this.ContentFilterScript(script);
        } else {
            this.ContentFilterSimple(this.contentFilter);
        }

        this.itemHeight = 0;
        Log.Debug("Filter resolved!");
    }

    private void ContentFilterSimple(string filter) {
        var colCount = this.selectedSheet!.ColumnCount;
        for (var i = 0u; i < this.selectedSheet.RowCount; i++) {
            var row = this.GetRow(this.selectedSheet, this.rowMapping, i);
            if (row is null) continue;

            var rowStr = row.RowId.ToString();
            if (row.SubRowId != 0) rowStr += $".{row.SubRowId}";
            if (rowStr.ToLower().Contains(filter.ToLower())) {
                this.filteredRows!.Add(i);
                continue;
            }

            for (var col = 0; col < colCount; col++) {
                var obj = row.ReadColumnRaw(col);
                if (obj is null) continue;
                var str = this.module.DisplayObject(obj);

                if (str.ToLower().Contains(filter.ToLower())) {
                    this.filteredRows!.Add(i);
                    break;
                }
            }
        }
    }

    private void ContentFilterScript(string script) {
        this.scriptError = null;

        // picked a random type for this, doesn't really matter
        var luminaTypes = Assembly.GetAssembly(typeof(Lumina.Excel.GeneratedSheets.Addon))?.GetTypes();
        var sheets = luminaTypes?
            .Where(t => t.GetCustomAttributes(typeof(SheetAttribute), false).Length > 0)
            .ToDictionary(t => ((SheetAttribute) t.GetCustomAttributes(typeof(SheetAttribute), false)[0]).Name);

        Type? sheetRow = null;
        if (sheets?.TryGetValue(this.selectedSheet!.Name, out var sheetType) == true) {
            sheetRow = sheetType;
        }

        // GameData.GetExcelSheet<T>();
        var getExcelSheet = typeof(GameData).GetMethod("GetExcelSheet", Type.EmptyTypes);
        var genericMethod = sheetRow is not null ? getExcelSheet?.MakeGenericMethod(sheetRow) : null;
        var sheetInstance = genericMethod?.Invoke(Services.GameData, Array.Empty<object>());

        var ct = new CancellationTokenSource();
        Task.Run(async () => {
            try {
                var globalsType = sheetRow != null
                                      ? typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow)
                                      : null;
                var expr = CSharpScript.Create<bool>(script, globalsType: globalsType);
                expr.Compile(ct.Token);

                for (var i = 0u; i < this.selectedSheet!.RowCount; i++) {
                    if (ct.IsCancellationRequested) {
                        Log.Debug("Filter script cancelled - aborting");
                        return;
                    }

                    var row = this.GetRow(this.selectedSheet, this.rowMapping, i);
                    if (row is null) continue;
                    var i1 = i;

                    async void SimpleEval() {
                        try {
                            var res = await expr.RunAsync(cancellationToken: ct.Token);
                            if (res.ReturnValue) this.filteredRows?.Add(i1);
                        } catch (Exception e) {
                            this.scriptError = e.Message;
                        }
                    }

                    if (sheetRow is null) {
                        SimpleEval();
                    } else {
                        object? instance;
                        if (row.SubRowId == 0) {
                            // sheet.GetRow(row.RowId);
                            var getRow = sheetInstance?.GetType().GetMethod("GetRow", new[] {typeof(uint)});
                            instance = getRow?.Invoke(sheetInstance, new object[] {row.RowId});
                        } else {
                            // sheet.GetRow(row.RowId, row.SubRowId);
                            var getRow = sheetInstance?.GetType()
                                .GetMethod("GetRow", new[] {typeof(uint), typeof(uint)});
                            instance = getRow?.Invoke(sheetInstance, new object[] {row.RowId, row.SubRowId});
                        }

                        // new ExcelScriptingGlobal<ExcelRow>(sheet, row);
                        var excelScriptingGlobal = typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow);
                        var globals = Activator.CreateInstance(excelScriptingGlobal, sheetInstance, instance);
                        if (globals is null) {
                            SimpleEval();
                        } else {
                            try {
                                var res = await expr.RunAsync(globals, ct.Token);
                                if (res.ReturnValue) this.filteredRows?.Add(i1);
                            } catch (Exception e) {
                                this.scriptError = e.Message;
                            }
                        }
                    }
                }
            } catch (Exception e) {
                Log.Error(e, "Filter script failed");
                this.scriptError = e.Message;
            }

            Log.Debug("Filter script finished");
            this.scriptToken = null;
        }, ct.Token);

        this.scriptToken = ct;
    }

    private void ResolveSidebarFilter() {
        Log.Debug("Resolving sidebar filter...");

        if (this.sidebarToken is not null && !this.sidebarToken.IsCancellationRequested) {
            this.sidebarToken.Cancel();
            this.sidebarToken.Dispose();
            this.sidebarToken = null;
        }

        if (string.IsNullOrEmpty(this.sidebarFilter)) {
            this.filteredSheets = null;
            return;
        }

        var filter = this.sidebarFilter.ToLower();
        if (this.fullTextSearch) {
            this.filteredSheets = new();

            var ct = new CancellationTokenSource();
            Task.Run(() => {
                foreach (var sheetName in this.module.Sheets) {
                    var sheet = this.module.GetSheet(sheetName, true);
                    if (sheet is null) continue;

                    if (this.filteredSheets.Contains(sheetName)) continue;

                    var rowMapping = this.SetupRows(sheet);
                    var colCount = sheet.ColumnCount;

                    var found = false;
                    foreach (var rowId in rowMapping.Keys) {
                        if (found) break;

                        var row = this.GetRow(sheet, rowMapping, rowId);
                        if (row is null) continue;

                        for (var col = 0; col < colCount; col++) {
                            if (ct.IsCancellationRequested) {
                                Log.Debug("Sidebar filter cancelled - aborting");
                                return;
                            }

                            var obj = row.ReadColumnRaw(col);
                            if (obj is null) continue;
                            var str = this.module.DisplayObject(obj);

                            if (str.ToLower().Contains(filter)) {
                                this.filteredSheets.Add(sheetName);
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }, ct.Token);
        } else {
            this.filteredSheets = this.module.Sheets
                .Where(x => x.ToLower().Contains(filter))
                .ToList();
        }
    }
}
