using ImGuiNET;

namespace Alpha.Core;

public abstract class SimpleModule : Module {
    private bool _enabled;

    protected SimpleModule(string name, string? category = null) : base(name, category) { }

    internal override bool IsEnabled() => this._enabled;

    internal override void OnClick() {
        this._enabled = !this._enabled;
    }

    internal virtual void SimpleDraw() { }

    internal override void Draw() {
        if (this._enabled) {
            var temp = true;

            if (ImGui.Begin(this.Name, ref temp)) this.SimpleDraw();
            ImGui.End();

            if (!temp) this._enabled = false;
        }
    }
}
