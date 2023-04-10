using System.Diagnostics;
using System.Reflection;
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

    private static Sdl2Window _window = null!;
    private static GraphicsDevice _graphicsDevice = null!;
    private static ImGuiHandler _imGuiHandler = null!;

    private static readonly Logger Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .MinimumLevel.Debug()
        .CreateLogger();

    private static ProgramState _state = ProgramState.Main;

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

        Services.Initialize();

        if (Services.Configuration.GamePath is null) {
            _state = ProgramState.Setup;
        }

        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "Alpha"),
            out _window,
            out _graphicsDevice
        );

        if (_window is null || _graphicsDevice is null) {
            Logger.Error("Failed to initialize Veldrid");
            return;
        }

        // ReSharper disable once AccessToDisposedClosure
        _window.Resized += () => { _graphicsDevice.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height); };

        var commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        _imGuiHandler = new ImGuiHandler(_window, _graphicsDevice);

        var stopwatch = Stopwatch.StartNew();
        while (_window.Exists) {
            var deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
            if (deltaTime < 1f / 60f) continue; // shitty FPS limiter
            stopwatch.Restart();

            var snapshot = _window.PumpEvents();
            if (!_window.Exists) break;

            _imGuiHandler.Update(deltaTime, snapshot);
            Draw();

            commandList.Begin();
            commandList.SetFramebuffer(_graphicsDevice.MainSwapchain.Framebuffer);
            var forty = 40f / 255f;
            commandList.ClearColorTarget(0, new RgbaFloat(forty, forty, forty, 1f));

            _imGuiHandler.Render(commandList);

            commandList.End();
            _graphicsDevice.SubmitCommands(commandList);
            _graphicsDevice.SwapBuffers(_graphicsDevice.MainSwapchain);
        }

        _graphicsDevice.WaitForIdle();
        _imGuiHandler.Dispose();
        _graphicsDevice.Dispose();
        _window.Close();

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
                // open a file dialog
                var folder = Dialog.FolderPicker();
                if (folder?.Path is not null) {
                    Services.Configuration.GamePath = folder.Path;
                    Services.Configuration.Save();
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

                if (ImGui.MenuItem("Exit")) {
                    _window.Close();
                }

                ImGui.Separator();

                ImGui.MenuItem($"Alpha {Version}", false);
                ImGui.MenuItem($"FPS: {ImGui.GetIO().Framerate:0.00}", false);
                ImGui.MenuItem($"GC: {GC.GetTotalMemory(false) / 1024 / 1024} MB", false);

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Modules")) {
                var modules = Services.ModuleManager.GetModules();
                var categories = modules.Select(m => m.Category).Distinct().ToList();

                foreach (var category in categories) {
                    if (ImGui.BeginMenu(category)) {
                        foreach (var module in modules.Where(m => m.Category == category)) {
                            ImGui.MenuItem(module.Name, null, ref module.WindowOpen);
                        }

                        ImGui.EndMenu();
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }
}
