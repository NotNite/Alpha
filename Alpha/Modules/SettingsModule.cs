using Alpha.Core;
using Alpha.Modules.Excel;
using ImGuiNET;
using Lumina.Data;

namespace Alpha.Modules;

public class SettingsModule : Module {
    public SettingsModule() : base("Settings") { }

    internal override void Draw() {
        var preferHr1 = Services.Configuration.PreferHr1;
        if (ImGui.Checkbox("Prefer high quality textures", ref preferHr1)) {
            Services.Configuration.PreferHr1 = preferHr1;
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
    }
}
