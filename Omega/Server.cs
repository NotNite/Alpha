using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Alpha.Proto;
using Dalamud.Game;
using Dalamud.Logging;
using Google.Protobuf;
using WebSocketSharp;
using WebSocketSharp.Server;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace Omega;

public class Server : IDisposable {
    private WebSocketServer _server;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect,
        out uint lpflOldProtect);

    public Server() {
        this._server = new WebSocketServer(41784);
        this._server.AddWebSocketService<OmegaConnection>("/");
        this._server.Start();
    }

    class OmegaConnection : WebSocketBehavior {
        private List<UpdateEntry> _updates = new();

        public OmegaConnection() {
            Services.Framework.Update += this.Update;
        }
        
        ~OmegaConnection() {
            Services.Framework.Update -= this.Update;
        }

        struct UpdateEntry {
            public long Start;
            public long End;
            public byte[]? Data;

            public void Update(byte[] data) {
                this.Data = data;
            }

            public void Reset() {
                this.Data = null;
            }
        }

        public void Update(Framework framework) {
            // reset state on disconnect
            if (this.State != WebSocketState.Open) {
                this._updates.Clear();
                return;
            }

            foreach (var update in this._updates) {
                var len = update.End - update.Start;
                var bytes = new byte[len];

                for (var i = 0; i < len; i++) {
                    unsafe {
                        var addr = update.Start + i;
                        var ptr = (byte*)addr;
                        try {
                            bytes[i] = *ptr;
                        } catch {
                            // ignored
                        }
                    }
                }

                if (update.Data?.Length != bytes.Length) {
                    update.Reset();
                }

                if (update.Data is not null) {
                    var changed = false;
                    for (var i = 0; i < len; i++) {
                        if (bytes[i] != update.Data[i]) {
                            changed = true;
                            break;
                        }
                    }

                    if (changed) {
                        var msg2 = new S2CMessage {
                            MemoryUpdate = new MemoryUpdate {
                                Address = update.Start,
                                Data = ByteString.CopyFrom(bytes)
                            }
                        };

                        // only send after handdshake
                        if (this.State == WebSocketState.Open) {
                            this.Send(msg2.ToByteArray());
                            update.Update(bytes);
                        }
                    }
                }
            }
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

                case C2SMessage.MessageOneofCase.MemoryPositionUpdate: {
                    var payloads = msg.MemoryPositionUpdate.Payloads;
                    var list = new List<UpdateEntry>();
                    foreach (var payload in payloads) {
                        var entry = new UpdateEntry {
                            Start = payload.Start,
                            End = payload.End,
                            Data = null
                        };

                        list.Add(entry);
                    }

                    this._updates = list;
                    break;
                }

                default:
                    PluginLog.Warning("Unknown message type: {Message}", msg.MessageCase);
                    break;
            }
        }

        protected override void OnError(ErrorEventArgs e) {
            this._updates.Clear();
        }

        protected override void OnClose(CloseEventArgs e) {
            this._updates.Clear();
        }
    }

    public void Dispose() {
        this._server.Stop();
    }
}
