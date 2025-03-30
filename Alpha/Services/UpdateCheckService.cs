using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Alpha.Services;

public class UpdateCheckService(Config config) : IHostedService {
    public const int UpdatePeriod = 60 * 60 * 24; // One day

    public Task StartAsync(CancellationToken cancellationToken) {
        if (config.DoUpdateChecking) {
            Task.Run(async () => {
                var updated = false;
                try {
                    var now = DateTime.UtcNow;
                    if (config.UpdateCheckTime is null || config.UpdateCheckTime.Value.AddSeconds(UpdatePeriod) < now) {
                        Log.Debug("Checking for updates...");
                        config.UpdateCheckTime = now;
                        updated = true;
                        await this.DoUpdateCheck();
                    }
                } catch (Exception e) {
                    Log.Warning(e, "Failed to do update check");
                }

                if (updated) config.Save();
            }, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task DoUpdateCheck() {
        const string url = "https://api.github.com/repos/NotNite/Alpha/releases/latest";
        var json = await Program.HttpClient.GetFromJsonAsync<VersionCheckApiResponse>(
                       url,
                       VersionCheckApiJsonSerializerContext.Default.VersionCheckApiResponse
                   );
        if (json == null) return;
        var verStr = json.TagName;
        if (verStr.StartsWith('v')) verStr = verStr.TrimStart('v');

        if (Version.TryParse(verStr, out var ver) && ver > Program.Version) {
            Log.Debug("New update available: {Version}", ver);
            config.UpdateCheckVersion = ver;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public record VersionCheckApiResponse(string TagName);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(VersionCheckApiResponse))]
public partial class VersionCheckApiJsonSerializerContext : JsonSerializerContext;
