using System.Numerics;
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
    [JsonIgnore]
    private static JsonSerializerOptions SerializerOptions => new() {
        WriteIndented = true,
        IncludeFields = true
    };

    // UI
    public Vector2 WindowPos = new(100, 100);
    public Vector2 WindowSize = new(1280, 720);

    // Initial setup
    public List<string> GamePaths = new();
    public bool FtueComplete;

    // Excel
    public bool SortByOffsets;
    public bool AlwaysShowOffsets;
    public bool HighlightLinks = true;
    public bool LineHeightImages;

    public static Config Load() {
        Config config;
        try {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), SerializerOptions)!;
        } catch (Exception e) {
            Log.Warning(e, "Failed to load config file - creating a new one");
            config = new Config();
        }

        config.Fixup();
        config.Save();

        return config;
    }

    public void Fixup() {
        this.GamePaths = this.GamePaths.Where(dir => Directory.Exists(Path.Combine(dir, "sqpack"))).ToList();
    }

    public void Save() {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, SerializerOptions));
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
