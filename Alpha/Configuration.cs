using System.Text.Json;
using Alpha.Utils;
using Lumina.Data;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public bool LineHeightImages { get; set; } = false;
    public bool AlwaysShowOffsets { get; set; } = false;

    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public float DisplayScale { get; set; } = 1.0f;
    public float FpsLimit { get; set; } = 60.0f;

    public string HexEditorPath { get; set; } = string.Empty;

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
        var obj = JsonSerializer.Deserialize<Configuration>(serialized) ?? new Configuration();

        if (obj.HexEditorPath == string.Empty) obj.HexEditorPath = DetermineHexEditor();

        obj.Save();
        return obj;
    }

    // Just the hex editors I know of, feel free to PR more
    private static string DetermineHexEditor() {
        var pathsWin = new[] {
            "C:/Program Files/010 Editor/010Editor.exe",
            "C:/Program Files/ImHex/ImHex.exe",
            "C:/Program Files (x86)/HxD/HxD.exe"
        };

        var pathsUnix = new[] {
            // I don't know where 010 installs to :P
            "/usr/bin/imhex"
        };

        var pathsMac = new[] {
            "/Applications/010 Editor.app/Contents/MacOS/010 Editor",
            "/Applications/imhex.app/Contents/MacOS/imhex"
        };

        var pathList = Environment.OSVersion.Platform switch {
            PlatformID.Win32NT => pathsWin,
            PlatformID.Unix => pathsUnix,
            PlatformID.MacOSX => pathsMac,
            _ => null
        };

        if (pathList is null) return string.Empty;

        foreach (var path in pathList) {
            if (File.Exists(path)) return path;
        }

        return string.Empty;
    }
}
