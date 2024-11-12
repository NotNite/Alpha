using System.Diagnostics.CodeAnalysis;
using Alpha.Gui.Windows;
using Hexa.NET.ImGui;
using Microsoft.Extensions.DependencyInjection;

namespace Alpha.Services.Excel.Cells;

public class DefaultCell : Cell {
    public const string Null = "(null)";
    public const string OpenInFilesystemBrowser = "Open in filesystem browser";
    public const string Copy = "Copy";

    private string? str;
    private bool? fileExists;
    private string rowColStr;

    [SetsRequiredMembers]
    public DefaultCell(uint row, ushort? subrow, uint column, object? data) {
        this.Row = row;
        this.Subrow = subrow;
        this.Column = column;
        this.Data = data;
        this.rowColStr = $"{this.Row}_{this.Column}";

        try {
            this.str = this.Data?.ToString();
        } catch {
            // ignored
        }
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        if (this.str is not null)
            ImGui.TextUnformatted(this.str);
        else {
            ImGui.BeginDisabled();
            ImGui.TextUnformatted(Null);
            ImGui.EndDisabled();
        }

        if (this.str is null) return;

        if (ImGui.BeginPopupContextItem(this.rowColStr)) {
            if (this.fileExists is null) {
                if (this.str is not null) {
                    try {
                        this.fileExists = window.GameData?.GameData.FileExists(this.str) ?? false;
                    } catch {
                        this.fileExists = false;
                    }
                }
            }

            if (this.fileExists == true && ImGui.MenuItem(OpenInFilesystemBrowser)) {
                var windowManager = Program.Host.Services.GetRequiredService<WindowManagerService>();
                var filesystemWindow = windowManager.CreateWindow<FilesystemWindow>();
                filesystemWindow.Open(new PathService.File(this.str!));
            }

            if (ImGui.MenuItem(Copy)) ImGui.SetClipboardText(this.str);

            ImGui.EndPopup();
        }
    }
}
