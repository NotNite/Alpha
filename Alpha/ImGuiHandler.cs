using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Veldrid;
using Veldrid.Sdl2;

namespace Alpha;

// stolen from https://github.com/mellinoe/ImGui.NET/blob/master/src/ImGui.NET.SampleProgram/ImGuiController.cs
public class ImGuiHandler : IDisposable {
    private readonly Sdl2Window _window;
    private readonly GraphicsDevice _graphicsDevice;

    private DeviceBuffer _vertexBuffer = null!;
    private DeviceBuffer _indexBuffer = null!;
    private DeviceBuffer _projMatrixBuffer = null!;

    private Shader _vertexShader = null!;
    private Shader _fragmentShader = null!;

    private ResourceLayout _layout = null!;
    private ResourceLayout _textureLayout = null!;

    private Pipeline _pipeline = null!;
    private ResourceSet _mainResourceSet = null!;
    private ResourceSet _fontTextureResourceSet = null!;

    private const nint FontAtlasId = 1;
    private Texture _fontTexture = null!;
    private TextureView _fontTextureView = null!;

    private readonly Dictionary<TextureView, (nint, ResourceSet)> _setsByView = new();
    private readonly Dictionary<Texture, TextureView> _autoViewsByTexture = new();
    private readonly Dictionary<nint, (nint, ResourceSet)> _viewsById = new();
    private readonly List<IDisposable> _ownedResources = new();
    private int _lastAssignedId = 100;

    private bool _frameBegun;

    public ImGuiHandler(Sdl2Window window, GraphicsDevice graphicsDevice) {
        this._window = window;
        this._graphicsDevice = graphicsDevice;

        ImGui.CreateContext();

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        // Why the hell does this require an unsafe block, I'll never know
        unsafe {
            var path = Path.Combine(Program.DataDirectory, "imgui.ini");
            File.Create(path).Dispose();
            io.NativePtr->IniFilename = (byte*)Marshal.StringToHGlobalAnsi(path).ToPointer();
        }

        this.CreateDeviceResources();
        this.SetPerFrameImGuiData(1f / 60f);

        ImGui.NewFrame();
        this._frameBegun = true;
    }

    public void Update(float delta, InputSnapshot snapshot) {
        if (this._frameBegun) ImGui.Render();

        this.SetPerFrameImGuiData(delta);
        this.UpdateImGuiInput(snapshot);

        this._frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render(CommandList cl) {
        if (!this._frameBegun) return;

        this._frameBegun = false;
        ImGui.Render();
        this.RenderImDrawData(ImGui.GetDrawData(), cl);
    }

    public void Dispose() {
        this._vertexBuffer.Dispose();
        this._indexBuffer.Dispose();
        this._projMatrixBuffer.Dispose();

        this._vertexShader.Dispose();
        this._fragmentShader.Dispose();

        this._layout.Dispose();
        this._textureLayout.Dispose();

        this._pipeline.Dispose();
        this._mainResourceSet.Dispose();
        this._fontTextureResourceSet.Dispose();

        this._fontTexture.Dispose();
        this._fontTextureView.Dispose();

        foreach (var res in this._ownedResources) {
            res.Dispose();
        }
    }

    // HELL FOLLOWS
    private void CreateDeviceResources() {
        var gd = this._graphicsDevice;
        var outputDescription = gd.SwapchainFramebuffer.OutputDescription;
        var factory = gd.ResourceFactory;

        this._vertexBuffer =
            factory.CreateBuffer(new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        this._vertexBuffer.Name = "Alpha ImGui Vertex Buffer";

        this._indexBuffer =
            factory.CreateBuffer(new BufferDescription(2000, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
        this._indexBuffer.Name = "Alpha ImGui Index Buffer";

        this.RecreateFontDeviceTexture();

        this._projMatrixBuffer =
            factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        this._projMatrixBuffer.Name = "Alpha Projection Buffer";

        var vertexShaderBytes = this.LoadEmbeddedShaderCode(gd.ResourceFactory, "imgui-vertex");
        var fragmentShaderBytes = this.LoadEmbeddedShaderCode(gd.ResourceFactory, "imgui-frag");

        this._vertexShader = factory.CreateShader(
            new ShaderDescription(ShaderStages.Vertex, vertexShaderBytes,
                gd.BackendType == GraphicsBackend.Metal ? "VS" : "main")
        );

        this._fragmentShader = factory.CreateShader(
            new ShaderDescription(ShaderStages.Fragment, fragmentShaderBytes,
                gd.BackendType == GraphicsBackend.Metal ? "FS" : "main")
        );

        VertexLayoutDescription[] vertexLayouts = {
            new(
                new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate,
                    VertexElementFormat.Float2),
                new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm)
            )
        };

        this._layout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription(
                    "ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex
                ),
                new ResourceLayoutElementDescription(
                    "MainSampler", ResourceKind.Sampler, ShaderStages.Fragment
                )
            ));


        this._textureLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(new ResourceLayoutElementDescription(
                "MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment
            )));

        GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
            BlendStateDescription.SingleAlphaBlend,
            new DepthStencilStateDescription(false, false, ComparisonKind.Always),
            new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, true),
            PrimitiveTopology.TriangleList,
            new ShaderSetDescription(vertexLayouts, new[] { this._vertexShader, this._fragmentShader }),
            new[] { this._layout, this._textureLayout },
            outputDescription,
            ResourceBindingModel.Default
        );
        this._pipeline = factory.CreateGraphicsPipeline(ref pd);

        this._mainResourceSet =
            factory.CreateResourceSet(new ResourceSetDescription(this._layout, this._projMatrixBuffer,
                gd.PointSampler));

        this._fontTextureResourceSet =
            factory.CreateResourceSet(new ResourceSetDescription(this._textureLayout, this._fontTextureView));
    }

    private byte[] LoadEmbeddedShaderCode(ResourceFactory factory, string name) => factory.BackendType switch {
        GraphicsBackend.Direct3D11 => this.GetEmbeddedResourceBytes($"{name}.hlsl.bytes"),
        GraphicsBackend.OpenGL => this.GetEmbeddedResourceBytes($"{name}.glsl"),
        GraphicsBackend.Vulkan => this.GetEmbeddedResourceBytes($"{name}.spv"),
        GraphicsBackend.Metal => this.GetEmbeddedResourceBytes($"{name}.metallib"),
        _ => throw new NotImplementedException()
    };

    private byte[] GetEmbeddedResourceBytes(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream s = assembly.GetManifestResourceStream(resourceName)!;

        var ret = new byte[s.Length];

        var readBytes = 0;
        while (readBytes < s.Length) {
            readBytes += s.Read(ret, readBytes, (int)s.Length - readBytes);
        }

        return ret;
    }

    private void RecreateFontDeviceTexture() {
        var gd = this._graphicsDevice;
        var io = ImGui.GetIO();

        io.Fonts.GetTexDataAsRGBA32(out nint pixels, out var width, out var height, out var bytesPerPixel);
        io.Fonts.SetTexID(FontAtlasId);

        this._fontTexture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled
        ));
        this._fontTexture.Name = "Alpha Font Texture";

        gd.UpdateTexture(
            this._fontTexture,
            pixels,
            (uint)(bytesPerPixel * width * height),
            0,
            0,
            0,
            (uint)width,
            (uint)height,
            1,
            0,
            0
        );
        this._fontTextureView = gd.ResourceFactory.CreateTextureView(this._fontTexture);

        io.Fonts.ClearTexData();
    }

    private void SetPerFrameImGuiData(float deltaSeconds) {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(this._window.Width, this._window.Height);
        io.DisplayFramebufferScale = new Vector2(1f);
        io.DeltaTime = deltaSeconds;
    }

    private void UpdateImGuiInput(InputSnapshot snapshot) {
        var io = ImGui.GetIO();

        io.AddMousePosEvent(snapshot.MousePosition.X, snapshot.MousePosition.Y);
        io.AddMouseButtonEvent(0, snapshot.IsMouseDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, snapshot.IsMouseDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, snapshot.IsMouseDown(MouseButton.Middle));
        io.AddMouseWheelEvent(0, snapshot.WheelDelta);

        foreach (var t in snapshot.KeyCharPresses) {
            io.AddInputCharacter(t);
        }

        foreach (var keyEvent in snapshot.KeyEvents) {
            if (this.TryMapKey(keyEvent.Key, out var imguiKey)) {
                io.AddKeyEvent(imguiKey, keyEvent.Down);
            }
        }
    }

    private void RenderImDrawData(ImDrawDataPtr drawData, CommandList cl) {
        var gd = this._graphicsDevice;

        var vertexOffsetInVertices = 0;
        var indexOffsetInElements = 0;

        if (drawData.CmdListsCount == 0) return;

        var totalVbSize = drawData.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>();
        if (totalVbSize > this._vertexBuffer.SizeInBytes) {
            gd.DisposeWhenIdle(this._vertexBuffer);
            this._vertexBuffer = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(totalVbSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic)
            );
        }

        var totalIbSize = drawData.TotalIdxCount * sizeof(ushort);
        if (totalIbSize > this._indexBuffer.SizeInBytes) {
            gd.DisposeWhenIdle(this._indexBuffer);
            this._indexBuffer = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(totalIbSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic)
            );
        }

        for (var i = 0; i < drawData.CmdListsCount; i++) {
            var cmdList = drawData.CmdListsRange[i];

            cl.UpdateBuffer(
                this._vertexBuffer,
                (uint)(vertexOffsetInVertices * Unsafe.SizeOf<ImDrawVert>()),
                cmdList.VtxBuffer.Data,
                (uint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>())
            );

            cl.UpdateBuffer(
                this._indexBuffer,
                (uint)(indexOffsetInElements * sizeof(ushort)),
                cmdList.IdxBuffer.Data,
                (uint)(cmdList.IdxBuffer.Size * sizeof(ushort))
            );

            vertexOffsetInVertices += cmdList.VtxBuffer.Size;
            indexOffsetInElements += cmdList.IdxBuffer.Size;
        }

        var io = ImGui.GetIO();
        var mvp = Matrix4x4.CreateOrthographicOffCenter(
            0f,
            io.DisplaySize.X,
            io.DisplaySize.Y,
            0f,
            -1f,
            1f
        );

        this._graphicsDevice.UpdateBuffer(this._projMatrixBuffer, 0, ref mvp);

        cl.SetVertexBuffer(0, this._vertexBuffer);
        cl.SetIndexBuffer(this._indexBuffer, IndexFormat.UInt16);
        cl.SetPipeline(this._pipeline);
        cl.SetGraphicsResourceSet(0, this._mainResourceSet);

        drawData.ScaleClipRects(io.DisplayFramebufferScale);

        var vtxOffset = 0;
        var idxOffset = 0;

        for (var n = 0; n < drawData.CmdListsCount; n++) {
            var cmdList = drawData.CmdListsRange[n];

            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++) {
                var pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.TextureId != IntPtr.Zero) {
                    if (pcmd.TextureId == FontAtlasId) {
                        cl.SetGraphicsResourceSet(1, this._fontTextureResourceSet);
                    } else {
                        var imageResourceSet = this.GetImageResourceSet(pcmd.TextureId);
                        if (imageResourceSet != null) cl.SetGraphicsResourceSet(1, imageResourceSet);
                    }
                }

                cl.SetScissorRect(
                    0,
                    (uint)pcmd.ClipRect.X,
                    (uint)pcmd.ClipRect.Y,
                    (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y)
                );

                cl.DrawIndexed(
                    pcmd.ElemCount,
                    1,
                    (uint)(pcmd.IdxOffset + idxOffset),
                    (int)(pcmd.VtxOffset + vtxOffset),
                    0
                );
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private bool TryMapKey(Key key, out ImGuiKey result) {
        ImGuiKey KeyToImGuiKeyShortcut(Key keyToConvert, Key startKey1, ImGuiKey startKey2) {
            int changeFromStart1 = (int)keyToConvert - (int)startKey1;
            return startKey2 + changeFromStart1;
        }

        result = key switch {
            >= Key.F1 and <= Key.F12 => KeyToImGuiKeyShortcut(key, Key.F1, ImGuiKey.F1),
            >= Key.Keypad0 and <= Key.Keypad9 => KeyToImGuiKeyShortcut(key, Key.Keypad0, ImGuiKey.Keypad0),
            >= Key.A and <= Key.Z => KeyToImGuiKeyShortcut(key, Key.A, ImGuiKey.A),
            >= Key.Number0 and <= Key.Number9 => KeyToImGuiKeyShortcut(key, Key.Number0, ImGuiKey._0),
            Key.ShiftLeft or Key.ShiftRight => ImGuiKey.ModShift,
            Key.ControlLeft or Key.ControlRight => ImGuiKey.ModCtrl,
            Key.AltLeft or Key.AltRight => ImGuiKey.ModAlt,
            Key.WinLeft or Key.WinRight => ImGuiKey.ModSuper,
            Key.Menu => ImGuiKey.Menu,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Space => ImGuiKey.Space,
            Key.Tab => ImGuiKey.Tab,
            Key.BackSpace => ImGuiKey.Backspace,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.NumLock => ImGuiKey.NumLock,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.Tilde => ImGuiKey.GraveAccent,
            Key.Minus => ImGuiKey.Minus,
            Key.Plus => ImGuiKey.Equal,
            Key.BracketLeft => ImGuiKey.LeftBracket,
            Key.BracketRight => ImGuiKey.RightBracket,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Quote => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.BackSlash or Key.NonUSBackSlash => ImGuiKey.Backslash,
            _ => ImGuiKey.None
        };

        return result != ImGuiKey.None;
    }

    public nint GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView) {
        if (!this._setsByView.TryGetValue(textureView, out var rsi)) {
            var resourceSet = factory.CreateResourceSet(new ResourceSetDescription(this._textureLayout, textureView));
            rsi = (this.GetNextImGuiBindingId(), resourceSet);

            this._setsByView.Add(textureView, rsi);
            this._viewsById.Add(rsi.Item1, rsi);
            this._ownedResources.Add(resourceSet);
        }

        return rsi.Item1;
    }

    public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture) {
        if (!this._autoViewsByTexture.TryGetValue(texture, out var textureView)) {
            textureView = factory.CreateTextureView(texture);
            this._autoViewsByTexture.Add(texture, textureView);
            this._ownedResources.Add(textureView);
        }

        return this.GetOrCreateImGuiBinding(factory, textureView);
    }

    public ResourceSet? GetImageResourceSet(IntPtr imGuiBinding) {
        if (!this._viewsById.TryGetValue(imGuiBinding, out var tvi)) {
            return null;
        }

        return tvi.Item2;
    }

    private IntPtr GetNextImGuiBindingId() {
        return this._lastAssignedId++;
    }

    public void DisposeAllTextures() {
        foreach (var d in this._ownedResources) {
            d.Dispose();
        }

        this._ownedResources.Clear();
        this._autoViewsByTexture.Clear();
        this._setsByView.Clear();
        this._viewsById.Clear();

        this._lastAssignedId = 100;
    }
}
