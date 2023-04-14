using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;

namespace Omega;

public class Services {
    [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static CommandManager CommandManager { get; private set; } = null!;
}
