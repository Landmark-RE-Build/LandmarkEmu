# Landmark network protocol

How the client and server talk, layer by layer, and the layout of every packet family the emulator handles. The server
speaks the 0.695 client's numbering natively (§1.6); the layouts were first traced on the 0.589 client and are marked
where 0.695 differs.
Types and conventions are listed in [README.md](README.md#conventions). "C→S" = client to server, "S→C" = server
to client.

## 1. Transport

### 1.1 SOE reliable UDP

Both the login server and the zone/gateway server speak the SOE (Sony Online Entertainment) UDP protocol, the same
family used by PlanetSide 2 and H1Z1. Protocol opcodes are big-endian u16:

| Opcode | Name | Notes |
|---|---|---|
| 0x01 / 0x02 | SessionRequest / SessionReply | Negotiate CRC seed/length, max packet size, compression flag. |
| 0x03 | MultiPacket | Several protocol packets in one datagram. |
| 0x05 | Disconnect | Either side. The client waits for the server to send one after `ClientLogout` (see §6). |
| 0x06 | Ping | Keep-alive. |
| 0x07 / 0x08 | NetStatusRequest / Reply | Client statistics. |
| 0x09 / 0x0D | Data / DataFragment | Reliable, sequenced payload (fragmented when large). |
| 0x11 / 0x15 | Ack / AckAll | Acknowledgements. |

- **Keep-alives and timeouts (0.695 client, traced 2026-10-18):** the gateway connection (UdpLibrary manager
  `ExternalGatewayApi_3`, `threading=1` in the client log, so its timers run on the UDP thread even while the main
  thread is busy in a loading screen) is built by the BaseUdpManager constructor FUN_143a0a980 on the UdpLibrary
  params FUN_142b5d5f0; no `UdpParams.*` keys are in ClientConfig.ini, so the built-in values stand:
  - `keepAliveDelay` **29 000 ms** (manager +0xc8 = params +0x20): the connection's GiveTime FUN_142b68590 sends Ping
    0x06 when it has *sent* nothing for that long; any other send (acks, data) resets it.
  - `noDataTimeout` **90 000 ms** (params +0x30, profile values in FUN_142b5d5f0; checked in FUN_142b68590 → disconnect
    reason 2 `DisconnectReasonTimeout`): the client's own limit for a silent server.
  - `oldestUnacknowledgedTimeout` 120 000 ms (params +0x34): unacked reliable data that old → `DisconnectReasonUnacknowledgedTimeout`.
  - In the world (run state 0x12, FUN_142cd4e60) the game also sends **KeepAlive 0x63 every 1 s** (constant
    `Network.KeepAlive` = 1 in Resources/StringHashToValue.txt, × 1000.0 at DAT_1446921e4; seen in the world log as one
    0x63 per client per second) and Synchronization 0xC3 every 5 s.
  - Server: a connection with no packet at all for `CLIENT_TIMEOUT_SECONDS` (default 60 = two keep-alive intervals, so
    one lost Ping is survived; Shared/Network/SocketHeartbeat.cs) is closed with Disconnect reason 2 and the character
    leaves the world at once. Behind the separate gateway the gateway measures it and sends the zone ConnectionClosed
    (reason Timeout). Live's server value is not known (not in the client); no linkdead / combat-logout rule is in the
    client's texts or the archived posts, so the character goes at once. A new login of the same character drops its
    older session at the login (PlayerManager.ReplaceOlderSessions), before the new client's loading screen.
- **Retransmission:** the server resends an unacknowledged reliable packet after `300 ms + 1.25 × ack ping`, then
  doubles the wait for each further resend (capped at 8 s). Without the backoff, a client that acks slowly (long
  frames while it meshes terrain) had its whole window resent every 1.3 s: 3,500 resends a minute against 1,500
  originals, and loading into a deep cave took 18 minutes instead of 7.
- **Compression:** an optional 1-byte flag before the body (1 = compressed). The compressed body uses the
  client's block codec (see §1.4), and only bodies longer than 0x17 bytes are compressed. It works but is left
  off: the zone payload is RC4 ciphertext by then, so it never shrinks.
- **Encryption:** after the gateway login, Data payloads are RC4-encrypted with the session key.

### 1.2 Gateway and tunnel

Inside SOE Data packets the zone server uses *gateway* packets. The client logs in to the gateway with a
`LoginRequest` (character id + ticket), then everything is wrapped in `TunnelPacketToExternalConnection` (S→C,
0x05) / `TunnelPacketFromExternalConnection` (C→S, 0x06) carrying a channel byte:

| Channel | Use |
|---|---|
| 0 Zone | Almost all game packets (this document), terrain cell requests included. |
| 1 World | World-level requests: friends, guilds, ignore, chat membership, trade invitations, Player Studio, logout to character select (0xF6). |
| 2 PositionUpdate | Player movement. |
| 3 | WorldEditing 0x10C build-cursor previews (once called "Voxel"; the client has no voxel channel). |

The channel picks the back end. In the world server's own gateway every channel goes to the one world process. The
separate gateway (`LandmarkEmulator.GatewayServer`) keeps a routing table filled from the back ends' registrations
(RegisterBackEnd: kind, world, landscapes, channel mask): channel 0 to the zone process of the player's landscape,
channels 1-3 to a service registered for the channel (the world-channel service takes 1), else to the zone. It tells
the client `ChannelIsRoutable` (gateway opcode 7: channel in the first byte, bool isRoutable, bool) when a channel loses
or regains its back end, and `ConnectionIsNotRoutable` (opcode 8, no body) when no zone serves the player's landscape at
login; the client only writes both to `Logs/NetInfo.log` (research/trace/architecture/gateway-service.md §1.1, §3.3,
§3.5).

In the world server the two halves are separate classes (gateway-service.md, Phase B step 2): `ClientSession`
(Network/ClientSession.cs) is the gateway side — SOE session, RC4, the gateway login (0.695 builds only, §1.6), the
DelayedTargetInfo drop and the transport flood limits — and `WorldSession` is the zone side with the character's game
state and the handlers. They meet only through `IClientLink` (zone → client: a zone packet in the client's own
numbering, sent as given, or close) and `WorldSession.FromClient` / `Flush` / `LinkClosed` (client → zone), the same
contract as the internal link's TunnelPacket 7 / 8 and CloseConnection / ConnectionClosed, so the gateway can move to
its own process without touching game code. A session that stops answering is closed after 180 s of silence like any
other (the zone side then saves the character).

### 1.3 Zone packets

A zone packet starts with its opcode. Most opcodes are one byte followed by a padding/zero byte (written here as
`u16 base`, e.g. `F9 00`), then a sub-opcode whose width depends on the family:

| Family | Base | Sub-opcode width |
|---|---|---|
| PlayerUpdate | 0x26 | u32 in 0.695 (`26 00 58 00 00 00`; u8 in 0.589), §1.6 |
| ClientUpdate | 0x29 | u16 |
| Command | 0x1E | u16 |
| AbilityBase | 0x27 | u16 |
| InventoryBase | 0x2D | u16 |
| GroupsBase | 0x2B | u8 |
| UiBase | 0x32 | u8 |
| RecipeBase | 0x42 | u32 |
| FriendBase | 0x4D | u8 |
| GuildBase | 0x83 | u16 |
| ResourcesBase | 0xC4 | u32 sequence + u8 sub |
| EquipmentBase | 0xCB | u8 |
| TerrainCell | 0xEC | u32 |
| PropBase | 0xED | u8 |
| TemplateBase | 0xF0 | u8 |
| VoxelConstructionBase | 0xF4 | u8 |
| MailBase | 0xF8 | u32 (0.695 numbers: §1.6) |
| ClaimsBase | 0xF9 | u8 (0.695 numbers: §1.6) |
| LootPinataBase | 0xFB | u8 |
| TeleportBase | 0xFC | u8 |

The client's zone dispatcher looks the opcode up in a byte table (`Zone_DispatchPacket`, see
[client-reference.md](client-reference.md)). In the decompiled switch statements, case labels are often shifted
by one (`case 0x2c` handles sub 0x2D).

### 1.4 The client block codec

Several blobs (terrain cells, compressed SOE bodies) use the same container: the magic bytes `A1 B2 C3 D4`, a
**big-endian** u32 uncompressed size, then a zlib stream. Terrain layer blobs additionally put a little-endian u32
raw size in front.

### 1.5 Rules the client enforces

- **Consume exactly.** Most readers reject the whole packet when bytes are left over or missing. A wrong field
  count usually means the packet is silently ignored. A few, such as the health resource record, crash the
  client instead.
- **Answer every request it waits on.** Examples:
  - An unanswered ability cast leaves the client "cast pending", which shows "Ability on Cooldown" forever.
  - An unanswered `TransferItem` blocks the second half of a tool switch.
  - An unanswered `CasterInfoRequest` disables the tool.
- **Use `WriteUShort` for u16 fields.** `GamePacketWriter.Write` has no `ushort` overload. A `(ushort)` value
  passed to `Write` is promoted to `uint` and goes out as 4 bytes.

### 1.6 Wire numbering: the 0.695 client's, natively (since 2026-10-17)

The server reads and writes every opcode and sub-opcode exactly as the **0.695 client** numbers them. There is no
translation anywhere on the wire path: a sub constant in the code is the number on the wire, and its source is the 0.695
client — the client's own packet-id name tables, `research/trace/0695/coverage-695.md` (every family's dispatcher /
send-template row, 0.589 → 0.695; coverage-695.md below) and `research/trace/0695/packets-695.md` ("Changed layouts" A). The 0.589 client
is refused at the gateway login (`Gateway/Network/ClientBuilds.cs`): it numbers four families differently and reads
PlayerUpdate with a one-byte sub; it is served by the frozen `client-0.589` branch.

**What differs from 0.589** (and so from older notes in this document, which were traced on 0.589):

| Family (sub width) | 0.695 change [P] | 0.695-only subs | Client evidence |
|---|---|---|---|
| PlayerUpdate 0x26 | sub is a **u32** (was u8), both directions: `26 00 ss 00 00 00` | 0x80 SetPlayerBio (both ways) | C→S header writer FUN_142c13350 writes 4 bytes (0.589 FUN_142b79bb0: 1); S→C dispatcher FUN_142cdfa60 reads `*(int *)` (0.589 FUN_142c45fa0: `char`); every body reader starts u16 + u32 |
| Claims 0xF9 (u8) | subs 0x45..0x60 moved **+2** (e.g. UpdatePropLimits 0x5F → 0x61, DebugAddItemClaim 0x5E → 0x60) | 0x45 UpdateAdventureFlags (SetAdventureFlags), 0x46 StartClaimPlacement, 0x63 PlayerEnteredClaim (C→S) | id table; S→C dispatcher FUN_142dbecb0 (last case 0x5E → 0x60, switch on sub − 1) |
| Mail 0xF8 (u32) | subs 4..0x16 moved **+1** (InboxList 6/7 → 7/8, Read 8/9 → 9/0xA, Delete 0xA/0xB → 0xB/0xC, NewMail 0xE → 0xF, ConsumeAttachment 0xF/0x10 → 0x10/0x11, EnableWindow 0x13 → 0x14, AttachItem 0x14/0x15 → 0x15/0x16, ComposeResponse 4 → 5; server-only ids ReturnAttachments 5 → 6, ConsumeAttachmentZone 0x11/0x12 → 0x12/0x13, System 0x16 → 0x17) | 4 DeveloperMailPlayer (id only, no class) | id table cMailPacketId*; readers FUN_143374400 … (coverage-695.md §0xF8) |
| InGamePurchase 0x45 (u16) | subs ≥ 0x30 moved **+1** (ProductCreateResponse 0x31, ProductRemove 0x32, ProductUpdate 0x34, Follow / UnFollow 0x35 / 0x36, RequestAccountStore 0x37, AccountStore 0x39, AddFollowing / RemoveFollowing 0x3A / 0x3B, Search 0x3E / 0x3F, GetProductForReview 0x40, ReviewProductResponse 0x41, SubmitProductReview 0x43 / 0x44, ModifyScreenshotsResponse 0x46, PointOfNeedRequest 0x47, ModifyScreenshots 0x4A, IsTemplateValid 0x4D / 0x4E); 0x2F ProductCreate and everything below 0x30 unchanged | 0x30 TemplateProductCreate (C→S; taken as ProductCreate, `Commerce.FromWire`) | S→C dispatcher FUN_143258f50 cases 0x31..0x4E (0.589 FUN_1431adf70 0x30..0x4D); ProductCreate's send site sets 0x2F (part_0093.c:18628) |
| InstancedGame 0x10D (u32) | subs ≥ 0xD moved **+1** (GetConfigurationResponse 0xE, SetConfiguration 0xF, StartRequest 0x10, EndRequest 0x11, PropState 0x12, PropInteraction 0x13, PlayerRemoval 0x14, TeamMembers 0x15, ObserverRegistration 0x16, UpdatePlayerFlags 0x17) | 0xD GetConfigurationRequest, 0x18 AddOrRemoveRule, 0x19 MatchmakingRequest (C→S), 0x1A MatchmakingUpdate (S→C) | coverage-695.md §0x10D (ctors / send sites part_0107); Frida 2026-10-17: PropState must arrive as 0x12 |

Every other family's header writer and dispatcher is "same" in 0.695 (coverage-695.md §1).

**Where the numbers live.** The enum (`ZoneMessageOpcode`) holds a family's base only, except PlayerUpdate, whose
values carry one sub byte (`0x2600ss` "…Wire", or `0x2600ss00`); the opcode codec `WorldSession.GetOpcodeBytes` /
`GetOpcode` writes and reads that sub as the client's u32 (a `0x2600ss00` value keeps its trailing 00 after the u32;
no S→C model uses one). Family sub numbers are constants beside the code that uses them:

| Family | Constants (0.695 numbers) |
|---|---|
| Claims 0xF9 | Game/Claims.cs (StartClaimPlacementSub 0x46, PlayerEnteredClaimSub 0x63, SetAdventureFlagsSub 0x45, the request switch), Game/PropLimits.cs (0x61), Game/ClaimInventory.cs (0x0D, 0x23, 0x60), Game/Contests.cs + CommerceServer (0x18) |
| Mail 0xF8 | Game/Mail.cs, Network/Message/Model/MailRequest.cs, Internal/Social/MailMessages.cs (client requests handed on + the server-only ids, shared by the zone and the social service), SocialServer/MailService.cs |
| InGamePurchase 0x45 | Game/Commerce.cs, Game/SiteScreenshots.cs, Network/Handlers/InteractionHandler.cs (PointOfNeedRequestSub 0x47), CommerceServer/ClientPackets.cs, Internal/Commerce/CommerceMessages.cs |
| InstancedGame 0x10D | Game/InstancedGames.cs, .Editor.cs, .Rework.cs, .Finder.cs, .Monitor.cs |

The zone and the services (social: mail; commerce: Player Studio) exchange client requests with the client's sub
numbers (ClientRequestForward / CommerceClientRequest) and client packets whole (TunnelToExternal), so all of them must
run the same numbering: deploy them from one build.

**The wire path, both directions (inventory of the translation steps that were removed).** Until 2026-10-17 the server
worked in 0.589 numbers and a header translation sat at the gateway (`Gateway/Network/Wire/ClientWire695.cs`, selected by
`ZoneOpcodeTranslator.Uses695Wire`); a packet written with its real 0.695 number was shifted twice (PropState 0x12 went
out as 0x13 and was dropped; RequestAccountStore 0x37 → 0x36 → 0x35 before 2026-10-16). The steps, and what replaced
them:

| # | Where | Direction | Was | Now |
|---|---|---|---|---|
| 1 | `ZoneGatewaySession.OnTunnelData` (Gateway; used by the world's in-process `ClientSession` and by `GatewayServer/GatewayClient`) | C→S, every channel but PositionUpdate | `ClientWire695.ToServer`: PlayerUpdate u32 → u8 sub; the four families' subs shifted down; 0.695-only subs → internal numbers (`NewBase` + sub: Claims 0x80, others 0x1000) | packets go on as sent |
| 2 | `ZoneGatewaySession.SendToClient` (both gateways) | S→C | `ClientWire695.ToClient` unless the packet was marked client-shaped: PlayerUpdate u8 → u32 sub; subs shifted up; internal numbers → 0.695 | packets go out as given |
| 3 | `IClientLink.Send(packet, clientShape)` → in-process `ClientSession`, or `RemoteClientLink` → TunnelToExternal flag 2 `ClientShape` → `GatewayServer/ZoneBackEnds` → `GatewayClient.FromZone(packet, clientShape)` | S→C | "client shape" marked raw dev packets (`WorldSession.SendRaw`) to skip step 2 | flag and parameter removed (a gateway ignores flag 2) |
| 4 | Game code: `ClientWire695.Internal(family, sub)` (Claims.cs × 3, Commerce.cs, InstancedGames.Rework.cs × 2, .Finder.cs × 2) | both | internal numbers for 0.695-only subs | the 0.695 numbers |
| 5 | `Commerce.FromWire` / `ToWire` | both | mapped internal 0x1030 → 0x2F; `ToWire` identity (a second shift until 2026-10-16) | `FromWire` maps 0x30 TemplateProductCreate → 0x2F; `ToWire` removed |
| 6 | World-channel service (`WorldChannel/Sinks.cs`) and its brokers | C→S taken there; S→C tunnelled straight to the gateway | read 0.589-shaped packets (`26 00 68`); wrote 0.589 numbers, step 2 translated | reads the u32 PlayerUpdate sub; Encounter / Rating / Trade / Logout families are unchanged in 0.695 |
| 7 | Social and commerce services (`SocialServer/ClientPackets.Mail`, `CommerceServer/ClientPackets`) → zone `Remote*Service.Deliver` → `WorldSession.GetOpcode` → `EnqueueMessage` | S→C | 0.589 numbers, step 2 translated | 0.695 numbers, sent as given |
| 8 | `WorldSession.SendSelf` (`Use589Layout` from the build), `Speaks695` (from the build) | S→C bodies | per-build bodies | the 0.695 bodies only: `Use589Layout`, `Speaks695`, `PerBuild` and every 0.589 body branch removed (2026-10-17, phase 2; the 0.589 layouts live on in the frozen `client-0.589` branch) |
| 9 | Raw byte builders: `DevCommands` treefall (`26 00 2D`, sent client-shaped, so never translated: a 0.695 client misread it), `PlayerUpdateLightweightToFullPc`'s nested `26 00 53` (inside a body; the 0.695 reader FUN_142c294b0 → FUN_142c34680 stops after the character blob, how the trailing bytes are read is not traced yet) | S→C | — | treefall writes the u32 sub; the nested bytes are unchanged |

Movement (channel 2, PositionUpdate) was never translated.

**Proof.** A one-off check (not shipped) ran every changed and unchanged sub constant
of the four families (107) through the old path — the old constant through the old `ClientWire695` — against the new
native constant, both directions, checked every new constant against the 0.695 client (id table, coverage row,
dispatcher case or send site), and compared every PlayerUpdate enum value through the old codec + translator with the
new codec (159 values S→C byte-identical; 127 subs × 3 bodies C→S). Differences found: **DebugAddItemClaim** (Claims
0x60, `ClaimInventory.DebugAddItemSub`) was already written in the 0.695 number, so the old translation turned the
client's 0x60 into 0x5E and `/item claimAddItem` never reached its handler — the native number is right (0.695 id table
0x60); and C→S PlayerUpdate subs the enum knows only as `0x2600ss00` (91, of which only Loading 0x6C and
BindingCustomization 0x71 have handlers, both ignoring the body) matched before only when the body's first byte was 0,
which the old lookup then ate; natively the sub alone matches and the body stays whole.

