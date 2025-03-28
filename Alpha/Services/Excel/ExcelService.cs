using Alpha.Game;
using Alpha.Gui.Windows;
using Alpha.Services.Excel.Cells;
using Alpha.Services.Excel.ExdSchema;
using Alpha.Services.Excel.SaintCoinach;
using Lumina.Excel;
using Microsoft.Extensions.Logging;

namespace Alpha.Services.Excel;

public class ExcelService {
    public AlphaGameData? GameData;
    public readonly Dictionary<string, IAlphaSheet?> SheetsCache = new();
    public readonly Dictionary<string, ISheetDefinition?> SheetDefinitions = new();
    public string[] Sheets = [];

    private readonly ISchemaResolver? resolver;
    private readonly List<string> resolvingDefinitions = new();

    private readonly WindowManagerService windowManager;
    private readonly ILogger<ExcelService> logger;
    private readonly Config config;

    public ExcelService(WindowManagerService windowManager, ILogger<ExcelService> logger, Config config) {
        this.windowManager = windowManager;
        this.logger = logger;
        this.config = config;

        switch (this.config.SchemaProvider) {
            case SchemaProvider.SaintCoinach: {
                this.resolver = new SaintCoinachResolver();
                break;
            }

            case SchemaProvider.ExdSchema: {
                this.resolver = new ExdSchemaResolver();
                break;
            }
        }
    }

    public void SetGameData(AlphaGameData gameData) {
        this.GameData = gameData;
        this.SheetsCache.Clear();
        this.SheetDefinitions.Clear();
        this.resolvingDefinitions.Clear();
        this.Sheets = this.GameData?.GameData.Excel.SheetNames.ToArray()
                          .OrderBy(s => s)
                          .ToArray() ?? [];
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
        var window = this.windowManager.CreateWindow<ExcelWindow>();
        if (sheet is not null) window.OpenSheet(sheet, row);
    }

    public void OpenNewWindow(IAlphaSheet? sheet = null, (uint Row, ushort? Subrow)? row = null) =>
        this.OpenNewWindow(sheet?.Name, row);

    public Cell GetCell(IAlphaSheet sheet, uint row, ushort? subrow, uint column, object? data) {
        var sheetDefinition = this.SheetDefinitions.GetValueOrDefault(sheet.Name);
        var cell = sheetDefinition?.GetCell(this, sheet, row, subrow, column, data);
        return cell ?? new DefaultCell(row, subrow, column, data);
    }

    public uint? GetDefaultColumnForSheet(string sheetName) => this.SheetDefinitions.TryGetValue(sheetName, out var def)
                                                                   ? def?.DefaultColumn
                                                                   : null;

    private void ResolveSheetDefinition(string name) {
        if (this.resolver is null) {
            this.SheetDefinitions[name] = null;
            return;
        }

        if (this.resolvingDefinitions.Contains(name)) return;
        this.resolvingDefinitions.Add(name);

        this.logger.LogInformation("Resolving sheet definition: {Name}", name);

        Task.Run(async () => {
            try {
                this.SheetDefinitions[name] = await this.resolver.GetDefinition(name);
            } catch (Exception e) {
                this.logger.LogWarning(e, "Request for sheet definition {SheetName} failed", name);
                this.SheetDefinitions[name] = null;
            }
        });
    }
}
