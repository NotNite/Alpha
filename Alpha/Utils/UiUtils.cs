using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Lumina.Data.Files;
using Veldrid;

namespace Alpha.Utils;

public static class UiUtils {
    public static void HorizontalSplitter(ref float width) {
        ImGui.Button("##splitter", new Vector2(5, -1));

        ImGui.SetItemAllowOverlap();

        if (ImGui.IsItemActive()) {
            var mouseDelta = ImGui.GetIO().MouseDelta.X;
            width += mouseDelta;
        }
    }

    private static Dictionary<string, nint> _textureCache = new();

    public static nint DisplayTex(string path) {
        return DisplayTex(Services.GameData.GetFile<TexFile>(path));
    }

    public static nint DisplayTex(TexFile tex) {
        var path = tex.FilePath.ToString();
        if (_textureCache.TryGetValue(path, out var ptr)) {
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
        _textureCache.Add(path, binding);
        return binding;
    }
}
