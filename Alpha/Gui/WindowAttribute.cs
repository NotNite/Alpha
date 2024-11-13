namespace Alpha.Gui;

public class WindowAttribute(string name) : Attribute {
    public string Name = name;
    public bool SingleInstance;
    public bool ShowInMenu = true;
}
