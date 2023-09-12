using System.Numerics;
using System.Text.Json;
using Alpha.Core;
using Alpha.Utils;
using Alpha.Windows;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Text;
using Serilog;

namespace Alpha.Modules.Excel;

public class ExcelModule : WindowedModule<ExcelWindow> {
    public readonly string[] Sheets;
    public readonly Dictionary<string, RawExcelSheet?> SheetsCache = new();
    public readonly Dictionary<string, SheetDefinition?> SheetDefinitions = new();

    private HttpClient _httpClient = new();

    public ExcelModule() : base("Excel Browser", "Data") {
        this.Sheets = Services.GameData.Excel.GetSheetNames().ToArray();
    }

    public RawExcelSheet? GetSheet(string name, bool skipCache = false) {
        if (skipCache) return Services.GameData.Excel.GetSheetRaw(name);
        if (this.SheetsCache.TryGetValue(name, out var sheet)) return sheet;

        sheet = Services.GameData.Excel.GetSheetRaw(name);
        this.SheetsCache[name] = sheet;

        if (!this.SheetDefinitions.ContainsKey(name)) {
            this.ResolveSheetDefinition(name);
        }

        return sheet;
    }

    public void ReloadAllSheets() {
        this.SheetsCache.Clear();
        foreach (var window in this.Windows) window.Reload();
    }

    public void OpenNewWindow(string? sheet = null, int? scrollTo = null) {
        var window = new ExcelWindow(this);
        window.Open = true;
        this.Windows.Add(window);

        if (sheet is not null) {
            window.OpenSheet(sheet, scrollTo);
        }
    }

    internal override void OnClick() {
        this.OpenNewWindow();
    }

    private void ResolveSheetDefinition(string name) {
        var url =
            $"https://raw.githubusercontent.com/xivapi/SaintCoinach/master/SaintCoinach/Definitions/{name}.json";

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
                        name,
                        sheetDefinition);

                    this.SheetDefinitions[name] = sheetDefinition;
                } else {
                    Log.Error("Request for sheet definition failed: {sheetName} -> {statusCode}",
                        name,
                        result.StatusCode);

                    this.SheetDefinitions[name] = null;
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to resolve sheet definition");
            }
        });
    }

    // Abstracted here so we can show previous of what links are
    internal void DrawEntry(
        ExcelWindow sourceWindow,
        RawExcelSheet sheet,
        int row,
        int col,
        object data,
        ConverterDefinition? converter
    ) {
        switch (converter) {
            // Was originally 'link when link.Target != null', Rider wants me to turn it into this monstrous thing
            case LinkConverterDefinition { Target: not null } link: {
                var targetRow = 0;
                try {
                    targetRow = Convert.ToInt32(data);
                } catch {
                    // ignored
                }

                this.DrawLink(sourceWindow, link.Target, targetRow, row, col);

                break;
            }

            case IconConverterDefinition: {
                var iconId = 0u;
                try {
                    iconId = Convert.ToUInt32(data);
                } catch {
                    // ignored
                }

                var icon = Services.ImageHandler.GetIcon(iconId);
                if (icon is not null) {
                    var path = icon.FilePath;
                    var handle = Services.ImageHandler.DisplayTex(icon);
                    if (handle == IntPtr.Zero) break;

                    var size = new Vector2(icon.Header.Width, icon.Header.Height);
                    if (size.Y > 512) size *= 512 / size.Y;
                    ImGui.Image(handle, size);

                    var shouldShowMagnum = ImGui.IsKeyDown(ImGui.GetKeyIndex(ImGuiKey.ModAlt)) && ImGui.IsItemHovered();
                    if (shouldShowMagnum) {
                        ImGui.BeginTooltip();
                        ImGui.Image(handle, size * 2);
                        ImGui.EndTooltip();
                    }

                    if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                        ImGui.MenuItem(path.Path, false);

                        if (ImGui.MenuItem("Copy icon ID")) {
                            ImGui.SetClipboardText(iconId.ToString());
                        }

                        if (ImGui.MenuItem("Copy icon path")) {
                            ImGui.SetClipboardText(path.Path);
                        }

                        if (ImGui.MenuItem("Open in filesystem browser")) {
                            Services.ModuleManager.GetModule<FilesystemModule>().OpenFile(path);
                        }

                        if (ImGui.MenuItem("Save (.tex)")) {
                            FileUtils.Save(icon.Data, "tex");
                        }

                        if (ImGui.MenuItem("Save (.png)")) {
                            UiUtils.ExportPng(icon);
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
                var thisRow = sheet.GetRow((uint)row);
                if (thisRow is null) break; // wtf?

                for (var i = 0; i < sheet.ColumnCount; i++) {
                    if (!this.SheetDefinitions.ContainsKey(sheet.Name)) continue;
                    var sheetDef = this.SheetDefinitions[sheet.Name];
                    if (sheetDef is null) continue;

                    var colName = sheetDef.GetNameForColumn(i);
                    var colValue = thisRow.ReadColumnRaw(i);
                    if (colName is null || colValue is null) continue;

                    keyValues[colName] = colValue;
                }

                var resolvedLinks = complex.ResolveComplexLink(keyValues);
                foreach (var link in resolvedLinks) {
                    this.DrawLink(sourceWindow, link, targetRow, row, col);
                }

                break;
            }

            default: {
                var str = data.ToString();
                if (data is SeString seString) {
                    str = UiUtils.DisplaySeString(seString);
                }

                if (str is null) break;

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

    private void DrawLink(
        ExcelWindow sourceWindow,
        string link,
        int targetRow,
        int row,
        int col
    ) {
        var text = $"{link}#{targetRow}" + $"##{row}_{col}";

        if (ImGui.Button(text)) {
            sourceWindow.OpenSheet(link, targetRow);
        }

        if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
            if (ImGui.MenuItem("Open in new window")) {
                this.OpenNewWindow(link, targetRow);
            }

            ImGui.EndPopup();
        }

        // Hack to preview a link
        var targetSheet = this.GetSheet(link);
        if (
            targetSheet is not null
            && this.SheetDefinitions.TryGetValue(link, out var sheetDef)
            && sheetDef is not null) {
            var targetRowObj = targetSheet.GetRow((uint)targetRow);
            var targetCol = sheetDef.DefaultColumn is not null
                ? sheetDef.GetColumnForName(sheetDef.DefaultColumn) ?? 0
                : 0;

            var data = targetRowObj?.ReadColumnRaw(targetCol);
            if (data is not null && ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                this.DrawEntry(
                    sourceWindow,
                    targetSheet,
                    targetRow,
                    targetCol,
                    data,
                    sheetDef.GetConverterForColumn(targetCol)
                );
                ImGui.EndTooltip();
            }
        }
    }

    public string DisplayObject(object obj) {
        if (obj is SeString seString) {
            return UiUtils.DisplaySeString(seString);
        }

        return obj.ToString() ?? "";
    }
}
