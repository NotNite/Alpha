namespace Alpha.Services.Excel;

public interface ISchemaResolver {
    public Task<ISheetDefinition?> GetDefinition(string name);
}
