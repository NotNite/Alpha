using Lumina.Excel;

namespace Alpha.Services.Excel;

public class ExcelScriptingGlobal<T>(ExcelSheet<T> sheet, T row)
    where T : struct, IExcelRow<T> {
    public ExcelSheet<T> Sheet { get; } = sheet;
    public T Row { get; } = row;
    public uint RowId { get; } = row.RowId;
}
