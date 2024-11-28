using System.Diagnostics.CodeAnalysis;
using Alpha.Gui.Windows;
using Hexa.NET.ImGui;

namespace Alpha.Services.Excel.Cells;

public class EmptyCell : Cell {
    [SetsRequiredMembers]
    public EmptyCell(uint row, ushort? subrow, uint column) {
        this.Row = row;
        this.Subrow = subrow;
        this.Column = column;
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        ImGui.TextUnformatted(string.Empty);
    }
}