## 2. Login server

The login server (default UDP 20042, `Server=` in `ClientConfig.ini`) handles `LoginRequest` (session key from the
launcher or from a character-select session), the server list, the character list, character creation/deletion
and `CharacterLoginRequest`, which hands out the gateway address and ticket. The server stamps each ticket with its
issue time and the world it was issued for (auth migration `AccountServerTicketBinding`); the gateway (in-process, or
the login service for a gateway in its own container) refuses and clears a ticket older than 2 minutes or one for
another world. A ticket for world 0 (written by a direct login that skips the server list) is good on any world.

Login packets the 0.695 client builds with a payload (CharacterCreateRequest 0x05, CharacterLoginRequest 0x07) put
their fields in a byte array written as `u32 count + bytes`, so the u32 after the ids is the payload length:

- CharacterCreateRequest `{u64 server, u32 length, u8 empire, u32 profile type, u32 gender, string name, list<{u32 slot,
  u32 option, u32 tint}>, u32 skin tint, string (always ""), u32 race, u32 gender, u32 gear set}` (FUN_1430ad2f0 /
  FUN_142f59020).
- CharacterLoginRequest `{u64 character, u64 server, u32 length, string locale, u32 locale id, u32 gateway id}`.
- CharacterLoginReply payload `{string address, string ticket, bytes key, u64 character, u64 guid, string account,
  string character name, string exit URL}`: a non-empty exit URL is opened in the player's browser when the game closes
  (FUN_142d56760), so the server sends "".

- LoginReply `{bool LoggedIn, u32 Status, u32 ResultCode, bool IsMember, bool IsInternal, string Namespace,
  AccountFeatures (u32 n × {u32 key, u32 Id, bool Active, u32 RemainingCount, string RawData}), ApplicationPayload
  (u32 length + bytes), ErrorDetails (u32 n × {string name, string value})}` (FUN_143a54c80, names from the XML reader
  FUN_143a57cb0); the server sends the last three empty.
- CharacterSelectInfoReply `{u32 status, bool CanBypassServerLock, u32 n × EntityDetails {u64 character, u64 last
  server, f64 last login, u32 status, ApplicationData (u32 length + bytes)}}`; only entities with status 1 are parsed
  (FUN_1430b4d80). ApplicationData is the character payload (FUN_1430ad7c0): `{string name, i8, u32, u32, u32 ModelId,
  u32 Gender, u32 Race, u32 ProfileId, u32, u64, list<u32>, model (FUN_142c18710), list<CharacterAttachmentData>,
  friends, u64 ServerId}`. The data source GameData.Login.Characters takes Guid, Name, ServerId (the payload's last
  u64), ServerName, ModelId, Gender, Race, ProfileId (FUN_1430bb290); CharacterSelection.lua selects the character's
  ServerId world (0 = the best one), so the server sends the character's last world. The i8, the two u32 before
  ModelId, the u32 after ProfileId, the u64 and the int list are never read.
