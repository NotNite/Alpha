using Alpha.Services.Excel.Cells;
using Lumina.Excel;

namespace Alpha.Services.Excel;

public abstract class SheetDefinition {
    public abstract uint? DefaultColumn { get; }

    public abstract string? GetNameForColumn(uint index);
    public abstract uint? GetColumnForName(string name);
    public abstract Cell? GetCell(ExcelService excel, IAlphaSheet sheet, uint row, ushort? subrow, uint column, object? data);
}
