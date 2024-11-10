using Lumina;
using Lumina.Data.Files;
using Microsoft.Extensions.DependencyInjection;

namespace Alpha.Game;

public record AlphaGameData {
    public required GameData GameData;
    public required string GamePath;
    public required GameInstallationInfo GameInstallationInfo;

    public TexFile? GetIcon(uint id) {
        var nqPath = $"ui/icon/{id / 1000 * 1000:000000}/{id:000000}.tex";
        var hqPath = $"ui/icon/{id / 1000 * 1000:000000}/{id:000000}_hr1.tex";
        var langPath = $"ui/icon/{id / 1000 * 1000:000000}/en/{id:000000}.tex"; // FIXME hardcoded lang
        string[] tryOrder = [hqPath, nqPath, langPath];

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
