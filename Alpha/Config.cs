using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Serilog;

namespace Alpha;

public class Config : IDisposable {
    [JsonIgnore]
    public static string ConfigPath => Path.Combine(
        Program.AppDir,
        "config.json"
    );

    // UI
    [JsonInclude] public int WindowX = 10;
    [JsonInclude] public int WindowY = 10;
    [JsonInclude] public int WindowWidth = 1280;
    [JsonInclude] public int WindowHeight = 720;

    // Initial setup
    [JsonInclude] public List<string> GamePaths = new();
    [JsonInclude] public string? CurrentGamePath;
    [JsonInclude] public bool FtueComplete;

    // Excel
    [JsonInclude] public bool SortByOffsets;
    [JsonInclude] public bool AlwaysShowOffsets;
    [JsonInclude] public bool HighlightLinks = true;
    [JsonInclude] public bool PreferHighQuality = true;
    [JsonInclude] public bool LineHeightImages;

    public static Config Load() {
        Config config;
        try {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath))!;
        } catch (Exception e) {
            Log.Warning("Failed to load config file - creating a new one: {e}", e);
            config = new Config();
        }

        config.Fixup();
        config.Save();

        return config;
    }

    public void Fixup() {
        this.GamePaths = this.GamePaths.Where(dir => Directory.Exists(Path.Combine(dir, "sqpack"))).ToList();
        if (this.CurrentGamePath is not null && !this.GamePaths.Contains(this.CurrentGamePath)) {
            this.CurrentGamePath = null;
        }
    }

    public void Save() {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions {
            WriteIndented = true
        }));
    }

    public void Dispose() {
        // If we edited the config file on disk, skip saving it so we don't overwrite it
        try {
            var newConfig = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(ConfigPath))!;
            var oldConfig = JsonSerializer.Deserialize<JsonNode>(JsonSerializer.Serialize(this))!;
            if (!JsonNode.DeepEquals(newConfig, oldConfig)) return;
        } catch {
            // ignored
        }

        this.Save();
    }
}
