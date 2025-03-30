using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

    public bool DoUpdateChecking = true;
    public DateTime? UpdateCheckTime;
    public Version? UpdateCheckVersion;

    // Excel
    public bool SortByOffsets;
    public bool AlwaysShowOffsets;
    public bool HighlightLinks = true;
    public bool LineHeightImages;
    public bool RowIdAsHex;
    public Language DefaultLanguage = Language.English;
    public SchemaProvider SchemaProvider = SchemaProvider.ExdSchema;

    public static Config Load() {
        Config config;
        if (!File.Exists(ConfigPath)) {
            config = new Config();
        } else {
            try {
                config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath),
                    ConfigJsonSerializerContext.Default.Config)!;
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
        if (this.UpdateCheckVersion != null && this.UpdateCheckVersion <= Program.Version)
            this.UpdateCheckVersion = null;
    }

    public void Save() {
        //Log.Debug("Saving config");
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, ConfigJsonSerializerContext.Default.Config));
    }

    public void Dispose() {
        // If we edited the config file on disk, skip saving it so we don't overwrite it
        try {
            var newConfig = JsonNode.Parse(File.ReadAllText(ConfigPath));
            var oldConfig = JsonNode.Parse(JsonSerializer.Serialize(this, ConfigJsonSerializerContext.Default.Config));
            if (!JsonNode.DeepEquals(newConfig, oldConfig)) return;
        } catch {
            // ignored
        }

        this.Save();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = true, Converters = [
    typeof(JsonStringEnumConverter<UiTheme>),
    typeof(JsonStringEnumConverter<Language>),
    typeof(JsonStringEnumConverter<SchemaProvider>)
])]
[JsonSerializable(typeof(Config))]
public partial class ConfigJsonSerializerContext : JsonSerializerContext;
