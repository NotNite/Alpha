using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Lumina.Data.Files;
using Lumina.Text;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;
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

    // Not a very good place for this...
    // Taken from Kizer - thanks!
    private static void XmlRepr(StringBuilder sb, BaseExpression expr) {
        switch (expr) {
            case PlaceholderExpression ple:
                sb.Append('<').Append(ple.ExpressionType).Append(" />");
                break;
            case IntegerExpression ie:
                sb.Append('<').Append(ie.ExpressionType).Append('>');
                sb.Append(ie.Value);
                sb.Append("</").Append(ie.ExpressionType).Append('>');
                break;
            case StringExpression se:
                sb.Append('<').Append(se.ExpressionType).Append('>');
                XmlRepr(sb, se.Value);
                sb.Append("</").Append(se.ExpressionType).Append('>');
                break;
            case ParameterExpression pae:
                sb.Append('<').Append(pae.ExpressionType).Append('>');
                sb.Append("<operand>");
                XmlRepr(sb, pae.Operand);
                sb.Append("</operand>");
                sb.Append("</").Append(pae.ExpressionType).Append('>');
                break;
            case BinaryExpression pae:
                sb.Append('<').Append(pae.ExpressionType).Append('>');
                sb.Append("<operand1>");
                XmlRepr(sb, pae.Operand1);
                sb.Append("</operand1>");
                sb.Append("<operand2>");
                XmlRepr(sb, pae.Operand2);
                sb.Append("</operand2>");
                sb.Append("</").Append(pae.ExpressionType).Append('>');
                break;
        }
    }

    private static void XmlRepr(StringBuilder sb, SeString s) {
        foreach (var payload in s.Payloads) {
            if (payload is TextPayload t) {
                sb.Append(t.RawString);
            } else if (!payload.Expressions.Any()) {
                sb.Append($"<{payload.PayloadType} />");
            } else {
                sb.Append($"<{payload.PayloadType}>");
                foreach (var expr in payload.Expressions)
                    XmlRepr(sb, expr);
                sb.Append($"<{payload.PayloadType}>");
            }
        }
    }

    public static string DisplaySeString(SeString s) {
        var sb = new StringBuilder();
        XmlRepr(sb, s);
        return sb.ToString();
    }
}
