using System.Diagnostics;
using Alpha.Game;
using Alpha.Utils;
using Lumina.Data;
using Lumina.Misc;
using Microsoft.Extensions.Logging;

namespace Alpha.Services;

public class PathService(ILogger<PathService> logger, PathListService pathList) : IDisposable {
    public static readonly Dictionary<string, byte> RootCategories = new() {
        {"common", 0},
        {"bgcommon", 1},
        {"bg", 2},
        {"cut", 3},
        {"chara", 4},
        {"shader", 5},
        {"ui", 6},
        {"sound", 7},
        {"vfx", 8},
        {"ui_script", 9},
        {"exd", 0xA},
        {"game_script", 0xB},
        {"music", 0xC}
    };
    public static readonly Dictionary<byte, string> ReverseRootCategories =
        RootCategories.ToDictionary(kv => kv.Value, kv => kv.Key);
    // Has a subfolder for version, so `cut/ffxiv` and `cut/ex1` and whatnot
    public static readonly string[] HasSubfolder = ["bg", "cut", "music"];

    public Folder RootDirectory = new("root", 0);
    public readonly Dictionary<Dat, Dictionary<ulong, File>> Files = new();
    public readonly Dictionary<string, Folder> Folders = new();
    public readonly Dictionary<Dat, Dictionary<uint, string>> FolderNames = new();
    private readonly object processLock = new();
    public bool IsReady;

    public void SetGameData(AlphaGameData gameData) {
        this.IsReady = false;
        logger.LogInformation("Processing path lists with game data {GameData}", gameData.GamePath);
        Task.Run(() => {
            try {
                lock (this.processLock) {
                    this.Clear();
                    logger.LogInformation("Processing path lists");

                    var stopwatch = Stopwatch.StartNew();

                    this.LoadPathLists();
                    logger.LogInformation("Loaded path lists: {Time}", stopwatch.Elapsed);

                    this.LoadGameFiles(gameData);
                    logger.LogInformation("Sorting folders: {Time}", stopwatch.Elapsed);

                    logger.LogInformation("Finished processing path lists in {Time}", stopwatch.Elapsed);
                }
            } catch (Exception e) {
                logger.LogError(e, "Failed to process path lists");
            }
            this.IsReady = true;
        });
    }

    public void Clear() {
        this.RootDirectory = new Folder("root", 0);
        this.Files.Clear();
        this.FolderNames.Clear();
    }

    public void Dispose() {
        this.Clear();
    }

    private void LoadPathLists() {
        foreach (var path in pathList.LoadPathLists()) {
            var file = this.ParseResLogger(path.ToLower());
            var folder = this.GetFolder(file.FolderName!, true)!;
            folder.Files[file.FileHash] = file;

            if (!this.Files.TryGetValue(file.Dat!, out var files))
                this.Files[file.Dat!] = files = new Dictionary<ulong, File>();
            files[file.Hash] = file;
        }
    }

    private void LoadGameFiles(AlphaGameData gameData) {
        foreach (var repo in gameData.GameData.Repositories.Values)
        foreach (var cat in repo.Categories.Values.SelectMany(c => c)) {
            if (cat.IndexHashTableEntries is null) continue;

            var category = new Category(cat.CategoryId, (byte) cat.Expansion);
            var dats = new Dictionary<byte, Dat>();
            foreach (var (hash, data) in cat.IndexHashTableEntries) {
                if (!dats.TryGetValue(data.DataFileId, out var dat))
                    dats[data.DataFileId] = dat = new Dat(category, data.DataFileId);

                var file = new File(dat, hash);
                if (!this.Files.TryGetValue(file.Dat!, out var files))
                    this.Files[file.Dat!] = files = new Dictionary<ulong, File>();
                if (files.ContainsKey(file.Hash)) continue;

                if (!ReverseRootCategories.TryGetValue(cat.CategoryId, out var rootCategory)) continue;

                var folderName = rootCategory + "/"
                                              + (HasSubfolder.Contains(rootCategory)
                                                     ? cat.Expansion == 0 ? "ffxiv/" : "ex" + cat.Expansion + "/"
                                                     : "")
                                              + Util.PrintFileHash(file.FolderHash);
                if (this.FolderNames.TryGetValue(dat, out var names) &&
                    names.TryGetValue(file.FolderHash, out var newName))
                    folderName = newName;

                var folder = this.GetFolder(folderName, true)!;
                folder.Files[file.FileHash] = file;
                files[file.Hash] = file;
            }
        }
    }

    public Folder? GetFolder(string path, bool mkdir = false) {
        if (this.Folders.TryGetValue(path, out var existing)) return existing;

        var folders = path.Split('/');
        var current = this.RootDirectory;

        while (folders.Length > 0) {
            var folder = folders[0];
            folders = folders[1..];

            var folderHash = Crc32.Get(folder.ToLower());
            if (current.Folders.TryGetValue(folderHash, out var next)) {
                current = next;
                continue;
            }

            if (!mkdir) return null;

            next = new Folder(folder, folderHash);
            current.Folders[folderHash] = next;
            current = next;
        }

        return this.Folders[path] = current;
    }

    public IEnumerable<(string, File)> GetAllFiles(Folder folder, string path = "") {
        foreach (var file in folder.Files) yield return (path, file.Value);

        foreach (var subFolder in folder.Folders) {
            var newPath = string.IsNullOrEmpty(path) ? subFolder.Value.Name : path + "/" + subFolder.Value.Name;
            foreach (var file in this.GetAllFiles(subFolder.Value, newPath)) yield return file;
        }
    }

    public T? GetFile<T>(AlphaGameData gameData, File file) where T : FileResource {
        if (file.Path != null) return gameData.GameData.GetFile<T>(file.Path);

        if (file.Dat is null) return null;
        foreach (var repo in gameData.GameData.Repositories.Values)
        foreach (var cat in repo.Categories.Values.SelectMany(c => c)) {
            if (cat.IndexHashTableEntries is null) continue;
            if (cat.CategoryId != file.Dat.Category.Id || cat.Expansion != file.Dat.Category.Expansion)
                continue;
            return cat.GetFile<T>(file.Hash);
        }

        return null;
    }

    private File ParseResLogger(string line) {
        var firstComma = line.IndexOf(',');
        var lastComma = line.LastIndexOf(',');
        var indexId = int.Parse(line[..firstComma]);
        var path = line[(lastComma + 1)..];

        var category = new Category((byte) (indexId >> 16), (byte) ((indexId >> 8) & 0xFF));
        var dat = new Dat(category, (byte) (indexId & 0xFF));

        var file = new File(path, dat);

        if (!this.FolderNames.TryGetValue(dat, out var names))
            this.FolderNames[dat] = names = new Dictionary<uint, string>();
        names.TryAdd(file.FolderHash, file.FolderName!);

        return file;
    }

    public record Category(byte Id, byte Expansion);
    public record Dat(Category Category, byte Index);

    public record Folder(
        string Name,
        uint FolderHash
    ) {
        public Dictionary<uint, Folder> Folders = [];
        public Dictionary<uint, File> Files = [];
    }

    public record File(Dat? Dat, ulong Hash, string? FolderName = null, string? FileName = null) {
        public string? Path => FolderName != null && FileName != null ? (FolderName + "/" + FileName) : null;
        public uint FolderHash = (uint) (Hash >> 32);
        public uint FileHash = (uint) Hash;

        public File(string path, Dat? dat = null) : this(dat, Util.GetFullHash(path)) {
            this.FolderName = path[..path.LastIndexOf('/')];
            this.FileName = path[(path.LastIndexOf('/') + 1)..];
        }
    }
}
