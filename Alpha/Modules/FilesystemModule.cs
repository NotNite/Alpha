using Alpha.Core;
using Alpha.Windows;

namespace Alpha.Modules;

[Module(DependsOn = new[] { "ResLoggerModule" })]
public class FilesystemModule : WindowedModule<FilesystemWindow> {
    public ResLoggerModule ResLogger;

    public FilesystemModule() : base("Filesystem Browser", "Data") {
        this.ResLogger = Services.ModuleManager.GetModule<ResLoggerModule>();
    }

    internal override void OnClick() {
        this.OpenNewWindow();
    }

    public void OpenFile(string path) {
        this.OpenNewWindow(path);
    }

    private void OpenNewWindow(string? path = null) {
        var window = new FilesystemWindow(this);
        window.Open = true;
        if (path is not null) window.OpenFile(path);
        this.Windows.Add(window);
    }
}
