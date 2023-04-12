using Lumina.Excel;

namespace Alpha.Modules.Excel;

public class ExcelScriptingGlobal<T> where T : ExcelRow {
    public ExcelSheet<T> Sheet { get; }
    public T Row { get; }
    public uint RowId { get; }
    public uint SubRowId { get; }

    public ExcelScriptingGlobal(ExcelSheet<T> sheet, T row) {
        Sheet = sheet;
        Row = row;
        RowId = row.RowId;
        SubRowId = row.SubRowId;
    }
}
