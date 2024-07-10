using System.Diagnostics;
using System.IO.Compression;
using Alpha.Utils;
using Lumina.Data;
using Microsoft.Extensions.Logging;

namespace Alpha.Services;

public class PathService : IDisposable {
    private const string PathListsDirectory = "pathlists";
    public static Dictionary<string, byte> RootCategories = new() {
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

    public readonly Dictionary<string, int> PathLists = new();
    public readonly Dictionary<Dat, Dictionary<ulong, File>> Files = new();
    public readonly Dictionary<Dat, List<uint>> Folders = new();
    public readonly Dictionary<string, Directory> Directories = new();
    public readonly Dictionary<Category, Dictionary<uint, List<File>>> UnknownFiles = new();

    private readonly ILogger<PathService> logger;
    private readonly GameDataService gameData;

    public bool IsDownloading => this.isDownloading;
    private bool isDownloading;

    public PathService(ILogger<PathService> logger, GameDataService gameData) {
        this.logger = logger;
        this.gameData = gameData;

        this.LoadPathLists();
        this.ProcessPathLists();
        this.gameData.OnGameDataChanged += this.ProcessPathLists;
    }

    public void Dispose() {
        this.gameData.OnGameDataChanged -= this.ProcessPathLists;
    }

    public void ProcessPathLists() {
        if (this.gameData.GameData is null) return;
        var stopwatch = Stopwatch.StartNew();

        foreach (var repo in this.gameData.GameData.Repositories.Values) {
            foreach (var cat in repo.Categories.Values.SelectMany(c => c)) {
                if (cat.IndexHashTableEntries is null) continue;

                var category = new Category(cat.CategoryId, (byte) cat.Expansion);
                var dats = new Dictionary<byte, Dat>();
                foreach (var (hash, data) in cat.IndexHashTableEntries) {
                    if (!dats.TryGetValue(data.DataFileId, out var dat)) {
                        dats[data.DataFileId] = dat = new Dat(category, data.DataFileId);
                    }
                    if (!this.Files.TryGetValue(dat, out var files)) {
                        this.Files[dat] = files = new();
                    }
                    if (files.ContainsKey(hash)) continue;
                    var file = files[hash] = new File(dat, hash);

                    if (!this.Folders.ContainsKey(dat)) this.Folders[dat] = new();
                    if (!this.Folders[dat].Contains(file.FolderHash)) this.Folders[dat].Add(file.FolderHash);
                }
            }
        }

        var count = this.Files.Values.Sum(f => f.Count);
        this.logger.LogInformation("Processed {FileCount} files in {Time}", count, stopwatch.Elapsed);
    }

    public Directory GetDirectory(
        Category category,
        string path,
        bool skipEx = false
    ) {
        if (this.Directories.TryGetValue(path, out var dir)) return dir;

        var folders = new List<string>();
        var files = new List<File>();

        var folderHash = Lumina.Misc.Crc32.Get(path);

        foreach (var (iDat, iFiles) in this.Files) {
            if (skipEx ? iDat.Category.Id != category.Id : iDat.Category != category) continue;
            foreach (var file in iFiles.Values) {
                if (file.Path?.StartsWith(path + "/") == true) {
                    var subPath = file.Path!.Substring(path.Length + 1);
                    if (subPath.Contains('/')) {
                        var folder = subPath.Split('/')[0];
                        if (!folders.Contains(folder)) folders.Add(folder);
                    } else {
                        files.Add(file);
                    }
                } else if (file.FolderHash == folderHash) {
                    files.Add(file);
                }
            }
        }

        return this.Directories[path] = new Directory(folders, files);
    }

    public Dictionary<uint, List<File>> GetUnknownFiles(Category category) {
        if (this.UnknownFiles.TryGetValue(category, out var files)) return files;

        return this.UnknownFiles[category] = this.Files
                   .Where(f => f.Key.Category == category)
                   .SelectMany(f => f.Value.Values)
                   .Where(x => x.Path is null && x.Dat is not null &&
                               (!this.Folders.ContainsKey(x.Dat) || !this.Folders[x.Dat].Contains(x.FolderHash)))
                   .GroupBy(f => f.FolderHash)
                   .ToDictionary(g => g.Key, g => g.ToList());
    }

    private void LoadPathLists() {
        var stopwatch = Stopwatch.StartNew();

        var pathDir = Path.Combine(Program.AppDir, PathListsDirectory);
        if (!System.IO.Directory.Exists(pathDir)) System.IO.Directory.CreateDirectory(pathDir);

        foreach (var path in System.IO.Directory.EnumerateFiles(pathDir)) {
            try {
                var i = 0;
                using var reader = new StreamReader(path);
                reader.ReadLine(); // skip header
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine()!.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var file = this.ParseResLogger(line);
                    if (!this.Files.ContainsKey(file.Dat!)) this.Files[file.Dat!] = new();
                    this.Files[file.Dat!][file.Hash] = file;
                    i++;
                }

                this.PathLists[Path.GetFileName(path)] = i;
            } catch (Exception e) {
                this.logger.LogError(e, "Failed to load paths from {PathFile}", path);
            }
        }

        this.logger.LogInformation("Loaded {PathCount} paths in {Time}", this.Files.Values.Sum(f => f.Count),
            stopwatch.Elapsed);
    }

