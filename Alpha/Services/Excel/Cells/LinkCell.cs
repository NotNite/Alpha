using System.Diagnostics.CodeAnalysis;
using Alpha.Gui.Windows;
using Alpha.Utils;
using Hexa.NET.ImGui;
using Lumina.Excel;

namespace Alpha.Services.Excel.Cells;

public class LinkCell : Cell {
    public const string OpenInNewWindow = "Open in new window";

    private IAlphaSheet target;
    private uint targetRow;
    private uint targetCol;
    private string text;
    private string rowColStr;

    [SetsRequiredMembers]
    public LinkCell(uint row, uint column, object? data, IAlphaSheet target, uint targetCol) {
        this.Row = row;
        this.Column = column;
        this.Data = data;
        this.target = target;
        this.targetCol = targetCol;
        this.rowColStr = $"{this.Row}_{this.Column}";

        try {
            this.targetRow = Convert.ToUInt32(this.Data);
        } catch {
            // ignored
        }

        this.text = $"{this.target.Name}#{this.targetRow}##{this.rowColStr}";
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        if (inAnotherDraw && Util.IsKeyDown(ImGuiKey.ModAlt)) {
            window.DrawCell(this.target, this.targetRow, null, this.targetCol, inAnotherDraw: true);
            return;
        }

        if (ImGui.Button(this.text)) {
            window.OpenSheet(this.target, ((uint) this.targetRow, null));
        }

        if (ImGui.BeginPopupContextItem(this.rowColStr)) {
            if (ImGui.MenuItem(OpenInNewWindow)) {
                window.GetExcelService().OpenNewWindow(this.target, ((uint) this.targetRow, null));
            }

            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered()) {
            ImGui.BeginTooltip();
            window.DrawCell(this.target, this.targetRow, null, this.targetCol, inAnotherDraw: true);
            ImGui.EndTooltip();
        }
    }
}
