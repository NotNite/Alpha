using Alpha.Services.Excel.Cells;
using Lumina.Excel;

namespace Alpha.Services.Excel;

public abstract class SheetDefinition {
    public abstract int? DefaultColumn { get; }

    public abstract string? GetNameForColumn(int index);
    public abstract int? GetColumnForName(string name);
    public abstract Cell? GetCell(ExcelService excel, RawExcelSheet sheet, int row, int column, object? data);
}
