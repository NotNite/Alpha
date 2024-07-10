using Alpha.Gui.Windows;

namespace Alpha.Services.Excel.Cells;

public abstract class Cell {
    public required int Row;
    public required int Column;
    public object? Data;

    public abstract void Draw(
        ExcelWindow window,
        bool inAnotherDraw = false
    );
}
