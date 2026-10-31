# Gateway as its own service (Phase B item 2) — agent #13, 2026-10-15

> 0.695 rebase (2026-10-26): this file mixes builds; its 20 0.589 address citations map to 0.695 in trace/0695/address-map-589-695.md (changes: trace/0695/rebase-695.md)

The gateway is the client's only connection to a world: one process per world that owns the SOE / UDP session, the
gateway login, the per-build header translation and the channel routing, so a zone (or any other back end) can restart
while the client stays connected. This file: client evidence (addresses **0.695 / 0.589**), what the emulator does
today (file:line), the service spec, proposed changes to internal-transport.md, a build + test plan in an isolated
Docker copy, risks, hand-offs. "Inference" = not proven by code; "not found yet" = open lead (OPEN-LEADS L69-L74).
Scratch: re/out/agent_gateway/ (gwdis.py = read-only capstone disassembler; mov589.c / mov695.c = Movement_ReadBody).

## Summary

| Topic | Result |
|---|---|
| External packets | Client **sends** 1 LoginRequest, 3 Logout (no caller found yet), 6 TunnelFrom (channel in bits 5-7), 9 gateway command; **receives only** 2, 4, 5, 7, 8 (switch FUN_143a4bd20 / FUN_143993e40). Layouts of every one now known (below). |
| Client reaction to routability | **Log only.** ClientGateway's OnChannelIsRoutable / OnConnectionIsNotRoutable write a NetInfo.log line and nothing else (both builds). ForcedLogout, LoginComplete and OnFailed listener slots are empty `ret` stubs. The client keeps sending on a dead channel; what happens to those packets is entirely the gateway's job. |
| One connection per session | The client connects to a gateway in exactly one place (run state 0xA → BaseClient vtable +0x118 = FUN_142d53aa0 / FUN_142cba700). Zone changes (ClientBeginZoning) never reconnect: the gateway is the fixed endpoint across every zone change, which is what makes zone restarts / zone-to-zone moves invisible at the transport level. |
| Before LoginReply | Every tunnel packet the client sends reliably before LoginReply(true) is **queued in the client** (+0xd88) and flushed when the reply arrives (FUN_143a4b710); unreliable ones are dropped; LoginReply(false) discards the queue. |
| Internal gateway packets (new) | The client's ApplicationPacketTracker strips gateway headers by **internal opcode**: **7 → 19-byte header, 8 → 13-byte header, 0x16 → 11 + 12 × n bytes with u32 n at offset 7**; external 5 / 6 → 1 byte (FUN_143c7a9c0 / FUN_143bba9c0). So live's internal tunnel opcodes were 7, 8 and 0x16 (0x16 = ToExternalConnectionList, from its ctor). Which of 7 / 8 is From / To: inference (8 = To: 13 = 1 + one 12-byte ExternalDestination). |
| Gateway profiler (page 24) | Data model traced: per message signature (zone base + sub, FUN_143c7a750) and direction: count, totalNano, totalRecipients, gatewayHeaderBytes, applicationBytes (FUN_143c79940, log formats FUN_143c7b210 / FUN_143c7b580). The page itself draws a server text blob from PlayClient vtable +0x300, which returns 0 in retail (FUN_14320f090). |
| Opcode 9 | Wire = `09` + full ClientCommandPacket **including its zone header** `1E 00 43 00`, then name string, u32 n, n strings (FUN_1432d0790: header writer FUN_142b79540 writes u16 base + u16 sub). The gateway's own list = CommandListPacket source 3 (tunnelled). Only `/gatewayprofiler` is named by the client. |
| Gateway-sourced positions | Channel 2 S→C body = **no zone opcode**: u32 transient id (2-bit-length uint, FUN_142c0bd80 = Stream_ReadUInt2BitLength) + Movement_ReadBody (FUN_142c22390 / FUN_142b88bc0: **identical layout in both builds**, normalised diff) + 4 floats (defaults if absent). The gateway can relay movement between players itself, unchanged across builds. |
| Relogin | CharacterSelectSessionRequest 0xF6 goes on **channel 1** (FUN_142d57e60 / FUN_142cbead0) with a 60 s deadline (+0x324a0); run state 0x15 fails with "Lost connection to the gateway while waiting for a relogin session." if the gateway drops meanwhile. The gateway must keep the connection through the World back end's answer and then close at once on ClientLogout 0x14 (existing research). |
| Emulator today | Gateway is a library (LandmarkEmulator.Gateway) inherited by WorldSession; one world process does SOE, gateway login, ClientWire695, flood limits, movement fan-out and every back end. Channel is ignored for routing except 2. Outgoing tunnel packets are always channel 0. No opcode 3 / 7 / 8 / 9 handling. Unused edge/zone scaffolding (IZoneSessionChannel, TunnelChannel, EdgeZoneBackendMode Grpc, TerrainPlan, BackendSessionId) exists in Shared. |

## Existing research used (not re-traced)

