using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Alpha.Gui.Windows;
using Alpha.Utils;
using ImGuiNET;
using Lumina.Data.Files;
using Microsoft.Extensions.DependencyInjection;

namespace Alpha.Services.Excel.Cells;

public class IconCell : Cell {
    public const string CopyIconId = "Copy icon ID";
    public const string CopyIconPath = "Copy icon path";
    public const string OpenInFilesystemBrowser = "Open in filesystem browser";
    public const string SaveTex = "Save (.tex)";
    public const string SavePng = "Save (.png)";

    private TexFile? texFile;
    private GuiService? gui;
    private Config config;
    private uint id;
    private string? loadFailureStr;
    private string rowColStr;

    [SetsRequiredMembers]
    public IconCell(int row, int column, object? data) {
        this.Row = row;
        this.Column = column;
        this.Data = data;
        this.rowColStr = $"{this.Row}_{this.Column}";

        this.config = Program.Host.Services.GetRequiredService<Config>();

        try {
            this.id = Convert.ToUInt32(this.Data);
        } catch {
            this.loadFailureStr = "(couldn't load icon)";
            return;
        }

        var gameData = Program.Host.Services.GetRequiredService<GameDataService>();
        this.texFile = gameData.GetIcon(this.id);

        if (this.texFile is not null) {
            this.gui = Program.Host.Services.GetRequiredService<GuiService>();
        } else {
            this.loadFailureStr = $"(couldn't load icon {this.id})";
        }
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        if (this.texFile is null) {
            ImGui.BeginDisabled();
            ImGui.TextUnformatted(this.loadFailureStr);
            ImGui.EndDisabled();
            return;
        }

        var icon = this.gui!.GetTexture(this.texFile);
        var lineSize = this.ScaleSize(icon.Size, config.LineHeightImages
                                                     ? ImGui.GetTextLineHeight() * 2
                                                     : 512);

        const int maxY = 512;
        if (inAnotherDraw) {
            var shouldShowMagnum = ImGui.IsKeyDown(ImGuiKey.ModAlt);
            if (shouldShowMagnum) {
                var magnumSize = ScaleSize(icon.Size, maxY);
                ImGui.Image(icon.Handle, magnumSize);
            } else {
                ImGui.Image(icon.Handle, lineSize);
            }
        } else {
            ImGui.Image(icon.Handle, lineSize);
            var shouldShowMagnum = ImGui.IsKeyDown(ImGuiKey.ModAlt) && (ImGui.IsItemHovered() || inAnotherDraw);
            if (shouldShowMagnum) {
                var magnumSize = ScaleSize(icon.Size, maxY);
                ImGui.BeginTooltip();
                ImGui.Image(icon.Handle, magnumSize);
                ImGui.EndTooltip();
            }
        }

        if (ImGui.BeginPopupContextItem(this.rowColStr)) {
            var path = this.texFile.FilePath;
            ImGui.MenuItem(path, false);

            if (ImGui.MenuItem(CopyIconId)) {
                ImGui.SetClipboardText(this.id.ToString());
            }

            if (ImGui.MenuItem(CopyIconPath)) {
                ImGui.SetClipboardText(path);
            }

            if (ImGui.MenuItem(OpenInFilesystemBrowser)) {
                var windowManager = Program.Host.Services.GetRequiredService<WindowManagerService>();
                var filesystemWindow = windowManager.CreateWindow<FilesystemWindow>();
                filesystemWindow.Open(new PathService.File(path));
            }

            if (ImGui.MenuItem(SaveTex)) {
                Util.ExportAsTex(this.texFile);
            }

            if (ImGui.MenuItem(SavePng)) {
                Util.ExportAsPng(this.texFile);
            }

            ImGui.EndPopup();
        }
    }

    private Vector2 ScaleSize(Vector2 size, float maxY) {
        if (size.Y > maxY) size *= maxY / size.Y;
        return size;
    }
}
