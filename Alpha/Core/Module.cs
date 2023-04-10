using ImGuiNET;

namespace Alpha.Core;

public class Module {
    public readonly string Name;
    public readonly string Category;

    public bool WindowOpen;
    public ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.None;

    protected Module(string name, string category) {
        this.Name = name;
        this.Category = category;
    }

    internal virtual void PreDraw() { }
    internal virtual void Draw() { }
    internal virtual void PostDraw() { }
}
