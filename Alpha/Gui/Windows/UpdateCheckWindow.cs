using System.Numerics;
using Alpha.Utils;
using Hexa.NET.ImGui;

namespace Alpha.Gui.Windows;

[Window("Update Check", SingleInstance = true, ShowInMenu = false)]
public class UpdateCheckWindow : Window {
    private readonly Config config;

    public UpdateCheckWindow(Config config) {
        this.IsOpen = true;
        this.config = config;
        this.ShouldSetSize = false;
        this.Flags |= ImGuiWindowFlags.NoDecoration
                      | ImGuiWindowFlags.NoNav
                      | ImGuiWindowFlags.NoBringToFrontOnFocus
                      | ImGuiWindowFlags.NoFocusOnAppearing;
    }

    public override bool ShouldDraw() => this.config.UpdateCheckVersion is not null;

    public override void PreDraw() {
        const float offset = 10;
        var ctx = ImGui.GetCurrentContext();
        var style = ImGui.GetStyle();
        var size = ctx.FontSize + (style.FramePadding.Y * 2) + (style.WindowPadding.Y * 2);
        ImGui.SetNextWindowSize(new Vector2(0, size));
        ImGui.SetNextWindowPos(new Vector2(offset, ImGui.GetIO().DisplaySize.Y - size - offset));
    }

    protected override void Draw() {
        var ctx = ImGui.GetCurrentContext();
        var style = ImGui.GetStyle();
        var sizeWithoutPadding = ctx.FontSize + (style.FramePadding.Y * 2);
        var size = sizeWithoutPadding + (style.WindowPadding.Y * 2);

        ImGui.SetCursorPosY((size / 2) - (ImGui.GetCurrentContext().FontSize / 2));
        ImGui.TextUnformatted(
            $"A new version of Alpha ({this.config.UpdateCheckVersion}) is available!");
        ImGui.SameLine();

        ImGui.SetCursorPosY(sizeWithoutPadding / 2);
        if (ImGui.Button("Download")) Util.OpenLink("https://github.com/NotNite/Alpha/releases/latest");
        ImGui.SameLine();

        ImGui.SetCursorPosY(sizeWithoutPadding / 2);
        if (ImGui.Button("Close")) {
            this.config.UpdateCheckVersion = null;
            this.config.Save();
        }
    }
}
