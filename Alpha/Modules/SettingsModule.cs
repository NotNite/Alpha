using Alpha.Core;
using ImGuiNET;

namespace Alpha.Modules; 

public class SettingsModule : Module {
    public SettingsModule() : base("Settings") { }

    internal override void Draw() {
        var preferHr1 = Services.Configuration.PreferHr1;
        if (ImGui.Checkbox("Prefer high quality textures", ref preferHr1)) {
            Services.Configuration.PreferHr1 = preferHr1;
            Services.Configuration.Save();
        }
    }
}
