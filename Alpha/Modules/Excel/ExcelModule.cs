using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Alpha.Utils;
using ImGuiNET;
using Lumina;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Serilog;
using Module = Alpha.Core.Module;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Alpha.Modules.Excel;

public class ExcelModule : Module {
    private float _sidebarWidth = 300f;
    private string _sidebarFilter = string.Empty;
    private string _contentFilter = string.Empty;
    private List<uint>? _filteredRows;

    private string[] _sheets;

    private RawExcelSheet? _selectedSheet;

    // Working around a Lumina bug to map index to row
    private Dictionary<uint, (uint, uint?)> _rowMapping = new();

    private HttpClient _httpClient = new();
    private readonly Dictionary<string, RawExcelSheet?> _sheetsCache = new();
    private readonly Dictionary<string, SheetDefinition?> _sheetDefinitions = new();

    private CancellationTokenSource? _cancellationToken;
    private string? _scriptError;

    private int? _tempScroll;
    private int _paintTicksLeft = -1;
    private float _itemHeight;

    public ExcelModule() : base("Excel Browser", "Data") {
        this._sheets = Services.GameData.Excel.GetSheetNames().ToArray();
    }

    internal override void PreDraw() {
        ImGui.SetNextWindowSize(new Vector2(960, 540), ImGuiCond.FirstUseEver);
    }

    public RawExcelSheet? GetSheet(string name) {
        if (this._sheetsCache.TryGetValue(name, out var sheet)) return sheet;

        sheet = Services.GameData.Excel.GetSheetRaw(name);
        this._sheetsCache[name] = sheet;

        return sheet;
    }

    public void OpenSheet(string name, int? scrollTo = null) {
        if (scrollTo is not null) this._tempScroll = scrollTo;

        var sheet = this.GetSheet(name);
        if (sheet is null) {
            Log.Warning("Tried to open sheet that doesn't exist: {name}", name);
            return;
        }

        Log.Debug("Opening sheet: {name}", name);

        this._selectedSheet = sheet;
        this._filteredRows = null;
        this.ResolveSheetDefinition();
        this.SetupRows();
        this.ResolveFilter();

        this.WindowOpen = true;
        this._itemHeight = 0f;
    }

    public void ReloadAllSheets() {
        this._sheetsCache.Clear();
        this._selectedSheet = this.GetSheet(this._selectedSheet!.Name);
    }

    private void ResolveSheetDefinition() {
        var sheetName = this._selectedSheet!.Name;
        if (this._sheetDefinitions.ContainsKey(sheetName)) return;

        var url =
            $"https://raw.githubusercontent.com/xivapi/SaintCoinach/master/SaintCoinach/Definitions/{sheetName}.json";

        this._httpClient.GetAsync(url).ContinueWith(t => {
            try {
                var result = t.Result;
                if (result.IsSuccessStatusCode) {
                    var json = result.Content.ReadAsStringAsync().Result;

                    var sheetDefinition = JsonSerializer.Deserialize<SheetDefinition>(json);
                    if (sheetDefinition is null) {
                        Log.Error("Failed to deserialize sheet definition");
                        return;
                    }

                    Log.Debug("Resolved sheet definition: {sheetName} -> {sheetDefinition}",
                        sheetName,
                        sheetDefinition);

                    this._sheetDefinitions[sheetName] = sheetDefinition;
                } else {
                    Log.Error("Request for sheet definition failed: {sheetName} -> {statusCode}",
                        sheetName,
                        result.StatusCode);

                    this._sheetDefinitions[sheetName] = null;
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to resolve sheet definition");
            }
        });
    }


    // TODO deduplicate this code from fs module
    internal override void Draw() {
        var temp = ImGui.GetCursorPosY();
        ImGui.SetNextItemWidth(this._sidebarWidth);
        ImGui.InputText("##ExcelFilter", ref this._sidebarFilter, 1024);

        var cra = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("##ExcelModule_Sidebar", cra with { X = this._sidebarWidth }, true);

        foreach (var sheet in this._sheets) {
            if (!string.IsNullOrEmpty(this._sidebarFilter) &&
                !sheet.Contains(this._sidebarFilter, StringComparison.OrdinalIgnoreCase)) continue;

            if (ImGui.Selectable(sheet, sheet == this._selectedSheet?.Name)) {
                this.OpenSheet(sheet);
            }
        }

        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        UiUtils.HorizontalSplitter(ref this._sidebarWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosY(temp);

        ImGui.BeginGroup();
        if (this._selectedSheet != null) {
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(width);

            var shouldRed = this._scriptError is not null;
            if (shouldRed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
            var shouldOrange = this._cancellationToken is not null && !shouldRed;
            if (shouldOrange) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.5f, 0f, 1f));

            var flags = ImGuiInputTextFlags.EnterReturnsTrue;
            if (ImGui.InputText("##ExcelContentFilter", ref this._contentFilter, 1024, flags)) {
                this.ResolveFilter();
            }

            // Disable filter on right click
            if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                this._contentFilter = string.Empty;
                this.ResolveFilter();
            }

            if (shouldRed) {
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(this._scriptError ?? "Unknown error");
                    ImGui.EndTooltip();
                }
            }

            if (shouldOrange) {
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered()) {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(
                        "A script is currently running, which will drastically impact performance.\n"
                        + "To stop the script, empty or right click the input box.");
                    ImGui.EndTooltip();
                }
            }

