using System.Numerics;
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
    private readonly void* glContext;
    private readonly ImGuiContext* imguiContext;

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

    public ImGuiWrapper(
        string title,
        Vector2 pos,
        Vector2 size,
        string iniPath,
        byte[]? iconData = null
    ) {
        this.sdl = Sdl.GetApi();
        this.sdl.Init(Sdl.InitEvents + Sdl.InitVideo);
        const WindowFlags flags = WindowFlags.Opengl
                                  | WindowFlags.Resizable
                                  | WindowFlags.AllowHighdpi;

        this.window = this.sdl.CreateWindow(
            title,
            (int) pos.X, (int) pos.Y,
            (int) size.X, (int) size.Y,
            (uint) flags
        );
        this.windowId = this.sdl.GetWindowID(this.window);

        if (iconData != null) {
            var widthHeight = iconData.Length / 4;
            fixed (byte* dataPtr = iconData) {
                // Assume uniform width/height
                var surface = this.sdl.CreateRGBSurfaceFrom(
                    dataPtr,
                    widthHeight, widthHeight, 32, widthHeight * 4,
                    0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000);
                this.sdl.SetWindowIcon(this.window, surface);
                this.sdl.FreeSurface(surface);
            }
        }

        this.glContext = this.sdl.GLCreateContext(this.window);
        GL.InitApi(new SdlNativeContext(this.sdl));

        this.imguiContext = ImGui.CreateContext();
        ImGui.SetCurrentContext(this.imguiContext);
        ImGuiImplSDL2.SetCurrentContext(this.imguiContext);

        var builder = new ImGuiFontBuilder();
        builder
            .AddDefaultFont()
            .SetOption(config => { config.FontBuilderFlags |= (uint) ImGuiFreeTypeBuilderFlags.LoadColor; });

        const string cjkFont = "C:/Windows/Fonts/msyh.ttc";
        if (File.Exists(cjkFont)) {
            var ranges = ImGui.GetIO().Fonts.GetGlyphRangesJapanese();
            builder.AddFontFromFileTTF(cjkFont, 13f, ranges);
        }

        builder.Build();

        ImGuiImplSDL2.InitForOpenGL((SDLWindow*) this.window, this.glContext);
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

        this.sdl.GLMakeCurrent(this.window, this.glContext);
        GL.BindFramebuffer(GLFramebufferTarget.Framebuffer, 0);

        const float grey = 40 / 255f;
        GL.ClearColor(grey, grey, grey, 1f);

        // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
        GL.Clear(GLClearBufferMask.ColorBufferBit | GLClearBufferMask.DepthBufferBit);

        ImGui.Render();
        ImGui.EndFrame();

        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

        this.sdl.GLSwapWindow(this.window);
        this.sdl.GLSetSwapInterval(1);
    }

    public float GetMonitorRefreshRate() {
        var displayIndex = this.sdl.GetWindowDisplayIndex(this.window);
        DisplayMode mode;
        this.sdl.GetDisplayMode(displayIndex, 0, &mode);
        return mode.RefreshRate;
    }

    public void Dispose() {
        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL2.Shutdown();
        ImGuiImplSDL2.SetCurrentContext(null);
        ImGuiImplOpenGL3.SetCurrentContext(null);
        ImGui.SetCurrentContext(null);
        ImGui.DestroyContext(this.imguiContext);

        this.sdl.GLDeleteContext(this.glContext);
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
            var texture = GL.GenTexture();
            GL.BindTexture(GLTextureTarget.Texture2D, texture);

            GL.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter,
                (int) GLTextureMinFilter.Linear);
            GL.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter,
                (int) GLTextureMagFilter.Linear);

            GL.PixelStorei(GLPixelStoreParameter.UnpackRowLength, 0);
            GL.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, width, height, 0, GLPixelFormat.Rgba,
                GLPixelType.UnsignedByte, (nint) dataPtr);

            return (nint) texture;
        }
    }

    public void DestroyTexture(nint texture) {
        GL.DeleteTexture((uint) texture);
    }

    public class SdlNativeContext(Sdl sdl) : INativeContext {
        public nint GetProcAddress(string procName) {
            return (nint) sdl.GLGetProcAddress(procName);
        }

        public bool TryGetProcAddress(string procName, out nint procAddress) {
            return (procAddress = (nint) sdl.GLGetProcAddress(procName)) != nint.Zero;
        }

        public bool IsExtensionSupported(string extensionName) {
            return sdl.GLExtensionSupported(extensionName) != 0;
        }

        public void Dispose() { }
    }
}
