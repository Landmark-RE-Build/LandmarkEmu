# Architecture leads L3-L8, caves and procedural placement (agent #8, 2026-10-15)

Client evidence only. Addresses are given as **0.695** (re/decomp_all_695) **/ 0.589** (re/decomp_all) where both were
checked; the 0.589 address comes from trace/0695/functions.tsv (`same` = identical code). "Inference" marks anything not
proven by the code itself. Scratch scripts and listings: re/out/agent_arch/ (ptrscan.py: pointers to a function in
the exe; relscan.py: rel32 call/jmp scan of .text; unresolved.py / ctx.py / ctor.py / bytemplate.py: L7; serveronly2.py
→ serveronly_695.tsv: packet ids with no client class or reader).

## Summary

| Lead | Status | One line |
|---|---|---|
| L3 page selection | **narrowed, still open** | Alt+F only shows the FPS header; the paged monitor needs flag +0x342a8, set only by FUN_142d56520 / 0.589 FUN_142cbd160, which nothing in the retail exe reaches (no call, pointer or rel32 reference in 0.695). So in retail no page is drawn and no page request is ever sent. |
| L4 page replies | **answered for retail (no reader); live layout open** | 0x1E/0x4AA PathingDataMonitorUpdateResponse and 0x1E/0x4AC EnvServerDataMonitorUpdateResponse exist only as ids: no class, no reader, no handler case; unknown admin subs fall to the module registry, whose 15 registered modules all return 0. Pages 11 / 12 / 24 read PlayClient slots that return 0. |
| L5 gateway opcode 9 | **answered** | No class exists: the client builds a bare `Gateway::ExternalPackets::BasePacket` with opcode 9 and appends the serialised ClientCommandPacket. The client never receives opcode 9 (it handles 2, 4, 5, 7, 8 only), so the gateway answers through tunnelled game packets. |
| L6 TCP login | **answered** | Nothing selects TCP: LoginApiWrapper's connect always builds the UDP API (`LoginUdp_10`); the TCP class is dead library code. New: CharacterLoginRequest carries a `GatewayId` from ClientConfig.ini. |
| L7 unresolved sends | **47 of 55 resolved** (0.695) | Mostly classes the script's regex missed (template class names, packets built by a constructor). 8 sites left, with family hints. |
| L8 back end ↔ zone | **partly answered (names)** | The client's packet-id registry names ~40 messages that no client code builds or reads, many naming two server parties (ZoneAddClaim, ConsumeAttachmentZoneRequest, OffGridPasteZoneRequest, EnvServerResponse, WorldKickRequest, LockStatusQuery, InternalConnectionBypass, GatewayProfilerRegistration...). Layouts are not in the client. |
| Caves | **answered** | Caves were generated **offline** by Daybreak's ProceduralPlacementTool (dev note in the install) into ContinentCavePlacements[N]_Surface/_Underground.cpa, shipped to the client and loaded by both sides; CaveMeshes/*.vfm are the triangle-mesh sources of the 37 .cav tiles. The client carries the generator (VoxelFarm::CaveGeneratorImpl) and the server-side CaveManager, but reaches neither at runtime. |

## L3. How retail selects a performance-monitor page

**Question.** What calls 0.589 FUN_142cbd160 (show + set page) / FUN_14317bc10 (set page) in the retail client?

**Evidence (0.695, re-traced from scratch).**
- The monitor object is DAT_145432880 (BaseClientPerformanceMonitor, created in the client init at part_0066 ~7860:
  FUN_14321cff0 / 0.589 FUN_1431725d0). Its vtable 0x14472a8c8 (re/out/rtti_695.tsv): +0x18 FUN_143226700 update,
  **+0x20 FUN_143226660 next page, +0x28 FUN_143226690 previous page** (FUN_14321c080 / FUN_14321c0a0: wrap 1..0x21),
  **+0x30 FUN_143226630 set page** (= 0.589 FUN_14317bc10), **+0x38 FUN_1432266e0 toggle paged view, +0x40
  FUN_143226620 set paged view** (byte +0x342a8; 0.589 FUN_14317bcc0 / FUN_14317bc00).
- **Two different "show" flags.** Alt+F (`Generic`/`TogglePerformance`, input handler FUN_142d1a5b0 part_0066 ~13193 /
  0.589 FUN_142c80f20) flips **+0x186**. The draw FUN_14321e590 (0.589 FUN_143173b70; part_0092 ~2343) uses +0x186 only
  to call the header slot +0x68 ("FPS / Kb/sec"); it draws pages only when **+0x342a8** (`plVar28[0x6855]`) is set. The
  update FUN_143226700 runs the current page's state (+0x20 of the state: the request senders below) also only when
  +0x342a8 is set (FUN_14321c130 → current page only).
- Who sets +0x342a8: only vtable +0x38 / +0x40. A scan of every virtual call made through DAT_145432880 or a local
  copy of it in the whole 0.695 decompile (re/out/agent_arch inline script) finds +0x40 only in FUN_142d56520 (= 0.589
  FUN_142cbd160) and no +0x20 / +0x28 / +0x30 / +0x38 call at all.
