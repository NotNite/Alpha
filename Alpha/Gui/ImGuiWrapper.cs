using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Utilities;
using Hexa.NET.SDL3;
using HexaGen.Runtime;
using Serilog;
using Backend = Hexa.NET.ImGui.Backends.SDL3;

namespace Alpha.Gui;

public unsafe class ImGuiWrapper : IDisposable {
    public bool Exiting;

    private readonly SDLWindow* window;
    private readonly uint windowId;
    private readonly SDLRenderer* renderer;
    private readonly ImGuiContext* imguiContext;
    private readonly Vector3 backgroundColor;

    private Backend.SDLWindowPtr backendWindow => new((Backend.SDLWindow*) this.window);
    private Backend.SDLRendererPtr backendRenderer => new((Backend.SDLRenderer*) this.renderer);

    public Vector2 WindowPos {
        get {
            int x;
            int y;
            SDL.GetWindowPosition(this.window, &x, &y);
            return new Vector2(x, y);
        }
    }

    public Vector2 WindowSize {
        get {
            int w;
            int h;
            SDL.GetWindowSize(this.window, &w, &h);
            return new Vector2(w, h);
        }
    }

    public ImGuiWrapper(Config config, string iniPath) {
        SDL.Init(SDLInitFlags.Events | SDLInitFlags.Video);

        var scale = SDL.GetDisplayContentScale(SDL.GetPrimaryDisplay());
        this.window = SDL.CreateWindow(
            "Alpha",
            (int) config.WindowSize.X, (int) config.WindowSize.Y,
            SDLWindowFlags.HighPixelDensity | SDLWindowFlags.Resizable
        );
        this.windowId = SDL.GetWindowID(this.window);
        if (config.WindowPos is { } pos) SDL.SetWindowPosition(this.window, (int) pos.X, (int) pos.Y);

        this.renderer = SDL.CreateRenderer(this.window, (byte*) null);
        SDL.SetRenderVSync(this.renderer, 1);

        SDL.ShowWindow(this.window);

        var context = ImGui.CreateContext();
        Backend.ImGuiImplSDL3.SetCurrentContext(context);

        // Apply user themes
        switch (config.Theme) {
            case UiTheme.Light: {
                ImGui.StyleColorsLight();
                this.backgroundColor = new Vector3(0.85f, 0.85f, 0.85f);
                break;
            }

            case UiTheme.Dark:
            default: {
                ImGui.StyleColorsDark();
                this.backgroundColor = new Vector3(0.15f, 0.15f, 0.15f);
                break;
            }
        }

        if (config.BackgroundColor is { } bg) this.backgroundColor = bg;

#pragma warning disable CS0618 // Type or member is obsolete
        var builder = new ImGuiFontBuilder();
#pragma warning restore CS0618 // Type or member is obsolete
        builder.Config.Flags |= (ImFontFlags) ImGuiFreeTypeLoaderFlags.LoadColor;

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        if (config.EnableDocking) io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.IniFilename = (byte*) Marshal.StringToHGlobalAnsi(iniPath + "\0");

        var loadedFirstFont = false;

        // ReSharper disable once MoveLocalFunctionAfterJumpStatement
        void SetMergeMode() {
            // MergeMode can't be set until the first font is loaded
            if (!loadedFirstFont) {
                builder.Config.MergeMode = true;
                loadedFirstFont = true;
            }
        }

        // Load user UI fonts first
        foreach (var font in config.ExtraFonts.Where(font => !font.FallbackOnly)) {
            if (File.Exists(font.Path)) {
                builder.AddFontFromFileTTF(font.Path, font.Size);
                SetMergeMode();
            }
        }

        // Apply user fonts
        builder.AddDefaultFont();
        SetMergeMode();

        // Load user fallback fonts next
        foreach (var font in config.ExtraFonts.Where(font => font.FallbackOnly)) {
            if (File.Exists(font.Path)) {
                builder.AddFontFromFileTTF(font.Path, font.Size);
                SetMergeMode();
            }
        }

        // In case the user doesn't provide a font with Japanese glyphs, let's add one for them
        if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
            var hasJpFont = config.ExtraFonts.Any(x => x.FallbackOnly);
            const string cjkFont = "C:/Windows/Fonts/msgothic.ttc";
            if (!hasJpFont && File.Exists(cjkFont)) {
                builder.AddFontFromFileTTF(cjkFont, 13f);
            }
        }

        builder.Build();

        var style = ImGui.GetStyle();
        style.ScaleAllSizes(scale);

        Backend.ImGuiImplSDL3.InitForSDLRenderer(this.backendWindow, this.backendRenderer);
        Backend.ImGuiImplSDL3.SDLRenderer3Init(this.backendRenderer);
    }

    public void DoEvents() {
        SDLEvent @event = default;
        SDL.PumpEvents();

        while (SDL.PollEvent(ref @event)) {
            Backend.ImGuiImplSDL3.ProcessEvent((Backend.SDLEvent*) &@event);

            var type = (SDLEventType) @event.Type;
            switch (type) {
                case SDLEventType.Quit or SDLEventType.Terminating: {
                    this.Exiting = true;
                    break;
                }

                case SDLEventType.WindowCloseRequested: {
                    var windowEvent = @event.Window;
                    if (windowEvent.WindowID == this.windowId) this.Exiting = true;
                    break;
                }
            }
        }
    }


    public void Render(Action draw) {
        Backend.ImGuiImplSDL3.SDLRenderer3NewFrame();
        Backend.ImGuiImplSDL3.NewFrame();
        ImGui.NewFrame();

        try {
            draw();
        } catch (Exception e) {
            Log.Error(e, "Error drawing ImGui");
        }

        ImGui.Render();

        var io = ImGui.GetIO();
        SDL.SetRenderScale(this.renderer, io.DisplayFramebufferScale.X, io.DisplayFramebufferScale.Y);
        SDL.SetRenderDrawColorFloat(this.renderer,
            this.backgroundColor.X,
            this.backgroundColor.Y,
            this.backgroundColor.Z,
            255
        );

        SDL.RenderClear(this.renderer);
        Backend.ImGuiImplSDL3.SDLRenderer3RenderDrawData(ImGui.GetDrawData(), this.backendRenderer);
        SDL.RenderPresent(this.renderer);
        ImGui.EndFrame();
    }

    public void Dispose() {
        Backend.ImGuiImplSDL3.SDLRenderer3Shutdown();
        Backend.ImGuiImplSDL3.Shutdown();
        ImGui.DestroyContext();

        SDL.DestroyRenderer(this.renderer);
        SDL.DestroyWindow(this.window);
        SDL.Quit();
    }

    public ImTextureRef CreateTexture(byte[] data, int width, int height) {
        fixed (byte* ptr = data) {
            var surface = SDL.CreateSurfaceFrom(
                width,
                height,
                SDLPixelFormat.Bgra32,
                ptr,
                width * 4
            );
            var texture = SDL.CreateTextureFromSurface(this.renderer, surface);
            SDL.DestroySurface(surface);
            return new ImTextureRef(null, texture);
        }
    }

    public void DestroyTexture(ImTextureRef texture) {
        SDL.DestroyTexture((SDLTexture*) texture.TexID);
    }
}
