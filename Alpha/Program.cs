using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Alpha.Modules;
using ImGuiNET;
using NativeFileDialogSharp;
using Serilog;
using Serilog.Core;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Alpha;

public class Program {
    public static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Alpha");

    public static readonly Version Version = Assembly.GetExecutingAssembly().GetName().Version!;

    public static Sdl2Window Window = null!;
    public static GraphicsDevice GraphicsDevice = null!;
    public static ImGuiHandler ImGuiHandler = null!;

    private static readonly Logger Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .MinimumLevel.Debug()
        .CreateLogger();

    private static ProgramState _state = ProgramState.Main;

    public static float FpsLimit = 60.0f;

    private enum ProgramState {
        Setup,
        Main
    }

    public static void Main() {
        Log.Logger = Logger;

        Log.Information("This is Alpha {version} - starting up...", Version);

        if (!Directory.Exists(DataDirectory)) {
            Directory.CreateDirectory(DataDirectory);
        }

        Services.InitPreSetup();
        if (!GamePathIsValid(Services.Configuration.GamePath)) {
            _state = ProgramState.Setup;
        } else {
            Services.InitPostSetup();
        }

        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(
                Services.Configuration.WindowX,
                Services.Configuration.WindowY,
                Services.Configuration.WindowWidth,
                Services.Configuration.WindowHeight,
                WindowState.Normal, "Alpha"),
            out Window,
            out GraphicsDevice
        );

        if (Window is null || GraphicsDevice is null) {
            Logger.Error("Failed to initialize Veldrid");
            return;
        }

        // ReSharper disable once AccessToDisposedClosure
        Window.Resized += () => { GraphicsDevice.MainSwapchain.Resize((uint) Window.Width, (uint) Window.Height); };

        var commandList = GraphicsDevice.ResourceFactory.CreateCommandList();
        ImGuiHandler = new ImGuiHandler(Window, GraphicsDevice);

        FpsLimit = Services.Configuration.FpsLimit;

        var stopwatch = Stopwatch.StartNew();
        var imageStopwatch = Stopwatch.StartNew();

        while (Window.Exists) {
            var deltaTime = stopwatch.ElapsedTicks / (float) Stopwatch.Frequency;
            if (deltaTime < 1f / FpsLimit) continue; // shitty FPS limiter
            stopwatch.Restart();

            var snapshot = Window.PumpEvents();
            if (!Window.Exists) break;

            ImGuiHandler.Update(deltaTime, snapshot);
            Draw();

            commandList.Begin();
            commandList.SetFramebuffer(GraphicsDevice.MainSwapchain.Framebuffer);
            var forty = 40f / 255f;
            commandList.ClearColorTarget(0, new RgbaFloat(forty, forty, forty, 1f));

            ImGuiHandler.Render(commandList);
            commandList.End();
            GraphicsDevice.SubmitCommands(commandList);
            GraphicsDevice.SwapBuffers(GraphicsDevice.MainSwapchain);

            if (imageStopwatch.ElapsedMilliseconds > 10000) {
                imageStopwatch.Restart();
                Services.ImageHandler.DisposeAllTextures();
            }
        }

        GraphicsDevice.WaitForIdle();
        ImGuiHandler.Dispose();
        GraphicsDevice.Dispose();
        Window.Close();

        Services.Configuration.WindowWidth = Window.Width;
        Services.Configuration.WindowHeight = Window.Height;
        Services.Configuration.WindowX = Window.X;
        Services.Configuration.WindowY = Window.Y;
        Services.Configuration.Save();

        Log.Information("Bye! :3");
    }

    private static void Draw() {
        switch (_state) {
            case ProgramState.Setup:
                DrawSetup();
                break;

            case ProgramState.Main:
                DrawMain();
                break;
        }

        if (Services.Configuration.DrawDebug) {
            var flags = ImGuiWindowFlags.NoCollapse
                        | ImGuiWindowFlags.NoResize
                        | ImGuiWindowFlags.NoTitleBar
                        | ImGuiWindowFlags.NoMove
                        | ImGuiWindowFlags.NoScrollbar
                        | ImGuiWindowFlags.NoInputs;

            var size = new Vector2(100, 50);
            var pos = ImGui.GetMainViewport().Size - size - new Vector2(10, 10);

            ImGui.SetNextWindowPos(pos);
            ImGui.SetNextWindowSize(size);

            if (ImGui.Begin("##AlphaDebug", flags)) {
                ImGui.Text($"FPS: {ImGui.GetIO().Framerate:0.00}");
                ImGui.Text($"GC: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
                ImGui.End();
            }
        }
    }

    private static void DrawSetup() {
        var flags = ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoResize;

        var windowSize = ImGui.GetMainViewport().Size / 2;
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos + ImGui.GetMainViewport().Size / 2 - windowSize / 2);
        ImGui.SetNextWindowSize(windowSize);

        if (ImGui.Begin("Alpha Setup", flags)) {
            ImGui.TextWrapped("Welcome to Alpha!");
            ImGui.TextWrapped(
                "Before starting, please select your game installation. It should contain the 'game' and 'boot' folders.");

            var cra = ImGui.GetContentRegionMax();

            ImGui.SetCursorPosY(cra.Y - 20);
            if (ImGui.Button("Select game path")) {
                var folder = Dialog.FolderPicker();
                if (GamePathIsValid(folder?.Path)) {
                    Services.Configuration.GamePath = folder!.Path;
                    Services.Configuration.Save();
                    Services.InitPostSetup();
                    _state = ProgramState.Main;
                }
            }
        }

        ImGui.End();
    }


    private static void DrawMain() {
        Services.ModuleManager.Draw();

        if (Services.Configuration.DrawImGuiDemo) {
            ImGui.ShowDemoWindow();
        }

        if (ImGui.BeginMainMenuBar()) {
            if (ImGui.BeginMenu("Alpha")) {
                var drawImGuiDemo = Services.Configuration.DrawImGuiDemo;
                if (ImGui.MenuItem("Draw ImGui demo", null, ref drawImGuiDemo)) {
                    Services.Configuration.DrawImGuiDemo = drawImGuiDemo;
                    Services.Configuration.Save();
                }

                var settings = Services.ModuleManager.GetModule<SettingsModule>();
                var isEnabled = settings.IsEnabled();
                if (ImGui.MenuItem("Open settings", null, ref isEnabled)) {
                    settings.OnClick();
                }

                if (ImGui.MenuItem("Exit")) {
                    Window.Close();
                }

                ImGui.Separator();

                ImGui.MenuItem($"Alpha {Version}", false);
                ImGui.MenuItem($"FPS: {ImGui.GetIO().Framerate:0.00}", false);
                ImGui.MenuItem($"GC: {GC.GetTotalMemory(false) / 1024 / 1024} MB", false);

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Modules")) {
                var modules = Services.ModuleManager.GetModules();
                var categories = modules
                    .Select(m => m.Category)
                    .Where(c => c is not null)
                    .Select(c => c!)
                    .Distinct().ToList();

                foreach (var category in categories) {
                    if (ImGui.BeginMenu(category)) {
                        foreach (var module in modules.Where(m => m.Category == category)) {
                            var enabled = module.IsEnabled();
                            if (ImGui.MenuItem(module.Name, null, ref enabled)) {
                                module.OnClick();
                            }
                        }

                        ImGui.EndMenu();
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }

    private static bool GamePathIsValid(string? path) {
        if (path is null) return false;
        if (!Directory.Exists(path)) return false;

        var dirs = Directory.GetDirectories(path);
        return dirs.Any(f => f.EndsWith("game"));
    }
}
