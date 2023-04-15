using System.Globalization;
using System.Numerics;
using Alpha.Core;
using Alpha.Modules;
using Alpha.Utils;
using ImGuiNET;
using Serilog;

namespace Alpha.Windows;

public class MemoryWindow : Window {
    private readonly MemoryModule _module;
    private readonly OmegaModule _omega;

    private long? _base;
    private long _scrollPos;
    private long? _scrollTo;

    private long? _selectedByte;
    private long? _editingByte;
    private string? _editingStr;
    private Dictionary<long, byte> _stagedBytes = new();

    public MemoryWindow(MemoryModule module, OmegaModule omega) {
        this.Name = "Memory Viewer";
        this._module = module;
        this._omega = omega;
        this.InitialSize = new(700, 540);
    }

    protected override void Draw() {
        if (!this._omega.IsConnected || this._omega.TextBase is null) {
            ImGui.Text("Not connected to Omega");
            return;
        }

        this._base ??= this._omega.TextBase;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0));

        var memorySize = ImGui.GetContentRegionAvail() with { X = 540 };
        var memoryFlags = ImGuiWindowFlags.NoScrollbar;
        ImGui.BeginChild("##MemoryViewer", memorySize, true, memoryFlags);
        this.DrawMemory();
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##MemoryViewerSidebar", ImGui.GetContentRegionAvail(), true);
        ImGui.PopStyleVar();
        this.DrawSidebar();
        ImGui.EndChild();
    }

    private byte GetByte(long pos) {
        if (this._stagedBytes.TryGetValue(pos, out var b)) {
            return b;
        }

        // round to nearest 0x10
        var chunk = pos - pos % 0x10;
        var offset = (int)(pos - chunk);

        return this._module.Memory.TryGetValue(chunk, out var bytes)
            ? bytes[offset]
            : (byte)0;
    }

    // Stolen from uDev :nikofingerguns:
    private void DrawMemory() {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4));

        var style = ImGui.GetStyle();
        var itemSpacingX = style.ItemSpacing.X;
        var spaceX = ImGui.CalcTextSize(" ").X;
        var bytesStr = string.Join("", Enumerable.Repeat("0", 12));
        var bytesX = ImGui.CalcTextSize(bytesStr).X + spaceX * 2 + (spaceX * 2 + itemSpacingX);
        var stringX = bytesX + 1 * (spaceX + itemSpacingX) + (spaceX * 2 + itemSpacingX) * 16;

        var rows = (int)Math.Floor(int.MaxValue / 16f / 2);
        var white = new Vector4(1f, 1f, 1f, 1f);
        var grey = new Vector4(0.5f, 0.5f, 0.5f, 1f);
        var aqua = new Vector4(1f, 1f, 1f, 1f);
        var orange = new Vector4(1f, 0.5f, 0f, 1f);

        var clipper = new ListClipper(rows * 16, 16);

        var startRow = long.MaxValue;
        var endRow = 0L;

        foreach (var row in clipper.Rows) {
            var realRow = row + this._base!.Value;
            startRow = Math.Min(startRow, realRow);
            endRow = Math.Max(endRow, realRow);

            var bytes = this._module.Memory.TryGetValue(realRow, out var value)
                ? value
                : new byte[16];

            ImGui.TextColored(aqua, $"{realRow:X12}");
            ImGui.SameLine(bytesX);

            var bytesPos = bytesX;
            foreach (var col in clipper.Columns) {
                var pos = realRow + col;
                var b = bytes[col];

                var staged = false;
                if (this._stagedBytes.TryGetValue(pos, out var stagedByte)) {
                    b = stagedByte;
                    staged = true;
                }

                if (this._editingByte == pos) {
                    ImGui.SetKeyboardFocusHere();
                    var flags = ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue;

                    ImGui.PushItemWidth(spaceX * 2 + itemSpacingX);

                    var shouldCancel = ImGui.IsItemDeactivatedAfterEdit()
                                       || ImGui.IsKeyPressed(ImGui.GetKeyIndex(ImGuiKey.Escape))
                                       || ImGui.IsKeyPressed(ImGui.GetKeyIndex(ImGuiKey.Tab))
                                       || (
                                           ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                                           && !ImGui.IsItemHovered()
                                       );

                    if (ImGui.InputText("##MemoryViewerEditing", ref this._editingStr, 2, flags)) {
                        var newValue = byte.Parse(this._editingStr, NumberStyles.HexNumber);
                        this._stagedBytes[pos] = newValue;

                        this._editingByte++;
                        this._editingStr = this.GetByte(this._editingByte!.Value).ToString("X2");
                    } else if (shouldCancel) {
                        this._editingByte = null;
                        this._editingStr = null;
                    }

                    ImGui.PopItemWidth();
                } else {
                    var color = staged
                        ? orange
                        : b != 0
                            ? white
                            : grey;
                    ImGui.TextColored(color, b.ToString("X2"));

                    if (ImGui.IsItemClicked()) {
                        this._editingByte = pos;
                        this._editingStr = b.ToString("X2");
                    }
                }

                bytesPos += spaceX * 2 + itemSpacingX;
                ImGui.SameLine(bytesPos);
            }

            ImGui.SameLine(stringX);
            var strPos = stringX;
            for (var b = 0; b < 16; b++) {
                var c = bytes[b];
                if (c is < 32 or > 126) {
                    ImGui.TextColored(grey, ".");
                } else {
                    ImGui.TextUnformatted(((char)c).ToString());
                }

                strPos += spaceX;
                if (b < 15) ImGui.SameLine(strPos);
            }
        }

        if (this._scrollTo is not null) {
            var scrollY = (this._scrollTo.Value - this._base!.Value) * clipper.ItemsHeight;
            Log.Debug("Scrolling to {ScrollY}", scrollY);
            ImGui.SetScrollY(scrollY);
            this._scrollTo = null;
        }

        this._scrollPos = clipper.DisplayStart - 1;

        if (startRow != long.MaxValue) {
            var roundedStart = startRow - startRow % 16;
            var roundedEnd = endRow - endRow % 16 + 16;
            this._module.EnsureMemory(roundedStart, roundedEnd);
        }

        ImGui.PopStyleVar(2);
        clipper.End();
    }

    private void DrawSidebar() {
        if (this._stagedBytes.Count > 0) {
            ImGui.TextUnformatted($"{this._stagedBytes.Count} staged bytes");

            if (ImGui.Button("Apply changes")) {
                this._module.WriteMemory(this._stagedBytes);
                this._stagedBytes.Clear();
            }

            if (ImGui.Button("Discard changes")) {
                this._stagedBytes.Clear();
            }

            ImGui.Separator();
        }

        var baseForReal = this._base!.Value.ToString("X12");
        var baseFlags = ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue;
        if (ImGui.InputText("Base", ref baseForReal, 12, baseFlags)) {
            this._base = long.Parse(baseForReal, NumberStyles.HexNumber);
        }

        var scrollPosForReal = (this._base.Value + this._scrollPos).ToString("X12");
        var scrollPosFlags = ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue;
        if (ImGui.InputText("Scroll position", ref scrollPosForReal, 12, scrollPosFlags)) {
            var a = long.Parse(scrollPosForReal, NumberStyles.HexNumber);
            this._scrollTo = Math.Max(0, a - this._base!.Value);
        }

        ImGui.Separator();
    }
}
