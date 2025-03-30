using System.Numerics;
using System.Reflection;
using Alpha.Game;
using Alpha.Services;
using Hexa.NET.ImGui;
using Serilog;

namespace Alpha.Gui;

public abstract class Window {
    public ImGuiWindowFlags Flags = ImGuiWindowFlags.NoSavedSettings;
    public GuiService.GuiScene Scene = GuiService.GuiScene.Main;
    public bool IsOpen;
    public Vector2 MaxSize = new(float.MaxValue, float.MaxValue);
    public Vector2 MinSize = new(100, 100);
    public Vector2? InitialSize;
    public AlphaGameData? GameData;
    public int Priority = 0;
    public bool ShouldSetSize = true;

    public readonly string Name;
    public int Id;

    protected Window() {
        this.Name = this.GetType().GetCustomAttribute<WindowAttribute>()?.Name ?? this.GetType().Name;
    }

    public virtual bool ShouldDraw() => true;
    public virtual void PreDraw() { }
    public virtual void PostDraw() { }

    public void InternalDraw() {
        if (this.IsOpen && this.ShouldDraw()) {
            try {
                ImGui.PushID(this.Id.ToString());

                try {
                    this.PreDraw();
                } catch (Exception e) {
                    Log.Error(e, "Error in PreDraw for window {Name}", this.Name);
                }

                if (this.ShouldSetSize) {
                    ImGui.SetNextWindowSizeConstraints(this.MinSize, this.MaxSize);
                    ImGui.SetNextWindowSize(this.InitialSize ?? this.MinSize, ImGuiCond.Appearing);
                }

                if (ImGui.Begin(this.Name + "##" + this.Id, ref this.IsOpen, this.Flags)) {
                    try {
                        this.Draw();
                    } catch (Exception e) {
                        Log.Error("Error drawing window {Name}: {Exception}", this.Name, e);
                    }
                }

                ImGui.End();

                try {
                    this.PostDraw();
                } catch (Exception e) {
                    Log.Error("Error in PostDraw for window {Name}: {e}", this.Name, e);
                }
            } finally {
                ImGui.PopID();
            }
        }
    }

    protected abstract void Draw();
}
