using System.Runtime.CompilerServices;
using Alpha.Core;
using Lumina;
using Serilog;

// ReSharper disable MemberCanBePrivate.Global

namespace Alpha;

public static class Services {
    public static Configuration Configuration = null!;
    public static ModuleManager ModuleManager = null!;
    public static GameData GameData = null!;

    private static bool _initialized;

    public static void Initialize() {
        if (_initialized) {
            Log.Warning("Tried to initialize services twice?");
            return;
        }

        Configuration = Configuration.Load();
        ModuleManager = new ModuleManager();

        if (Configuration.GamePath is not null) InitLumina();

        _initialized = true;
    }

    public static void InitLumina() {
        var sqpackDir = Path.Combine(Configuration.GamePath, "game", "sqpack");
        GameData = new GameData(sqpackDir, new LuminaOptions {
            PanicOnSheetChecksumMismatch = false
        });
    }
}
