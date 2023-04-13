using System.Diagnostics;

namespace Alpha.Utils;

public static class AppUtils {
    public static void OpenFile(string path) {
        switch (Environment.OSVersion.Platform) {
            case PlatformID.Win32NT:
                Process.Start("explorer.exe", path);
                break;
            case PlatformID.Unix:
                Process.Start("xdg-open", path);
                break;
            case PlatformID.MacOSX:
                Process.Start("open", path);
                break;
            default:
                throw new PlatformNotSupportedException();
        }
    }

    public static void OpenInApp(string app, string path) {
        Process.Start(app, path);
    }
}
