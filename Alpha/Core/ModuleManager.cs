using System.Reflection;
using ImGuiNET;
using Serilog;

namespace Alpha.Core;

public class ModuleManager {
    private readonly List<Module> _modules = new();

    public ModuleManager() {
        this.InitializeModules();
    }

    private void InitializeModules() {
        var reflectedModules = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Module)))
            .Select(t => (Module)Activator.CreateInstance(t)!);

        foreach (var module in reflectedModules) {
            Log.Debug("Initializing module: {0}", module.GetType().Name);
            this._modules.Add(module);
        }
    }

    public T GetModule<T>() where T : Module {
        foreach (var module in this._modules) {
            if (module is T m) return m;
        }

        Log.Warning("Module not found?: {0}", typeof(T).Name);
        return null!;
    }

    public List<Module> GetModules() {
        return this._modules;
    }

    public void Draw() {
        foreach (var module in this._modules) {
            var typeName = module.GetType().Name;

            if (module.WindowOpen) {
                try {
                    module.PreDraw();

                    ImGui.PushID("Alpha_Module_" + typeName);
                    if (ImGui.Begin(module.Name, ref module.WindowOpen, module.WindowFlags)) module.Draw();
                    ImGui.End();
                    ImGui.PopID();

                    module.PostDraw();
                } catch (Exception e) {
                    Log.Error(e, "Failed to draw {typeName}", typeName);
                }
            }
        }
    }
}