- FUN_142d56520: no caller in the decompile, no 8-byte pointer in the exe (ptrscan.py: only its .pdata entry), no
  rel32 call/jmp to it in .text (relscan.py). Its neighbours in PlayClient are also unreferenced debug bodies:
  FUN_142d569a0 (spawn a cavern at the player, see Caves), FUN_142d56ba0 (VehiclePacketSpawnVehicle). Inference: these
  are bodies of developer console commands whose registration is not in the retail build.
- Not in the retail command table either: the client command names registered with FUN_142b7c980 (attack ...
  zrange, __sendworldcommand, __sendzonecommand, 45 names) contain no monitor command; luabind_695.tsv has no monitor
  binding; Resources/Scripts/admin.lua has none; the exe has no `StatsNext` / `ToggleStats` action string (only
  `TogglePerformance`).

**Answer / status.** Not found yet — but now precise: in the unmodified retail client (both builds) **the paged
monitor is never shown and none of the page requests (0x109 procedural, 0x1E/0x4A9 pathing, 0x1E/0x4AB EnvServer,
0x1E/0x4FC Storybricks) is ever sent**. admin-monitor.md "To do 3" (confirm a normal client sends 0x109) is answered
statically: it does not. Next paths: runtime (Frida: call FUN_143226620(monitor,1) + FUN_143226630(monitor,28) on a
test client to prove the pages and their requests; this is a test-harness change, not a game-file change); other
client builds with the console command table; the 0.695 `Debug` input profile files.

**Docker.** No service is needed for retail players. The admin-monitor container work (BUILD-PATH Phase B item 7)
can only be exercised through a test hook; keep it last and low priority.

## L4. Replies that feed admin pages 11, 12, 24, 32, 33 (and 30)

