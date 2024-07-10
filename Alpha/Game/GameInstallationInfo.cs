namespace Alpha.Game;

public class GameInstallationInfo {
    public readonly string GamePath;

    public string? BootVersion;
    public string? GameVersion;
    public Dictionary<int, string> ExpansionVersions = new();

    public GameInstallationInfo(string path) {
        var dirName = new DirectoryInfo(path).Name;
        if (dirName == "game") {
            this.GamePath = path;
        } else if (Path.Exists(Path.Combine(path, "game"))) {
            this.GamePath = Path.Combine(path, "game");
        } else if (dirName == "sqpack") {
            this.GamePath = Directory.GetParent(path)!.FullName;
        } else {
            throw new Exception($"Invalid game path: {path}");
        }

        // Normalize
        this.GamePath = Path.GetFullPath(this.GamePath);

        var gameVer = Path.Combine(this.GamePath, "ffxivgame.ver");
        if (File.Exists(gameVer)) this.GameVersion = File.ReadAllText(gameVer).Trim();

        var bootVer = Path.Combine(this.GamePath, "..", "boot", "ffxivboot.ver");
        if (File.Exists(bootVer)) this.BootVersion = File.ReadAllText(bootVer).Trim();

        var sqpackDir = Path.Combine(this.GamePath, "sqpack");
        var sqpackRepos = Directory.Exists(sqpackDir) ? Directory.GetDirectories(sqpackDir) : [];
        foreach (var sqpackRepo in sqpackRepos) {
            if (int.TryParse(Path.GetFileName(sqpackRepo.Replace("ex", "")), out var ex)) {
                var ver = Path.Combine(sqpackRepo, $"ex{ex}.ver");
                if (File.Exists(ver)) {
                    this.ExpansionVersions[ex] = File.ReadAllText(ver).Trim();
                }
            }
        }
    }
}
