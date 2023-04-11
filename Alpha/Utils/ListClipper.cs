using System.Collections;
using ImGuiNET;

namespace Alpha.Utils;

// Copied from https://github.com/UnknownX7/Hypostasis/blob/master/ImGui/ListClipper.cs
// (with permission - thank you UnknownX!)
public unsafe class ListClipper : IEnumerable<(int, int)> {
    private ImGuiListClipperPtr _clipper;
    private readonly int _rows;
    private readonly int _columns;
    private readonly bool _twoDimensional;
    private readonly int _itemRemainder;

    public int FirstRow { get; private set; } = -1;
    public int LastRow => this.CurrentRow;
    public int CurrentRow { get; private set; }
    public bool IsStepped => this.CurrentRow == this.DisplayStart;
    public int DisplayStart => this._clipper.DisplayStart;
    public int DisplayEnd => this._clipper.DisplayEnd;
    public float ItemsHeight => this._clipper.ItemsHeight;

    public IEnumerable<int> Rows {
        get {
            while (this._clipper.Step()) {
                if (this._clipper.ItemsHeight > 0 && this.FirstRow < 0) {
                    this.FirstRow = (int)(ImGui.GetScrollY() / this._clipper.ItemsHeight);
                }

                for (var i = this._clipper.DisplayStart; i < this._clipper.DisplayEnd; i++) {
                    this.CurrentRow = i;
                    yield return this._twoDimensional ? i : i * this._columns;
                }
            }
        }
    }

    public IEnumerable<int> Columns {
        get {
            var cols = this._itemRemainder == 0
                       || this._rows != this.DisplayEnd
                       || this.CurrentRow != this.DisplayEnd - 1
                ? this._columns
                : this._itemRemainder;

            for (var j = 0; j < cols; j++)
                yield return j;
        }
    }

    public ListClipper(int items, int cols = 1, bool twoD = false, float itemHeight = 0) {
        this._twoDimensional = twoD;
        this._columns = cols;
        this._rows = this._twoDimensional ? items : (int)MathF.Ceiling((float)items / this._columns);
        this._itemRemainder = !this._twoDimensional ? items % this._columns : 0;
        this._clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
        this._clipper.Begin(this._rows, itemHeight);
    }

    public void End() {
        this._clipper.End();
        this._clipper.Destroy();
    }

    public IEnumerator<(int, int)> GetEnumerator() =>
        (from i in this.Rows from j in this.Columns select (i, j)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
