using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CS8618
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Alpha.Modules.Excel;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(LinkConverterDefinition), "link")]
[JsonDerivedType(typeof(IconConverterDefinition), "icon")]
[JsonDerivedType(typeof(ComplexLinkConverterDefinition), "complexlink")]
public class ConverterDefinition {
    [JsonPropertyName("type")] public string? Type { get; init; }
}

public class LinkConverterDefinition : ConverterDefinition {
    [JsonPropertyName("target")] public string? Target { get; init; }
}

public class IconConverterDefinition : ConverterDefinition { }

public class ComplexLinkConverterDefinition : ConverterDefinition {
    [JsonPropertyName("links")] public ComplexLink[] Links { get; init; }

    private bool Compare(object a, JsonElement b) {
        try {
            switch (b.ValueKind) {
                case JsonValueKind.True or JsonValueKind.False:
                    if (a is bool boolean) return boolean == b.GetBoolean();
                    break;

                case JsonValueKind.Number:
                    var number = Convert.ToInt64(a);
                    return number == b.GetInt64();

                case JsonValueKind.String:
                    if (a is string str) return str == b.GetString();
                    break;
            }
        } catch {
            // ignored, for number casting
        }

        return false;
    }

    public string[] ResolveComplexLink(Dictionary<string, object> values) {
        foreach (var link in this.Links) {
            foreach (var (key, value) in values) {
                if (link.When is null) continue;
                if (key != link.When?.Key) continue;

                if (this.Compare(value, link.When.Value)) {
                    return link.Sheets;
                }
            }
        }

        return Array.Empty<string>();
    }
}
