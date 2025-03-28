using Alpha.Services.Excel.Cells;

namespace Alpha.Services.Excel;

public interface ISheetDefinition {
    public uint? DefaultColumn { get; }
    public bool Ready { get; }

    public void Init(ExcelService excel, IAlphaSheet sheet);
    public string? GetNameForColumn(uint column);

    public Cell? GetCell(
        ExcelService excel, IAlphaSheet sheet, uint row, ushort? subrow, uint column, object? data
    );
}
