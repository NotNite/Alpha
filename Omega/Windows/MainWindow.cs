using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace Omega.Windows;

public class MainWindow : Window, IDisposable {
    public MainWindow() : base("Omega") {
        this.SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(200, 200),
            MaximumSize = new Vector2(float.MaxValue)
        };
    }

    public override void Draw() {
        ImGui.Text("forgot to put something here");
    }

    public void Dispose() { }
}
