using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Alpha.Gui;
using Hexa.NET.ImGui;
using Lumina.Data.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Texture = Alpha.Gui.Texture;

namespace Alpha.Services;

public class GuiService(
    Config config,
    ILogger<GuiService> logger
) : IHostedService {
    private Task task = null!;
    private CancellationTokenSource cts = null!;
    private string iniPath = Path.Combine(Program.AppDir, "imgui.ini");

    private ImGuiWrapper imgui = null!;
    private GuiScene scene = GuiScene.Main;
    private Dictionary<string, Texture> textures = new();

    public event Action<GuiScene>? OnDraw;

    public Task StartAsync(CancellationToken cancellationToken) {
        if (!config.FtueComplete) this.scene = GuiScene.Ftue;

        this.cts = new CancellationTokenSource();
        this.task = Task.Run(this.Run, this.cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        this.cts.Cancel();
        return this.task;
    }

    private void Run() {
        this.imgui = new ImGuiWrapper(config, this.iniPath);

        var stopwatch = Stopwatch.StartNew();
        while (!this.imgui.Exiting && !this.cts.Token.IsCancellationRequested) {
            stopwatch.Restart();

            this.imgui.DoEvents();
            if (this.imgui.Exiting) break;

            lock (this.imgui) {
                this.CleanOldTextures();
                this.imgui.Render(() => {
                    try {
                        this.Draw();
                    } catch (Exception e) {
                        logger.LogError(e, "Error in draw loop");
                    }
                });
            }
        }

        config.WindowPos = this.imgui.WindowPos;
        config.WindowSize = this.imgui.WindowSize;
        config.Save();

        ImGui.SaveIniSettingsToDisk(this.iniPath);
        this.imgui.Dispose();
        _ = Task.Run(async () => await Program.Host.StopAsync());
    }

    public void SetScene(GuiScene newScene) {
        this.scene = newScene;
    }

    private void Draw() {
        try {
            this.OnDraw?.Invoke(this.scene);
        } catch (Exception e) {
            logger.LogError("Error in draw: {Error}", e);
        }
    }

    public Texture GetTexture(TexFile file) {
        if (this.textures.TryGetValue(file.FilePath, out var tex)) {
            tex.LastUsed = ImGui.GetTime();
            return tex;
        } else {
            var size = new Vector2(file.Header.Width, file.Header.Height);
            var handle = this.imgui.CreateTexture(file.ImageData, file.Header.Width, file.Header.Height);
            return this.textures[file.FilePath] = new Texture {
                Handle = handle,
                Size = size
            };
        }
    }

    private void CleanOldTextures() {
        const int seconds = 5;
        var cutoff = ImGui.GetTime() < seconds ? 0 : ImGui.GetTime() - seconds;
        foreach (var (key, value) in this.textures) {
            if (value.LastUsed < cutoff) {
                this.textures.Remove(key);
                if (value.Handle is not null) {
                    this.imgui.DestroyTexture(value.Handle.Value);
                    value.Handle = null;
                }
            }
        }
    }

    public enum GuiScene {
        Ftue,
        Main
    }
}
