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
    public int Count => this.Sheet.Count;

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

    // They forgot to implement ReadColumn lol
    public object ReadColumn(uint columnIdx) {
        var data = this.RawSubrow.Page.Sheet.Columns[(int) columnIdx];
        return data.Type switch {
            ExcelColumnDataType.String => this.RawSubrow.ReadString(data.Offset),
            ExcelColumnDataType.Bool => this.RawSubrow.ReadBool(data.Offset),
            ExcelColumnDataType.Int8 => this.RawSubrow.ReadInt8(data.Offset),
            ExcelColumnDataType.UInt8 => this.RawSubrow.ReadUInt8(data.Offset),
            ExcelColumnDataType.Int16 => this.RawSubrow.ReadInt16(data.Offset),
            ExcelColumnDataType.UInt16 => this.RawSubrow.ReadUInt16(data.Offset),
            ExcelColumnDataType.Int32 => this.RawSubrow.ReadInt32(data.Offset),
            ExcelColumnDataType.UInt32 => this.RawSubrow.ReadUInt32(data.Offset),
            ExcelColumnDataType.Float32 => this.RawSubrow.ReadFloat32(data.Offset),
            ExcelColumnDataType.Int64 => this.RawSubrow.ReadInt64(data.Offset),
            ExcelColumnDataType.UInt64 => this.RawSubrow.ReadUInt64(data.Offset),
            >= ExcelColumnDataType.PackedBool0 and <= ExcelColumnDataType.PackedBool7 =>
                this.RawSubrow.Page.ReadPackedBool(data.Offset, (byte) (data.Type - ExcelColumnDataType.PackedBool0)),
            _ => throw new InvalidOperationException($"Unknown column type {data.Type}")
        };
    }
}
