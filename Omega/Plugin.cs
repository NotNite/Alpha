using System;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Omega.Windows;

namespace Omega;

public class Plugin : IDalamudPlugin {
    public string Name => "Omega";
    private const string CommandName = "/pomega";

    public static WindowSystem WindowSystem = null!;
    public readonly MainWindow MainWindow;

    private static Server _server = null!;

    public Plugin(DalamudPluginInterface pluginInterface) {
        pluginInterface.Create<Services>();

        WindowSystem = new("Omega");

        this.MainWindow = new();
        WindowSystem.AddWindow(this.MainWindow);

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand));
        Services.PluginInterface.UiBuilder.Draw += this.DrawUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi += this.OpenUi;

        _server = new();
    }

    public void Dispose() {
        _server.Dispose();

        WindowSystem.RemoveAllWindows();
        this.MainWindow.Dispose();

        Services.CommandManager.RemoveHandler(CommandName);
        Services.PluginInterface.UiBuilder.Draw -= this.DrawUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenUi;
    }

    private void DrawUi() {
        WindowSystem.Draw();
    }

    private void OpenUi() {
        this.MainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args) {
        this.OpenUi();
    }
}
