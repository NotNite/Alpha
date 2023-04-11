using System.Runtime.InteropServices;
using Lumina.Data.Files;
using Veldrid;

namespace Alpha.Utils;

public class ImageHandler {
    private Dictionary<string, nint> _textureCache = new();
    private List<IDisposable> _disposables = new();
    private List<nint> _pointers = new();

    public TexFile? GetIcon(uint id) {
        var nqPath = $"ui/icon/{id / 1000 * 1000:000000}/{id:000000}.tex";
        var hqPath = $"ui/icon/{id / 1000 * 1000:000000}/{id:000000}_hr1.tex";
        var tryOrder = Services.Configuration.PreferHr1 ? new[] { hqPath, nqPath } : new[] { nqPath, hqPath };

        string? usedPath = null;
        try {
            foreach (var p in tryOrder) {
                if (Services.GameData.FileExists(p)) {
                    usedPath = p;
                    break;
                }
            }
        } catch {
            // Lumina likes to throw errors on FileExists for some reason, so let's just ignore it
        }

        return usedPath is null ? null : Services.GameData.GetFile<TexFile>(usedPath);
    }

    public nint DisplayTex(string path) {
        return this.DisplayTex(Services.GameData.GetFile<TexFile>(path));
    }

    public nint DisplayTex(TexFile tex) {
        var path = tex.FilePath.ToString();
        if (this._textureCache.TryGetValue(path, out var ptr)) {
            return ptr;
        }

        var rgb = tex.ImageData;
        var imageDataPtr = Marshal.AllocHGlobal(rgb.Length);

        for (var i = 0; i < rgb.Length; i += 4) {
            var b = rgb[i];
            var g = rgb[i + 1];
            var r = rgb[i + 2];
            var a = rgb[i + 3];

            Marshal.WriteByte(imageDataPtr, i, r);
            Marshal.WriteByte(imageDataPtr, i + 1, g);
            Marshal.WriteByte(imageDataPtr, i + 2, b);
            Marshal.WriteByte(imageDataPtr, i + 3, a);
        }

        var texture = Program.GraphicsDevice.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
            tex.Header.Width,
            tex.Header.Height,
            1,
            1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled
        ));
        texture.Name = path;
        Program.GraphicsDevice.UpdateTexture(
            texture,
            imageDataPtr,
            (uint)(4 * tex.Header.Width * tex.Header.Height),
            0,
            0,
            0,
            tex.Header.Width,
            tex.Header.Height,
            1,
            0,
            0
        );

        var binding = Program.ImGuiHandler.GetOrCreateImGuiBinding(Program.GraphicsDevice.ResourceFactory, texture);
        
        this._textureCache.Add(path, binding);
        this._disposables.Add(texture);
        this._pointers.Add(imageDataPtr);
        
        return binding;
    }

    public void DisposeAllTextures() {
        Program.ImGuiHandler.DisposeAllTextures();

        foreach (var d in this._disposables) d.Dispose();
        foreach (var p in this._pointers) Marshal.FreeHGlobal(p);
        
        this._textureCache = new();
        this._disposables = new();
        this._pointers = new();
    }
}
