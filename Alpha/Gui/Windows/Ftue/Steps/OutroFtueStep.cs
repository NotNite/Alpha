using Alpha.Utils;
using ImGuiNET;

namespace Alpha.Gui.Windows.Ftue.Steps;

public class OutroFtueStep : FtueStep {
    public override bool IsLocked => false;

    public override void Draw() {
        Components.DrawAbout();
    }
}
