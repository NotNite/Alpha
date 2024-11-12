using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CS8618
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Alpha.Services.Excel.SaintCoinach;

[JsonConverter(typeof(RowDefinitionJsonConverter))]
public class ColumnDefinition {
    [JsonPropertyName("index")] public uint Index { get; init; } = 0;
}

public class SingleColumnDefinition : ColumnDefinition {
    [JsonPropertyName("name")] public string Name { get; init; }
    [JsonPropertyName("converter")] public ConverterDefinition? Converter { get; init; }
}

public class RepeatColumnDefinition : ColumnDefinition {
    [JsonPropertyName("count")] public uint Count { get; init; }
    [JsonPropertyName("definition")] public ColumnDefinition Definition { get; init; }
}

public class GroupColumnDefinition : ColumnDefinition {
    [JsonPropertyName("members")] public ColumnDefinition[] Members { get; init; }
}

// Really not a fan I had to do this, but I can't wrestle the polymorphism attributes into working
internal class RowDefinitionJsonConverter : JsonConverter<ColumnDefinition> {
    public override ColumnDefinition? Read(
        ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options
    ) {
        var jsonDocument = JsonDocument.ParseValue(ref reader);
        var type = jsonDocument.RootElement.TryGetProperty("type", out var prop) ? prop.GetString() : null;

        return type switch {
            "repeat" => jsonDocument.Deserialize(typeof(RepeatColumnDefinition)) as ColumnDefinition,
            "group" => jsonDocument.Deserialize(typeof(GroupColumnDefinition)) as ColumnDefinition,
            _ => jsonDocument.Deserialize(typeof(SingleColumnDefinition)) as ColumnDefinition
        };
    }

    public override void Write(Utf8JsonWriter writer, ColumnDefinition value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}
