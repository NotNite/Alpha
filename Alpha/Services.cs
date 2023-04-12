using Alpha.Core;
using Alpha.Utils;
using Lumina;

// ReSharper disable MemberCanBePrivate.Global

namespace Alpha;

public static class Services {
    public static Configuration Configuration = null!;
    public static ModuleManager ModuleManager = null!;
    public static GameData GameData = null!;
    public static ImageHandler ImageHandler = null!;

    // Setup is split into two parts for modules that depend on Lumina
    // This is a garbage state machine, and garbage dependency management, but I don't care
    public static void InitPreSetup() {
        Configuration = Configuration.Load();
        ImageHandler = new ImageHandler();
    }

    public static void InitPostSetup() {
        var sqpackDir = Path.Combine(Configuration.GamePath!, "game", "sqpack");
        GameData = new GameData(sqpackDir, new LuminaOptions {
            PanicOnSheetChecksumMismatch = false,
            DefaultExcelLanguage = Configuration.ExcelLanguage
        });

        ModuleManager = new ModuleManager();
        ModuleManager.InitializeModules();
    }
}
