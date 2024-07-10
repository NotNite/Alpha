using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Veldrid;

namespace Alpha.Gui;

public class Texture(nint realHandle, Vector2 size) : IDisposable {
    public double LastUsed = ImGui.GetTime();
    public nint Handle => this.Use();
    public Vector2 Size => size;

    public TextureView? View;
    public ResourceSet? Set;
    public nint? Global;

    public Texture(nint realHandle, TextureView view, ResourceSet set, nint global, Vector2 size) : this(realHandle, size) {
        this.View = view;
        this.Set = set;
        this.Global = global;
    }

    private nint Use() {
        this.LastUsed = ImGui.GetTime();
        return realHandle;
    }

    public void Dispose() {
        this.View?.Dispose();
        this.Set?.Dispose();
        if (this.Global != null) Marshal.FreeHGlobal(this.Global.Value);
    }
}
