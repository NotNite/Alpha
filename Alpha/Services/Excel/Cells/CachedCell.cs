using ImGuiNET;

namespace Alpha.Services.Excel.Cells;

public class CachedCell(Cell cell) {
    public double LastUsed = ImGui.GetTime();

    public Cell Value {
        get {
            this.LastUsed = ImGui.GetTime();
            return cell;
        }
    }
}
