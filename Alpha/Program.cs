using System.Net.Http.Headers;
using System.Reflection;
using Alpha.Game;
using Alpha.Gui.Windows;
using Alpha.Gui.Windows.Ftue;
using Alpha.Services;
using Alpha.Services.Excel;
using Alpha.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Alpha;

public class Program {
    public static string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Alpha"
    );
    public static IHost Host = null!;
    public static HttpClient HttpClient = null!;
    public static Version Version = Assembly.GetExecutingAssembly().GetName().Version!;

    public static void Main() {
        HttpClient = new HttpClient();
        HttpClient.DefaultRequestHeaders.Add("User-Agent", $"Alpha/{Version} (https://github.com/NotNite/Alpha)");

        var overrideAppDir = Environment.GetEnvironmentVariable("ALPHA_APPDIR");
        if (overrideAppDir is not null) AppDir = overrideAppDir;

        if (!Directory.Exists(AppDir)) Directory.CreateDirectory(AppDir);
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(AppDir, "Alpha.log"))
            .MinimumLevel.Debug()
            .CreateLogger();

        var builder = new HostApplicationBuilder();
        builder.Environment.ContentRootPath = AppDir;
        builder.Services.AddSerilog();

        builder.Services.AddSingleton(Config.Load());

        builder.Services.AddSingletonHostedService<GuiService>();
        builder.Services.AddSingletonHostedService<WindowManagerService>();
        builder.Services.AddSingletonHostedService<GameDataService>();
        builder.Services.AddSingleton<PathListService>();

        builder.Services.AddScoped<PathService>();
        builder.Services.AddScoped<ExcelService>();
        builder.Services.AddScoped<AlphaGameData>(p =>
            p.GetRequiredService<GameDataService>().GameDatas.First().Value);

        builder.Services.AddScoped<FtueWindow>();
        builder.Services.AddScoped<ExcelWindow>();
        builder.Services.AddScoped<SettingsWindow>();
        builder.Services.AddScoped<FilesystemWindow>();

        Log.Information("Alpha is starting, please wait... {Version}", Version);
        Host = builder.Build();

        Host.Start();
        Host.WaitForShutdown();
        HttpClient.Dispose();
    }
}
