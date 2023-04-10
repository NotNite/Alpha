using Alpha.Core;
using Alpha.Utils;
using ImGuiNET;
using Lumina.Data;
using NativeFileDialogSharp;

namespace Alpha.Modules;

public class FilesystemModule : Module {
    // https://xiv.dev/data-files/sqpack
    private readonly string[] _rootCategories = {
        "common",
        "bgcommon",
        "bg",
        "cut",
        "chara",
        "shader",
        "ui",
        "sound",
        "vfx",
        "ui_script",
        "exd",
        "game_script",
        "music"
    };

    private ResLoggerModule? _reslogger;
    private Dictionary<string, List<string>> _cache = new();

    private string? _selectedPath;
    private FileResource? _selectedFile;
    private float _sidebarWidth = 300f;


    public FilesystemModule() : base("Filesystem Browser", "Data") { }


    internal override void Draw() {
        this._reslogger ??= Services.ModuleManager.GetModule<ResLoggerModule>();

        var cra = ImGui.GetContentRegionAvail();

        ImGui.BeginChild("##FilesystemModule_Sidebar", cra with { X = this._sidebarWidth }, true);

        if (this._reslogger.CurrentPathCache.Count > 0) {
            foreach (var rootCategory in this._rootCategories) {
                if (ImGui.TreeNode(rootCategory)) {
                    this.RecursiveTree(rootCategory);
                    ImGui.TreePop();
                }
            }
        } else {
            ImGui.Text("No ResLogger data :(");
        }

        ImGui.EndChild();


        ImGui.SameLine();
        UiUtils.HorizontalSplitter(ref this._sidebarWidth);
        ImGui.SameLine();

        ImGui.BeginChild("##FilesystemModule_Content", ImGui.GetContentRegionAvail(), true);
        if (this._selectedFile is not null) {
            ImGui.Text(this._selectedPath);
            ImGui.Text($"{this._selectedFile.Data.Length} bytes");

            if (ImGui.Button("Save file")) {
                var extension = Path.GetExtension(this._selectedPath).Substring(1);
                var dialogResult = Dialog.FileSave(extension);
                if (dialogResult?.Path is not null) {
                    var path = dialogResult.Path;
                    if (!path.EndsWith(extension)) {
                        path += "." + extension;
                    }

                    File.WriteAllBytes(path, this._selectedFile.Data);
                }
            }
        } else {
            ImGui.Text("No item selected :(");
        }

        ImGui.EndChild();
    }

    private void RecursiveTree(string folder) {
        foreach (var item in this.GetContentsOfDirectory(folder)) {
            if (item.EndsWith("/")) {
                if (ImGui.TreeNode(item)) {
                    this.RecursiveTree(folder + "/" + item[..^1]);
                    ImGui.TreePop();
                }
            } else {
                if (ImGui.Selectable(item)) {
                    this._selectedPath = folder + "/" + item;
                    this._selectedFile = Services.GameData.GetFile(this._selectedPath);
                }
            }
        }
    }

    private List<string> GetContentsOfDirectory(string directory) {
        if (this._cache.ContainsKey(directory)) {
            return this._cache[directory];
        }

        var partCount = directory.Split("/").Length;

        var retFolder = new List<string>();
        var retFile = new List<string>();

        foreach (var path in this._reslogger!.CurrentPathCache) {
            if (path.StartsWith(directory) && path.Split("/").Length == partCount + 1) {
                var fileName = path.Split("/").Last();
                if (!retFile.Contains(fileName)) {
                    retFile.Add(fileName);
                }
            }

            if (path.StartsWith(directory) && path.Split("/").Length > partCount + 1) {
                var folderName = path.Split("/")[partCount + 1 - 1] + "/";

                if (!retFolder.Contains(folderName)) {
                    retFolder.Add(folderName);
                }
            }
        }

        retFolder.Sort();
        retFile.Sort();

        var ret = retFolder.Concat(retFile).ToList();
        this._cache.Add(directory, ret);
        return ret;
    }
}
