using System.IO.Compression;
using Alpha.Core;
using ImGuiNET;

namespace Alpha.Modules;

public class ResLoggerModule : SimpleModule {
    public readonly List<string> CurrentPathCache = new();
    public readonly List<string> PathCache = new();

    private Task? _currentFetchTask;
    private Task? _fetchTask;

    public ResLoggerModule() : base("ResLogger", "Data") {
        if (Services.Configuration.AutoPaths) this.FetchPaths();
        if (Services.Configuration.AutoCurrentPaths) this.FetchCurrentPaths();
        var pathsDir = Path.Combine(Program.DataDirectory, "pathlists");
        if (Directory.Exists(pathsDir)) {
            foreach (var file in Directory.EnumerateFiles(pathsDir)) {
                var lines = File.ReadAllLines(file);
                this.CurrentPathCache.AddRange(lines);
            }
        }
    }

    internal override void SimpleDraw() {
        ImGui.Text("Path cache count: " + this.PathCache.Count);
        ImGui.Text("Current path cache count: " + this.CurrentPathCache.Count);

        var shouldDisable = this._fetchTask is not null;
        if (shouldDisable) ImGui.BeginDisabled();
        if (ImGui.Button("Fetch paths")) this.FetchPaths();
        if (shouldDisable) ImGui.EndDisabled();

        ImGui.SameLine();

        var autoPaths = Services.Configuration.AutoPaths;
        if (ImGui.Checkbox("Download on startup##AutoPaths", ref autoPaths)) {
            Services.Configuration.AutoPaths = autoPaths;
            Services.Configuration.Save();
        }

        var shouldDisable2 = this._currentFetchTask is not null;
        if (shouldDisable2) ImGui.BeginDisabled();
        if (ImGui.Button("Fetch current paths")) this.FetchCurrentPaths();
        if (shouldDisable2) ImGui.EndDisabled();

        ImGui.SameLine();

        var autoCurrentPaths = Services.Configuration.AutoCurrentPaths;
        if (ImGui.Checkbox("Download on startup##AutoCurrentPaths", ref autoCurrentPaths)) {
            Services.Configuration.AutoCurrentPaths = autoCurrentPaths;
            Services.Configuration.Save();
        }

        if (ImGui.Button("Clear paths")) {
            this._fetchTask = null;
            this._currentFetchTask = null;
            this.PathCache.Clear();
            this.CurrentPathCache.Clear();
        }
    }

    public void FetchPaths() {
        if (this._fetchTask is not null) return;

        this._fetchTask = Task.Run(() => {
            using var client = new HttpClient();
            var req = client.GetStreamAsync("https://rl2.perchbird.dev/download/export/PathList.gz").Result;
            using var gzip = new GZipStream(req, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            // skip header
            reader.ReadLine();

            while (!reader.EndOfStream) {
                var line = reader.ReadLine();
                if (line is null) continue;
                this.PathCache.Add(line);
            }
        });
    }

    public void FetchCurrentPaths() {
        if (this._currentFetchTask is not null) return;

        this._currentFetchTask = Task.Run(() => {
            using var client = new HttpClient();
            var req = client.GetStreamAsync("https://rl2.perchbird.dev/download/export/CurrentPathList.gz").Result;
            using var gzip = new GZipStream(req, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            // skip header
            reader.ReadLine();

            while (!reader.EndOfStream) {
                var line = reader.ReadLine();
                if (line is null) continue;
                this.CurrentPathCache.Add(line);
            }
        });
    }
}
