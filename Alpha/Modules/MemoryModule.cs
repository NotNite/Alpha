using Alpha.Core;
using Alpha.Proto;
using Alpha.Windows;
using Google.Protobuf;
using Serilog;

namespace Alpha.Modules;

[Module(DependsOn = new[] { "OmegaModule" })]
public class MemoryModule : WindowedModule<MemoryWindow> {
    private OmegaModule _omega;

    public Dictionary<long, byte[]> Memory = new();
    private List<(long, long)> _requested = new();

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
        this.OpenNewWindow();
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
        var key = (start, end);
        if (this._requested.Contains(key)) {
            return;
        }

        for (var i = start; i < end; i += 0x10) {
            if (!this.Memory.ContainsKey(i)) {
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
                });

                this._requested.Add(key);
                this.Memory.Clear();
                return;
            }
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
}
