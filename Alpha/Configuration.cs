using System.Text.Json;
using Lumina.Data;

namespace Alpha;

[Serializable]
public class Configuration {
    public string? GamePath { get; set; }
    public Language ExcelLanguage { get; set; } = Language.English;
    public bool DrawImGuiDemo { get; set; }
    public bool DrawDebug { get; set; }

    public bool AutoPaths { get; set; }
    public bool AutoCurrentPaths { get; set; }

    public bool PreferHr1 { get; set; } = true;

    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;

    public float FpsLimit { get; set; } = 60.0f;

    public void Save() {
        var path = Path.Combine(Program.DataDirectory, "config.json");
        var serialized = JsonSerializer.Serialize(this);
        File.WriteAllText(path, serialized);
    }

    public static Configuration Load() {
        var path = Path.Combine(Program.DataDirectory, "config.json");

        if (!File.Exists(path)) {
            var config = new Configuration();
            config.Save();
            return config;
        }

        var serialized = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Configuration>(serialized) ?? new Configuration();
    }
}
