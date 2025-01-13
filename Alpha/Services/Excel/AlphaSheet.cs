using Lumina.Data.Structs.Excel;
using Lumina.Excel;

namespace Alpha.Services.Excel;

public interface IAlphaSheet {
    public string Name { get; }
    public int Count { get; }
    public IReadOnlyList<ExcelColumnDefinition> Columns { get; }

    public IAlphaRow? GetRow(uint row, ushort? subrow = null);
    public IEnumerable<IAlphaRow> GetRows();
}

public record AlphaSheet(ExcelSheet<RawRow> Sheet, string Name) : IAlphaSheet {
    public IReadOnlyList<ExcelColumnDefinition> Columns => this.Sheet.Columns;
    public int Count => this.Sheet.Count;

    public IAlphaRow? GetRow(uint row, ushort? subrow = null) {
        try {
            if (row == uint.MaxValue) return null;
            return new AlphaRow(this.Sheet.GetRow(row));
        } catch {
            return null;
        }
    }

    public IEnumerable<IAlphaRow> GetRows() {
        foreach (var row in this.Sheet) {
            yield return new AlphaRow(row);
        }
    }
}

public record AlphaSubrowSheet(SubrowExcelSheet<RawSubrow> Sheet, string Name) : IAlphaSheet {
    public IReadOnlyList<ExcelColumnDefinition> Columns => this.Sheet.Columns;
    public int Count => this.Sheet.Select(s => s.Count).Sum();

    public IAlphaRow? GetRow(uint row, ushort? subrow = null) {
        if (row == uint.MaxValue) return null;
        return new AlphaSubrow(this.Sheet.GetSubrow(row, subrow.GetValueOrDefault()));
    }

    public IEnumerable<IAlphaRow> GetRows() {
        foreach (var row in this.Sheet) {
            foreach (var subrow in row) {
                yield return new AlphaSubrow(subrow);
            }
        }
    }
}

public interface IAlphaRow {
    public uint Row { get; }
    public ushort? Subrow { get; }
    public object ReadColumn(uint column);
}

public record AlphaRow(RawRow RawRow) : IAlphaRow {
    public uint Row => this.RawRow.RowId;
    public ushort? Subrow => null;

    public object ReadColumn(uint column) {
        return this.RawRow.ReadColumn((int) column);
    }
}

public record AlphaSubrow(RawSubrow RawSubrow) : IAlphaRow {
    public uint Row => this.RawSubrow.RowId;
    public ushort? Subrow => this.RawSubrow.SubrowId;

    public object ReadColumn(uint columnIdx) {
        return this.RawSubrow.ReadColumn((int) columnIdx);
    }
}
