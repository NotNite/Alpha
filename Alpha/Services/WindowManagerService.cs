using System.Numerics;
using System.Reflection;
using Alpha.Gui;
using Alpha.Gui.Windows;
using Alpha.Gui.Windows.Ftue;
using Hexa.NET.ImGui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Alpha.Services;

public class WindowManagerService(
    GuiService gui,
    Config config,
    IServiceScopeFactory scopeFactory,
    ILogger<WindowManagerService> logger
) : IHostedService {
    private List<(Window Window, IServiceScope Scope)> windows = new();
    private Dictionary<string, Type> windowTypes = new();
    private bool ready;
    private Dictionary<Type, int> windowIds = new();

    public Task StartAsync(CancellationToken cancellationToken) {
        var windows = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Window)))
            .Where(t => t.GetCustomAttribute<WindowAttribute>() is not null);
        foreach (var window in windows) this.RegisterWindow(window);

        gui.OnDraw += this.Draw;

        if (!config.FtueComplete) this.CreateWindow<FtueWindow>();
        this.ready = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        gui.OnDraw -= this.Draw;
        this.windows.Clear();
        return Task.CompletedTask;
    }

    private void Draw(GuiService.GuiScene scene) {
        if (!this.ready) {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            if (ImGui.Begin("Loading", ImGuiWindowFlags.NoDecoration)) {
                const string str = "Loading Alpha... Hang tight!";
                var textSize = ImGui.CalcTextSize(str);
                var windowSize = ImGui.GetWindowSize();
                var pos = ImGui.GetWindowPos();

                ImGui.SetCursorPos(
                    new Vector2(
                        pos.X + ((windowSize.X - textSize.X) / 2),
                        pos.Y + ((windowSize.Y - textSize.Y) / 2)
                    )
                );
                ImGui.TextUnformatted(str);

                ImGui.End();
            }
        }
        // Sort consistently because of service loading shenanigans; priority, then name
        var sortedWindows = this.windows
            .Select(x => x.Window)
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.Name)
            .ToList();

        if (scene is GuiService.GuiScene.Main) {
            if (ImGui.BeginMainMenuBar()) {
                if (ImGui.BeginMenu("Alpha")) {
                    foreach (var windowType in this.windowTypes.Values) {
                        var attr = windowType.GetCustomAttribute<WindowAttribute>()!;
                        if (!attr.ShowInMenu) continue;

                        if (ImGui.MenuItem(attr.Name, string.Empty, false)) {
                            if (attr.SingleInstance) {
                                var instanceWindow =
                                    this.windows
                                        .Select(x => x.Window)
                                        .FirstOrDefault(w => w.GetType() == windowType);
                                if (instanceWindow is not null) {
                                    instanceWindow.IsOpen ^= true;
                                } else {
                                    var window = this.CreateWindow(windowType);
                                    window.IsOpen = true;
                                }
                            } else {
                                var window = this.CreateWindow(windowType);
                                window.IsOpen = true;
                            }
                        }
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMainMenuBar();
            }
        }

        ImGui.DockSpaceOverViewport();
        foreach (var window in sortedWindows.Where(w => w.Scene == scene)) {
            window.InternalDraw();
        }

        // Dispose closed windows that aren't single instance
        foreach (var (window, scope) in this.windows.Where(w =>
                         !w.Window.IsOpen && !w.Window.GetType().GetCustomAttribute<WindowAttribute>()!.SingleInstance)
                     .ToList()) {
            logger.LogInformation("Disposing window {Name}", window.Name);
            this.windows.Remove((window, scope));
            scope.Dispose();
            GC.Collect();
        }
    }

    public void RegisterWindow<T>() where T : Window {
        var name = typeof(T).GetCustomAttribute<WindowAttribute>()?.Name ?? typeof(T).Name;
        this.windowTypes.Add(name, typeof(T));
    }

    public void RegisterWindow(Type type) {
        var name = type.GetCustomAttribute<WindowAttribute>()?.Name ?? type.Name;
        this.windowTypes.Add(name, type);
    }

    public T CreateWindow<T>() where T : Window {
        var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<T>();
        this.SetId(window);
        this.windows.Add((window, scope));
        window.IsOpen = true;
        return window;
    }

    public Window CreateWindow(Type type) {
        var scope = scopeFactory.CreateScope();
        var window = (Window) scope.ServiceProvider.GetRequiredService(type);
        this.SetId(window);
        this.windows.Add((window, scope));
        return window;
    }

    private void SetId(Window window) {
        if (this.windowIds.TryGetValue(window.GetType(), out var id)) {
            this.windowIds[window.GetType()]++;
            window.Id = id + 1;
        } else {
            this.windowIds.Add(window.GetType(), 1);
            window.Id = 1;
        }
    }

    public void DeleteWindow(Type type) {
        var (window, scope) = this.windows.First(w => w.Item1.GetType() == type);
        this.windows.Remove((window, scope));
        scope.Dispose();
    }
}
