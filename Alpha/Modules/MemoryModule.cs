using System.Collections.Concurrent;
using Alpha.Core;
using Alpha.Proto;
using Alpha.Windows;
using Google.Protobuf;
using Serilog;

namespace Alpha.Modules;

[Module(DependsOn = new[] { "OmegaModule" })]
public class MemoryModule : WindowedModule<MemoryWindow> {
    private OmegaModule _omega;

    public ConcurrentDictionary<long, byte[]> Memory = new();
    private List<(long, long)> _requested = new();
    private List<PositionUpdatePayload> _updates = new();

    public MemoryModule() : base("Memory Viewer", "Omega") {
        this._omega = Services.ModuleManager.GetModule<OmegaModule>();
        this._omega.MessageReceived += msg => {
            if (msg.MessageCase == S2CMessage.MessageOneofCase.MemoryUpdate) {
                var start = msg.MemoryUpdate.Address;
                var data = msg.MemoryUpdate.Data.ToByteArray();

                for (var i = 0; i < data.Length; i += 0x10) {
                    var key = start + i;
                    var section = data.AsSpan(i, 0x10).ToArray();
                    this.Memory[key] = section;
                }
            }
        };
    }

    internal override void OnClick() {
        this.OpenNewWindow();
    }

    private void OpenNewWindow() {
        var window = new MemoryWindow(this, this._omega);
        window.Open = true;
        this.Windows.Add(window);
    }

    public void EnsureMemory(long start, long end) {
        lock (this._requested) {
            var key = (start, end);
            if (this._requested.Contains(key)) {
                return;
            }

            Task.Run(() => {
                try {
                    var bytes = this._omega.GetBytes(start, end);
                    for (var j = 0; j < bytes.Length; j += 0x10) {
                        var key2 = start + j;
                        var section = bytes.AsSpan(j, 0x10).ToArray();
                        this.Memory[key2] = section;
                    }
                } catch (Exception e) {
                    Log.Error(e, "Failed to get memory");
                }

                this._requested.Remove(key);
            });

            this._requested.Add(key);
        }
    }

    public void WriteMemory(Dictionary<long, byte> bytes) {
        var memoryWrite = new MemoryWrite();
        foreach (var (pos, b) in bytes) {
            memoryWrite.Payloads.Add(new WritePayload {
                Address = pos,
                // this is stupid
                Data = ByteString.CopyFrom(b)
            });

            // round to nearest 0x10
            var chunk = pos - pos % 0x10;
            var offset = (int)(pos - chunk);

            this.Memory[chunk][offset] = b;
        }

        var msg = new C2SMessage {
            MemoryWrite = memoryWrite
        };

        this._omega.Send(msg);
    }

    internal override void Draw() {
        base.Draw();

        var positions = this.Windows.Where(x => x.Start is not null && x.End is not null)
            .Select(x => (x.Start!.Value, x.End!.Value))
            .ToList();

        var newUpdates = new List<PositionUpdatePayload>();
        foreach (var (start, end) in positions) {
            var overlaps = positions
                .Where(x =>
                    (x.Item1 >= start && x.Item1 <= end) ||
                    (x.Item2 >= start && x.Item2 <= end)
                )
                .ToList();

            var newStart = Math.Min(overlaps.Min(x => x.Item1), start);
            var newEnd = Math.Max(overlaps.Max(x => x.Item2), end);
            if (newUpdates.Any(x => x.Start == newStart && x.End == newEnd)) continue;

            newUpdates.Add(new PositionUpdatePayload {
                Start = newStart,
                End = newEnd
            });
        }

        if (newUpdates.SequenceEqual(this._updates)) return;

        var msg = new C2SMessage {
            MemoryPositionUpdate = new MemoryPositionUpdate {
                Payloads = { this._updates }
            }
        };
        this._omega.Send(msg);
        this._updates = newUpdates;
    }

    public byte[] GetBytes(long pos, int size) {
        var ret = new byte[size];

        for (var i = 0; i < size; i++) {
            var chunk = pos - pos % 0x10;
            var offset = (int)(pos - chunk);
            var section = this.Memory.TryGetValue(chunk, out var value) ? value : new byte[0x10];
            ret[i] = section[offset];
            pos++;
        }

        return ret;
    }
}
