using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Alpha.Gui;
using Alpha.Services.Excel;
using Lumina.Data;
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
        IncludeFields = true,
        Converters = {new JsonStringEnumConverter()}
    };

    // UI
    public Vector2 WindowPos = new(100, 100);
    public Vector2 WindowSize = new(1280, 720);
    public UiTheme Theme = UiTheme.Dark;
    public Vector3? BackgroundColor;
    public List<FontConfig> ExtraFonts = [];
    public bool EnableDocking = true;

    // Initial setup
    public List<string> GamePaths = [];
    public bool FtueComplete;

    // Excel
    public bool SortByOffsets;
    public bool AlwaysShowOffsets;
    public bool HighlightLinks = true;
    public bool LineHeightImages;
    public Language DefaultLanguage = Language.English;
    public SchemaProvider SchemaProvider = SchemaProvider.ExdSchema;

    public static Config Load() {
        Config config;
        if (!File.Exists(ConfigPath)) {
            config = new Config();
        } else {
            try {
                config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), SerializerOptions)!;
            } catch (Exception e) {
                Log.Warning(e, "Failed to load config file - creating a new one");
                config = new Config();
            }
        }

        config.Fixup();
        config.Save();

        return config;
    }

    public void Fixup() {
        this.GamePaths = this.GamePaths.Where(dir => Directory.Exists(Path.Combine(dir, "sqpack"))).ToList();
        this.ExtraFonts = this.ExtraFonts.Where(path => !string.IsNullOrWhiteSpace(path.Path)).ToList();
    }

    public void Save() {
        //Log.Debug("Saving config");
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
