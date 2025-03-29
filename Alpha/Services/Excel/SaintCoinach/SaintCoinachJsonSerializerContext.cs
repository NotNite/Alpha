using System.Text.Json.Serialization;

namespace Alpha.Services.Excel.SaintCoinach;

[JsonSerializable(typeof(SaintCoinachSheetDefinition))]
[JsonSerializable(typeof(RepeatColumnDefinition))]
[JsonSerializable(typeof(GroupColumnDefinition))]
[JsonSerializable(typeof(SingleColumnDefinition))]
public partial class SaintCoinachJsonSerializerContext : JsonSerializerContext;