- The payload's friends (`Friends::Friend`, FUN_1430ae3a0 / FUN_1430ad9e0): `u32 n × {u32 key, string (StringFixed<32>),
  string (<40>), string (<64>), u64, bool online, string status}`; status is XML `<Status StatusId MiniGameType
  MiniGameNameId ZoneId WorldId/>` (FUN_1430b85f0). For the selected character the client counts each world's online
  friends whose WorldId names it (FUN_1430c43e0) into the server list column FriendsOnline (FUN_1430c3840); the shipped
  ServerSelect UI never shows it. The login server fills the lists from the social service (CharacterSelectFriendsQuery,
  with presence) or, without it, from character_friend (all offline); AuthServer.json CharacterSelectFriends turns it off.

Login tunnel (TunnelAppPacket 0x10 / 0x11, `u8 type` first; 0.695 switch FUN_1430b5370):

| Type | Dir | Content |
|---|---|---|
| 1 / 2 | C→S / S→C | NameValidationRequest / Reply |
| 3 | S→C | QueueStatus (login queue place) |
| 4 | S→C | ServerQueue: the queued character may log in now |
| 5 | C→S | QueueCancel `{u64}` |
| 6 | C→S | LoginInit (empty); answered with type 9 |
| 7 | S→C | `{string}`: read and dropped by the client (its handler is an empty function); not sent |
| 9 | S→C | ClaimData: `list<ServerClaimBasicData {u64, u64 owner character, u64, u32 server id, u32, u32, bool, string, 3 × f64, 3 × f64, u32, u32}>`; the server list counts them per world ("Claims") |
| 10 | S→C | ArtData: the eight tint definition lists (also zone packet 0x116 sub 1); field names in docs/unknowns.md |

## 3. Entering the zone

| Step | Packet | Notes |
|---|---|---|
| S→C | `SendSelfToClient` 0x0F | The player's whole state: position, appearance, inventory, hotbars, recipes, currencies. The 0.589 and 0.695 layouts differ in the tail (sendself.md); it must be consumed exactly or the client aborts with `0xBADBEEF`. Its transient id is the player's 2-bit-length entity id. |
| S→C | `ZoneDetails`, `ZoneDoneSendingInitialData` | |
| C→S | `ClientIsReady` | The server then sends the rest of the world (other players, props, claims, trees). |
| S→C | `Synchronization` 0xC3 | 6 × u64 + bool. Every client must get the same server millisecond clock, or remote movement stutters. The bool (+0x40) is "reply": the client answers a server Synchronization with it set (FUN_142ce7090) and its own requests have it clear; the server answers only requests. |
| S→C | `GameTimeSync` 0x37 | `{u64 timestamp, f32 cycle speed, bool paused}`. The client takes the time of day from the timestamp's UTC h/m/s and advances it at the cycle speed (see gameplay.md, world clock); while `paused` is set (ClientGameTimeManager +0xc0) the clock stands still. Sent false. |

### 3.0 Zone details, zoning and game settings (0.695)

All three are read and stored field by field by the zone dispatcher FUN_142d1ccf0; the models name every field with its
client slot (Model/SendZoneDetails.cs, ClientBeginZoning.cs, Shared/ContinentDefinition.cs, ClientGameSettings.cs).

- **SendZoneDetails 0x2E** (reader FUN_142c246b0, case 0x11): `str zone name, u32 zone type (5 BlockTerrain;
  0x7FFFFFFF = partial: world only), bool encounter flags (all of EncounterDataCommon, +0x1a = Ui.IsInCombat),
  str sky file (BaseClient::InitSkyDefinition), u32 zone mode (game +0x32868; 3 = starting zone), u32 geometry id
  (game +0x31fac, the StaticZoneData row id), u64 (formatted and discarded: unused), u32 zone id (+0x378d0),
  u32 world id (+0x378cc), u32 world name text, ContinentDefinition, bool IsInNewbieZone (Lua Ui.IsInNewbieZone)`.
  The server sends IS_NEWBIE_ZONE of StaticZoneData (true on the Pioneer lands); live 406 sent false.
- **ContinentDefinition** (reader FUN_142c1ddf0): the client's own ContinentDefinition record, every field named by
  its INI / table loaders (FUN_143cdf940, FUN_143ce07c0): ContinentId, Version, NameId, Name, Description, f64
  VoxelSize (40), 3 × f64 offset, 3 × f64 extents, UseContinentMap, FourMaterialBlending, BiomeMap + f32 m/texel,
  ShapeMap + f32, HeightLayerMap + f32, MaxClaims, Biome blend curve (2 × f32), material blend / noise factors,
  material noise {step, lacunarity, u32 octaves, tile}, layer start / end depth, overland cave density %, overland
  tier, f64 OceanHeight (2999), OceanMaterialId (474 Ocean Water), EntryRequirementExpressionId, DestructionDisabled,
  TeleportImageId, UseHeightLayerMap, HeightLayer blend curve, ProceduralLayerType, DistanceField. A new ContinentId
  rebuilds the voxel world (FUN_143cdf500 → FUN_143f9a0e0).
- **ClientBeginZoning 0x23** (reader FUN_142c23310, case 8): `str zone name, u32 zone type, vec4 position, quaternion,
  str sky (unread: the sky resets to "sky.xml" until the zone details), u8 movement version (game +0x32560, see §4.3:
  send 1), u32 zone mode, f32 (ActorManager +0x6ce678, floored at its default 1500; reader not found yet), u32
  geometry id, u32 world id, u32 zone id, u32 world name text, bool (ActorManager +0x6ce68c, cleared when an encounter
  ends; reader not found yet), bool WaitForZoneReadyPacket (send false), ContinentDefinition`.
- **ClientGameSettings 0x8E** (reader FUN_142c236c0, case 0x3b): `u32 (unused), u32 default interaction range (10),
  u32 (unused), bool movement check, f32 game time scalar (1.0; GameEvents:OnSetGameTimeScalar), u32 shared equipment
  record (0 = per profile), u32 shared experience record (1), u32 (unused), f32 impact speed threshold (12), f32
  impact scale (110)`. Sent as the live capture (ClientGameSettings.LiveBody); the two "shared record" switches are
  latched by the first packet of the client process.

### 3.1 SendSelfToClient fields (0.589)

The reader (`SendSelfToClient_Read`, FUN_142b80420) stores each field into the client's self-data object (global
`0x1453524a0`). Names come from what the client does with each offset: the Lua bindings that read them were found
from the client's Lua binding tables and the functions that read each offset.

| Model field | Offset | Meaning (client evidence) |
|---|---|---|
| `ServerTime` | local | Server clock (unix s). The client keeps (own clock − this) to convert `+0x290`. |
| `TransientId` | +0x38 | 2-bit-length entity id. |
| `Position` / `Rotation` | +0x260 / +0x270 | Spawn position and rotation quaternion. |
| `Coin` | +0x72a38 | Lua `GetPlayerCoinCount`, merchant `SetPlayerCoin`. |
| `Currencies` | +0x72a40 | (currency id, amount), each passed to the currency setter FUN_142f33fd0. |
| `CreationDate` | +0x74a70 | Unix s; `/played` "Character Created". |
| `PlayedMinutes` | +0x74a7c | `/played` adds the current session. |
| `LoginCount` | +0xf4 | `/played` "Character Logins". |
| `IsUnderage` | +0xf8 | Lua `IsUnderage`. |
| `IsDead` | +0xf9 | Opens `RespawnHandler:Show` at login. |
| `MembershipStatus` | +0xfc | 0 none, 1 trial (`IsTrialMember`), 2 member (`IsMember` = non-zero). |
| `IsPlayerStudioRegistered` | +0x100 | Lua of the same name. |
| `ShowMemberNagScreen` | +0x108 | Lua `ShouldShowMemberNagScreen`; ignored for members. |
| `LanguageId` | +0x10c | `ClientRequirementCheckLanguage`; localises shop country names (default 2). |
| `CountryId` | +0x110 | `ClientRequirementCheckCountry`. |
| `PreferredLanguages` | +0x74a98 | Bit mask; the client sends `PlayerUpdatePreferredLanguages` on change. |
| `SpeechSetId` | +0x280 | Voice set used by `PlayerPlaySpeech`. |
| `ClassProgressions` | +0x7a9d0 | `ClassProgression` objects keyed by class id. |
| `FacialAnimReceiver` | +0x285 | SOEmote receiving (`/soemote receiver`). |
| `CombatInputDisabled` | +0x286 | Keeps the "Combat" input context off. |
| `BypassClaimPermissions` | +0x287 | Claim permission and camera checks pass (FUN_142d15900, FUN_142d37330). |
| `IsContentBuilder` | +0x288 | Toggled by ContentManagement `SetContentBuilder`. |
| `Unused74a78`, `UnusedFa`, `Unused74a9c`, `UnusedTimestamp`, `Unused284` | | Stored, never read by the 0.589 client. |

- Identity: `u32 ×3, first name, last name`. The display name is first + " " + last. The three u32s are copied onto
  the player entity; no reader was found yet.
- Profile records (FUN_142b81480, 200-byte client profile; 0.695 FUN_142c1ac40): the model's fields map in order to
  offsets +0x4, +0x8 (name), +0xc, +0x14, +0x1c, +0x28, +0x20, +0x24, flag 0x80, +0x18, list +0x40, flag 0x40, +0x2c,
  +0x30, flag 0x20, +0x38 (signed byte), flag 0x10, flag 0x08, flag 0x04, +0x70, +0x74, +0x78, +0x80, +0x7c, +0x84,
  +0x8c, +0x90, +0x94, +0x98, +0x9c, +0xa0, +0x88, +0x3c, +0x34. Each is named from the 0.695 reader (sendself.md
  "Records named from the 0.695 readers", `SendSelfToClient.Profile`).
- Inventory: 12 × `{u32 size, bool}` (bag +0x30 / +0x34), then the items. Per item (`ClientItem`, FUN_142b87110 /
  0.695 FUN_142c208e0; every field named in `Model/Shared/InventoryItem.cs`): definition header, guid +0xc8, count
  +0xd0, consumed count +0xd4, u32 +0xd8 (a positive value blocks stacking), auto-resupply +0xdc (the saved switch, §InventoryBase 0x0D), container +0xe8,
  inventory id +0xf0, slot +0xf8, owner type +0xfc, u64 +0x100 (dropped by the client), chest +0x108 / slot +0x110,
  wear +0x148 (float), flags +0x14c (0x80 stacks of one, 0x40 no overflow warning), `SecondsSinceLastCast`
  (FUN_142b98a50: last cast = now − value, `0x7FFFFFFF` = never), optional rental block +0x120 (expiration, upsells
  sent, marketplace bundle) and design block +0x128, `MetaData` (`ItemInstanceMetaData`: present byte, type, type 1 =
  `ItemInstanceMetaCharacaterGuid` body), `TimedDeleteSeconds` +0x138 (clamped at 0), then one bool the list reader
  passes to the item (ClientItem +0x161, unread; live 1 on Lightstone and Cozy Campfire). The client also carries the
  server's item table schema (`items.stack_count`, `items.inventory_slot`, `items.timed_delete_offset`, …) as a plain
  name list, useful as a reference only.

## 4. Entities

### 4.1 PlayerUpdate 0x26 (u32 sub in 0.695, u8 in 0.589; §1.6)

| Sub | Name | Layout / notes |
|---|---|---|
| 0x01 | RemovePlayer | `i16 version (0), u64 guid` |
| 0x03 | UpdateHitpoints | `u64 guid, u32 current, u32 max, u32` |
| 0x0B | PlayCompositeEffect | `u8 target type, target, u64 instance, u32 effect, u32 delay ms, u32 duration ms (0 = the effect's own), bool, bool, bool`. Target type 1 TargetCharacterGuid `{vec4 offset, u64 guid}`; 2 TargetLocation `{vec4 offset, vec4 position (w 1), vec4 rotation}` (FUN_142b6a1b0). Used for hits, states (with a duration) and area effects on the ground. |
| 0x2D | Destroyed | `u64 guid, u32, u32, u32, bool keep` |
| 0x3B | Respawn request (C→S) | `u32 type, …` |
| 0x4C | SafeEject (C→S) | `u8 1` after the header, exactly (writer FUN_142ebdda0, send template FUN_142e85070, channel Zone) [P]. Sent once when a launcher throws the local player (ChainThrustPad FUN_1436d9c90, Thrower FUN_1436dbf00, AntiGravThrustPad FUN_1436d8250, thrust pads 0xCF types 1-3 FUN_142fe0f00 / FUN_142fe01c0 / FUN_142fe0bc0), after the client set its own entity state bit 26. Only a state record (0x0F / 0x51) clears that bit; while it is set the client sends no second SafeEject and type-1 thrust pads don't fire. No S→C case for 0x4C. Answered with 0x51 `clear = 1 << 26` when the player lands (Game/SafeEject). |
| 0x49 | Currency | `u64 guid, u32 n, n × {u8 currency id, u32 qty}` |
| 0x51 | UpdateCharacterStateDelta | `u64 guid, u64 source (0), u64 set, u64 clear, u32 time (server ms)`, exactly (reader FUN_142c2c7a0 via FUN_142c4fb90; handler FUN_142ecdd20 queues it, applied after the interpolation delay as `word = (word \| set) & ~clear` on entity +0xa20). **Bit 45 = blocking**: the Sweep stops on it, the Block targeter also needs the Block effect (§7 EffectStart). Used by Game/Combat/BlockStance. Never use 0x0F UpdateCharacterState for this: it replaces the whole word, including bits the client keeps itself. |
| 0x4B | InGamePurchaseResult | `u32, u32, u32` (exactly; the client reads it and does nothing more): the answer to PacketClientInGamePurchase 0xD0 (§14 InGamePurchase) |
| 0x58 | AddLightweightPc | Other players: guid, transient id, identity, u8 faction (0), model, u32 current profile id (entity +0x1474), position/rotation vec4, scale, bools underage / member / GM, …, **movement version byte (must be 1)**, … |
| 0x59 | AddLightweightNpc | The shared lightweight base only (creatures). |
| 0x5B | LightweightToFullPc | `bool compressed, byte[] blob, movement body, stat lists`. The appearance field after the 3 strings must be 0, or the body is hidden. |
| 0x5E | FullCharacterDataRequest (C→S) | `u64 guid` → answer with 0x5B (players) or 0x63 (props/NPCs). |
| 0x62 | AddLightweightProp | Props, trees, plants, the Spire (see 4.2). |
| 0x63 | LightweightToFullNpc/Prop | Needed before a placed prop can be edited. The 0.695 apply (FUN_142d532f0) copies the shared full-data base over the entity's defaults unconditionally, so the defaults are sent, not zeros: chat-bubble colours 0xFFFFFF / 0xD4E2F0 / 1 (+0x1c..+0x24), expected speed 8.0 (+0xb4), and for props disposition 2 (+0x90), 100 (+0xb8), 2 (+0x248), −1 at +0x250 / +0x254 (OPEN-LEADS L85). It does nothing for a prop's collision. |
| 0x69 / 0x6A | InteractionInfo request / reply | Reply entries `{u64 guid, u32 type (3 = interactable), 4 × u32 colour, u32, f32, f32 range, u8}`. |
| 0x6B | DynamicInteractionFlagging | `bool, list<u64 guid>`: marks entities the client should ask about. |
| 0x71 | BindingCustomization (C→S) | Key-binding report; nothing to store. |
| 0xC7 | ManagedPosition (C→S) | Position of an entity the client drives itself (a prop held for placement). |

### 4.2 Lightweight entity base and the prop part

The lightweight base is shared by props and NPCs:
- `u64 guid`, 2-bit-length transient id;
- two names (string key, literal), `u32` name text id;
- `u32 model id` (Models.txt);
- `vec4 scale`, three alias strings;
- `vec4 position`, `vec4 rotation` (quaternion);
- character-model info, flags, the attachment block;
- **three f64 = the origin the position is relative to** (send the zone origin);
- draw distance, effect tags.

The client places the entity at `(position × 10 + origin) − zone origin`, scaled back by 0.1.

Base fields the server fills for props (packet offsets as the 0.695 setup FUN_142d54040 reads them;
`Network/Message/Model/PlayerUpdateAddLightweightProp.cs`):
- **+0x19c control mode** (signed byte, shared with NPC spawns, copied to entity +0x41A0). The per-frame update
  (FUN_142eb1950 → FUN_142e93340) plays buffered Position 0x7F records only when it is not 0. Creatures and the moving
  part of a platform are sent 2; a static prop 0, which queues its position updates and never plays them.
- **Flag byte 2 (+0x299) bit 0x08 = collidable**, applied through SetCollidable (FUN_142ec2c20 → entity +0x1459 bit 4),
  which adds or removes the model's collision body. Every prop is sent with it set: with 0 a prop whose model finished
  loading after its setup lost its collision (players fell through the Spire's deck after travel). The model's own
  `.cdt` still decides the shape; a model without one stays walk-through.
- **+0x260 static area** (a GlobalAreas.txt id the client builds from its own tables: the movers' teleporter, flinger,
  launcher and platform areas), **+0x268 area guid** (the trigger volume the prop stands for; also how the Area Tool
  finds a Linking Area's box) and **+0x270 group key** (shared by the parts of one mover pack). A spawn with an area
  guid and an empty name is named "area" by the client (FUN_142d54040 → FUN_142ec18c0, its only caller), which then
  floats over the prop as a label; mover parts are therefore sent a single-space name, which shows nothing.
- **+0x280 spawn animations**: `u32 n × {u32 AnimationSelection id, bool, bool}` played as the entity is created (the
  Respawn Point's enabled / disabled look).

The prop part follows:
- `u64 owner` (+0x2A0). A prop whose owner is the local player goes into the client's locally managed set (FUN_142cde1c0
  → FUN_142c98c00), and the 0x7F handler (FUN_142ce6340) ignores server positions for those. The owner is used for
  nothing else there, so a server-moved prop (a platform's moving part) is sent with owner 0; the server keeps the real
  owner;
- a physics-body block: `u8 mode, u32 flags, u8, …`. Mode 1 = flags only. This is the movement body, **not**
  collision; collision comes from the model's `.adr`;
- a list, `u32`, `u64`, `bool`.

### 4.3 Position 0x7F and the movement body

`Position` (S→C) is a 2-bit transient id, then the movement body, then 4 floats (scale; 0 = unchanged).

The movement body is `u8 type, u32 timestamp (server ms), u8 version`, then signed 2-bit-length integers divided by
10^precision:

| Type | Content |
|---|---|
| 0 | Full: position (3 × p2), rotation (4 × p5), velocity (3 × p5), 8 × p2, p1, p5, flags |
| 1 | One 2-bit uint |
| 2 | Position + velocity |
| 3 | Rotation + 4 × p2 |
| 4 | 4 × p2, p1, p5 |
| 5 | The trailing uint only |

Non-full updates are dropped unless their version byte matches the entity's stored version.

**Frame:** a client sends its own movement in the **local** frame. Movement about **other players** is in the
remote frame (`local + (2558400, 2000, 2556450)`, `WorldSession.RemoteFrameOffset`), matching how players are spawned
(AddLightweightPc). Movement about **NPCs/creatures** is in the local frame, matching AddLightweightNpc (which
carries the zone origin itself); creature updates sent in the remote frame were ignored.
The state uint after the floats (2-bit length: bits 0-1 the extra byte count, value shifted left 2) is in type 0 and
type 1 bodies only; it is followed by one more 2-bit-length uint (empty: `00`; player +0x4ae8 when bit 0x20000000 is
set). Its bits, from the client's writer BaseClient::BroadcastLocalPlayerMovement (FUN_142ca4ce0; OPEN-LEADS L121):

| Bit | Meaning | Bit | Meaning |
|---|---|---|---|
| 0x1 | always set | 0x2000 | strafing |
| 0x2 | diving | 0x4000 | movement input held |
| 0x4 | sprint | 0x10000 | vaulting |
| 0x8 | one-shot resend | 0x40000 / 0x80000 / 0x100000 | grapple deploy / fire / grappling |
| 0x10 | jumping | 0x200000 | ledge grab |
| 0x20 | falling | 0x800000 | flung |
| 0x40 | not moving (velocity sent as 0) | 0x1000000 | fell off |
| 0x400 | landed / on the ground | 0x10000000 | standing on a supporting entity |
| 0x800 | double jump | 0x20000000 | the trailing uint carries a value |
| 0x1000 | gliding | 0x2000000 | sitting (the client's own /sit; see below) |
| | | 0x80, 0x8000, 0x400000, 0x8000000 | not named yet |

With 0x10000000 the body carries, after the trailing uint, 3 × p2 (the offset from the supporting entity) and a signed
2-bit-length int (its id). A change in mask 0x201f810 forces a full type-0 body; any other change sends a type-1 body,
so the server always holds the current word. Typical words: 0x10000441 standing on something, 0x441 / 0x401 in the
air standing / moving, 0x21 falling.

**Sitting (0x2000000).** The client's /sit is client-side: it sets the character's sitting flag (+0x1461 bit 0x4) and
sends only the chat line (EmoteStringById); the client then sits (AnimationSelection 351). The flag goes to everyone
in this word; an observer's client sits the character while it is set and stands it up (selection 478) when it clears.
The sitting client clears it itself when the player moves or jumps; a server stance or `/stand` must clear it with
`PlayerUpdate 0x65 RequestAnimationSelection {u64 entity, u32 selection, bool clear sit, u8 seated}` — an
AbilityPlayAnim only plays the animation, so the flag (and the word) stayed "sitting" and later observers sat the
character down again. The server sends every stance and "stand" that way (Game/Emotes.cs) and shows a stance to players
who meet the player later; a sit reaches them in the last full movement.

**Relay order (OPEN-LEADS L179):** the server keeps the newest body of each (mover, type) between ticks and sends each
mover's bodies oldest first by their u32 timestamp (`MovementFanout.InTimeOrder`, both the gateway fan-out and the
per-session queue). The observer's history push (0.695 FUN_1435a4430) resets an entity's history when a record is
older than its newest, so a type 2 sent before an older type 0 snapped the mover back by one tick. The platform block
(flag 0x10000000, offset, supporting entity's transient id) travels only in type 0 bodies (types 2-5 are built with
flags 1, type 1 with a zero block), and observers keep the last type 0's block across the type-2 stream [P,
research/trace/0695/agent-leadsB.md §4]; the relay forwards it unchanged.

### 4.4 Stats and resources

- **ClientUpdate 0x29/0x2C UpdateCalculatedStats:** `u64 guid, list<{u32 stat id, f32 value}>`. Stats used:
  - 3 HarvestingArea: a volume, shown as the mining ring diameter.
  - 11 harvest tier.
  - 13 HarvestSpeed: recast = 50 / max(1, speed) + base.
- **ResourcesBase 0xC4 (health bar):**
  - Header: `u32 sequence (must increase per player), u8 sub, u64 guid`.
  - Record: `u32 resource id, u32 type (1 Health), u32 variable count, …`, then 10 × u32 (the current value is
    first), `u32`, 4 × u64, 3 bools, `u32`.
  - A wrong count crashes the client.
  - Resource types: Health 1, Mana 2, Stamina 10, Armor 11, RevivePoints 12, Shield 14.
- **ClientUpdate 0x29/0x01 Hitpoints:** combat text only. It does not move the HUD bar.
- **Character stats** (a separate id space; names in Resources/StringHashToValue.txt `StatId.<name>^id^`, read by
  FUN_142ea2e20): element `u32 id, i8 kind (0 int, 1 float), u32 value, u32 base` (FUN_142c18c40; the getter returns
  value + base, the default when the stat is absent).
  - **ClientUpdate 0x29/0x06 UpdateStat** (own player): `u32 count, elements`. Live sent 37 at login (backpedal 3 = 0.65,
    crouch 4 = 0.25, sprint 5 = 1, strafe 7 = 0.75, acceleration / deceleration times 21-29 = 0.2 / 0.15 s, waist twist
    23 = 48 …); Game/CharacterStats sends them (setting LiveCharacterStats).
  - **PlayerUpdate 0x26/0x53 UpdateStat** (another character): `u64 guid, u32 count, elements` (handler case 0x52: each
    stat, then ProxiedCharacter vtable +0x588 copies the eight times 21, 22, 24-29 into the actor's animation). The
    same block is nested in LightweightToFullPc, which carries those eight (CharacterStatDefinitions
    SEND_TO_REMOTE_CLIENT).

### 4.5 Equipment 0xCB (u8 sub)

| Sub | Name | Layout |
|---|---|---|
| 1 | SetCharacterEquipment | Full set. |
| 2 | SetCharacterEquipmentSlot | `u32 profile, u64 character guid, u32 equip slot, u64 item guid`, 2 strings, attachment `{model .adr, texture alias, tint alias, decal, u32 tint, u32 composite effect, u32 slot, u32, bool}` |
| 3 | UnsetCharacterEquipmentSlot | `u32 profile, u64 guid, u32 slot` |

## 5. Inventory and items

- **Item definitions:** Command 0x1E/0x4B. `u32 size, u32 count`, then per item `u16 compressed size, u16 raw
  size, LZ4 block`. Sent at world entry (the client logs two ItemDefinitions.log lines per item, opening the file
  each time: ~15 s for 2,885 items on a PC with real-time file scanning). The cache survives a logout to character
  select, so the server skips the list for the same client coming back (its account session key is still the one
  from CharacterSelectSessionResponse).
  - **Cut into packets** of `ItemDefinitionChunkSize` items (default 100, Docker `ITEM_DEFINITION_CHUNK_SIZE`; 0 = one
    packet as the live dump). Each packet has the same layout with its own size and count; the 0.695 client adds
    each packet's entries to its cache (FUN_1434042d0 → FUN_143402db0, nothing cleared) and rebuilds the
    Character.Materials data source after each (FUN_143406d30). Why: the list arrives while the client's run state is
    Connecting (0xb, loading bar at 16 %), where its main thread takes queued packets for up to 1000 ms per pass
    (FUN_142df6120(…, 1000) → threaded UdpManager delivery FUN_142b60910) before pumping Windows messages; one
    packet holding the whole list was one 16–25 s frame (client log `SlowFrameMs=25523`) and Windows showed
    "Not Responding". Cut up, no pass runs much past 1 s.
  - 0x49 ItemDefinitionRequest (client → server) `u32 id`: sent for a missing definition from a few icon / tooltip
    wait lists (FUN_14335a2e0); most lookups never ask.
  - 0x4A ItemDefinitionReply `u32 size`, one entry as above; the client adds it and drops the pending request.
- **InventoryBase 0x2D (u16 sub):**

| Sub | Dir | Layout / notes |
|---|---|---|
| 0x02 | C→S | EquippedRemove `{u32 equip slot, u32 profile}` |
| 0x03 | C→S | EquipByGuid `{u64 item guid, u32 profile, u32 equip slot}` (Lua `EquipItem`) |
| 0x0B | C→S | DeleteItem `{u64 guid, u32 count, u32 mode}` |
| 0x0D | C→S | ToggleAutoResupply `{u64 item guid, bool on}` (writer FUN_142f18aa0). Sent by the Lua natives `CoinStoreScreen.TurnOnAutoResupply` / `TurnOffAutoResupply(item definition id)` for the first stack of that definition, only on a change, after the client sets its own ClientItem +0xdc, and only for definitions that pass FUN_142f1d360 (definition +0x100, +0x28, +0xfc > 0 and stack size > 1): no live definition does and no UI or script calls the natives. No reply. The server keeps the switch per item (`character_item.autoResupply`, `chest_item.autoResupply`; setting `ItemAutoResupply`) and sends it back in every item record; ItemUpdate has no such field |
| 0x0E | C→S | RearrangeItemSlots `{u32 container, u32 n, n × {u32 slot, u64 item guid}}` — the bag window's sort |
| — | — | TransferItem quantity: a count below the stack (split dialog / Ctrl-drag) or onto the same item merges / splits; container 1 = the vault (open through Command 0x1E sub 0x50 InteractionVault `{u16 sub, u64 vault entity}`) |
| 0x0F / 0x10 / 0x11 | C→S | Tooltip requests: by location, by guid, by definition id |
| 0x12 | S→C | Tooltip reply: item record with the compressed definition chunk; the record's 4th int list is the tooltip's KeywordDefinitions ids (the only source of item keywords: affinity, Ranged 56 / Melee 57, Pet 86); the u64 after the stats' u64 + u32 is the FraudTimer (record +0x120, 0.695 reader FUN_142f17a30): Unix seconds when a store purchase becomes tradeable, shown as "Tradable in %TIME" (562375), 0 = none |
| 0x15 | C→S | ActivateItem `{u64 guid}`: use / start prop placement / learn recipe |
| 0x16 | C→S | TransferItem `{location dest, location source, u64 guid, u32 qty, bool}`. **The first location is the destination.** |
| 0x19 | S→C | TransferItemResponse = the request echoed + `u32 result`. Must answer every 0x16. |

  - Location = `{u32 owner type, u32 container (0 bag, 2 equipped), u32 slot}`, plus a `u64` owner for owner
    types 2, 3 and 5. Slot `0xFFFFFFFF` = any.
  - Right-click quirks:
    - Right-clicking gear in the bag sends 0x16 with destination (equipped, slot 2 = main hand) for *any*
      gear. The server uses the item's own slot.
    - Right-clicking worn gear names bag slot 0. The server falls back to the first free bag slot.
- **Coins = Lumens:** the client's coin count everywhere (top bar, inventory, mail send/cash on delivery, trade)
  is the stack of item 1579 (StringHashToValue `Currency.ItemDefinitionId`) in container 8, which the self packet
  must declare with a size above 0. ClientUpdate 0x29/0x13 CoinCount `{u32}` sets the separate Lua
  `Player.GetPlayerCoinCount` value. The inventory's second currency slot is Daybreak Cash: InGamePurchase 0x45
  sub 0x2D WalletBalanceUpdate `{u32}`.
- **ClientUpdate ItemAdd 0x29/2:** the blob starts with `bool definition follows` (send false), then the item.
- **ClientUpdate ItemUpdate 0x29/3:** `guid, count, consumed count, u32 rental duration, u64 rental expiration time,
  slot, container`, then 2 × {5 × u32}, 2 × u32 (the last is the wear). Names from the client's item printer
  FUN_143b793d0; an item with a rental expiration or duration is never stacked. Use it for moves: delete + add loses
  the hotbar binding.

## 6. Session lifecycle

| Packet | Notes |
|---|---|
| KeepAlive 0x63 (C→S) | `{u32 game time}`, periodic. No reply. |
| CharacterSelectSessionRequest 0xF6 (C→S) | Logout to character select. |
| CharacterSelectSessionResponse 0xF7 (S→C) | `{bool ok, string login session}`. On `false` the client quits. |
| ClientLogout 0x14 (C→S) | Sent while leaving the zone. **The server must close the connection now.** The client waits up to 2 s; if the disconnect arrives later, its "disconnected" handler starts a second logout and the client shuts down. |

## 7. Abilities 0x27 (u16 sub)

- **2 CasterInfoRequest (C→S):** `u32 ability`. Repeated until answered.
  - Holding a tool asks for its abilities.
  - Weapons ask for combo abilities that aren't on the item.
  - Travel asks for 185/186.
- **3 CasterInfoReply:** includes range (min/max), cast and recovery seconds, cost pool, animation id, tool
  re-pick mode, name/description ids. The server answers from captured bodies and item data.
  - The f32 after the two strings (`ArcDegrees`, stored at record +0x60) is the ability's arc width. With the
    min/max range it shapes the sickle's blue plant-harvest wedge (ActionStateReaping → PlantHarvestDecal, set in
    FUN_1433abd90 as reaping params 9/10/11). An arc of 0 draws nothing.
  - **ClientExecutedEffectData** (reply +0xf8, `u32 count`, 14 × u32 each: `u32 0, u32 type hash, u32 0, 9 × f32,
    u32 0, u32 0`): movement the client runs itself when the cast starts. Types: Glide 0x9055911d, ChanneledGlide
    0x1c3c381c, Jump 0x80d81da0, DoubleJump 0x7f2bc64f, Sprint 0x7faad620, ChanneledSprint 0xf1fae31f, Fly 0xea3d9ce0.
    The f32 are AbilityParameterDefinitions ids (as floats) and seconds: Sprint / Fly a 3-phase curve {start id, end
    id, seconds} (the speed multiplier); Jump / DoubleJump {height id, forward-metres id or 271 = none}; Glide {vertical
    speed factor id, seconds id (0 = until landing)}. The client's only way to sprint, glide, double jump or leap
    (Game/MovementEffects, setting MovementEffects). The cast is refused locally unless each effect can start; a
    channelled kind (0x216b5dcb) swaps Sprint / Glide for the channelled forms, which a CastSuccess ends.
  - **ClientExecutedTargeterData** (reply +0x118, the last list before the final bool; `u32 count`, then per element
    `u32 id, u32 type hash, u32 origin, u32 follow, f32 delay (+0x14), u32 max targets (+0x10), f32 duration, f32
    start angle, f32 end angle, f32 re-hit seconds, f32 radius`, then `List<ShapeDefinition>` (`u32 count`; each `u32
    +8, u32 kind +0x10, u32 +0xc, u32 +0x18, bool +0x1c, u32 +0x14, 9 × u32 +0x20..+0x40`). These are targeters the
    client runs itself from the cast start (FUN_142e0f810 → FUN_142e257b0). Types: **Sweep 0xdeecf7d8**, **Block
    0xcc82431f**.
    - Sweep: after the delay it turns from the start to the end angle (degrees round the caster's facing) over the
      duration. It reports every character inside the swept angle and within the radius of its origin (1 = the
      caster; follow 2 = taken again every update) once per re-hit time, as FindTarget, up to max targets (0 = no
      limit). Then it sends ClientTargeterEnd. A character with entity state bit 45 stops it (effect 508).
    - Block: tests its shapes for a blocking character facing the caster. It plays the same effect and ends; it never
      sends FindTarget.
    - Melee weapon swings carry one Sweep (Game/Combat/WeaponCombat.Targeters, setting MeleeTargeters, OPEN-LEADS
      L170).
- **0x24 ClientTargeterFindTarget (C→S):** `u64 character hit, u64 cast guid, u32 cast hash, u32 targeter id, 16 bytes
  0` (FUN_14354e800 / FUN_14354c760). The client waits for no answer. The server checks the target against its own
  cone (range, arc, height, line of sight in caverns) and strikes it.
- **0x23 ClientTargeterEnd (C→S):** `u64 cast guid, u32 cast hash, u32 targeter id` (FUN_14354d570 / FUN_14354c520),
  once per targeter. The server closes the swing (the attack's whole-swing rule). If neither 0x24 nor 0x23 comes
  for a swing that waits for its sweep, the server resolves the swing itself after the sweep's window plus 1.5 s.
- **1 AbilityCastAttempt (C→S):** `u64 cast guid, u32 hash, u32 sequence, u32 ability`, target position, tail.
  - For picks, tail bytes 44–55 are the **mining ring point**.
  - For axes, tail +36 is the targeted entity guid.
- **7 CastSuccess (S→C):** caster guid, cast guid + hash, ability, animation, … Every cast must be answered.
- **6 CastFail (S→C):** the answer to a refused cast; same backtimed header as CastSuccess, then `u64 caster, u64 cast
  guid, u32 cast hash, u32 ability, u32, u32 animation (played if not 0), u32, u32 kind (2 on a harvest shows
  Harvest.CastFailMessage)` (reader FUN_142d63390, handler FUN_142d79ee0). The client ends the cast, clears its casting
  / charging state, drops the pending cast and takes back the hotbar cooldown it started (FUN_142d87d90).
  AbilityStartFailed / StartCanceled (0x10 / 0x11, `{u64, u32, u32, u32 ability, u64 caster}` / `{u32 ability, u64
  caster}`) only act on an ability started with AbilityStart.
- **0x18 AbilityCollisionReport (C→S):** the client reporting what an ability hit (seen when arriving at the
  Spire). Not handled yet.
- **Client can-cast errors:** 2/15 cast pending, 3 cost pool, 5 cooldown, 14 busy.
- **0x0E FinishChargedCast (C→S):** `u32 ability, u32, vec4 camera position (w 1), vec4 aim direction (w 0)` — sent
  when a charged ability's button is let go (FUN_142d767b0; charged = caster-info kind 0xF7150354, max / min charge
  seconds at reply +0xd0 / +0xd4). Answer the cast (CastSuccess) only then: one during the charge ends it.
  **0x0F CancelCast (C→S):** `bool` only (0.695 FUN_142e0f5a0; 1 = another input action interrupted the cast). It names
  no ability or cast, so the server must know what is pending. Sent when a charge is given up and when a **channel's**
  key is let go.
- **Channelled casts (kind 0x216b5dcb; Shift's sprint 326, Immediate Delivery 328, Endless Breeze 542 —
  Game/MovementEffects, OPEN-LEADS L168).** Press: the client starts the channel at once (ability manager mode +0x2b0 = 2,
  channelled effect at +0x2b8; input manager state 3, FUN_1434793b0). Hold: the server sends nothing that ends it — a
  CastSuccess or CastFail ends a channel (FUN_142e18340 / FUN_142e17eb0) — and confirms with **0x08 CastContinue (S→C)**
  (reader FUN_142e011b0, handler FUN_142e17b90): same header as CastSuccess, `u64 caster, u64 cast guid, u32 cast hash,
  u32 ability, 3 × u32, u32 animation, u32 effect, u32, bool skip-own`; it never resets the cast and restarts the effects
  if none runs. Release: the input manager's GiveTime (FUN_1434783f0, "Channel: end ability") calls CancelCast, which ends
  the effect and, only when one was still running, clears the current cast (+0x2a0) and the casting time (+0x290). A
  channelled effect that ended by itself (landing, unable to sprint) leaves both set, and the can-cast check
  (FUN_142e0f080 errors 15 / 2) then refuses **every** cast until a CastSuccess for that ability arrives. So the server
  answers each release with CastSuccess (`Force` off) a moment later, unless the client casts again first (CastSuccess
  matches the current cast by ability id only, so an early answer would end a fresh press of the same key). The server
  ends a channel itself (out of Energy, death, zone change) with the same CastSuccess.
- **Movement costs and cooldowns:** reply +0x20 / +0x24 / +0x28 cost resource / amount / ignore-pool (record +8 / +0xc /
  +0x10), +0x70 cooldown seconds (record +0x14c), +0xa0 cooldown group (record +0x7c), +0xa4 start the group's timer at
  CastSuccess (record +0x80) — copied by FUN_142e187a0; the can-cast check refuses with error 3 (cost) / 5 (cooldown)
  before any client-run effect starts. Every ClientExecutedEffect starts when the cast is sent (no delay), so a cast time
  does not delay the movement.
- **0x0C EffectStart (S→C):** puts an ability effect on a character (reader FUN_142d64890, handler FUN_142d7b690
  → character virtual +0x240). Header `u16 sub, u8, u32`; body `u64 key, u32 key, u32 effect id, u32, u32 type, 4 × u32,
  bool, u32, 4 × u32, u32, u64, u64 target, u32, 8 × u32 (incl. rotation), u32 animation type, u64, u32, vec4,
  5 × u32 (2nd: f32 strength → effect +0xa4, 3rd: f32 fade → +0xa8), vec4, 8 × u32 (7th: f32 duration s → +0x68,
  8th: start in server-clock ms → +0x40), bool, u32`. The type is the hash of an effect type name (FUN_142de0ef0's
  table). The local character acts on some types itself (FUN_1432777f0): **DetectCave 0x6eb699a5** goes to
  CaveDetectionManager (needs strength > 0; ends at start + duration × 1000; fade capped at half the duration) and
  makes the voxel renderer draw nearby caves in green — the Cave Seeker. EffectEnd (0x0D) with the same key removes
  it (FUN_14307df30).
  - The effect goes on the character at +0x78 (0.695 handler FUN_142e19660), any character, not only the recipient's;
    an EffectStart whose key that character already carries is ignored. **Block 0xC5838838**: +0x110 f32 full arc in
    degrees (→ effect +0xa0), +0x114 f32 height in metres (→ +0xa4). With entity state bit 45 (PlayerUpdate 0x51) it
    makes another client's Block targeter (FUN_14354dc10) treat the character as blocking an attacker inside the arc
    round its facing and within half the height. Sent for blocking stances to the other viewers
    (Game/Combat/BlockStance, setting BlockStanceVisible; 157° / 4 m for players, 360° / 4 m for creatures' shields, ours).

## 8. Terrain 0xEC (`EC 00`, u32 sub)

Full detail in [terrain.md](terrain.md).

| Sub | Dir | Name |
|---|---|---|
| 1 | C→S | ClientCellRequest `{u16, u64 context (always 0: a .bss global), list<{u64 cell, 4 × u32 layer version}>}` |
| 2 | C→S | PendingRequestTimeout `{u64 cell}`: a cell edit was never answered with sub 3 (the client stops editing it) |
| 3 | S→C | ClientCellUpdateResponse `{u64 cell, bool ok, string}`: send one per cell of every ApplyDiffRequest; with ok false the string is shown as an alert |
| 5 | S→C | CellsUpdate `u16 pad, list<{u64 cell, u32 version, list<{u32 layer, u32 layer version, u8 flag, blob}>}>, list<u64 transaction>, bool, bool`. **Version 0 = block (voxel) data**, which the client re-meshes; other versions carry finished meshes. |
| 7 | S→C | CellBlockDamage (crack visuals only) |
| 0x0A / 0x0B | S→C | Manifest / ModifiedCellManifestUpdate `{u64 cell, u32 code, u32 version, u8}` (the client re-requests) |
| 0x0C | S→C | ZoneProcessingComplete `{u16, u64 transaction}` |

## 9. Building: VoxelConstructionBase 0xF4 (u8 sub)

- **1 ApplyDiffRequest (C→S):**
  - Body: `u8, 24 bytes, list<{u64 cell, u64 cell, u32 4, u32 length, blob}>`, then the material usage
    `list<{u16 material, i32 count}>`, cell versions and areas.
  - Blob = 8000-byte changed-voxel mask + the cell in block format 2 (terrain.md).
  - Sent by every brush (Add, Remove, Smooth, Paint, …).
- **7 Response (S→C):** `{u32 request type, u32 result, bool}`.
  - Results: 0 ok, 0x0C insufficient materials, 0x0D props, 0x0E permissions, 0x11 too many template voxels, 0x1A
    "Only the Build Site owner may place, copy or create Designs using Designs purchased from the marketplace." — sent
    for a paste of a Player Studio copy onto a site the player doesn't own, and for a copy / Save Design by a non-owner
    whose box overlaps a placed Player Studio copy (`site_template_instance`, Game/TemplateInstances.cs).
- **8 ZoneUnknownPropList (S→C)** `{u32 n × {u32 key, u32 prop name text, i32 count}}` (hash list; reader 0.589
  FUN_1430b3240, 0.695 FUN_14315d360): "Construction.MissingPropKnowledge" then "count : name" lines — props of a paste
  left out because their recipe is unknown. **9 ZoneMissingPropList (S→C)** `{u32 n × i32 name text, u32 n × i32 count}`:
  "Construction.SkippedProps" — left out for lack of the item or materials.
  - Without a response, a later resend of the cell makes the client time out and stop editing.
- **2 ClientSelectionAreaRequest (C→S), copy and paste:**
  - Body: `u8 layer (FF), u32 action (0 copy, 4 paste), u64 level-2 cell, u32 x, y, z origin (voxels in that
    cell), u32 sx, sy, sz size, list<PropData>, 4×4 i32 rotation matrix (rows first), list<{u16 material, u32 n}>,
    u64, string, u64, list<{u64 cell, u64 version}> (every cell the box touches), list<{u64 key, u64, u32 type,
    u32 length, packed block, u32 if type is 2 or 3}>, u32, u8, string, u8`.
  - Copy (Select tool + Ctrl+C) carries one block: the box's voxels in a 40³ grid with the box origin at (0,0,0),
    same packed format as ApplyDiff (just the 8000-byte mask when nothing is solid). The server answers
    **6 ClipboardData** `{list<PropData>, list<{u16 material, u32 voxels}>, block list, 3 × u32 size, u32,
    bool has voxels}`. The paste tool only starts when that material list is not empty, and the client checks
    the player holds those amounts before placing.
  - Paste sends no voxels: the server places the copy it kept (grid point s → M·s, shifted so the turned box's
    minimum corner is the origin), charges materials, records one undoable edit, answers a Response (sub 7,
    type 2), then TerrainCell sub 3 for every listed cell **before** sending the new voxels. The client holds the
    listed cells pending (and drops their updates) until it hears sub 3; a copy marks them pending too.
- **0x0A ResponseObserved (S→C)** `{u32 operation, double×4 min, double×4 max, u32×3 direction, bool}` (reader
  FUN_1430b11e0, handler FUN_1430bbd60): another player's edit, effect only. Bounds are global VoxelFarm units (dm;
  the client subtracts its scene origin and scales by 0.1), the w components are ignored. Operation → brush effect
  (ModalToolBrushDefinitions BRUSH_TYPE): 0/2 placement (Stamp 4 when the bool is set, else FillStamp 11), 1 the
  same as a box, 3 delete (2), 4 heal (1), 5 smooth (0), 6 paint (5). Direction is the placement's up axis (0,1,0 for
  boxes); the bool picks ShapePlacement over ShapePlacementNoDestroy. A non-cubic box (or op 1) plays the box effect.
  The server sends it after every applied edit to the other players within 100 m (the builder's client played it).
  ApplyDiffRequest +0xe0 is the client's operation kind (1/2 box placement without/with an aimed cell, 3 any
  sculpting brush, 4–9 other operations); it doesn't name the brush, so the server picks delete/paint/placement/
  smooth from what the edit changed, and heal from the mask-only heal cells.
- **Voxel ownership (Voxel Vision).** The site window's Voxel Vision button fires `GameEvents:OnOpenVoxelVision`;
  the Builder List widget calls Lua `VoxelOwnership.RequestAllCellsForClaim(site)` (FUN_14354c980), which works out
  the site's level-2 cells (16 m, 40³ voxels) itself.
  - **4 ClientOwnershipRequest (C→S)** `{u32 n, n × {u64 cell, u32 cached version (0xFFFFFFFF = none)}}`
    (built in FUN_143086450; a 210 × 180 × 210 m site lists about 2,550 cells).
  - Every listed cell must be answered before the request completes (FUN_143083510) and the builders list fills:
    - **0x16 EnvOwnershipUpToDateResponse** `{u32 n, u64 cells}` when the cached version is current;
    - otherwise **0x17 EnvOwnershipDataResponse** (reader FUN_1430b0ed0) `{u64 (unused), u64 cell, u32 n + grid
      blob, u32 n + strings (owner names), u32 n + u64 (owner character guids), u32 n + i64 (unused), u32 version}`.
      Names and guids pair up into the cell's owner list; entry 0 is "nobody" (send a blank one). An empty blob means
      nobody owns anything in the cell.
  - **Grid blob:** packed codec (`A1 B2 C3 D4`, big-endian raw size, zlib) around FUN_14307ee80:
    - u8 version 4, u64, bool false, `u32 0` (a u16 list);
    - then the 40³ owner indices as 64 chunks of 10³ (chunk = z/10·16 + x/10·4 + y/10): an 8-byte chunk mask, then
      a 125-byte voxel mask per present chunk (bit = dz·100 + dx·10 + dy), then one byte per set voxel. Masks are
      MSB first.
    - The value is the voxel's index into the owner list. Voxel index = y + 40·(x + 40·z), the same as the edit
      mask.
  - The client lists builders (FUN_143603e50: OwnerName, OwnerCharacterGuid, OwnedVoxelCount, OwnedVoxelPct,
    OwnerColor) and highlights each one's voxels. Server: `Game/Terrain/VoxelOwnership.cs`. Every edit's solid
    voxels become the builder's; air, heals, digs and site removal clear them.
- **WorldEditing 0x010C (u32 sub, relayed):** the client sends its build cursor whenever it changes and draws other
  players' cursors from the same packets (ClientWorldEditingEffectManager FUN_142fe3ae0, one effect per guid; it
  does not skip its own guid, so the server must not echo). Wire: `u16 0x010C, u32 sub, u32 id`, then:
  0 Clear `{u8 selection-only, u64 guid}` (0 removes everything for the guid), 1 Box `{vec4 min, vec4 max, string,
  u64 guid}`, 2 Sphere `{vec3, f32 radius, string, u64 guid}`, 3 Shape `{4 × vec4 transform, u32, string, u64 guid}`,
  4 Selection `{u32, 2 × vec4, string, u64 guid, u32}`, 5 PreviewSetup `{vec4, template records, vec4, string, u64
  guid, byte array}`, 6 PreviewUpdate `{vec4, 4 × vec4, bool, string, u64 guid}`, 7 Fill `{u32 n × vec4, string,
  u64 guid}`. The guid is the builder's character guid (BaseClient +0x32508). Server: `Game/WorldEditing.cs` relays
  to players within 160 m, newest per builder at most every 100 ms, clears for viewers leaving range or when the
  builder leaves, and drops packets whose guid is not the sender's.

### Designs (templates): TemplateBase 0xF0 (u8 sub)

- **Save:** the Designs tab "+" (needs a screenshot first) sends VoxelConstruction sub 2 with action 3; the string
  after the material map's u64 is the name, the string near the end the tags, and the block list holds the voxels
  as for a copy. Answer **3 SaveTemplateResponse** `{bool ok, TemplateBasicData}`; the client adds the design.
- **2 EnvironmentListResponse** `{u32 n × TemplateBasicData, u64}` on zone-in.
- **TemplateBasicData** (reader FUN_143058180): `u64 guid, string name, u64, u64 author guid, u64 created,
  u64 acquired, u32 on marketplace, u32 designer template, 3 × u32 size, list<{u16 material, u32 n}>,
  list<PropData>, list<0x38-byte>, list<0x68-byte>, bool default, string tags, bool counts against storage,
  u32, u32, u32, bool own design (true → "Personal", false → "Collected"), string author name, u32 value,
  bool contest winner, u32`. A designer template's shown name is the text id in the first u32 after "counts
  against storage" (0.695 record +0xe4, 0.589 +0xfc); 0 shows "Daybreak Template (Invalid Name)". The second u32
  (+0xe8) is unknown. The **third u32 (+0xec) is the exported template id**, read only for designer templates.
  Designer templates never count against storage (the client computes that flag itself).
  - **Category, 0.695** (PaletteCollectionTemplates FUN_143754540 / FUN_143755d60, lookup FUN_143755020): marketplace
    ≠ 0 → Player Studio; else designer ≠ 0 → the first `ExportedTemplateCategory` whose `ExportedTemplateCategoryMap`
    rows hold the +0xec id (1 "Starter Castle" 576947: ids 1119-1324; 3 "Collected": 148 ids such as 7-30, 599-608,
    1318-1319), else Collected; else the own-design bool picks Personal / Collected. Categories with no design get no
    tab (FUN_143755ca0). The id is the live template database id, the same number as the design item's `grantId`
    (e.g. item 3706 Castle Wall - Long → 1122). The server sends the Starter Castle piece's id, or the design item's
    grantId (research/trace/0695/spec-design-categories.md).
  - **Category, 0.589** (FUN_1436966c0 and its list builder): designer ≠ 0 → Starter Castle
    (`Template.Category.Daybreak`), whatever the id.
  - The client only shows designs it is sent (its template manager, filled by subs 2 / 3 / 0xC); the category tables
    pick the tab. `ExportedTemplates.txt` (ids 2-41, .otd names) is a separate definition manager the Designs window
    does not use.
- **0x1B ExportToItemRequest (C→S)** `{u64 guid}` from Lua `ExportToItem` (FUN_14352a680): make a "Player Design" item
  (2433) of a design (Game/DesignItems.cs: a copy of the design keyed by the new item's guid). A design bought in Player
  Studio is refused with the system line 572728 "We ran into troubles trying to export your Design!" (an exported item
  is tradeable; wiki: purchased designs can't be traded). The receiving side is **0x11 AddOwnershipResponse** (reader
  FUN_143058780).
- **4 VoxelDataRequest (C→S)** `{u64, u64 guid, bool}` when a design is picked and not cached → **5 VoxelDataResponse**
  `{u64, TemplateBasicData, block list (as saved), list<image>, bool}`. The client builds its paste clipboard from
  it; the paste request (sub 2 action 4) then carries the design guid in the u64 after the material map.
- **Screenshots:** after a save the client uploads each screenshot as MarketplaceImageBase 0x107 (wire 07 01) sub 1 (u16)
  `{u64 guid, u64 guid, string "images/templates", string "large"|"small", u32, u32 index, u32 length + JPEG}`. Tiles
  load them over HTTP from ClientConfig `MarketplaceAssets`:
  `/uploads/lm/images/templates[N]/{small|large}/{guid as 24 digits, "/" every 3}.jpg` (FUN_143ad69a0); the
  emulator serves these on port 19001 when MarketplaceAssets points there.
- **6 DeleteTemplateRequest** `{u64 guid, u8}` → **7** `{u64, u64 guid, bool ok, bool cannot delete}`.
- **0xA RequestRenameTemplate** `{u64 guid, string name}` → **0xC UpdateTemplateResponse** `{TemplateBasicData}`;
  **0xB** `{u32 error (1 not found, 2 not author, 3 immutable), u64 guid}` only on failure.

- **ToolBase 0x117 sub 2 UndoStackChange (S→C):** `{u32 undo count, u32 redo count, list<string> names}` enables
  the undo/redo buttons and fills the Tool.UndoStack list.
- **Heal tool** (VoxelEraserCursor) diffs against the client's base terrain layer; with the server's edits on
  that layer it finds nothing and sends nothing.

## 10. Claims (build sites): ClaimsBase 0xF9 (u8 sub)

S→C:

| Sub | Name | Layout |
|---|---|---|
| 1 | SendAllClaimBasicInfo | `u64, u32 n`, n × `{u64 id, u64 parent group (0 = root site), u64 owner, 3 × u32, 3 × f32 min, 3 × f32 max (local m), u32, bool, u32, u32}`. Root sites go into the client's placement-buffer index by their centre. |
| 4 | UpdateClaimsAOI | `bool full (clears all), list<ClaimRecord> adds, list<u64> removes` |
| 6 / 7 | PlaceClaimResponse / Complete | 6: `u32 result (1 ok)`. 7: `u32 result 2, record`. Refusal results pick the client's text: 3 invalid location, 4 insufficient upkeep, 6 no flag, 8 landscape full, 10 site limit, 11 attachment limit, 13 attachment must touch, 25 / 27 caves, 26 too close (buffer), 28 Pioneer, other → "Unable to place build site." |
| 8 | ClaimManagmentData | `u64 upkeep seconds left, list<{bool isGroup, u64 id, string name, bool overriding settings, u64 parent group, string category, u32, u32}>`. The last two u32s are server ids; a mismatch only appends a server name. |
| 9 | EditClaimItemData | `u64 id, bool, ClaimRecord` (applied only to the selected site) |
| 10 | UpkeepManagmentData | `u64 expiry (Unix s)`, root/attached owned, player max root/attached, max root/attached allowed, `u32 warning`, max upkeep days, 3 × u32 |
| 0x0F | SendClaimsByOwner | `u64 owner, u32 n`, gallery entries (not decoded; sent empty) |
| 0x11 | RecievePropExclusionArea | `u32, list, list` (reader FUN_142da0c00; one count was missing until the 2026-10-17 wire audit) |
| 0x30 | DeleteClaimItemReply | `u64 id, u32 result`: 2 deleted; 0x12 must be on the same landscape; 0x13 delete attachments first; 0x14 site has a game object; other → "Unable to recover build site". |

C→S:

| Sub | Name | Server action |
|---|---|---|
| 0x19 | RequestAllClaimBasicInfo | Answer with `SendAllClaimBasicInfo` (sub 1). (Replying with the full AOI list instead made the client repeat 0x19/0x27/0x35 several times a second.) |
| 0x1A / 0x1B | PlaceClaimRequest / Confirm | 0x1A is `f64 centre (global), 4 × f32 flag point, u64`. |
| 0x1C | ClaimManagmentRequest | Answer with 8 + 10. |
| 0x1E | RequestEditClaimItemData | `{u64 claim id}` → 9. |
| 0x20 | RequestUpkeepManagmentData | → 10. |
| 0x27 | RequestClaimsByOwner | → 0x0F. |
| 0x2D | SetItemBasicPermissions | `{u64 id, u32 public, u32 group, u32 guild, u8 1}` (writer FUN_1435e6760). All three kept; answered with ClaimInfoUpdate kind 5. |
| 0x2E | SetItemIndividualPermission | `{u64 id, u8 (Lua bool, always false from the shipped window), string name, u8 0, u32 mask, u8 1}` (writer FUN_1435e68c0). Mask 0x80000000 = remove (Lua DeleteAdvancedSecuritySetting); any other mask, 0 = No Touching included, is stored. Answered with ClaimInfoUpdate kind 6 + EditClaimItemData to the owner. |
| 0x2F | DeleteClaimItem | `{u64 id, bool, bool}` |
| 0x35 | RequestPropExclusionArea | → 0x11. |

ClaimRecord:
- `u64 claim id, u64 parent group (0 = a root site; non-zero files the site under that group), u64 owner guid`,
  `string name`, `u64`, 3 strings, `u32`;
- `3 × f64 min, 3 × f64 max` (global decimetres);
- `3 × u32 Default Access masks` (public, group, guild: Site Management's PermissionsPublic / PermissionsGroup /
  PermissionsGuild, 0.695 claim entry +0x2d8 / +0x2dc / +0x2e0), `list<{u64 guid, u32 mask}>` players' Special Access,
  the same for guilds, then the named list `{u64 guid, string name, u32 mask, bool}` Site Management shows;
- … (see `Claims.WriteRecord`).

Permission masks: No Touching 0, Play Around 0x003, Pillage & Plunder 0x007, Get Inspired 0x107, Let's Decorate
0x10F, Ultimate Power 0x11F (ClaimPermissionDefinitions + AccessLevelMappings; FUN_142dbd940 turns a level name into the
sum of its bits).

**Default Access** (who gets which mask). The client's rule, 0.695 FUN_143c17590 / 0.589 FUN_143b5b480: owner 0x11F;
else the player's Special Access entry (a 0 entry = No Touching, it still wins); else the guild's entry; else the group
mask if its group flag is set, the guild mask if its guild flag is set, else the public mask. Every caller in both
clients passes both flags false and an invalid guild guid (the build / prop / chest pre-check FUN_142d98cc0, the site
access shown on entry, free camera, ClientRequirementCasterOnClaim), and the shipped Site Management window only sets
the public mask (group and guild arrive as 0). The server stores and echoes all three masks but applies only the public
one and players' entries, exactly as the client does.

ClaimInfoUpdate (sub 5) kinds that carry access: 5 = the record's 3 masks (+0x2c) replace the site's Default Access; 6 =
`u64 guid` (+0x38) and the first u32 after it (+0x40) set one player's Special Access (0x80000000 removes it); 7 = the
same for a guild. After 5-7 (and 0x11) the client recomputes its access to the site it stands in (FUN_142dbecb0 case 4).

- **0x61 UpdatePropCategoryCount (S→C; 0.589 0x5F):** `{u64 site, u32 n × {u32 category name text, u32 current, u32 max}}` (0.589 reader
  FUN_142d08c00; the client keeps it per site and fills ClaimDialogs.PropLimits for the site the player is on, which the
  Build Mode widget shows as "current/max Name"). Launch limit: Chests (text 573121) 50 per site.
- The client's claim dispatcher switches on sub − 1: catalogue entries keyed by the case value are one lower than the
  sub on the wire (AOIUpdate is sub 4, "UpdateClaimBasicInfoAndAOI" case 3).

## 11. Props: PropBase 0xED (u8 sub)

| Sub | Dir | Name / layout |
|---|---|---|
| 1 | C→S | PropAccess `{u64 prop, u64, u32}`: the prop tool picked a placed prop (FUN_142f15300); the client waits (+0x159) for sub 2 |
| 2 | S→C | PropData (opens the placement / edit session; needs the entity to exist first). Also the answer to PropAccess: the data when the player may move the prop (owner, or Let's Decorate on its site), else success false with error 10 (no permission) or 13 (Play Mode) |
| 3 | C→S | Save `{6 × vec4: position, rotation, scale, old position, …}` (repeated ~2/s while the gizmo is open) |
| 4 | S→C | SaveReply `{u32 0, u64 guid}` |
| 5 | C→S | PickUp `{u64 guid, u8 recycle}`: u8 0 = the prop menu's "Send to Inventory" (Lua Prop.PickUpObject), 1 = "Recycle" (Prop.DisassembleObject, RadialMenuButtons row 64); a cancelled new placement sends one of them too (PropData flags below) |
| 6 | S→C | PickUp reply `{u64 player, bool ok, [only when not ok: u32 error, u64], u64 guid}` (0.695 reader FUN_142fa8940; written `{bool, u32, u64}` always until the 2026-10-17 wire audit, so refusals were dropped and a success named the wrong prop) (errors the client shows: 3 inventory full, 6 invalid location, 7 no-trade item, 9 vendor has stock, 10 no claim permission, 11 chest with soulbound items, 13 Play Mode, 14 loot chest paid out; 0 nothing) |
| 7 | C→S | Delete `{u64}` (after the client's "Prop.DeleteConfirmation" dialog). Pick-up and delete: the prop's owner, or Ultimate Power on its site (a non-owner's pick-up goes to their own bag, P3596); "Let's Decorate" only moves props (client text 576943), so it gets error 10 and no Delete / Send to Inventory / Recycle in the prop menu |
| 8 | S→C | Delete reply (same layout as 6) |
| 0x0C | C→S | PropProcessorStateNotification `{u32 state, u64, u32}` (informational) |
| 0x12 | C→S | PlacePropById `{u32 prop id}` |
| 0x16 | C→S | SetTintRequest `{u32 n, n × u64 prop, u64 player, u32 alias hash}` (build tray tint tab; hash of the alias string, see 0x17) |
| 0x17 | S→C | AvailableTintsReply `{u64 player, bool ok (false adds u32 error, u64), u32 groups × {u32 key, u32 id, u32 name text, u32, u32, u32 n × u32 tint id}, u32 values × {u32 key = art tint id, u32 art tint id, u32 name text, u32 sort, u32 image set, string alias "GlobalTint_Coral", u32 alias hash}}` |
| 0x18 | C→S | AvailableTintsRequest (empty; sent when the tint tab opens) |

PropData (sub 2) carries a u8 of placement flags after the name id (reader 0.695 FUN_142fa7b40 +0x88 → prop tool +0x178). Only the
placement cancel reads it, for a new placement: 0x20 = Escape puts the prop back where the session started (and sends a Save
of that), else 0x40 = Escape sends PickUp u8 1 (Recycle), else PickUp u8 0 (Send to Inventory) (0.695 FUN_142fb3b80, 0.589
FUN_142f14cb0).

A tinted prop carries the alias in its spawn (lightweight base "tint alias" string, 4.2) and is updated live with
PlayerUpdate 0x7A SetPropTint `{u64 guid, string alias}`. The colours behind an alias come from the login server's
ArtData tunnel blob (also zone packet 0x116 sub 1); a client without it shows props untinted.

## 12. Crafting: RecipeBase 0x42 (u32 sub)

| Sub | Dir | Name |
|---|---|---|
| 1 | C→S | Start `{u32 recipe, u32 count, list<u64 item>}` |
| 2 | S→C | StartResponse `{bool, string}` |
| 5 | S→C | CompleteResponse `{bool, u32, string}` |
| 7 | S→C | CraftWindowEnable `{u32 result, u32 station, u64}` (0 opens the window) |
| 0x18 | C→S | SalvagingStart `{u32 recipe, u32 n × {u64 item, string, string, u32 icon, u32}}` |
| 0x19 | C→S | SalvagingAddItem `{u32 recipe, u32 slot, u64 item}` (Lua `SalvagingStationAddItem`) |
| 0x1A | S→C | SalvagingAddItemResponse `{bool ok, u32 recipe, u32 slot, u64 item}`; the slot only fills on ok |
| 0x1B | S→C | DefaultLearnedRecipes |
| 0x1C | C→S | ComponentImageSetIdsRequest (empty) |
| 0x1D | S→C | ComponentImageSetIdsResponse `{list<u32>}` |
| 0x1E | S→C | PlayerRecipeDataUpdate |
| 0x1F | S→C | RecipeHideRecipeResponse `{u32 recipe}`: adds the recipe to the hidden set (reader FUN_142f30050) |
| 0x20 | S→C | RecipeUnhideRecipeResponse `{u32 recipe}`: takes it out again (reader FUN_142f307f0) |
| 0x23 | C→S | PlayerOpenCraftingWindow `{u32 station}` |

Recipes come from the SendSelf recipe records. A record with an empty output-category list gets no row in the
window.

- **Hidden recipes** (OPEN-LEADS L43; dispatcher FUN_142f39870): the client keeps a set of hidden recipe ids (recipe
  manager +0x20, player +0x7aa00). SendSelf replaces it (sendself.md), sub 0x1F / 0x20 change it, and an open window
  rebuilds itself. The window list (FUN_143611630) leaves hidden recipes out (not greyed) and the client's own start
  paths (`Recipe.StartRecipe`, `Recipe.RefiningStationAddProcess`, through FUN_142f37f30) refuse them. The client loads
  Recipes.txt `VISIBILITY_REQUIREMENT_ID` but never reads it, so the server decides: it hides the Lumen Station unlocks
  a character already owns (`RecipeBook.HiddenUnlocks`: a "Recipe:" item whose recipes / prop are all known, a storage
  expansion at its most) and updates the set when a recipe item, prop or expansion is used. The rule is ours; a hidden
  recipe asked for anyway is still refused by the server.
- **Recipe record** (reader FUN_142c2e640; column names from the window's row builder FUN_14360e950), in wire order:
  `u32 RecipeId, u32 RecipeType, u32 SortOrder (record +0x00; the server sends SORT_ORDER), u32 output item, u32 count,
  u32 name text, u32 description text, u32 IconId, u32 IconTintId (IconData {+0x28, +0x2c}, FUN_14360c330), f32
  CastingTime (CASTING_TIME; FuelNeeded = station queue × this), list<component>, bool AllowCatalyst (ALLOW_CATALYST),
  u32 Unused38 (+0x38, no reader), u32 StackSize (STACK_SIZE; QueueSlots = station queue / this), u32
  TrackerAchievementId (TRACKER_ACHIEVEMENT_ID: the window's IsTracking column is "the player's tracked goals hold
  it"; the Watch button is enabled when it is not 0; the server sends it only when the goal exists), u32 CooldownTotal (COOLDOWN_TIME, seconds), list<u32 category>, u32 MaxQueue (MAX_QUEUE: 1 for most Lumen
  Station unlocks, 3 / 3 / 7 for the storage expansions)`. Every one of the 2,363 live records matches Recipes.txt for
  RecipeType, SortOrder, CastingTime, StackSize and MaxQueue. The server fills them all from Recipes.txt (AllowCatalyst,
  TrackerAchievementId and MaxQueue since 2026-10-18, CastingTime / StackSize / CooldownTotal since the SendSelf naming
  pass the same day; before that they went out as 0, so QueueSlots divided by zero and FuelNeeded was 0).
  Component (FUN_142c2e4e0; window row FUN_14360e6e0): `u32 index, u32 ComponentType (COMPONENT_TYPE), u32 item, u32
  count, u32 name text, u32 IconId, u32 IconTintId, u32 DescriptionId (DESCRIPTION_ID, no client reader)`.
  Cooldowns (the SendSelf block before the hidden list; RecipeBase 0x1E PlayerRecipeDataUpdate changes one):
  `Crafting::PlayerRecipeData {u32 recipe key, u32 recipe, u64 cooldown start (server seconds)}`; CooldownRemaining =
  CooldownTotal - (now - start) (FUN_143613b30).

- **Components:** RecipeComponents.txt rows naming the recipe, plus rows listed for it in
  RecipeRecipeComponentMaps.txt (those rows have RECIPE_ID 0; every Lumen Station price is given this way).
  COMPONENT_TYPE 1 = an item, 4 = any item of that item category, 5 = any item whose category is in that
  crafting category group (CraftingCategoryGroupMapDefinitions.txt), 6/7 accepted by the client as is
  (client check FUN_142e9d0e0). Types 4-7 only occur in salvaging recipes.
- **Stations:** CraftingStations.txt TYPE 1 generic, 5 salvaging (41, the Magical Monument: equipment → Lumens),
  6 repair (56/57, priced in Lumens per wear point; wear is the item record's +0x148 float, saved with the item and
  kept through trades, mail and chests, Game/Durability.cs). Station 55 is the Lumen Station
  (prop 2565, whose name text clashes with the salvager's).

## 12b. Chests: ChestBase 0xFF (u32 sub)

Client dispatcher FUN_142dad120; all readers re-read the u16 opcode and u32 sub from the packet start.

| Sub | Dir | Layout / notes |
|---|---|---|
| 1 EnableWindow | S→C | `{u64 chest}`. The client records the chest and sends 3 (what a click on a chest leads to). |
| 2 StopObservingChest | C→S | The window closed. |
| 3 InventoryListRequest | C→S | `{u64 chest}` (Lua `OpenChest`). |
| 4 InventoryListResponse | S→C | `{u32 result (1 ok; 4 → "Chest.ClaimAccessDenied"), u64 owner, u64 chest, u64 inventory, u32 title text id, u32 slots, bool unsolicited, u32 len, items}`. Items = `u32 n`, per item `bool definition follows` + item record. Opens `DisplayChestWindow`. |
| 5 AddItem | S→C | `{u64 chest, u64 inventory, u32 len, bool + item record}` |
| 6 DeleteItem | S→C | `{u64 chest, u64 inventory, u64 item}` |
| 7 UpdateItem | S→C | `{u64 chest, u64 inventory, u64 item, u32 count, u32, u32, u64, u32 slot}` |

- The client keys its chest view by the inventory guid; the server uses the chest prop's guid for both.
- Chest items have owner type 3 (`OwnerType`), `InventoryId` = `ChestOwnerGuid` = the chest, `ChestSlot` = slot. The
  client refuses an item whose stack it can't place ("HandleAddItem--Failed to add item", Logs/ClientItemsError.log).
- Moving items: InventoryBase TransferItem with owner type 3 (+ u64 owner) on the chest side. Errors the client
  explains: 0x16/0x17 access denied, 0x20/0x21 no-trade item, 0x12 fraud timer, 0x25 chest with items into a chest.
- Sizes come from the server (the client's Chests.txt has sizes 10-35 by id but nothing links props to them).

## 13. Social

- **Chat (0x13 sub 1):**
  - Layout: `u16, u16 channel, u64 from, u64 to`, two name blocks
    `{u32, u32, u32, string first, string last}`, `string message`, `vec4 position`, `u64`, `u32 × 3`, `u8 × 2`
    (38 bytes after the message; 0.695 writer FUN_1433562c0, reader FUN_143356a90), then on channel 9 a `u16` public
    channel id and on channel 10 the room name string.
  - Public channel ids: the client names exactly one, 0 = "General" (FUN_143b67f50). It registers General itself at
    every login (numbered slot 1, so `/1` talks in it) and shows channel 9 in its default chat tab.
  - The first u16 is a kind: 0 a message; 2 "User '[target]' not found." and 3 "User '[target]' is offline." (both
    about the second name block) — FUN_1432b7bc0 case 1.
  - Channels (the client's format table FUN_143aaf8f0): 0 say (relayed within 100 m), 1 whisper / tell ("[X]
    whispers:", only to the named player; the receiver's client stores the sender for /reply), 2 whisper echo, 5 guild,
    8 party, 10 player chat room (the room name trails the packet).
  - **TellEcho (sub 5, S→C):** `{u64 target, name block, string message, bool}` (FUN_1432aa700 / FUN_1432c0f90): the
    sender's own "To [X]: …" line.
- **FriendBase 0x4D (u8 sub):** S→C 1 List, 2 Online `{string, u64, bool}`, 3 Offline `{u64}`, 6 Add, 7 Remove.
  - FriendData: `{name block, u64 guid, bool online, …}`.
  - Requests are Command 0x1E subs: 0x10 AddFriend `{string name, u64 guid}` and 0x12 RemoveFriend.
- **Command 0x1E chat subs:** 0x22 ChatChannelOn / 0x23 ChatChannelOff `{string channel}` (World channel; built only by
  the Lua natives Ui.ChannelOn / Ui.ChannelOff, which no shipped script calls; no answer). 0x15 SetChatBubbleColor
  `{u32 foreground, u32 background, u32 size, u64 guid}` both ways (C→S on the Zone channel from Lua
  Ui.SetChatColorFgBgSz, also uncalled; S→C applies the values to the character with that guid). The same three
  values are the 2nd-4th u32 of the LightweightToFullPc character blob; client defaults 0xFFFFFF, 0xD4E2F0, 1.
- **Command 0x1E map subs:** 0x18 FriendsPositionRequest (Lua GetFriendPositions, no body) → FriendBase 0x4D sub 5
  `{u32 n × {u64 guid, string name, bool hidden, f32 x, y, z unless hidden}}`. 0x24 RequestPlayerPositions `{u64 own
  guid, u8 1, u8 1}` (World channel; built only by the Lua native Ui.RequestPlayerPositions, which no shipped script
  calls) → 0x25 RequestPlayerPositionsReply `{u64, bool, bool, u32 n × {u64 guid, string name, bool, bool, bool,
  u32 × 4}}` (0.695 reader FUN_1433831b0). The client reads 0x25 and frees it without showing anything; the server
  echoes the request and lists the friends and party members on the landscape with x, y, z, 1.
- **GroupsBase 0x2B (u8 sub, then u32 result, u32):**
  - S→C Invite (sub 1, result 1) shows the popup. The invite inside is `{u64 key, u32 raid flag, inviter player,
    invitee player, u32}`, player = `{u64 guid, name block, u8}`. The client answers only if the key is non-zero,
    both guids are set and each player's last byte is 1-3 (the server sends key = inviter guid, bytes 1).
  - C→S Join (sub 2) is the popup's answer: `u32 1, u32 0, u32 response (1 accept, 2 decline), u32, invite`.
  - The client also sends **Notifications** (bytes `01 00`) sub 3 UpdateNotification `{u64 id, u32 state}`. State 4
    only means "popup entry finished" (sent after Accept, Decline or Remove), so it is not an answer.
  - S→C: AddGroup 0x11, AddMember 0x12, RemoveMember 0x16, RemoveGroup 0x15, Kick 5, Disband 6, SetOwner 8.
  - Invite (sub 1) `{u8 sub, u32 type, u32 code, invite}`: type 1 = an invitation (the popup), 4 = remove, anything else
    = a result line for the inviter with the client's own text for `code` (FUN_143b5f4a0: 0 invited, 5 party full, 9 not
    found, 13 already invited, 14 already in a party, 16 declined; [*source*] / [*target*] from the invite's names).
  - RemoveInvitation (sub 0x17) `{u8 sub, u32 code, invite}` (reader FUN_142de4220: the code, then the record; no type
    field, unlike Invite; FUN_142d4ea30, texts FUN_143b5f570): 3 "Your party invitation to [target] has expired.", 4
    "… from [source] has expired." (also drops the invitee's popup when the key matches). Until the 2026-10-17 wire
    audit the server wrote a type too, which shifted the record's key: expired popups were never cleared.
  - Join result (S→C sub 2) `{u8 2, u32 2 (result), u32 code, u32 1 on success else 0, invite}` (reader
    FUN_142de3e50: three u32, then the record; one u32 more until the 2026-10-17 wire audit garbled the join lines);
    codes from FUN_143b5f4d0's table (0 success … 13 inviter group full).
  - The hidden `/party` sub-commands (`Groups.HandleLookingForMore`): 3 AutoGroup → `{u8 3, u32 3, u32 code, u32 0,
    u32 0}` (3 already in a party, 5 no available party; 4 would leave the client wait-listed); 7 SetGroupFlags
    `{u32 1, u32 0, u8 flags}` and 9 SetGroupDescription `{u32 1, u32 0, string}` → mode 2 `{u32 2, u32 0, value}` to
    the members (stored in the client's group record) and mode 3 `{u32 3, u32 code, …}` (a system message: 0 success,
    2 not in a party, 3 not the leader) to the sender; 0xE GetGroupInfo and 0x10 JoinLookingForMore `{u32 1, u32 0,
    u32 group}` → `{u32 0, u32 code}` (system messages); 0xD FindLookingForMoreGroup gets nothing (its reply is dropped
    by the client).
- **CombatBase 0x24 (C→S, u16 sub):** 3 AutoAttackOff `{}` (the target died, or an interaction auto-select), 2
  AutoAttackTarget / 4 SingleAttackTarget `{u64 target}` (/attack, /hit): accepted without a reply.
- **GuildBase 0x83 (u16 sub):**
  - C→S: 1 Create, 4 Invite, 5 InviteReply `{u64, u8}`, 0x0B Motd, 0x0D Quit, 0x0E UpdateRank (the whole edited
    rank `{u32 level, string name, u32, string, u32, u32 n × permission, u32, u32, u32 n × {…}, string, u32 rank id
    (0 = new)}` + `u32 action` 1 set / 2 remove), 0x0F DescriptionUpdate `{u64 guild, 3 strings}` (text in the
    non-empty one), 0x11 UpdateAppDataField `{u64 guild, string GuildGameData XML}` (GuildKeywords = the tag).
  - Guild core strings: Description is the 6th string after the name (+0x108); the string after the leader id is the
    app data XML (+0x168). Re-sending GuildData resets the client's rank definitions: send the ranks after it.
  - S→C: 0x13 GuildData (member id = guid as a decimal *string*), 0x0E UpdateRank (permissions by name),
    0x15 AddMember, 0x16 RemoveMember `{u64 guild, u64 member, string name}` (reader FUN_142ee5650, consumed exactly: a
    member-id string after it made the client drop the packet, so members who left stayed in rosters until the
    2026-10-17 wire audit), 0x18 Online, 2 Disband.
- **MailBase 0xF8 (u32 sub; 0.695 numbers, §1.6):**

| Sub | Dir | Layout |
|---|---|---|
| 7 | C→S | InboxListRequest `{u16 flags, u64 mailbox guid}` (the Open Inbox button sends guid 0) |
| 8 | S→C | InboxListResponse `{u32 list type, u64 mailbox guid, u16 flags, u32 n, n × 0x1D8-byte MailData}`. Flags bit 0 = request complete: it clears the client's pending flag. The client then calls Lua `OpenEmailInbox`. |
| 0x14 | S→C | MailEnableWindow `{u64 mailbox}`: the client requests the list and opens the inbox. A mailbox would send this. **Don't send it at login:** the inbox opens unseen and the button stops working. |

## 14. Other systems

- **TemplateBase 0xF0 (designs, u8 sub):**
  - 2 EnvironmentListResponse `{u32 n, n × 0x130-byte TemplateBasicData, u64}`. Send it (empty) at zone-in, or
    the Designs window shows a placeholder row and "0/0" storage.
  - Others: 3 save, 7 delete, 0x0B rename, 0x0C update, 0x11 ownership.
- **Preferences 0x03 (both ways, `Game/Preferences.cs`):** `{u32 kind = 1, u32 len, char[len] xml}` (no terminator;
  client writer FUN_142c13420, reader FUN_142c4e700). The document is the client's preference store: `<Root
  Version="1.0"><World/><Zone/><Client><UI><Chat><ChatWindows IsList="1">` one `<ChatWindow>` per chat window (Top,
  Left, Width, Height) `</ChatWindows></Chat><ActionBar AreActionBarsLocked/></UI></Client></Root>`.
  - S→C BasePreferencePacket, once per login at the first ClientIsReady: the stored document, else live's default
    (the 142-character document above with an empty ChatWindows list and AreActionBarsLocked="0"). The client creates
    its store on this packet, loads kind 1 and runs `ChatManager:RestoreSavedChatWindows` (moves and sizes the main chat
    window). It must be sent even with nothing stored: without the store the client never sends its preferences, and
    without the restore `ChatManager:SaveChatWindows` saves nothing. Character select resets the restore flag; a
    re-zone does not, so it is not sent again then.
  - C→S PreferencePacketClientXml: 30 s after Lua Chat.SaveChatTabs (1 s for an "insta" save), which ChatManager calls
    when a chat window is added or removed or a tab is registered or removed; and at Ui.Logout (FUN_142d086a0 sends the
    store, then deletes it). Moving or resizing a window alone is not saved until the next such call (client
    behaviour). Stored per character in `character_preference` (kind 1, at most 16 KB, must start with `<Root`),
    overwritten; no reply.
- **Bag filters, zone 0xDD (u8 sub; `Game/InventoryFilters.cs`):** the Inventory window's named filters, kept per
  character in `character_inventory_filter` and sent back at login.
  - C→S sub 4 PlayerFilterUpdatePacket `{u32 FilterId, string FilterName, string FilterString, u32 op}` (writer
    FUN_142fc3d20, Zone channel). Op 0 = add or replace the filter with that id (Lua `ClientInventory.AddFilter(id,
    name, filterString)`, FUN_142efd5e0: id 0 from Lua takes the client's NextFilterId; both strings must be non-empty,
    else an existing filter is removed instead), 1 = remove (`RemoveFilter(id)`, FUN_142efd330, also carries the
    removed filter's strings), 2 = remove all (`ResetFilters()`, FUN_142efd4f0, id 0). The filter string is a
    comma-separated list of item Type values (Inventory.swf `ItemContainerBase.filterData`). No reply: the client's
    0xDD reader (FUN_142fcf8b0) handles only subs 1 and 3. No shipped 0.695 script or window calls the three natives.
  - S→C at login: SendSelfToClient InventoryFilterManager `{list {u32 key = id, u32 FilterId, string FilterName,
    string FilterString}, u32 NextFilterId}` (reader FUN_142c3d470; sendself.md). NextFilterId is one past the highest
    stored id (1 with none, the live value): the client does not check a new id against its list.
  - Server limits (ours, the client has none but its 8 KB send buffer): 64 filters, name ≤ 255 and filter string ≤
    2048 UTF-8 bytes; id 0, empty strings with op 0 and unknown ops are dropped. Writes are coalesced per character
    (the whole set in one transaction).
  - The other C→S subs of the family: 1 SetSpeechPack `{u32}`, 2 GetSpeechPackList `{}` (not handled yet).
- **UiBase 0x32 sub 0x17 (C→S):** UIEventTriggered `{u32 event id}` when a window opens (0x1A inventory, 0x18
  journal, 0x1C inbox, …). No reply.
- **InGamePurchase:** 0x0B WalletInfoRequest → 0x0C `{u32 1, u8, u32, u32, string, string, u8}`.
  - 5 StoreBundleBase (S→C, `Game/StoreBundles.cs`; handler FUN_143258f50 case 5 → FUN_143257d60): `{u32 type, u32
    store id, …}`. Type 1 (PacketInGamePurchaseStoreBundles) creates or refills the store: `{u32, u32, u32, string,
    string, u32 n, n × {u32 bundle id, bundle}}` (FUN_14322c6f0, FUN_1432330d0); the packet must be consumed exactly.
    Type 2 adds one bundle, but only to a store that already exists; the client looks products up in store 1
    (DAT_14526873c = 1). Bundle (AppStoreBundleDefinition, FUN_14322cae0 after FUN_14322fa80 and FUN_14322d330):
    `{u32 id, u32 name text, u32 description text, u32, string image set id (decimal), string tint, bool tintable,
    string, u32 currency id, u32 price, u32 alt currency id, u32 alt price, u32, u64, u64, u32, bool, u32 n × item
    {u32 quantity, u32, u64 item id, u32 type (1 = item), string, string}, u32 n × {u32, u32}, u32 store id, u32
    category, bool on sale, u32 × 8 (terms text, max per order, …, discount type, discount value, …), bool × 3 (…, new,
    view only), u32 × 6 (sale price 2nd, item of the day 5th, member-only item of the day 6th), string, u32 camera, u32
    sale schedule, u32, u32 member sale price, u32, u64 Player Studio creator, string Player Studio name, u32 × 3, bool,
    u64, u32, bool}` (names from the client's StoreBundles columns, FUN_143703740). The price the client shows is the
    base price unless an item of the day or a scheduled sale applies (FUN_143c2c370), so the server sends each player's
    own price and resends the store when it changes; a type-1 store replaces the client's whole store. A creator id or
    name marks a Player Studio product, which the client may ignore. Every item id of every bundle goes into the
    client's item → bundle map, which the Crimson Parlor reads (docs/showcase.md "The client's store products").
- **PacketClientInGamePurchase 0xD0 (C→S, no sub, wire `D0 00`):** `{u32 Lua argument 1, u32 Lua argument 3, u32 0}`
  from the native InGamePurchaseStoreScreen.PurchaseBundleWithInGameCurrency (0.695 FUN_1435e9400, writer FUN_1435ebe10,
  Zone channel). Answered with **PlayerUpdate 0x4B cPlayerInGamePurchaseResult** `{u32, u32, u32}` (reader
  FUN_142c4b300 → FUN_142c25490, read and dropped by the client): the request's two fields and a result code (1, 3, 4, 7
  as the client's CanPurchaseStoreBundle FUN_143248df0; 0 other) [I meanings]. `Game/StoreBundles.HandlePurchase`.
- **TeleportBase 0xFC (u8 sub):** 2 RequestLocations → 3 Locations `{u32 n, n × world location, u32 k, k × entry}`
  (reader FUN_1430c4150; opens the Landscapes window), 4 Request, 8 IntraZone, 10 Managed,
  0x0B RandomUnderground.
  - World location: `{u32 world id, u32 name text, u32 capacity %, u32 n, n × zone}`. The world id must be the
    client's current world (the one SendZoneDetails gave it, 2 here) or the window stays empty.
  - Zone: 7 × u32 `{zone id, name text, biome type (0 = shown under every filter), site count, capacity (f32 bits),
    tier, image id}`.
  - 4 TeleportRequest (C→S) `{u32 world, u32 zone}`: the server re-zones the player (`Game/Travel.cs`). The client
    keeps world and zone in its teleport manager (+0x30 / +0x34) for subs 6 / 7.
  - Travel queue (`Game/TravelQueue.cs`, setting `LandscapeCapacity`): a full landscape answers sub 4 with zone packet
    **LoginQueueStatus 0xE0** `{u32 place (0-based), u32 members in queue, u32 non-members in queue, u32 member s per
    place, u32 non-member s per place, u8 is member, u32 × 4 unused}` (handler FUN_143171ae0 → queue data source in
    state 2: the "This location is currently full" dialog). When a place frees, **5 PostQueueNotify** (S→C) `{u32,
    u32 text, u32, u32 text2}` (handler FUN_1431726c0 uses text2 if non-zero, else text) adds a Game Action Queue
    "teleport to zone" item showing that text. Its Accept sends **6 TeleportToQueuedLocation** `{u32 world, u32 zone}`,
    Decline (or the dialog's Cancel) **7 TeleportCancelQueue** `{same}`; both close the dialog. Nothing else closes it.
  - 8 TeleportIntraZoneRequest (C→S, FUN_1430c8260) `{f32 x, y, z, w, u64 target, u8}`: the site game editor's Go To;
    served only to a player editing a game, to a point on that game's site.
- **ZoneSafeTeleportRequest 0x7C (C→S, no body):** Lua EvacLocalPlayerToSafepoint (player outside the zone's bounds)
  and TeleportToSafePoint. The server moves the player to the nearest respawn site (at most every 10 s).
- **Teleport requests, Command 0x1E (C→S, u16 sub; all built by FUN_1433c80a0, `Game/TeleportRequests.cs`):**
  0x51 TeleportToLocation `{vec4 position}`, 0x52 TeleportToLocationEx `{vec4 position, vec4 heading quaternion, vec4}`
  (a new heading), 0x3F6 TeleportManagedToLocation `{vec4 position, u64 mover}` (player on a managed object). Sent by
  the Escape menu's **Evac to Safety** inside the zone (FUN_1435fdb70: on a build site, the site box's centre + its half
  diagonal + 10 m along the player's heading; elsewhere the player's own spot; y put on the ground, at most 5 m above),
  by /location x y z, /teleporttocamera and the admin map teleport. Before sending, the client sets its
  teleport-pending bit (actor +0x1457 & 8) and stops moving the player; only the ClientUpdate **TeleportToLocation**
  handler (FUN_142d59f30) clears it, so every request is answered with a TeleportToLocation (a re-zone does not clear
  it): to the point when accepted (admins anywhere; players only for an evac near their spot or off their site, once
  per 10 s; setting PlayerTeleportRequests), else to where they are.
- **Player reports (C→S, u16 base + u16 sub 1):** BugSubmission 0x55 AddBug and ClaimReportSubmission 0x56 AddReport
  `{u32, string, u32, string XML}` (header writer FUN_14306db00). The XML is the client's TinyXML document
  (`<BugSubmission Category BugDescription><PlayerData …/>` / `<ClaimReport ReportDescription>`); the client shows its
  own "submitted" message. Saved to Reports/bugs and Reports/sites (`Game/Reports.cs`).
- **Informational client packets** (no reply): ClientMetrics, FotomatBase, SetLocale, RatingBase, PlayerUpdate
  Loading, ClientPcDataBase, ClientInitializationDetails.
- **LootPinataBase 0xFB (u8 sub):**
  - 1 SpawnLoot: `u32 id, u32 definition (LootPinata.txt: 14 dirt, 19 sand, 21 stone…), vec3 position, u32 item,
    u32, u32 count, f32, u64 owner, string, u32 kind, u32 ×3`. Kind 0 keeps the position; kind 3 zeroes it. Before
    scooping a pinata up, the client checks that the bag or the resource (Collections) container can take item × count
    (FUN_143079860), and otherwise shows "Your inventory is full!".
  - 5 PickupLoot (C→S) → 3 PickupLootResponse `{u32 id, u64 player, u8}`. Chunks fly to their owner.
  - 2 RemoveLoot (S→C) `{u32 id, u64 player}`: removes it. The client shows a pinata only when its owner is the local
    player or none (0).
  - Ownerless loot: within 5 m (`LootPinata.DistanceToPickupOwnerlessLoot`) the client sends 7
    RequestClaimOwnerlessLoot `{u32 id, u64 0}` on every update; the server answers the first claimer only, with 4
    NotifyOwnerlessLootClaimed `{u32 id, u64 claimer}` to every viewer. Every copy then flies to the claimer; the
    claimer's client sends the normal PickupLoot, the others drop their copy on arrival. Losing claims get no reply
    (a 3 or 2 would delete the pinata at once). Bags last `LootPinata.DecayAge` = 120 s.
- **Placed props moved by the server** (moving platforms, `Game/Props.Platforms.cs`): PlayerUpdatePosition 0x7F for
  the prop's transient id, the same full movement body creatures get (position, quaternion, velocity). The client
  marks the prop moved (ProxiedProp +0x4F00) and re-registers its collision at most once a second. The prop must be
  spawned with control mode 2 and no owner (§4.2), or the client queues the updates (mode 0) or drops them (on the
  owner's own screen).
- **ClientUpdate TeleportToLocation (S→C)** moves the local player without zoning (reader FUN_142b83d70, handler
  FUN_142d59f30): `vec4 position, vec4 facing (+0x30), u8 (+0x40), bool wait screen (+0x41), bool snap to ground
  (+0x42), u64 teleporter guid (+0x48), u32 ms (+0x50)`.
  - +0x30 is a **forward direction**, not a quaternion: the apply path (FUN_142ec1650) masks w off and normalises
    x/y/z as the actor's facing. Send a flat unit vector `(x, 0, z, 0)`, never zero: the identity quaternion sent until
    2026-10-18 is the zero vector there, which made the actor's rotation singular and its visibility boxes NaN, the
    likely cause of the plain-teleport freeze (docs/terrain.md "Long teleports"). ClientBeginZoning's rotation, by
    contrast, is a real quaternion.
  - +0x40 is the client's movement version (FUN_142d59f30 → game +0x32560, the byte ClientBeginZoning +0x98 and
    ClientUpdate MovementVersion set): the client's movement bodies carry it (§4.3), so it is sent as 1 like the login
    MovementVersion. 0, sent until 2026-10-18, made other clients drop the mover's non-full updates until its next
    full one.
  - +0x41 makes the client wait in its "Waiting for Teleport..." state, but that state checks world-ready at the
    character's stale stored position, so it does not protect a long move (docs/terrain.md "Long teleports").
  - +0x42: the client puts the player on the terrain at x/z (sent off on the Spire's deck, for cavern points and for
    pad hops).
  - +0x48 / +0x50 = "ignore teleporter <guid> for <ms>": when the guid is valid and the count above 0, the client puts
    guid → now + ms into the actor's timed set (FUN_143325060, +0x4dc0). The Translocation Pad's enter check
    (FUN_1436db8b0) and its state machine (FUN_1436db650 case 1) do not fire while the pad is in it. The key is the
    pad's **prop guid** (the teleporter area keeps the spawn guid in its data +0x10, FUN_1431b6070), not its area guid.
    Pad hops send the arrival pad and 2500 ms (ours), so the player isn't sent straight back.
  - The handler also clears the teleport-pending bit set by the teleport requests above.
- **Claims 0xF9, site inventory and the lost-connection notice:**
  - 0x60 DebugAddItemClaim (C→S, `/item claimAddItem`): `{u32 item, u32 tint, u32 count, u64 target, u64 claim, u32,
    u32, u8}`.
  - 0x23 ClaimRequestMoveItemToPlayer (C→S, `/item claimItemToInventory`): `{u64 claim, u64 player, u64 item guid}`.
  - 0x0D SendInventoryDetails (S→C): `{u64 claim, u64, u32 n, n × {u64 item guid, u32, u32, u32, u32, u32, bool, u32,
    u64, u32, u32}}`; the client prints the item guids to its console.
  - 0x12 LostClaimServerConnection (S→C, no body): the client shows text 562155 "Unable to process the request.
    Connection to the Build SiteServer is unavailable." as a system message and a chat line.
- **HudManagerBase 0xDC (u8 sub), right-click menus:** the client asks the server which buttons to show.
  - 1 RadialMenuRequest (C→S) `{u64 target guid, u32 request type}`. Types: 1 prop, 2 friend (Friends window),
    3 player in the world, 4 other social row, 5 party member, 6 guild member, 7 chat tab.
  - 2 RadialMenuResponse (S→C) `{u64 target guid (must match), u32 menu type, u32 n, n × {u32 sort order,
    u32 button id, u8 selectable}, u32 request type}`. Button ids are rows of the client's
    `Resources/RadialMenuButtons.txt`, which gives each button's label and Lua call (e.g. 23 `Ui.TeleportToFriend`).
  - Closing the prop menu sends nothing (0.695): the X runs Lua RadialMenu.ExitScriptingState (FUN_1435fd800 →
    FUN_14343a640, a scripting-controller state change) and the menu's reset (FUN_14372d730) has no packet. Its title
    can't be set by the server: the context-menu indicator takes its labels from the scripting controller's state
    (FUN_143446870 → FUN_142fed440), a no-op in EditingStateIdle (FUN_142fed470).
- **Travel abilities** (`StringHashToValue.txt`): 185 Teleport.HubAbilityId (Spire), 186 OwnClaimAbilityId (own
  site), 187 FriendAbilityId (friend; the cast's target is the friend's guid). The client never requests caster info
  for 187 before "Teleport to Friend" casts it, so the server pushes 185-187 at zone-in. Their bodies are the 1098
  capture's (u32 / f32 +0x70 = 60 s cooldown → record +0x14c, +0xa0 = the ability id → the cooldown timer key +0x7c,
  bool +0xa4 = 1 → +0x80 "start the timer on CastSuccess", FUN_142e187a0 / FUN_142e18340); 187's +0x70 is replaced by
  `FriendTeleportCooldownSeconds` (0 by default), which Ui.TeleportToFriend (FUN_1435c91f0) checks itself.
- **EncounterBase 0x2C:** death window: 0x7D shows "You have died.", 0x7E hides it. The whole family (duels, both clients' dispatch, the 0.695 jump table) is in [encounters.md](encounters.md).
- **ActionManager 0x9A:**
  - 1 ClearSlot.
  - 2 (right-click a bag tool) and 5 UpdateSlot share the layout `{u32 bar, u32 slot, u32 type (0 empty,
    1 item definition, 2 item guid), u64 value}`.
  - Hotbars are sized only by the SendSelf hotbar list; a bar sent with 0 slots rejects every update.
- **Server commands (CommandBase 0x1E):**
  - 0x44 (S→C) dynamic command list `{u32 n, n × string name, u32 source (1 = zone)}`: each becomes a "/name" the
    client accepts (DynamicCommands.log); any other unknown "/name" gets "Command not supported".
  - 0x43 (C→S) ClientCommandPacket `{string name, u32 n × string argument}` when one is typed.
  - Sources: 1 = zone (typed → channel 0), 2 = world (channel 1), 3 = the gateway (gateway opcode 9); any other source
    registers the name but typing it sends nothing (FUN_1433c3d80).
  - A name is kept by the first list or AddWorldCommand / AddZoneCommand (0x45 / 0x46) that names it, for the whole
    session; later duplicates are ignored ("Duplicate command from server", FUN_1433bad50). So the zone list
    (Emotes), the world / zone commands added one by one (DynamicCommands) and the gateway's list
    (GatewaySession.GatewayCommands) must not share a name: the world server checks this at start-up and logs an error
    for each duplicate (DynamicCommands.CheckUniqueNames).
- **Emotes:** played with Abilities 0x27 sub 0x12 PlayAnim on the player's guid. AnimationSelection.txt
  "EmoteSingle" rows select a clip through Emote_Index (AnimationSelectionVariables.txt); "EmoteLoop" rows repeat
  until selection 253; stance rows (Stance_Index) persist until 223. A selection that arrives while the character
  is moving is ignored.
- **HeroicMovement 0x0105 (wire 05 01, u8 sub):** `{u8 sub, u64 entity, body}`, the entity being the sender's own
  guid (0.695 header reader FUN_142c1fd70 / writer FUN_142e5eaf0): 1 FireGrapplingHook `{f32 x4, f32 x4, u32, u32,
  bool}`, 2 SetHeroicMovement `{bool}`, 3 SwimmingUpdate `{u8 flags, f32 depth, [flags & 0x40: f32, f32]}` (writer
  FUN_14358bc70, sent when the swim state changes and once around zone-in with all zeros; 0x80 = dive), 4
  TerminateGrappleHookEffects `{}`, 5 HeroicMovement `{u32}` (a grapple pull arrived; no client reads it). The pull is
  client physics (stats 18-21); no sub is answered. The server (Game/HeroicMovement.cs) relays 1-4 to players within
  200 m with the entity set to the sender (the client ignores ones about itself) and not to viewers mid re-zone,
  SwimmingUpdates at most every 250 ms per swimmer and only when changed after rounding (depth to 1/64 of the
  character's height, the two heights to 0.05 m; a non-zero value never rounds to 0). The client sends on any exact
  float change with no rate limit of its own (FUN_14358c410 / FUN_14358d760), so the rounding is what drops the
  near-identical states (bandwidth audit 2026-10-18 R3). The receiver keeps the last state per character and nothing repeats it, so a
  player who meets a swimmer later (zone-in, re-zone, coming into view: `PlayerManager.Introduce`) is sent that
  swimmer's last SwimmingUpdate once, right after the character's AddLightweightPc; under the surface (0x40) the dive
  flag 0x80 is set on that copy so the receiver switches to under water (OPEN-LEADS L167). Between b653b73 (16 Oct
  2026, the base got its wire-form enum value) and
  OPEN-LEADS L166 the packets reached no handler ("unreadable") and nothing was relayed.
- **ToolBase 0x0117 sub 1 ToolPacketActivate (C→S):** build tool switched: `{u8 by item, u32 item id, u32 ToolTypes
  id}` (writer FUN_143452550 in 0.695); no reply. The server remembers the tool per player for the edit effect other
  players see (docs/terrain.md "Tool switch", Game/Building.Tools.cs).
- **Mail (MailBase 0xF8, u32 sub; 0.695 numbers, §1.6):**
  - C→S: 1 Compose `{u64, u32 n × PlayerName to, u32 m × PlayerName, string subject, string body, u32 k ×
    attachment}`, 7 InboxListRequest, 9 ReadMessage `{u64 id}`, 0xB Delete `{u64 id}`, 0x10 ConsumeAttachment
    `{u64 mail, u64 attachment (0 = all), u8}`, 0x15 AttachItemToCompose `{u32 type 1, u64, u32 bag slot, u32 count,
    u32 container, bool}`.
  - S→C: 5 ComposeResponse `{u32 result (+ PlayerName for 1 / 0xD)}`, 8 InboxListResponse `{u32 0, u64 owner,
    u16 flags (bit 0 = complete), u32 n × MailData}`, 0xA ReadMessageResponse `{u64 id, u32 result, u32 kind}` + on
    success `{u64 sender, PlayerName, string subject, string body, u64 sent, u32, u32 n × attachment, u32 n + n
    bytes}`, 0xC DeleteResponse `{u64 id, u32 result}`, 0xF NewMail `{u16 count}` (the 0.695 client needs the count:
    without it the packet was dropped, so no new-mail prompt and no inbox refresh; L67), 0x11 ConsumeAttachmentResponse
    `{u64 mail, u64 attachment, u32 result}` (one per attachment), 0x14 EnableWindow `{u64}`, 0x16
    AttachItemResponse `{u32 result, request echoed}`.
  - PlayerName `{u32, u32, u32, string first, string last}`. MailData `{u64 id, u64, PlayerName, subject, body,
    u64 sent (Unix s), u32 read, u16 attachments, u32 kind (0 player)}`.
  - Attachment `{u64 id, u32 type, …}`: 1 `{u64 item, u32 count, u32, u32}`, 2 = the client item record
    (FUN_142b87110: the inventory record without the 0.695 trailing byte), 3 currency `{u32, u32}`, 4 `u64 list`.
  - Result codes: client string table at 0x14521a980 (0 Success, 1 unable to find user, 2 attachment to self,
    5 attachment invalid, 7 item no longer exists, 0xB attachments to several people, 0xC too many attachments,
    0xD inbox full, 0xE sent too many, 0x14 message not found, 0x1A attachment not found, 0x1C inventory full…).
- **Projectiles 0xEE (wire `EE 00`, i16 sub; `Game/Projectiles.cs`, OPEN-LEADS L138 / L156):** the client draws a shot
  (model, trail) only when the server sends it: this family's handler (FUN_14318fbe0) is the client's only reader of
  ProjectileDefinitions.txt. The shot is visual; the client sends nothing back and the server keeps the hit timing
  (distance / the definition's SPEED).
  - 1 Create (reader FUN_14318ce10): `u32 projectile id, u32 ProjectileDefinitions id, vec3 origin, vec3 direction (a
    unit vector is scaled by the definition's speed; any other length is the velocity), u64 shooter (when the
    definition names a bone, the shot starts at that bone of the shooter), bool gravity, f32 maximum range (each frame
    the shot is clamped to that distance from its start; 0 pins it at the origin), vec3 (the aim point [I]; the only
    field nothing reads), f32 homing turn rate, u64 homing target, f32 seconds for the turn rate to fade to 0, vec3
    fixed homing point, u64 homing start time`. The turn rate applies only while a homing point is set and after the
    start time.
  - 2 Update (FUN_14318d480): `u32 id, vec3, vec3` (a new course). 3 Delete (FUN_14318d320): `u32 id, f32 × 4 impact
    point (x, y, z, 1), u32, bool hit, u64 target`.
  - Positions are in the local frame. One Create and one Delete per shot, to the players who can see it.
- **The Crimson Parlor: CharacterCustomization 0x11C (u8 sub; `Game/Barbershop.cs`):**
  - 3 EquippedAttachmentRequest (C→S) `{u64 character, -1 = self}` when the Parlor opens → 4 EquippedAttachmentReply
    (S→C, reader FUN_1430a0cc0) `{u32 n × attachment}`, one per visible equipment item, the same record as the
    attachment part of EquipmentSetCharacterEquipmentSlot: `{string model, string texture alias, string tint alias,
    string decal alias, u32 tint, u32 composite effect, u32 slot, u32 0, bool}` (record reader FUN_142c185c0). The
    Parlor dresses its preview from this list; sent empty, the preview and then the character showed the bare
    creation clothes.
  - 1 PlayerSet (C→S) is the purchase: `{u32 1, u64 character, u32 n × {u32 slot, u32 option, u32 tint}, u32 skin tint,
    string, u32, u32}` and in 0.695 `u32 new base actor, u32 0` (writer FUN_1430a0530).
  - After a purchase the server sends **PlayerUpdate 0x1B CustomizationChange** `{u64 character, bool, customization
    data (slots, skin, string, race, gender), u32 n × attachment}` (0.695 reader FUN_142c27ad0) to the buyer and the
    landscape: the client stores the look for its own player and rebuilds the model with those attachments. Without it
    the buyer's client went back to the old look when the Parlor closed.
- **Other layouts corrected by the 2026-10-17 wire audit** (each server packet checked against its 0.695 reader; a body
  with bytes left over or missing is dropped whole by the client; research/trace/0695/audit-wire-*.md):
  - PlayerUpdate 0x1C PlayerTitle: `{u32 title id, u32 type, u32 name text, u64 player guid}` — the title first, the
    guid last (reader FUN_142c2a390). With the guid first, titles over other players never changed.
  - LightweightToFullPc's nested UpdateStat carries the u32 PlayerUpdate sub (`26 00 53 00 00 00`); with a one-byte
    sub the body was 3 bytes short and other players stayed placeholders. A PlayerUpdate sub is written as exactly
    the u16 base and the u32 sub, nothing after it (CustomizationChange's guid had been shifted by a stray byte).
  - PropBase 0x19 SetPropName (C→S): `{string name (up to 128), u64 prop}` — name first (client writer FUN_1435fca20).
  - PlayerUpdate 0x7C UpdatePropName (S→C): `{u64 prop, string name}` (reader FUN_142c4de60; 0.695 dispatcher case
    0x7b). Sets the entity's name (FUN_142ec5150, +0x1308), which the Prop HUD indicator floats over the prop
    (HUDIndicatorSettings 2^18^13: TargetIsProp && ShowProxyName = entity +0x1454 bit 0x40 clear, FUN_1437119c0), and
    the prop data's custom name (+0x4e80, shown in Prop Settings). An empty name is replaced by the text of the spawn's
    NameId (+0x4e98 = AddLightweightProp +0x58), 0 on our props; a single space shows no label.
  - ClientUpdate ZonePopulation: exactly four u32 (reader FUN_142c49410), all 0; meanings unknown.
  - Harvest failure (Abilities EffectEnd 0x0D with a reason, `Gathering.HarvestFail`): the resource and the last u32
    follow the reason only when the reason is not 1.
- **Read and ignored** (no reply exists or is needed): MountBase 0xA1 (u16 sub; the 0.695 client sends 3
  DismountRequest, no body, when an encounter starts; Landmark has no mounts); Abilities 0x27/0x1D AbilityLanding `{u64,
  u64}` (sent by FUN_143322830 when the character lands after an ability that set its landing flag; logged only).
- **CommerceSessionRequest 0x77 (C→S)** asks for a session ticket for the real-money payment pages (fund wallet,
  cards, redeem code). The server answers **CommerceSessionResponse 0x78** `{bool ok = false, string ""}` (reader
  FUN_142c4f100), so the client fails that request at once ("Failed to request commerce session.") instead of waiting.
  Nothing is ever paid with real money here.
- **Voice 0xB9** (u16 base, u8 sub; `Network/Handlers/VoiceHandler.cs`). Live handed the client a Vivox login and
  channels, S→C only: sub 0 VoiceLogin (four strings, reader FUN_1430cb780: account name, account password, the Vivox
  server URL, one unused [P offsets, I Vivox names]), sub 1 VoiceJoinChannel (u8 channel type, then channel URI,
  password, display name; FUN_1430cada0), sub 2 VoiceLeaveChannel (u8 channel type). Channel types (FUN_143c6dff0): 0 Echo,
  1 Proximity (the wiki's Local), 2 Faction, 3 Group, 4 GroupLeader, 5 Raid, 6 Guild, 7 Custom. The server sends none of
  them, so the client's voice never connects (as with voice switched off; nothing waits): the clean "unavailable" state
  (an empty VoiceLogin would make the client connect to an empty URL). OPEN-LEADS L181. The only C→S packet, sub 6 VoiceRequestCustomChannel
  `{string room, string password}` (a player asking for a custom room), is logged and gets the system line "Voice chat
  is not available on this server." once per session. Real voice needs a voice service (OPEN-LEADS).

### 14.0 Spire Local Travel: WarpNetwork 0x113 (opcode bytes 13 01, u32 sub)

The Spire menu's "Local Travel" (Lua `Teleport:ActivateLocalTravelMap`) switches the camera to ControllerLocalTravel
(mode 0x1E): a top-down map with the hint "Click a HUB or Portal Crystal destination". Server: `Game/WarpNetwork.cs`.

- **Request (C→S) sub 4:** `{u32 type, u32 zone, u32 world, u64 from, u64 to}`.
  - Type 1 is sent once, at the first ClientFinishedLoading; the server re-sends the network on every landscape
    change.
  - Type 2 is a warp: Teleport.TravelConfirmation or Lua WarpToNode.
- **Network (S→C) sub 2:** `{u32 world, u32 zone, u32 n layers × {u32 id, u32 n × {u64 key, u64 node, u32 type,
  u32 biome, u64 cell, f32 x, y, z (local m), u32}, u32 id}, u32}`.
  - The client files it by (world, zone) and looks it up by its own (game +0x378cc, +0x378d0).
  - Node ids are entity guids: the map picks the entity under the cursor.
  - Type 1 = hub, type 2 = portal crystal.
  - The map shows one layer at a time: the cave tier where the player stands (0 on the surface), changed with
    `Teleport.SetCurrentLayer`. While the map is open, every node of that layer gets a HUD marker.
- **TeleportBase 0xFC sub 1 ActivateTerminal (S→C):** `{bool open map, u64 node}` tells the client the player stands
  at that node.
  - Open = the map at once.
  - Otherwise the client asks for the travel locations (the Spire's Landscapes window).
- **Node last u32 (+0x24):** the node's location key (default -2), not a discovered flag. The client reads it only
  for the cost (HubCost when the from and to keys differ). The server sends the node's layer (0 = hub, the cave tier
  for a crystal).
- **Cost** (the client's confirm, FUN_143504a80; settings read by FUN_143505fd0), in Lumens (1579):
  - BaseCost;
  - + NodeCost if the from node is a crystal (type 2);
  - + WorldCost to another world, + ZoneCost to another zone;
  - + HubCost if the location keys (or zone / world) differ;
  - + NodeCost if the destination is a crystal.
  - Shipped data (StringHashToValue rows 675-680): BaseCost, HubCost and WorldCost are 0. The keys "NodeCost " and
    "ZoneCost " end in a space, so the client uses its defaults (1 and 5).
  - So: hub → crystal 1, crystal → hub 1, crystal → crystal 2, + 5 to another landscape. The server charges the same.
- **Nodes (0.589):**
  - the Spire's "Portal Spire (Hub)" prop;
  - the home continent's 81 cave Graveyard01 pieces (tile 111, tier 1 from VoxelLayerPropertyGroups), each with an
    "Underground Portal Crystal" prop (1881).

### 14.1 Client requests left unanswered on purpose (checked 2026-10-12)

The server counts every received opcode (`opcode_stats.tsv`), and every request that falls through to a handler's
default branch (`OpcodeStats.Unanswered`); a coverage script (not in this repository) compares those counts with the
client's packet classes and lists the default-branch hits as the runtime truth (research/tables/coverage.md). Some requests are only logged or dropped, by design:

| Request | When | Why there is no answer |
|---|---|---|
| InGamePurchase 0x15 CountryCodesRequest, 0x0F Station Cash price list, 0x37 CommerceRequestAccountStore (0.589 0x36) | Store start-up (`RequestStartupDataInternal`, ClientInGamePurchase.log), each login | The real-money Daybreak Cash store; the store is free and in the Showcase instead. 0x1A AccountInfo and 0x40 GetProductForReview are answered now for Player Studio (docs/commerce.md); the AccountInfo reply makes the client ask for the Station Cash price list (0x0F), which stays unanswered, so the cash store stays unavailable. 0x37 (the player's own products) is still open. |
| InGamePurchase 0x47 type 2 (crafting Point-of-Need; 0.589 0x46) | Crafting window "buy missing" | Opens a store URL in the client; no server reply known. |
| Notifications 0x01 sub 2 AddNotification | The client queued an entry itself | Informational. |
| Notifications 0x01 sub 1 ListNotifications | UI shutdown (logout / exit): the unanswered queue entries `{u32 n, n × Data}` | The server keeps invitations itself; at the next login GuildInvitations 0x83/0x14 brings a still-valid guild invitation (and the client's own popup for it) back (Guilds.Enter). |
| ToolBase 0x117 sub 1, PlayerUpdate 0x6C (ClientLoading {u8}: 1 = GameStateLoading entered, 0 = left, i.e. the loading screen dropped; FUN_1436abc90 / FUN_1436abd00), SocialShare 0x104 | Tool switch, loading screen, sharing | No reply expected. The tool switch is read (Building.Tools.cs) for the edit effect others see; the first ClientLoading 0 of a login shows the player and the others to each other (PlayerManager.LoadingScreenLeft); the rest is informational. (WorldEditing 0x10C used to be listed here; it is relayed to other players, §9.) |
| ClaimsBase 0x19 | See §10 | Answered with SendAllClaimBasicInfo (sub 1); never with the AOI list. |

## Achievements (AchievementBase 0x92, u8 sub)

Client dispatcher `FUN_142d92750`. Subs 4/6/7/8 are rejected when bytes are left over.

| Sub | Name | Body |
|---|---|---|
| 2 | Add | one achievement record |
| 3 | Initialize | `u32 n`, n x achievement record |
| 4 | Complete | `u32 achievement` |
| 5 | ObjectiveAdded | `u32 achievement`, objective record |
| 6 | ObjectiveActivated | `u32 ach, u32 obj, u32 current, u32 required, u32 progress, bool` |
| 7 | ObjectiveUpdate | `u32 ach, u32 obj, u32 current, u32 progress` |
| 8 | ObjectiveComplete | `u32 ach, u32 obj, u32 progress, u32 message, bool` |
| 0x0B | TrackerInitialize | `u32 n`, n x `u32 achievement` |
| 0x0C | TrackerUpdate | `u32 n` x {`u32 id`, achievement record} (0 from the client), `u32 n`, n x `u32 achievement`, `u32 removed` (0 tracked, 1 untracked); reader FUN_142e28e60. C→S from SetTrackingAchievement; S→C (FUN_142e33d00) adds the listed records the client lacks, then applies the change (not sent by the server) |
| 0x0E | SecretComplete | one achievement record |
| 0x0F | GlobalAnnounce | |

`progress` is the achievement's completion as float bits (achievement +0x20).

**Achievement record** (0.589 `FUN_142b7ca00` / 0.695 `FUN_142c161b0`, client class AchievementData; every field
named in `Model/Shared/Achievement.cs` with its readers): `u32 id, u8 flags` (0x10 = not active today),
`u32 name text, u32 description text, u64 started (Unix s), u64 completed (Unix s, 0 = not), u32 progress (float)`,
reward bundle, `u32 n` x {`u32 objective id`, objective}, `u32 icon, u32 icon tint` (IconData, unread),
`u32 trophy, u32 points, u32 subcategory` (AchievementSubcategories.txt), `i8` (unread), `u32` (unread),
`bool Steam achievement`, `u32 completion composite effect, u32 quality` (AchievementQuality.txt),
`u32 visibility type` (4), `u32 hint text, u32, u32, u32` (three unread), `u32 foreground image,
u32 background image, u32 sort order`, `bool can watch after completion, bool track collapsed, bool` (unread),
`u32 n` x `u32 dependency achievement`, `u32 n` x {`u32 objective id, u32 m`, m x {u32 item, u32 count,
u32 image set, u32 recipe}} (ObjectiveItemHint), `u32 n` x {`u32 objective id, u32 required achievement`}, `u32`
(unread).

**Objective** (0.589 `FUN_142b89710` / 0.695 `FUN_142c22ee0`, ObjectiveData; `Model/Shared/Objective.cs`):
`u32 id, u32 name text, u32 description text`, reward bundle, `u32 status` (0 not started, 1 active, 2 done; client
+0x2150), `u32 count` (+0x2154), `u32 total` (+0x2158), `u32 sort order` (+0x2170), `u32` (unread),
`u32 hint text` (+0xc), `4 x u32` event bar (quests only: type, label text, value, mode), `u8 flags` (0x80 = other
status icons), `u32` (unread).

**Reward bundle** (0.589 `FUN_142b95550` / 0.695 `FUN_142c2f040`, ClientRewardBundle; `Model/Shared/RewardBundle.cs`):
`bool include item data, u32 coins`, `u32 n` x {u32 currency, u32 amount}, `u32 experience`, `u32, u32, u64,
5 x u32, u64` (no reader found), `u64 recipient guid` [I], `u32 icon, u32 name text` (0xFFFFFFFF = the first
entry's), `u32 n` x {`u32 type`, entry}, `u32 bundle id` [I]. Entries (factory `FUN_143ac8420`, reader = vtable +0x78)
start with `bool, u32 icon, u32, u32 name text, u32 count, u32 item id, u32, string, u32, bool, u32, u32`; types 6, 7,
8, 10, 0x0E, 0x16, 0x19 (QuestAdd, ProfileAdd, AbilityLine, CollectionAdd, Recipe, AbilityEntry, PropRecipe) add one
u32. Type 1 (Item) adds an item definition only when `include item data` is set. The models
(`Model/Shared/Achievement.cs`, `Objective.cs`, `RewardBundle.cs`) read a capture to the last byte. The server sends the captured records, with each player's progress and guid patched in.
