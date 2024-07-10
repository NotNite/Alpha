using System.Text.Json;
using Alpha.Game;
using Lumina;
using Lumina.Data.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Environment = System.Environment;

namespace Alpha.Services;

public class GameDataService {
    public GameData? GameData;
    public event Action? OnGameDataChanged;

    public string? CurrentGamePath => this.config.CurrentGamePath;
    public GameInstallationInfo? CurrentGamePathInfo => this.config.CurrentGamePath is not null
                                                            ? this.GamePathInfo[this.config.CurrentGamePath]
                                                            : null;

    public readonly Dictionary<string, GameInstallationInfo> GamePathInfo = new();

    private Config config;
    private ILogger<GameDataService> logger;

    public GameDataService(Config config, ILogger<GameDataService> logger) {
        this.config = config;
        this.logger = logger;

        foreach (var path in this.config.GamePaths.ToList()) {
            try {
                this.GamePathInfo[path] = new GameInstallationInfo(path);
            } catch (Exception e) {
                this.logger.LogError(e, "Failed to load game path info");
                this.config.GamePaths.Remove(path);
                if (this.config.CurrentGamePath == path) this.config.CurrentGamePath = null;
            }
        }

        this.RecreateGameData(true);

        this.config.Save();
    }

    public void RecreateGameData(bool init = false) {
        if (this.config.CurrentGamePath is not null) {
            try {
                var newGameData = new GameData(Path.Combine(this.config.CurrentGamePath, "sqpack"));
                this.GameData?.Dispose();
                this.GameData = newGameData;
                if (!init) this.OnGameDataChanged?.Invoke();
            } catch (Exception e) {
                this.logger.LogError(e, "Failed to load game data");
            }
        }
    }

    public void AddGamePath(string path) {
        if (this.config.GamePaths.Contains(path)) return;

        try {
            var info = new GameInstallationInfo(path);
            this.GamePathInfo[path] = info;
            this.config.GamePaths.Add(info.GamePath);
            this.SetGamePath(info.GamePath);
        } catch (Exception e) {
            this.logger.LogError(e, "Failed to add game path");
        }
    }

    public void SetGamePath(string path) {
        if (!this.config.GamePaths.Contains(path)) return;
        this.config.CurrentGamePath = path;
        this.config.Save();
        this.RecreateGameData();
    }

    public void RemoveGamePath(string path) {
        this.GamePathInfo.Remove(path);
        this.config.GamePaths.Remove(path);
        if (this.config.CurrentGamePath == path) this.config.CurrentGamePath = null;
        this.config.Save();
    }

    public string TryGamePaths() {
        List<string> dirs = [
            "C:/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn",
            "C:/Program Files (x86)/Steam/steamapps/common/FINAL FANTASY XIV Online",
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.xlcore/ffxiv"
        ];

        var xlGamePath = ReadXlGamePath();
        if (xlGamePath is not null) dirs.Insert(0, xlGamePath);

        foreach (var dir in dirs) {
            try {
                var info = new GameInstallationInfo(dir);
                return info.GamePath;
            } catch {
                // ignored
            }
        }

        return string.Empty;
    }

    private string? ReadXlGamePath() {
        if (OperatingSystem.IsWindows()) {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher",
                "launcherConfigV3.json");
            if (File.Exists(path)) {
                try {
                    var config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    var gamePath = config?["GamePath"];
                    if (gamePath is not null && Directory.Exists(gamePath)) {
                        return gamePath;
                    }
                } catch {
                    // ignored
                }
            }
        } else if (OperatingSystem.IsLinux()) {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xlcore",
                "launcher.ini");
            if (File.Exists(path)) {
                try {
                    var config = File.ReadAllLines(path);
                    const string key = "GamePath=";
                    var gamePath = config.FirstOrDefault(x => x.StartsWith(key))?[key.Length..];
                    if (gamePath is not null && Directory.Exists(gamePath)) {
                        return gamePath;
                    }
                } catch {
                    // ignored
                }
            }
        }

        // TODO: XOM
        return null;
    }

    public TexFile? GetIcon(uint id) {
        var nqPath = $"ui/icon/{id / 1000 * 1000:000000}/{id:000000}.tex";
        var hqPath = $"ui/icon/{id / 1000 * 1000:000000}/{id:000000}_hr1.tex";
        var langPath = $"ui/icon/{id / 1000 * 1000:000000}/en/{id:000000}.tex"; // FIXME hardcoded lang
        string[] tryOrder = config.PreferHighQuality ? [hqPath, nqPath, langPath] : [nqPath, hqPath, langPath];

        string? usedPath = null;
        try {
            foreach (var p in tryOrder) {
                if (this.GameData?.FileExists(p) == true) {
                    usedPath = p;
                    break;
                }
            }
        } catch {
            // Lumina likes to throw errors on FileExists for some reason, so let's just ignore it
        }

        return usedPath is null ? null : this.GameData?.GetFile<TexFile>(usedPath);
    }
}
