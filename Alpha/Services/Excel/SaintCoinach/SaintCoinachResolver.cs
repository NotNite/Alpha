using System.Text.Json;

namespace Alpha.Services.Excel.SaintCoinach;

public class SaintCoinachResolver : ISchemaResolver {
    public async Task<ISheetDefinition?> GetDefinition(string name) {
        var url =
            $"https://raw.githubusercontent.com/xivapi/SaintCoinach/master/SaintCoinach/Definitions/{name}.json";
        var str = await Program.HttpClient.GetStringAsync(url);
        return JsonSerializer.Deserialize<SaintCoinachSheetDefinition>(str);
    }
}