| File | What it already settled |
|---|---|
| trace/families/Gateway_protocol.md | The client's gateway classes, the TunnelFrom builders (`channel << 5 \| 6`), BasePacket opcode 9 builder FUN_1432d0790, ToExternalConnectionList built only in FUN_143bb8d10, server status per packet (opcode 9 / Logout not handled). |
| trace/families/Login_protocol.md | Login opcodes 1-0x13, CharacterLoginReply / TransferServer layouts, ServerUpdate sent; the handover side the gateway receives. |
| trace/systems/session.md | Login → zone entry order, session messages, kick 0x53 (duplicate login, close 1.5 s later), 0x4C deliberately unused, logout to character select via 0xF6 / 0xF7. |
| trace/systems/commands.md, families/001E_Command.md | Command family: 0x43 ClientCommand handled, 0x44 CommandListPacket layout (u16 sub, list of strings, u32 source), 0x45 / 0x47 builders. |
| families/0014, 0053, 0054, 004C, 005F, 00E0, 00F6, 00F7 | ClientLogout 0x14; KickedFromServer {StringFixed<128>} message box; UpdateClientSessionData 0x54 never sent; ServerShuttingDown 0x4C text; WorldShutdownNotice 0x5F layout; LoginQueueStatus 0xE0 layout (queue not built); 0xF6 builders + 60 s deadline; 0xF7 {bool, session} → run state 0x16 / 0x17. |
| trace/FAMILIES.md, managers.md, server-only-data.md | Gateway family row (3 C2S / 7 S2C); `Gateway::ExternalGatewayApiHandler` manager; "gateway disconnected" WaitForWorldReady abort text. |
| trace/architecture/channels.md, login-gateway.md, dynamic-commands.md, architecture-leads.md L5 / L6 / L8 | Channel per message class (no message changed channel in 0.695), opcode 9 = gateway command, CommandList sources 1 / 2 / 3, internal opcode 0x16 + 11 / 12 header (now extended below), GatewayId in CharacterLoginRequest (L50), server-only ids GatewayProfilerRegistration 0x4D7, InternalConnectionBypass 0x4E0, WorldKick* (L48). |
| LandmarkEmu/docs/protocol.md §1, §6 | SOE layer (session request, data, acks, retransmit back-off, compression off, RC4 after gateway login), session lifecycle (server must close on 0x14). |
| LandmarkEmu/docs/docker.md | Current compose layout (setup, db, auth, web, world; extra worlds per env file; one `landmarkemu` network). |
| MODLOG.md | Logout to character select works only if the server closes on 0x14 (2026-10-09, l.727); re-zone hang rules: one re-zone at a time, hold a player's other packets until arrival (2026-10-11, l.1216); relog keys "R…" / Gateway LoginTicket (2026-10-14, l.1794, l.1889); graceful world shutdown runs Ui.Logout then 0xF7 (l.1791); ClientWire695 / PerBuild at the session boundary (2026-10-15, l.2018). |

## 1. Client evidence

### 1.1 External gateway packets (all from Gateway::ExternalGatewayApi, RTTI 0x144907690 / 0.589 equivalent)

First byte of every external packet: `opcode & 0x1F | channel << 5` (receive switch FUN_143a4bd20 / FUN_143993e40
splits it; senders FUN_143a4a3d0, FUN_143a4a260).

| Op | Name | Dir | Layout after the first byte | Client code |
|---|---|---|---|---|
| 1 | LoginRequest | C→S | u64 character guid (+0x1030, the CharacterLoginReply Guid), StringFixed<32> ticket, StringFixed<32> client protocol, StringFixed<32> client build (u32 length + chars each) | writer FUN_143a4a3d0; sent from FUN_143a4b990 / FUN_143993ab0 "OnConnect, sending login packet." |
| 2 | LoginReply | S→C | bool loggedIn | FUN_143a4a110 → FUN_143a4b710 / FUN_143993830: stores +0x10d0, flushes (true) or discards (false) the pre-login queue +0xd88, then listener +0x20 (ClientGateway: empty) |
| 3 | Logout | C→S | none | FUN_143a4bb50 / FUN_143993c70 "Logout sent to the server. DisconnectTimeoutMs=%d, ForceDisconnectOnTimeout=%d." — **no caller found yet** (no call / jmp / 8-byte pointer in 0.695; one 4-byte .rdata reference at 0x145080210, likely unwind data) → L71 |
| 4 | ForcedLogout | S→C | StringFixed<64> reason (u32 length + chars) | FUN_143a49fc0 → thunk LAB_143a4b6f0 → listener +0x28 = ClientGateway FUN_142df65a0 / FUN_142d58530 = `ret` (nothing happens until the connection closes) |
| 5 | TunnelPacketToExternalConnection | S→C | channel in the first byte; body | FUN_143a4b830 → listener +0x40 = FUN_142df65e0 / FUN_142d58570(conn, channel, data, len) |
| 6 | TunnelPacketFromExternalConnection | C→S | channel in the first byte; body | 593 send templates; FUN_142c464c0 / FUN_142c43250 (plain or "SendPacketSecure" FUN_143a4bf20) |
| 7 | ChannelIsRoutable | S→C | channel in the first byte; bool isRoutable; bool (second flag, meaning not found yet → L70) | FUN_143a49ee0 → FUN_143a4b690 → listener +0x30(channel, b1, b2) = FUN_142df6310 / FUN_142d582a0: NetInfo.log "RECEIVED=OnChannelIsRoutable channel=%d isRoutable=%d" only |
| 8 | ConnectionIsNotRoutable | S→C | none (channel bits ignored) | FUN_143a4b6d0 → listener +0x38 = FUN_142df6370 / FUN_142d58300: NetInfo.log "RECEIVED=OnConnectionIsNotRoutable" only |
| 9 | (BasePacket 9) gateway command | C→S | ClientCommandPacket with its zone header: `1E 00 43 00`, string name (StringFixed<64>), u32 n, n × string (StringFixed<256>) | FUN_14337d580 / FUN_1432d0790, header FUN_142b79540; only caller: ServerCommand execute for source 3 (FUN_1433c3d80 / FUN_143316f90) |

ClientGateway vtable (0.695 0x1446c6790 / 0.589 0x1446000c0; base Gateway::ExternalGatewayApiHandler 0x1446c66d0):
+0x08 OnConnect (log "RECEIVED=OnConnect %lld"), **+0x10 OnDisconnect FUN_142df63a0 / FUN_142d58330** (message box
ErrorDisconnected / ErrorLabelOops + reason, log "RECEIVED=OnDisconnect"), +0x18 OnFailed (empty), +0x20 LoginComplete
(empty), +0x28 ForcedLogout (empty), +0x30 ChannelIsRoutable (log), +0x38 ConnectionIsNotRoutable (log), +0x40 tunnel.
Checked by decompile and disassembly (`ret 0`, gwdis.py) in 0.589; same slots in 0.695.

### 1.2 Connecting, login and the session

