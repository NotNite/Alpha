using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL2;
using Hexa.NET.ImGui.Utilities;
using Hexa.NET.OpenGL;
using HexaGen.Runtime;
using Silk.NET.SDL;

namespace Alpha.Gui;

public unsafe class ImGuiWrapper : IDisposable {
    private readonly Sdl sdl;
    private readonly Silk.NET.SDL.Window* window;
    private readonly uint windowId;
    private readonly NativeContext context;
    private readonly GL gl;
    private readonly ImGuiContext* imguiContext;
    private readonly Vector3 backgroundColor;

    public bool Exiting;

    public Vector2 WindowPos {
        get {
            int x;
            int y;
            this.sdl.GetWindowPosition(this.window, &x, &y);
            return new Vector2(x, y);
        }
    }
    public Vector2 WindowSize {
        get {
            int w;
            int h;
            this.sdl.GetWindowSize(this.window, &w, &h);
            return new Vector2(w, h);
        }
    }

    public ImGuiWrapper(Config config, string iniPath) {
        this.sdl = Sdl.GetApi();
        this.sdl.Init(Sdl.InitEvents + Sdl.InitVideo);
        const WindowFlags flags = WindowFlags.Opengl
                                  | WindowFlags.Resizable
                                  | WindowFlags.AllowHighdpi;

        this.window = this.sdl.CreateWindow(
            "Alpha",
            (int) config.WindowPos.X, (int) config.WindowPos.Y,
            (int) config.WindowSize.X, (int) config.WindowSize.Y,
            (uint) flags
        );
        this.windowId = this.sdl.GetWindowID(this.window);

        this.context = new NativeContext(this.sdl, this.window);
        this.gl = new GL(this.context);

        this.imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(this.imguiContext);
        ImGuiImplSDL2.SetCurrentContext(this.imguiContext);

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

        var builder = new ImGuiFontBuilder();
        builder.Config.FontBuilderFlags |= (uint) ImGuiFreeTypeBuilderFlags.LoadColor;

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        if (config.EnableDocking) io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.IniFilename = (byte*) Marshal.StringToHGlobalAnsi(iniPath + "\0");

        // Specify some fonts to load with the Japanese ranges, some without
        var defaultRanges = io.Fonts.GetGlyphRangesDefault();
        var japaneseRanges = io.Fonts.GetGlyphRangesJapanese();
        var loadedFirstFont = false;

        // ReSharper disable once MoveLocalFunctionAfterJumpStatement
        void SetMergeMode() {
            // MergeMode can't be set until the first font is loaded
            if (!loadedFirstFont) {
                builder.Config.MergeMode = true;
                loadedFirstFont = true;
            }
        }

        // Apply user fonts
        foreach (var font in config.ExtraFonts) {
            if (File.Exists(font.Path)) {
                builder.AddFontFromFileTTF(font.Path, font.Size, font.JapaneseGlyphs ? japaneseRanges : defaultRanges);
                SetMergeMode();
            }
        }

        // Fallback fonts
        builder.AddDefaultFont();
        SetMergeMode();

        // In case the user doesn't provide a font with Japanese glyphs, let's add one for them
        if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
            var hasJpFont = config.ExtraFonts.Any(x => x.JapaneseGlyphs);
            const string cjkFont = "C:/Windows/Fonts/msgothic.ttc";
            if (!hasJpFont && File.Exists(cjkFont)) {
                builder.AddFontFromFileTTF(cjkFont, 13f, japaneseRanges);
            }
        }

        builder.Build();

        ImGuiImplSDL2.InitForOpenGL((SDLWindow*) this.window, (void*) this.context.Handle);
        ImGuiImplGLFW.SetCurrentContext(ImGui.GetCurrentContext());

        ImGuiImplOpenGL3.SetCurrentContext(ImGui.GetCurrentContext());
        ImGuiImplOpenGL3.Init((string) null!);