**Evidence.**
- Registry names (the client's own packet-id table, FUN_1434c7940 calls in part_0107; re/out/packet_ids.tsv for 0.589):
  0x1E/0x4A9 PathingDataMonitorUpdateRequest, **0x4AA PathingDataMonitorUpdateResponse**, 0x4AB
  EnvServerDataMonitorUpdateRequest, **0x4AC EnvServerDataMonitorUpdateResponse**, 0x450 `AdminCommandPacketIdProfiler`,
  0x4D7 `GatewayProfilerRegistration`, 0x4FC StorybricksRequestDiagnostics, 0x4FD StorybricksRequestDataErrors.
- RTTI (both builds) has the two **request** classes and no response class; no `Response` vftable for either appears in
  the decompile (only the request ctors part_0091 42290 / part_0092 314).
- The Command family dispatcher FUN_1433baf00 (0.589 FUN_14330e110) handles admin subs ≥ 0x42A only for 0x42A
  (ClientTime), 0x4AE (CurrentZoneReply, "CurrentZone: %i"), 0x4B5 (AdminPacketVerifyAdminTarget), 0x4D8 (Runspeed); every
  other sub goes to **FUN_143cd6dc0 / 0.589 FUN_143c17ec0**, which calls vtable +0x70 of every module in the registry
  list DAT_1456035f0. Modules register by name through FUN_143cd7650 / 0.589 FUN_143c18750 — 15 in 0.695:
  AdminTemplateTester, ClientCategoryPaletteManager, ClientClaimCameraCommander, ClientInstancedGameManager,
  ClientPropTintManager, ClientStorybricksDebugManager, ClientTemplateManager, CombatSpamManager, Tooltips::TooltipManager,
  VoxelMeshModelManager, ContentManagement::ClientContentManager, ReticleManager, CharacterCreate::CharacterCreateOptionsManager,
  ClientHttpImageCacheManager, WarpNetwork::ClientWarpNetworkManager. **+0x70 of all 15 is FUN_142d90810 = `return 0`.**
  So nothing reads 0x4AA / 0x4AC.
- Page states (vtables 0x144727c80...): update slot +0x20 is empty for ZoneProfiler (FUN_14321ca20), ZoneStatistics
  (FUN_14321ca30), Gateway (FUN_14321c280) — these pages never request anything; they draw text from PlayClient +0x2F0 /
  +0x2F8 / +0x300 (return 0, L4 note). Pathing +0x20 FUN_14321c770 sends 0x4A9 "Pathing" (channel 0); EnvServer +0x20
  FUN_14321c1f0 → FUN_14321b350 (admin-client gated); Procedural FUN_14321c840 sends 0x109 (channel 0); Storybricks
  (part_0092 ~385) sends 0x4FC only for an admin client (+0xB0).
- Page texts name the server commands that fed them: "No zone profiler stats. Use '/profiler on' to enable."
  (part_0091 38659) and "No gateway profiler stats. Use '/gatewayprofiler on' to enable." (FUN_14320f090 / 0.589
  FUN_143164670).

**Answer / status.** In the retail client the replies for pages 11, 12, 24, 30, 32 and 33 have **no reader** (0.695
re-checked; 0.589 dispatcher and registry functions are `same`/equivalent). Their wire layouts were never compiled
into a retail build → layout **not found yet**; next paths: other (internal) client builds, live captures. Pages 28
(0x10A) and 29 (0x10D sub 2) do have readers (agent-L1-L2.md, admin-monitor.md) but are unreachable by L3.
Inference: `/profiler on` was a zone command (AdminCommandPacketIdProfiler 0x450) and `/gatewayprofiler on` a gateway
command (GatewayProfilerRegistration 0x4D7; L5), each registering the admin for periodic stats.

**Docker.** No reply work is needed for the retail client beyond 0x10A / 0x10D-2 (both have readers). Record the page
data sources per service for the replica: zone (profiler, zone stats, pathing, procedural), gateway (profiler), the
Environment Server (job summary, ruin layers), Storybricks (not used by Landmark's server, inference).

## L5. Gateway external opcode 9

**Evidence.** FUN_14337d580 / 0.589 FUN_1432d0790 (`same`): `local_2090 = Gateway::ExternalPackets::BasePacket::vftable;
local_2088 = 9;` first byte 9 (no channel bits), then FUN_142c12ce0 (packet header), FUN_142d4b510 (+0x20, the
StringFixed<64> command name) and FUN_1430a6f80 (+0x80, the argument list) — i.e. the ClientCommandPacket body —
sent by FUN_143a4bea0. Its only caller is the ServerCommand execute FUN_1433c3d80 / 0.589 FUN_143316f90 for source 3.
The client-side receive switch FUN_143a4bd20 / 0.589 FUN_143993e40 handles opcodes **2** LoginReply (FUN_143a4a110),
**4** ForcedLogout (FUN_143a49fc0), **5** TunnelPacketToExternalConnection (FUN_143a4b830), **7** ChannelIsRoutable
(FUN_143a49ee0), **8** ConnectionIsNotRoutable; nothing else. Gateway::ExternalPackets classes used in the 0.695
decompile: BasePacket (17 uses), LoginRequest, LoginReply, Logout, ForcedLogout, ChannelIsRoutable,
TunnelPacketToExternalConnection, TunnelPacketFromExternalConnection (607).

**Answer.** Found: opcode 9 has **no class of its own** (the RTTI search could never find one): it is
`BasePacket(opcode 9)` + a serialised ClientCommandPacket (0x1E/0x43), i.e. "execute this slash command in the
gateway". The gateway's own command list reaches the client as an ordinary CommandListPacket with source 3 (tunnelled),
and any answer comes back as a tunnelled game packet (opcode 5), because the client parses no gateway opcode 9.
Command names: `/gatewayprofiler` is the one the client itself names (page 24 text); the rest not found yet.

**Docker.** The gateway container must (1) accept external opcode 9 and run the command itself, (2) send its own
CommandListPacket (source 3) through the tunnel after login, (3) reply only with tunnelled packets (e.g. a chat/system
message). Never send opcode 9 to a client.

**Also found (dynamic commands, extends dynamic-commands.md).** A second registration path: Command subs **0x45
AddWorldCommand** and **0x46 AddZoneCommand** (handler FUN_1433baf00 cases 0x45 / 0x46) each carry one command name and
register it as an alias of `__sendworldcommand` / `__sendzonecommand` (FUN_1433a0890 / 0.589 FUN_1432f3aa0). Those
two client commands (ClientCommandProcessor::CommandWorldCommand / CommandZoneCommand, execute FUN_1433a2710 →
FUN_1433b6900 / 0.589 FUN_143309b10 and FUN_1433a2790 → FUN_1433b6fb0 / 0.589 FUN_14330a1c0) send
**CommandPacketExecuteCommand 0x1E/0x47** with the typed line on **channel 1 (World)** and **channel 0 (Zone)**
respectively (FUN_14338d040(..., 1, 1) / (..., 0, 1)). 0x48 `ZoneExecuteCommand` has no client class (server-only, L8).

## L6. What selects the TCP login API

**Evidence.** LoginApiWrapper (RTTI 0x1447053a8; ctor FUN_1430afbc0 / 0.589 FUN_143009830, created by FUN_1430c68f0 /
0.589 FUN_143020540) connects in **FUN_1430b3a70 / 0.589 FUN_14300d6e0** (`same`): allocates 0xDA0 bytes and calls
**FUN_143a58420 / 0.589 FUN_1439a0540** = the `Login::ExternalLoginUdpApi` ctor with name `LoginUdp_10`, sets the key
string `F70IaxuU8C/w7FPXY1ibXw==` (FUN_143a5adc0 / 0.589 FUN_1439a2ee0; the emulator already uses it, AuthServer.cs),
then connects with a 30 000 ms timeout. There is no branch and no config read for the transport. The TCP ctor
FUN_143a582e0 / 0.589 FUN_1439a0400 has no caller, no pointer (ptrscan.py) and no inline copy (the only
`ExternalLoginTcpApi::vftable` writes are its own ctor / dtor, part_0135 5416 / 5952).

**Answer.** Found: **nothing selects TCP — the retail client always uses UDP**; ExternalLoginTcpApi is unused library
code (inference: shared with other SOE titles).

**New.** The character login FUN_1430b3cb0 / 0.589 FUN_14300d920 fills `Login::ExternalPackets::PacketCharacterLoginRequest`
(FUN_143a5b9b0) with an app payload: StringFixed<32> `Internationalization/Locale`, u32 locale id
(thunk_FUN_14393fcb0), **u32 `GatewayId` read from the section-less part of ClientConfig.ini (FUN_142b21540 with section
"" , default 0)**. The emulator reads these 8 bytes as `Unknown2` (u64) in LandmarkEmulator.AuthServer
CharacterLoginRequest.cs. Inference: a non-zero GatewayId let a client ask the login server for a specific gateway (one
of several per world); 0 = any. What live did with it: not found yet (new lead L50).

**Docker.** Login container: UDP only. Keep one gateway per world addressable by id so `GatewayId` can be honoured.

## L7. Unresolved send sites in the channel map

**Evidence.** re/out/agent_arch/unresolved.py lists the 55 sites channels.py leaves unresolved in 0.695 (54 in 0.589);
ctor.py resolves a site through the constructor called on the packet variable; the rest by hand (fn.py).

| Channel | Site (0.695) | Class |
|---|---|---|
| Position | FUN_142ca4ce0 (0.589 FUN_142c0af00) | the compact movement update (no class; bit-packer FUN_142c30e20) |
| World | FUN_142cd4e60 | PacketSetLocale (0x5B, ctor FUN_142c65e40) |
| World | FUN_14325d150 | PacketInGamePurchasePlaceOrderPacket&lt;ClientInGamePurchaseOrder&gt; |
| World | FUN_143261640 | PacketInGamePurchaseStoreClientStatistics&lt;…&gt; |
| World | FUN_1432623a0 | PacketInGamePurchasePreviewOrder&lt;…&gt; |
| World | FUN_143645430 | PacketInGamePurchasePlaceOrderClientTicket&lt;…&gt; |
| World | FUN_14326b980 | PacketInGamePurchaseSubscriptionProductsRequest |
| World | FUN_1433a5e60 | AdminCommandPacketFriendsListRequest (and the ForceAdd/RemoveFriend admin packets in the same function) |
| World | FUN_1433a8940 ×6 | GuildChangeMemberRankPacket, GuildUpdateRankPacket ×5 |
| Zone | FUN_1433a8940 | HudPackets::PacketRadialMenuRequest (0xDC) |
| Zone | FUN_142d4b040 | PacketSecurity |
| Zone | FUN_142e0f810 | Abilities::AbilityPacketCastAttempt |
| Zone | FUN_142e33840, FUN_14355a5b0 | AchievementTrackerUpdatePacket |
| Zone | FUN_142fd2d40 | PlayerFilterUpdatePacket |
| Zone | FUN_1431642e0, FUN_1431649d0, FUN_143165440, FUN_143165700 | VoxelConstructionPacketClientSelectionAreaRequest |
| Zone | FUN_1431d9070 ×3 | AdminCommandPacketZoneFlags |
| Zone | FUN_143258720, FUN_143262c00 | PacketInGamePurchaseCommerceTemplateProductCreate (new 0.695 sub 0x30; designs for sale are created through the **zone**) |
| Zone | FUN_143367ce0, FUN_143370e70 … FUN_143371ab0 (9) | PacketChat |
| Zone | FUN_1433a4820 ×5 | AdminCommandPacketAdd / Set / Clear Currency, ListCurrencyRequest |
| Zone | FUN_1433a7d70 | AdminCommandPacketGotoWaypointRequest |
| Zone | FUN_1434e1f30 | InstancedGameSetConfigurationRequestPacket |
| Zone | FUN_143565270 | CoinStoreSellToClientRequestPacket |
| Zone | FUN_14363f210 | PacketMailComposeRequest |

Still unresolved (8): FUN_142e36f70 / 0.589 FUN_142d98fa0 (same packet on World if arg = 1, Zone if 2; template
FUN_142e38410, serializer FUN_142e37100); FUN_142d4ab90 (Zone; object at client +0x314f0 +0x96990, serializer
FUN_142c155f0); FUN_14315f270 and FUN_143165810 (Zone; VoxelConstruction-area code, header writer FUN_14315b0c0 →
FUN_143129600 two-byte family header, inference: VoxelConstruction family); FUN_14330e300 / FUN_14330e370 (Zone,
serializer FUN_14330e4d0, Command family area); FUN_143329ed0 (Zone, object +0x988, serializer FUN_142e00c50).
Next path: the dev world's console.log already prints class + channel per received message — trigger these features
(clipboard / selection area, the Command-area senders) and read the log; or find the owners' constructors that write
the vftable into those fields. **No resolved site contradicts the channel rules in channels.md.**

**Docker.** Confirms the split: World channel = commerce orders / store statistics / locale / guild ranks / admin
friends; everything else above goes to the zone.

## L8. How the World back end and the zone servers talked to each other

**Evidence.**
1. **Ids with no client code.** The packet-id registry compiled into the client (re/out/packet_ids.tsv, 0.589; 0.695
   part_0107 FUN_1434c7940 calls) names messages that have no RTTI class and appear nowhere in the decompile outside
   the registry (checked per name in 0.695; full list re/out/agent_arch/serveronly_695.tsv, which also contains
   ordinary unused client ids — only the names below were verified one by one). Ids are 0.589; in 0.695 Claims subs
   ≥ 0x40 moved +2 and Mail subs ≥ 4 +1 (packets-695.md §A).

   | Parties in the name | Messages (0.589 id) |
   |---|---|
   | Claim service ↔ zone | Claims 0xF9: ZoneAddClaim 0x48, ZoneRemoveClaim 0x49, ZoneAddAllClaimsRequest 0x4A, ZoneAddAllClaims 0x4B, ClaimMoveItemToPlayerOnZone 0x45, UpdateClaimBasicServerInfo 0x58, SaveBasicInfo 0x4F, StatusChange 0x5A, PlayerDeleted 0x60, NameToGuid / GuidToName Request+Response 0x54-0x57, RequestClaimPurchase 0x52, RequestClaimRemoved 0x53, UpdatePropLimits 0x5F, BlockVeins 0x59, ClearAllProceduralProps 0x47, ResetTemporaryTerrainDamage 0x46, SendPlayerUpkeepInfo / UpdatePayment / UpdateReminder 0x4C-0x4E |
   | Mail service ↔ zone | Mail 0xF8: ComposeRequestStage2 / Stage3 0x2 / 0x3, ComposeResponseReturnAttachments 0x5, **ConsumeAttachmentZoneRequest / Response 0x11 / 0x12** |
   | Zone ↔ voxel / Environment Server | VoxelConstruction 0xF4: ZoneSelectionAreaRequest 0xF, ZoneRequestFailedCellList 0x12, OffGridPasteZoneRequest 0x13, **EnvServerResponse 0x15**, HealClaimRequest 0xC, PasteClaimAreaRequest 0xD, DeleteClaimAreaRequest 0xE, ExportContestClaimRequest / Response 0x14 / 0x1A, **SpawnExportedTemplateRequest / DespawnExportedTemplateRequest 0x18 / 0x19** | (TerrainCell 0xEC/0x6 removed 2026-10-15: client-handled MaterialChangeInfo, reader FUN_1431468f0 — terrain-service.md §1.3) |
   | World ↔ zone / gateway (admin) | Command 0x1E: WorldKickRequest / Reply / Pending 0x43A / 0x43B / 0x451, WorldKick 0x443, BroadcastZone 0x43D, BroadcastWorld 0x43E, WorldRequest 0x482, ZoneRequest 0x3F4, ServerInformation 0x3ED, ServerVersion 0x431, ServerUptime 0x432, ZoneVars 0x456, WorldVars 0x46A, LockStatusQuery / Reply 0x4E4 / 0x4E5, **InternalConnectionBypass 0x4E0**, **GatewayProfilerRegistration 0x4D7**, ZoneExecuteCommand 0x48, Profiler 0x450 |
   | Leaderboards / stats service ↔ zone | ProfileStats 0xCA: GetZonePlayerProfileStats / ZonePlayerProfileStats 0x1 / 0x3, GetZoneCharacterStats / ZoneCharacterStats 0xA / 0xB, UpdatePlayerLeaderboards / Reply 0x4 / 0x5 |

2. **Client-handled messages named after the Environment Server.** VoxelConstruction 0xF4/0x16
   `VoxelConstructionPacketEnvOwnershipUpToDateResponse` and 0x17 `…EnvOwnershipDataResponse` (dispatcher FUN_143162830
   part_0087 ~34179; handed to the manager at client +0x320a0, FUN_14312e2c0 / FUN_14312e000) answer the client's
   `VoxelConstructionPacketClientOwnershipRequest` (sent on channel 0, channels-695.tsv). Next to them the zone-named
   `VoxelConstructionPacketZoneDisplayNotification / ZoneMissingPropList / ZoneUnknownPropList`.
3. **Gateway internal side.** RTTI (both builds): `Gateway::InternalPackets::BasePacket`,
   `PacketTunnelPacketFromExternalConnection`, `PacketTunnelPacketToExternalConnection`,
   `PacketTunnelPacketToExternalConnectionList`, `List<Gateway::ExternalDestination>`. The only code use is the
   monitor's ApplicationPacketTracker (ctor FUN_143c78d10 / 0.589 FUN_143bb8d10, created by the monitor ctor): it builds
   a TunnelPacketToExternalConnectionList with **internal opcode 0x16** and accounts its header as **11 bytes + 12 per
   destination**, next to other fixed header sizes (2/6, 0x13/0xd, 7/0xc, 1/1). Decoded 2026-10-15 (gateway-service.md §1.4): 0x13 / 0xd are the headers of internal opcodes 7 / 8; 2 / 6 are where zone packets keep their sub-opcode, not gateway headers. This is the
   "gatewayHeaderBytes" of the gateway profiler.
4. Developer posts (live-server-architecture.md) add nothing about the inter-server protocol; design/forum searched
   for gateway / world server / zone server / environment server / login server / claim server (2026-10-15).

**Answer / status.** Partly found. The inter-server messages used **the same packet families and id space as the
client** (inference from the shared registry: ids for server-to-server messages sit between client ids in the same
families). The names show who talked to whom: a **claim (build site) service** that pushed claims to zones
(ZoneAddClaim / ZoneAddAllClaims) and asked zones to move items to players; a **mail service** that asked the zone to
hand over attachments (ConsumeAttachmentZoneRequest); the **zone ↔ voxel / Environment Server** path for selections,
pastes, claim heal / paste / delete and exported-template spawns (EnvServerResponse; the client's EnvOwnership replies
come from the Environment Server side, inference); a **world** level that kicked, broadcast and queried zones and
gateways (WorldKick*, BroadcastZone/World, LockStatus*, InternalConnectionBypass); a **stats / leaderboard** service.
How these travelled (gateway internal tunnel vs direct connections) and their field layouts: **not found yet** (L48).

**Docker.** Use the client's family / sub ids and these names for the messages between containers (claims ↔ zone,
mail ↔ zone, zone ↔ terrain / Environment Server, world ↔ zone / gateway, stats). The transport is ours to choose; the
names and responsibilities are traced.

## ProceduralPlacementTool, CaveMeshes and the cave placements

### What ships

- `CommonData\ProceduralPlacementTool\CaveMeshes\` — 37 `.vfm`, identical in both builds (fileindex_589/695, same
  sizes); the 2017 CDN digest (re/out/final-digest-2017/common-shared-cdn.sha.soe.txt ~1669) lists the same 37 with
  timestamps 2014-05-27 … 2015-02-10 and marks 4 older ones deleted (Biome_Caves_Entrances_Stalag01, LargeRoom02,
  MediumRoom03, and a Cap01 copy).
- `CommonData\Voxel\caves\` — the same 37 base names as `.cav` (VoxelCaveTileDefinitions.txt `CAVE_TILE_ASSET`), plus
  `ContinentCavePlacements[N]_Surface.cpa` / `_Underground.cpa`. In the digest each `.cav` and its `.vfm` were
  committed within ~70 s of each other (CavernCorner01 .cav 1423595108 / .vfm 1423595177; Connection05 1423597191 /
  1423597242); the `.cpa` of continent 406 is dated 2016-04-16 (1460835232).
- `CommonData\Voxel\veinData\HowToGenerateVeins.txt` (Daybreak dev note, 2014): per continent, an ini
  `Continent_XXX.ini` under `EQNext\Dev\Runtime\Tools\ProceduralPlacementTool` and a matching `.bat`; flags
  `ProcessSurfaceCaves=1`, `ProcessCaveNodes=1` (caves), `ProcessSurfaceCache=1` (the .csc surface cache),
  `ProcessMaterialVeins=1` (veins); "Veins are dependent on both caves and the surface cache".

### The .vfm format (decoded here, every file parses)

Little-endian: `u32 version = 2, u32 nVert, nVert × float3, u32 nIndex, nIndex × u32, u32 nSub, nSub × {u32 1, u32
firstIndex, u32 indexCount}`; the submeshes cover the index list exactly (Cap01: 180 + 4584 + 2409 + 2136 + 36 = 9345).
All 37 parse exactly to the end of file, every submesh record starts with 1. Units metres, centred on the tile origin
(Cap01 ±16 m; CavernMiddle01 ±44.8 × ±89.6 × ±44.8; Hub01 ±128). 34 of 37 have a 36-index submesh (12 triangles, a
box) and 25 a 180-index one — inference: connection / cap markers (not proven: L49). A plain triangle
mesh: the source geometry the tool voxelised into the `.cav` tile (`.cav` = VoxelFarm::CaveTileAsset, LandmarkEmu
docs/terrain.md "Caves"). Neither exe contains `.vfm`, `CaveMeshes` or `ProceduralPlacementTool` (raw scan, both
builds), so the client never reads them.

### What the client contains

- **The generator:** `VoxelFarm::CaveGeneratorImpl` (TileChoiceData, TileEntry, CaveTileTracker, InstanceConnectionData),
  `CaveTileSetTemplate`, `CaveTileMaterialRemap`, with its rule tables shipped as Resources: VoxelCaveTileSets (CAVE_LENGTH,
  FREQUENCY, ENTRANCE_MIN/MAX_SLOPE, ALLOWED/FORCED_EXPOSED_PERCENT, elevations, SUGGESTED_CAVERN_COUNT_MIN/MAX,
  START_WITH_CAVERN_PERCENT), VoxelCaveTileSetMappings, VoxelCavernDefinitions (min/max width / length),
  VoxelCaveTileToCavernMappingDefinitions, VoxelCavernToTileSetMappingDefinitions, VoxelBiomeCaveTileSetMapping
  (biome → tile set, underwater %), VoxelCaveTileMaterials, VoxelBiomeCaveMaterials. Network builder FUN_144030970 /
  0.589 FUN_143f6ccb0 ("Tried to start network with a cavern…", "Cave Tile %d has a connection that doesn't have a
  matching cap…", "One of your cave tile sets doesn't include an entrance.").
- **Its only runtime entry is dead:** FUN_14403ade0 ← FUN_14403add0 ← FUN_143f9b910 ← FUN_143f9bba0 ← **FUN_142d569a0**
  (0.589 FUN_142cbd610; PlayClient: generate a cavern at the player's position) — no caller, no pointer (same
  situation as the monitor page setter). So the retail client never generates caves.
- **The loader:** FUN_143ff1310 / 0.589 FUN_143f2d420 loads `ContinentCavePlacements[%d].cpa` /
  `[%d]_Surface.cpa` / `_Underground.cpa` per continent (VoxelFarmCore::AddCaveData). The `.cpa` structure
  (`VoxelFarm::CavePlacementAsset`, ctor area part_0161 ~28265-28900) holds TileInstanceData, CavernInstanceData,
  LayerInfo, CaveTileObjectGaps / CaveRoomObjectGapData and **ManuallyPlacedExportedTemplateData** (exported templates
  placed by hand into the cave network). The emulator reads only the tile records (CavePieces.cs).
- **The server half:** `VoxelFarm::CaveManager` with GetCaveBeginningLocation / GetNextCaveBeginningLocation /
  GetRandomCaveBeginningLocation (FUN_143f839e0 / FUN_143f841b0 / FUN_143f84420; 0.589 FUN_143ebfe50 / 143ec0620 /
  143ec0890) and "Player attempting to zone to next cave…" logs. No caller and no pointer in the client: this is code
  for the side that answers `PacketTeleportRandomUndergroundRequest` / `TeleportToNextUndergroundCave` (luabind_695:
  Teleport.TeleportToRandomUndergroundCave FUN_1435e8270, TeleportToNextUndergroundCave FUN_1435e82e0). Random pick:
  a cave index different from the current one (`do … while (uVar3 == param_4)`), optionally restricted to caves whose
  node value (+0x60) equals `*param_5` (what that value is: not found yet, L51).
- No packet carries cave placements: the only cave packet is Command 0x5C `TeleportToCave` (registry).

### What it says about the server that placed caves

- **No live server generated caves.** Daybreak ran the ProceduralPlacementTool offline per continent (dev note) and
  shipped the result as `.cpa`; the client builds cave voxels from `.cpa` + `.cav` itself, and there is no packet for
  cave layout, so client and server must have read the same files (the server-side CaveManager compiled into the client
  works on "loaded caves"). The event islands 436-438 have no `.cpa` / `.vdt` because the tool was never run for them
  (consistent with terrain-695.md §2.3).
- **Procedural content in caves had two sources:** static manual template placements inside the `.cpa`
  (ManuallyPlacedExportedTemplateData) and the Environment Server's queued "Ruin Layer" jobs (admin page 33) plus the
  zone↔Env exported-template messages (SpawnExportedTemplateRequest / DespawnExportedTemplateRequest, L8). Which cavern
  ruins came from which: not found yet (L47; also relevant to L12 outpost chests).
- Procedural surface spawns (trees, rocks, harvestables) remain the zone's job (page 28 data, agent-L1-L2.md); masks and
  ProceduralPlacementSet* are the same tool's inputs.

**Docker.** Terrain / voxel container: load `.cav` + `.cpa` (already done) and expose "cave beginning locations"
(CaveManager equivalent) to the zone for the underground teleports. No cave-generation service. The Environment Server
container owns ruin-layer jobs and exported-template spawns; it needs the `.cpa` manual placements once decoded.

## Changes to the recreation plan (for trace/live-server-architecture.md — not edited here)

1. **Login:** replace "login over TCP (ExternalLoginTcpApi)" in "Connection flow strings" with: the retail client
   always uses UDP (FUN_1430b3a70 → FUN_143a58420 `LoginUdp_10`); the TCP class is unused. Add: CharacterLoginRequest
   carries locale, locale id and a ClientConfig.ini `GatewayId` (FUN_1430b3cb0) — the login service can be asked for a
   specific gateway.
2. **Gateway:** add external opcode 9 = gateway-executed slash command (bare BasePacket + ClientCommandPacket body); the
   client accepts only gateway opcodes 2 / 4 / 5 / 7 / 8, so gateway answers are tunnelled packets; the gateway has its
   own command list (source 3) incl. `/gatewayprofiler`. Internal side: TunnelPacketToExternalConnectionList is internal
   opcode 0x16, header 11 bytes + 12 per destination (ApplicationPacketTracker FUN_143c78d10).
3. **Commands:** world and zone also add single commands with AddWorldCommand / AddZoneCommand (0x1E/0x45 / 0x46),
   executed by the client as CommandPacketExecuteCommand 0x1E/0x47 on World / Zone channel (update dynamic-commands.md).
4. **Server-to-server messages:** add a section listing the id-only messages of L8 (claims ↔ zone, mail ↔ zone, zone ↔
   voxel / Environment Server, world ↔ zone / gateway admin, stats) as the traced inventory of internal messages; the
   recreation's containers use these family / sub ids and names.
5. **Claims and mail as services:** strengthen the table rows "Claims (build site) service" and "Mail service" with
   ZoneAddClaim / ZoneAddAllClaims / ClaimMoveItemToPlayerOnZone and ConsumeAttachmentZoneRequest / Response.
6. **Environment Server:** add that it answers cell ownership (VoxelConstructionPacketEnvOwnership* 0xF4/0x16-0x17,
   client-handled) and exported-template spawns (0xF4/0x15, 0x18, 0x19), besides ruin-layer jobs.
7. **Caves:** add "Caves were generated offline (ProceduralPlacementTool, ProcessSurfaceCaves / ProcessCaveNodes) into
   per-continent `.cpa`; client and server load the same files; CaveManager (server code in the client) picks cave
   beginning locations for the underground teleports". Drop any idea of a cave-generating service.
8. **Admin monitor:** state that the retail client cannot open the paged monitor (L3) and has no reader for pages 11,
   12, 24, 30, 32, 33 (L4); the "client's admin panel working" goal (BUILD-PATH §1) is reachable only with a test hook
   (Frida) and only for pages 28 / 29. Decision needed from the user before spending Phase B item 7 effort.
9. **Channel table:** "Commerce template products are created through the zone (PacketInGamePurchaseCommerceTemplateProductCreate,
   channel 0)" and the L7 resolutions (World: SetLocale, PlaceOrder / PreviewOrder / PlaceOrderClientTicket /
   StoreClientStatistics, guild rank packets).

## Gaps (TASKS §7 style)

- [ ] **Gateway opcode 9** (architecture-leads.md L5): the gateway service accepts external opcode 9 (BasePacket 9 +
      ClientCommandPacket body), runs its own commands, sends a CommandListPacket with source 3 after login, and answers
      only through tunnelled packets. Emulator gateway knows opcodes 1-8 only.
- [ ] **AddWorldCommand / AddZoneCommand / ExecuteCommand** (L5): accept 0x1E/0x47 on both World and Zone channels; use
      0x45 / 0x46 where a single command is added at runtime.
- [ ] **CharacterLoginRequest tail** (L6): name the emulator's `Unknown2` as u32 LocaleId + u32 GatewayId; when several
      gateways exist, honour a non-zero GatewayId.
- [ ] **Internal message inventory** (L8): write trace/architecture/internal-messages.md from the L8 table and use those
      family / sub ids between the Docker services (claims, mail, terrain / Environment Server, world admin, stats).
- [ ] **Underground teleports like CaveManager** (Caves): "random cave" never returns the cave the player is in; keep a
      per-continent list of cave beginning locations in the terrain service.
- [ ] **Admin monitor scope** (L3 / L4): only 0x10A and 0x10D sub 2 have client readers; build them last, test with a
      Frida page hook; ask the user whether the panel goal stays.

## Hand-offs

- #2 terrain: decode the `.cpa` sections after the tile records (CavernInstanceData, LayerInfo, CaveTileObjectGaps /
  CaveRoomObjectGapData, **ManuallyPlacedExportedTemplateData**; CavePlacementAsset ctor part_0161 ~28265-28900, 0.589
  reader FUN_143edfd60) — may hold the cavern ruin / outpost placements (L12, L47).
- #3 packets: CharacterLoginRequest app payload = StringFixed<32> locale, u32 locale id, u32 GatewayId (FUN_1430b3cb0);
  0x1E/0x45 AddWorldCommand / 0x46 AddZoneCommand (one IString) and 0x47 CommandPacketExecuteCommand layouts.
- #10 coverage: re/out/agent_arch/serveronly_695.tsv lists registry ids with no client class or reader — use it to mark
  "server-only" rows instead of "missing" in the coverage audit (it also contains plain unused client ids; check each).
- #1 / procedural: the vein log "Continent Veins: vein/group/mapping … attempts=%d (surface/cave/void=%d/%d/%d)" is the
  ProcessMaterialVeins step of the same offline tool, compiled into the client.
- #5 functions: PlayClient's unreferenced debug bodies FUN_142d56520 (monitor page), FUN_142d569a0 (spawn cavern),
  FUN_142d56ba0 (spawn vehicle) — one block of developer commands with no registration in retail.
