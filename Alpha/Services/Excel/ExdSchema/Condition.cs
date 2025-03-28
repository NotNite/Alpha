namespace Alpha.Services.Excel.ExdSchema;

public record Condition {
    public string? Switch { get; set; }
    public Dictionary<int, List<string>>? Cases { get; set; }
}
