using Alpha.Gui.Windows;

namespace Alpha.Services.Excel.Cells;

public abstract class Cell {
    public required uint Row;
    public ushort? Subrow;
    public required uint Column;
    public object? Data;

    public abstract void Draw(
        ExcelWindow window,
        bool inAnotherDraw = false
    );
}