- **One connect site.** Run state 0xA (PlayClient state switch, part_0065 ~10469): `vtable+0x118` = FUN_142d53aa0 /
  FUN_142cba700 connects (address from CharacterLoginReply, 60 s timeout FUN_142df5f60, `UseCompression`, `InRate`,
  `OutRate`, `LatencyMs` from config, s-channel FUN_142ca9a00, errors "Invalid gateway address.", "Failed gateway
  connection - %s - %s" → error 0xF / 0x1A), then FUN_143a4b990 sends LoginRequest → state 0xB waits for the
  character ("Connection to gateway lost before authenticating." / "Failure to set up initial connection."). No other
  call of that slot on the client object exists → the client never changes gateway during play.
- **Pre-login queue.** FUN_143a4a260 / FUN_143a4bea0: while +0x10d0 (logged in) is 0, a reliable send is queued
  (FUN_143a90730 into +0xd88), an unreliable one is dropped; FUN_143a4b710 flushes the queue on LoginReply(true).
  +0x10d0 is cleared on connect (FUN_143a4bc20), disconnect (FUN_143a4bc60 "OnDisconnect.") and failure
  (FUN_143a4bcc0 "OnFailed.").
- **Raw-packet log.** +0x10d2 set → every outgoing gateway packet is hex-dumped to RawPacketsOutgoing.txt
  (FUN_143a4b8b0) — a test aid for verifying the new gateway's framing on a test client (config key that sets it: not
  traced, not needed).
- **Expected connections.** `UdpParams.processExpectedConnectionsOnly` is a UdpLibrary parameter (reader part_0133
  ~36282 with the other UdpParams). Where the UdpManager checks it and how the login server registered the
  expectation: not found yet (L72). Inference: live's gateway accepted only connections the login server announced
  (ticket + address).
- **Relogin / one-time session id.** FUN_142d57e60 / FUN_142cbead0 sets a 60 s deadline at PlayClient +0x324a0 and
  sends PacketCharacterSelectSessionRequest 0xF6 on channel 1 (`FUN_142c43250(gw, &pkt, 1, 1)`). State 0x15 waits:
  gateway lost → "Lost connection to the gateway while waiting for a relogin session.", deadline → "Timed out while
  waiting for a relogin session." (part_0065 ~10851). FUN_142d54a00 / FUN_142cbb660: "Did not receive One-Time
  Session Id from server." when the flag +0x32498 is set and the deadline passed. 0xF7 (Zone_DispatchPacket case 0x79)
  carries the session → state 0x16 → ClientLogout 0x14, then the server must close within ~2 s (MODLOG l.727).

### 1.3 Channels and positions

- Routing is by the channel the client sends on (channels.md: 0 Zone, 1 World, 2 Position, 3 "channel 3"; no
  message changed channel in 0.695). For S→C the client treats every channel except 2 the same (tunnel handler
  FUN_142df65e0: channel 2 → position reader, else the zone dispatch via client vtable +0x138).
- **Channel 2 S→C = PlayerUpdatePacketUpdatePositionGatewayToClient** (RTTI 0x1446c6720): body without opcode bytes:
  Stream_ReadUInt2BitLength (FUN_142c0bd80 / FUN_142b72520) transient id → Movement_ReadBody (FUN_142c22390 /
  FUN_142b88bc0) → up to 4 u32/floats (defaults DAT_14468c380), handed to client vtable +0x208; profiled under
  signature 0x7F00. The two Movement_ReadBody decompiles are identical after normalising names / addresses
  (re/out/agent_gateway/mov589.c vs mov695.c), so a gateway relays position bodies **between 0.589 and 0.695 clients
  without translation**.
- C→S movement goes on channel 2 (BaseClient::BroadcastLocalPlayerMovement FUN_142ca4ce0 → FUN_142c464c0(gw, data,
  2, 1)), sent with the same reliable flag as other tunnel sends.

### 1.4 Internal side and profiler

- RTTI (0.695): Gateway::InternalPackets::BasePacket 0x144949838, PacketTunnelPacketFromExternalConnection 0x1449498b8,
  PacketTunnelPacketToExternalConnection 0x144949898, PacketTunnelPacketToExternalConnectionList 0x1449498a8,
  `List<Gateway::ExternalDestination,64>` / `<,-1>`.
- **ApplicationPacketTracker header table** (ctor FUN_143c78d10 / FUN_143bb8d10, tracker at monitor +0x188):
  +0x340a0 = 2 and +0x340a4 = 6 are **zone-packet sub-opcode offsets** used by the signature builder FUN_143c7a750
  (u16 sub at offset 2 for bases 0x13, 0x1E, 0x27, 0x2D … ; u8 sub at offset 2 for 0x26, 0xEC, 0xF4, 0xF8, 0xF9 … ;
  offset 6 for 0xBA / 0xC4) — not gateway headers (correcting architecture-leads.md L8 "2/6 … meaning not decoded").
  The gateway header sizes, used by FUN_143c7a9c0 / FUN_143bba9c0(tracker, data, len, hasGatewayHeader, internal, out):
  - external (internal = 0): opcode 5 → 1 byte (+0x340bc), opcode 6 → 1 byte (+0x340c0); recipients 1;
  - **internal**: first byte **7 → 0x13 = 19 bytes** (+0x340a8), **8 → 0xD = 13 bytes** (+0x340ac), **0x16 → 11
    (+0x340b0) + 12 (+0x340b4) × u32 count read at offset 7 (+0x340b8)**, recipients = that count; any other first
    byte → no gateway header.
  So the internal packets start with a **u8** opcode (the switch reads one byte). Field meaning beyond the sizes: not
  found yet (L69).
- **Profiler record** (FUN_143c79940): per (direction, u64 signature): count += 1, applicationBytes += len × recipients,
  gatewayHeaderBytes += header × recipients, totalRecipients += recipients, totalNano += time. Logged per signature
  `"%s: signature=\"%s\", count=…, totalNano=…, totalMs=…, totalRecipients=…, totalBytes=… (gatewayHeaderBytes=…,
  applicationBytes=…)"` and as Incoming / Outgoing totals with uniquePacketCount (FUN_143c7b210 / FUN_143c7b580).
  The client's own copy only counts when the admin flag (PlayClient vtable +0xB0) is set (FUN_14321d910).
- Page 24 (GatewayStatisticsState 0x144727d00, draw FUN_14320f090 / FUN_143164670) prints the text returned by
  PlayClient vtable +0x300 or "No gateway profiler stats. Use '/gatewayprofiler on' to enable." — the stats arrived as
  a text blob in a server reply that no retail reader handles (architecture-leads.md L4). Registration message:
  Command 0x1E/0x4D7 GatewayProfilerRegistration (id only, L48).

### 1.5 Flood limits

No client evidence of live's gateway flood limits (searched strings for flood / throttle / rate limit / "too many";
only the client-side UdpParams.incomingLogicalPacketMax and the config keys InRate / OutRate / LatencyMs). Our limits
stay "ours" (L74).

