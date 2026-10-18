using LandmarkEmulator.AuthServer.Network.Message;
using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.AuthServer.Network.Message.Model.Shared;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model;
using LandmarkEmulator.Shared.Network.Message.Model.Shared;
using LandmarkEmulator.Shared.Network.Packets;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using static LandmarkEmulator.WorldServer.Network.Message.Model.SendSelfToClient;

namespace LandmarkEmulator.Parser
{
    /// <summary>
    /// Lightweight Auth + early Gateway walk to fill triage metadata
    /// (selected server name/description, SendSelf spawn location). Stops once found.
    /// </summary>
    public static class CaptureEnrichment
    {
        private const ushort DefaultAuthPort = 20042;

        public static void Enrich(TriageFileRecord record, string filePath, CancellationToken token = default)
        {
            if (record == null || string.IsNullOrEmpty(filePath))
                return;
            // Gateway-only captures have no Auth key → cannot decrypt zone / no server list.
            if (record.GatewayOnly)
                return;
            if (!record.HasAuth)
                return;

            try
            {
                new EnrichmentWalker(record).Run(filePath, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(record.Error))
                    record.Error = $"enrich: {ex.Message}";
            }
        }

        private sealed class EnrichmentWalker
        {
            private readonly TriageFileRecord _record;
            private readonly List<Server> _servers = new();
            private readonly HashSet<ushort> _authPorts = new() { DefaultAuthPort };
            private readonly HashSet<ushort> _gatewayPorts = new();
            private string _gatewayEncryptionKey;

            private DataStreamParsing _authServerStream;
            private DataStreamParsing _authClientStream;
            private DataStreamParsing _zoneServerStream;
            private DataStreamParsing _zoneClientStream;

            private bool _gotLoginReply;
            private bool _gotWorldLocation;
            private bool _done;

            private uint? _authCrcSeed;
            private int _authCrcBytes = 2;
            private uint? _gatewayCrcSeed;
            private int _gatewayCrcBytes = 2;

            public EnrichmentWalker(TriageFileRecord record)
            {
                _record = record;
            }

            public void Run(string filePath, CancellationToken token)
            {
                InitStreams();

                PcapUdpReader.ForEach(filePath, datagram =>
                {
                    if (_done)
                        return false;

                    bool isClient = PcapUdpReader.IsLikelyClient(datagram.SourcePort);
                    Parser.GamePacketType? gameType = Classify(datagram.SourcePort, datagram.DestinationPort);

                    if (CaptureTriage.TryReadSessionProtocol(datagram.Payload, out string protocol))
                    {
                        ushort serverPort = PcapUdpReader.ServerPort(datagram.SourcePort, datagram.DestinationPort);
                        if (protocol.StartsWith("LoginUdp", StringComparison.OrdinalIgnoreCase))
                        {
                            _authPorts.Add(serverPort);
                            gameType = Parser.GamePacketType.Auth;
                        }
                        else if (protocol.StartsWith("ExternalGatewayApi", StringComparison.OrdinalIgnoreCase))
                        {
                            _gatewayPorts.Add(serverPort);
                            gameType = Parser.GamePacketType.Gateway;
                            ResetGatewayStreams();
                        }
                    }

                    if (gameType is not Parser.GamePacketType.Auth and not Parser.GamePacketType.Gateway)
                        return true;

                    var protoPacket = new ProtocolPacket(datagram.Payload, new ParserPacketOptions
                    {
                        IsSubpacket = false,
                        Compression = true,
                        GamePacketType = gameType.Value,
                        IsClient = isClient
                    });

                    try
                    {
                        HandleProtocolPacket(protoPacket);
                    }
                    catch
                    {
                        // Best-effort enrichment; ignore frame-level failures.
                    }

                    UpdateDone();
                    return !_done;
                }, token);
            }

            private void InitStreams()
            {
                _authServerStream = new DataStreamParsing(null, "enrichAuthServer");
                _authServerStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Auth, false);
                _authClientStream = new DataStreamParsing(null, "enrichAuthClient");
                _authClientStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Auth, true);

