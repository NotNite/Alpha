using System.Text.Json;
using System.Text.Json.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace Alpha.Modules.Excel;

public class ComplexLink {
    [JsonPropertyName("sheet")] public string? SheetSingle { get; init; }
    [JsonPropertyName("sheets")] public string[]? SheetList { get; init; }

    public string[] Sheets => this.SheetList ?? new[] { this.SheetSingle! };

    // TODO project & key

    [JsonPropertyName("when")] public WhenClause? When { get; init; }
}

public class WhenClause {
    [JsonPropertyName("key")] public string Key { get; init; }
    [JsonPropertyName("value")] public JsonElement Value { get; init; }
}
