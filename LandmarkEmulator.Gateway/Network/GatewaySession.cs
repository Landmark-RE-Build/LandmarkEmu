using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Gateway.Network.Message.Model;
using LandmarkEmulator.Gateway.Network.Packets;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game.Events;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace LandmarkEmulator.Gateway.Network
{
    public class GatewaySession : GameSession
    {
        public AccountModel Account { get; private set; }
        public List<CharacterModel> Characters { get; private set; }

        private readonly ConcurrentQueue<GatewayPacket> incomingPackets = new();
        /// <summary>
        /// Concurrent: drained on the tick (<see cref="OnProcessPackets"/>), but /gatewayprofiler answers
        /// (<see cref="HandleGatewayCommand"/>) are enqueued on the UDP receive thread, racing a plain Queue's dequeue
        /// (a zone packet lost from the reliable stream, or a null dequeued on the tick).
        /// </summary>
        private readonly ConcurrentQueue<GatewayPacket> outgoingPackets = new();

        public ClientProtocol ClientProtocol { get; private set; }
        public string ClientBuild { get; private set; }

        public GatewaySession(string cryptoKey) : base(cryptoKey, subPacketAcks: true)
        {
            // We initialise with Arc4 Encryption off, because the initial Data Packet which includes the LoginRequest comes in un-encrypted.
            ToggleEncryption(false);
        }

        public override void OnAccept(EndPoint ep)
        {
            base.OnAccept(ep);

            log.Debug($"New session received on {ep}");
        }

        protected override void OnGamePacket(ushort sequence, byte[] data)
        {
            base.OnGamePacket(sequence, data);

            var packet = new GatewayPacket(data);
            HandlePacket(packet);
        }

        /// <summary>
        /// This is fired between incoming packets being parsed and outgoing packets being sent, to allow for the Class inheritor to handle its packets.
        /// </summary>
        /// <remarks>This is called from within Update().</remarks>
        protected virtual void OnProcessZonePackets()
        {
            // Deliberately left empty
        }

        protected sealed override void OnProcessPackets()
        {
            OnProcessZonePackets();

            while (CanProcessPackets && outgoingPackets.TryDequeue(out GatewayPacket packet))
                SendPacket(packet);
        }

        private void HandlePacket(GatewayPacket packet)
        {
            // Handle Tunnel Packets slightly separately.
            if (packet.Opcode == GatewayMessageOpcode.TunnelPacketFromExternalConnection)
            {
                log.Trace($"Received Gateway packet {packet.Opcode}(0x{packet.Opcode:X}) | Channel: {packet.Channel} | {BitConverter.ToString(packet.Data)}");
                Profile(false, packet.Data);
                OnTunnelData(packet.Channel, packet.Data);
                return;
            }

            IReadable message = GatewayMessageManager.Instance.GetGatewayMessage(packet.Opcode, ProtocolVersion);
            if (message == null)
            {
                log.Warn($"Received unknown Gateway packet {packet.Opcode:X} : {BitConverter.ToString(packet.Data)}");
                return;
            }

            GatewayMessageHandlerDelegate handlerInfo = GatewayMessageManager.Instance.GetGatewayMessageHandler(packet.Opcode, ProtocolVersion);
            if (handlerInfo == null)
            {
                log.Warn($"Received unhandled Gateway packet {packet.Opcode}(0x{packet.Opcode:X})  : {BitConverter.ToString(packet.Data)}.");
                return;
            }

            log.Trace($"Received packet {packet.Opcode}(0x{packet.Opcode:X}) : {BitConverter.ToString(packet.Data)}");

            var reader = new GamePacketReader(packet.Data);
            message.Read(reader);
            if (reader.BytesRemaining > 0)
                log.Warn($"Failed to read entire contents of Gateway packet {packet.Opcode} ({reader.BytesRemaining} bytes remaining)");

            try
            {
                handlerInfo.Invoke(this, message);
            }
            catch (InvalidPacketValueException exception)
            {
                log.Error(exception);
                OnDisconnect();
            }
            catch (Exception exception)
            {
                log.Error(exception);
            }
        }

        private void SendPacket(GatewayPacket packet)
        {
            List<byte> data = new();
            var writer = new GamePacketWriter(data);

            writer.Write((byte)((int)packet.Opcode | (int)packet.Channel << 5));
            writer.WriteBytes(packet.Data);

            log.Trace($"Sending packet {packet.Opcode}(0x{packet.Opcode:X}) : {BitConverter.ToString(packet.Data)}");

            PackAndSend(data.ToArray());
        }

        protected void EnqueueGatewayMessage(IWritable message, Channel channel = Channel.Zone)
        {
            if (!GatewayMessageManager.Instance.GetOpcode(message, out GatewayMessageOpcode opcode))
            {
                log.Warn("Failed to send message with no attribute!");
                return;
            }

            outgoingPackets.Enqueue(new GatewayPacket(opcode, message, channel));
        }

        // ---- Gateway commands (opcode 9, CommandList source 3; gateway-service.md §3.9, architecture-leads.md L5) ------

        /// <summary>The gateway's own slash commands. Live named only /gatewayprofiler (the monitor's page 24 text).</summary>
        private static readonly string[] GatewayCommands = { "gatewayprofiler" };

        /// <summary>Per-signature traffic of this connection while the profiler is on: (direction, signature) → (count, bytes).</summary>
        private readonly Dictionary<(bool Out, string Signature), (long Count, long Bytes)> profile = new();
        private bool profiling;

        /// <summary>
        /// After the login the gateway tells the client its commands: a CommandListPacket (Command 0x1E / 0x44: u32 n ×
        /// string name, u32 source) with source 3 (the gateway), tunnelled on channel 0; typing one sends opcode 9.
        /// </summary>
        protected void SendGatewayCommandList()
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(0x1E);
            w.WriteUShort(0x44);
            w.Write((uint)GatewayCommands.Length);
            foreach (string name in GatewayCommands)
                w.Write(name);
            w.Write(3u);
            PackTunnelPacket(output.ToArray());
        }

        /// <summary>
        /// Opcode 9: a slash command for the gateway itself — the client's ClientCommandPacket with its zone header
        /// (1E 00 43 00, string name, u32 n × string arguments; FUN_14337d580). Answers only through tunnelled zone packets
        /// (the client parses no gateway opcode 9). "/gatewayprofiler on | off | dump": per-signature counts of this
        /// connection's packets (the profiler record of FUN_143c79940: count, bytes per direction and signature), dumped
        /// as system lines. Ours: open to every player and limited to their own connection (live's was an admin tool).
        /// </summary>
        [GatewayMessageHandler(GatewayMessageOpcode.GatewayCommand, ProtocolVersion.ExternalGatewayApi_3)]
        public void HandleGatewayCommand(GatewayCommand command)
        {
            string name;
            var args = new List<string>();
            try
            {
                var r = new GamePacketReader(command.Packet);
                r.ReadUShort();
                r.ReadUShort();
                name = r.ReadString();
                uint n = r.ReadUInt();
                for (uint i = 0; i < n && i < 8; i++)
                    args.Add(r.ReadString());
            }
            catch (Exception)
            {
                log.Debug($"Gateway command unreadable: {BitConverter.ToString(command.Packet)}");
                return;
            }
            if (!string.Equals(name, "gatewayprofiler", StringComparison.OrdinalIgnoreCase))
            {
                log.Debug($"Gateway command /{name} is not one of ours: ignored.");
                return;
            }
            string arg = args.FirstOrDefault()?.ToLowerInvariant() ?? "dump";
            log.Info($"Gateway command /{name} {arg}.");
            switch (arg)
            {
                case "on":
                    lock (profile)
                    {
                        profile.Clear();
                        profiling = true;
                    }
                    SystemLine("Gateway profiler on.");
                    break;
                case "off":
                    lock (profile)
                        profiling = false;
                    SystemLine("Gateway profiler off.");
                    break;
                default:
                    List<string> lines;
                    lock (profile)
                        lines = profile.OrderByDescending(p => p.Value.Bytes).Take(12)
                            .Select(p => $"{(p.Key.Out ? "out" : "in")} {p.Key.Signature}: count={p.Value.Count}, bytes={p.Value.Bytes}").ToList();
                    if (lines.Count == 0)
                        SystemLine("No gateway profiler stats. Use '/gatewayprofiler on' to enable.");
                    // Totals per direction first (the split-vs-monolith byte comparison, TASKS B.5), then the top signatures;
                    // also logged, so a test can read them without the chat window.
                    long outBytes, inBytes;
                    lock (profile)
                    {
                        outBytes = profile.Where(p => p.Key.Out).Sum(p => (long)p.Value.Bytes);
                        inBytes = profile.Where(p => !p.Key.Out).Sum(p => (long)p.Value.Bytes);
                    }
                    lines.Insert(0, $"total out={outBytes} bytes, in={inBytes} bytes");
                    foreach (string line in lines)
                    {
                        SystemLine(line);
                        log.Info($"Gateway profiler {Account?.Username ?? "?"}: {line}");
                    }
                    break;
            }
        }

        /// <summary>Counts one packet for the profiler (its first two bytes, the zone opcode, as the signature).</summary>
        private void Profile(bool outgoing, byte[] data)
        {
            if (!profiling || data == null || data.Length < 2)
                return;
            string signature = $"0x{data[0] | data[1] << 8:X}";
            lock (profile)
            {
                var (count, bytes) = profile.GetValueOrDefault((outgoing, signature));
                profile[(outgoing, signature)] = (count + 1, bytes + data.Length);
            }
        }

        /// <summary>A system chat line through the tunnel: ShowSystemMessage (0x69) with client text 558682 "[*message*]".</summary>
        private void SystemLine(string message)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(0x69);
            w.Write(558682u);
            w.Write("");
            w.Write(1u);
            w.Write("message");
            w.Write(message);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            PackTunnelPacket(output.ToArray());
        }

        [GatewayMessageHandler(GatewayMessageOpcode.LoginRequest, ProtocolVersion.ExternalGatewayApi_3)]
        public void HandleLoginRequest(LoginRequest request)
        {
            log.Info($"{request.CharacterId}, {request.ServerTicket}, {request.ClientProtocol}, {request.ClientBuild}");

            // We toggle Encrytion back on, here, because the Client expects the LoginReply (and all subsequent packets) to come in with Arc4 Encryption.
            ToggleEncryption(true);

            ClientBuild = request.ClientBuild;
            if (Enum.TryParse(request.ClientProtocol, out ClientProtocol protocol))
                ClientProtocol = protocol;
            else
            {
                log.Error($"Unsupported Client Protocol: {request.ClientProtocol}. Disconnecting.");
                OnDisconnect();
                return;
            }
            if (ClientBuilds.Is589(ClientBuild))
            {
                // The server speaks the 0.695 numbering only (ClientBuilds; docs/protocol.md §1.6).
                log.Error($"Client build {ClientBuild} is not served here (0.695 only). Disconnecting.");
                OnDisconnect();
                return;
            }

            LoginTicket = request.ServerTicket ?? "";
            CheckLogin(request.ServerTicket, request.CharacterId, (account, character) =>
            {
                bool loggedIn = account != null && character != null;
                EnqueueGatewayMessage(new LoginReply
                {
                    LoggedIn = loggedIn
                });
                if (!loggedIn)
                {
                    log.Warn($"Login refused for character {request.CharacterId} (unknown or used ticket, or not the account's character). Disconnecting.");
                    OnDisconnect();
                    return;
                }
                Account = account;
                OnCharacterLogin(character);
                SendGatewayCommandList();
            });
        }

        /// <summary>
        /// The login check: the ticket is single use (cleared on login); an unknown or already used ticket, or a character
        /// that isn't the account's, is refused (<paramref name="done"/> gets nulls). Runs <paramref name="done"/> on this
        /// session's update. By default read from the auth and character databases (the world server's in-process
        /// gateway); a gateway in its own process asks the login service instead (gateway-service.md §3.7).
        /// </summary>
        protected virtual void CheckLogin(string ticket, ulong characterId, Action<AccountModel, CharacterModel> done)
        {
            EnqueueEvent(new TaskGenericEvent<AccountModel>(DatabaseManager.Instance.AuthDatabase.ConsumeServerTicketAsync(ticket, TicketWorld),
                account =>
            {
                if (account == null)
                {
                    done(null, null);
                    return;
                }
                EnqueueEvent(new TaskGenericEvent<List<CharacterModel>>(DatabaseManager.Instance.CharacterDatabase.GetCharacters(account.Id),
                    characters =>
                {
                    Characters = characters;
                    done(account, characters.SingleOrDefault(x => x.Id == characterId));
                }));
            }));
        }

        /// <summary>
        /// The world this in-process gateway serves (the world server's ZoneServerId, set at start-up): tickets issued for
        /// another world are refused (ticket hardening). 0 = not checked.
        /// </summary>
        public static uint TicketWorld { get; set; }

        /// <summary>
        /// The server ticket this client logged in with. The login server starts it with "R" when the client signed in
        /// with a relogin key (back from character select in the same running client); see
        /// Game.Inventory.ItemDefinitionManager.SendLoginList.
        /// </summary>
        public string LoginTicket { get; protected set; } = "";

        /// <summary>
        /// Called after a user's login has been authenticated to indiciate to inherited Classes which character is logging in.
        /// </summary>
        public virtual void OnCharacterLogin(CharacterModel model)
        {
            // Deliberately left empty
        }

        /// <summary>
        /// Called when the Gateway receives a <see cref="GatewayMessageOpcode.TunnelPacketFromExternalConnection"/> message.
        /// </summary>
        /// <param name="data"></param>
        public virtual void OnTunnelData(Channel channel, byte[] data)
        {
            // Deliberately left empty
        }

        /// <summary>
        /// Packs the provided <see cref="byte[]"/> into a Tunnel Packet to be sent to the Client.
        /// </summary>
        protected void PackTunnelPacket(byte[] data, Channel channel = Channel.Zone)
        {
            Profile(true, data);
            EnqueueGatewayMessage(new TunnelPacketToExternalConnection
            {
                Data = data
            }, channel);
        }
    }
}