                _zoneServerStream = new DataStreamParsing(null, "enrichZoneServer");
                _zoneServerStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Gateway, false);
                _zoneServerStream.SetEncryption(false);

                _zoneClientStream = new DataStreamParsing(null, "enrichZoneClient");
                _zoneClientStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Gateway, true);
                _zoneClientStream.SetEncryption(false);
            }

            private Parser.GamePacketType? Classify(ushort src, ushort dst)
            {
                if (_authPorts.Contains(src) || _authPorts.Contains(dst))
                    return Parser.GamePacketType.Auth;
                if (_gatewayPorts.Contains(src) || _gatewayPorts.Contains(dst))
                    return Parser.GamePacketType.Gateway;
                return null;
            }

            private void ResetGatewayStreams()
            {
                _zoneClientStream.Reset();
                _zoneServerStream.Reset();
                _zoneClientStream.SetEncryption(false);
                _zoneServerStream.SetEncryption(false);
                if (_gatewayEncryptionKey != null)
                {
                    _zoneClientStream.SetEncryptionKey(_gatewayEncryptionKey);
                    _zoneServerStream.SetEncryptionKey(_gatewayEncryptionKey);
                }
            }

            private void UpdateDone()
            {
                // Auth-only: stop after login reply. Full login: also wait for SendSelf position.
                if (!_gotLoginReply)
                    return;
                if (!_record.HasGateway || _gotWorldLocation)
                    _done = true;
            }

            private void OnGamePacket(byte[] data, Parser.GamePacketType gamePacketType, bool isClient)
            {
                if (_done || data == null || data.Length < 1)
                    return;

                switch (gamePacketType)
                {
                    case Parser.GamePacketType.Auth:
                        HandleAuth(data);
                        break;
                    case Parser.GamePacketType.Gateway:
                        HandleGateway(data, isClient);
                        break;
                    case Parser.GamePacketType.Zone:
                        HandleZone(data, isClient);
                        break;
                }

                UpdateDone();
            }

            private void HandleAuth(byte[] data)
            {
                var opcode = (AuthMessageOpcode)data[0];
                IReadable message = AuthMessageManager.Instance.GetAuthMessage(opcode, ProtocolVersion.LOGIN_ALL);
                if (message == null)
                    return;

                var body = new Span<byte>(data, 1, data.Length - 1).ToArray();
                try
                {
                    CaptureReading.Read(message, new GamePacketReader(body));
                }
                catch
                {
                    return;
                }

                if (message is ServerListReply listReply)
                {
                    _servers.Clear();
                    _servers.AddRange(listReply.Servers);
                }
                else if (message is CharacterLoginReply loginReply)
                {
                    _gotLoginReply = true;
                    _record.ServerId = loginReply.ServerId;
                    _record.ServerAddress = loginReply.Server?.ServerAddress;
                    if (!string.IsNullOrWhiteSpace(loginReply.Server?.CharacterName))
                        _record.CharacterName = loginReply.Server.CharacterName;

                    ApplyServerFromList(loginReply.ServerId);

                    if (loginReply.Server?.EncryptionKey is { Length: > 0 } keyBytes)
                    {
                        string key = Convert.ToBase64String(keyBytes);
                        _gatewayEncryptionKey = key;
                        _zoneClientStream.SetEncryptionKey(key);
                        _zoneServerStream.SetEncryptionKey(key);
                    }
                }
            }

            private void ApplyServerFromList(ulong serverId)
            {
                Server match = null;
                foreach (var s in _servers)
                {
                    if (s.ServerId == serverId)
                    {
                        match = s;
                        break;
                    }
                }

                // Fallback: single-server list.
                if (match == null && _servers.Count == 1)
                    match = _servers[0];

                if (match == null)
                    return;

                _record.ServerName = CleanText(match.Name);
                _record.ServerDescription = CleanText(match.Description);
                if (_record.ServerId == null)
                    _record.ServerId = match.ServerId;
            }

            private void HandleGateway(byte[] data, bool isClient)
            {
                var opcode = (GatewayMessageOpcode)(data[0] & 0x1F);
                var body = new Span<byte>(data, 1, data.Length - 1).ToArray();

                    if (isClient && opcode == GatewayMessageOpcode.LoginRequest)
                    {
                        if (!_zoneClientStream.UsingEncryption)
                            _zoneClientStream.SetEncryption(true);
                        if (!_zoneServerStream.UsingEncryption)
                            _zoneServerStream.SetEncryption(true);
                    }

                    if (opcode is GatewayMessageOpcode.TunnelPacketFromExternalConnection
                        or GatewayMessageOpcode.TunnelPacketToExternalConnection)
                    {
                        OnGamePacket(body, Parser.GamePacketType.Zone, isClient);
                    }
                }

            private void HandleZone(byte[] data, bool isClient)
            {
                if (isClient || _gotWorldLocation)
                    return;

                (ZoneMessageOpcode? zoneOpcode, int offset) = WorldSession.GetOpcode(data);
                if (zoneOpcode != ZoneMessageOpcode.SendSelfToClient)
                    return;

                var payload = new Span<byte>(data, offset, data.Length - offset).ToArray();
                if (!TryReadSendSelfSpawn(payload, out float x, out float y, out float z, out float w, out string firstName))
                    return;

                _record.WorldX = x;
                _record.WorldY = y;
                _record.WorldZ = z;
                _record.WorldW = w;
                if (string.IsNullOrWhiteSpace(_record.CharacterName) && !string.IsNullOrWhiteSpace(firstName))
                    _record.CharacterName = firstName;
                _gotWorldLocation = true;
            }

            private void HandleProtocolPacket(ProtocolPacket packet)
            {
                var packetOptions = packet.PacketOptions as ParserPacketOptions;
                IProtocol message = MessageManager.Instance.GetProtocolMessage(packet.Opcode);
                if (message == null || message is Ack or AckAll)
                    return;

                if (packetOptions.GamePacketType == Parser.GamePacketType.Gateway)
                    packet.PacketOptions.Compression = false;

                try
                {
                    byte[] body = StripTrailingCrcIfNeeded(packet, packetOptions, message);
                    message.Read(new ProtocolPacketReader(body), packet.PacketOptions);
                }
                catch
                {
                    return;
                }

                if (message is SessionReply sessionReply)
                {
                    if (packetOptions.GamePacketType == Parser.GamePacketType.Auth)
                    {
                        _authCrcSeed = sessionReply.CRCSeed;
                        _authCrcBytes = sessionReply.CRCLength > 0 ? sessionReply.CRCLength : 2;
                    }
                    else if (packetOptions.GamePacketType == Parser.GamePacketType.Gateway)
                    {
                        _gatewayCrcSeed = sessionReply.CRCSeed;
                        _gatewayCrcBytes = sessionReply.CRCLength > 0 ? sessionReply.CRCLength : 2;
                        ResetGatewayStreams();
                    }
                }

                if (message is MultiPacket multiPacket)
                {
                    foreach (ProtocolPacket multiPacketPacket in multiPacket.Packets)
                    {
                        multiPacketPacket.PacketOptions = new ParserPacketOptions
                        {
                            IsSubpacket = true,
                            Compression = packetOptions.GamePacketType != Parser.GamePacketType.Gateway,
                            GamePacketType = packetOptions.GamePacketType,
                            IsClient = packetOptions.IsClient
                        };
                        HandleProtocolPacket(multiPacketPacket);
                    }
                }

                if (message is DataWhole dataWhole)
                    FeedData(packetOptions, dataWhole);
                if (message is DataFragment fragment)
                    FeedFragment(packetOptions, fragment);
            }

            private byte[] StripTrailingCrcIfNeeded(ProtocolPacket packet, ParserPacketOptions options, IProtocol message)
            {
                if (options.IsSubpacket || packet.Data == null)
                    return packet.Data;

                if (!MessageManager.Instance.GetOpcodeData(message, out (ProtocolMessageOpcode opcode, bool useCrc) meta) || !meta.useCrc)
                    return packet.Data;

                int crcBytes = options.GamePacketType == Parser.GamePacketType.Auth ? _authCrcBytes : _gatewayCrcBytes;
                uint? seed = options.GamePacketType == Parser.GamePacketType.Auth ? _authCrcSeed : _gatewayCrcSeed;
                if (crcBytes <= 0 || packet.Data.Length < crcBytes)
                    return packet.Data;

                byte[] full = new byte[2 + packet.Data.Length];
                full[0] = (byte)(((ushort)packet.Opcode >> 8) & 0xFF);
                full[1] = (byte)((ushort)packet.Opcode & 0xFF);
                Buffer.BlockCopy(packet.Data, 0, full, 2, packet.Data.Length);

                if (seed.HasValue && CrcProvider.TryVerifyAndStrip(full, seed.Value, crcBytes, out byte[] withoutCrc))
                    return withoutCrc.AsSpan(2).ToArray();

                var peeled = new byte[packet.Data.Length - crcBytes];
                Buffer.BlockCopy(packet.Data, 0, peeled, 0, peeled.Length);
                return peeled;
            }

            private void FeedData(ParserPacketOptions options, DataWhole dataWhole)
            {
                if (options.GamePacketType == Parser.GamePacketType.Auth)
                {
                    if (options.IsClient) _authClientStream.ProcessDataFragment(dataWhole);
                    else _authServerStream.ProcessDataFragment(dataWhole);
                }
                else if (options.GamePacketType == Parser.GamePacketType.Gateway)
                {
                    if (options.IsClient) _zoneClientStream.ProcessDataFragment(dataWhole);
                    else _zoneServerStream.ProcessDataFragment(dataWhole);
                }
            }

            private void FeedFragment(ParserPacketOptions options, DataFragment fragment)
            {
                if (options.GamePacketType == Parser.GamePacketType.Auth)
                {
                    if (options.IsClient) _authClientStream.ProcessDataFragment(fragment);
                    else _authServerStream.ProcessDataFragment(fragment);
                }
                else if (options.GamePacketType == Parser.GamePacketType.Gateway)
                {
                    if (options.IsClient) _zoneClientStream.ProcessDataFragment(fragment);
                    else _zoneServerStream.ProcessDataFragment(fragment);
                }
            }
        }

        /// <summary>
        /// Reads only the SendSelf header through spawn position + identity
        /// (avoids depending on the full ~2k-line model succeeding).
        /// </summary>
        public static bool TryReadSendSelfSpawn(
            byte[] payload,
            out float x, out float y, out float z, out float w,
            out string firstName)
        {
            x = y = z = w = 0;
            firstName = null;
            if (payload == null || payload.Length < 32)
                return false;

            try
            {
                var reader = new GamePacketReader(payload);
                reader.ReadUInt();                  // SizePrefix (the framework's prepended size)
                reader.ReadULong();                 // Guid
                reader.ReadString();                // AccountName
                reader.ReadULong();                 // CharacterId
                reader.ReadUInt2BitLength();        // TransientId
                reader.ReadULong();                 // ServerTime
                reader.ReadUInt();                  // ModelId

                var model = new CharacterModelInfo();
                model.Read(reader);

                x = reader.ReadSingle();
                y = reader.ReadSingle();
                z = reader.ReadSingle();
                w = reader.ReadSingle();

                // Rotation — skip so identity aligns.
                reader.ReadSingle();
                reader.ReadSingle();
                reader.ReadSingle();
                reader.ReadSingle();

                try
                {
                    var identity = new CharacterIdentity();
                    identity.Read(reader);
                    firstName = identity.FirstName;
                }
                catch
                {
                    // Character name is optional; position is the enrichment goal.
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string trimmed = value.Trim();
            // CaptureReading.DecorateServers appends " (stringtable)" even when TextManager has no entry.
            if (trimmed.EndsWith(" ()", StringComparison.Ordinal))
                trimmed = trimmed[..^3].TrimEnd();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }
}
