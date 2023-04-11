using System.Text.Json.Serialization;

#pragma warning disable CS8618

namespace Alpha.Modules.Excel;

// Don't believe Rider's lies, all JSON stuff *needs* to have setters or else the JSON deserializer won't assign them
// I wasted an hour with this, I hate you Microsoft
public class SheetDefinition {
    [JsonPropertyName("sheet")] public string? Sheet { get; init; }
    [JsonPropertyName("defaultColumn")] public string? DefaultColumn { get; init; }
    [JsonPropertyName("definitions")] public ColumnDefinition[] Definitions { get; init; }

    private Dictionary<uint, ColumnDefinition?> _columnCache = new();

    private ColumnDefinition? GetDefinitionByIndex(uint index) {
        if (this._columnCache.TryGetValue(index, out var def)) return def;

        def = this.Definitions.FirstOrDefault(d => d.Index == index);
        this._columnCache[index] = def;

        return def;
    }

    public string? GetNameForColumn(int index) {
        var def = this.GetDefinitionByIndex((uint)index);

        if (def is SingleColumnDefinition srd) return srd.Name;
        // TODO

        return null;
    }

    public ConverterDefinition? GetConverterForColumn(int index) {
        var def = this.GetDefinitionByIndex((uint)index);

        if (def is SingleColumnDefinition srd) return srd.Converter;
        // TODO

        return null;
    }
}
