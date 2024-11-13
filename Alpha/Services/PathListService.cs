using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Alpha.Services;

public class PathListService {
    private const string PathListsDirectory = "pathlists";
    public readonly List<string> PathLists = new();
    public bool IsDownloading;

    private readonly ILogger<PathListService> logger;

    public PathListService(ILogger<PathListService> logger) {
        this.logger = logger;
        foreach (var path in this.GetPathListFiles()) this.PathLists.Add(Path.GetFileName(path));
    }

    public async Task DownloadResLogger(bool currentOnly) {
        this.IsDownloading = true;

        try {
            var filename = currentOnly ? "CurrentPathListWithHashes.gz" : "PathListWithHashes.gz";
            var url = $"https://rl2.perchbird.dev/download/export/{filename}";

            using var client = new HttpClient();
            await using var req = await client.GetStreamAsync(url);
            await using var gzip = new GZipStream(req, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            var filenameOutput = currentOnly ? "reslogger-current.csv" : "reslogger.csv";
            var outputFile = Path.Combine(Program.AppDir, PathListsDirectory, filenameOutput);
            if (File.Exists(outputFile)) File.Delete(outputFile);

            var i = 0;
            await using var writer = new StreamWriter(outputFile);
            reader.ReadLine(); // skip header
            while (!reader.EndOfStream) {
                var line = reader.ReadLine()!.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                writer.WriteLine(line);
                i++;
            }

            if (!this.PathLists.Contains(filenameOutput)) this.PathLists.Add(filenameOutput);
            Log.Information("Downloaded {PathCount} paths to {PathFile}", i, outputFile);
        } finally {
            this.IsDownloading = false;
        }
    }

    public void DeletePathList(string name) {
        var path = Path.Combine(Program.AppDir, PathListsDirectory, name);
        if (File.Exists(path)) {
            File.Delete(path);
            this.PathLists.Remove(name);
            this.logger.LogInformation("Deleted path list {PathFile}", name);
        }
    }

    public IEnumerable<string> GetPathListFiles() {
        var pathDir = Path.Combine(Program.AppDir, PathListsDirectory);
        if (!Directory.Exists(pathDir)) Directory.CreateDirectory(pathDir);

        foreach (var path in Directory.EnumerateFiles(pathDir)) yield return Path.GetFullPath(path);
    }

    public IEnumerable<string> LoadPathLists() {
        foreach (var path in this.GetPathListFiles()) {
            using var reader = new StreamReader(path);
            reader.ReadLine(); // skip header

            while (!reader.EndOfStream) {
                var line = reader.ReadLine()!.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                yield return line;
            }
        }
    }
}