    public async Task DownloadResLogger(bool currentOnly) {
        this.isDownloading = true;
        try {
            var filename = currentOnly ? "CurrentPathListWithHashes.gz" : "PathListWithHashes.gz";
            var url = $"https://rl2.perchbird.dev/download/export/{filename}";

            using var client = new HttpClient();
            var req = await client.GetStreamAsync(url);
            using var gzip = new GZipStream(req, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            var filenameOutput = currentOnly ? "reslogger-current.csv" : "reslogger.csv";
            var outputFile = Path.Combine(Program.AppDir, PathListsDirectory, filenameOutput);
            if (System.IO.File.Exists(outputFile)) System.IO.File.Delete(outputFile);

            var i = 0;
            using var writer = new StreamWriter(outputFile);
            reader.ReadLine(); // skip header
            while (!reader.EndOfStream) {
                var line = reader.ReadLine()!.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                writer.WriteLine(line);

                var file = this.ParseResLogger(line);
                if (!this.Files.ContainsKey(file.Dat!)) this.Files[file.Dat!] = new();
                this.Files[file.Dat!][file.Hash] = file;

                i++;
            }

            this.logger.LogInformation("Downloaded {PathCount} paths to {PathFile}", i, outputFile);
            this.PathLists[Path.GetFileName(outputFile)] = i;
        } finally {
            this.isDownloading = false;
        }
    }

    public void DeletePathList(string name) {
        var path = Path.Combine(Program.AppDir, PathListsDirectory, name);
        if (System.IO.File.Exists(path)) {
            System.IO.File.Delete(path);
            this.PathLists.Remove(name);
            this.logger.LogInformation("Deleted path list {PathFile}", name);
        }
    }

    public T? GetFile<T>(File file) where T : FileResource {
        if (file.Path != null) {
            return this.gameData.GameData!.GetFile<T>(file.Path);
        }

        if (file.Dat is null) return null;
        foreach (var repo in this.gameData.GameData!.Repositories.Values) {
            foreach (var cat in repo.Categories.Values.SelectMany(c => c)) {
                if (cat.IndexHashTableEntries is null) continue;
                if (cat.CategoryId != file.Dat.Category.Id || cat.Expansion != file.Dat.Category.Expansion)
                    continue;
                return cat.GetFile<T>(file.Hash);
            }
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
        var hash = Util.GetFullHash(path);
        return new File(dat, hash, path);
    }

    public record Category(byte Id, byte Expansion);
    public record Dat(Category Category, byte Index);
    public record Directory(List<string> Folders, List<File> Files);

    public record File(Dat? Dat, ulong Hash, string? Path = null) {
        public string? Name = Path?.Split('/').Last();
        public uint FolderHash = (uint) (Hash >> 32);
        public uint FileHash = (uint) Hash;

        public File(string path) : this(null, Util.GetFullHash(path), path) { }
    }
}