        ImGuiImplOpenGL3.NewFrame();
    }

    public void DoEvents() {
        Event @event;
        this.sdl.PumpEvents();
        while (this.sdl.PollEvent(&@event) == (int) SdlBool.True) {
            var type = (EventType) @event.Type;
            if (type == EventType.Windowevent) {
                var windowEvent = @event.Window;
                if (windowEvent.WindowID == this.windowId) {
                    if ((WindowEventID) @event.Window.Event == WindowEventID.Close) this.Exiting = true;
                }
            }

            ImGuiImplSDL2.ProcessEvent((SDLEvent*) &@event);
        }
    }

    public void Render(Action draw) {
        ImGui.SetCurrentContext(this.imguiContext);
        ImGuiImplSDL2.NewFrame();
        ImGui.NewFrame();

        draw();

        this.sdl.GLMakeCurrent(this.window, (void*) this.context.Handle);
        this.gl.BindFramebuffer(GLFramebufferTarget.Framebuffer, 0);

        this.gl.ClearColor(this.backgroundColor.X, this.backgroundColor.Y, this.backgroundColor.Z, 1);
        // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
        this.gl.Clear(GLClearBufferMask.ColorBufferBit | GLClearBufferMask.DepthBufferBit);

        ImGui.Render();
        ImGui.EndFrame();

        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

        this.sdl.GLSwapWindow(this.window);
        this.sdl.GLSetSwapInterval(1);
    }

    public void Dispose() {
        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL2.Shutdown();
        ImGuiImplSDL2.SetCurrentContext(null);
        ImGuiImplOpenGL3.SetCurrentContext(null);
        ImGui.SetCurrentContext(null);
        ImGui.DestroyContext(this.imguiContext);

        this.context.Dispose();
        this.sdl.DestroyWindow(this.window);
        this.sdl.Quit();
    }

    public nint CreateTexture(byte[] data, int width, int height) {
        // Swap ARGB to RGBA
        data = data.ToArray();
        for (var i = 0; i < data.Length; i += 4) {
            (data[i], data[i + 2]) = (data[i + 2], data[i]);
        }

        fixed (byte* dataPtr = data) {
            var texture = this.gl.GenTexture();
            this.gl.BindTexture(GLTextureTarget.Texture2D, texture);

            this.gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter,
                (int) GLTextureMinFilter.Linear);
            this.gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter,
                (int) GLTextureMagFilter.Linear);

            this.gl.PixelStorei(GLPixelStoreParameter.UnpackRowLength, 0);
            this.gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, width, height, 0,
                GLPixelFormat.Rgba,
                GLPixelType.UnsignedByte, (nint) dataPtr);

            return (nint) texture;
        }
    }

    public void DestroyTexture(nint texture) {
        this.gl.DeleteTexture((uint) texture);
    }

    private class NativeContext(Sdl sdl, Silk.NET.SDL.Window* window) : IGLContext {
        private void* glContext = sdl.GLCreateContext(window);
        public nint Handle => (nint) this.glContext;
        public bool IsCurrent => sdl.GLGetCurrentContext() == this.glContext;

        public void Dispose() {
            if (this.glContext != null) {
                sdl.GLDeleteContext(this.glContext);
                this.glContext = null;
            }
        }

        public bool TryGetProcAddress(string procName, out nint procAddress) {
            procAddress = (nint) sdl.GLGetProcAddress(procName);
            return procAddress != 0;
        }

        public nint GetProcAddress(string procName)
            => (nint) sdl.GLGetProcAddress(procName);

        public bool IsExtensionSupported(string extensionName)
            => sdl.GLExtensionSupported(extensionName) != 0;

        public void MakeCurrent() => sdl.GLMakeCurrent(window, this.glContext);
        public void SwapBuffers() => sdl.GLSwapWindow(window);
        public void SwapInterval(int interval) => sdl.GLSetSwapInterval(interval);
    }
}
