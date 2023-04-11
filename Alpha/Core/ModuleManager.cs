using System.Reflection;
using ImGuiNET;
using Serilog;

namespace Alpha.Core;

public class ModuleManager {
    private readonly List<Module> _modules = new();

    public void InitializeModules() {
        var modules = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Module)))
            .ToArray();

        // I will write my own dependency resolver instead of using DI and you will slowly weep
        var dependencyGraph = new Dictionary<Type, List<Type>>();
        foreach (var type in modules) {
            var attr = type.GetCustomAttribute<ModuleAttribute>() ?? new ModuleAttribute();
            var dependencies = new List<Type>();

            foreach (var dependency in attr.DependsOn) {
                var dependencyType = modules.First(x => x.Name == dependency);
                dependencies.Add(dependencyType);
            }

            dependencyGraph.Add(type, dependencies);
        }

        var resolvedOrder = new List<Type>();
        while (dependencyGraph.Count > 0) {
            var noDependencies = dependencyGraph.Where(x => x.Value.Count == 0).ToList();
            foreach (var (type, _) in noDependencies) {
                resolvedOrder.Add(type);
                dependencyGraph.Remove(type);
            }

            foreach (var (_, dependencies) in dependencyGraph) {
                foreach (var noDependency in noDependencies) {
                    dependencies.Remove(noDependency.Key);
                }
            }
        }

        foreach (var type in resolvedOrder) {
            Log.Information("Initializing module: {0}", type.Name);
            var module = (Module)Activator.CreateInstance(type)!;
            this._modules.Add(module);
        }

        Log.Information("Initialized {0} modules.", this._modules.Count);
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
