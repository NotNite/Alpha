using System.Numerics;
using Alpha.Services;
using Alpha.Utils;
using Hexa.NET.ImGui;
using Lumina.Data;
using Lumina.Data.Files;
using Microsoft.Extensions.Logging;
using NativeFileDialog.Extended;

namespace Alpha.Gui.Windows;

[Window("Filesystem")]
public class FilesystemWindow : Window, IDisposable {
    public (FileResource, PathService.File)? SelectedFile;
    public FileResource? File;

    private string filter = string.Empty;
    private readonly List<string> filteredDirectories = [];
    private readonly List<string> visibleRootCategories = [..PathService.RootCategories.Keys];
    private float sidebarWidth = 300f;

    private readonly GameDataService gameData;
    private readonly GuiService gui;
    private readonly PathService pathService;
    private readonly ILogger<FilesystemWindow> logger;

    public FilesystemWindow(
        GameDataService gameData,
        GuiService gui,
        PathService pathService,
        ILogger<FilesystemWindow> logger
    ) {
        this.gameData = gameData;
        this.gui = gui;
        this.pathService = pathService;
        this.logger = logger;

        this.gameData.OnGameDataChanged += this.GameDataChanged;

        this.InitialSize = new Vector2(800, 600);
    }

    public void Dispose() {
        this.gameData.OnGameDataChanged -= this.GameDataChanged;
    }

    private void GameDataChanged() {
        this.SelectedFile = null;
        this.File = null;
    }

