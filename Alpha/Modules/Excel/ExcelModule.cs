using System.Numerics;
using System.Text.Json;
using Alpha.Core;
using Alpha.Utils;
using ImGuiNET;
using Lumina.Data.Files;
using Lumina.Excel;
using Lumina.Text;
using Serilog;

namespace Alpha.Modules.Excel;

public class ExcelModule : Module {
    private float _sidebarWidth = 300f;
    private string _sidebarFilter = string.Empty;
    private string _contentFilter = string.Empty;
    private List<uint>? _filteredRows;

    private string[] _sheets;
    private RawExcelSheet? _selectedSheet;

    private HttpClient _httpClient = new();
    private readonly Dictionary<string, RawExcelSheet?> _sheetsCache = new();
    private readonly Dictionary<string, SheetDefinition?> _sheetDefinitions = new();

    private int? _tempScroll;
    private int _paintTicksLeft = -1;

    public ExcelModule() : base("Excel Browser", "Data") {
        this._sheets = Services.GameData.Excel.GetSheetNames().ToArray();

        this.OpenSheet("Item");
    }

    internal override void PreDraw() {
        ImGui.SetNextWindowSize(new Vector2(960, 540), ImGuiCond.FirstUseEver);
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

        ImGui.BeginGroup();
        if (this._selectedSheet != null) {
            var width = ImGui.GetContentRegionAvail().X;

            ImGui.SetNextItemWidth(width);
            if (ImGui.InputText("##ExcelContentFilter", ref this._contentFilter, 1024)) {
                this.ResolveFilter();
            }

            ImGui.SetNextItemWidth(width);
            this.DrawTable();
        }

        ImGui.EndGroup();
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

        var actualRowCount = this._filteredRows?.Count ?? (int)rowCount;
        var clipper = new ListClipper(actualRowCount);
        foreach (var i in clipper.Rows) {
            var actualIndex = this._filteredRows?[i] ?? (uint)i;
            var row = this._selectedSheet.GetRow(actualIndex);
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
        // Spam try/catch here because we don't want to throw an error mid ImGui frame
        // No idea why conversions fail sometimes

        switch (converter) {
            case LinkConverterDefinition link when link.Target != null: {
                var targetRow = 0;
                try {
                    targetRow = Convert.ToInt32(data);
                } catch {
                    // ignored
                }

                var text = $"{link.Target}#{targetRow}" + $"##{row}_{col}";

                if (ImGui.Button(text)) {
                    Services.ModuleManager.GetModule<ExcelModule>().OpenSheet(link.Target, targetRow);
                }

                break;
            }

            case IconConverterDefinition: {
                var iconId = 0u;
                try {
                    iconId = Convert.ToUInt32(data);
                } catch {
                    // ignored
                }

                var icon = UiUtils.GetIcon(iconId);
                if (icon is not null) {
                    var path = icon.FilePath;
                    var handle = UiUtils.DisplayTex(icon);
                    var size = new Vector2(icon.Header.Width, icon.Header.Height);
                    ImGui.Image(handle, size);

                    var shouldShowMagnum = ImGui.IsKeyDown(ImGui.GetKeyIndex(ImGuiKey.ModAlt)) && ImGui.IsItemHovered();
                    if (shouldShowMagnum) {
                        ImGui.BeginTooltip();
                        ImGui.Image(handle, size * 2);
                        ImGui.EndTooltip();
                    }

                    if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                        ImGui.MenuItem(path, false);

                        if (ImGui.MenuItem("Copy icon ID")) {
                            ImGui.SetClipboardText(iconId.ToString());
                        }

                        if (ImGui.MenuItem("Copy icon path")) {
                            ImGui.SetClipboardText(path);
                        }

                        if (ImGui.MenuItem("Open in filesystem browser")) {
                            Services.ModuleManager.GetModule<FilesystemModule>().OpenFile(path);
                        }

                        if (ImGui.MenuItem("Save")) {
                            FileUtils.Save(icon.Data, "tex");
                        }

                        ImGui.EndPopup();
                    }
                } else {
                    ImGui.BeginDisabled();
                    ImGui.TextUnformatted($"(couldn't load icon {iconId})");
                    ImGui.EndDisabled();
                }

                break;
            }

            case ComplexLinkConverterDefinition complex: {
                var targetRow = 0;
                try {
                    targetRow = Convert.ToInt32(data);
                } catch {
                    // ignored
                }

                var keyValues = new Dictionary<string, object>();
                // We need to be being parsed *from* a sheet definition, so these !s are safe
                var thisRow = this._selectedSheet!.GetRow((uint)row)!;

                for (var i = 0; i < this._selectedSheet!.ColumnCount; i++) {
                    var colName = this._sheetDefinitions[this._selectedSheet.Name]!.GetNameForColumn(i);
                    var colValue = thisRow.ReadColumnRaw(i);
                    if (colName is null || colValue is null) continue;
                    keyValues[colName] = colValue;
                }

                var resolvedLinks = complex.ResolveComplexLink(keyValues);
                foreach (var link in resolvedLinks) {
                    var text = $"{link}#{targetRow}" + $"##{row}_{col}";

                    if (ImGui.Button(text)) {
                        Services.ModuleManager.GetModule<ExcelModule>().OpenSheet(link, targetRow);
                    }
                }

                break;
            }

            default: {
                var str = data.ToString();
                if (data is SeString seString) {
                    str = UiUtils.DisplaySeString(seString);
                }

                ImGui.TextUnformatted(str);

                if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                    var fileExists = false;
                    try {
                        fileExists = Services.GameData.FileExists(str);
                    } catch {
                        // ignored
                    }

                    if (fileExists && ImGui.MenuItem("Open in filesystem browser")) {
                        Services.ModuleManager.GetModule<FilesystemModule>().OpenFile(str);
                    }

                    if (ImGui.MenuItem("Copy")) {
                        ImGui.SetClipboardText(str);
                    }

                    ImGui.EndPopup();
                }

                break;
            }
        }
    }

    private void ResolveFilter() {
        if (string.IsNullOrEmpty(this._contentFilter)) {
            this._filteredRows = null;
            return;
        }

        if (this._selectedSheet is null) {
            this._filteredRows = null;
            return;
        }

        this._filteredRows = new();

        var colCount = this._selectedSheet.ColumnCount;
        foreach (var row in this._selectedSheet) {
            var shouldAdd = false;
            for (var col = 0; col < colCount; col++) {
                var obj = row.ReadColumnRaw(col);
                if (obj is null) continue;
                var str = obj.ToString();
                if (str is null) continue;

                if (str.ToLower().Contains(this._contentFilter.ToLower())) {
                    shouldAdd = true;
                    break;
                }
            }

            if (shouldAdd) {
                this._filteredRows.Add(row.RowId);
            }
        }
    }
}
