using System.Diagnostics.CodeAnalysis;
using Alpha.Gui.Windows;
using Alpha.Utils;
using Hexa.NET.ImGui;
using Lumina.Excel;

namespace Alpha.Services.Excel.Cells;

public class ComplexLinkCell : Cell {
    public const string OpenInNewWindow = "Open in new window";

    private List<(IAlphaSheet, uint, ushort?, uint)> links;

    [SetsRequiredMembers]
    public ComplexLinkCell(uint row, ushort? subrow, uint column, object? data, List<(IAlphaSheet, uint, ushort?, uint)> links) {
        this.Row = row;
        this.Column = column;
        this.Data = data;
        this.links = links;
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        if (inAnotherDraw && Util.IsKeyDown(ImGuiKey.ModAlt)) {
            foreach (var (sheet, row, subrow, col) in this.links) {
                window.DrawCell(sheet, row, subrow, col, inAnotherDraw: true);
            }
            return;
        }

        foreach (var (sheet, row, subrow, col) in this.links) {
            var rowStr = subrow is null ? row.ToString() : $"{row}.{subrow}";
            var text = $"{sheet.Name}#{rowStr}" + $"##{this.Row}_{this.Subrow}_{this.Column}_{sheet.Name}_{row}";
            if (ImGui.Button(text)) {
                window.OpenSheet(sheet, ((uint) row, null));
            }

            if (ImGui.BeginPopupContextItem($"{this.Row}_{this.Column}_{sheet.Name}_{row}")) {
                if (ImGui.MenuItem(OpenInNewWindow)) {
                    window.GetExcelService().OpenNewWindow(sheet, ((uint) row, null));
                }

                ImGui.EndPopup();
            }

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                window.DrawCell(sheet, row, subrow, col, inAnotherDraw: true);
                ImGui.EndTooltip();
            }
        }
    }
}