    protected override void Draw() {
        if (this.gameData.GameData is null) {
            ImGui.TextUnformatted("No game data loaded.");
            return;
        }

        var temp = ImGui.GetCursorPosY();
        this.DrawSidebar();
        ImGui.SameLine();

        ImGui.SetCursorPosY(temp);
        Components.DrawHorizontalSplitter(ref this.sidebarWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        this.DrawContent();
    }

    private void DrawSidebar() {
        ImGui.SetNextItemWidth(this.sidebarWidth);
        if (ImGui.InputText("##FilesystemWindow_Filter", ref this.filter, 1024, ImGuiInputTextFlags.EnterReturnsTrue)) {
            if (this.filter.Length > 0) {
                this.filteredDirectories.Clear();
                this.visibleRootCategories.Clear();
                foreach (var files in this.pathService.Files.Values) {
                    foreach (var file in files.Values) {
                        var path = file.Path ?? Util.PrintFileHash(file.Hash);
                        if (path.Contains(this.filter, StringComparison.OrdinalIgnoreCase)) {
                            var dir = path.AsSpan(0, path.LastIndexOf('/')).ToString();
                            if (!this.filteredDirectories.Contains(dir)) this.filteredDirectories.Add(dir);
                            if (dir.Split('/').FirstOrDefault() is { } root &&
                                !this.visibleRootCategories.Contains(root))
                                this.visibleRootCategories.Add(root);
                        }
                    }
                }
            } else {
                this.filteredDirectories.Clear();
                this.visibleRootCategories.Clear();
                this.visibleRootCategories.AddRange(PathService.RootCategories.Keys);
            }
        }

        if (ImGui.BeginChild("##FilesystemWindow_Sidebar", ImGui.GetContentRegionAvail() with {X = this.sidebarWidth},
                ImGuiChildFlags.Borders)) {
            foreach (var name in this.visibleRootCategories) {
                var id = PathService.RootCategories[name];
                if (ImGui.TreeNode(name + "/")) {
                    this.DrawFolder(name, new PathService.Category(id, 0), 0);
                    ImGui.TreePop();
                }
            }

            ImGui.EndChild();
        }
    }

    private void DrawContent() {
        if (this.SelectedFile is null) return;

        if (ImGui.BeginChild("##FilesystemWindow_Content", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders)) {
            var (resource, file) = this.SelectedFile.Value;
            var filePath = file.Path ?? Util.PrintFileHash(file.Hash);
            ImGui.TextUnformatted(filePath);
            ImGui.TextUnformatted($"{resource.Data.Length} bytes");

            if (ImGui.Button("Save file")) {
                var filename = Path.GetFileName(filePath);
                var dialogResult = NFD.SaveDialog(string.Empty, filename);
                if (!string.IsNullOrWhiteSpace(dialogResult)) {
                    System.IO.File.WriteAllBytes(dialogResult, resource.Data);
                }
            }

            ImGui.SameLine();

            // TODO: Excel
            if (resource is TexFile texFile) {
                if (ImGui.Button("Export as .png")) {
                    Util.ExportAsPng(texFile);
                }

                var size = new Vector2(texFile.Header.Width, texFile.Header.Height);
                size = Util.ClampImageSize(size, ImGui.GetContentRegionAvail());
                var img = this.gui.GetTexture(texFile);
                img.Draw(size);
            }

            ImGui.EndChild();
        }
    }

    private void DrawFolder(string path, PathService.Category cat, int depth) {
        var filterExists = !string.IsNullOrWhiteSpace(this.filter);
        var dir = this.pathService.GetDirectory(cat, path, depth == 0);

        var folders = new Dictionary<string, List<PathService.File>?>();
        foreach (var folder in dir.Folders) folders[folder] = null;

        if (depth == 0 || (cat.Expansion != 0 && depth == 1)) {
            // Either the root of a cat (FFXIV files can be here) or the root of an expansion folder
            var unk = this.pathService.GetUnknownFiles(cat);
            foreach (var (folder, files) in unk) {
                folders[Util.PrintFileHash(folder)] = files;
            }
        }

        foreach (var (folder, files) in folders
                     .OrderBy(x => x.Key.StartsWith('~') ? 1 : 0)
                     .ThenBy(x => x.Key)
                ) {
            if (filterExists && !this.filteredDirectories.Any(x => x.StartsWith(path + "/" + folder))) continue;

            if (ImGui.TreeNode(folder + "/")) {
                if (files != null) {
                    foreach (var file in files) {
                        if (filterExists) {
                            var filePath = file.Path ?? Util.PrintFileHash(file.FileHash);
                            if (!filePath.Contains(this.filter, StringComparison.OrdinalIgnoreCase)) continue;
                        }

                        if (ImGui.Selectable(file.Name ?? Util.PrintFileHash(file.FileHash))) {
                            this.Open(file);
                        }
                    }
                }

                if (depth == 0 && folder.StartsWith("ex") && int.TryParse(folder[2..], out var ex)) {
                    this.DrawFolder(path + "/" + folder, cat with {Expansion = (byte) ex}, depth + 1);
                } else {
                    this.DrawFolder(path + "/" + folder, cat, depth + 1);
                }
                ImGui.TreePop();
            }
        }

        foreach (var (file, name) in dir.Files
                     .Select(x => (x, x.Name ?? Util.PrintFileHash(x.FileHash)))
                     .OrderBy(x => x.Item2.StartsWith('~') ? 1 : 0)
                     .ThenBy(x => x.Item2)
                ) {
            if (filterExists) {
                var filePath = file.Path ?? Util.PrintFileHash(file.FileHash);
                if (!filePath.Contains(this.filter, StringComparison.OrdinalIgnoreCase)) continue;
            }

            if (ImGui.Selectable(name)) {
                this.Open(file);
            }
        }
    }

    public void Open(PathService.File file) {
        try {
            FileResource? resource;
            if (file.Path?.EndsWith("tex") == true) {
                resource = this.pathService.GetFile<TexFile>(file);
            } else {
                resource = this.pathService.GetFile<FileResource>(file);
            }

            if (resource is null) throw new Exception("File resource is null");
            this.SelectedFile = (resource, file);
        } catch (Exception e) {
            this.logger.LogError(e, "Failed to open file");
        }
    }
}
