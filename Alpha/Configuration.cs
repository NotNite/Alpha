using System.Text.Json;

namespace Alpha;

[Serializable]
public class Configuration {
    public string? GamePath { get; set; }
    public bool DrawImGuiDemo { get; set; }
    
    public bool AutoPaths { get; set; }
    public bool AutoCurrentPaths { get; set; }
    
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
