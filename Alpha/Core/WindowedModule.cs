using ImGuiNET;

namespace Alpha.Core;

public abstract class WindowedModule<T> : Module where T : Window {
    protected readonly List<T> Windows = new();

    public WindowedModule(string name, string? category = null) : base(name, category) { }

    internal override bool IsEnabled() {
        return this.Windows.Any(x => x.Open);
    }

    internal override void Draw() {
        this.Windows.RemoveAll(x => !x.Open);

        for (var i = 0; i < this.Windows.Count; i++) {
            var window = this.Windows[i];
            ImGui.PushID(i);
            window.InternalPreDraw();
            window.InternalDraw(i);
            window.InternalPostDraw();
            ImGui.PopID();
        }
    }
}
