using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Alpha.Services.Excel.ExdSchema;

public class ExdSchemaResolver : ISchemaResolver {
    private string? sha;

    // lol this sucks
    private readonly ILogger<ExdSchemaResolver> logger =
        Program.Host.Services.GetRequiredService<ILogger<ExdSchemaResolver>>();
    private readonly IDeserializer deserializer = new StaticDeserializerBuilder(new ExdSchemaStaticContext())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public ExdSchemaResolver() {
        Task.Run(async () => {
            try {
                var resp = await Program.HttpClient.GetFromJsonAsync<GitHubApiResponse>(
                               "https://api.github.com/repos/xivdev/EXDSchema/contents/schemas/latest",
                               GitHubApiJsonSerializerContext.Default.GitHubApiResponse);
                this.sha = resp?.Sha;
            } catch (Exception e) {
                this.logger.LogError(e, "Failed to get latest EXDSchema sha");
            }
        });
    }

    public async Task<ISheetDefinition?> GetDefinition(string name) {
        if (this.sha is null) return null;

        var url =
            $"https://raw.githubusercontent.com/xivdev/EXDSchema/{this.sha}/{name}.yml";
        var str = await Program.HttpClient.GetStringAsync(url);
        return this.deserializer.Deserialize<ExdSchemaSheetDefinition>(str);
    }
}

public record GitHubApiResponse(string Sha);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GitHubApiResponse))]
public partial class GitHubApiJsonSerializerContext : JsonSerializerContext;

[YamlStaticContext]
public partial class ExdSchemaStaticContext;
