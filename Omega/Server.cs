using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Alpha.Proto;
using Dalamud.Logging;
using Google.Protobuf;

namespace Omega;

public class Server : IDisposable {
    public bool IsConnected { get; private set; }
    private CancellationTokenSource? _ct;
    private NamedPipeServerStream? _server;

    public void Run() {
        if (this._ct is not null || this._server is not null) {
            PluginLog.Warning("Trying to start server while it's already running?");

            this._server?.Disconnect();
            this._server?.Dispose();

            this._ct?.Cancel();
            this._ct?.Dispose();
        }

        this._ct = new CancellationTokenSource();
        Task.Run(() => {
            while (true) {
                try {
                    this.RunInternal();
                } catch (Exception e) {
                    PluginLog.Error(e, "Omega server loop error");
                }

                this._server?.Disconnect();
                this._server?.Dispose();
            }
        }, this._ct.Token);
    }

    private void RunInternal() {
        this._server = new NamedPipeServerStream("Omega");
        PluginLog.Debug("Waiting for connection");

        this._server.WaitForConnectionAsync().Wait(this._ct!.Token);
        PluginLog.Debug("Got connection to pipe");

        while (this._server.IsConnected) {
            var msg = C2SMessage.Parser.ParseDelimitedFrom(this._server);

            switch (msg.MessageCase) {
                case C2SMessage.MessageOneofCase.Ping: {
                    PluginLog.Debug("Got ping, sending pong");
                    new S2CMessage {
                        Pong = new Pong {
                            GameVersion = File.ReadAllText("ffxivgame.ver")
                        }
                    }.WriteDelimitedTo(this._server);
                    break;
                }

                default:
                    PluginLog.Warning("Unknown message type: {Message}", msg.MessageCase);
                    break;
            }
        }

        PluginLog.Debug("Connection closed");
    }

    public void Dispose() {
        try {
            this._server?.Close();
            this._ct?.Cancel();
        } catch {
            // ignored
        }

        this._server?.Dispose();
        this._ct?.Dispose();
    }
}
