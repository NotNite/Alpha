namespace Alpha.Gui;

public record FontConfig(string Path = "", int Size = 13, bool JapaneseGlyphs = false) {
    public string Path = Path;
    public int Size = Size;
    public bool JapaneseGlyphs = JapaneseGlyphs;
}
