using System.Numerics;
using Alpha.Core;
using ImGuiNET;
using NativeFileDialogSharp;

namespace Alpha.Modules;

public class ExportModule : Module {
    private string _path = string.Empty;
    private bool _errored;

    public ExportModule() : base("File Export", "Data") { }

    internal override void Draw() {
        return;

        if (ImGui.Begin("File Export")) {
            ImGui.InputText("Path", ref this._path, 1024);

            if (ImGui.Button("Export file")) {
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

            if (this._errored) {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Couldn't save file - maybe it doesn't exist?");
            }
        }

        ImGui.End();
    }
}
