using System.Text.Json.Serialization;
using Alpha.Services.Excel.Cells;
using Serilog;

#pragma warning disable CS8618

namespace Alpha.Services.Excel.SaintCoinach;

// Don't believe Rider's lies, all JSON stuff *needs* to have setters or else the JSON deserializer won't assign them
// I wasted an hour with this, I hate you Microsoft
public class SaintCoinachSheetDefinition : ISheetDefinition {
    [JsonPropertyName("sheet")] public string? Sheet { get; init; }
    [JsonPropertyName("defaultColumn")] public string? DefaultColumnName { get; init; }
    [JsonPropertyName("definitions")] public ColumnDefinition[] Definitions { get; init; }

    public uint? DefaultColumn { get; set; }
    public bool Ready { get; private set; }

    private readonly Dictionary<uint, ColumnDefinition> columnCache = new();

    public void Init(ExcelService excel, IAlphaSheet sheet) {
        if (!this.Ready) {
            try {
                foreach (var def in this.Definitions) {
                    this.ResolveDefinition(def);
                }

                this.DefaultColumn = this.DefaultColumnName is null
                                         ? null
                                         : this.GetColumnForName(this.DefaultColumnName);
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize sheet definition");
            }

            this.Ready = true;
        }
    }

    private uint ResolveDefinition(ColumnDefinition def, uint offset = 0) {
        // Index defaults to zero if there isn't one specified, BUT this might be a repeat or group definition
        var realOffset = def.Index == 0 ? offset : def.Index;

        if (def is RepeatColumnDefinition rcd) {
            var baseIdx = realOffset;

            for (var i = 0; i < rcd.Count; i++) baseIdx += this.ResolveDefinition(rcd.Definition, baseIdx);

            return baseIdx - realOffset;
        }

        if (def is GroupColumnDefinition gcd) {
            var baseIdx = realOffset;

            foreach (var member in gcd.Members) baseIdx += this.ResolveDefinition(member, baseIdx);

            return baseIdx - realOffset;
        }

        // Normal definition, just insert and move on
        this.columnCache.TryAdd(realOffset, def);

        return 1;
    }

    private ColumnDefinition? GetDefinitionByIndex(uint index) {
        return this.columnCache.TryGetValue(index, out var retDef) ? retDef : null;
    }

    public string? GetNameForColumn(uint column) {
        var def = this.GetDefinitionByIndex(column);

        if (def is SingleColumnDefinition srd) return srd.Name;
        // TODO

        return null;
    }

    public uint? GetColumnForName(string name) {
        foreach (var (key, value) in this.columnCache)
            if (value is SingleColumnDefinition srd && srd.Name == name)
                return key;
        // TODO
        return null;
    }

    public ConverterDefinition? GetConverterForColumn(uint index) {
        var def = this.GetDefinitionByIndex(index);

        if (def is SingleColumnDefinition srd) return srd.Converter;
        // TODO

        return null;
    }

    public Cell? GetCell(
        ExcelService excel, IAlphaSheet sheet, uint row, ushort? subrow, uint column, object? data
    ) {
        var converter = this.GetConverterForColumn(column);
        if (converter is null) return null;

        try {
            switch (converter) {
                case LinkConverterDefinition {Target: not null} link: {
                    var linkSheet = excel.GetSheet(link.Target);
                    if (linkSheet is null) return null;
                    var targetCol = excel.GetDefaultColumnForSheet(link.Target);
                    return new LinkCell(row, column, data, linkSheet, targetCol ?? 0);
                }

                case IconConverterDefinition: {
                    return new IconCell(row, subrow, column, data);
                }

                case ComplexLinkConverterDefinition complex: {
                    var targetRow = 0u;
                    try {
                        targetRow = Convert.ToUInt32(data);
                    } catch {
                        // ignored
                    }

                    var resolvedLinks = complex.ResolveComplexLink(
                        excel,
                        sheet,
                        row,
                        targetRow
                    );

                    var result = new List<(IAlphaSheet, uint, ushort?, uint)>();
                    foreach (var link in resolvedLinks) {
                        var targetSheet = excel.GetSheet(link.Link);
                        if (targetSheet is null) continue;
                        var targetCol = excel.GetDefaultColumnForSheet(link.Link) ?? 0;
                        result.Add((targetSheet, link.TargetRow, null, targetCol));
                    }

                    return new ComplexLinkCell(row, subrow, column, data, result);
                }

                default: {
                    return null;
                }
            }
        } catch (Exception e) {
            Log.Error(e, "Failed to create cell for {SheetName} {Row} {Column}", sheet.Name, row, column);
            return null;
        }
    }
}
