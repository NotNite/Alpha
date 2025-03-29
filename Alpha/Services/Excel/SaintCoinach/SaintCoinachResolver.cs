using System.Net.Http.Json;

namespace Alpha.Services.Excel.SaintCoinach;

public class SaintCoinachResolver : ISchemaResolver {
    public async Task<ISheetDefinition?> GetDefinition(string name) =>
        await Program.HttpClient.GetFromJsonAsync<SaintCoinachSheetDefinition>(
            $"https://raw.githubusercontent.com/xivapi/SaintCoinach/master/SaintCoinach/Definitions/{name}.json",
            SaintCoinachJsonSerializerContext.Default.SaintCoinachSheetDefinition
        );
}
