using Alpha.Core;
using Alpha.Modules.Excel;
using ImGuiNET;
using Lumina.Data;

namespace Alpha.Modules;

public class SettingsModule : SimpleModule {
    public SettingsModule() : base("Settings") { }

    internal override void SimpleDraw() {
        var preferHr1 = Services.Configuration.PreferHr1;
        if (ImGui.Checkbox("Prefer high quality textures", ref preferHr1)) {
            Services.Configuration.PreferHr1 = preferHr1;
            Services.Configuration.Save();
        }

        var lineHeightImages = Services.Configuration.LineHeightImages;
        if (ImGui.Checkbox("Lock images to line height in Excel", ref lineHeightImages)) {
            Services.Configuration.LineHeightImages = lineHeightImages;
            Services.Configuration.Save();
        }

        var alwaysShowOffsets = Services.Configuration.AlwaysShowOffsets;
        if (ImGui.Checkbox("Always show offsets in Excel", ref alwaysShowOffsets)) {
            Services.Configuration.AlwaysShowOffsets = alwaysShowOffsets;
            Services.Configuration.Save();
        }

        var sortByOffsets = Services.Configuration.SortByOffsets;
        if (ImGui.Checkbox("Sort by offsets in Excel", ref sortByOffsets)) {
            Services.Configuration.SortByOffsets = sortByOffsets;
            Services.Configuration.Save();
        }

        var highlightLinks = Services.Configuration.HighlightLinks;
        if (ImGui.Checkbox("Highlight links in Excel", ref highlightLinks)) {
            Services.Configuration.HighlightLinks = highlightLinks;
            Services.Configuration.Save();
        }

        var languages = new[] {
            Language.English,
            Language.Japanese,
            Language.German,
            Language.French,
            Language.Korean,
            Language.ChineseSimplified,
            Language.ChineseTraditional
        };
        var languagesStr = new[] {
            "English",
            "Japanese",
            "German",
            "French",
            "Korean",
            "Chinese (Simplified)",
            "Chinese (Traditional)"
        };

        var language = Array.IndexOf(languages, Services.Configuration.ExcelLanguage);
        if (ImGui.Combo("Excel language", ref language, languagesStr, languagesStr.Length)) {
            Services.Configuration.ExcelLanguage = languages[language];
            Services.Configuration.Save();
            Services.GameData.Options.DefaultExcelLanguage = Services.Configuration.ExcelLanguage;
            Services.ModuleManager.GetModule<ExcelModule>().ReloadAllSheets();
        }

        var fpsLimit = Services.Configuration.FpsLimit;
        if (ImGui.InputFloat("Framerate limit", ref fpsLimit)) {
            Services.Configuration.FpsLimit = MathF.Max(20.0f, fpsLimit);
            Program.FpsLimit = MathF.Max(20.0f, fpsLimit);
            Services.Configuration.Save();
        }

        var drawDebug = Services.Configuration.DrawDebug;
        if (ImGui.Checkbox("Draw debug info", ref drawDebug)) {
            Services.Configuration.DrawDebug = drawDebug;
            Services.Configuration.Save();
        }

        var scale = Services.Configuration.DisplayScale;
        if (ImGui.InputFloat("Display scale", ref scale)) {
            Services.Configuration.DisplayScale = MathF.Max(0.5f, scale);
            Services.Configuration.Save();
        }

        var hexEditorPath = Services.Configuration.HexEditorPath;
        if (ImGui.InputText("Hex editor path", ref hexEditorPath, 1024)) {
            Services.Configuration.HexEditorPath = hexEditorPath;
            Services.Configuration.Save();
        }
    }
}
