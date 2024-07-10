using Alpha.Services;
using ImGuiNET;

namespace Alpha.Gui.Windows.Ftue.Steps;

public class AssetsFtueStep(PathService path) : FtueStep {
    public override bool IsLocked => false;

    public override void Draw() {
        ImGui.TextWrapped(
            "Alpha makes use of several networked services that provide information about the game client, including Excel schemas and path lists.");
        ImGui.TextWrapped("You can download these assets manually, or Alpha can get them for you.");

        Components.DrawPathLists(path);
        // TODO: Excel schemas
    }
}
