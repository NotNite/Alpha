using System.Numerics;
using System.Text.Json;
using Alpha.Core;
using Alpha.Utils;
using Alpha.Windows;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
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
        ConverterDefinition? converter,
        bool insideLink = false
    ) {
        switch (converter) {
            // Was originally 'link when link.Target != null', Rider wants me to turn it into this monstrous thing
            case LinkConverterDefinition {Target: not null} link: {
                var targetRow = data is uint @uint ? @uint : 0;

                if (insideLink && ImGui.IsKeyDown(ImGuiKey.ModAlt)) {
                    // Draw what the link points to
                    var targetSheet = this.GetSheet(link.Target);
                    var targetRowObj = targetSheet?.GetRow(targetRow);
                    var sheetDef = this.SheetDefinitions.TryGetValue(link.Target, out var definition)
                                       ? definition
                                       : null;

                    if (sheetDef is not null) {
                        var targetCol = sheetDef.DefaultColumn is not null
                                            ? sheetDef.GetColumnForName(sheetDef.DefaultColumn) ?? 0
                                            : 0;
                        var targetData = targetRowObj?.ReadColumnRaw(targetCol);

                        if (targetData is not null) {
                            this.DrawEntry(
                                sourceWindow,
                                targetSheet!,
                                (int) targetRow,
                                targetCol,
                                targetData,
                                sheetDef.GetConverterForColumn(targetCol),
                                true
                            );
                            return;
                        }
                    }
                }

                this.DrawLink(sourceWindow, link.Target, (int) targetRow, row, col);
                break;
            }

            case IconConverterDefinition: {
                var iconId = data is uint @uint ? @uint : 0;
                var icon = Services.ImageHandler.GetIcon(iconId);

                if (icon is not null) {
                    var path = icon.FilePath;
                    var handle = Services.ImageHandler.DisplayTex(icon);
                    if (handle == IntPtr.Zero) break;

                    Vector2 ScaleSize(float maxY) {
                        var size = new Vector2(icon!.Header.Width, icon.Header.Height);
                        if (size.Y > maxY) size *= maxY / size.Y;
                        return size;
                    }

                    var lineSize = ScaleSize(Services.Configuration.LineHeightImages
                                                 ? ImGui.GetTextLineHeight() * 2
                                                 : 512);
                    ImGui.Image(handle, lineSize);

                    var shouldShowMagnum = ImGui.IsKeyDown(ImGui.GetKeyIndex(ImGuiKey.ModAlt)) && ImGui.IsItemHovered();
                    if (shouldShowMagnum) {
                        var magnumSize = ScaleSize(1024);
                        ImGui.BeginTooltip();
                        ImGui.Image(handle, magnumSize);
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

            case TomestoneConverterDefinition: {
                // FIXME this allocates memory like a motherfucker, cache this
                var dataInt = data is uint @uint ? @uint : 0;
                var tomestone = dataInt > 0
                                    ? Services.GameData.GetExcelSheet<TomestonesItem>()!
                                        .FirstOrDefault(x => x.Tomestones.Row == dataInt)
                                    : null;

                if (tomestone is null) {
                    this.DrawLink(
                        sourceWindow,
                        "Item",
                        (int) dataInt,
                        row,
                        col
                    );
                } else {
                    this.DrawLink(
                        sourceWindow,
                        "Item",
                        (int) tomestone.Item.Row,
                        row,
                        col
                    );
                }

                break;
            }

            case ComplexLinkConverterDefinition complex: {
                var targetRow = data is uint @uint ? @uint : 0;
                var resolvedLinks = complex.ResolveComplexLink(
                    this,
                    sheet,
                    row,
                    (int) targetRow
                );

                foreach (var link in resolvedLinks) {
                    this.DrawLink(sourceWindow, link.Link, link.TargetRow, row, col);
                }

                break;
            }

            default: {
                string? str;

                try {
                    str = data.ToString();
                    if (data is SeString seString) {
                        str = UiUtils.DisplaySeString(seString);
                    }
                } catch {
                    // Some sheets (like CustomTalkDefineClient) have broken SeString, so let's catch that
                    break;
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
            var targetRowObj = targetSheet.GetRow((uint) targetRow);
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
                    sheetDef.GetConverterForColumn(targetCol),
                    true
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
