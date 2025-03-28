using System.Diagnostics.CodeAnalysis;
using Alpha.Gui.Windows;
using Hexa.NET.ImGui;

namespace Alpha.Services.Excel.Cells;

public class ModelCell : Cell {
    public const string CopyUnpackedValue = "Copy unpacked value";
    public const string CopyPackedValue = "Copy packed value";

    private readonly string rowColStr;
    private readonly string str;
    private readonly ulong? raw;

    [SetsRequiredMembers]
    public ModelCell(uint row, ushort? subrow, uint column, object? data) {
        this.Row = row;
        this.Subrow = subrow;
        this.Column = column;
        this.Data = data;
        this.rowColStr = $"{this.Row}_{this.Column}";

        if (data is uint rawModel) {
            var model = (ushort) rawModel;
            var variant = (byte) (rawModel >> 16);
            var stain = (byte) (rawModel >> 24);
            this.str = $"Model ({model}, {variant}, {stain})";
            this.raw = rawModel;
        } else if (data is ulong rawWeapon) {
            var skeleton = (ushort) rawWeapon;
            var model = (ushort) (rawWeapon >> 16);
            var variant = (ushort) (rawWeapon >> 32);
            var stain = (ushort) (rawWeapon >> 48);
            this.str = $"Weapon ({skeleton}, {model}, {variant}, {stain})";
            this.raw = rawWeapon;
        } else {
            this.str = "(couldn't unpack model)";
        }
    }

    public override void Draw(ExcelWindow window, bool inAnotherDraw = false) {
        if (this.raw is not null) {
            ImGui.TextUnformatted(this.str);

            if (ImGui.BeginPopupContextItem(this.rowColStr)) {
                ImGui.MenuItem(this.str, false, false);

                if (ImGui.MenuItem(CopyPackedValue)) ImGui.SetClipboardText(this.raw.ToString());
                if (ImGui.MenuItem(CopyUnpackedValue)) ImGui.SetClipboardText(this.raw.ToString());
                ImGui.EndPopup();
            }
        } else {
            ImGui.BeginDisabled();
            ImGui.TextUnformatted(this.str);
            ImGui.EndDisabled();
        }
    }
}
