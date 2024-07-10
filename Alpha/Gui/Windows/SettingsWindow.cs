using System.Numerics;
using Alpha.Services;
using ImGuiNET;

namespace Alpha.Gui.Windows;

[Window("Settings", SingleInstance = true)]
public class SettingsWindow(GameDataService gameData, PathService path, Config config) : Window {
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
        Components.DrawGamePaths(gameData);
    }

    private void DrawPathListsTab() {
        Components.DrawPathLists(path);
    }

    private void DrawExcelTab() {
        var anyChanged = false;
        if (ImGui.Checkbox("Sort by offsets", ref config.SortByOffsets)) anyChanged = true;
        if (ImGui.Checkbox("Always show offsets", ref config.AlwaysShowOffsets)) anyChanged = true;
        if (ImGui.Checkbox("Highlight links", ref config.HighlightLinks)) anyChanged = true;
        if (ImGui.Checkbox("Prefer high quality images", ref config.PreferHighQuality)) anyChanged = true;
        if (ImGui.Checkbox("Keep images at line height", ref config.LineHeightImages)) anyChanged = true;
        if (anyChanged) config.Save();
    }
}
