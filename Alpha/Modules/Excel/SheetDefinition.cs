using System.Text.Json.Serialization;
using Serilog;

#pragma warning disable CS8618

namespace Alpha.Modules.Excel;

// Don't believe Rider's lies, all JSON stuff *needs* to have setters or else the JSON deserializer won't assign them
// I wasted an hour with this, I hate you Microsoft
public class SheetDefinition {
    [JsonPropertyName("sheet")] public string? Sheet { get; init; }
    [JsonPropertyName("defaultColumn")] public string? DefaultColumn { get; init; }
    [JsonPropertyName("definitions")] public ColumnDefinition[] Definitions { get; init; }

    private Dictionary<uint, ColumnDefinition?>? _columnCache;

    private uint ResolveDefinition(ColumnDefinition def, uint offset = 0) {
        // Index defaults to zero if there isn't one specified, BUT this might be a repeat or group definition
        var realOffset = def.Index == 0 ? offset : def.Index;

        if (def is RepeatColumnDefinition rcd) {
            var baseIdx = realOffset;

            for (var i = 0; i < rcd.Count; i++) {
                baseIdx += this.ResolveDefinition(rcd.Definition, baseIdx);
            }

            return baseIdx - realOffset;
        }

        if (def is GroupColumnDefinition gcd) {
            var baseIdx = realOffset;

            foreach (var member in gcd.Members) {
                baseIdx += this.ResolveDefinition(member, baseIdx);
            }

            return baseIdx - realOffset;
        }

        // Normal definition, just insert and move on
        if (!this._columnCache!.ContainsKey(realOffset)) {
            this._columnCache[realOffset] = def;
        }

        return 1;
    }

    // can't put this in constructor, dunno why
    private void EnsureColumnCache() {
        if (this._columnCache is null) {
            this._columnCache = new();

            foreach (var def in this.Definitions) {
                this.ResolveDefinition(def);
            }
        }
    }

    private ColumnDefinition? GetDefinitionByIndex(uint index) {
        this.EnsureColumnCache();
        return this._columnCache!.TryGetValue(index, out var retDef) ? retDef : null;
    }

    public string? GetNameForColumn(int index) {
        var def = this.GetDefinitionByIndex((uint)index);

        if (def is SingleColumnDefinition srd) return srd.Name;
        // TODO

        return null;
    }

    public int? GetColumnForName(string name) {
        this.EnsureColumnCache();
        foreach (var (key, value) in this._columnCache!) {
            if (value is SingleColumnDefinition srd && srd.Name == name) return (int)key;
            // TODO
        }

        return null;
    }

    public ConverterDefinition? GetConverterForColumn(int index) {
        var def = this.GetDefinitionByIndex((uint)index);

        if (def is SingleColumnDefinition srd) return srd.Converter;
        // TODO

        return null;
    }
}
