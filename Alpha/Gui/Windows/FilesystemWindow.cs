using System.Numerics;
using Alpha.Game;
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
    public (FileResource, PathService.File, string)? SelectedFile;
    public FileResource? File;

    private string filter = string.Empty;
    private readonly List<string> filteredDirectories = [];
    private readonly List<string> visibleRootCategories = [..PathService.RootCategories.Keys];
    private readonly List<PathService.File> selectedFiles = new();
    private float sidebarWidth = 300f;

    private readonly GameDataService gameDataService;
    private readonly GuiService gui;
    private readonly PathService pathService;
    private readonly ILogger<FilesystemWindow> logger;

    public FilesystemWindow(
        GameDataService gameDataService,
        AlphaGameData gameData,
        GuiService gui,
        PathService pathService,
        ILogger<FilesystemWindow> logger
    ) {
        this.gameDataService = gameDataService;
        this.GameData = gameData;
        this.gui = gui;
        this.pathService = pathService;
        this.logger = logger;

        this.pathService.SetGameData(this.GameData);

        this.InitialSize = new Vector2(800, 600);
    }

    public void Dispose() {
        this.pathService.Dispose();
    }

    private void GameDataChanged() {
        this.SelectedFile = null;
        this.File = null;
    }

    protected override void Draw() {
        if (this.GameData is null) {
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
        if (!this.pathService.IsReady) {
            ImGui.TextUnformatted("Processing path lists...");
            return;
        }

        Components.DrawFakeHamburger(() => {
            if (Components.DrawGameDataPicker(this.gameDataService, this.GameData!) is { } newGameData) {
                this.GameData = newGameData;
                this.pathService.SetGameData(this.GameData);
                this.GameDataChanged();
            }

            var hasSelectedFiles = this.selectedFiles.Count > 0;
            if (!hasSelectedFiles) ImGui.BeginDisabled();
            if (ImGui.Button("Export selected files")) {
                var dir = NFD.PickFolder(string.Empty);
                if (!string.IsNullOrWhiteSpace(dir)) {
                    foreach (var file in this.selectedFiles) {
                        var resource = this.pathService.GetFile<FileResource>(this.GameData!, file);
                        if (resource is null) continue;

                        var path = Path.Combine(dir, this.GetPath(file));
                        var dirPath = Path.GetDirectoryName(path)!;
                        if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

                        System.IO.File.WriteAllBytes(path, resource.Data);
                    }
                }
            }
            if (!hasSelectedFiles) ImGui.EndDisabled();
        });

        ImGui.SameLine();

        ImGui.SetNextItemWidth(this.sidebarWidth - ImGui.GetCursorPosY());
        if (ImGui.InputText("##FilesystemWindow_Filter", ref this.filter, 1024, ImGuiInputTextFlags.EnterReturnsTrue)) {
            if (this.filter.Length > 0) {
                this.filteredDirectories.Clear();
                this.visibleRootCategories.Clear();

                foreach (var (folder, file) in this.pathService.GetAllFiles(this.pathService.RootDirectory)) {
                    var path = folder + "/" + (file.FileName ?? Util.PrintFileHash(file.FileHash));
                    if (path.Contains(this.filter, StringComparison.OrdinalIgnoreCase)) {
                        if (!this.filteredDirectories.Contains(folder)) {
                            this.filteredDirectories.Add(folder);
                            if (folder.Split('/').FirstOrDefault() is { } root &&
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
            foreach (var folder in this.pathService.RootDirectory.Folders
                         .Values
                         .OrderBy(x => PathService.RootCategories[x.Name])) {
                if (!this.visibleRootCategories.Contains(folder.Name)) continue;
                if (ImGui.TreeNode(folder.Name + "/")) {
                    this.DrawFolder(folder.Name, folder);
                    ImGui.TreePop();
                }
            }

            ImGui.EndChild();
        }
    }

    private string GetPath(PathService.File file) {
        if (file.Path != null) return file.Path;

        var folder = file.FolderName ??
                     (file.Dat != null
                          ? this.pathService.FolderNames.GetValueOrDefault(file.Dat)
                              ?.GetValueOrDefault(file.FolderHash)
                          : null)
                     ?? Util.PrintFileHash(file.FolderHash);

        var name = file.FileName ?? Util.PrintFileHash(file.FileHash);
        return $"{folder}/{name}";
    }

    private void DrawContent() {
        if (this.SelectedFile is null) return;

        if (ImGui.BeginChild("##FilesystemWindow_Content", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders)) {
            var (resource, file, filePath) = this.SelectedFile.Value;
            ImGui.TextUnformatted(filePath);
            ImGui.TextUnformatted($"{resource.Data.Length} bytes");

            if (ImGui.Button("Save file")) {
                var filename = Path.GetFileName(filePath);
                var dialogResult = NFD.SaveDialog(string.Empty, filename);
                if (!string.IsNullOrWhiteSpace(dialogResult)) System.IO.File.WriteAllBytes(dialogResult, resource.Data);
            }

            ImGui.SameLine();

            // TODO: Excel
            if (resource is TexFile texFile) {
                if (ImGui.Button("Export as .png")) Util.ExportAsPng(texFile);

                var size = new Vector2(texFile.Header.Width, texFile.Header.Height);
                size = Util.ClampImageSize(size, ImGui.GetContentRegionAvail());
                var img = this.gui.GetTexture(texFile);
                img.Draw(size);
            }

            ImGui.EndChild();
        }
    }

    private void DrawFolder(string path, PathService.Folder folder) {
        var filterExists = !string.IsNullOrWhiteSpace(this.filter);

        var folders = folder.Folders.Values.OrderBy(x => x.Name.StartsWith('~')).ThenBy(x => x.Name);
        var files = folder.Files.Values
            .Select(x => (File: x, Filename: x.FileName ?? Util.PrintFileHash(x.FileHash)))
            .OrderBy(x => x.File.FileName is null)
            .ThenBy(x => x.Filename);

        foreach (var childFolder in folders) {
            if (filterExists && folder.Name != null &&
                !this.filteredDirectories.Any(x => x.StartsWith(path + "/" + childFolder.Name))) continue;

            if (ImGui.TreeNode(childFolder.Name + "/")) {
                this.DrawFolder(path + "/" + childFolder.Name, childFolder);
                ImGui.TreePop();
            }
        }

        foreach (var (file, filename) in files) {
            if (filterExists && !this.GetPath(file).Contains(this.filter, StringComparison.OrdinalIgnoreCase)) continue;

            var selected = this.SelectedFile?.Item2 == file || this.selectedFiles.Contains(file);
            if (ImGui.Selectable(filename, selected)) {
                if (Util.IsKeyDown(ImGuiKey.LeftCtrl)) {
                    if (this.selectedFiles.Contains(file)) {
                        this.selectedFiles.Remove(file);
                    } else {
                        this.selectedFiles.Add(file);
                    }
                } else {
                    this.Open(file);
                }
            }
        }
    }

    public void Open(PathService.File file) {
        try {
            FileResource? resource = null;
            if (this.GameData != null) {
                if (file.Path?.EndsWith("tex") == true)
                    resource = this.pathService.GetFile<TexFile>(this.GameData, file);
                else
                    resource = this.pathService.GetFile<FileResource>(this.GameData, file);
            }

            if (resource is null) throw new Exception("File resource is null");
            this.SelectedFile = (resource, file, this.GetPath(file));
        } catch (Exception e) {
            this.logger.LogError(e, "Failed to open file");
        }
    }
}
