using System.IO.Pipes;
using Alpha.Core;
using Alpha.Proto;
using Google.Protobuf;
using ImGuiNET;
using Serilog;

namespace Alpha.Modules;

public class OmegaModule : SimpleModule {
    private CancellationTokenSource? _ct;
    private NamedPipeClientStream? _client;

    public OmegaModule() : base("Omega", "Omega") { }

    internal override void SimpleDraw() {
        ImGui.TextUnformatted($"Connected: {this._client?.IsConnected ?? false}");

        if (ImGui.Button("Connect")) {
            this.Connect();
        }
    }

    private void Connect() {
        if (this._ct is not null || this._client is not null) {
            Log.Warning("Trying to start client while it's already running?");

            this._client?.Close();
            this._client?.Dispose();

            this._ct?.Cancel();
            this._ct?.Dispose();
        }

        this._ct = new CancellationTokenSource();
        Task.Run(() => {
            try {
                this.ConnectInternal();
            } catch (Exception e) {
                Log.Error(e, "Omega connect loop error");
            }

            this._ct.Dispose();
            this._ct = null;
        }, this._ct.Token);
    }

    private void ConnectInternal() {
        this._client = new NamedPipeClientStream("Omega");

        Log.Debug("Connecting to Omega...");
        this._client.Connect();
        Log.Debug("Connected to Omega!");

        new C2SMessage {
            Ping = new Ping()
        }.WriteDelimitedTo(this._client);

        while (this._client.IsConnected) {
            var msg = S2CMessage.Parser.ParseDelimitedFrom(this._client);

            switch (msg.MessageCase) {
                case S2CMessage.MessageOneofCase.Pong: {
                    Log.Debug("Got pong: {GameVersion}", msg.Pong.GameVersion);
                    break;
                }

                default:
                    Log.Warning("Unknown message type: {Message}", msg);
                    break;
            }
        }

        this._client.Close();
    }
}
