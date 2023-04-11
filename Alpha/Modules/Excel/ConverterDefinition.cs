using System.Text.Json.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Alpha.Modules.Excel;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(LinkConverterDefinition), "link")]
[JsonDerivedType(typeof(IconConverterDefinition), "icon")]
public class ConverterDefinition {
    [JsonPropertyName("type")] public string? Type { get; init; }
}

public class LinkConverterDefinition : ConverterDefinition {
    [JsonPropertyName("target")] public string? Target { get; init; }
}

public class IconConverterDefinition : ConverterDefinition { }
