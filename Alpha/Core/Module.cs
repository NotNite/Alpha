namespace Alpha.Core;

public class Module {
    public readonly string Name;
    public readonly string? Category;

    protected Module(string name, string? category = null) {
        this.Name = name;
        this.Category = category;
    }

    internal virtual void OnClick() { }
    internal virtual void Draw() { }
    
    // Tried using a getter for this, didn't work. Not going to question it
    internal virtual bool IsEnabled() => true;
}
