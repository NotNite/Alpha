using System.Numerics;
using Hexa.NET.ImGui;

namespace Alpha.Gui;

public class Texture {
    public double LastUsed = ImGui.GetTime();
    public nint? Handle;
    public Vector2 Size = Vector2.Zero;

    public (byte[], uint, uint)? CreationData;

    public void Draw(Vector2? drawSize = null) {
        this.LastUsed = ImGui.GetTime();
        var actualSize = drawSize ?? this.Size;
        if (this.Handle is null) {
            ImGui.Dummy(actualSize);
        } else {
            ImGui.Image((ulong) this.Handle, actualSize);
        }
    }
}
