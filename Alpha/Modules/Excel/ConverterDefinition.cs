using System.Text.Json;
using System.Text.Json.Serialization;
using Lumina.Excel;

#pragma warning disable CS8618
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Alpha.Modules.Excel;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(LinkConverterDefinition), "link")]
[JsonDerivedType(typeof(IconConverterDefinition), "icon")]
[JsonDerivedType(typeof(TomestoneConverterDefinition), "tomestone")]
[JsonDerivedType(typeof(ComplexLinkConverterDefinition), "complexlink")]
public class ConverterDefinition {
    [JsonPropertyName("type")] public string? Type { get; init; }
}

public class LinkConverterDefinition : ConverterDefinition {
    [JsonPropertyName("target")] public string? Target { get; init; }
}

public class IconConverterDefinition : ConverterDefinition { }
public class TomestoneConverterDefinition : ConverterDefinition { }

public class ComplexLinkConverterDefinition : ConverterDefinition {
    [JsonPropertyName("links")] public ComplexLink[] Links { get; init; }

    private bool Compare(object a, JsonElement b) {
        try {
            switch (b.ValueKind) {
                case JsonValueKind.True or JsonValueKind.False:
                    if (a is bool boolean) return boolean == b.GetBoolean();
                    break;

                case JsonValueKind.Number:
                    var number = Convert.ToInt64(a);
                    return number == b.GetInt64();

                case JsonValueKind.String:
                    if (a is string str) return str == b.GetString();
                    break;
            }
        } catch {
            // ignored, for number casting
        }

        return false;
    }

    private bool CheckWhenClause(
        ExcelModule excel,
        RawExcelSheet sheet,
        int rowId,
        WhenClause when
    ) {
        if (!excel.SheetDefinitions.TryGetValue(sheet.Name, out var def)) return false;
        if (def is null) return false;

        for (var i = 0; i < sheet.ColumnCount; i++) {
            var colName = def.GetNameForColumn(i);
            if (colName == when.Key) {
                var value = sheet.GetRow((uint) rowId)?.ReadColumnRaw(i);
                if (value is null) continue;
                if (this.Compare(value, when.Value)) return true;
            }
        }

        return false;
    }

    public IEnumerable<ComplexLinkResolution> ResolveComplexLink(
        ExcelModule excel,
        RawExcelSheet sheet,
        int rowId,
        int targetRowId
    ) {
        var ret = new List<ComplexLinkResolution>();

        foreach (var link in this.Links) {
            if (link.When is not null
                && !this.CheckWhenClause(excel, sheet, rowId, link.When)) {
                continue;
            }

            if (link.Project is not null) {
                // I don't know how the fuck this works perch save me
                continue;
            }

            var resolution = new ComplexLinkResolution {
                Link = link.Sheets[0],
                TargetRow = targetRowId
            };
            ret.Add(resolution);
        }

        return ret;
    }

    public class ComplexLinkResolution {
        public string Link;
        public int TargetRow;
    }
}