## 2. Emulator today

| Part | Where |
|---|---|
| Gateway library | LandmarkEmulator.Gateway (net10.0, refs Shared only): GatewaySession.cs, GatewayPacket.cs, GatewayMessageOpcode.cs (1-8 only, l.3-13), Channel.cs, models LoginRequest / LoginReply / ForceDisconnect / TunnelPacketToExternalConnection, GatewayMessageManager (attribute registry), GatewayProvider. |
| SOE / UDP | Shared/Network: Connection.cs (UDP socket), NetworkManager.cs (endpoint → session map l.28-57, update / flatline l.111-137), GameSession.cs (SOE: SessionRequest l.425, data / fragments / acks, RC4 ToggleEncryption l.418, compression off GatewayCompression l.53, unacked timeout 120 s DataStreamOutput.cs l.24), SocketHeartbeat 180 s. |
| Gateway session | GatewaySession : GameSession (l.18). Starts unencrypted (l.32). HandlePacket l.67-111: opcode 6 → OnTunnelData(channel, data) (l.70-75); other opcodes through the registry (unknown → warn, so 3 and 9 are logged and dropped). SendPacket l.113-127 writes `opcode \| channel << 5` for opcode 5 but GatewayPacket's write ctor (GatewayPacket.cs l.30-40) never sets Channel, so **every S→C tunnel packet goes on channel 0** (TunnelPacketToExternalConnection.Channel l.9 is ignored). |
| Login handover | HandleLoginRequest l.140-194: RC4 on, ClientBuild / ClientProtocol (enum 709), ticket consumed in the **auth database directly** (`ConsumeServerTicketAsync`, AuthDatabase.cs l.166 — single use), characters loaded from the character DB, character must belong to the account; LoginTicket l.201 ("R…" = relog, skips item list). AuthServer LoginHandler.cs l.489-545 writes the ticket and answers CharacterLoginReply with `server.Host:Port` from the zone_server row. |
| World session | WorldSession : GatewaySession (WorldServer/Network/WorldSession.cs l.26). OnTunnelData l.1080-1202: channel 2 → PositionUpdate.TryRead (PositionUpdate.cs l.43), arrival filter, achievements / emotes, ToRemoteFrame (l.83) and PlayerManager.RelayMovement (PlayerManager.cs l.277); everything else (channels 0, 1, 3 alike) → ClientWire695.ToServer (l.1121-1122), drop DelayedTargetInfo 0x27/0x17 (l.1128), WorldEditing 0x10C bypass (l.1136-1141), flood limiters (l.1145-1152: packetLimiter 300 / 100 s⁻¹, terrainLimiter 200 / 50 s⁻¹ for 0xEC / 0xF4, warn 1 / min; fields l.528-536), OpcodeStats, opcode match (GetOpcode l.977-1001), HandleZonePacket l.896-942. |
| S→C path | EnqueueMessage l.1003-1020 → OnProcessZonePackets l.824-850 (raw dev packets, movement coalescing per (transient id, type) l.829-837 via QueueMovement l.810-814, then the queue) → SendPacket l.1026-1069 (opcode bytes, OpcodeStats.Sent, **ClientWire695.ToClient l.1036**, PackTunnelPacket). Speaks695 l.1024 (ZoneOpcodeTranslator.Uses695Wire l.19). PerBuild.cs l.10-19 builds a body once per layout. Other players' movement goes as zone packet PlayerUpdatePosition 0x7F00 on channel 0 (Message/Model/PlayerUpdatePosition.cs l.10-22), i.e. the zone path, not the channel-2 gateway path. |
| Game rate limits (not transport) | ChatLimiter, ChatWarnLimiter, StorageLimiter, ContestLimiter, ReportLimiter (WorldSession.cs l.515-526) — game rules, stay with their back end. |
| Logout / relog | CharacterSelectHandler.cs l.25-57: 0x14 → OnDisconnect (closes at once); 0xF6 → new relog key in the auth DB, 0xF7. Shutdown: HostedService.StopAsync l.165-186 (Ui.Logout for all, 0xF7 fallback, wait ≤60 s, then NetworkManager.Shutdown). Duplicate login kick 0x53 then close 1.5 s later (PlayerManager.cs ~l.48-55). |
| Re-zone | Game/Teleport.cs: Rezone l.91-115 (deferred re-zone, retarget while pending), RezoneNow l.135-171 (ClientBeginZoning, SendZoneDetails, manifest), PendingRezoneLimit 60 s l.175, FinishRezone l.184 (repeat ClientIsReady). All inside the one process; no zone-process handover exists. |
| Dynamic commands | Game/Emotes.cs SendCommandList l.94-106 (CommandList source 1 = zone; emotes, duels), HandleCommand l.112 (0x43). No world (2) or gateway (3) list, no opcode 9, no 0x45-0x47. |
| Hosting | HostedService.cs: GatewayProvider.Initialise l.97, one tick runs network + every game system l.123-152, NetworkManager<WorldSession> on the world port l.157. WorldServer.cs l.25 EncryptionKey. |
| Unused split scaffolding | Shared/Network: IZoneSessionChannel.cs (SendTunnelDown / SubmitTerrainPlan / Disconnect; IZoneTunnelSink.OnTunnelUp), TunnelChannel.cs, EdgeZoneBackendMode.cs (InProcess / Grpc), EdgeZoneGrpcLimits.cs (64 MiB), TerrainPlan.cs (edge-paced terrain delivery), ISoeOutputTelemetry.cs, SessionIdBytes.cs, ZoneDisconnectReason.cs; NetworkSession.BackendSessionId l.23 (assigned per SOE session, GameSession.cs l.434) + NetworkManager.GetSessionById l.83-91. No caller uses them; no gRPC project exists. |
| Docker | docker-compose.yml l.45-50 (world extends docker/world/docker-compose.yml), network `landmarkemu` l.55-58. docker/world/docker-compose.yml l.11-48: image landmarkemu-world, auth / character / zone DB strings, `Network__Port` = WORLD_PORT, **UDP WORLD_PORT published by the world container** (l.45-47), web pages TCP. docker/Dockerfile targets auth (l.25-29) / world (l.33-39). Dev world: docker/worlds/dev.env (project landmarkemu-world-dev, WORLD_PORT 19000, data server-data/worlds/dev). |

