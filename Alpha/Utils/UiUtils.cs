using System.Numerics;
using System.Text;
using ImGuiNET;
using Lumina.Data.Files;
using Lumina.Text;
using Lumina.Text.Expressions;
using Lumina.Text.Payloads;

namespace Alpha.Utils;

public static class UiUtils {
    public static void HorizontalSplitter(ref float width) {
        ImGui.Button("##splitter", new Vector2(5, -1));

        ImGui.SetNextItemAllowOverlap();

        if (ImGui.IsItemActive()) {
            var mouseDelta = ImGui.GetIO().MouseDelta.X;
            width += mouseDelta;
        }
    }

    public static void ExportPng(TexFile tex) {
        var img = Image.LoadPixelData<Bgra32>(tex.ImageData, tex.Header.Width, tex.Header.Height);
        var bytes = new MemoryStream();
        img.SaveAsPng(bytes);
        FileUtils.Save(bytes.ToArray(), "png");
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
