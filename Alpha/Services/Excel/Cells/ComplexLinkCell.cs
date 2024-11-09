using System.Diagnostics.CodeAnalysis;
using Alpha.Gui.Windows;
using Alpha.Utils;
using Hexa.NET.ImGui;
using Lumina.Excel;

namespace Alpha.Services.Excel.Cells;

public class ComplexLinkCell : Cell {
    public const string OpenInNewWindow = "Open in new window";

    private List<(RawExcelSheet, int, int)> links;

    [SetsRequiredMembers]
    public ComplexLinkCell(int row, int column, object? data, List<(RawExcelSheet, int, int)> links) {
        this.Row = row;
        this.Column = column;
        this.Data = data;
        this.links = links;
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        if (inAnotherDraw && Util.IsKeyDown(ImGuiKey.ModAlt)) {
            foreach (var (sheet, row, col) in this.links) {
                window.DrawCell(sheet, row, col, inAnotherDraw: true);
            }
            return;
        }

        foreach (var (sheet, row, col) in this.links) {
            var text = $"{sheet.Name}#{row}" + $"##{this.Row}_{this.Column}_{sheet.Name}_{row}";
            if (ImGui.Button(text)) {
                window.OpenSheet(sheet, row);
            }

            if (ImGui.BeginPopupContextItem($"{this.Row}_{this.Column}_{sheet.Name}_{row}")) {
                if (ImGui.MenuItem(OpenInNewWindow)) {
                    window.GetExcelService().OpenNewWindow(sheet, row);
                }

                ImGui.EndPopup();
            }

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                window.DrawCell(sheet, row, col, inAnotherDraw: true);
                ImGui.EndTooltip();
            }
        }
    }
}
