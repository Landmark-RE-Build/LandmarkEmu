using LandmarkEmulator.AuthServer.Network.Message;
using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model;
using LandmarkEmulator.Shared.Network.Packets;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.Parser
{
    /// <summary>
    /// Parses one pcap/pcapng with per-session crypto state (Auth key → Gateway ARC4).
    /// </summary>
    public class CaptureSessionParser
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const ushort DefaultAuthPort = 20042;

        private readonly string _capturePath;
        private readonly string _outDir;
        private readonly SessionParseOptions _options;

        private DataStreamParsing _authServerStream;
        private DataStreamParsing _authClientStream;
        private DataStreamParsing _zoneServerStream;
        private DataStreamParsing _zoneClientStream;

        private readonly HashSet<ushort> _authPorts = new() { DefaultAuthPort };
        private readonly HashSet<ushort> _gatewayPorts = new();
        private string _gatewayEncryptionKey;
        private readonly List<string> _lines = new();
        private readonly ParseSummary _summary = new();
        private readonly List<ZoneTimelineEntry> _timeline = new();
        private readonly List<DumpIndexEntry> _dumps = new();
        private int _packetIndex;
        private int _dumpIndex;
        private long _udpSeen;
        private double _currentPacketSeconds;
        private int _currentFrameIndex;
        private double? _firstZoneSeconds;
        private double? _sendSelfSeconds;
        private double? _clientReadySeconds;

        // SOE CRC (SessionReply). Live GameSession strips before Data*/Multi Read; parser must too.
        private uint? _authCrcSeed;
        private int _authCrcBytes = 2;
        private uint? _gatewayCrcSeed;
        private int _gatewayCrcBytes = 2;

        public IReadOnlyList<ZoneTimelineEntry> Timeline => _timeline;

        public CaptureSessionParser(string capturePath, string outDir, SessionParseOptions options)
        {
            _capturePath = capturePath;
            _outDir = outDir;
            _options = options ?? new SessionParseOptions();
            _options.OpcodeFilter ??= new HashSet<uint>();
            _summary.FileName = Path.GetFileName(capturePath);
        }

        /// <summary>Backward-compatible ctor used by older call sites.</summary>
        public CaptureSessionParser(string capturePath, string outDir, HashSet<uint> opcodeFilter = null, bool quietProgress = false, bool timelineOnly = false)
            : this(capturePath, outDir, new SessionParseOptions
            {
                OpcodeFilter = opcodeFilter ?? new HashSet<uint>(),
                QuietProgress = quietProgress,
                TimelineOnly = timelineOnly,
                WriteTimeline = timelineOnly,
                SummaryOnly = timelineOnly,
                DumpAllEc = opcodeFilter == null || opcodeFilter.Count == 0,
                DumpLargeMinBytes = opcodeFilter == null || opcodeFilter.Count == 0 ? 5000 : null,
                DumpAnchors = !timelineOnly
            })
        {
        }

        private bool LogPackets => !_options.SummaryOnly;

        public ParseSummary Parse(CancellationToken token = default)
        {
            Directory.CreateDirectory(_outDir);
            if (!_options.TimelineOnly)
                Directory.CreateDirectory(Path.Combine(_outDir, "bins"));

            InitStreams();

            string tag = _options.TimelineOnly ? "[timeline]" : "[parse]";
            Console.WriteLine($"{tag} {_summary.FileName}");

            try
            {
                PcapUdpReader.ForEach(_capturePath, datagram =>
                {
                    token.ThrowIfCancellationRequested();
                    _udpSeen++;
                    _currentPacketSeconds = datagram.Seconds + datagram.Microseconds / 1_000_000.0;
                    _currentFrameIndex = datagram.FrameIndex;
                    if (!_options.QuietProgress && _udpSeen % 10000 == 0)
                        Console.Write($"\r{tag} {_summary.FileName}: {_udpSeen} UDP");

                    bool isClient = PcapUdpReader.IsLikelyClient(datagram.SourcePort);
                    Parser.GamePacketType? gameType = Classify(datagram.SourcePort, datagram.DestinationPort);

                    // Peek SessionRequest before crypto so Auth/Gateway ports can be learned.
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

                    // Drop unrecognized UDP (and pre-Gateway-port noise) so foreign SOE sequences
                    // cannot poison zoneClientStream ahead of LoginRequest (seq 0).
                    if (gameType is not Parser.GamePacketType.Auth and not Parser.GamePacketType.Gateway)
                        return true;

                    var protoPacket = new ProtocolPacket(datagram.Payload, new ParserPacketOptions
                    {
                        IsSubpacket = false,
                        // Auth LoginUdp still uses a compression-flag byte when SessionReply says so;
                        // Gateway frames are adjusted below after classification.
                        Compression = true,
                        GamePacketType = gameType.Value,
                        IsClient = isClient
                    });

                    try
                    {
                        HandleProtocolPacket(protoPacket);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }

                    return true;
                }, token);

                if (!_options.QuietProgress && _udpSeen > 0)
                    Console.WriteLine();
            }
            catch (Exception ex)
            {
                _summary.Error = ex.Message;
                log.Error(ex);
            }

            if (_options.WriteTimeline && _timeline.Count > 0)
                FinalizeTimelineRelatives();
            FinalizeDumpRelatives();

            if (!_options.TimelineOnly)
            {
                if (LogPackets)
                    File.WriteAllLines(Path.Combine(_outDir, "PacketParse.txt"), _lines);
                File.WriteAllText(Path.Combine(_outDir, "summary.json"),
                    JsonConvert.SerializeObject(_summary, Formatting.Indented));
                if (_dumps.Count > 0)
                    WriteDumpsIndex(Path.Combine(_outDir, "dumps-index.csv"));
            }

            if (_options.WriteTimeline && _timeline.Count > 0)
                WriteTimelineCsv(Path.Combine(_outDir, "zone-timeline.csv"));

            Console.WriteLine($"{tag} Done {_summary.FileName}: key={_summary.GotEncryptionKey}, opcodes={_summary.Opcodes.Count}, dumps={_summary.DumpedBins.Count}, timeline={_timeline.Count}");
            return _summary;
        }

        public void WriteTimelineCsv(string path)
        {
            var lines = new List<string>
            {
                "seq,frame,abs_s,rel_sendself_ms,rel_clientready_ms,rel_firstzone_ms,dir,opcode,name,payload_len,packet_len"
            };
            foreach (var e in _timeline)
            {
                lines.Add(string.Join(',',
                    e.Seq,
                    e.FrameIndex,
                    e.AbsoluteSeconds.ToString("F6", CultureInfo.InvariantCulture),
                    e.RelSendSelfMs.ToString("F1", CultureInfo.InvariantCulture),
                    e.RelClientReadyMs.ToString("F1", CultureInfo.InvariantCulture),
                    e.RelFirstZoneMs.ToString("F1", CultureInfo.InvariantCulture),
                    e.Direction,
                    e.OpcodeHex,
                    EscapeCsv(e.Name),
                    e.PayloadLength,
                    e.PacketLength));
            }
            File.WriteAllLines(path, lines);
        }

        private void WriteDumpsIndex(string path)
        {
            var lines = new List<string>
            {
                "dump_index,file,dir,opcode,name,packet_len,abs_s,rel_sendself_ms,rel_clientready_ms,rel_firstzone_ms"
            };
            foreach (var d in _dumps)
            {
                lines.Add(string.Join(',',
                    d.DumpIndex,
                    EscapeCsv(d.FileName),
                    d.Direction,
                    d.OpcodeHex,
                    EscapeCsv(d.Name),
                    d.PacketLength,
                    d.AbsoluteSeconds.ToString("F6", CultureInfo.InvariantCulture),
                    d.RelSendSelfMs.ToString("F1", CultureInfo.InvariantCulture),
                    d.RelClientReadyMs.ToString("F1", CultureInfo.InvariantCulture),
                    d.RelFirstZoneMs.ToString("F1", CultureInfo.InvariantCulture)));
            }
            File.WriteAllLines(path, lines);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private void InitStreams()
        {
            _authServerStream = new DataStreamParsing(null, "authServerStream");
            _authServerStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Auth, false);
            _authClientStream = new DataStreamParsing(null, "authClientStream");
            _authClientStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Auth, true);

            _zoneServerStream = new DataStreamParsing(null, "zoneServerStream");
            _zoneServerStream.OnData += data => OnGamePacket(data, Parser.GamePacketType.Gateway, false);
            _zoneServerStream.SetEncryption(false);

            _zoneClientStream = new DataStreamParsing(null, "zoneClientStream");
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

        /// <summary>
        /// New ExternalGateway SOE session: clear fragment/sequence state. Re-apply Auth key if present.
        /// Encryption stays off until client LoginRequest (matches GatewaySession).
        /// </summary>
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

        private void OnGamePacket(byte[] data, Parser.GamePacketType gamePacketType, bool isClient)
        {
            string header = isClient ? "[Client]" : "[Server]";
            _packetIndex++;

            switch (gamePacketType)
            {
                case Parser.GamePacketType.Auth:
                    HandleAuthMessage(data, isClient, header);
                    break;
                case Parser.GamePacketType.Gateway:
                    HandleGatewayPacket(data, isClient, header);
                    break;
                case Parser.GamePacketType.Zone:
                    HandleZonePacket(data, isClient, header);
                    break;
            }
        }

        private void HandleAuthMessage(byte[] data, bool isClient, string header)
        {
            if (data.Length < 1)
                return;

            var opcode = (AuthMessageOpcode)data[0];
            TrackOpcode("Auth", opcode.ToString(), $"0x{(byte)opcode:X2}", data.Length, isClient);
            if (LogPackets)
                _lines.Add($"{header} [Auth] {opcode} (0x{(byte)opcode:X2}) | {data.Length} bytes");

            IReadable message = AuthMessageManager.Instance.GetAuthMessage(opcode, ProtocolVersion.LOGIN_ALL);
            if (message == null)
            {
                log.Trace($"{header} Unknown auth packet {opcode:X}");
                return;
            }

            var body = new Span<byte>(data, 1, data.Length - 1).ToArray();
            var reader = new GamePacketReader(body);
            try
            {
                message.Read(reader);
            }
            catch (Exception ex)
            {
                if (LogPackets)
                    _lines.Add($"  (failed to read: {ex.Message})");
                return;
            }

            if (message is CharacterLoginReply loginReply)
            {
                byte[] keyBytes = loginReply.Server?.EncryptionKey;
                if (keyBytes is { Length: > 0 })
                {
                    string key = Convert.ToBase64String(keyBytes);
                    _gatewayEncryptionKey = key;
                    _zoneClientStream.SetEncryptionKey(key);
                    _zoneServerStream.SetEncryptionKey(key);
                    _summary.GotEncryptionKey = true;
                    Console.WriteLine($"[crypto] Gateway ARC4 key set from CharacterLoginReply ({keyBytes.Length} bytes)");
                    if (LogPackets)
                        _lines.Add("  [crypto] Gateway ARC4 key set from CharacterLoginReply");
                }
                else
                {
                    Console.WriteLine("[crypto] CharacterLoginReply seen but EncryptionKey empty");
                }
            }

            if (LogPackets)
                _lines.Add(JsonConvert.SerializeObject(message, Formatting.Indented));
        }

        private void HandleGatewayPacket(byte[] data, bool isClient, string header)
        {
            if (data.Length < 1)
                return;

            var opcode = (GatewayMessageOpcode)(data[0] & 0x1F);
            var body = new Span<byte>(data, 1, data.Length - 1).ToArray();
            TrackOpcode("Gateway", opcode.ToString(), $"0x{(int)opcode:X2}", data.Length, isClient);

            // Match GatewaySession: LoginRequest is cleartext; ARC4 starts for everything after.
            // Arming on the first Gateway frame (old behavior) desynced RC4 mid-0x0019 multiplex
            // when early server plaintext pieces followed the toggle inside DecryptData's loop.
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
                return;
            }

            IReadable message = GatewayMessageManager.Instance.GetGatewayMessage(opcode, ProtocolVersion.ExternalGatewayApi_3);
            if (message == null)
            {
                // Pre-key / mis-decrypt frames show up as unknown gateway opcodes; keep quiet.
                log.Trace($"Unknown Gateway packet {opcode:X}");
                return;
            }

            try
            {
                var reader = new GamePacketReader(body);
                message.Read(reader);
                if (LogPackets)
                {
                    _lines.Add($"{header} [Gateway] {opcode}");
                    _lines.Add(JsonConvert.SerializeObject(message, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                if (LogPackets)
                    _lines.Add($"{header} [Gateway] {opcode} (read failed: {ex.Message})");
            }
        }

        private void HandleZonePacket(byte[] data, bool isClient, string header)
        {
            (ZoneMessageOpcode? zoneOpcode, int offset) = WorldSession.GetOpcode(data);
            if (!zoneOpcode.HasValue)
            {
                // GetOpcode only returns named enum members; still surface the wire prefix for RE.
                string rawHex = FormatUnknownOpcodeHex(data);
                int rawOffset = GuessUnknownOpcodeOffset(data.Length);
                int unknownPayload = Math.Max(0, data.Length - rawOffset);
                // ARC4/demux garbage rarely matches Landmark family shape; keep real catalog holes
                // (e.g. 0x26006500 / 0x29002E00) as Unknown and quarantine the rest as UnknownNoise.
                bool familyShape = LooksLikeLandmarkOpcodeFamily(data);
                string unknownName = familyShape ? "Unknown" : "UnknownNoise";
                TrackOpcode("Zone", unknownName, rawHex, data.Length, isClient);
                if (_options.WriteTimeline)
                    RecordTimeline(isClient, rawHex, unknownName, unknownPayload, data.Length);
                if (LogPackets)
                    _lines.Add($"{header} [Zone] {unknownName} packet ({rawHex}) | {unknownPayload} bytes");
                if (!_options.TimelineOnly && familyShape && ShouldDumpLarge(data.Length))
                    DumpBin($"unknown_{_packetIndex}_{rawHex}", data, isClient, rawHex, unknownName);
                return;
            }

            uint opcodeValue = (uint)zoneOpcode.Value;
            string opcodeHex = $"0x{opcodeValue:X8}";
            string opcodeName = zoneOpcode.Value.ToString();
            TrackOpcode("Zone", opcodeName, opcodeHex, data.Length, isClient);

            int payloadLen = data.Length - offset;
            if (_options.WriteTimeline)
                RecordTimeline(isClient, opcodeHex, opcodeName, payloadLen, data.Length);

            if (zoneOpcode == ZoneMessageOpcode.SendSelfToClient && !_sendSelfSeconds.HasValue)
                _sendSelfSeconds = _currentPacketSeconds;
            if (zoneOpcode == ZoneMessageOpcode.ClientIsReady && !_clientReadySeconds.HasValue)
                _clientReadySeconds = _currentPacketSeconds;
            if (!_firstZoneSeconds.HasValue)
                _firstZoneSeconds = _currentPacketSeconds;

            if (_options.TimelineOnly)
                return;

            bool hasFilter = _options.OpcodeFilter.Count > 0;
            bool filterMatch = hasFilter && _options.OpcodeFilter.Contains(opcodeValue);
            bool terrainFamily = (opcodeValue & 0xFF000000) == 0xEC000000;
            bool dumpTerrain = _options.DumpAllEc && terrainFamily;
            bool dumpLarge = ShouldDumpLarge(data.Length);
            bool dumpAnchor = _options.DumpAnchors && (
                zoneOpcode == ZoneMessageOpcode.SendSelfToClient
                || zoneOpcode == ZoneMessageOpcode.TerrainCellModifiedCellManifest);
            // No filter → dump terrain/large/anchors (legacy). With filter → listed opcodes + optional extras.
            bool shouldDump = hasFilter
                ? (filterMatch || dumpTerrain || dumpLarge || dumpAnchor)
                : (dumpTerrain || dumpLarge || dumpAnchor || filterMatch);
            bool verboseJson = LogPackets && (!hasFilter || filterMatch || dumpTerrain);

            if (LogPackets)
                _lines.Add($"{header} [Zone] {zoneOpcode} ({opcodeHex}) | {payloadLen} bytes");

            if (shouldDump)
            {
                string dumpName = $"{_dumpIndex:D4}_{(isClient ? "C" : "S")}_{zoneOpcode}_{opcodeHex}";
                DumpBin(dumpName, data, isClient, opcodeHex, opcodeName);
                if (terrainFamily)
                    _summary.TerrainFamilyDumps++;
                if (LogPackets)
                {
                    int previewLen = Math.Min(64, data.Length);
                    _lines.Add($"  hex[{previewLen}]: {BitConverter.ToString(data, 0, previewLen)}");
                }
            }

            if (_options.DumpAnchors)
            {
                if (zoneOpcode == ZoneMessageOpcode.SendSelfToClient)
                    DumpBin("SendSelf", data, isClient, opcodeHex, opcodeName);
                else if (zoneOpcode == ZoneMessageOpcode.TerrainCellModifiedCellManifest)
                    DumpBin("TerrainCellManifest", data, isClient, opcodeHex, opcodeName);
            }

            IReadable message = ZoneMessageManager.Instance.GetZoneMessage(zoneOpcode.Value, ClientProtocol.ClientProtocol_ALL);
            if (message == null)
                return;

            try
            {
                var payload = new Span<byte>(data, offset, payloadLen).ToArray();
                var reader = new GamePacketReader(payload);
                message.Read(reader);
                if (verboseJson)
                    _lines.Add(JsonConvert.SerializeObject(message, Formatting.Indented));
            }
            catch (Exception ex)
            {
                if (LogPackets)
                    _lines.Add($"  (model read failed / incomplete: {ex.Message})");
            }
        }

        private bool ShouldDumpLarge(int length)
            => _options.DumpLargeMinBytes is int min && min > 0 && length > min;

        /// <summary>
        /// Landmark zone opcodes are 1–4 wire bytes (see <see cref="ZoneSession.GetOpcode"/>).
        /// Prefer a 4-byte BE peek when present so timeline/summary match named <c>0xXXXXXXXX</c> form.
        /// </summary>
        private static string FormatUnknownOpcodeHex(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "0x????????";
            if (data.Length >= 4)
                return $"0x{(uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]):X8}";
            if (data.Length == 3)
                return $"0x{(uint)((data[0] << 16) | (data[1] << 8) | data[2]):X6}";
            if (data.Length == 2)
                return $"0x{(uint)((data[0] << 8) | data[1]):X4}";
            return $"0x{data[0]:X2}";
        }

        /// <summary>
        /// Landmark zone opcodes are typically <c>xx 00 yy 00</c> (mask <c>0xFF00FF == 0</c>) or a
        /// known high-byte family (<c>0x26/0x27/0x29/0xEC</c>). High-entropy prefixes after ARC4
        /// desync fail this check and must not be treated as catalog gaps.
        /// </summary>
        internal static bool LooksLikeLandmarkOpcodeFamily(byte[] data)
        {
            if (data == null || data.Length < 2)
                return false;

            if (data.Length >= 4)
            {
                uint op = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
                if ((op & 0xFF00FFu) == 0)
                    return true;
            }

            byte b0 = data[0];
            if (b0 is 0x26 or 0x27 or 0x29 or 0xEC)
                return true;

            // Two-byte form xx00 still used by many base opcodes when only 2 bytes are present.
            if (data.Length == 2 && data[1] == 0)
                return true;

            return false;
        }

        private static int GuessUnknownOpcodeOffset(int length) => length >= 4 ? 4 : length;

        private void RecordTimeline(bool isClient, string opcodeHex, string name, int payloadLength, int packetLength)
        {
            double abs = _currentPacketSeconds;
            double Rel(double? anchor) => anchor.HasValue ? (abs - anchor.Value) * 1000.0 : double.NaN;

            _timeline.Add(new ZoneTimelineEntry
            {
                Seq = _timeline.Count,
                FrameIndex = _currentFrameIndex,
                AbsoluteSeconds = abs,
                RelSendSelfMs = Rel(_sendSelfSeconds),
                RelClientReadyMs = Rel(_clientReadySeconds),
                RelFirstZoneMs = Rel(_firstZoneSeconds),
                Direction = isClient ? "C" : "S",
                OpcodeHex = opcodeHex,
                Name = name,
                PayloadLength = payloadLength,
                PacketLength = packetLength
            });
        }

        private void FinalizeTimelineRelatives()
        {
            foreach (var e in _timeline)
            {
                e.RelSendSelfMs = _sendSelfSeconds.HasValue
                    ? (e.AbsoluteSeconds - _sendSelfSeconds.Value) * 1000.0
                    : double.NaN;
                e.RelClientReadyMs = _clientReadySeconds.HasValue
                    ? (e.AbsoluteSeconds - _clientReadySeconds.Value) * 1000.0
                    : double.NaN;
                e.RelFirstZoneMs = _firstZoneSeconds.HasValue
                    ? (e.AbsoluteSeconds - _firstZoneSeconds.Value) * 1000.0
                    : double.NaN;
            }
        }

        private void FinalizeDumpRelatives()
        {
            foreach (var d in _dumps)
            {
                d.RelSendSelfMs = _sendSelfSeconds.HasValue
                    ? (d.AbsoluteSeconds - _sendSelfSeconds.Value) * 1000.0
                    : double.NaN;
                d.RelClientReadyMs = _clientReadySeconds.HasValue
                    ? (d.AbsoluteSeconds - _clientReadySeconds.Value) * 1000.0
                    : double.NaN;
                d.RelFirstZoneMs = _firstZoneSeconds.HasValue
                    ? (d.AbsoluteSeconds - _firstZoneSeconds.Value) * 1000.0
                    : double.NaN;
            }
        }

        private void HandleProtocolPacket(ProtocolPacket packet)
        {
            var packetOptions = packet.PacketOptions as ParserPacketOptions;
            string header = packetOptions.IsClient ? "[Client]" : "[Server]";

            IProtocol message = MessageManager.Instance.GetProtocolMessage(packet.Opcode);
            if (message == null)
            {
                // Truncated/misclassified frames are common in official sniffs; avoid log spam.
                log.Trace($"{header} Unknown protocol packet {packet.Opcode:X}");
                return;
            }

            if (message is Ack or AckAll)
                return;

            if (packetOptions.GamePacketType == Parser.GamePacketType.Gateway)
                packet.PacketOptions.Compression = false;

            try
            {
                byte[] body = StripTrailingCrcIfNeeded(packet, packetOptions, message);
                var reader = new ProtocolPacketReader(body);
                message.Read(reader, packet.PacketOptions);
            }
            catch (Exception ex)
            {
                // SessionReply/etc. often fail on short UDP frames or CRC-trimmed leftovers.
                log.Trace(ex, $"{header} Failed to read protocol {packet.Opcode} ({packet.Data?.Length ?? 0} bytes)");
                return;
            }

            if (LogPackets && message is SessionRequest sessionRequest)
            {
                _lines.Add($"{header} [Protocol] SessionRequest protocol={sessionRequest.Protocol}");
                _lines.Add(JsonConvert.SerializeObject(sessionRequest, Formatting.Indented));
            }

            if (message is SessionReply sessionReply)
            {
                if (packetOptions.GamePacketType == Parser.GamePacketType.Auth)
                {
                    _authCrcSeed = sessionReply.CRCSeed;
                    _authCrcBytes = sessionReply.CRCLength > 0 ? sessionReply.CRCLength : 2;
                    packet.PacketOptions.Compression = sessionReply.EncryptionMethod != 0;
                }
                else if (packetOptions.GamePacketType == Parser.GamePacketType.Gateway)
                {
                    _gatewayCrcSeed = sessionReply.CRCSeed;
                    _gatewayCrcBytes = sessionReply.CRCLength > 0 ? sessionReply.CRCLength : 2;
                    // SessionReply is the authoritative start of Gateway SOE seq space.
                    ResetGatewayStreams();
                }

                if (LogPackets)
                {
                    _lines.Add($"{header} [Protocol] SessionReply");
                    _lines.Add(JsonConvert.SerializeObject(sessionReply, Formatting.Indented));
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
            {
                FeedData(packetOptions, dataWhole);
            }

            if (message is DataFragment fragment)
            {
                FeedFragment(packetOptions, fragment);
            }
        }

        /// <summary>
        /// Match <see cref="GameSession"/>: protected SOE opcodes carry trailing crcBytes that must
        /// not enter Data/DataFragment payloads. Without this, Auth megatunnels early-complete via
        /// the truncate hack and late true-tail fragments wedge the server stream (no LoginReply/key).
        /// </summary>
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

            // Fallback peel when seed unknown or CRC mismatch in sniffs. Length matches the
            // verify-success path (trailer crcBytes removed); do not drop — holes stall the pcap stream.
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

        private void TrackOpcode(string layer, string name, string opcodeHex, int size, bool isClient)
        {
            string key = $"{layer}:{opcodeHex}";
            if (!_summary.Opcodes.TryGetValue(key, out var stat))
            {
                stat = new OpcodeStat { Layer = layer, Name = name, OpcodeHex = opcodeHex };
                _summary.Opcodes[key] = stat;
            }
            stat.Observe(size, isClient);
        }

        private void DumpBin(string name, byte[] data, bool isClient, string opcodeHex, string opcodeName)
        {
            string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{safe}.bin";
            string path = Path.Combine(_outDir, "bins", fileName);
            File.WriteAllBytes(path, data);
            _summary.DumpedBins.Add(fileName);

            int index = _dumpIndex++;
            _dumps.Add(new DumpIndexEntry
            {
                DumpIndex = index,
                FileName = fileName,
                Direction = isClient ? "C" : "S",
                OpcodeHex = opcodeHex,
                Name = opcodeName,
                PacketLength = data.Length,
                AbsoluteSeconds = _currentPacketSeconds
            });
        }

        public static void WriteOpcodeIndex(IEnumerable<ParseSummary> summaries, string outPath)
        {
            var rows = new List<string>
            {
                "file,layer,name,opcode,count,clientCount,serverCount,minSize,maxSize,avgSize"
            };

            foreach (var summary in summaries.Where(s => !s.Skipped && s.Error == null))
            {
                foreach (var stat in summary.Opcodes.Values.OrderBy(s => s.Layer).ThenBy(s => s.OpcodeHex))
                {
                    rows.Add(string.Join(',',
                        Csv(summary.FileName),
                        Csv(stat.Layer),
                        Csv(stat.Name),
                        Csv(stat.OpcodeHex),
                        stat.Count,
                        stat.ClientCount,
                        stat.ServerCount,
                        stat.MinSize == int.MaxValue ? 0 : stat.MinSize,
                        stat.MaxSize,
                        stat.AvgSize.ToString("F1")));
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllLines(outPath, rows);
        }

        private static string Csv(string value)
        {
            if (value == null) return "";
            if (value.Contains(',') || value.Contains('"'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
