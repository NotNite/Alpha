using System.Numerics;
using Alpha.Core;
using ImGuiNET;
using NativeFileDialogSharp;
using Serilog;

namespace Alpha.Modules;

public class ExportModule : SimpleModule {
    private string _path = string.Empty;
    private bool _errored;

    public ExportModule() : base("File Export", "Data") { }

    private void BulkExport(string[] lines, string outDir) {
        foreach (var line in lines) {
            try {
                var file = Services.GameData.GetFile(line);
                var dir = Path.GetDirectoryName(line);
                var name = Path.GetFileName(line);

                Directory.CreateDirectory(Path.Combine(outDir, dir));
                File.WriteAllBytes(
                    Path.Combine(outDir, dir, name),
                    file.Data
                );
            } catch (Exception e) {
                Log.Error(e, "Failed to export file {Path}", line);
            }
        }
    }

    internal override void SimpleDraw() {
        if (ImGui.Button("Export file")) {
            var lines = this._path.Trim().Split("\n");
            if (lines.Length > 1) {
                var dir = Dialog.FolderPicker();
                if (dir?.Path is not null) {
                    this.BulkExport(lines, dir.Path);
                }

                return;
            }

            var file = Services.GameData.GetFile(this._path);

            if (file is null) {
                this._errored = true;
                return;
            }

            var extension = Path.GetExtension(this._path).Substring(1);
            var dialogResult = Dialog.FileSave(extension);
            if (dialogResult?.Path is not null) {
                var path = dialogResult.Path;
                if (!path.EndsWith(extension)) {
                    path += "." + extension;
                }

                File.WriteAllBytes(path, file.Data);
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Batch export from file")) {
            var dialogResult = Dialog.FileOpen();
            if (dialogResult?.Path is not null) {
                var lines = File.ReadAllLines(dialogResult.Path);
                var dir = Dialog.FolderPicker();
                if (dir?.Path is not null) {
                    this.BulkExport(lines, dir.Path);
                }
            }
        }

        if (this._errored) {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Couldn't save file - maybe it doesn't exist?");
        }

        ImGui.InputTextMultiline("Paths", ref this._path, 1024, ImGui.GetContentRegionAvail());
    }
}
