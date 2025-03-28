using System.Numerics;
using Alpha.Services;
using Alpha.Services.Excel;
using Hexa.NET.ImGui;
using Lumina.Data;

namespace Alpha.Gui.Windows;

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
        // ReSharper disable once ReplaceWithSingleAssignment.False
        var anyChanged = false;
        // ReSharper disable once ConvertIfToOrExpression
        if (ImGui.Checkbox("Sort by offsets", ref this.config.SortByOffsets)) anyChanged = true;
        if (ImGui.Checkbox("Always show offsets", ref this.config.AlwaysShowOffsets)) anyChanged = true;
        if (ImGui.Checkbox("Highlight links", ref this.config.HighlightLinks)) anyChanged = true;
        if (ImGui.Checkbox("Keep images at line height", ref this.config.LineHeightImages)) anyChanged = true;

        {
            Language[] languages = [Language.English, Language.Japanese, Language.German, Language.French];
            if (Components.DrawEnumCombo("Default language (requires restart)",
                    ref this.config.DefaultLanguage, languages)) {
                anyChanged = true;
            }
        }

        {
            SchemaProvider[] providers = [SchemaProvider.None, SchemaProvider.ExdSchema, SchemaProvider.SaintCoinach];
            string[] providerNames =
                ["None", "EXDSchema (github.com/xivdev/EXDSchema)", "SaintCoinach (github.com/xivapi/SaintCoinach)"];
            if (Components.DrawEnumCombo("Schema provider (requires restart)", ref this.config.SchemaProvider,
                    providers, providerNames)) {
                anyChanged = true;
            }
        }

        if (anyChanged) this.config.Save();
    }
}
