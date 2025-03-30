using System.Numerics;
using Alpha.Services;
using Alpha.Services.Excel;
using Hexa.NET.ImGui;
using Lumina.Data;
using NativeFileDialog.Extended;

namespace Alpha.Gui.Windows;

// ReSharper disable ReplaceWithSingleAssignment.False
// ReSharper disable ConvertIfToOrExpression
[Window("Settings", SingleInstance = true)]
public class SettingsWindow : Window {
    private readonly GameDataService gameData;
    private readonly PathListService pathList;
    private readonly Config config;

    public SettingsWindow(GameDataService gameData, PathListService pathList, Config config) {
        this.gameData = gameData;
        this.pathList = pathList;
        this.config = config;

        this.InitialSize = new Vector2(800, 600);
    }

    protected override void Draw() {
        if (ImGui.BeginTabBar("##SettingsTabBar")) {
            this.DrawTab("Game Paths", this.DrawGamePathsTab);
            this.DrawTab("Path Lists", this.DrawPathListsTab);
            this.DrawTab("Excel", this.DrawExcelTab);
            this.DrawTab("UI", this.DrawUiTab);
            this.DrawTab("Misc", this.DrawMiscTab);

            ImGui.EndTabBar();
        }
    }

    private void DrawTab(string name, Action draw) {
        if (ImGui.BeginTabItem(name)) {
            try {
                draw();
            } catch (Exception e) {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), e.Message);
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawGamePathsTab() {
        Components.DrawGamePaths(this.gameData);
    }

    private void DrawPathListsTab() {
        Components.DrawPathLists(this.pathList);
    }

    private void DrawExcelTab() {
        var anyChanged = false;

        if (ImGui.Checkbox("Sort by offsets", ref this.config.SortByOffsets)) anyChanged = true;
        if (ImGui.Checkbox("Always show offsets", ref this.config.AlwaysShowOffsets)) anyChanged = true;
        if (ImGui.Checkbox("Highlight links", ref this.config.HighlightLinks)) anyChanged = true;
        if (ImGui.Checkbox("Keep images at line height", ref this.config.LineHeightImages)) anyChanged = true;
        if (ImGui.Checkbox("Display row IDs as hexadecimal", ref this.config.RowIdAsHex)) anyChanged = true;

        {
            Language[] languages = [Language.English, Language.Japanese, Language.German, Language.French];
            if (Components.DrawEnumCombo("Default language (requires restart)",
                    ref this.config.DefaultLanguage, languages)) {
                anyChanged = true;
            }

            Components.DrawHelpTooltip("You can override the language for a specific game install at any time.");
        }

        {
            SchemaProvider[] providers = [SchemaProvider.None, SchemaProvider.ExdSchema, SchemaProvider.SaintCoinach];
            string[] providerNames =
                ["None", "EXDSchema (github.com/xivdev/EXDSchema)", "SaintCoinach (github.com/xivapi/SaintCoinach)"];
            if (Components.DrawEnumCombo("Schema provider (requires restart)", ref this.config.SchemaProvider,
                    providers, providerNames)) {
                anyChanged = true;
            }

            Components.DrawHelpTooltip(
                "Relations between sheets are maintained by the community. Sheet relations are downloaded automatically when using Alpha.");
        }

        if (anyChanged) this.config.Save();
    }

    private void DrawUiTab() {
        ImGui.TextWrapped("All settings on this page require restarting Alpha.");

        var anyChanged = false;

        if (ImGui.Checkbox("Enable docking", ref this.config.EnableDocking)) {
            anyChanged = true;
        }
        Components.DrawHelpTooltip(
            "Docking allows you to snap windows onto one another. If you find the overlays distracting, turn this off.");

        if (ImGui.CollapsingHeader("Colors")) {
            if (Components.DrawEnumCombo("UI theme",
                    ref this.config.Theme,
                    Enum.GetValues<UiTheme>())) {
                anyChanged = true;
            }

            var useCustomBackground = this.config.BackgroundColor is not null;
            if (ImGui.Checkbox("Use custom background color", ref useCustomBackground)) {
                this.config.BackgroundColor = useCustomBackground ? Vector3.Zero : null;
                anyChanged = true;
            }

            if (this.config.BackgroundColor is { } bg) {
                if (ImGui.ColorEdit3("Background color", ref bg)) {
                    this.config.BackgroundColor = bg;
                }
                if (ImGui.IsItemDeactivatedAfterEdit()) anyChanged = true;
            }
        }

        if (ImGui.CollapsingHeader("Fonts")) {
            ImGui.TextWrapped(
                "If you don't like the default ImGui font, you can add custom ones here. Supply a path to a .ttf file.");

            if (ImGui.Button("Pick from file")) {
                var file = NFD.OpenDialog(string.Empty);
                if (!string.IsNullOrWhiteSpace(file)) {
                    this.config.ExtraFonts.Add(new FontConfig(file));
                    anyChanged = true;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Add entry")) this.config.ExtraFonts.Add(new FontConfig());

            foreach (var (idx, font) in this.config.ExtraFonts.ToList().Index()) {
                ImGui.Separator();
                ImGui.PushID(idx);

                ImGui.InputText("Font Path", ref font.Path, 512);
                if (ImGui.IsItemDeactivatedAfterEdit()) anyChanged = true;

                ImGui.InputInt("Font Size", ref font.Size);
                if (ImGui.IsItemDeactivatedAfterEdit()) {
                    if (font.Size < 8) font.Size = 8;
                    if (font.Size > 48) font.Size = 48;
                    anyChanged = true;
                }

                if (ImGui.Checkbox("Use Japanese Glyphs", ref font.JapaneseGlyphs)) {
                    anyChanged = true;
                }
                Components.DrawHelpTooltip(
                    "Check this to have the font apply to Japanese glyphs instead of only the Latin glyphs.");

                if (ImGui.Button("Remove")) {
                    this.config.ExtraFonts.RemoveAt(idx);
                }

                ImGui.PopID();
            }
        }

        if (anyChanged) this.config.Save();
    }

    private void DrawMiscTab() {
        var anyChanged = false;

        if (ImGui.Checkbox("Check for updates", ref this.config.DoUpdateChecking)) {
            anyChanged = true;
        }
        Components.DrawHelpTooltip(
            "Every day, Alpha will reach out to the GitHub API to check for version updates.");

        if (anyChanged) this.config.Save();
    }
}
