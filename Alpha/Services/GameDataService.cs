using System.Text.Json;
using Alpha.Game;
using Lumina;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Environment = System.Environment;

namespace Alpha.Services;

public class GameDataService : IHostedService {
    public Dictionary<string, AlphaGameData> GameDatas = new();

    private readonly Config config;
    private readonly ILogger<GameDataService> logger;

    public GameDataService(Config config, ILogger<GameDataService> logger) {
        this.config = config;
        this.logger = logger;

        foreach (var path in this.config.GamePaths.ToList()) {
            try {
                this.AddGamePath(path);
            } catch (Exception e) {
                this.logger.LogError(e, "Failed to load game path info");
                this.config.GamePaths.Remove(path);
            }
        }

        this.config.Save();
    }

    public void AddGamePath(string path) {
        try {
            var info = new GameInstallationInfo(path);
            var platform = PlatformId.Win32;

            // Use known exe names to determine platform
            if (File.Exists(Path.Combine(info.GamePath, "ffxivgame.exe"))) {
                platform = PlatformId.Lys;
            } else if (File.Exists(Path.Combine(info.GamePath, "ffxivgame.elf"))) {
                platform = PlatformId.PS4;
            }

            var options = new LuminaOptions {
                CurrentPlatform = platform,
                PanicOnSheetChecksumMismatch = false,
                DefaultExcelLanguage = this.config.DefaultLanguage
            };
            var gameData = new AlphaGameData {
                GameData = new GameData(Path.Combine(info.GamePath, "sqpack"), options),
                GamePath = info.GamePath,
                GameInstallationInfo = info
            };
            this.GameDatas.Add(info.GamePath, gameData);
            if (!this.config.GamePaths.Contains(info.GamePath)) {
                this.config.GamePaths.Add(info.GamePath);
                this.config.Save();
            }
        } catch (Exception e) {
            this.logger.LogError(e, "Failed to add game path");
        }
    }

    public void RemoveGamePath(string path) {
        this.GameDatas.Remove(path);
        this.config.GamePaths.Remove(path);
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

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
