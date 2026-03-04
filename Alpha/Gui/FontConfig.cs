namespace Alpha.Gui;

public record FontConfig(string Path = "", int Size = 13, bool FallbackOnly = false) {
    public string Path = Path;
    public int Size = Size;
    public bool FallbackOnly = FallbackOnly;
}