            ImGui.SetNextItemWidth(width);
            this.DrawTable();
        }

        ImGui.EndGroup();
    }

    private void SetupRows() {
        var sheet = this._selectedSheet;
        if (sheet is null) return;

        var currentRow = 0u;
        foreach (var page in sheet.DataPages) {
            foreach (var row in page.File.RowData.Values) {
                var parser = new RowParser(sheet, page.File);
                parser.SeekToRow(row.RowId);

                if (sheet.Header.Variant == ExcelVariant.Subrows) {
                    for (uint i = 0; i < parser.RowCount; i++) {
                        // Build a new parser for every subrow - this is VERY inefficient
                        this._rowMapping[currentRow] = (row.RowId, i);
                        currentRow++;
                    }
                } else {
                    this._rowMapping[currentRow] = (row.RowId, null);
                    currentRow++;
                }
            }
        }
    }

    private RowParser? GetRow(uint index) {
        var sheet = this._selectedSheet;
        if (sheet is null) return null;

        var (row, subrow) = this._rowMapping[index];

        var page = sheet.DataPages.FirstOrDefault(x => x.File.RowData.ContainsKey(row));
        if (page is null) return null;

        var parser = new RowParser(sheet, page.File);
        if (subrow is not null) {
            parser.SeekToRow(row, subrow.Value);
        } else {
            parser.SeekToRow(row);
        }

        return parser;
    }

    private void DrawTable() {
        if (!this._sheetDefinitions.TryGetValue(this._selectedSheet!.Name, out var sheetDefinition)) return;

        var rowCount = this._selectedSheet.RowCount;
        var colCount = this._selectedSheet.ColumnCount;
        colCount = Math.Min(colCount, 63); // holy shit

        var flags = ImGuiTableFlags.Borders
                    | ImGuiTableFlags.NoSavedSettings
                    | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.Resizable
                    | ImGuiTableFlags.ScrollX
                    | ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable("##ExcelTable", (int)(colCount + 1), flags)) {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);

        ImGui.TableHeadersRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TableHeader("Row");
        for (var i = 0; i < colCount; i++) {
            var colName = sheetDefinition?.GetNameForColumn(i) ?? i.ToString();

            ImGui.TableSetColumnIndex(i + 1);
            ImGui.TableHeader(colName);

            if (ImGui.IsItemHovered()) {
                var col = this._selectedSheet.Columns[i];
                var offset = col.Offset;
                var str = $"Offset: {offset} (0x{offset:X})\nIndex: {i}\nData type: {col.Type.ToString()}";

                ImGui.BeginTooltip();
                ImGui.TextUnformatted(str);
                ImGui.EndTooltip();
            }
        }

        var actualRowCount = this._filteredRows?.Count ?? (int)rowCount;
        var clipper = new ListClipper(actualRowCount, itemHeight: this._itemHeight);

        // Sheets can have non-linear row IDs, so we use the index the row appears in the sheet instead of the row ID
        var newHeight = 0f;
        foreach (var i in clipper.Rows) {
            var rowId = i;
            if (this._filteredRows is not null) {
                rowId = (int)this._filteredRows[i];
            }

            var row = this.GetRow((uint)rowId);
            if (row is null) {
                ImGui.TableNextRow();
                continue;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var str = row.RowId.ToString();
            if (row.SubRowId != 0) str += $".{row.SubRowId}";
            ImGui.TextUnformatted(str);
            ImGui.TableNextColumn();

            for (var col = 0; col < colCount; col++) {
                var obj = row.ReadColumnRaw(col);
                if (obj != null) {
                    var converter = sheetDefinition?.GetConverterForColumn(col);

                    var prev = ImGui.GetCursorPosY();
                    this.DrawEntry(rowId, col, obj, converter);
                    var next = ImGui.GetCursorPosY();

                    var height = next - prev;
                    var needed = this._itemHeight - height;
                    if (needed > 0) {
                        ImGui.Dummy(new Vector2(0, needed));
                    }

                    if (height > newHeight) newHeight = height;
                }

                if (col < colCount - 1) ImGui.TableNextColumn();
            }
        }

        if (newHeight > this._itemHeight) {
            this._itemHeight = newHeight;
        }

        // I don't know why I need to do this but I really don't care, it's 12 AM and I want sleep
        // seems to crash if you scroll immediately, seems to do nothing if you scroll too little
        // stupid tick hack works for now lol
        if (this._tempScroll is not null & this._paintTicksLeft == -1) {
            this._paintTicksLeft = 5;
        } else if (this._paintTicksLeft <= 0) {
            this._tempScroll = null;
            this._paintTicksLeft = -1;
        } else if (this._tempScroll is not null) {
            ImGui.SetScrollY(this._tempScroll.Value * clipper.ItemsHeight);
            this._paintTicksLeft--;
        }

        clipper.End();
        ImGui.EndTable();
    }

    private void DrawEntry(int row, int col, object data, ConverterDefinition? converter) {
        // Spam try/catch here because we don't want to throw an error mid ImGui frame
        // No idea why conversions fail sometimes

        switch (converter) {
            case LinkConverterDefinition link when link.Target != null: {
                var targetRow = 0;
                try {
                    targetRow = Convert.ToInt32(data);
                } catch {
                    // ignored
                }

                var text = $"{link.Target}#{targetRow}" + $"##{row}_{col}";

                if (ImGui.Button(text)) {
                    Services.ModuleManager.GetModule<ExcelModule>().OpenSheet(link.Target, targetRow);
                }

                break;
            }

            case IconConverterDefinition: {
                var iconId = 0u;
                try {
                    iconId = Convert.ToUInt32(data);
                } catch {
                    // ignored
                }

                var icon = Services.ImageHandler.GetIcon(iconId);
                if (icon is not null) {
                    var path = icon.FilePath;
                    var handle = Services.ImageHandler.DisplayTex(icon);
                    if (handle == IntPtr.Zero) break;

                    var size = new Vector2(icon.Header.Width, icon.Header.Height);
                    if (size.Y > 512) size *= 512 / size.Y;
                    ImGui.Image(handle, size);

                    var shouldShowMagnum = ImGui.IsKeyDown(ImGui.GetKeyIndex(ImGuiKey.ModAlt)) && ImGui.IsItemHovered();
                    if (shouldShowMagnum) {
                        ImGui.BeginTooltip();
                        ImGui.Image(handle, size * 2);
                        ImGui.EndTooltip();
                    }

                    if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                        ImGui.MenuItem(path, false);

                        if (ImGui.MenuItem("Copy icon ID")) {
                            ImGui.SetClipboardText(iconId.ToString());
                        }

                        if (ImGui.MenuItem("Copy icon path")) {
                            ImGui.SetClipboardText(path);
                        }

                        if (ImGui.MenuItem("Open in filesystem browser")) {
                            Services.ModuleManager.GetModule<FilesystemModule>().OpenFile(path);
                        }

                        if (ImGui.MenuItem("Save")) {
                            FileUtils.Save(icon.Data, "tex");
                        }

                        ImGui.EndPopup();
                    }
                } else {
                    ImGui.BeginDisabled();
                    ImGui.TextUnformatted($"(couldn't load icon {iconId})");
                    ImGui.EndDisabled();
                }

                break;
            }

            case ComplexLinkConverterDefinition complex: {
                var targetRow = 0;
                try {
                    targetRow = Convert.ToInt32(data);
                } catch {
                    // ignored
                }

                var keyValues = new Dictionary<string, object>();
                // We need to be being parsed *from* a sheet definition, so these !s are safe
                var thisRow = this._selectedSheet!.GetRow((uint)row)!;

                for (var i = 0; i < this._selectedSheet!.ColumnCount; i++) {
                    if (!this._sheetDefinitions.ContainsKey(this._selectedSheet.Name)) continue;
                    var sheetDef = this._sheetDefinitions[this._selectedSheet.Name];
                    if (sheetDef is null) continue;

                    var colName = sheetDef.GetNameForColumn(i);
                    var colValue = thisRow.ReadColumnRaw(i);
                    if (colName is null || colValue is null) continue;

                    keyValues[colName] = colValue;
                }

                var resolvedLinks = complex.ResolveComplexLink(keyValues);
                foreach (var link in resolvedLinks) {
                    var text = $"{link}#{targetRow}" + $"##{row}_{col}";

                    if (ImGui.Button(text)) {
                        Services.ModuleManager.GetModule<ExcelModule>().OpenSheet(link, targetRow);
                    }
                }

                break;
            }

            default: {
                var str = data.ToString();
                if (data is SeString seString) {
                    str = UiUtils.DisplaySeString(seString);
                }

                if (str is null) break;

                ImGui.TextUnformatted(str);

                if (ImGui.BeginPopupContextItem($"{row}_{col}")) {
                    var fileExists = false;
                    try {
                        fileExists = Services.GameData.FileExists(str);
                    } catch {
                        // ignored
                    }

                    if (fileExists && ImGui.MenuItem("Open in filesystem browser")) {
                        Services.ModuleManager.GetModule<FilesystemModule>().OpenFile(str);
                    }

                    if (ImGui.MenuItem("Copy")) {
                        ImGui.SetClipboardText(str);
                    }

                    ImGui.EndPopup();
                }

                break;
            }
        }
    }

    private void ResolveFilter() {
        Log.Debug("Resolving filter...");

        // clean up scripts
        if (this._cancellationToken is not null) {
            this._cancellationToken.Cancel();
            this._cancellationToken.Dispose();
        }

        this._scriptError = null;

        if (string.IsNullOrEmpty(this._contentFilter)) {
            this._filteredRows = null;
            return;
        }

        if (this._selectedSheet is null) {
            this._filteredRows = null;
            return;
        }

        this._filteredRows = new();
        if (this._contentFilter.StartsWith("$")) {
            var script = this._contentFilter[1..];
            this.FilterScript(script);
        } else {
            this.FilterSimple(this._contentFilter);
        }

        this._itemHeight = 0;
        Log.Debug("Filter resolved!");
    }

    private void FilterSimple(string filter) {
        var colCount = this._selectedSheet!.ColumnCount;
        for (var i = 0u; i < this._selectedSheet.RowCount; i++) {
            var row = this.GetRow(i);
            if (row is null) continue;

            for (var col = 0; col < colCount; col++) {
                var obj = row.ReadColumnRaw(col);
                var str = obj?.ToString();
                if (str is null) continue;

                if (str.ToLower().Contains(filter.ToLower())) {
                    this._filteredRows!.Add(i);
                    break;
                }
            }
        }
    }

    private void FilterScript(string script) {
        this._scriptError = null;

        // picked a random type for this, doesn't really matter
        var luminaTypes = Assembly.GetAssembly(typeof(Lumina.Excel.GeneratedSheets.Addon))?.GetTypes();
        var sheets = luminaTypes?
            .Where(t => t.GetCustomAttributes(typeof(SheetAttribute), false).Length > 0)
            .ToDictionary(t => ((SheetAttribute)t.GetCustomAttributes(typeof(SheetAttribute), false)[0]).Name);

        Type? sheetRow = null;
        if (sheets?.TryGetValue(this._selectedSheet!.Name, out var sheetType) == true) {
            sheetRow = sheetType;
        }

        // GameData.GetExcelSheet<T>();
        var getExcelSheet = typeof(GameData).GetMethod("GetExcelSheet", Type.EmptyTypes);
        var genericMethod = sheetRow is not null ? getExcelSheet?.MakeGenericMethod(sheetRow) : null;
        var sheetInstance = genericMethod?.Invoke(Services.GameData, Array.Empty<object>());

        var ct = new CancellationTokenSource();

        Task.Run(async () => {
            var globalsType = sheetRow != null
                ? typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow)
                : null;
            var expr = CSharpScript.Create<bool>(script, globalsType: globalsType);
            expr.Compile(ct.Token);

            for (var i = 0u; i < this._selectedSheet!.RowCount; i++) {
                if (ct.IsCancellationRequested) {
                    Log.Debug("Filter script cancelled - aborting");
                    return;
                }

                var row = this.GetRow(i);
                if (row is null) continue;
                var i1 = i;

                async void SimpleEval() {
                    try {
                        var res = await expr.RunAsync(cancellationToken: ct.Token);
                        if (res.ReturnValue) this._filteredRows?.Add(i1);
                    } catch (Exception e) {
                        this._scriptError = e.Message;
                    }
                }

                if (sheetRow is null) {
                    SimpleEval();
                } else {
                    object? instance;
                    if (row.SubRowId == 0) {
                        // sheet.GetRow(row.RowId);
                        var getRow = sheetInstance?.GetType().GetMethod("GetRow", new[] { typeof(uint) });
                        instance = getRow?.Invoke(sheetInstance, new object[] { row.RowId });
                    } else {
                        // sheet.GetRow(row.RowId, row.SubRowId);
                        var getRow = sheetInstance?.GetType().GetMethod("GetRow", new[] { typeof(uint), typeof(uint) });
                        instance = getRow?.Invoke(sheetInstance, new object[] { row.RowId, row.SubRowId });
                    }

                    // new ExcelScriptingGlobal<ExcelRow>(sheet, row);
                    var excelScriptingGlobal = typeof(ExcelScriptingGlobal<>).MakeGenericType(sheetRow);
                    var globals = Activator.CreateInstance(excelScriptingGlobal, sheetInstance, instance);
                    if (globals is null) {
                        SimpleEval();
                    } else {
                        try {
                            var res = await expr.RunAsync(globals, ct.Token);
                            if (res.ReturnValue) this._filteredRows?.Add(i1);
                        } catch (Exception e) {
                            this._scriptError = e.Message;
                        }
                    }
                }
            }

            Log.Debug("Filter script finished");
            this._cancellationToken = null;
            ct.Dispose();
        }, ct.Token);

        this._cancellationToken = ct;
    }
}
