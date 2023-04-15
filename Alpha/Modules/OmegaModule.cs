using Alpha.Core;
using Alpha.Proto;
using Google.Protobuf;
using ImGuiNET;
using Serilog;
using WebSocketSharp;

namespace Alpha.Modules;

public class OmegaModule : SimpleModule {
    public bool IsConnected => this._client?.IsAlive ?? false;

    private WebSocket? _client;

    public long? TextBase { get; private set; }
    public long? DataBase { get; private set; }

    public delegate void MessageHandler(S2CMessage msg);

    public event MessageHandler? MessageReceived;

    public OmegaModule() : base("Omega Settings", "Omega") { }

    internal override void SimpleDraw() {
        ImGui.TextUnformatted($"Connected: {this.IsConnected}");

        if (ImGui.Button("Connect")) {
            this.Connect();
        }
    }

    public byte[] GetBytes(long start, long end) {
        var uuid = Guid.NewGuid().ToString();
        var msg = new C2SMessage {
            MemoryRequest = new MemoryRequest {
                Start = start,
                End = end,
                Uuid = uuid
            }
        };

        this.Send(msg);
        var wait = new AutoResetEvent(false);
        byte[] result = null!;

        void OnMessageReceived(S2CMessage msg2) {
            if (msg2.MessageCase == S2CMessage.MessageOneofCase.MemoryResult
                && msg2.MemoryResult.Uuid == uuid) {
                result = msg2.MemoryResult.Data.ToByteArray();
                wait.Set();
            }
        }

        this.MessageReceived += OnMessageReceived;
        wait.WaitOne();
        this.MessageReceived -= OnMessageReceived;

        return result;
    }

    private void Connect() {
        this._client?.Close();

        this._client = new WebSocket("ws://localhost:41784");
        this._client.OnOpen += (_, _) => {
            Log.Debug("Connected to Omega!");
            this.Send(new C2SMessage {
                Ping = new Ping()
            });
        };

        this._client.OnMessage += (_, e) => {
            var msg = S2CMessage.Parser.ParseFrom(e.RawData);
            this.MessageReceived?.Invoke(msg);

            switch (msg.MessageCase) {
                case S2CMessage.MessageOneofCase.Pong: {
                    Log.Debug("Got pong: {GameVersion}", msg.Pong.GameVersion);
                    this.TextBase = msg.Pong.TextBase;
                    this.DataBase = msg.Pong.DataBase;
                    break;
                }
            }
        };

        this._client.Connect();
    }

    public void Send(C2SMessage msg) {
        var bytes = msg.ToByteArray();
        this._client?.Send(bytes);
    }
}
