using System.Numerics;
using System.Text.Json.Serialization;
using Alpha.Utils;
using ImGuiNET;
using Lumina.Extensions;
using NativeFileDialogSharp;
using Serilog;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Alpha.Modules.Excel;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(LinkConverterDefinition), "link")]
[JsonDerivedType(typeof(IconConverterDefinition), "icon")]
public class ConverterDefinition {
    [JsonPropertyName("type")] public string? Type { get; init; }

    public virtual void Draw(int row, int col, object data) { }
}

public class LinkConverterDefinition : ConverterDefinition {
    [JsonPropertyName("target")] public string? Target { get; init; }

    public override void Draw(int row, int col, object data) {
        if (this.Target is null) return;

        var targetRow = Convert.ToInt32(data);
        var text = $"{this.Target}#{targetRow}" + $"##{row}_{col}";

        if (ImGui.Button(text)) {
            Services.ModuleManager.GetModule<ExcelModule>().OpenSheet(this.Target, targetRow);
        }
    }
}

public class IconConverterDefinition : ConverterDefinition {
    public override void Draw(int row, int col, object data) {
        var iconId = Convert.ToUInt32(data);
        var icon = Services.GameData.GetIcon(iconId);
        if (icon is not null) {
            var handle = UiUtils.DisplayTex(icon);
            var size = new Vector2(icon.Header.Width, icon.Header.Height);
            ImGui.Image(handle, size);

            if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                if (ImGui.MenuItem("Save")) {
                    FileUtils.Save(icon.Data, "tex");
                }

                ImGui.EndPopup();
            }
        }
    }
}
