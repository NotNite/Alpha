using Alpha.Services;
using ImGuiNET;

namespace Alpha.Gui.Windows.Ftue.Steps;

public class GameFtueStep(Config config, GameDataService gameData) : FtueStep {
    public override bool IsLocked => config.GamePaths.Count == 0;

    public override void Draw() {
        ImGui.TextWrapped(
            "Alpha can switch between multiple FFXIV game installs. You will need at least one selected to continue.");

        Components.DrawGamePaths(gameData);
    }
}
