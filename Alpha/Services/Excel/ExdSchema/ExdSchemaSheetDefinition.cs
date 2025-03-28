using Alpha.Services.Excel.Cells;
using Serilog;

// ReSharper disable CollectionNeverUpdated.Global

namespace Alpha.Services.Excel.ExdSchema;

public class ExdSchemaSheetDefinition : ISheetDefinition {
    public required string Name { get; init; }
    public string? DisplayField { get; init; }
    public List<Field>? Fields { get; init; }
    public List<Field>? PendingFields { get; init; }
    public Dictionary<string, List<string>>? Relations { get; init; }

    public uint? DefaultColumn { get; set; }
    public bool Ready { get; private set; }

    private readonly List<Field> flatFields = [];
    private readonly List<uint> columns = [];
    private readonly Dictionary<uint, Field> columnCache = new();

    public void Init(ExcelService excel, IAlphaSheet sheet) {
        if (!this.Ready) {
            try {
                // EXDSchema is ordered by offsets, but we do by sheet column index
                foreach (var col in sheet.Columns
                             .Index()
                             .GroupBy(c => c.Item.Offset)
                             .OrderBy(c => c.Key)
                             .SelectMany(g => g.OrderBy(c => c.Item.Type))) {
                    this.columns.Add((uint) col.Index);
                }

                if (this.Fields is not null) {
                    foreach (var field in this.Fields) {
                        this.ResolveDefinition(field);
                    }
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize sheet definition");
            }

            this.Ready = true;
        }
    }

    private void ResolveDefinition(Field field) {
        if (field.Type is FieldType.Array) {
            for (var i = 0; i < (field.Count ?? 1); i++) {
                if (field.Fields is not null) {
                    foreach (var subfield in field.Fields) {
                        this.ResolveDefinition(subfield);
                    }
                } else {
                    this.RegisterNormalDefinition(field);
                }
            }
        } else {
            // Normal definition, just insert and move on
            this.RegisterNormalDefinition(field);
        }
    }

    private void RegisterNormalDefinition(Field field) {
        this.flatFields.Add(field);
        if (this.IndexToColumn(this.flatFields.Count - 1) is { } col) {
            this.columnCache.TryAdd(col, field);
        }
    }

    private uint? IndexToColumn(int index) {
        if (index < 0 || index >= this.columns.Count) return null;
        return this.columns[index];
    }

    private int? ColumnToIndex(uint column) {
        var idx = this.columns.FindIndex(c => c == column);
        return idx == -1 ? null : idx;
    }

    private Field? GetDefinitionByColumn(uint column) {
        return this.columnCache.TryGetValue(column, out var retDef) ? retDef : null;
    }

    public string? GetNameForColumn(uint column) {
        return this.GetDefinitionByColumn(column)?.Name;
    }

    public uint? GetColumnForName(string name) {
        var idx = this.flatFields.FindIndex(x => x.Name == name);
        return idx == -1 ? null : this.IndexToColumn(idx);
    }

    public Cell? GetCell(
        ExcelService excel, IAlphaSheet sheet, uint row, ushort? subrow, uint column, object? data
    ) {
        var idx = this.ColumnToIndex(column);
        if (idx == null) return null;
        var field = this.flatFields.ElementAtOrDefault((int) idx);
        if (field == null) return null;

        switch (field.Type) {
            case FieldType.Icon: return new IconCell(row, subrow, column, data);
            case FieldType.ModelId: return new ModelCell(row, subrow, column, data);

            case FieldType.Link when field.Condition is {Switch: not null, Cases: not null}: {
                var thisRow = sheet.GetRow(row);
                if (thisRow is null) return null;

                var switchColumn = this.GetColumnForName(field.Condition.Switch);
                if (switchColumn is null) return null;

                var switchData = thisRow.ReadColumn(switchColumn.Value);
                try {
                    var switchValue = Convert.ToInt32(switchData);
                    if (field.Condition.Cases.TryGetValue(switchValue, out var cases)) {
                        var link = this.ResolveMultiLink(excel, row, column, data, cases);
                        if (link is not null) return link;
                    }
                } catch {
                    // ignored
                }

                break;
            }

            case FieldType.Link: {
                if (field.Targets is not null) {
                    var link = this.ResolveMultiLink(excel, row, column, data, field.Targets);
                    if (link is not null) return link;
                }
                break;
            }
        }

        return new DefaultCell(row, subrow, column, data);
    }

    private Cell? ResolveMultiLink(ExcelService excel, uint row, uint column, object? data, List<string> targets) {
        foreach (var target in targets) {
            var targetSheet = excel.GetSheet(target);
            if (targetSheet is null) continue;

            // Match valid row
            try {
                var targetRow = Convert.ToUInt32(data);
                if (targetSheet.GetRow(targetRow) is null) continue;
            } catch {
                continue;
            }

            var targetCol = excel.GetDefaultColumnForSheet(target);
            return new LinkCell(row, column, data, targetSheet, targetCol ?? 0);
        }

        return null;
    }
}
