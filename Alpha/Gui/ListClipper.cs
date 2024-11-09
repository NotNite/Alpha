using System.Collections;
using Hexa.NET.ImGui;

namespace Alpha.Gui;

public unsafe class ListClipper : IEnumerable<(int, int)> {
    private ImGuiListClipper clipper;
    private readonly int rows;
    private readonly int columns;
    private readonly bool twoDimensional;
    private readonly int itemRemainder;

    public int FirstRow { get; private set; } = -1;
    public int LastRow => this.CurrentRow;
    public int CurrentRow { get; private set; }
    public bool IsStepped => this.CurrentRow == this.DisplayStart;
    public int DisplayStart => this.clipper.DisplayStart;
    public int DisplayEnd => this.clipper.DisplayEnd;
    public float ItemsHeight => this.clipper.ItemsHeight;

    public IEnumerable<int> Rows {
        get {
            while (this.clipper.Step()) {
                if (this.clipper.ItemsHeight > 0 && this.FirstRow < 0) {
                    this.FirstRow = (int) (ImGui.GetScrollY() / this.clipper.ItemsHeight);
                }

                for (var i = this.clipper.DisplayStart; i < this.clipper.DisplayEnd; i++) {
                    this.CurrentRow = i;
                    yield return this.twoDimensional ? i : i * this.columns;
                }
            }
        }
    }

    public IEnumerable<int> Columns {
        get {
            var cols = this.itemRemainder == 0
                       || this.rows != this.DisplayEnd
                       || this.CurrentRow != this.DisplayEnd - 1
                           ? this.columns
                           : this.itemRemainder;

            for (var j = 0; j < cols; j++)
                yield return j;
        }
    }

    public ListClipper(int items, int cols = 1, bool twoD = false, float itemHeight = 0) {
        this.twoDimensional = twoD;
        this.columns = cols;
        this.rows = this.twoDimensional ? items : (int) MathF.Ceiling((float) items / this.columns);
        this.itemRemainder = !this.twoDimensional ? items % this.columns : 0;
        this.clipper = new ImGuiListClipper();
        this.clipper.Begin(this.rows, itemHeight);
    }

    public void End() {
        this.clipper.End();
    }

    public IEnumerator<(int, int)> GetEnumerator() =>
        (from i in this.Rows from j in this.Columns select (i, j)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
