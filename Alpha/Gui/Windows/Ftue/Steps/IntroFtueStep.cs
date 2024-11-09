using Hexa.NET.ImGui;

namespace Alpha.Gui.Windows.Ftue.Steps;

public class IntroFtueStep : FtueStep {
    public override bool IsLocked => false;

    public override void Draw() {
        ImGui.TextWrapped("Welcome to Alpha! Before we begin, let's get a few things set up.");
        ImGui.TextWrapped("Please have a game install of FINAL FANTASY XIV: A Realm Reborn ready.");
    }
}
