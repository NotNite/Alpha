using System.Numerics;
using ImGuiNET;

namespace Alpha.Core;

public class Window : IDisposable {
    public string Name = string.Empty;
    public bool Open;

    public ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.None;
    public Vector2? InitialSize = null;

    internal void InternalPreDraw() {
        if (this.InitialSize != null) {
            ImGui.SetNextWindowSize(this.InitialSize.Value, ImGuiCond.FirstUseEver);
        }

        this.PreDraw();
    }

    internal void InternalDraw(int i) {
        var name = this.Name + "##" + i;
        if (ImGui.Begin(name, ref this.Open, this.WindowFlags)) this.Draw();
        ImGui.End();
    }

    internal void InternalPostDraw() {
        this.PostDraw();
    }

    protected virtual void PreDraw() { }
    protected virtual void Draw() { }
    protected virtual void PostDraw() { }

    public virtual void Dispose() { }
}
