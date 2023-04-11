namespace Alpha.Core;

public class ModuleAttribute : Attribute {
    public string[] DependsOn = Array.Empty<string>();
}
