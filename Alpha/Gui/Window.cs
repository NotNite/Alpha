using System.Numerics;
using System.Reflection;
using Alpha.Services;
using ImGuiNET;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Alpha.Gui;

public abstract class Window {
    public ImGuiWindowFlags Flags = ImGuiWindowFlags.NoSavedSettings;
    public GuiService.GuiScene Scene = GuiService.GuiScene.Main;
    public bool IsOpen;
    public Vector2 MaxSize = new(float.MaxValue, float.MaxValue);
    public Vector2 MinSize = new(0, 0);
    public Vector2? InitialSize;
    public int Priority = 0;

    public readonly string Name;
    public int Id;

    protected Window() {
        this.Name = this.GetType().GetCustomAttribute<WindowAttribute>()?.Name ?? this.GetType().Name;
    }

    public virtual void PreDraw() { }
    public virtual void PostDraw() { }

    public void InternalDraw() {
        if (this.IsOpen) {
            try {
                ImGui.PushID(this.Id.ToString());

                try {
                    this.PreDraw();
                } catch (Exception e) {
                    Log.Error("Error in PreDraw for window {Name}: {e}", this.Name, e);
                }

                ImGui.SetNextWindowSizeConstraints(this.MinSize, this.MaxSize);
                ImGui.SetNextWindowSize(this.InitialSize ?? this.MinSize, ImGuiCond.FirstUseEver);
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
