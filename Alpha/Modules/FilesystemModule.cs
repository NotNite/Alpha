using System.Diagnostics;
using System.Numerics;
using Alpha.Core;
using Alpha.Modules.Excel;
using Alpha.Utils;
using ImGuiNET;
using Lumina.Data;
using Lumina.Data.Files;
using NativeFileDialogSharp;

namespace Alpha.Modules;

[Module(DependsOn = new[] { "ResLoggerModule" })]
public class FilesystemModule : Module {
    // https://xiv.dev/data-files/sqpack
    private string _filter = string.Empty;

    private string[] _rootCategories = {
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

    private ResLoggerModule _reslogger;
    private Dictionary<string, List<string>> _cache = new();

    private string? _selectedPath;
    private FileResource? _selectedFile;
    private float _sidebarWidth = 300f;

    public FilesystemModule() : base("Filesystem Browser", "Data") {
        this._reslogger = Services.ModuleManager.GetModule<ResLoggerModule>();
    }

    internal override void Draw() {
        return;

        if (ImGui.Begin("Filesystem Browser")) {
            var temp = ImGui.GetCursorPosY();
            ImGui.SetNextItemWidth(this._sidebarWidth);
            if (ImGui.InputText("##FilesystemFilter", ref this._filter, 1024)) {
                this._cache.Clear();

                this._rootCategories = this._reslogger.CurrentPathCache
                    .Where(x => x.ToLower().Contains(this._filter.ToLower()))
                    .Select(x => x.Split("/").First())
                    .Distinct()
                    .ToArray();
            }

            var cra = ImGui.GetContentRegionAvail();
            ImGui.BeginChild("##FilesystemModule_Sidebar", cra with { X = this._sidebarWidth }, true);

            if (this._reslogger.CurrentPathCache.Count > 0) {
                foreach (var rootCategory in this._rootCategories) {
                    if (ImGui.TreeNode(rootCategory + "/")) {
                        this.RecursiveTree(rootCategory);
                        ImGui.TreePop();
                    }
                }
            } else {
                ImGui.Text("No ResLogger data :(");
            }

            ImGui.EndChild();

            ImGui.SameLine();
            ImGui.SetCursorPosY(temp);

            UiUtils.HorizontalSplitter(ref this._sidebarWidth);

            ImGui.SameLine();
            ImGui.SetCursorPosY(temp);

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

                ImGui.SameLine();

                if (ImGui.Button("Open in default app")) {
                    var tempFile = Path.GetTempFileName() + Path.GetExtension(this._selectedPath);
                    File.WriteAllBytes(tempFile, this._selectedFile.Data);

                    // TODO unix support
                    Process.Start("explorer.exe", $"\"{tempFile}\"");
                }

                if (this._selectedPath.EndsWith("exh")) {
                    if (ImGui.Button("Open in Excel browser")) {
                        var path = this._selectedPath
                            .Replace("exd/", string.Empty)
                            .Replace(".exh", string.Empty);
                        //Services.ModuleManager.GetModule<ExcelModule>().OpenSheet(path);
                    }
                }

                if (this._selectedPath.EndsWith("tex")) {
                    var texFile = (TexFile)this._selectedFile;
                    var size = new Vector2(texFile.Header.Width, texFile.Header.Height);
                    ImGui.Image(Services.ImageHandler.DisplayTex(texFile), size);
                }
            }

            ImGui.EndChild();
        }

        ImGui.End();
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
                    this.OpenFile(folder + "/" + item);
                }
            }
        }
    }

    public void OpenFile(string path) {
        //this.WindowOpen = true;
        this._selectedPath = path;

        if (path.EndsWith("tex")) {
            this._selectedFile = Services.GameData.GetFile<TexFile>(this._selectedPath);
        } else {
            this._selectedFile = Services.GameData.GetFile(this._selectedPath);
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
            if (this._filter.Trim() is not "" && !path.ToLower().Contains(this._filter.ToLower())) {
                continue;
            }

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
