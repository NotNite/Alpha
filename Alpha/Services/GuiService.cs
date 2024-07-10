using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Lumina.Data.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Texture = Alpha.Gui.Texture;

namespace Alpha.Services;

public class GuiService(
    Config config,
    ILogger<GuiService> logger
) : IHostedService {
    private static RgbaFloat BackgroundColor = new(0.1f, 0.1f, 0.1f, 1f);

    private Task task = null!;
    private CancellationTokenSource cts = null!;
    private string iniPath = Path.Combine(Program.AppDir, "imgui.ini");

    private Sdl2Window window = null!;
    private GraphicsDevice gd = null!;
    private ImGuiRenderer imgui = null!;
    private CommandList cl = null!;
    private ResourceLayout textureLayout = null!;

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
        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(config.WindowX, config.WindowY, config.WindowWidth, config.WindowHeight,
                WindowState.Normal, "Alpha"),
            out this.window,
            out this.gd
        );

        this.textureLayout = this.gd.ResourceFactory.CreateResourceLayout(
            new ResourceLayoutDescription(new ResourceLayoutElementDescription(
                "MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment
            )));

        this.imgui = new ImGuiRenderer(
            this.gd,
            this.gd.MainSwapchain.Framebuffer.OutputDescription,
            this.window.Width, this.window.Height
        );
        var io = ImGui.GetIO();
        unsafe {
            io.NativePtr->IniFilename = null;
            ImGui.LoadIniSettingsFromDisk(this.iniPath);
        }
        this.cl = this.gd.ResourceFactory.CreateCommandList();

        this.gd.SyncToVerticalBlank = true;

        this.window.Resized += this.Resized;
        this.window.Moved += this.Moved;

        var stopwatch = Stopwatch.StartNew();
        while (!this.cts.Token.IsCancellationRequested && this.window.Exists) {
            var deltaTime = stopwatch.ElapsedTicks / (float) Stopwatch.Frequency;
            stopwatch.Restart();

            var snapshot = this.window.PumpEvents();
            if (!this.window.Exists) break;

            this.CleanOldTextures();
            this.imgui.Update(deltaTime, snapshot);
            try {
                this.Draw();
            } catch (Exception e) {
                logger.LogError("Error in draw loop: {Exception}", e);
            }

            this.cl.Begin();
            this.cl.SetFramebuffer(this.gd.MainSwapchain.Framebuffer);
            this.cl.ClearColorTarget(0, BackgroundColor);

            this.imgui.Render(this.gd, this.cl);
            this.cl.End();
            this.gd.SubmitCommands(this.cl);
            this.gd.SwapBuffers(this.gd.MainSwapchain);
        }

        this.gd.WaitForIdle();

        this.window.Resized -= this.Resized;
        this.window.Moved -= this.Moved;

        ImGui.SaveIniSettingsToDisk(this.iniPath);

        this.cl.Dispose();
        this.imgui.Dispose();
        this.textureLayout.Dispose();
        this.gd.Dispose();
        this.window.Close();

        Program.Host.StopAsync();
    }

    private void Moved(Point point) {
        config.WindowX = point.X;
        config.WindowY = point.Y;
        config.Save();
    }

    private void Resized() {
        this.gd.MainSwapchain.Resize((uint) this.window.Width, (uint) this.window.Height);
        this.imgui.WindowResized(this.window.Width, this.window.Height);
        config.WindowWidth = this.window.Width;
        config.WindowHeight = this.window.Height;
        config.Save();
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
            return this.textures[file.FilePath] = this.CreateTextureFromRgba(
                       file.ImageData,
                       file.Header.Width,
                       file.Header.Height
                   );
        }
    }

    private void CleanOldTextures() {
        const int seconds = 5;
        var cutoff = ImGui.GetTime() < seconds ? 0 : ImGui.GetTime() - seconds;
        foreach (var (key, value) in this.textures) {
            if (value.LastUsed < cutoff) {
                this.textures.Remove(key);
                this.imgui.RemoveImGuiBinding(value.View!);
                value.Dispose();
            }
        }
    }

    private Texture CreateTextureFromRgba(Span<byte> data, uint width, uint height) {
        var texture = this.gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            width,
            height,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled
        ));

        var global = Marshal.AllocHGlobal(data.Length);
        for (var i = 0; i < data.Length; i += 4) {
            var b = data[i];
            var g = data[i + 1];
            var r = data[i + 2];
            var a = data[i + 3];

            Marshal.WriteByte(global, i, r);
            Marshal.WriteByte(global, i + 1, g);
            Marshal.WriteByte(global, i + 2, b);
            Marshal.WriteByte(global, i + 3, a);
        }

        this.gd.UpdateTexture(
            texture,
            global,
            4 * width * height,
            0,
            0,
            0,
            width,
            height,
            1,
            0,
            0
        );

        var textureView = this.gd.ResourceFactory.CreateTextureView(texture);
        var resourceSet = this.gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
            this.textureLayout,
            textureView
        ));

        var binding = this.imgui.GetOrCreateImGuiBinding(this.gd.ResourceFactory, textureView);
        return new Texture(binding, textureView, resourceSet, global, new Vector2(width, height));
    }

    public enum GuiScene {
        Ftue,
        Main
    }
}