## 3. Spec: the gateway service

### 3.1 Process and what moves

New executable **LandmarkEmulator.GatewayServer** (Docker target `gateway`, one container per world,
`gateway-<WORLD_KEY>`), built on the existing LandmarkEmulator.Gateway library.

| Moves to the gateway | Stays in the zone (world) process |
|---|---|
| UDP socket, SOE session (Shared GameSession / NetworkManager), RC4, heartbeat / flatline, unacked timeout | All game systems, SendSelf, inventory, re-zone logic (Teleport), arrival / StreamsOnHold, PerBuild body layouts |
| Gateway login (opcode 1/2), ticket check (via the login service, not the auth DB), opcode 3 / 4 / 7 / 8 / 9 | Zone command list (source 1) and its 0x43 / 0x47 handling |
| ClientWire695.ToServer / ToClient (header translation) + Speaks695 | Handlers (keep 0.589 numbering) |
| Transport flood limits + the two per-signature drops (0x27/0x17 drop, 0x10C exempt) | Game rate limits (chat, storage, contests, reports) |
| Movement: PositionUpdate parse, ToRemoteFrame, coalescing, channel-2 fan-out to players on the same landscape in range (PlayerUpdatePacketUpdatePositionGatewayToClient) | Movement *meaning*: position persistence, achievements / emotes, arrival filter — from a copy the gateway forwards (coalesced, ≤ 1 per 100 ms per player) |
| Per-signature profiler (incoming / outgoing; header vs application bytes) | OpcodeStats for handler coverage (unchanged) |
| Gateway command list (source 3) incl. `/gatewayprofiler on\|off` | — |
| Routing table, back-end registrations, routability, hold-and-replay during back-end loss | — |

Character-level state the gateway needs (connection events, below): character guid, account id, client build, world
id, current landscape (zone id), transient id, remote-frame offset, admin flag, current position (from channel 2).

### 3.2 Session / connection state owned by the gateway

`connectionId` (u64; reuse NetworkSession.BackendSessionId, made a u64), endpoint, SOE streams, RC4 key, login state
(None → LoginPending → LoggedIn → Closing), ticket, account id, character guid, client build / protocol,
`relog` flag (ticket "R…"), current zone id + owning zone back end, channel → back end map, per-channel hold queue,
flood buckets, movement coalescing buffer, profiler counters, last activity.

### 3.3 Routing table (per connection, per channel)

| Channel | Back end | Note |
|---|---|---|
| 0 Zone | the zone process that owns the connection's landscape (today: the one world process) | Set by AttachConnection from the zone; changes only through HandOver (3.6). |
| 1 World | `WORLD_CHANNEL_ADDRESS` back end (agent #15) once it exists; until then the zone process | World channel carries friends / guilds / chat membership (social, #12) and commerce / ratings / lobbies (#15); whether the gateway splits it further by family is their call (Hand-offs). |
| 2 Position | the gateway itself (fan-out) + a coalesced copy to the channel-0 back end | |
| 3 | the zone process (world-editing previews are relayed by Game.WorldEditing) | If #14 takes previews, route there. |
| opcode 9 | the gateway's own command handler | never forwarded; answers are tunnelled packets (never opcode 9 to a client). |

**Built 2026-10-16 (untested; TESTING-NEEDED "Gateway routing by registration")**: GatewayServer/RoutingTable.cs,
ZoneBackEnds.cs, GatewayClient.cs. The table is filled only from RegisterBackEnd: (world, channel) → the back ends that
registered that channel bit for that world (`WorldId` of the gateway = WORLD_ID, or the first registration's world;
other worlds are not routed), each with its zone ids (none = every landscape) and kind. Lookups: channel 0 = the back
end with the channel-0 bit serving the connection's landscape (one naming the landscape before a catch-all, newer
before older) — the connection is attached to it and stays with it (HandOver later, §3.6); channels 1-3 = a back end of
another kind than zone registered for the channel and landscape (channel 1: the world-channel service, mask 1 << 1),
else the connection's zone. A new registration with the same role (kind, world, zone ids, mask) replaces a live one
(a restarted copy whose old link is not yet seen closed); its clients are held and re-attached. Each connection caches
its four routes and refreshes them on the tick when the table's version, its zone or its attach state changed, so the
per-packet path is a field read plus the link's queue write (no lock, no copy). The connection's landscape comes from
the login, ConnectionAttached and ZoneChanged 0x27 (now sent by the zone on every landscape change,
GatewayLink.SendZoneChanged).

### 3.4 Internal messages (refines internal-transport.md "Gateway internal opcodes")

Framing: `u32 length` then the internal packet, whose **first byte is the u8 internal opcode** (as the client's tracker
reads it). Little endian. Back ends **dial the gateway** (`GATEWAY_INTERNAL_ADDRESS=gateway-<key>:20300`) and register,
so the gateway is the stable hub and a restarting back end simply reconnects.

