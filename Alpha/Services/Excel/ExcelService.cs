using System.Text.Json;
using Alpha.Gui.Windows;
using Alpha.Services.Excel.Cells;
using Alpha.Services.Excel.SaintCoinach;
using Lumina.Excel;
using Microsoft.Extensions.Logging;

namespace Alpha.Services.Excel;

public class ExcelService(GameDataService gameData, WindowManagerService windowManager, ILogger<ExcelService> logger)
    : IDisposable {
    public readonly Dictionary<string, AlphaSheet?> SheetsCache = new();
    public readonly Dictionary<string, SheetDefinition?> SheetDefinitions = new();
    public string[] Sheets => (gameData.GameData?.Excel.SheetNames.ToArray() ?? [])
        .OrderBy(s => s)
        .ToArray();

    private readonly HttpClient httpClient = new();

    public void Dispose() {
        this.httpClient.Dispose();
    }

    public AlphaSheet? GetSheet(string name, bool skipCache = false) {
        if (this.SheetsCache.TryGetValue(name, out var sheet)) return sheet;

        var rawSheet = gameData.GameData?.Excel.GetSheet<RawRow>(name: name);
        sheet = rawSheet is not null ? new AlphaSheet(rawSheet, name) : null;
        if (skipCache) return sheet;
        this.SheetsCache[name] = sheet;

        if (!this.SheetDefinitions.ContainsKey(name)) {
            this.ResolveSheetDefinition(name);
        }

        return sheet;
    }

    public void OpenNewWindow(string? sheet = null, int? scrollTo = null) {
        var window = windowManager.CreateWindow<ExcelWindow>();
        if (sheet is not null) window.OpenSheet(sheet, scrollTo);
    }

    public void OpenNewWindow(AlphaSheet? sheet = null, int? row = null) {
        var window = windowManager.CreateWindow<ExcelWindow>();
        if (sheet is not null) window.OpenSheet(sheet, row);
    }

    public Cell GetCell(AlphaSheet sheet, int row, int column, object? data) {
        var sheetDefinition = this.SheetDefinitions[sheet.Name];
        var cell = sheetDefinition?.GetCell(this, sheet, row, column, data);
        return cell ?? new DefaultCell(row, column, data);
    }

    public int? GetDefaultColumnForSheet(string sheetName) => this.SheetDefinitions.TryGetValue(sheetName, out var def)
                                                                  ? def?.DefaultColumn
                                                                  : null;

    private void ResolveSheetDefinition(string name) {
        // TODO: exdschema
        var url =
            $"https://raw.githubusercontent.com/xivapi/SaintCoinach/master/SaintCoinach/Definitions/{name}.json";

        this.httpClient.GetAsync(url).ContinueWith(t => {
            try {
                var result = t.Result;
                if (result.IsSuccessStatusCode) {
                    var json = result.Content.ReadAsStringAsync().Result;

                    var sheetDefinition = JsonSerializer.Deserialize<SaintCoinachSheetDefinition>(json);
                    if (sheetDefinition is null) {
                        logger.LogError("Failed to deserialize sheet definition");
                        return;
                    }

                    logger.LogDebug("Resolved sheet definition: {SheetName}", name);

                    this.SheetDefinitions[name] = sheetDefinition;
                } else {
                    logger.LogWarning("Request for sheet definition failed: {SheetName} -> {StatusCode}",
                        name,
                        result.StatusCode);

                    this.SheetDefinitions[name] = null;
                }
            } catch (Exception e) {
                logger.LogWarning(e, "Failed to resolve sheet definition");
            }
        });
    }
}
