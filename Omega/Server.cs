using System;
using System.IO;
using System.Runtime.InteropServices;
using Alpha.Proto;
using Dalamud.Logging;
using Google.Protobuf;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Omega;

public class Server : IDisposable {
    private WebSocketServer _server;
    private readonly OmegaConnection _connection;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect,
        out uint lpflOldProtect);

    public Server() {
        this._server = new WebSocketServer(41784);
        this._connection = new OmegaConnection();
        this._server.AddWebSocketService("/", () => this._connection);
        this._server.Start();
    }

    public void Update() {
        this._connection.Update();
    }

    class OmegaConnection : WebSocketBehavior {
        private long? _reqStart;
        private long? _reqEnd;
        private byte[]? _lastReq;

        public void Update() {
            if (this._reqStart is null || this._reqEnd is null) return;
            // reset state on disconnect
            if (this.State != WebSocketState.Open) {
                this._reqStart = null;
                this._reqEnd = null;
                this._lastReq = null;
                return;
            }

            var len = this._reqEnd.Value - this._reqStart.Value;
            var bytes = new byte[len];

            for (var i = 0; i < len; i++) {
                unsafe {
                    var addr = this._reqStart.Value + i;
                    var ptr = (byte*)addr;
                    try {
                        bytes[i] = *ptr;
                    } catch {
                        // ignored
                    }
                }
            }

            if (this._lastReq?.Length != bytes.Length) {
                this._lastReq = null;
            }

            if (this._lastReq is not null) {
                var changed = false;
                for (var i = 0; i < len; i++) {
                    if (bytes[i] != this._lastReq[i]) {
                        changed = true;
                        break;
                    }
                }

                if (changed) {
                    var msg2 = new S2CMessage {
                        MemoryUpdate = new MemoryUpdate {
                            Address = this._reqStart.Value,
                            Data = ByteString.CopyFrom(bytes)
                        }
                    };

                    // only send after handdshake
                    if (this.State == WebSocketState.Open) {
                        this.Send(msg2.ToByteArray());
                    }
                }
            }

            this._lastReq = bytes;
        }

        protected override void OnMessage(MessageEventArgs e) {
            var msg = C2SMessage.Parser.ParseFrom(e.RawData);

            switch (msg.MessageCase) {
                case C2SMessage.MessageOneofCase.Ping: {
                    PluginLog.Debug("Got ping, sending pong");
                    var msg2 = new S2CMessage {
                        Pong = new Pong {
                            GameVersion = File.ReadAllText("ffxivgame.ver"),
                            TextBase = Services.SigScanner.TextSectionBase,
                            DataBase = Services.SigScanner.DataSectionBase
                        }
                    };
                    this.Send(msg2.ToByteArray());

                    break;
                }

                case C2SMessage.MessageOneofCase.MemoryRequest: {
                    this._reqStart = msg.MemoryRequest.Start;
                    this._reqEnd = msg.MemoryRequest.End;

                    var len = msg.MemoryRequest.End - msg.MemoryRequest.Start;
                    var bytes = new byte[len];
                    for (var i = 0; i < len; i++) {
                        unsafe {
                            var addr = (nint)msg.MemoryRequest.Start + i;
                            var ptr = (byte*)addr;
                            try {
                                bytes[i] = *ptr;
                            } catch {
                                // ignored
                            }
                        }
                    }

                    PluginLog.Debug("Got memory request, sending result");
                    var msg2 = new S2CMessage {
                        MemoryResult = new MemoryResult {
                            Data = ByteString.CopyFrom(bytes),
                            Uuid = msg.MemoryRequest.Uuid
                        }
                    };
                    this.Send(msg2.ToByteArray());

                    break;
                }

                case C2SMessage.MessageOneofCase.MemoryWrite: {
                    var payloads = msg.MemoryWrite.Payloads;

                    foreach (var payload in payloads) {
                        var addr = (nint)payload.Address;
                        var data = payload.Data.ToByteArray()[0];
                        PluginLog.Verbose("Writing {Data:X} to {Address:X}", data, payload.Address);

                        VirtualProtect(addr, 1, 0x40, out var oldProtect);
                        unsafe {
                            var ptr = (byte*)addr;
                            *ptr = data;
                        }

                        VirtualProtect(addr, 1, oldProtect, out _);
                    }

                    break;
                }

                default:
                    PluginLog.Warning("Unknown message type: {Message}", msg.MessageCase);
                    break;
            }
        }
    }

    public void Dispose() {
        this._server.Stop();
    }
}