| Op | Name (client name where one exists) | Dir | Layout (header size per client in bold) |
|---|---|---|---|
| 7 | TunnelPacketFromExternalConnection | G→B | **19**: u8 op, u64 connectionId, u64 characterGuid, u8 channel, u8 flags (bit0 reliable, bit1 replayed-after-hold) ; then the client packet after ClientWire695.ToServer |
| 8 | TunnelPacketToExternalConnection | B→G | **13**: u8 op, ExternalDestination {u64 connectionId, u8 channel, u8 flags (bit0 reliable, bit1 "already in 0.695 shape"), u16 reserved} ; then a zone packet in 0.589 numbering (gateway applies ClientWire695.ToClient unless bit1) |
| 0x16 | TunnelPacketToExternalConnectionList | B→G | **11 + 12n**: u8 op, u8 channel, u8 flags, u32 bodyLength, **u32 n at offset 7**, n × ExternalDestination (12), body. PerBuild bodies = one list per layout. |
| 0x20 | RegisterBackEnd (ours) | B→G | u8 op, u8 kind (1 zone, 2 world-channel, 3 social, 4 terrain, 5 env), u32 world id, u32 n × u32 zone ids served, u8 channel mask, string name, u32 build stamp |
| 0x21 | AttachConnection (ours; "character entered" of internal-transport) | G→B | u8 op, u64 connectionId, u64 characterGuid, u64 accountId, string accountName, string clientBuild, string ticket, u8 isRelog, u8 isReattach, u32 zoneId |
| 0x22 | ConnectionAttached (ours) | B→G | u8 op, u64 connectionId, u32 zoneId, u32 transientId, 3 × f32 remote-frame offset, u8 admin, 3 × f32 position |
| 0x23 | ConnectionClosed (ours) | G→B | u8 op, u64 connectionId, u8 reason (0 logout, 1 timeout, 2 kicked, 3 SOE disconnect, 4 gateway stopping) |
| 0x24 | CloseConnection (ours; the zone's "close now", 0x14 / kick / shutdown) | B→G | u8 op, u64 connectionId, u32 delayMs, u8 sendForcedLogout, string reason (≤64) |
| 0x25 | HandOver (ours, 3.6) | B→G | u8 op, u64 connectionId, u32 targetZoneId, 3 × f32 position, u32 stateToken |
| 0x26 | PositionCopy (ours) | G→B | u8 op, u64 connectionId, movement body (coalesced newest) |
| 0x27 | ZoneChanged (ours) | B→G | u8 op, u64 connectionId, u32 zoneId, 3 × f32 remote-frame offset (fan-out interest set changes) |
| 0x28 | Ping / Pong (ours) | both | u8 op, u64 timestamp |

Unchanged from the contract: ServicePacket (u16 family, u32 sub, body) for service-to-service messages
(WorldKick 0x1E/0x43B, BroadcastWorld 0x1E/0x43E, GatewayProfilerRegistration 0x1E/0x4D7 …) on the same link.

### 3.5 Behaviour on back-end loss / restart

1. Link to the channel-0 back end drops (or 3 missed pings, 15 s): the gateway marks channels 0 / 3 (and 1 while the
   zone also serves it) **not routable**, sends ChannelIsRoutable(channel, false, false) to each affected client
   (cosmetic: the client only logs it), keeps every SOE session alive (it still acks, pings, relays channel 2 between
   players on the gateway's own state).
2. Client packets on a non-routable channel go to a **per-connection hold queue** (cap 512 packets / 256 KB /
   120 s); overflow drops the oldest non-critical ones. ClientLogout 0x14 and 0xF6 on a dead channel are answered by
   the gateway itself: 0x14 → close the connection now (protocol.md §6 rule); 0xF6 → hold (the client waits 60 s).
   **Built 2026-10-15** (GatewayServer/GatewayClient.cs, ZoneBackEnds.cs; WorldSession.OnReattach): items 1-4 below
   as written, except the > 120 s case closes the connection (the 0xF7 push to character select needs a relog session
   from the login service: later) and the in-place re-zone is on (L73 answered from docs/terrain.md, tested).
3. The restarted zone dials in, sends RegisterBackEnd; the gateway sends **AttachConnection with isReattach = 1** for
   each connection whose landscape it serves; the zone loads the character (as at login, without SendSelf) and
   answers ConnectionAttached; the gateway sends ChannelIsRoutable(channel, true, …), replays the hold queue
   (flag bit1 so stale requests can be ignored), and the zone rebuilds the client's world with a re-zone to the
   player's current position (ClientBeginZoning + SendZoneDetails + manifest, Teleport.RezoneNow), respecting the
   re-zone rules (one at a time, nothing else in the burst). How to make the client drop the entities it still holds
   on a same-landscape re-zone (it keeps them when zone name and id match) is open — L73; until it is answered the
   restarted zone cannot know what the client still holds, so the safe default is the 0xF7 push to character select
   (Shutdown.cs path: connection kept, the player presses Play again) and the in-place re-zone is enabled only after
   the L73 trace and test 5.6 prove it clean.
4. The zone stays down > 120 s: CloseConnection-equivalent from the gateway: KickedFromServer 0x53 text is a zone
   packet, so the gateway sends 0xF7 {true, relog session from the login service} if it can get one (client returns to
   character select, the friendliest path), else ForcedLogout + SOE disconnect (client shows ErrorDisconnected).
5. ConnectionIsNotRoutable is sent when the connection has **no** back end at all for its zone (no registration ever
   arrived after login). Live's exact use: not found yet (L70).
   **Built 2026-10-16 (untested)**, per channel: a channel is routable while its route (above) has a live back end —
   the zone only while it has the character (not held), a service of its own even while the zone is away (so with the
   world-channel service up, a zone loss reports channels 0 and 3, not 1; channel-1 packets keep going to that service,
   which drops what it would hand on to the absent zone, as world-channel-service.md §6.2 says). ChannelIsRoutable
   (channel, isRoutable, false) is sent for channels 0, 1, 3 whenever one changes; the world-channel service stopping
   alone changes nothing the client sees (channel 1 falls back to the zone, which registered channel 1 too) — only the
   gateway log shows the switch. Packets of a channel without a live back end wait in the per-connection queue (256,
   in order per channel; movement is never kept). ConnectionIsNotRoutable: at login when no back end serves channel 0
   for the character's landscape; the connection then waits like a hold (attached with isReattach = 0 when a zone
   registers, ChannelIsRoutable 1 for 0 / 1 / 3 after it; closed after GATEWAY_HOLD_SECONDS). §3.7's "LoginReply(false)"
   for that case is not used: LoginReply(true) has gone out before the zone is asked (as since step 3).
6. Gateway restart = every client disconnects (unavoidable; the SOE session lives there). The zones see
   ConnectionClosed(4) for all and save.

### 3.6 Re-zoning between zone processes (only when Phase B item 6 splits landscapes)

Zone A decides a move to a landscape it doesn't serve → saves the character, sends **HandOver** → the gateway holds
channel 0 / 3, sends ConnectionClosed(handover) to A and AttachConnection(isReattach = 1, zoneId = target) to B → B
sends the re-zone (ClientBeginZoning to the new landscape, which the client treats as a zone change and tears down
entities) → ChannelIsRoutable not needed (no outage). Nothing else is sent to the client between A's last packet and
B's ClientBeginZoning (re-zone hang rule, MODLOG l.1216). Whether live moved players between zone processes this way:
not found yet (L73).

### 3.7 Login handover

- The login service stays the directory of gateways: CharacterLoginReply → `PUBLIC_HOST:GATEWAY_PORT` of the world's
  gateway (zone_server row registered by the **gateway**, not the zone). GatewayId (L50) = 0 → that gateway.
- **Built 2026-10-15** (Internal/Login/LoginMessages.cs, AuthServer/LoginInternalService.cs on 20210, GatewayServer/
  LoginService.cs; ServicePacket family 0xF6, ours): TicketConsume / Reply and RelogSession / Reply; the gateway
  container needs no database. The hold's end uses RelogSession → 0xF7 (the client sent ClientLogout and left).
- Ticket check moves off the shared auth DB: gateway → login service `TicketConsume {ticket, characterGuid}` → reply
  {ok, accountId, accountName, isRelog, admin} over the login service's internal port (ServicePacket, ours). Single
  use stays; the login service also checks the character belongs to the account (today GatewaySession l.184).
  Expected-connection push (live-like, L72) can be added later without changing the client side.
- LoginReply only after the zone answered ConnectionAttached (the client queues its own reliable packets until then,
  §1.2, so nothing is lost while the zone loads the character). If no zone is registered: LoginReply(false), close.
- Relog (0xF6 / 0xF7): handled by the channel-1 back end; the gateway only keeps the connection and closes on 0x14 /
  CloseConnection.

### 3.8 One packet to many (0x16)

Back ends send ToExternalConnectionList for broadcasts (chat to a landscape, entity spawns in range, world
announcements): the gateway applies ClientWire695.ToClient once per build present in the list, then queues the bytes
per SOE session (no re-serialisation per player). PerBuild becomes "one list per layout". The profiler counts it once
with recipients = n.

### 3.9 Gateway commands (opcode 9, source 3)

After LoginReply the gateway tunnels a CommandListPacket (0x1E/0x44, source 3) on channel 0. Commands: `gatewayprofiler
on|off|dump` (admin only; dump → system chat lines / web admin), later others (L74 for live's list). Replies are
tunnelled zone packets (system chat text). Unknown names → ignored with a log line.

### 3.10 Flood limits (ours)

Per connection, per channel token buckets (today's numbers: channel 0 / 1 / 3 300 burst, 100 s⁻¹; terrain bases 0xEC
/ 0xF4 their own 200 / 50 s⁻¹; channel 2 60 s⁻¹ after coalescing), signature rules (drop 0x27/0x17 DelayedTargetInfo,
exempt 0x10C WorldEditing cursor), opcode 9 2 s⁻¹, pre-login 32 packets. Over the limit: drop + warn once a minute;
sustained 10× over for 30 s: CloseConnection. Live's limits: not found yet (L74).

### 3.11 Ports and .env (new keys)

| Key | Default | Use |
|---|---|---|
| `GATEWAY_PORT` | = old `WORLD_PORT` 19000 (UDP, published) | client-facing; the zone no longer publishes UDP |
| `GATEWAY_INTERNAL_PORT` | 20300 (TCP, not published) | back ends dial in |
| `GATEWAY_INTERNAL_ADDRESS` | `gateway-${WORLD_KEY}:20300` | in each back end's env |
| `LOGIN_INTERNAL_ADDRESS` | `auth:20210` | gateway → login TicketConsume |
| `GATEWAY_HOLD_SECONDS` | 120 | 3.5 |
| `GATEWAY_ENCRYPTION_KEY` | today's WorldServer.EncryptionKey | RC4 / SOE key (moves from WorldServer.cs l.25) |

## 4. Proposed changes to internal-transport.md (for the coordinator)

1. Gateway link framing: `u32 length` + packet whose first byte is a **u8** internal opcode (the client's tracker reads
   one byte; FUN_143c7a9c0). Either make the internal opcode u8 everywhere or keep u16 only for non-gateway links.
2. Internal opcode numbers **7, 8 and 0x16 are traced** (header sizes 19 / 13 / 11 + 12n with u32 n at offset 7);
   assign From = 7, To = 8 (inference, documented), and fix the field layouts to fill exactly those sizes (3.4).
3. ExternalDestination = 12 bytes = u64 connectionId + u8 channel + u8 flags + u16 reserved (ours; size traced).
4. "Connections are made by the client side of each pair": for gateway links the **back ends dial the gateway** (the
   stable hub), and register (RegisterBackEnd).
5. Add the ours messages 0x20-0x28 (3.4); "character entered / left" become AttachConnection / ConnectionClosed with
   isReattach for restarts.
6. Correct architecture-leads.md L8: tracker sizes 2 / 6 are zone sub-opcode offsets, not gateway headers.

## 5. Build and test plan (isolated Docker copy; dev servers untouched)

Rules: own compose project `landmarkemu-gwtest`, own network `landmarkemu-gwtest`, own DB container + volume, own image
tags (`landmarkemu-gateway:gwtest`, `landmarkemu-world:gwtest`, `landmarkemu-auth:gwtest`), own ports, own data folder
`server-data/worlds/gwtest`; never `docker compose down` the dev project; clean up after (down -v, rmi). Don't run the
servers in a background task (BUILD-PATH rule 11). Close test clients after each run (pc-resource-limits).

1. **Library first, no behaviour change.** In LandmarkEmulator.Gateway add: internal packet models (7, 8, 0x16,
   0x20-0x28), framing codec, models for external 3 / 4 (string reason) / 7 / 8 / 9, the channel on outgoing tunnel
   packets (fix GatewayPacket l.30-40), and a unit-test project with byte-exact tests: header sizes 19 / 13 / 11 + 12n
   and count at offset 7; opcode-9 parse of `09 1E 00 43 00 …`; ChannelIsRoutable bytes `07|ch<<5, b, b`.
2. **In-process seam.** (**Done 2026-10-15**: gateway side `ClientSession`, zone side keeps the name `WorldSession` so
   no handler changed; the seam is `IClientLink` instead of the old IZoneSessionChannel scaffolding, which stays unused;
   early packets between LoginReply and the character load are held, max 256.) Split WorldSession into ZoneConnection (game state) and the gateway session, talking through
   IZoneSessionChannel / IZoneTunnelSink (reuse the scaffolding; drop the gRPC mode) with the internal messages as
   objects. Dev servers keep running the monolith build until 4 passes. Verify on the test client: login, chat,
   movement between two clients (0.589 + 0.695), build, re-zone (rezonetwice / tp_test scenarios), logout to character
   select and back.
3. **Out of process.** (**Done 2026-10-15** except the login-service TicketConsume — the gateway still consumes the
   ticket in the auth database as the world did — and the zone_server row, which the world still writes with its
   PublicAddress = the gateway's port. Zone link loss closes the gateway's clients until §3.5 hold + reattach.)
   LandmarkEmulator.GatewayServer (UDP + internal TCP server) and the zone's GatewayLink client
   (dial, register, reconnect with back-off). Login service: TicketConsume endpoint; zone_server row registered by the
   gateway. Dockerfile target `gateway`; docker/gateway/docker-compose.yml; world compose drops its UDP port.
3b. **Routing table by registration** (**Built 2026-10-16**, untested: §3.3 / §3.5 notes above; replaces the fixed zone
   and world-channel slots; test plan TESTING-NEEDED "Gateway routing by registration").
4. **Isolated copy up**: `docker compose -p landmarkemu-gwtest --env-file docker/gwtest.env -f docker/gwtest/docker-compose.yml up -d --build`
   with ports LOGIN 20142, ACCOUNT 5100, GATEWAY 19100, WEB_PAGES 19101, DB 127.0.0.1:3317, internal 20300 (not
   published). Fresh DB: create a test account + character via the account API on 5100.
5. **Connect test**: dlogin-style direct start of the 0.589 test copy with `Server=127.0.0.1:19100 Ticket=<ticket
   written into the gwtest DB> …` (SLOG = server-data/worlds/gwtest/console.log; the ticket update goes to the gwtest DB
   container with `docker exec … mariadb`, not dbq.sh's dev DB). Expect "entered the world"; client NetInfo.log
   "RECEIVED=OnConnect". Repeat with the 0.695 copy (TESTGAME / MATCH).
6. **Zone restart with the client connected (the key test)**: with the client in the world and idle,
   `docker compose -p landmarkemu-gwtest restart zone`. Expect, by the client's reaction:
   - client NetInfo.log: `RECEIVED=OnChannelIsRoutable channel=0 isRoutable=0` then `…isRoutable=1`, **no**
     `RECEIVED=OnDisconnect`, no ErrorDisconnected box (screenshot of the test client window);
   - after the zone is back: the reattach re-zone runs (loading screen, ClientIsReady in the zone log), then a chat
     line typed by the harness reaches the other test client and a `/emote` (zone command) works;
   - movement typed during the outage: the other client kept seeing it (gateway fan-out), and the zone's saved
     position after the outage equals the client's.
   Repeat 5 times; once while the client is mid-build (terrain requests held and replayed: no stuck build tool), once
   with a second client on the other build.
7. **Edge cases**: zone down > GATEWAY_HOLD_SECONDS → client goes to character select (0xF7 path) or shows the
   disconnect box, never hangs; logout to character select while the zone is down (0x14 answered by the gateway);
   duplicate login kick; world shutdown (dev "shutdown 10") still runs Ui.Logout for everyone; gateway restart drops
   clients cleanly and zones save.
8. **Bandwidth check**: profiler dump before / after (one 0x16 list per broadcast per layout; movement coalesced);
   no rise in bytes per player per minute against the monolith on the same scenario.
9. **Clean up**: `docker compose -p landmarkemu-gwtest down -v`, remove the gwtest images, delete
   server-data/worlds/gwtest. Then MODLOG + TESTING-NEEDED.md (user check: play normally while `docker compose restart
   zone` runs), docs/docker.md + protocol.md §1.2 updates, and only then switch the dev project (with the user told).

## 6. Risks

- **Stale client world after a zone restart** (L73): the client keeps entities on a same-landscape re-zone; a fresh
  zone re-sending them gives duplicate-spawn refusals (MODLOG: 107 in one session) and possibly crash paths seen around
  re-zones (boss list, placement). Test hard; keep the 0xF7 fallback.
- **Re-zone hang rules** (cave-teleport-hang): the reattach re-zone must be alone in its burst; the hold-queue replay
  must come after ClientIsReady, not with ClientBeginZoning.
- **Ordering**: replayed held packets may arrive after newer state; mark replayed (flag bit1) so handlers can drop
  stale requests (cell requests must still be answered or the client's 6 s scene timer reverts terrain).
- **Latency / CPU**: one extra hop per packet; terrain bursts (8.5 MB manifest) through the TCP link — size the
  frames (EdgeZoneGrpcLimits said 64 MiB) and stream large bodies.
- **Ticket path**: login service down → nobody can enter (as live's 14 Oct 2016 outage); players already in are
  unaffected.
- **Position authority**: the gateway fans out movement on its copy of landscape / range; ZoneChanged must reach the
  gateway before the client's first position at the destination, or players briefly see the mover on the old landscape.
- **0.589 / 0.695 mix**: header translation in the gateway only; body layouts stay in the zone (PerBuild) — the zone
  must still know each connection's build (AttachConnection.clientBuild).

## 7. Hand-offs

- **#15 world-channel**: channel 1 back end receives 0xF6 CharacterSelectSessionRequest (relog session, 60 s client
  deadline) and needs a relog-key source (today the zone writes the auth DB); decide whether social families on
  channel 1 go to #12 directly (gateway can route by family if asked).
- **#12 social**: chat lines come on channel 0 (zone) — the zone forwards; broadcasts back via 0x16 lists.
- **#14 terrain**: channel 3 previews (WorldEditing 0x10C) and terrain cell traffic (channel 0) — decide the owner;
  the unused TerrainPlan / ack-paced delivery idea needs SOE ack state, which after the split lives in the gateway
  (would need a gateway-side "deliver when acked" message).
- **#16 Environment Server / admin**: gateway profiler data (3.9) for the web admin page; GatewayProfilerRegistration
  0x1E/0x4D7, InternalConnectionBypass 0x4E0 and WorldKick* layouts (L48) if the admin page wants them.
- **Coordinator**: internal-transport.md changes (4); architecture-leads.md L8 correction (2 / 6 = zone sub offsets);
  login-gateway.md "To trace next" 5 and 6 are answered here (opcode 9 has no reply opcode; gateway position path =
  channel-2 body without opcode).
