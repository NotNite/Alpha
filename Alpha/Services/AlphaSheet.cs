using System.Diagnostics.CodeAnalysis;
using Lumina.Excel;

namespace Alpha.Services;

[method: SetsRequiredMembers]
public record AlphaSheet(ExcelSheet<RawRow> Sheet, string Name) {
    public ExcelSheet<RawRow> Sheet = Sheet;
    public required string Name = Name;

    public RawRow? GetRow(uint row) {
        try {
            if (row == uint.MaxValue) return null;
            return this.Sheet.GetRow(row);
        } catch {
            return null;
        }
    }
}
