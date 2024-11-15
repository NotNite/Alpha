using System.Text.Json;
using Alpha.Game;
using Alpha.Gui.Windows;
using Alpha.Services.Excel.Cells;
using Alpha.Services.Excel.SaintCoinach;
using Lumina.Excel;
using Microsoft.Extensions.Logging;

namespace Alpha.Services.Excel;

public class ExcelService(WindowManagerService windowManager, ILogger<ExcelService> logger)
    : IDisposable {
    public AlphaGameData? GameData;
    public readonly Dictionary<string, IAlphaSheet?> SheetsCache = new();
    public readonly Dictionary<string, SheetDefinition?> SheetDefinitions = new();
    public string[] Sheets => (this.GameData?.GameData.Excel.SheetNames.ToArray()
                                      .OrderBy(s => s)
                                      .ToArray()) ?? [];

    private readonly HttpClient httpClient = new();
    private readonly List<string> resolvingDefinitions = new();

    public void Dispose() {
        this.httpClient.Dispose();
    }

    public void SetGameData(AlphaGameData gameData) {
        this.GameData = gameData;
        this.SheetsCache.Clear();
        this.SheetDefinitions.Clear();
        this.resolvingDefinitions.Clear();
    }

    public IAlphaSheet? GetSheet(string name, bool skipCache = false, bool resolveDefinition = true) {
        if (!this.SheetDefinitions.ContainsKey(name) && resolveDefinition) {
            this.ResolveSheetDefinition(name);
        }
        if (this.SheetsCache.TryGetValue(name, out var sheet)) return sheet;

        var rawSheet = this.GameData?.GameData.Excel.GetRawSheet(name: name);
        if (rawSheet is not null) {
            if (rawSheet is RawSubrowExcelSheet) {
                sheet = new AlphaSubrowSheet(this.GameData!.GameData.Excel.GetSubrowSheet<RawSubrow>(name: name), name);
            } else {
                sheet = new AlphaSheet(this.GameData!.GameData.Excel.GetSheet<RawRow>(name: name), name);
            }
        }
        if (skipCache) return sheet;
        this.SheetsCache[name] = sheet;

        return sheet;
    }

    public void OpenNewWindow(string? sheet = null, (uint Row, ushort? Subrow)? row = null) {
        var window = windowManager.CreateWindow<ExcelWindow>();
        if (sheet is not null) window.OpenSheet(sheet, row);
    }

    public void OpenNewWindow(IAlphaSheet? sheet = null, (uint Row, ushort? Subrow)? row = null) =>
        this.OpenNewWindow(sheet?.Name, row);

    public Cell GetCell(IAlphaSheet sheet, uint row, ushort? subrow, uint column, object? data) {
        var sheetDefinition = this.SheetDefinitions[sheet.Name];
        var cell = sheetDefinition?.GetCell(this, sheet, row, subrow, column, data);
        return cell ?? new DefaultCell(row, subrow, column, data);
    }

    public uint? GetDefaultColumnForSheet(string sheetName) => this.SheetDefinitions.TryGetValue(sheetName, out var def)
                                                                   ? def?.DefaultColumn
                                                                   : null;

    private void ResolveSheetDefinition(string name) {
        if (this.resolvingDefinitions.Contains(name)) return;
        this.resolvingDefinitions.Add(name);

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
