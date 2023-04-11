using System.Text.Json;
using Alpha.Core;
using Alpha.Utils;
using ImGuiNET;
using Lumina.Excel;
using Serilog;

namespace Alpha.Modules.Excel;

public class ExcelModule : Module {
    private float _sidebarWidth = 300f;
    private string _sidebarFilter = string.Empty;
    private string _contentFilter = string.Empty;

    private string[] _sheets;
    private RawExcelSheet? _selectedSheet;

    private HttpClient _httpClient = new();
    private readonly Dictionary<string, RawExcelSheet?> _sheetsCache = new();
    private readonly Dictionary<string, SheetDefinition?> _sheetDefinitions = new();

    private int? _tempScroll;
    private int _paintTicksLeft = -1;

    public ExcelModule() : base("Excel Browser", "Data") {
        this._sheets = Services.GameData.Excel.GetSheetNames().ToArray();
    }

    public RawExcelSheet? GetSheet(string name) {
        if (this._sheetsCache.TryGetValue(name, out var sheet)) return sheet;

        sheet = Services.GameData.Excel.GetSheetRaw(name);
        this._sheetsCache[name] = sheet;

        return sheet;
    }

    public void OpenSheet(string name, int? scrollTo = null) {
        if (scrollTo is not null) this._tempScroll = scrollTo;

        var sheet = this.GetSheet(name);
        if (sheet is null) {
            Log.Warning("Tried to open sheet that doesn't exist: {name}", name);
            return;
        }

        Log.Debug("Opening sheet: {name}", name);

        this._selectedSheet = sheet;
        this.ResolveSheetDefinition();
        this.WindowOpen = true;
    }

    private void ResolveSheetDefinition() {
        var sheetName = this._selectedSheet!.Name;
        if (this._sheetDefinitions.ContainsKey(sheetName)) return;

        var url =
            $"https://raw.githubusercontent.com/xivapi/SaintCoinach/master/SaintCoinach/Definitions/{sheetName}.json";

        this._httpClient.GetAsync(url).ContinueWith(t => {
            try {
                var result = t.Result;
                if (result.IsSuccessStatusCode) {
                    var json = result.Content.ReadAsStringAsync().Result;

                    var sheetDefinition = JsonSerializer.Deserialize<SheetDefinition>(json);
                    if (sheetDefinition is null) {
                        Log.Error("Failed to deserialize sheet definition");
                        return;
                    }

                    Log.Debug("Resolved sheet definition: {sheetName} -> {sheetDefinition}",
                        sheetName,
                        sheetDefinition);

                    this._sheetDefinitions[sheetName] = sheetDefinition;
                } else {
                    Log.Error("Request for sheet definition failed: {sheetName} -> {statusCode}",
                        sheetName,
                        result.StatusCode);

                    this._sheetDefinitions[sheetName] = null;
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to resolve sheet definition");
            }
        });
    }


    // TODO deduplicate this code from fs module
    internal override void Draw() {
        var temp = ImGui.GetCursorPosY();
        ImGui.SetNextItemWidth(this._sidebarWidth);
        ImGui.InputText("##ExcelFilter", ref this._sidebarFilter, 1024);

        var cra = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##ExcelModule_Sidebar", cra with { X = this._sidebarWidth }, true);

        foreach (var sheet in this._sheets) {
            if (!string.IsNullOrEmpty(this._sidebarFilter) &&
                !sheet.Contains(this._sidebarFilter, StringComparison.OrdinalIgnoreCase)) continue;

            if (ImGui.Selectable(sheet, sheet == this._selectedSheet?.Name)) {
                this.OpenSheet(sheet);
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        UiUtils.HorizontalSplitter(ref this._sidebarWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        if (this._selectedSheet != null) this.DrawTable();
    }

    private void DrawTable() {
        if (!this._sheetDefinitions.TryGetValue(this._selectedSheet!.Name, out var sheetDefinition)) return;

        var rowCount = this._selectedSheet.RowCount;
        var colCount = this._selectedSheet.ColumnCount;

        var flags = ImGuiTableFlags.Borders
                    | ImGuiTableFlags.NoSavedSettings
                    | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.Resizable
                    | ImGuiTableFlags.ScrollX
                    | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##ExcelTable", (int)(colCount + 1), flags)) {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);

        ImGui.TableSetupColumn("Row");
        for (var i = 0; i < colCount; i++) {
            var colName = sheetDefinition?.GetNameForColumn(i) ?? i.ToString();
            ImGui.TableSetupColumn(colName);
        }

        ImGui.TableHeadersRow();

        var clipper = new ListClipper((int)rowCount);
        foreach (var i in clipper.Rows) {
            var row = this._selectedSheet.GetRow((uint)i);
            if (row is null) continue;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var str = row.RowId.ToString();
            if (row.SubRowId != 0) str += $".{row.SubRowId}";
            ImGui.TextUnformatted(str);
            ImGui.TableNextColumn();

            for (var col = 0; col < colCount; col++) {
                var obj = row.ReadColumnRaw(col);
                if (obj != null) {
                    var converter = sheetDefinition?.GetConverterForColumn(col);

                    this.DrawEntry((int)row.RowId, col, obj, converter);
                }

                if (col < colCount - 1) ImGui.TableNextColumn();
            }
        }

        // I don't know why I need to do this but I really don't care, it's 12 AM and I want sleep
        // seems to crash if you scroll immediately, seems to do nothing if you scroll too little
        // stupid tick hack works for now lol
        if (this._tempScroll is not null & this._paintTicksLeft == -1) {
            this._paintTicksLeft = 5;
        } else if (this._paintTicksLeft <= 0) {
            this._tempScroll = null;
            this._paintTicksLeft = -1;
        } else if (this._tempScroll is not null) {
            ImGui.SetScrollY(this._tempScroll.Value * clipper.ItemsHeight);
            this._paintTicksLeft--;
        }

        clipper.End();
        ImGui.EndTable();
    }

    private void DrawEntry(int row, int col, object data, ConverterDefinition? converter) {
        // The base converter has nothing to draw
        if (converter is null || converter.GetType() == typeof(ConverterDefinition)) {
            var str = data.ToString();
            ImGui.TextUnformatted(str);

            if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                if (ImGui.MenuItem("Copy")) {
                    ImGui.SetClipboardText(str);
                }

                ImGui.EndPopup();
            }

            return;
        }

        // Else, use the converter
        try {
            converter.Draw(row, col, data);
        } catch {
            // Catch & toss exceptions because casting fucking sucks
        }
    }
}
