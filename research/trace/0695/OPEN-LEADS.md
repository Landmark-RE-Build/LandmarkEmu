# Open leads: things not found yet

Nothing here is "lost" or "unknown for good". Each entry lists what is missing, every path already searched, and the
paths still to try. Come back to this list regularly (BUILD-PATH-0695.md rule 2), especially once
re/decomp_all_695 exists. Move an entry to **Found** with its answer and source when it is resolved.

## Open

### L1. Procedural placement mask bitmap TilingTestZZ.bmp (the other 13 masks: see Found)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** TilingTestZZ.bmp is not in the live Feb 2017 CDN manifest either (33 masks listed, not it); row 170 is a test row (group 17, altitude 0-4000, angle 0-90). Live clients never had it; the server keeps groups 17/18 out. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — Notes below (trace/0695/agent-leads2-d1.md)
- **Need:** `TilingTestZZ.bmp`, the MASK of ProceduralPlacementSets.txt row 170 (group 17, biome 32, MASK_SCALE 4).
- **Searched:** 0.589 Resources/*.pack raw bytes for "PPP_Mask" (no hit); Resources top folder. 2026-10-15
  (trace/0695/agent-L1-L2.md): full file index of both installs (re/tools/fileindex.py → re/out/fileindex_589.tsv /
  fileindex_695.tsv: every loose file and every pack directory entry; no name containing "tiling"; all 302 .bmp per
  build are loose, none in a pack); text search of every txt/xml/ini/cfg/lua/csv/json in both installs; raw scan of
  both Landmark64.exe (ASCII + UTF-16: no "TilingTest", "PPP_", "ProceduralPlacementTool", ".bmp" path); both
  decompiles and re/out/strings.tsv; testclient (its CommonData links to the 0.589 install).
- **Try next:** other client builds / EQN-era archives the user may have. The client never loads masks (no loader in
  either exe), so this only matters for the server-side placement rebuild.

### L2. Row data of the two server-only procedural tables (schemas found: see Found)
- **CLOSED 2026-10-18 (built):** min/max respawn model built (3c8f9c4, user decision); entity-list rows themselves server-only.
- **Pass 3, 2026-10-17: ADVANCED (proposal)** — Not in the live manifest (server-only, proven). Best-evidence default for the entity-list model from Domino's 55352 post: min / max per island-layer-biome-entity, respawn elsewhere when below min. (trace/0695/agent-leads3-b.md)
- **Need:** the rows of ProceduralPlacementEntityLists.txt and ProceduralPlacementEntityListEntities.txt.
- **Searched:** gameData (0.589 Resources tables). 2026-10-15 (trace/0695/agent-L1-L2.md): both file indexes (no name
  containing "EntityList"), text search of both installs, testclient/Resources, both exes (the ".txt" names exist only
  in the 0x10A packet ctor FUN_142bcf5d0 / 0.695 FUN_142c69030), both decompiles (no loader; no
  StandardDefinitionManager for these definitions in RTTI).
- **Try next:** runtime capture of a 0x10A packet is impossible without a live server; archives (forum / dev posts)
  for entity-list contents; otherwise rebuild rows from ProceduralPlacementSet* tables + live observations.
- **2026-10-16 (procedural-placement-695.md):** the server now places trees from the shipped sets (Gathering +
  ProceduralPlacement.cs); what the entity lists added (per-zone min / max per entity, respawn elsewhere: Domino, forum
  55352) is L113.

### L3. How retail selects a performance-monitor page
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN [P]):** Monitor global DAT_145432880 (set in FUN_142d12360); its vtable 0x14472a8c8 slots +0x20 / +0x28 / +0x30 / +0x38 / +0x40 (next / prev / set page / toggle / set +0x342a8) are called only by FUN_142d56520 (no reference). Every other use of the global reads +0x50 (page), +0x60, +0x168, +0x186. Retail cannot show a page; decided 2026-10-15 (web admin) (trace/0695/agent-leads3-a.md)
- **Sweep 2026-10-17: NEEDS-RUNTIME** — Only a Frida page hook can show a page; nothing new statically (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what calls FUN_142cbd160 (show + set page) / FUN_14317bc10 in the retail client.
- **Searched:** static callers in re/decomp_all (none), 8-byte and 4-byte pointers in the exe (only .pdata), Lua
  bindings (luabind.tsv), input actions (StatsNext / ToggleStats etc. not in the exe or RefInputProfileActions).
- **Try next:** runtime (Frida on FUN_14317bc10 while pressing Alt+F and keys), indirect calls through jump tables
  Ghidra didn't recover, the 0.695 decompile, server-sent commands that run client commands.
- **2026-10-15 (agent #8, trace/0695/architecture-leads.md L3): narrowed, still open.** Alt+F flips monitor +0x186,
  which only draws the FPS header; pages are drawn and their requests sent only when +0x342a8 is set (vtable +0x38 /
  +0x40, 0.695 FUN_1432266e0 / FUN_143226620), and the only call of those is in FUN_142d56520 (= 0.589
  FUN_142cbd160), which has no caller, no pointer and no rel32 reference in the 0.695 exe; no client command, Lua
  binding, admin.lua line or input action selects pages. So retail never shows a page nor sends 0x109 / 0x4A9 / 0x4AB
  / 0x4FC. **Try next:** Frida test hook (FUN_143226620(monitor,1) + FUN_143226630(monitor,page)) to prove the pages;
  other client builds with the developer command table.

### L4. Replies that feed admin pages 11, 12, 24, 32, 33 (and 30 Storybricks)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Already answered (no reader, no sender in retail; agent-e-leads.md §3). Nothing client-side can show live's layouts (trace/0695/agent-leads3-a.md)
- **E-leads 2026-10-17: FOUND — no retail reader for the three replies; retail can't send the requests (agent-e-leads.md).**
- **Need:** the packets that fill the zone profiler (11), zone stats (12), gateway stats (24), pathing (32) and
  Environment Server (33) pages.
- **Found so far:** pages 11 / 12 / 24 read text from PlayClient vtable +0x2f0 / +0x2f8 / +0x300, which return 0 in
  the 0.589 retail exe; page 32's metrics array (ClientPathMetricEntry) is swapped in by FUN_14316f010, which has no
  static caller; no handler compares Command subs 0x4A9-0x4AD.
- **0.695 checked (2026-10-15):** PlayClient / BaseClient vtable +0xB0 = FUN_142b21000 and +0x2F0 / +0x2F8 / +0x300 =
  FUN_142b20fd0 / FUN_142b20fe0 / FUN_142b20f20 all `return 0` in 0.695 too.
- **Try next:** indirect calls, the module registry FUN_143c16290 / FUN_143c16de0
  (which objects register, their vtable +0x70 HandleCommand), runtime capture with an admin-flagged player.
- **2026-10-15 (agent #8, architecture-leads.md L4): retail answered — no reader; live layouts still open.** Registry
  names 0x1E/0x4AA PathingDataMonitorUpdateResponse and 0x4AC EnvServerDataMonitorUpdateResponse have no class and no
  reader; the Command dispatcher FUN_1433baf00 handles admin subs 0x42A / 0x4AE / 0x4B5 / 0x4D8 and passes the rest to
  the module registry FUN_143cd6dc0 (0.589 FUN_143c17ec0), whose 15 registered modules all have +0x70 = `return 0`
  (FUN_142d90810). FUN_14316f010 is just a slot of Array<ClientPathMetricEntry>'s vtable. Pages 11 / 12 / 24 have empty
  update slots. Page texts name `/profiler on` (zone) and `/gatewayprofiler on` (gateway). **Try next for the live
  layouts:** other / internal client builds, live captures (none known).

### L7. 54 unresolved send sites in the channel map
- **CLOSED 2026-10-17 (pass 2, FOUND):** All 8 remaining send sites resolved: 7 on Zone, and 1 that picks World or Zone; 3 of them are dead Built / done: AbilityLanding read and logged (ef483fb). (trace/0695/agent-leads2-d1.md)
- **Sweep 2026-10-17: ADVANCED** — FUN_14315f270 proven Zone (channel 0 constant, via the L77 trace) (evidence: trace/0695/agent-leads-sweep.md).
- trace/architecture/channels.md (re/tools/channels.py prints them): resolve by hand.
- **2026-10-15 (agent #8, architecture-leads.md L7): 47 of 55 (0.695) resolved** (table in that file; no conflict
  with channels.md). Still open (8): FUN_142e36f70 (World or Zone by argument), FUN_142d4ab90, FUN_14315f270,
  FUN_143165810, FUN_14330e300, FUN_14330e370, FUN_143329ed0 — all Zone except the first. **Try next:** the dev world's
  console.log (class + channel per message) while using clipboard / selection and the Command-area senders; or find
  the owners' constructors writing the vftable into those fields.

### L8. How the World back end and the zone servers talked to each other
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 2, 2026-10-17: ADVANCED (small)** — Internal tunnel header sizes proven (L69); the sibling client layouts named (trace/0695/agent-leads2-d1.md)
- Server-to-server traffic never reaches the client. **Try:** the internal packet classes compiled into the client
  (Gateway::InternalPackets::*, any other *Internal* / *Server* classes in RTTI), developer posts, the 0.695 client.
- **2026-10-15 (agent #8, architecture-leads.md L8): partly found (names).** The client's packet-id registry names
  ~40 messages with no class or reader in the client whose names give the parties: claims ↔ zone (ZoneAddClaim,
  ZoneAddAllClaims, ClaimMoveItemToPlayerOnZone...), mail ↔ zone (ConsumeAttachmentZoneRequest / Response), zone ↔
  voxel / Environment Server (ZoneSelectionAreaRequest, OffGridPasteZoneRequest, EnvServerResponse,
  Spawn/DespawnExportedTemplateRequest), world admin (WorldKick*, BroadcastZone / World, LockStatusQuery / Reply,
  InternalConnectionBypass, GatewayProfilerRegistration), stats (ZonePlayerProfileStats...). Internal gateway opcode
  0x16 = TunnelPacketToExternalConnectionList (11 + 12 bytes per destination). Layouts and transport: lead L48.

### L10. Length of the launch Lost Crystal loot lockout (TASKS 6.25)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN):** 24 h + 574500 built; the only number on disk is a player's (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — No new text near 574500; keep 24 h. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED** — The only number ("once every 24 hours or so") is from 24 Jun 2016, after the 16 Jun increase, not from launch. The server's 24 h matches it. 574500 is already built. Comment corrected (01e6ad6). (trace/0695/agent-leads2-c.md)
- **Sweep 2026-10-17: ADVANCED** — No duration found; the refusal text exists: StringHashToValue `CaveRewards.TooSoonToUse` = 574500, which the server does not use (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** how long one player stays locked out of one Lost Crystal's loot at launch (dev, 4 May 2016: "The timer is
  for each Lost Crystal, though, not globally"; on lockout only the teleport prompt shows).
- **Searched (2026-10-15):** trace/0695/patchnotes.tsv; design/forum/all (2016-05-04 feedback thread: only a player's
  guess "once every 24 hours or so"; 2016-06-15 notes: the timer was "significantly increased", no number);
  design/wiki; Outposts.cs.
- **Try next:** the wiki's Lost Crystal page history, other forum threads (search "lockout", "Lost Crystal"), the 0.695
  client (any Lost Crystal timer text or table), archived streams / guides.

### L11. Which items wear on harvesting, and by how much (TASKS 6.53) — PARTLY FOUND 2026-10-16
- **CLOSED 2026-10-18 (FOUND, built):** the 12 tools with a DurabilitySet are known [P items.json]; players saw picks / extractors wear on every swing, dirt included, not scaled by material (19 Apr 2016, agent-leads3-b.md). Built: the flat 1 point per swing on those tools. No source gives another number.
- **Pass 3, 2026-10-17: ADVANCED (small)** — Player, 19 Apr 2016: picks / extractors wear even when digging dirt; wear is not scaled by material. Our flat 1 point per swing matches. No change. (trace/0695/agent-leads3-b.md)
- **Sweep 2026-10-17: ADVANCED (weak)** — Forum 56945 (Apr 2016): tools broke and needed Lumen repairs; no per-swing number (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the DurabilitySet of the pick / axe / extractor items and the condition lost per harvest swing.
- **Found 2026-10-16 [P]:** in the live item dump (gameData695/items.json, definition u41_46[4] = client +0x168)
  exactly 12 tools name a set: set 1 (9751) Farseer / Goliath / Charmed / Whirlwind picks 3254-3257 and Hotfoot /
  Goliath / Charmed / Whirlwind axes 3260-3263; set 5 (14751) NovaTech Extractor / Harvestron / Holoseeker /
  Velocitizer 3702-3705. Every other tool (Adventurer's 3253 / 3259 etc.) has set 0. The client's
  DurabilitySetDefinition reader (0.695 part_0146) reads only DURABILITY_COUNT / LOSS_PER_HP_DMG / LOSS_PER_DEATH,
  so the loss per harvest was a server value.
- **Interim (ours, Durability.ToolLossPerHarvest):** 1 point per harvesting swing (mining swing that collects, every
  axe hit, every reaped plant); a broken tool (0 %) gives half the wood (Durability.ToolStrength, mirrors the weapon
  rule; mining's yield in Abilities.Dig doesn't apply it yet).
- **Still open:** the live loss per swing and what "limited functionality" (P4524) / "a bit more functional" (P4846)
  meant for a broken tool. **Try next:** forum / reddit posts Apr - Jun 2016 counting swings until a tool broke,
  repair costs players quoted (cost per point is known: 0.004 Lumen), videos of a broken tool.

### L12. Where an outpost's Lost Chest could hide (TASKS 6.24)
- **CLOSED 2026-10-17:** answered and built: the ruin templates' "Spawner: Ruins: Chests (1 Limit)" props (2731); Outposts.cs picks one at random. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND (already built)** — The 0.695 ruin_*.otd "Spawner: Ruins: Chests (1 Limit)" props 2731; Outposts.cs picks one at random (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the set of hidden chest spots per outpost (dev, 26 Apr 2016: the chest "won't always be in the same place").
- **Searched:** Outposts.cs (one spot per ruin, chosen by the server), the cavern_ruins_*.otd templates (voxels only;
  2026-10-15: none of the 0.695 cavern_ruins_*.otd holds a "Spawner: Ruins: Chests (1 Limit)" prop 2731, while the 0.695
  ruin_*.otd cavern ruins gained up to 43 each (30 of 32 gained some) — trace/0695/terrain-695.md §4).
- **Try next:** the ruins' prop data in the .otd / design files (chest props placed by the contest builders), forum
  guides to outpost chests, the 0.695 data.

### L13. Bitmap names live sent for the event islands (continents 436-438) — FOUND 2026-10-16 (trace/0695/terrain-695.md §2)
- **CLOSED 2026-10-17 (pass 3, FOUND (no change)):** Bake check of 2026-10-16 stands. (trace/0695/agent-leads3-b.md)
- **Need:** the continent definition in SendZoneDetails for zones 460 Halloween Party, 461 Winter Holiday Party, 462
  Isle of Judgement: BiomeMap / ShapeMap / HeightLayerMap names (no Continent_*_436/438.bmp and no Shape /
  HeightLayerIds for 437 exist in either build) and the other fields.
- **Found so far:** the .ocm/.csc are loaded by continent id (engine +0x5208, 0x143ff376f); 436's surface cache = the
  template island 427/429 (Qeynos / deciduous BiomeIds ee5ba3f32e) at 99.99 %, 437 = 438 at 99.98 %;
  Continent_BiomeIds_437.bmp is the template island in Tundra; continents 424-429 and 432 share Shape and HeightLayerIds;
  VoxelContinentWorld 436 → world setting 43 Museum Qeynos, 437 / 438 → 48 Museum Tundra.
- **Searched:** both installs' CommonData/Voxel and re/out/fileindex_*.tsv (names containing 436-438), Resources tables,
  the 0.695 decompile for "Continent_Shape" / "BiomeIds" format strings (none: names come from the packet / definition,
  FUN_143cdf940 keys BiomeMap / ShapeMap / HeightLayerMap).
- **Found 2026-10-16 [I] → [event-islands-695.md](event-islands-695.md) §2:** caches built from the same three bitmaps
  agree at a 99.994-99.998 % noise floor (425 / 426, 427 / 429), different biome bitmaps on the same Shape differ by
  2.2-2.5 % (low field); 436 = 427 / 429 at the floor → template Shape / HeightLayerIds + BiomeIds ee5ba3f32e (427 =
  429); 437 = 438 and ≥ 6 % from every other cache → the one unused template-layout biome bitmap, BiomeIds_437 (Tundra,
  = VoxelWorldSettings 48 of 437 / 438). The server now sends Shape_424 + HeightLayerIds_424 + BiomeIds_427 (460) /
  BiomeIds_437 (461, 462) (Game/Landscapes.cs). The .csc is decoded (L16), so a bake can be checked against it.
- **Try next:** the bake test (TESTING-NEEDED "Event islands"): bake zones 460-462 on the 0.695 client and run
  `tools/terrain/csc.py pick 460 436 427 424 437` — ~96-98 % against the island's own cache and siding with it where the
  candidates differ proves the bytes (all 28 baked islands score 95.5-98 % against their own cache, 5-7 % against
  others). Still open after that: whether live sent these file names or files the install never got (only a capture
  shows), and which SendZoneDetails field carried StaticZoneData GEOMETRY_ID (456-458 for the event zones; = zone id
  on every island, so the 406 capture's Unknown5 / Unknown7 = 406 cannot tell): the 0.695 PacketSendZoneDetails
  consumer (0.589 dispatcher part_0065 ~44880 copies the packet into locals local_e7e0 = world id, local_e838 compared
  with the current zone at +0x63f5 × 8) — map which local is +0x4c / +0x58 of the packet; forum / wiki screenshots.

- **FOUND 2026-10-16 (bake check):** the 0.695 test client loaded zones 460-462 with the names the server sends (Shape_424 +
  HeightLayerIds_424 + BiomeIds_427 for 436, BiomeIds_437 for 437 / 438): heights baked from the client itself
  (gameData695/TerrainHeights460-462.bin, ~26 MB each, 100 % valid, y 733-1776 m), and `tools/terrain/csc.py pick`
  sides with each island's own shipped cache wherever the candidates differ (460: 436 over 424 80.9 %, over 437 94.4 %;
  461: 437 over 436 / 427 83 %; 437 vs 438 differ in 28 texels only).

### L14. Layer 496 "391_Layer2": a second underground layer for one continent (terrain-695.md §3)
- **CLOSED 2026-10-17 (pass 3, FOUND [P]):** TELEPORT_IMAGE_ID is an Images.txt image id: 10228 = back_depthSelector_tier1.dds, 10229 = back_depthSelector_tier2.dds. Layer 496 is a tier-2 depth layer. The Travel window shows it as a zone row's ImageId (FUN_143174ca0). Built: comment fixed 88fa629. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED** — The client loads every mapped layer of a continent (FUN_143fe3930), so 391 gets both 364 and 496. Each layer's biome bmp and its bmp → property-group pairs are registered. (trace/0695/agent-leads2-b.md)
- **Need:** how the client consumes two VoxelContinentToLayerMapping rows for continent 391 (364 and 496), where tier-2
  caves (property groups 234 / 228 / 258) would come from, and what TELEPORT_IMAGE_ID 10229 shows.
- **Searched:** ContinentCavePlacements[391]_Surface/_Underground.cpa (only groups 227 / 233 and surface groups); the
  cave-file name builder (only "", "_Surface", "_Underground" suffixes, part_0163 ~19231-19506); row parsers
  FUN_144047f00 / FUN_14407ae40; the definitions manager's generic AddOrUpdate (FUN_143fe26f0).
- **Try next:** users of mapping +0xc / +0x10 and layer +0x48 (find the continent → layer lookup through VoxelClientImpl
  +0xc8 definitions, container +0x1008); the warp network's layer handling (FUN_143f34cb0); patch notes for a deeper
  Arid Thicket cave layer; runtime on 391 with a 0.695 client.

### L15. How the client picks a continent's VoxelWorldSettings
- **CLOSED 2026-10-17 (pass 2, FOUND):** VoxelFarmCore::InitVoxelStructureContinent FUN_143ffa7c0 reads the client table VoxelContinentWorld, keyed by the core's continent id. Built / done: nothing to change (the client picks the setting itself). (trace/0695/agent-leads2-b.md)
- **Need:** whether the world setting (biome set) of a continent comes from the client table VoxelContinentWorld
  (loaded into the definitions container at +0x2a8, FUN_143fbcb30) or from the server's SendZoneDetails.
- **Try next:** users of container +0x2a8 (container = VoxelClientImpl +0xc8, part_0087 ~16046); the SendZoneDetails
  reader in 0.695 (not located in this pass: PacketSendZoneDetails vtable 0x1446aced0 slots are ctor/dtor only).
- **Supporting evidence 2026-10-16 (event-islands-695.md §2):** the server sends the 406 capture's Sky fields (Sky2 5,
  Sky5 "OpenBeta_Do_NOT_ChangeThisText_16") for every landscape, yet each of the 28 baked landscapes matches its own
  shipped surface cache (95.5-98 %): nothing continent-specific for the terrain comes from those fields, consistent
  with the setting following the continent id (VoxelContinentWorld). Not a proof of which code path reads it.

### L16. Meaning of the .csc / .ocm values, the client without them, and material 65532 in templates
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN):** 65532 found; event islands are refused to 0.589 clients (event-islands-695.md:133, Landscapes.cs needs the 0.695 .csc), so "client without the files" never happens; the .ocm grids are client rendering data the server never needs (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED (bookkeeping)** — Material 65532 is already FOUND (= interior air, ruin-stamping.md §1, VoxelTemplate.InteriorAir). Only the .ocm grids and "client without the files" stay open. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — Notes below. (trace/0695/agent-leads2-b.md)
- **Sweep 2026-10-17: ADVANCED** — The client treats materials 1..0xFFFC as solid (FUN_143f9cc80, FUN_144003140), so 65532 is the top of the solid range (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what the u16 texels of ContinentSurfaceCache (low byte 0-~65, high byte multiples of 4) and the second
  ocean grid / four int-pair lists mean; what a client (0.589) does when ContinentOceanMap / SurfaceCache for its
  continent is missing (FUN_144000110 asset request); what voxel material 65532 (0xFFFC) is in the ruin templates
  (hundreds of thousands per file; e.g. hadder_bunker 860,245).
- **Searched:** readers FUN_14407b070 / FUN_14407b810 / FUN_14407c470 / FUN_14407c370 (layout only), FUN_144000110
  (only "Unrecognized userTypeId" log); LandmarkEmu docs / code for 65532 (none).
- **Found 2026-10-16 (.csc) [P layout, I meaning] → [event-islands-695.md](event-islands-695.md) §3, docs/terrain.md
  "Surface cache":** LOD k texel = column of global cell (x + c, z + r), cells 16 · 2^k m; u16 = low | count << 10 =
  the ground lies in cells y + low … y + low + count (bits 8-9 never set). Rule from the server's 2 m bakes reproduces
  `low` on 95.5-98 % of texels for all 28 baked landscapes; each LOD is computed from the terrain (2 × 2 merge only
  96-97 %). Open here: the .ocm grids / lists, the client without the files, material 65532.
- **Try next:** users of VoxelContinentSurfaceCacheAsset LodLevelSurfaceInfo and COceanLayer; FUN_144000110's failure
  path; the block decoder / compositor (FUN_143f5ce70) for 0xFFFC.

### L17. Where PaletteCollectionMeshes gets its entries
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Already closed 2026-10-16 (client-only palette). (trace/0695/agent-leads3-b.md)
- **Need:** proof that the Mesh Shapes palette is client-only (no unlock / ownership data from the server).
- **Found so far:** FUN_14374e110 (new) iterates manager DAT_145437390 (entries 0xe0 bytes, +0x2398 / +0x23a0) and
  creates MeshPaletteEntry objects; selection never sends (terrain-695.md §5).
- **Found 2026-10-16 [P] → [mesh-shapes-695.md](mesh-shapes-695.md) §4:** DAT_145437390 is the VoxelClientImpl
  (ctor FUN_14314bda0); its +0x2390 `Array<VoxelShapeDefinition>` is filled by VoxelFarmCore::InitVoxelStructures
  FUN_14349d440 (part_0106 30286-30337) from the VoxelShapes definition manager (list at +0x868; "VoxelShapesManager is
  not available" otherwise), which loads `VoxelShapes.txt` (part_0162 10456). Entry writer FUN_1437ac720 exposes only
  ImageSetId / EntryType "Shape" / name / Enabled (admin level gate). Client-only; nothing from the server. Closed.

### L18. EditablePropertyValues.txt: the drop-down option table (storytools-695.md §2, §4)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN [P]):** EditablePropertyValues.txt is also absent from the live manifest. The live client never had it, so drop-down options always came from the server's EditSettingsReply. Our option values are legitimately the server's own. (trace/0695/agent-leads3-b.md)
- **Need:** the option values (and defaults) of every drop-down setting: Story Node Mood / Stance / Emote / Text
  Location / Weapon Visuals, spawner Reaction / Combat Tactics / Spawn With Weapons Equipped, Linking Area Allow Voxel
  Destruction (and its "blocked" option's text id). EditableProperties VALUE_DEFAULT on drop-down rows (20 = 89,
  21 = 81, 22 = 51, 25 = 4 in 0.589 / 5 in 0.695, 40 = 109, 41 = 106) does not match LandmarkEmu's values, except the
  text display modes (4 bubble, 5 bubble + chat in FUN_142f859b0).
- **Found so far:** both clients load it (0.695 EditablePropertyValueDefinition loader FUN_143dc1690, columns
  IS_DEFAULT, NAME_STRING_ID, VALUE, EDIT_PROPERTY_ID; file name PTR 0x1452fbca8 → "EditablePropertyValues.txt";
  0.589 part_0151 of re/decomp_all), neither ships it.
- **Searched:** re/out/fileindex_589.tsv / fileindex_695.tsv (only EditableProperties.txt), both Resources folders,
  raw Landmark64.exe 0.695 (only the file name and column names), re/decomp_all and decomp_all_695.
- **Try next:** who reads the manager (IF Gate / settings window code, FUN_142fb71f0) to see whether values double as
  AnimationSelection ids; runtime capture of a 0.589 / 0.695 settings window against our server; pack raw scan for
  "EDIT_PROPERTY_ID"; forum / wiki screenshots of the Story Node window.

### L19. Server-only setting rows and labels (storytools-695.md §4.4-§4.12)
- **CLOSED 2026-10-17 (built 70f7d05):** Combat Node ability rows (Ability N text rows from the linked spawner). [I]: row keys, "Secondary Ability" wording.
- **Pass 4, 2026-10-17: BUILD (Combat Node ability rows)** — The read-only TextLine row (max 0 / min 0) is proven and already used for row 106; FUN_142fb71f0 draws the drop-down list before the plain rows [P], i.e. "Combat Tactics … Below this … a list of the creature's abilities" (P6157). Spec B2 (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED [P]** — Dev post: the IF Gate rows are labelled "If" and "Is". Patch notes + wiki: the Combat Node's settings show Combat Tactics, then the linked creature's ability list. Our server sends the Combat Node no rows (gap). Built: IF Gate labels + Combat Tactics row 88fa629 (ability rows open). (trace/0695/agent-leads3-b.md)
- **Need:** the rows the live server added to settings windows with no EditableProperties row or no text id: IF Gate
  property / comparison / value (comparison words "Equal To"..., P6496), the IF properties of doors and lights (P6494),
  Server Time Node "Desired Time 1-6" (help page, wiki), creature health / damage settings (P6428; "Damage % Multiplier"
  577652 has a text but no row), Combat Node ability rows (P6152), which prop owns row 39 Enable Combat AI Movement.
- **Found so far:** 0.695 drop-down options carry a free string label (FUN_142c1eae0) and the setting record a free
  label string, so the live server could label rows without text ids.
- **Searched:** text_695.tsv (no "Equal", "Greater", "Less Than", "Desired Time"), raw 0.695 exe (no "Equal To",
  "Greater Than", "Less Than", "Not Equal"), as3_695 (InstancePropertyEditor, RulesetObjectSettings), HelpPages.
- **Try next:** forum threads / dev posts and videos of the IF Gate and creature settings (design/forum), 0.695
  settings-window code FUN_142fb71f0 for special-cased keys, runtime capture.

### L20. How the client hides story tools in Play Mode (and shows the Scoreboard / Respawn enabled look)
- **CLOSED 2026-10-17:** the client has no Play Mode rule for props (every read of claim +0x378 traced; Counting Node and Scoreboard differ only in mesh), so live's server hid them [P / I strong]; built (e-playmode: Props.PlayModeVisibility, Respawn Point look) and harness-checked hide / show (agent-playmode.md §1). Open sub-points L20-a..d stay there as notes.
- **Need:** what makes story-tool props invisible in Play Mode while the Scoreboard 3000 and Starting Location stay
  visible, and how a Respawn Point shows enabled / disabled (P6178) — client-side or a server message.
- **Searched:** PropDefs (2901 and 3000 rows identical apart from the name), PropTag / PropTagPropMap, prop-id
  constants (0xbb8, 0xb55...) in both decompiles, CodeStringMappings / tables for "PlayMode" (only
  Claim.NoCameraPlayMode); FUN_142d99b70 returns "PlayMode" for the claim mode (+0x378) — callers not followed.
- **Try next:** callers of FUN_142d99b70 / 0.589 FUN_143485f00; Models.txt / ArtSourceActors for the story models
  (1728 vs 1736); runtime: place a Counting Node on our server and switch modes.

### L21. Score / Victory Node placement, the Game Table's "On Game Ended" Link, Audio / Group Node models
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Everything the client can show is built (3010 / 3011 props, 577664-577692 events / actions, EditableProperties row 109, "On Game Ended" 573011). No 0.695 prop uses models 1742 / 1743, so nothing can place an Audio / Group Node. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED** — The Game Table's prop list (InstancedGame.AddAdditionalProp, ScriptPropId) is the only client path that places a prop with no palette entry. Forum 57990 confirms players placed Victory and Score Nodes, that score changes printed chat lines, and what happened at game end. Built / done: already built (Rework.cs lists 3010 / 3011). (trace/0695/agent-leads2-b.md)
- **Sweep 2026-10-17: ADVANCED** — Live prop list: 3011 Score Node uses model 1744 Points Node and 3010 uses 1745, both with image set 0; no prop uses 1742 / 1743 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** how Score Node 3011 and Victory Node 3010 were placed (no palette category, not in IgPropRoleProps), the
  "On Game Ended" Link (P6517) and which prop owns 577665 / 577666 / 577668 / 577684-577687; whether models 1742
  "Audio Node" / 1743 "Group Node" ever had props; whether model 1744 "Points Node" is the Score Node's.
- **Searched:** 0.695 PropDefs, PropCategoryPropMap, IgPropRoleProps, IgRuleSetPlacementProps, text_695.tsv (no
  "game ends" sentence), Models.txt.
- **Try next:** the 0.695 Game Table editor (RulesetObjectSettings as3, "Game Props" tab), IgGeneralSetting /
  IgRuleTypes rows, forum Game Table threads (29 Sep 2016).

### L22. How "The story on this Build Site has been reset!" (577089) reached players
- **CLOSED 2026-10-17 (pass 4, CLOSE (built)):** 577089 on reset and 577105 on Edit → Play are both sent; the carrier is the server's choice (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED** — The twin text 577105 "The story on this Build Site has been saved!" (Claim.StorySaveSnapshot.Message) belongs to Edit → Play (P5276). The server sends 577089 on reset but never 577105. The Claims snapshot packets 0xF9/0x13-0x16 are terrain snapshots, not this message. Built: 577105 on Edit → Play 88fa629. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED** — The key `Claim.StoryReset.Message` is absent from the exe, Lua and swf, so the server sent the text id. ChatPacketFromStringId is the existing carrier. Built / done: 577089 sent on reset (790f139). (trace/0695/agent-leads2-b.md)
- **Need:** the message route for CodeStringMappings `Claim.StoryReset.Message` = 577089.
- **Searched:** both decompiles for the key string and its one-at-a-time hash (-0x6db67177); text 577089 exists in
  both builds.
- **Try next:** server → client text-id messages the client prints (ClientMessage / chat system text paths), forum
  videos of a reset.

### L23. Firing details the sources don't state (storytools-695.md §6.5, §6.8)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN):** Firing rules are server logic only (pass 3); ours are documented in StoryTools (TASKS 2.34) (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — Firing rules are server-side only; nothing in the client decides them. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED (forum)** — At game end (Sep 2016) the site reset and players were teleported to the start. The Score Node had an "add points to team" Link and a chat message. (trace/0695/agent-leads2-b.md)
- **Need:** Counting Node "+1" at Max / "−1" at 0 (fire anything?); whether a Reset's restart fires spawner
  "spawns" / Starting Location events; "...parse every entity in this area..." semantics (once per entity, entity as
  triggering character?); the target of a Combat Node "perform Ability N"; whether Pause-and-Reset fires "resets".
- **Searched:** patch notes (P5776-P6553), wiki story-tool pages, 0.695 HelpPages/ScriptObj_*.
- **Try next:** design/forum feedback threads on story tools, dev livestream notes, player tutorials referenced in
  the notes (TR3LON1ST, Grox).
- **2026-10-16 (TASKS 2.34, ours [I]):** delayed events keep the chain that set them up — "When this timer
  completes..." runs with the context of the Link that last started the timer, "When the text on this node expires..."
  with the chain that showed the text (its speaking creature as the chain's creature, so P5964's "have him say the next
  text" reaches him through the next node's fallback). Server Time Node slots have no triggering character. A creature's
  death keeps its killer as the triggering character. Whether live did the same is not stated (P6536 only says
  "whichever entity kicked off the action").

### L24. How the live login item list was chosen, and the ids / definitions of items not in it (trace/0695/items-695.md §1.2-1.3)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN):** Our ids are built; definitions exist only in live captures, none on disk (re-open if a capture turns up) (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — Built with our own ids; no capture on disk. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — No design source gives item ids. (trace/0695/agent-leads2-c.md)
- **Need:** why the live 0x4B list (Resources/PacketDumps/44-CommandBase, 2,885 items, 0.695-era) leaves out items the
  client uses (4419, 4521-4523 named by CharacterCreateOptions; category 291 "Pet" has no item in it), and the item ids
  + full definitions of: the 7 pet figurines (texts 577507-577547), the 22 fireworks consumables (577366-577408), the
  Thank You Token (577640), the titles 577697 / 577727-577734, the Colonist's Flight Suits (577737-577740), Holiday
  Sweaters Green / Blue (577741 / 577742), and the older cosmetics also absent (Skeletal Bodysuits, Holiday Sweaters,
  Year Zero outfits, Tech gear) plus 23 recipe-component items missing in both builds. Candidate ids from gaps in the
  dump's id order (4410-4442, 4450-4454 fireworks / parlor; 4499-4507 pets; 4516-4518 token) are unproven.
- **Searched (2026-10-15):** every 0.695 Resources table column named *ITEM* (re/out/agent_items/items695.py), the
  three 0.695-era dumps (44-CommandBase, 145-PropBase, 39-ReferenceDataItemCategoryDefinitions), text_589 / text_695,
  0.695 ImageSets (icons found: 21667-21680, 21689-21711, 21734-21740, 21775-21777), Models / ArtSourceActors, both file
  indexes, re/as3_695 (no item ids), design/wiki (names only), StringHashToValue, patchnotes.tsv. The client's paths that
  could deliver them (0x4A reply, ItemAdd embedded definition, tooltip reply) are traced (items-695.md §2.1).
- **Try next:** other captures of a live 0.695 session (any packet dump holding 0x29 sub 1 ItemAdd with an embedded
  definition, 0x4A replies, or 0x9E CoinStore product lists, whose product entries name item ids); 0.695 cached data in
  any live-era client folder the user may have (Cache/, Logs/ItemDefinitions.log from 2016); Marketplace screenshots /
  forum posts that show item ids or tooltips.
- **Searched 2026-10-16:** the Steam install's Cache/ (empty folders 0/ and 2/), Resources/ImageCache (empty) and Logs/ItemDefinitions.log: only the emulator's own 2026 sessions, nothing from live.

### L25. Fireworks: the consumables' launch effect and the packs' contents (items-695.md §3)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN):** The packs hold 26 Firework_*.fxd (bursts, streamers, Rockets_Launcher_50-80m, Rockets_StraightUp_Gold); the 22 emitter composites 12836-12857 are the only composites that use them [I: name match], which is what Fireworks.Launch plays (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — Built (emitter composite at the player). (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — Only P5913-P5915 (Lumen Station "Fireworks" section). No effect id. (trace/0695/agent-leads2-c.md)
- **Need:** the ability / composite effect a fireworks consumable plays over the player; the reward sets 1124-1129 of
  packs 4444-4449 (grantId; server-only) beyond what the descriptions say; ability 1237 of the Pack of Unstable
  Fireworks (type 4) and its random spread.
- **Searched:** InteractableCompositeEffects / Sets (+44 / +22 rows: emitter sets 129-150, effects 12836-12857 "On"),
  TriggerFXMap (+2: 12950 / 12951), StringHashToValue, the 0.695 Ability* tables (no ability rows; abilities are
  server-defined), the item dump (ability 1237 only on 4443).
- **Try next:** the composite-effect / .fxd files in the 0.695 packs (names with "Fireworks"), the effect ids between
  12836 and 12951 not yet tied to a row, forum videos of a consumable launch.

### L26. Roll Wraps 4406: where it came from, and the record field u59 (items-695.md §1.4, §3)
- **CLOSED 2026-10-17 (pass 2):** Roll Wraps were never released. Recipe 5331's tracker goal 1467 is missing from the Feb 2017 capture, while every released Replicator boot has one. The texts are new in 0.695. No wiki or forum mention. Built: nothing to change (the server can't hand Roll Wraps out). (trace/0695/agent-leads2-c.md)
- **Need:** how Roll Wraps were obtained (recipe 5331 has no CraftingStationRecipeMappings row, CAN_CRAFT_WITHOUT_STATION
  0, DEFAULT_LEARNED 0; other boots are station 53, default learned) and what the definition field u59 (client def
  +0x1d0, read by FUN_142c34a10 at the end of FUN_142c176d0) means: every boot carries one value there (51-69; Roll Wraps
  90), weapons e.g. [50, 82].
- **Searched:** 0.695 Recipes / CraftingStationRecipeMappings, patchnotes.tsv ("roll"), text 577205-577211, the item
  dump field census.
- **Try next:** the readers of def +0x1d0 in the 0.695 decompile (ability / boot ability bar), AbilitySets /
  ItemCategoryStatMappings tables, forum posts about Roll Wraps (Aug - Sep 2016).
- **u59 FOUND 2026-10-16** (trace/0695/cosmetics-695.md §3): u59 = DynamicInputActionSystems id; Roll Wraps 90 →
  state machine 84 → input group 153 → abilities 1207-1214 (all rows new in 0.695). **Still open:** where Roll Wraps
  were obtained (searched again: patch notes, forum archive "roll wraps" — no hit).

### L27. Cosmetic pets: summon / follow / dismiss mechanics (items-695.md §3)
- **CLOSED 2026-10-17:** user decision 2026-10-17: keep the Lop Ear Bunny drop at 2 % (one player report only).
- **Pass 2, 2026-10-17: ADVANCED (small)** — Wiki: unlimited use, one pet out at a time. A player says pets reuse the Repair Bot follower. One player's kill count suggests the Lop Ear drop is rarer than our 2 %. (trace/0695/agent-leads2-c.md)
- **Need:** the ability and buff a figurine uses, how the follower entity is spawned (NPC with Models 1729-1735?
  profile 379 "Non-Combat Entity"?), the buff icon that dismisses it, what happens on a build site (P6145).
- **Searched:** rtti_new_695.txt (no pet class), the 0.695 tables diff (Models +7 pet rows, ProfileDefinitions 379
  renamed, KeywordDefinitions 86 / TagDefinitions 623 / category 291 "Pet"), patch notes P6142-P6147, wiki "Cosmetic
  Pet (List)".
- **Try next:** 0.695 functions using KeywordDefinitions 86 or category 291, the buff / "companion" paths of the
  client (follower movement), forum videos of a pet.

### L28. Inspect reply: unnamed blob fields (trace/0695/packets-695.md §2)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** No 0.695 reader of InspectProxy +0x110 / +0x11c / +0x118 (pass 2 + sweep). Any value is invisible, so 0 / empty is correct. A runtime read would only show our own values. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED** — Radial entry found: RadialMenuButtons row 75 (menu 3 = player, button 7, text 563898 "Inspect", Lua `Ui.InspectPlayer`). +0x110 / +0x11c sit at the same offsets in 0.589, and no 0.695 reader turned up (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED** — The 0.695 InspectWidget never shows +0x110, +0x11c or the profile list, so the server can send 0 / empty (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the meaning of the StartInspect blob's u32 at InspectProxy +0x110 and +0x11c, of the profile list
  {u32 key, u32 value} (array +0x370), and which right-click entry opens Inspect (RadialMenuButtons +2).
- **Searched:** blob parser FUN_143089c10, InspectProxy vtables 144701380 / 144701468 (no reads of +0x110 / +0x11c),
  data-source filler FUN_14308b5c0 (`Character.InspectData` does not show them), 0.589 parser FUN_142fe9cb0.
- **Try next:** InspectWidget.swf ActionScript (JPEXS export), the actor profile copy FUN_142de67a0, RadialMenuButtons
  rows and their handler, runtime capture.

### L29. Buying a Design's missing materials: the result and FoundLumenRecipeForAllItems (packets-695.md §3)
- **CLOSED 2026-10-17:** +0x20 is never on the wire (the constructor zeroes it, the reader skips it) and no UI reads it; nothing to send. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND (part)** — +0x20 is never on the wire (the constructor zeroes it, the reader skips it) and no UI reads it (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what the server sent after PurchaseMissingTemplateMaterials (0xF0/0x1F) and how
  FoundLumenRecipeForAllItems (response +0x20) was meant to arrive.
- **Searched:** Template S→C dispatcher FUN_14310bbf0 (no sub after 0x1E), response reader FUN_143100050 (does not read
  +0x20), handler FUN_14310c290, SelectedTemplate.lua (GetResources is a stub).
- **Try next:** TemplateConfirmationWidget.swf (HUD 219) ActionScript; inventory / currency packets around a purchase in
  any capture; patch notes / forum posts on "Get Resources".

### L30. Story Node question bubble: DisplayPropText's new fields and the dialog id (packets-695.md §4, §B2)
- **CLOSED 2026-10-17:** the NpcDialogResponse HUD indicator's Yes / No send PropPacketDialogResponse {u64 question id, bool}; built (Props / StoryTools). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — NpcDialogResponse HUD indicator (HudIndicators row 29): Yes/No send PropPacketDialogResponse {u64 question id, bool} (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** which 0.589 DisplayPropText u32 became which 0.695 field (new u64 +0x20, u32 +0x48), and how the speech
  bubble's Confirm / Deny pass Lua arg 1 to Prop.OnConfirmNpcDialog (actor +0x1280 = packet +0x18 is a guess).
- **Searched:** reader FUN_142c27f40, dispatcher case 0x78 of FUN_142cdfa60, FUN_142f859b0, FUN_142ebfcb0,
  FUN_142ece660 (`Character.ActiveChatBubbles` only gets PositionX/Y there), FUN_1434445a0; no other reads of +0x1280.
- **Try next:** SpeechBubbleWidget.swf ActionScript, other writers to `Character.ActiveChatBubbles` (part_0074 ~26315),
  HudIndicatorDefinitionNpcDialogResponse / NpcDialogResponseIndicator (part_0104 ~17385), runtime capture.

- **Partly found (2026-10-15, coordinator):** field mapping. Dispatcher case 0x78 (0.695 FUN_142cdfa60 → FUN_142f859b0,
  0.589 FUN_142c45fa0 → FUN_142ee6ce0): both handlers look the prop up from their third argument, which is packet +0x18
  in 0.589 and **+0x20** in 0.695; both read the mode at string+0x18 (the first u32 after the text) and take the second
  u32 as seconds (×1000). So 0.695 = {u64 question id (+0x18, new: handed with the bool to the bubble FUN_142ebfcb0),
  u64 prop, str, u32 mode, u32 seconds, bool question}. Built in Props.WritePropText. Still open: how the bubble's
  Confirm / Deny pass the question id back (PropPacketDialogResponse u64).
### L31. Game Finder: MatchmakingUpdate u64, joining a found match, JoinTeam answer (packets-695.md §5b, §5c)
- **CLOSED 2026-10-17 (pass 2, FOUND (client side)):** The client has no other way to move a matched player: the update's u64 is never read, "Match found" only plays a sound, and the S→C Request knows types 4/5/6. JoinTeam has no answer packet. The server already does both (trace/0695/agent-leads2-a.md)
- **Need:** the u64 at MatchmakingUpdate +0x18, what moves a matched player into the game, and what the server sent
  after RequestJoinTeam (Request type 9).
- **Searched:** handler FUN_1434e9a50 (does not read +0x18), Request handler FUN_1434e93e0 (types 4/5/6 only),
  dispatcher FUN_1434ee920.
- **Try next:** GameFinder.swf, DropdownDialog.lua callers, the TeamMembers handler (0.695 sub 0x15), patch notes
  29 Sep / 14 Oct 2016, captures.
- **2026-10-16:** the cross-world Game Finder counts are built (social-service.md §9). A match on another world is
  only reported to the player (system line + log "moving between worlds: L31"); the move itself waits for this lead —
  also look at how a world change is started from the server (Travel / ClientBeginZoning with another world id,
  event-islands-695.md) and whether 0x1A's unread u64 or Request type 6 carried a world.
- **2026-10-16 (B.10):** a server-started world change exists now: Game/CrossWorld.cs hands a connection to another world's zone on a MultiWorld gateway (HandOver 0x25 + transfer attach + re-zone with the new world id; L114). Still missing for a cross-world match: which landscape / site the other world's game is on (MatchmakingUpdate carries only world + game), and carrying the search over (A's searcher ends with the session; B would have to resume it and match locally). Live's way is still this lead.

### L32. Game Table rework: unnamed fields (packets-695.md §5e, §B6, §B7)
- **CLOSED 2026-10-17 (pass 2, FOUND):** The last unnamed fields: the settings value's u32 +0x50/+0x54 and the per-block string list are only copied back on Apply. PropState's new u64 = the free-for-all controller's player guid. PropState / PropInteraction drive the "InstancedGameObject" overhead indicator, which we never send Built / done: PropState built (5ae6bdd). (trace/0695/agent-leads2-a.md)
- **Need:** AddOrRemoveRule's u64 (manager table +0x3d8 keyed by rule id), EventPacket's new str, str, u32, u32,
  GetConfigurationResponse's new settings u32, per-block string list and per-trigger u32 + u64, HUD entry's new
  bool + u64, PropState entry's new u64, PropRecipe item / PropRequest new u32.
- **Searched:** FUN_1434f4d60, FUN_1434d6eb0, FUN_1434dbb20 and its nested readers, FUN_1434e98d0, FUN_1434f39a0 (UI
  names only for SettingKey / SettingDescription / SettingValue), FUN_1434d8180, FUN_1434d77c0, FUN_1434d7f90,
  FUN_1434f0f00.
- **Try next:** each packet's handler (FUN_1434e2f70 HUD, the Event handler, FUN_1434e9210 recipe),
  RulesetObjectSettings.swf, the 0.695 IgRules / IgRuleSets / IgGeneralSetting columns (tables-diff.md), captures.

### L33. What the live server did with ClaimsPacketPlayerEnteredClaim (packets-695.md §8)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Built: Claims.Entered, and the reward-chest notice 577634 (RewardChests.cs:66, Claims.cs:419). The client reads no reply. (trace/0695/agent-leads3-c.md)
- **Sweep 2026-10-17: ADVANCED (existing)** — leads-L33-L35-L36.md §L33 has the recommendation (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the server's reaction to 0xF9/0x63 {u64 claim}.
- **Searched:** sender FUN_142dc8ec0 (no reply read), Claims S→C dispatcher FUN_142dbecb0.
- **Try next:** patch notes / dev posts (visitor counts, the "To Serve Luminaries" goals, rating prompts), claim S→C
  packets that only make sense after entry.
- **Built 2026-10-15 (ours, interim):** Claims.Entered validates the claim against the player's position and counts the
  visit (DailyGoals Tourist / visit achievements, deduplicated with the position check); answers nothing. Reward-chest
  lockout notice to hook in when chests exist. Lead stays open for live's real reaction.

### L34. Crimson Parlor: use of EquippedAttachmentReply (packets-695.md §9)
- **CLOSED 2026-10-17:** built: the server sends the equipped attachments (Barbershop.cs). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND (existing)** — Its own entry already says FOUND: send the equipped attachments (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what the barber window does with the attachment list stored at customization manager +0x1360.
- **Searched:** FUN_1430a29d0, FUN_1430a5c60.
- **Try next:** readers of manager +0x1360 (preview with / without gear), BarberShopWidget.swf ActionScript.
- **Found 2026-10-16:** the list is the gear the barber window's preview character wears: the preview rebuild FUN_1430a1e60 (called with manager +0x1360 at part_0083 ~27077 / 27341 / 29122) loads Char_Biped_<race>_BaseActor.adr for the chosen race and hands the list to FUN_142e44540 (actor + 0x75e70) with the appearance — so live sent the player's equipped attachments and the preview showed the current gear. Ours sends it empty (preview without gear). To do: write the equipped items' CharacterAttachmentData (reader FUN_1430a0cc0) — the same records the full player data's attachment list takes.

### L35. Text ids sent in InventoryPacketShowActivationFailure (packets-695.md §10)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** The refusal-text set is complete (pass 2). Only a live capture could show which carrier live used. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED** — No client code holds any refusal text id as a constant. The 0.695-new unmapped refusal texts are exactly 577364, 577416 and 577705-577707, all already built. List of server-only CodeStringMappings keys added (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED (existing)** — leads-L33-L35-L36.md §L35 has the text table (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** which refusals the live server sent (pets, unlockers, reward chests ...).
- **Searched:** only the client handler (FUN_142f202e0 case 0x1E).
- **Try next:** text-new.tsv / text_695.tsv for item-use refusal sentences, item definitions with activation
  requirements, captures.

### L36. WorldEditingPlayEffectPacket: effect ids, the dispatcher gate, header u32 (packets-695.md §11) — FOUND (trace/0695/leads-L33-L35-L36.md: gate +0x153b = UserOptions DemoModeOthers "Show Previews of Others' Build Actions", composite id + box, header unused; built in VoxelDestruction.PlayEffect)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing)):** leads-L33-L35-L36.md; built in VoxelDestruction.PlayEffect. (trace/0695/agent-leads3-c.md)
- **Need:** which effect ids / table FUN_143d57870 takes, what DAT_145431328 +0x153b is (all 0x10C handling returns
  early without it), and the second header u32 (+0x14).
- **Searched:** FUN_1430838d0 case 8, FUN_14307c4d0, FUN_142c38220.
- **Try next:** the object at DAT_145431328 (setting / option name), FUN_143d57870's table, Linking Area destroy / heal
  traces, captures.

### L37. New trailing u32 of the site-game struct (packets-695.md §B5)
- **CLOSED 2026-10-17 (pass 2, FOUND (built)):** FUN_142dc9660 is the only reader of entry +0x15c in the claims and InstancedGame code. The team count is already sent (InstancedGames.cs:445-456) (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED [I strong]** — It is the team count: IgRuleSets DEFAULT_TEAM_COUNT is 2 for every team rule set (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the meaning of the u32 at claim entry +0x15c (CanSwitchTeam needs it ≥ 2; team count is a guess).
- **Searched:** reader FUN_142d9fc20, walk-on window FUN_142dc9660.
- **Try next:** other readers of entry +0x15c, the game rules (IgGeneralSetting NumberOfTeams).

### L38. Sent S→C classes not compared 0.589 vs 0.695 yet (packets-695.md §B9)
- **CLOSED 2026-10-17 (pass 2, FOUND (closed earlier)):** Confirmed: the 0xED changes it listed are built (RewardChests.cs PickUpReply 0xE, PropSettings f64) (trace/0695/agent-leads2-a.md)
- **Need:** a layout comparison for classes the emulator sends whose readers are inline (no reader in the family
  traces or catalog2): PacketChat 0x13/1, ClientUpdate ItemUpdate 0x29/3 and UpdateCalculatedStats 0x29/0x2C,
  Groups 0x2B replies, Encounter 0x2C, Inventory GrapplingHookEquipped 0x2D/0x18, ReferenceData 0x2F/1 and /3,
  Crafting 0x42 (BaseRecipe, SalvagingAddItemResponse), InGamePurchase store bundles / search results / review
  response, QuickChat 0x46, SetLocale 0x5B, Ignore 0x72, Activity 0x8F, Achievement / Tracker initialize 0x92,
  PlayerTitleRemove 0x94, InitializationParameters 0x9F, Skills 0xBC/0xA, Experience 0xBE, Resources 0xC4,
  PlayerUpdateManagedPosition 0xC7, Equipment 0xCB, Hud radial notification 0xDC, 0xDD speech packs / PC data,
  PropFromZoneToClient / EditSettingsReply / AccessReply 0xED, VoxelConstruction 0xF4 (1, 2, 4, 6, 7, 0x16),
  PaletteUpdate 0xF5, Mail ReadMessageResponse, Teleport 0xFC, ChestEnableWindow 0xFF, ToolBase 0x117, Login /
  Gateway external packets.
- **Searched:** re/out/agent_packets closure_check.py / catalog2.py found no reader address for these.
- **Try next:** locate each family dispatcher case in both builds (as done for Claims / InstancedGame / InGamePurchase)
  and diff the inline reads.
- **Status 2026-10-15 (agent #10): closed** — every listed class compared (reader closures incl. ctor-site readers,
  per-case dispatcher comparison of all decoded family switches, zone-case closures, pkt.py layouts): all unchanged
  apart from the PlayerUpdate u32 header, Prop 0xED (EditSettingsReply f64 records / option labels; FromZoneToClient
  list elements; PickUpReply new error 0xE) and the known renumbering. Details: trace/0695/coverage-695.md §3.
  Remaining class ↔ sub gap for classes without compiled vtables: L53.

### L39. Tint unlocks: which knowledge id an unlocker grants (trace/0695/functions-695.md §3, §7)
- **CLOSED 2026-10-17:** answered in leads-L39.md; built. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND (existing)** — leads-L39.md (evidence: trace/0695/agent-leads-sweep.md).
- **Answered 2026-10-15 → [leads-L39.md](leads-L39.md):** tints = ArtTint ids in set B (type 4, +0x7ae48, vtable
  +0x10); options = CharacterCreateOptions ids in set A (type 3, +0x7ae30, vtable +0x8); wire u16 0x11C, u8 2, u32 n,
  n × u32, u32 type. Built in Game/Unlockers.cs. Left open there: success notice, Marketplace products.
- **Need:** what the live server added to the customization-knowledge set (player +0x7ae28; SendSelfToClient list /
  0x11C sub 2 AddKnowledgeResponse type 3 or 4) when a player used a tint unlocker (Fantasy Parlor Pack, Cowboy Hat /
  Ninja Mask / Samurai Helmet unlockers), so that the 0.695 barber window lists the tints.
- **Found so far:** the tint list FUN_1430a4720 shows a tint if admin, ArtTint DEFAULT_UNLOCKED (+0x40), knowledge
  `contains(tint id)` (vtable +0x10 of player +0x7ae28), or a store bundle sells one of the tint's unlock items
  (FUN_1430a3210 → FUN_143098500: ArtTintGroupUnlockTint → ArtTintGroupUnlock.UNLOCKING_ITEM_ID). Not found yet:
  whether the knowledge entry is the tint id, the unlock group id or the item id on the wire, and whether type 3 or 4.
- **Searched:** FUN_1430a4720, FUN_1430a3210, FUN_143098500, FUN_14368a830 (= 0.589 FUN_1435d7a50), FUN_1430a29d0,
  the ArtTintGroupUnlock loaders FUN_143c6be40 / FUN_143c6c260.
- **Try next:** the knowledge store's `contains` (vtable of player +0x7ae28) to see which set type 3 / 4 feed;
  items-695.md unlocker items and their activation; BarberShopWidget.swf ActionScript; patch notes on the Parlor packs.

### L40. Daybreak Designs categories: what ExportedTemplateCategoryMap.TEMPLATE_ID matches (functions-695.md §2)
- **CLOSED 2026-10-17:** TEMPLATE_ID matches TemplateBasicData +0xec (spec-design-categories.md); built. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND (existing, built)** — Matches TemplateBasicData +0xec (spec-design-categories.md) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** which template field the map's TEMPLATE_ID (6 … 1324; category 1 "Starter Castle" 28 ids, category 3
  "Collected" 148 ids) is compared with when the palette files a Design under a category, so the server can send the
  Daybreak Designs with matching ids; and whether ExportedTemplates / ExportedTemplateSide (.otd file names, faces,
  attachment types) are used by any client feature.
- **Found so far:** FUN_1437546f0 builds the categories (0.589 FUN_1436966c0 had a fixed "Template.Category.Daybreak")
  and stores each category's TEMPLATE_ID set (category entry +0x50, FUN_142ea6f90); the ExportedTemplate loaders
  FUN_143de3af0 / FUN_143de3e00 have no caller outside the definition managers.
- **Searched:** FUN_1437546f0, FUN_143752e00, FUN_143753360 / FUN_143753450 (map loaders), the ExportedTemplate
  managers (143de1440-143de4570).
- **Try next:** readers of category entry +0x50 (the palette's per-entry category test), TemplatePaletteEntry
  FUN_1437ad920, the template ids the 0.589 server data uses for Daybreak Designs, DesignsWindow.swf ActionScript.

### L41. New / changed functions with no system evidence (functions-695.md §0)
- **CLOSED 2026-10-18 (out of server scope):** function labelling only; no server behaviour can come from it (pass 4). The relabel path (Ghidra re/Landmark695 xrefs for the 443 still unlabelled, .rdata jump tables) stays in functions-695.md §0 for the RE notes.
- **Pass 4, 2026-10-17: KEEP-OPEN (no server value)** — Function labelling only; next path in §2 (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED** — 141 of the 584 `how = none` functions resolve through 1-3 caller levels. 74 of those are dispatcher noise. 67 usable relabels [I] in §2. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED (3 relabels)** — FUN_142ca4ce0, FUN_142c30e20 and FUN_142ec4be0 relabelled; see below. (trace/0695/agent-leads2-b.md)
- **Need:** a system for the 272 functions classify.py could not label and a check of the 1,851 labelled only by
  address locality (re/out/agent_funcs/final.tsv, `how` = none / locality±n).
- **Searched:** RTTI vtables (rtti_695.tsv), vftable references, source-file / Class::Method strings, Lua bindings,
  direct callers and callees (classify.py); the low-address static initialisers were set aside as noise.
- **Try next:** data references to the functions (vtables Ghidra did not type, jump tables), callers two levels up,
  the Ghidra 0.695 project's xrefs (re/Landmark695), runtime hit tracing for the ones on hot paths.

### L42. Goal tiers missing from the achievement capture (trace/0695/wiki-content-audit.md §3.3)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — Record ids exist only in captures; built from the wiki. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — The wiki tier pages are already built 1:1 (Achievements.Tiers.cs). Record ids exist only in captures. (trace/0695/agent-leads2-c.md)
- **Need:** full AchievementData records for the 28 combat tiers 575-617 (children of 595 Slayer of All the Things and
  618 There Can Be Only One, named in the capture's objective → child map) and 13 more goals with client text but no
  record (574675 I Make the World, 574677, 574693, 574616, 574619, 575191, 575192, 575194, 575195, 575198, 576237,
  576265, 576271): objective ids, text ids, description, icon, reward bundles; and how / when the live server sent
  them (client AchievementAdd = AchievementBase 0x92 sub 2, FUN_142d92750 case 2 → FUN_142d8c370 / FUN_142d8f4a0).
- **Searched:** 71-AchievementBase (898 records; decode_achievements.py map field), re/out/achievements.tsv, client
  text 0.589 / 0.695 (names and objective texts present), wiki goal pages (counts, Lumens, score, rewards, "Unlocks …"),
  0.589 Achievement* tables (categories / subcategories / quality / trophies only).
- **Try next:** other packet captures or logs with sub 2, the sub-2 reader's layout (#3), objective / description text
  ids next to the names in the text table, forum screenshots of the Journal.
- **2026-10-16 (TASKS 6.57):** 576237 Getting Crafty, 576265 Building Block Basics and 576271 Modular Thinking are built
  (Game/Achievements.Tutorial.cs, ids 9001-9003, goals-audit-695.md "Tutorial goals built from the wiki"): after 685 in
  the Getting Started chain (the captured records' predecessor field h3[6], and the captured player had not finished
  685) [I], steps in order, wiki counts / rewards, Modular Castle designs from Building Block Basics and Architecture
  Tools from Modular Thinking (patch notes, forum 57063). What is still not proven about them is in L112.

### L43. Replicator tracker achievements and VISIBILITY_REQUIREMENT_ID (wiki-content-audit.md §3.2, Hand-offs)
- **CLOSED 2026-10-18 (FOUND [P], built 1a5b831):** the client never reads VISIBILITY_REQUIREMENT_ID (plain int at recipe def +0x40, no reader; agent-l43.md). It hides recipes from a server-sent set: SendSelf recipe block 3 (FUN_142c1b810) and RecipeBase 0x1F / 0x20 (FUN_142f39870); the window drops them (FUN_143611630) and its start paths refuse them. Built: owned unlocks hidden [I rule]; records now carry ALLOW_CATALYST, TRACKER_ACHIEVEMENT_ID (IsTracking column) and MAX_QUEUE. VERIFIED round 69 (Frida on FUN_143c53c70): after using item 3330 the client found 2003 in its hidden set.
- **Pass 4, 2026-10-17: NEEDS-RUNTIME** — Server refusal built (Crafting.cs:143 UnlockRefusal). Only the client's reader of recipe def +0x40 is open: T1 in §3 (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED [P data]** — VISIBILITY_REQUIREMENT_ID is set on exactly the 581 Lumen Station "unlock" purchases (CAN_CRAFT_WITHOUT_STATION = 1, MAX_QUEUE 1): 555 "Recipe:" items, 22 firework emitter recipes, the Dungeon Builder's Starter Set, the 3 store expansions. It is one id per recipe, numbered in recipe order. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED (tracker part FOUND, already built)** — Dev, 20 Apr 2015: a recipe achievement completes by crafting the recipe once and gives no reward. Already built as TrackerGoalsCrafted. VISIBILITY_REQUIREMENT_ID stays open. (trace/0695/agent-leads2-c.md)
- **Sweep 2026-10-17: ADVANCED (small)** — Stored at recipe definition +0x40 (FUN_143cb6e00); reader not found (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what completed the 186 "Replicator" subcategory achievements (each the TRACKER_ACHIEVEMENT_ID of the recipe
  making the item it is named after; objective = item name, required 1), and what Recipes.VISIBILITY_REQUIREMENT_ID
  refers to (not achievement ids: 512 of 513 values that hit capture ids name an unrelated achievement).
- **Searched:** Recipes.txt (0.589 / 0.695), the capture, re/out/achievements.tsv, Requirements.txt (0.695: no rows
  1394 / 1424 / 1467 / 1563-1584).
- **Try next:** the client's tracker UI path (ObjectivesController / Replicator "track" button) and the recipe window's
  visibility check in the decompile; a runtime capture of tracking a recipe.

### L44. How the non-elite Barren creature props were unlocked (wiki-content-audit.md §2.7)
- **CLOSED 2026-10-17 (user decision, built 4afe1d7):** unlocked by twelve "Recipe: Barren …" drop items (texts 574376-574399; Game/BarrenRecipes.cs); drop rates ours [I].
- **Pass 2, 2026-10-17: ADVANCED** — Dev: "start with a set of basic creatures, unlock more". The wiki's innate creature list has no Barren. Flameblade comes from the Dungeon Builder's Starter Set. (trace/0695/agent-leads2-c.md)
- **Need:** the source of the 12 non-elite Barren creature props (items 3156-3159, 3161-3164, 3167-3170; props
  2445-2459 without the elites) the wiki marks "Innate: No";
  no "Recipe:" item names or grants them (only the four Elite Barren recipes 3561-3564 exist), and the Dungeon
  Builder's Starter Set gives only 2 Flameblades and 1 Bowman (ItemUses.cs:85-96).
- **Searched:** dump names and grantIds, Recipes (no type-2 recipe for those items), Lumen Station recipes, the wiki
  pages and "Innate Creature Props (List)", patch notes (P4166: creatures unlocked by recipes).
- **Try next:** forum posts / videos of the Lumen Station creature section, 0.695 recipe tables, goal rewards of the
  "<creature> unlocked" goals.
- **2026-10-16 (item-sources-audit-695.md §3.2):** the 0.695 tables have no recipe for them either (no type-2 recipe,
  no Lumen Station row, no grantId). New evidence: the client text (0.589 and 0.695) holds "Recipe: Barren Bowman" …
  "Recipe: Barren Stormshot" names 574376-574387 and "This recipe will teach you how to place the Barren … on your
  Build Site." descriptions 574388-574399 for all twelve, so the recipe items were authored, but no item record of the
  live item list (44-CommandBase) uses those texts; and goal 478 Cry Havoc! asks to "Place the Barren Flameblade
  character from your Props tab" from the start of the Getting Started chain. The server keeps them innate (the
  client's data). Still to try: the item-definition ids next to the Elite Barren recipes 3561-3564 in other captures,
  forum posts on Barren recipes after launch.

### L45. Wiki pages not found under their names; riddle goals (wiki-content-audit.md §2.8, gap 5.10)
- **CLOSED 2026-10-17 (pass 3, FOUND (4 of 5)):** Dark Desert Succulent (+ With Butterflies) = props 275 / 392 "Drylily Plant" (live internal name "desert succulent (dark with butterflies)"; wiki gallery caption). Glowing Jungle (Triple) Pendant Plant = flora OrbSingleGlow01 / OrbThreeGlow01, never a prop. Bowl of Apples: no model exists. Large Square Knotted Rope: model only, no prop. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED** — Rusty Sifter = Rusty Sifter (Small). Coiled Hanging Chain = Coiled Chain (Brass). The riddle goals are already built. (trace/0695/agent-leads2-c.md)
- **Need:** the item / prop behind Bowl of Apples, Coiled Hanging Chain, Large Square Knotted Rope, Dark Desert
  Succulent (+ With Butterflies), Glowing Jungle (Triple) Pendant Plant, Rusty Sifter (candidates in content.tsv); and
  the triggers the wiki marks "?" or "???" (Iron From Ice "Wood of iron burning", Ice Cold Brewskies, Better Than
  Reality, Hashtag Mutant!, Gosh It's Hot "Craziness in a volcanic area", Venison?, the riddle Likes).
- **Searched:** dump names (0.589 + 0.695 text), PropDefs both builds, client text, fuzzy / word-set matching
  (re/out/agent_wiki/audit.py), the wiki pages' Other Information.
- **Try next:** prop images on the wiki pages vs model names (145-PropBase), the wiki page histories in the dump,
  forum goal guides.

- **2026-10-16:** Iron From Ice is a Link goal (description 574800 "Link anything to this most special of magic fire
  colors"; its name sits in the Link-goal block 574756-574760): one of the seven Magic Fire Mote Emitters (567308-567323).
  Which colour "wood of iron burning" means: still open (burning iron's flame colour is orange-gold — a hint, not proof).
- **2026-10-17 FOUND (Iron From Ice, inferred strong):** "Iron From Ice" is episode 1 of Telltale's Game of Thrones
  (House Forrester of Ironrath, https://ttgot.fandom.com/wiki/Iron_From_Ice); in that setting ironwood ("wood of
  iron") burns with a blue flame (https://awoiaf.westeros.org/index.php/Ironwood). So the colour is **blue**: a Link
  to or from Magic Fire (Blue) (prop 1883, text 567308) or (Small Blue) Mote Emitter counts 964 (d2327b4). The other
  L45 items stay open.

### L46. Titles with no known goal: Strip Miner, Deforester, Curator (wiki-content-audit.md §3.5)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN [P]):** No goal of the 898 in the Feb 2017 capture grants 3925 Strip Miner, 3926 Deforester or 4363 Curator (achievement and objective bundles scanned); the "…ium?" / "Teak" capstones 642 / 644 give 80 Lumens only. The server awarding none is right (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — — (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED** — Curator = goal 576977 "My Life, It Used To Be Incomplete" / "Swap Meet Established" (never live). In 0.695 "Curator" 577736 sits among the giveaway titles. Strip Miner / Deforester = the "Obtain …ium?" / "What About Teak?" capstones (I). (trace/0695/agent-leads2-c.md)
- **Need:** which goals awarded title items 3925 Strip Miner, 3926 Deforester ("finally got enough burled wood!") and
  4363 Curator.
- **Searched:** capture reward bundles, the wiki dump (no page names them), client text around the title names.
- **Try next:** announcement texts near 575528-575546 (Achievements.cs AnnouncedTitles comment), forum posts.

### L47. The .cpa sections after the tile records, and its manual template placements (architecture-leads.md, Caves)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN (islands)):** On the 0.695 islands every manual record is (rot, 4, 76) = the cavern graveyard, built (0fb9cc4). Ids 61 / 75 occur only in the v12 alpha continents 325-384, which the server does not serve (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED (+neg)** — Template ids 61 / 75 / 76 are in neither VoxelSystemTemplates (2-30) nor ExportedTemplates (2-41). Built: graveyard template stamped 0fb9cc4. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED (major)** — The client never uses the manual templates (their only collector has no reference), so the server must stamp them. LayerInfo, CavernInstanceData and the manual-template record decoded across all .cpa files (trace/0695/agent-leads2-d1.md)
- **Need:** the layout and content of CavernInstanceData, LayerInfo, CaveTileObjectGaps / CaveRoomObjectGapData and
  **ManuallyPlacedExportedTemplateData** in ContinentCavePlacements[N]_*.cpa — which exported templates (cavern ruins,
  outposts?) were placed into caves statically, versus the Environment Server's ruin-layer jobs (also feeds L12).
- **Found so far:** class names (RTTI); CavePlacementAsset ctor / arrays at 0.695 part_0161 ~28265-28900; loader
  FUN_143ff1310 / 0.589 FUN_143f2d420; the emulator (CavePieces.cs) reads only the 39-byte tile records.
- **Try next:** the readers of those arrays (callers of the Array vftables in part_0161), then parse every .cpa.

### L48. Layouts and transport of the server-only messages (architecture-leads.md L8)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Need:** fields of ZoneAddClaim / ZoneAddAllClaims, ConsumeAttachmentZoneRequest / Response, OffGridPasteZoneRequest,
  EnvServerResponse, Spawn / DespawnExportedTemplateRequest, WorldKick*, LockStatusQuery / Reply,
  InternalConnectionBypass, GatewayProfilerRegistration, Pathing / EnvServer monitor responses; and whether servers
  used the gateway's internal tunnel packets or direct links.
- **Searched:** RTTI both builds (no classes), the 0.695 decompile outside the id registry (no use), design/forum.
- **Try next:** other client builds (internal / test) that may include server classes; the client-visible sibling
  messages with the same payload (e.g. ClientSelectionAreaRequest vs ZoneSelectionAreaRequest, OffGridPasteClientRequest
  vs OffGridPasteZoneRequest) as a starting layout; developer posts.

### L49. The .vfm cave meshes: submesh meaning and the mesh → .cav voxelisation (architecture-leads.md, Caves)
- **CLOSED 2026-10-17 (pass 2, FOUND):** .vfm planar submeshes = the .cav connection records (92 of 92 match). The 36-index box = the tile's frame (.cav = .vfm shifted so that box is centred) (trace/0695/agent-leads2-d1.md)
- **Need:** what the 36-index (box) and 180-index submeshes mark (connections / caps?) and the transform between a
  .vfm (metres, centred) and its .cav box (e.g. Cap01 .vfm z ±16 m vs .cav z −9.6..19.2 m).
- **Found so far:** full .vfm layout (all 37 parse); same base names and commit minute as the .cav files.
- **Try next:** compare each .cav's air voxels with its mesh (scale 0.4 m, axis swap); the generator's connection
  matching code (FUN_144030970 "connection hash").

### L50. What live did with the GatewayId in CharacterLoginRequest (architecture-leads.md L6)
- **CLOSED 2026-10-17 (pass 3, FOUND (source) [P]):** `GatewayId` is read from the client's main config object with no section, the same place as the launcher arguments `SessionId`, `Ticket`, `Server`, `StationId`, `CasSessionId`; that object parses the command line (`key=value` words) before and after ClientConfig.ini / LocalConfig.ini. So it is a launch argument (`GatewayId=N`), not only an ini key. Live's launcher never passes it in any file we have → 0 Built: comment fixed 88fa629. (trace/0695/agent-leads3-a.md)
- **Need:** whether the login server honoured the ClientConfig.ini `GatewayId` (u32, default 0) sent after the locale
  in CharacterLoginRequest (FUN_1430b3cb0 / 0.589 FUN_14300d920).
- **Searched:** both decompiles (only the read), the installs' ClientConfig.ini (no GatewayId key).
- **Try next:** design/forum and wiki for GatewayId / ClientConfig tweaks; old launcher configs; runtime with a
  non-zero value against our login server.

### L51. CaveManager's cave filter for the underground teleports (architecture-leads.md, Caves)
- **CLOSED 2026-10-17 (pass 3, FOUND [P]):** A "cave beginning location" is a .cpa tile record with byte 32 set. Its type is byte 38: 1 = surface entrance, 2 = Pioneer isolated cavern, 3 = cavern graveyard (Graveyard01, tile 111). Only type 3 goes into the random-pick table: exactly the 81 Graveyard01 pieces of each classic island. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED** — The filter is the cavern record's type byte. AddCave FUN_143f813e0 enters only type-3 caverns in the random-pick tables. (trace/0695/agent-leads2-b.md)
- **Need:** what value GetRandomCaveBeginningLocation (FUN_143f84420 / 0.589 FUN_143ec0890) compares with node +0x60
  (`*param_5`) when choosing a random cave, and what a "cave beginning location" is (entrance, cavern, layer start).
- **Searched:** the three CaveManager functions (no caller or pointer in the client).
- **Try next:** the CaveManager load path (who fills +0xd78 / +0x1480 / +0x14b8: likely the .cpa CavernInstanceData,
  L47); PacketTeleportRandomUndergroundRequest / NextUndergroundReply layouts (agent #3).

### L52. PropBase 0x0A ListReply: is the 0.695-only prop list send justified? (trace/0695/coverage-695.md §4.7)
- **CLOSED 2026-10-17:** the 145-PropBase dump parses byte-exactly with the shared 0.589 / 0.695 reader; the send is fine for both. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — The 145-PropBase dump parses byte-exactly with the shared 0.589/0.695 reader, so 0.589 can read it (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether the captured prop list LandmarkEmu sends only to 0.695 clients (MiscHandler.cs:283-287,
  Resources/PacketDumps/145-PropBase, PropBase sub 0x0A) is readable by the 0.589 client, and what its body is.
- **Found so far:** both builds have the same reader (0.589 FUN_142f0bc80 = 0.695 FUN_142faa9b0, code "same"; Prop
  dispatcher 0.589 FUN_142f1b980 / 0.695 FUN_142fba9f0 case 0xA identical), so "0.589 has no reader" (the comment
  at MiscHandler.cs:283) is not supported by the client code. The dump's first body bytes (u64 guid, then 01 be 07 …)
  were not matched against the reader (header part FUN_142fa8940: bool, u32, u64; element FUN_142fa7e30: str, u32,
  str, str, 6 × u32 …).
- **Searched:** both decompiles (readers, dispatcher cases); the dump's first 48 bytes.
- **Try next:** walk the dump with the pkt.py layout of FUN_142faa9b0; a 0.589 test with the send un-gated (backup,
  test copy).

### L53. Packet classes with no compiled vtable reference: class ↔ sub (coverage-695.md §2, §11)
- **CLOSED 2026-10-17 (pass 3, ADVANCED [P] → CLOSE-BY-DESIGN):** Grief 0xC0 dispatcher FUN_142ee1bf0: sub 1 SetGriefPoints, sub 2 SetGriefLevel (FUN_142ee1850, old → new level texts), sub 3 Reset = header only (zeroes state inline). Method for the rest: per-family dispatcher on demand; every family is unchanged in 0.695 (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — re/tools/packetids.py drops every namespaced id name (`\w+` doesn't match `::`), which loses 135 rows in 0.589. The 0.695 registry FUN_1434c7940 gives 1,397 rows; 23 of the 108 non-PlayerUpdate L53 classes now have a registry sub (trace/0695/agent-leads2-a.md)
- **Need:** the sub-opcode of 176 packet classes (108 outside PlayerUpdate) that appear in both RTTIs but are never
  referenced by vtable in either decompile (header constants not compiled), e.g. Encounter* replies,
  ClientActivityLaunch*, Groups RemoveGroup, Vehicle*, Gaq*.
- **Found so far:** their families' dispatchers / zone closures compare equal 0.589 vs 0.695 (coverage-695.md §9), so
  their 0.695 status is "unchanged"; PlayerUpdate ones are covered through the id table and the per-case comparison.
- **Searched:** both decompiles for `Class::vftable` and the vtable addresses from re/out/rtti*.tsv (no hit).
- **Try next:** match each class's destructor (RTTI slot 0) to the dispatcher case that calls it (sd_*.tsv case
  calls, re/out/agent_coverage), or the RTTI COL / type descriptor references in the exe.

### L54. Tokenised rich text: producers other than the prop property editor (trace/0695/ui-trace-695.md, Chat)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing) + mail checked):** Mail needs no server escaping. The client HTML-escapes subject and message (FUN_142d08920: & < >), and the mail window does not detokenise. (trace/0695/agent-leads3-c.md)
- **FOUND 2026-10-17 [P + runtime]:** the chat window detokenises every line it shows (ChatDocumentClass.detokenizeText), and a /say carrying the tokens reached the client's chat layer unchanged (re/test/chat_trace.js) — players could inject markup. The server now neutralises the tokens in chat, guild MOTD / description and site names (Shared/Game/RichTextTokens.cs). Harness 2026-10-17 on the rebuilt test world: the same /say is relayed as `[font color="#FF0000" size="28"]RED TOKEN TEST[/font]` (world log [Chat]), i.e. plain text.
- **Sweep 2026-10-17: NEEDS-RUNTIME** — — (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** which text reaching Chat / NotificationWindow / SpeechBubbleWidget can carry the `~[~ ~]~ ~a~ ~q~` tokens
  (all three detokenise every line in 0.695): only story text from props, or also chat typed by players, mail, system
  messages; and so whether the server must strip or escape these tokens in player chat.
- **Found so far:** the only producer is LargeTextInput.tokenizeText (re/as3_695/InstancePropertyEditor/scripts/controls/
  LargeTextInput.as 239-250).
- **Searched:** both Landmark64.exe images (byte search `~[~`, `~]~`, `~a~`: 0 hits; `~q~`: 2 hits in each build, 0.589 too, not found as a
  string in either decompile),
  0.695 Resources/Scripts and UI/Default Lua / XML (0 hits), every exported 0.695 / 0.589 swf script (only LargeTextInput and the three
  consumers).
- **Try next:** the unchanged swfs' exports in re/as3 for HTML building of chat input (ChatInput / Hud), forum posts
  on "colored text in chat", runtime: type `~[~b~]~` in chat on a 0.695 client against our server and watch the log.

### L55. SetClaimAdventureFlag "current claim" sentinel (ui-trace-695.md, BuildModeTimeWidget; packets-695.md §6)
- **CLOSED 2026-10-17:** DAT_145476ff0 = 0 [P]; nothing to change. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — DAT_145476ff0 = 0 [P] (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the value of DAT_145476ff0, which FUN_1435e6470 compares the Lua claim argument with before substituting
  the current claim (+0x348). The UI passes 0 (TimePanel.as 405-420); packets-695.md §6 says -1.
- **Found so far:** DAT_145476ff0 lies in .data beyond the raw size (zero-initialised); the 0.695 decompile has five
  reads (part_0113 7740-9307) and no write, so 0 is the likely value.
- **Searched:** decomp_all_695 for every reference to DAT_145476ff0; the exe section table.
- **Try next:** a static initialiser that writes through a pointer (xrefs in Ghidra re/Landmark695), runtime read
  with Frida (rules: never kill the runner with hooks attached), or the packet a 0.695 client sends when a checkbox is
  toggled (claim guid field).

### L56. New Lua bindings with no caller found (ui-trace-695.md, Lua bindings)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN [P]):** None of the five bindings sends a packet (luabind_695 "sends packet" empty); SetEyeSlotIndex only changes the character-select preview value. Whatever calls them, the server sees nothing new (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — Tool.GetCursorSmoothing has a caller: ModalToolBrushOptionDefinitions.txt row 45, ACTIVATED_LUA_CHECK_FUNC. The other five still have none. (trace/0695/agent-leads2-b.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — No caller in any Lua, UI, ScriptsBase.bin or pack (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** who calls CharacterSelect.GetEyeSlotCount / SetEyeSlotIndex (FUN_1435da2c0 / FUN_1435da360),
  Mesh.Populate / GetMeshesFilteredByAvailability (FUN_1436080e0 / FUN_143608160), Ui.Get/SetAmbientVolume
  (FUN_1435ce900 / FUN_1435ce760) and Tool.GetCursorSmoothing (FUN_1436066e0) — or whether they are unused.
- **Searched:** the 29 exported new / changed 0.695 swfs (re/as3_695), 0.695 Resources/Scripts, UI/Default, UI/Admin
  Lua / XML, UI/ScriptsBase.bin, InputProfile_Default.xml (only ToggleCursorSmoothing and GameSettings.*AmbientVolume
  have callers). The unchanged swfs are byte-identical to 0.589, which lacks these bindings.
- **Try next:** Lua / swf inside Resources/Assets packs (re/tools/pack.py index), UI/TestMod, native callers of the Lua
  globals (`FUN_142edbdb0` call sites with these names), CharacterCreate eye slots in CharacterCreateOptions tables.

### L57. Design materials confirmation: how the cost line showed the server's total (ui-trace-695.md)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Settled 2026-10-17 (DesignMaterials re-sends the currencies). (trace/0695/agent-leads3-c.md)
- **SETTLED BY DESIGN 2026-10-17:** the widget draws its cost line at initialize (invalidate "Text", TotalLumens of PointOfSale row 0) and again only on a `Character.Currencies` change (TemplateConfirmationWidget.as 62-63, 89-117, 176-178); FUN_14310c290 writes only PointOfSale. Our server re-sends the currencies right after 0x1E (Game/DesignMaterials.cs, `session.SendCurrencies()`), so the line shows the total whichever way live did it; a re-send of unchanged currencies is harmless. Whether live sent it stays a history question only.
- **Sweep 2026-10-17: NEEDS-RUNTIME** — — (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether the live server followed TemplatePacketPurchaseTemplateMaterialsDataResponse (0xF0/0x1E) with a
  currency update, because TemplateConfirmationWidget redraws its "Total Lumen Cost" line only on
  `Character.Currencies` changes (TemplateConfirmationWidget.as 63, 178; PointOfSale events only invalidate the list,
  173), and the widget opens before the response arrives.
- **Searched:** TemplateConfirmationWidget.as, SelectedTemplateDocumentClass.as 359-365, hudstate.lua
  OnOpenTemplateConfirmation, the 0x1E handler FUN_14310c290 (fills `Character.Templates.PointOfSale` only).
- **Try next:** forum / patch notes 14 Jul 2016 on "Get Resources" screenshots; runtime on a 0.695 client (does the
  line update when the response arrives?); whether FUN_14310c290 or its caller touches `Character.Currencies`.

### L58. Interaction glow missing on the 0.695 client (first in-world test, 2026-10-15)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing)):** Runtime 2026-10-17: the flag arrives; no change. (trace/0695/agent-leads3-c.md)
- **FOUND 2026-10-17 (runtime, re/test/interaction_probe.js):** on 0.695 every flagged guid was in the entity table and its indicator was created (Spire pads, crystals, 81 cave crystals at login) — the flag is not dropped; the 10-15 "missing glow" was timing / a fading outline. No server change.
- **Sweep 2026-10-17: ADVANCED → NEEDS-RUNTIME** — The 0x6B path is identical in both builds; the flag is dropped if the guid is not yet in the +0x50 table (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** why the NPC / player near the spawn shows the purple "interactable" outline on the 0.589 test client but
  not on the 0.695 one, at the same spot with the same server data.
- **Found so far:** the server sent PlayerUpdate 0x6B DynamicInteractionFlagging three times to each session
  (console.log 23:51:45 / 23:53:42); neither build's main PlayerUpdate dispatcher has a case for it (0.589
  FUN_142c45fa0, 0.695 FUN_142cdfa60), both route it to the proxied-packet handler, and the 0.695 one
  (FUN_142d54c10) reads the sub as a u32, matching ClientWire695. Screenshots: 0.589 taken right after loading, 0.695
  about a minute later, so it may be timing (a fading outline) rather than a lost flag.
- **Try next:** screenshot both at the same moment after load; follow FUN_142d54c10's 0x6B branch to the flag write
  (0.589 FUN_142ee6aa0 sets entity +0x1461 bit 0x40) and compare the 0.695 target; Frida read of the flag on 0.695.

### L59. Leaderboards: who called them and what the reply looked like (trace/architecture/world-channel-service.md)
- **CLOSED 2026-10-17 (pass 2, FOUND (negative)):** ClientLeaderboardManager has only a destructor: no reader, no caller (trace/0695/agent-leads2-d1.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — No caller of Leaderboard.* anywhere (Lua, UI, packs) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether anything in Landmark asked for leaderboards, and the layout of the replies.
- **Found so far:** Lua natives Leaderboard.GetLeaderboard (FUN_143527e40 → ProfileStatsPacketGetLeaderboard 0xCA/6,
  send FUN_143257af0, World) and GetPlayerLeaderboardEntry (FUN_143527f50 → 0xCA/8, FUN_143257b80, World);
  LeaderboardPacketGetPlayerStats 0xC6/2 goes on Zone (FUN_143257bf0). The reply ids ProfileStats 0x7 Leaderboard /
  0x9 PlayerLeaderboardRow are in the id table with **no client class or reader** (coverage-695.md 0xCA), and
  ClientLeaderboardManager exists in RTTI (trace/managers.md).
- **Searched:** re/as3, re/as3_695, 0.589 install UI/*.lua / *.xml for "Leaderboard." / GetLeaderboard (no caller);
  luabind.tsv; family files 00C6 / 00CA.
- **Try next:** ClientLeaderboardManager's vtable / data sources (a reader may be inline there); Resources/Scripts and
  UI/ScriptsBase.bin; the pack index (re/tools/pack.py) for leaderboard UI; forum / wiki for in-game leaderboards.

### L60. Teleport queue: what opens the queue prompt and what PostQueueNotify carries (world-channel-service.md)
- **CLOSED 2026-10-17 (pass 2, FOUND (confirmation)):** PostQueueNotify uses only its text id. The queued pair is the client's own last TeleportRequest {world, zone} (trace/0695/agent-leads2-d1.md)
- **Need:** who creates the Game Action Queue item GaqItemTeleportToZoneRequest (vtable 14469e3b8; accept
  FUN_14332d5b0 → FUN_143350e00(…, 1) → PacketTeleportToQueuedLocation 0xFC/6 {u32, u32 from teleport manager +0x30 /
  +0x34} on World; decline FUN_14332f7b0 → PacketTeleportCancelQueue 0xFC/7 FUN_1430c6430, World), what the fields of
  PacketTeleportPostQueueNotify 0xFC/5 mean (reader FUN_1430c2960: u8, u32 × 4; handler FUN_1430c7580 → GameEvents
  OnPlayerTeleportPostQueueNotify), and when live queued a teleport (landscape full? instanced zone?).
- **Searched:** 0.589 install UI Lua (only Dialog.lua:69 OnCancelTeleportQueueButtonClicked → Teleport.CancelQueue; no
  OnPlayerTeleportPostQueueNotify handler), re/as3 / re/as3_695 (no TeleportToQueuedLocation), family file 00FC.
- **Try next:** callers of the GaqItemTeleportToZoneRequest ctor (vtable reference in both decompiles); FUN_1430c7580's
  second half (what it stores at +0x30 / +0x34 and whether it adds the GAQ item); patch notes for "queue" + "landscape".
- **Found 2026-10-17 (E.8):** the travel queue's client side is the login-queue object in state 2: zone packet PacketLoginQueueStatus 0xE0 (FUN_143171ae0 → FUN_14368afa0; same fields as login tunnel type 3 plus four unused u32) opens and updates the queue dialog; the client leaves it only by TeleportToQueuedLocation 0xFC/6 or TeleportCancelQueue 0xFC/7 (FUN_143173a90 / FUN_143171570, both close it with FUN_14368acd0), carrying two u32 from the queue object +0x30 / +0x34 — set by PacketTeleportPostQueueNotify (to confirm). A zone-side hold tried on 2026-10-17 showed the dialog stays open without them.

### L61. Logout to character select without a reply: what the client does at its 60 s deadline (world-channel-service.md)
- **CLOSED 2026-10-17:** the client gives up with "Timed out while waiting for a relogin session." and shuts down; the server answers in time (admin2). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — The client gives up with "Timed out while waiting for a relogin session." and shuts down (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the client's behaviour when PacketCharacterSelectSessionResponse 0xF7 never arrives (the World-channel
  back end down or restarting): FUN_142cbead0 (0.589) sets game +0x324a0 = now + 60000 when it sends
  PacketCharacterSelectSessionRequest 0xF6 on World; FUN_142c38920 tests "deadline set".
- **Searched:** the writers / readers of +0x324a0 in re/decomp_all (FUN_142cbead0, FUN_142c38920; callers at
  part_0073 ~13801, part_0074 ~666, part_0098 ~5655 not followed yet); MODLOG 2026-10-09 logout notes (state 0x15
  WaitingForReloginSession → 0xF7 → 0x16 / 0x17).
- **Try next:** follow the three callers of FUN_142c38920 to the expiry branch; a test in the isolated Docker copy with
  the world-channel container stopped (client log + run state).

### L62. Commerce session ticket and Spice web auth: who asked and where the ticket went (world-channel-service.md)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: ADVANCED [P]** — The windows that would start a Spice REST action (GameCode → `PromotionalScreen.RedeemCode`, StationCashPurchase → `InGamePurchaseStoreScreen.FundWalletWithPaypal / Sms`, CreditCard*) are loaded (UI/Default manifest) but nothing opens them: no Lua, XML, data table or native shows them. Only the in-game web pages could (L137). 0x78 {false} (built) is the right answer for good (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — Commerce session = ticket for 12 Spice REST actions (`https://<host>/rest/commerce/<id>/<action>`). The SpiceWebAuthUrl senders are dead Built / done: 0x77 answered with 0x78 false (ef483fb). (trace/0695/agent-leads2-d1.md)
- **Need:** when the client requests a commerce session (PacketCommerceSessionRequest 0x77, built in FUN_142ca25a0,
  World; called only from ClientInGamePurchaseManager vtable 144666448 slot 15 FUN_143596940) and what the in-game
  browser posts the returned ticket to (handler FUN_143972530: "Total time for session ticket retrieval", adds form
  field "sessionID" to a pending browser request and POSTs it; failure text "Failed to request commerce session.");
  same for PacketInGamePurchaseSpiceWebAuthUrlRequest 0x45/0x2A (sends FUN_1431a3f40 / FUN_1431b9080) → 0x2B.
- **Searched:** decomp callers of FUN_142ca25a0 / FUN_143596940 (none direct: vtable slot), callgraph.tsv,
  docs/showcase.md (the Showcase pages never needed it so far).
- **Try next:** who calls ClientInGamePurchaseManager slot 15 (the browser / CEF glue around FUN_143972360,
  FUN_143978880; slot 9 FUN_143596630 builds a "&service=http%3A%2F%2Fwww.soe.com" ticket URL); runtime: log the
  0x77 send while browsing the Showcase on the real client.

### L63. World-channel requests with Lua natives but no UI caller found: lobbies, mini-game invites, quizzes, requisitions, encounter matchmaking, telemetry bodies (world-channel-service.md)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN [P neg]):** The data-table Lua columns (new space) hold none of the lobby / quiz / requisition / Wall-of-Data natives (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — The match-invitation and mini-game-invite response senders are dead in 0.695 (trace/0695/agent-leads2-d1.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — No caller in Lua, UI or packs (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether Landmark's UI ever sent JoinLobbyGame family 0x67 (Create / Join / Leave / Start / LeaveLobby;
  natives CreateNewLobbyGame FUN_14351f500, JoinLobbyGame FUN_14351f5a0 …), PacketInviteAndStartMiniGame 0xAD/1, /3,
  QuizRequest 0xAF/1, /3 (RequestQuiz FUN_14353e010), RequisitionItems 0x6E (FinishRequisition FUN_1434ff7a0),
  EncounterMatchmaking::MatchInvitationResponse 0x8A/0xA, and the bodies of the telemetry messages
  PlayerUpdatePacketUpdateSteamInfo (FUN_142f3c790, sends when SteamUser's id differs from +0x100), PacketClientLuaMetrics
  (FUN_142cc0b10), WallOfData* (Marketplace FUN_1435162f0 …, UIEvent FUN_14351c5c0), ItemReview::PacketOptInAccount
  (FUN_143539b80).
- **Searched:** re/as3, re/as3_695 and the 0.589 install UI Lua / XML for the native names (no hits for
  JoinLobbyGame, CreateNewLobbyGame, RequestQuiz, FinishRequisition, MarketplaceCategorySelected, SetWallOfData,
  OwnerMatchmakeActivity); luabind.tsv; family files 0067 / 0068 / 00AD / 00AF / 006E / 008A / 010B.
- **Try next:** Resources/Scripts and UI/ScriptsBase.bin (compiled Lua), the pack index, GAQ item classes that call
  the 0x1433509a0 / 0x143350a40 response functions (mini-game / match invitations), runtime log of channel-1 traffic in
  a long play session (console.log "Channel: 1").

### L64. Guild mail: how a player addressed a mail to a guild (trace/architecture/social-service.md §3.1)
- **CLOSED 2026-10-17 (pass 3, FOUND (UI path) [P]):** The only guild "Send Mail" in 0.695 is the guild roster radial menu (RadialMenuButtons row 48, menu type 6, text 564058) → `Guild.OpenComposeWindowForTarget(memberGuid)` → GameEvents:OnComposeGuildMail(member name) → HudStateManager:SendGuildEmailMessage → compose window with To = that member's name. No guild-wide compose exists (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — No guild field and no guild compose UI. Text 564049 implies a guild named as the recipient [I] (trace/0695/agent-leads2-d1.md)
- **Sweep 2026-10-17: ADVANCED [P layout]** — ComposeRequest has no guild field: To names, ToGuid, Subject, Message, COD, attachments (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the compose path that sends a guild mail: mail result 0x11 "player tried to send guild mail and is not in
  a guild" (Mail.Error.Compose.PlayerNotInGuild 564049; result tables 0x14521a980 / 0x14521aa90) and mail kind 0x100
  shown as "[Guild] [*GuildName*]" / "Guild message sent from: [*PlayerName*]" (FUN_143585ac0 → FUN_143589e50, guild id
  in MailData's second u64) prove guild mail existed; what the client put in ComposeRequest for it (ToGuid +0x18 set
  from a data-source "ToGuid", FUN_143588240; a guild name in "To"?) is not found yet.
- **Searched:** 0.589 AS3 EmailComposeWindow / Inbox / EmailMessageWindow / GuildWindow / GuildInfoWindow /
  ContactWidget (no guild mail action; ToGuid only in ClaimFeedback), luabind.tsv (Mail.* natives), CodeStringMappings.
- **Try next:** 0.695 AS3 (re/as3_695) and its luabind; Resources/Scripts and UI/ScriptsBase.bin; slash commands
  (FUN_142ae3120 registrations, dynamic command lists) for a guild-mail command; forum / wiki for "guild mail".

### L65. Public numbered chat channels (General / Trade / Crafting / Newbie): wire identity and join (social-service.md)
- **CLOSED 2026-10-17 (pass 2, FOUND (by agent-chat.md)):** General = public id 0, the only public channel in 0.695; the client sets it up itself (trace/0695/agent-leads2-d1.md)
- **2026-10-17 (agent-chat.md, merged e-chat):** a public line carries a 38-byte block after the message, then on channel 9 a u16 channel id (0 = General, the only one; the client registers General itself as /1). The u64 after the position was read as a string (room lines reached nobody) — fixed. ChannelOn / Off and the bubble colour come only from Lua natives no shipped script calls; built anyway.
- **Sweep 2026-10-17: ADVANCED [P]** — OnAddChatChannel / OnRemoveChatChannel are never called from native code (the names are not in the exe) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** how a line on wire channel 9 (ChatChannels.txt row 10 "Public") says which public channel it belongs to,
  and how a client joins / leaves General: CommandPacketChatChannelOn / Off 0x1E/0x22 / 0x23 {string} on World (Lua
  Ui.ChannelOn FUN_143521850 / Ui.ChannelOff FUN_143521b30), ChatManager.lua OnAddChatChannel(label, channelName,
  defaultCommand, isPlayerCreatedChannel) / OnRemoveChatChannel (install Resources/Scripts/ChatManager.lua 382-391),
  and the unknown string after the position in PacketChat's trailing block (writer FUN_1432a93e0). Dev posts: General
  is a "custom chat channel" (P3672), joinable with server-kept membership (P1826, P1893, P4311), can be disabled in
  Chat Options (P5310).
- **Searched:** re/as3 (no ChannelOn / "General" caller outside soe utils), the 0.589 decompile for
  OnAddChatChannel (no native string reference), strings.tsv ("General" only in settings functions).
- **Try next:** who calls the Lua globals OnAddChatChannel / OnRemoveChatChannel (string hashes or the Lua event
  table near ChatManager), the room manager FUN_14357d8d0 (RoomInfoNotification kind 1 → tab), the chat options UI
  (ChatOptions .swf) for ChannelOn; runtime: Frida log of OnAddChatChannel arguments after a RoomInfoNotification.

### L66. Friend presence fields, friend positions and friend groups (social-service.md §3.1)
- **CLOSED 2026-10-17 (pass 2, FOUND (most)):** Friend row columns, FriendStatus / ProfileInfo layouts, and the 10 s offline debounce proven (trace/0695/agent-leads2-d1.md)
- **Need:** the meaning of FriendData's online block (+0xcc…+0xec incl. the 5 × u32 FUN_14348c770), FriendStatus
  0x4D/9 (copied to the friend's +0x128…+0x138 by FUN_143493e80), FriendUpdateProfileInfo 0x4D/4 {str, 4 × u32},
  FriendUpdatePositions 0x4D/5 {u64, string, bool, 3 × u32} (FUN_143493a70, answer to CommandPacketFriendsPositionRequest
  0x1E/0x18 on World, Ui.GetFriendPositions FUN_1435189e0) — world / zone / position? —; how the stored friend groups
  (FriendUpdateExtraInfos 0x4D/0xB: HashListMap<IString, FriendExtraInfoEntry>, 3 strings per entry, FUN_143498ce0 /
  FUN_14348cd20) came back to the client (FriendData trailing string +0x108?); the time window of the client's
  offline debounce (FUN_143490ae0 stores guid + time at manager +0x78; FUN_143493150 checks it before UI_FriendOnline).
- **Searched:** the friend dispatcher FUN_143493360 and its handlers, ContactWidget / TeleportWidget AS3 (they use
  only FriendName / GUID / Online), luabind (ClientFriends.MoveFriends / CreateGroup …).
- **Try next:** readers of friend +0x118…+0x138 (map / teleport widgets, FUN_143497820 row refresh → data source
  columns), who expires the +0x78 map (the manager's update), MapWidget AS3 for friend markers; runtime with values.

### L67. /who, the pending-mail notification, quick chat ids and chat-service-down texts (social-service.md)
- **CLOSED 2026-10-17 (pass 2, FOUND (b, c, d) + server bug):** NewMailInboxNotification needs a u16 count, or the 0.695 client drops it Built / done: new-mail notification u16 count fixed and client-verified ("You have 1 message.", ef483fb). (trace/0695/agent-leads2-d1.md)
- **Sweep 2026-10-17: ADVANCED (a)** — Wiki and forum: /who lists every player on the current server, with a count (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** (a) /who: 0x1E/0x1 WhoRequest has an id but no client class (only CommandPacketWhoReply, reader
  FUN_1432d6c80, WhoData FUN_1432d8e50 {string, i8, u32, u32, u32, u64}, "%d %s" UI_WhoCommandPlayersFound in
  FUN_14330f560) — which server command list registered /who and what the WhoData numbers are; (b) the Notifications
  0x01 entry for pending mail ("You have [*count*] message(s)", Mail.GAQ.*, FUN_14332b9a0) — type id and fields;
  (c) family / sub of QuickChatSendChatToChannelPacket (FUN_143290f30, hints 0xff / 8) and QuickChatSendTellPacket
  (Zone); (d) whether the client has any text for a tell / guild line that can't be delivered because a back end is
  down (PacketTargetClientNotOnline 0x1A, reader FUN_142bb13b0, is never sent today).
- **Searched:** RTTI (no WhoRequest class), family files 001E / 0046 / 001A, CodeStringMappings (Mail.GAQ keys).
- **Try next:** dynamic-commands.md world command list, FUN_14332b9a0's caller (GAQ item ctor) and its type switch
  FUN_143337930, the QuickChat send templates FUN_14329e500 / FUN_14329e680, the zone dispatcher's 0x1A case.

### L68. When live sent PacketClientServerShuttingDown 0x4C (operations.md)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** 0x4C is only a chat line (pass 2). Keep it out of the character-select hand-over; optional on a final stop (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — 0x4C only prints a chat line (no state change). Live's countdown text came from 0x5F (forum 2014) (trace/0695/agent-leads2-d1.md)
- **Need:** when the live server sent 0x4C (no body; zone dispatcher case 0x21 shows "The server has shutdown and
  logged you out.") — at every stop, only on forced stops, or never.
- **Found so far:** the emulator's shutdown sends players to character select with a fresh session instead (tested
  2026-10-14); 0x4C would undo that hand-over, so it is not sent.
- **Try next:** developer posts / forum threads about restarts quoting the message; captures; other callers of the
  message text (localisation key) in the client.

### L69. Gateway internal packet fields beyond the header sizes (trace/architecture/gateway-service.md §1.4)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** The tracker header sizes are profiler bookkeeping in shared engine code; the client never sends or reads opcodes 7 / 8 / 0x16 (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — The tracker's header table is now read: external 5 / 6 = 1-byte headers; internal 7 = 19 B, 8 = 13 B, 0x16 = 11 + 12·n (n at byte 7) (trace/0695/agent-leads2-d1.md)
- **Need:** the fields of live's internal gateway packets: opcode 7 (19-byte header), 8 (13-byte header), 0x16
  (11 + 12 × n, u32 n at offset 7) — which of 7 / 8 is TunnelPacketFromExternalConnection vs ...ToExternalConnection,
  what the remaining header bytes and the 12-byte ExternalDestination hold.
- **Found so far:** sizes and opcodes from ApplicationPacketTracker FUN_143c7a9c0 / 0.589 FUN_143bba9c0 (table set in
  ctor FUN_143c78d10); 0x16 = ToExternalConnectionList (ctor). Our layouts fill those sizes (gateway-service.md §3.4).
- **Searched:** RTTI classes 0x144949838-0x1449498b8 (only dtor slots), the 0.695 decompile (no other use).
- **Try next:** other client builds with server code; any serialiser for Gateway::InternalPackets in packs / DLLs.

### L70. What live's gateway did when a back end was down (gateway-service.md §1.1, §3.5)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** The client only logs ChannelIsRoutable / ConnectionIsNotRoutable; holding vs dropping is the gateway's choice. Our 2026-10-16 rules stand (trace/0695/agent-leads3-a.md)
- **Need:** when ChannelIsRoutable / ConnectionIsNotRoutable were sent, the meaning of ChannelIsRoutable's second
  bool, and whether client packets for a downed channel were held or dropped.
- **Found so far:** layouts (opcode 7: channel bits, bool, bool; opcode 8: empty); the client only logs both
  (ClientGateway FUN_142df6310 / FUN_142d582a0, FUN_142df6370 / FUN_142d58300); NetInfo.log shows them at runtime.
- **Searched:** both decompiles (listener slots, receive switch FUN_143a4bd20), design/forum (no gateway posts).
- **Try next:** forum / wiki for "routable" or zone-crash player reports; patch notes about zone crashes keeping
  players connected.
- **Ours meanwhile (2026-10-16, gateway-service.md §3.5 notes):** ChannelIsRoutable per channel (0, 1, 3) when a
  channel loses / regains every back end that could take it, second bool false; ConnectionIsNotRoutable only at a login
  with no zone for the landscape (then wait like a hold); packets of an unroutable channel held (256), channel 1 to the
  world-channel service during a zone outage. Change when the lead is answered.

### L71. Who sends the gateway Logout (opcode 3) (gateway-service.md §1.1)
- **CLOSED 2026-10-17:** nobody: FUN_143a4bb50 is dead in 0.695 (only exception-table references). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — Nobody: FUN_143a4bb50 is dead in 0.695 (its only references are exception-handling tables) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the caller of FUN_143a4bb50 / 0.589 FUN_143993c70 ("Logout sent to the server. DisconnectTimeoutMs=%d,
  ForceDisconnectOnTimeout=%d.").
- **Searched:** 0.695 decompile callers (none), rel32 call / jmp scan (none), 8-byte pointer scan (none); one 4-byte
  .rdata reference at 0x145080210 (likely unwind data) — not checked.
- **Try next:** identify the .rdata structure at 0x145080210; the ExternalGatewayApi vtable thunks; runtime (Frida hook
  on FUN_143a4bb50 during Exit Game / logout).

### L72. Expected connections: how the login server announced a client to the gateway (gateway-service.md §1.2, §3.7)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** processExpectedConnectionsOnly is a server-side UdpLibrary option; the client is the connecting side. Our gateway's ticket check in LoginRequest does that job (trace/0695/agent-leads3-a.md)
- **Need:** where UdpParams.processExpectedConnectionsOnly (UdpLibrary, reader part_0133 ~36282) is checked and what
  "expecting" a connection meant (address, ticket?) between live's login server and gateway.
- **Searched:** strings (no ExpectConnection / expected-connection text), the UdpParams reader.
- **Try next:** the UdpManager connect-request path (callers of the params copy, field +0x180 of the params struct);
  other SOE titles' public UdpLibrary notes are NOT to be used as evidence (client only).

### L73. Re-attaching a connected client to a restarted / different zone process (gateway-service.md §3.5, §3.6)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN (rest)):** The restart case is answered and built; zone-to-zone process hand-over is server-internal (names only in the client) (trace/0695/agent-leads3-a.md)
- **Need:** how to make the client drop the entities it holds when a restarted zone re-zones it on the same landscape
  (ClientBeginZoning keeps terrain and entities when zone name and id match), and whether live moved players between
  zone processes without a reconnect.
- **Found so far:** the client connects to a gateway only in run state 0xA (BaseClient vtable +0x118 = FUN_142d53aa0 /
  FUN_142cba700), so every zone change happens under one gateway connection; ClientBeginZoning handler FUN_142d60340 /
  FUN_142cc6ed0 → FUN_142d58cd0 / FUN_142cbf940 (same-zone flag set in Zone_DispatchPacket).
- **Try next:** read FUN_142cbf940's same-zone / different-zone branches (entity teardown), test a re-zone with a
  changed zone name or world id on the test client; dev posts about zone crashes.
- **Answered for the restart case (2026-10-15)** from our own research and a test: docs/terrain.md "Long teleports"
  already had it — ClientBeginZoning on the same landscape builds a fresh terrain engine and drops every entity except
  the player (FUN_142ee4930); entities *streamed during the load* are kept (MODLOG 107 duplicate refusals), so the
  zone holds streams until ClientIsReady. Built on it: the gateway holds the connection, a restarted zone re-attaches
  it and re-zones the client (WorldSession.OnReattach); 0.695 test: zone container killed and started, the client's
  NetInfo.log shows OnChannelIsRoutable 0 → 1 and Begin Zoning, no disconnect, chat and mail work after. Still open:
  whether live moved players between zone *processes* (§3.6) without a reconnect.
- **2026-10-16 (names found):** the zone base registry (0.695 FUN_1434ba530 part_0107 ~8313-8420 / 0.589
  FUN_14340c950) names server-only transfer messages with no client code: TransferPlayerToServer 0x09,
  PlayerTransferServerRequest / Reply 0x0B / 0x0C, WorldInitiatedPlayerTransferServer 0x16, ZoneToZonePacket 0x18,
  NpcTransferServer* 0x50-0x52, ZoneLogin* 0x04 / 0x05 (live-server-architecture.md "0.695" §3). So the engine had a
  zone-to-zone player handover [I]; layouts and whether Landmark used it are still open. A multi-zone build uses these
  names (internal-messages.md §1).

### L74. Gateway command list and live's gateway flood limits (gateway-service.md §3.9, §3.10)
- **CLOSED 2026-10-17 (pass 3, ADVANCED [P] → CLOSE-BY-DESIGN (limits)):** Client side of dynamic commands fully read: source 1 → sent on channel 0, 2 → channel 1, 3 → gateway opcode 9, any other source → nothing is sent; a name already registered by any source is ignored ("Duplicate command from server"). Live's flood limits can't be seen from the client Built: start-up duplicate check 88fa629. (trace/0695/agent-leads3-a.md)
- **Need:** which slash commands the gateway registered (CommandList source 3) besides `/gatewayprofiler`, the layout of
  GatewayProfilerRegistration 0x1E/0x4D7, and any per-connection rate limits live's gateway applied.
- **Searched:** client strings for flood / throttle / rate limit / "too many" (none for the gateway; only
  UdpParams.incomingLogicalPacketMax and the InRate / OutRate / LatencyMs config keys); DynamicCommands texts.
- **Try next:** forum / wiki for gateway or "/gatewayprofiler"; client logs from live players (DynamicCommands.log lines
  "command list from world with %d entries" quoted anywhere).

### L75. Who relayed the channel-3 WorldEditing packets on live (trace/architecture/terrain-service.md §0, §3.3)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Client constraint only: 0x10C is accepted on any channel except 2. Which back end relays is our choice (the zone today) (trace/0695/agent-leads3-a.md)
- **Need:** which back end received channel 3 and sent other players' build cursors (WorldEditing 0x10C) back: the
  gateway (it sees every channel-2 position and sends PlayerUpdatePositionGatewayToClient itself), the voxel back end
  (Environment Server), or the zone.
- **Found so far:** all eight 0x10C senders use channel 3 (0.695 FUN_143086080 Clear, FUN_143085f20 Box, FUN_143086400
  Sphere, FUN_143086280 Shape, FUN_143086110 Selection, Preview / Fill per channels-695.tsv); the client accepts 0x10C on
  any channel except 2 (FUN_142d58570 → dispatch) and draws one effect per builder guid (FUN_142fe3ae0).
- **Searched:** the 0x10C send sites in decomp_all_695; channels-589 / 695.tsv; the gateway profiler strings (L8).
- **Try next:** the monitor's ApplicationPacketTracker header sizes (FUN_143c78d10: 2/6, 0x13/0xd, 7/0xc, 1/1 not decoded
  — one may be a gateway-side relay); forum posts on seeing other players' build cursors (lag, "ghost cursors").

### L76. Environment Server: heal granularity, ruin layers, job types 4 / 5 / 6 / 17 (terrain-service.md §1.1, §4.3, §4.5)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN (a, c) / built (b)):** (a) heal rules from dev posts, built; (b) ruin layers = .cpa LayerInfo kind-2 [I strong], built behind RuinLayers; (c) → L128 (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — Heal rules from the dev posts (already built). Ruin layers ↔ .cpa LayerInfo [I strong] (trace/0695/agent-leads2-d1.md)
- **Need:** (a) how a pending heal was bounded (the destroyed volume, the cells, a box) and whether its due time was a
  fixed 20 min; (b) what a "ruin layer" index means (continent cave layer, tier, template set) and how live chose a
  ruin's cavern and desired count; (c) the work behind job types 4 Cell Temporary Modifications, 5 Cell Generation,
  6 Cell Composite Update Lod, 17 Misc Cell Ownership Reusable.
- **Found so far:** job names FUN_143c74ef0 / 0.589 FUN_143bb4ef0; page FUN_14320ea00 / FUN_143163fe0: per job type
  active / pending, total, per ruin layer {Desired, Active, Failed, Last ID} (16 bytes); dev post 27 Jul 2016 (pending
  heal with a desired time, not cancelled by a new destroy); forum 13 Apr 2016 "Chaos Caverns without ruins".
- **Searched:** the page drawer and the name function; design/forum for "ruin" (2016 threads 56105, 56109, 56901,
  57065); live-server-architecture.md dev posts.
- **Try next:** forum / wiki "Linking Area" destruction thread (27 Jul 2016) in full; Chaos Caverns feedback thread
  57065 replies; the .cpa LayerInfo / CavernInstanceData (L47) for per-layer ruin slots; ClientEnvironmentServerMetrics
  reader (none in retail, L4) in other builds.

### L77. OffGridPasteClientRequest 0xF4/5: send site, channel and body (terrain-service.md §1.2)
- **CLOSED 2026-10-17:** std::function callback → FUN_14315f270 → Zone channel; writer FUN_14315b130; handled (admin2 off-grid paste). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — std::function callback → FUN_14315f270 → sent on Zone (channel 0); writer FUN_14315b130 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** where the off-grid paste request is finally sent, on which channel, and its body (for the zone's handler
  and OffGridPasteZoneRequest 0xF4/0x13).
- **Found so far:** ctor FUN_14315e5a0 / 0.589 FUN_1430b3c90, built in FUN_1431645b0 / FUN_1430b9970 (paste tool
  FUN_1436d00b0, branch when param_1[0x5d] is set) and handed to a deferred job (FUN_143159fe0 copies it,
  FUN_143160cf0 queues it); copy ctor FUN_14315e3e0. The emulator does not handle sub 5 (Building.Handle falls to
  "Unanswered").
- **Searched:** callers of FUN_14315e5a0 / FUN_14315e3e0 (FUN_143159f00 … FUN_14315d510, FUN_143159fe0), FUN_143160cf0
  (no send template call inside).
- **Try next:** the job's execute slot (the vtable FUN_143159fe0 installs) → send template and channel; the writer
  (BasePacket slot) for the body; runtime: paste a design off-grid on the test client and log the packet.

### L78. ResetAllCells / ResetTemporaryCells 0xEC/8 / 9: who sends them (terrain-service.md §1.2)
- **CLOSED 2026-10-17:** the sender FUN_143158bb0 is dead in 0.695. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — The sender FUN_143158bb0 is dead in 0.695 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what calls the sender FUN_143158bb0 / 0.589 FUN_1430ae050 (char param: 0 → ResetAllCells, else
  ResetTemporaryCells; channel 0) and what the server answered.
- **Searched:** decomp xrefs (none), ptrscan.py on the 0.695 exe (no 8-byte pointer; one 4-byte RVA in .rdata
  0x144e93df8, likely unwind data; .pdata), relscan.py (two non-call hits 0x142d3d903, 0x143123207), string search for
  a console command (none). trace/families/0009_TerrainCell.md lists it as "NOT HANDLED".
- **Try next:** what the .rdata RVA at 0x144e93df8 belongs to (unwind or a command table); the two rel32 hits'
  instructions; Lua natives / console commands near the voxel client (VoxelClientImpl vtable 14464c218).

### L79. TerrainCell MaterialChangeInfo 0xEC/6: field meanings and when live sent it (terrain-service.md §1.3)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: ADVANCED [P]** — A non-zero trailing u64 queues a client-local VoxelFarm::VoxelCellJobQueueHandler::CellGenerateMaterialsJob per cell (FUN_14408a770); the u64 is only a key handed back with the regenerated material data to VoxelCellBlockCacheImpl. No packet goes back. Server: keep u64 0 (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — Trailing u64: when it is non-zero, each listed cell is handed with it to FUN_14401ecf0 (sentinel DAT_14560bdc0 has no writer, so it is 0). The u16 is stored at VoxelClientBlockEvent +0x70 with no reader. Send u16 0, u64 0 (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED** — The u16 is stored at block-event +0x70 with the cell set; no reader near it (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the meaning of the leading u16 (material?) and the trailing u64 (block-event key), and which server event
  sent it (mining, explosions, heals).
- **Found so far:** client-handled (class TerrainCellPacketMaterialChangeInfo; reader FUN_1431468f0 / 0.589
  FUN_14309bfd0: u16, u32 n × {u64 cell, i32 min[3], i32 max[3], BoxPartialBitSet<40,40,40> (FUN_1431483c0)}, u64);
  handler: FUN_143f94230 → FUN_14401ecf0 per cell unless the u64 equals DAT_14560bdc0, then FUN_1436bb0e0 / 0.589
  FUN_143608360 (block-event map +0x98, shared with CellBlockDamage / ZoneProcessingComplete) → FUN_143667630(event, u16,
  map). Not in architecture-leads.md L8's server-only list after all (the id table spells it
  cTerrainCellPacketCellMaterialChangeInfo).
- **Searched:** the 0xEC dispatcher (part_0087 ~22230), the reader and handlers above, MODLOG 2026-10-08 notes.
- **Try next:** FUN_143667630 and FUN_14408d780 (what the u16 selects: a material, an effect); runtime test: send one
  with a transaction before a CellsUpdate on the test client and watch the effect.

### L80. Manifest entry flag and the "set 1" layer codes (terrain-service.md §1.4)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN (runtime optional)):** Flag stored and copied, never read in the cache code (pass 2); 0 works on both clients (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — The only keyed lookup of the manifest (FUN_144014800) copies set and versions, never the flag. The other use is a whole-entry copy into the terrain ResetMemento. The client never seems to read the flag; 0 is safe (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED [P]** — Entry layout decoded (flag at entry +4); reader not found (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what the u8 flag of each ModifiedCellManifest / ModifiedCellManifestUpdate entry means, and what layer codes
  5-8 ("set 1") of ModifiedCellManifestUpdate are for.
- **Found so far:** the live manifest capture (PacketDumps/TerrainCellManifest.bin, parsed 2026-10-15): flag 1 on 34,157
  of 38,190 layer-0 entries, 9,039 of 11,162 layer-1, 387 of 125,246 layer-2; the server sends 0 everywhere and the
  client works. Codes 1-4 = set 0 layers 0-3 (docs/terrain.md).
- **Searched:** docs/terrain.md, trace/systems/terrain.md, the capture.
- **Try next:** HandleCellManifest FUN_144016680 / 0.589 FUN_143f529a0 and the manifest entry struct (CellBlockManifestEntry
  "set, flag, 4 layer versions" at impl +0x150): what reads the flag; ModifiedCellManifestUpdate reader FUN_143146ad0.

### L81. Live content and push trigger of InstancedGameAdminMonitorPacket 0x10D sub 2 (trace/architecture/admin-pages.md §2)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 2, 2026-10-17: UNCHANGED** — Nothing new beyond the client block (FUN_1434e65d0). (trace/0695/agent-leads2-d2.md)
- **Need:** which keys / values / indent levels live's server put in the page-29 tree, for which games, to whom, and
  what made it send (no page-29 request exists).
- **Found so far:** container only: `u32 n × {string key, string value, u32 indent}` (reader FUN_143427160 / FUN_1434d6be0),
  printed `"%*s%s %s"` with colour by indent % 6 (FUN_143164bd0 / FUN_14320f5f0); the page's update slot is empty
  (FUN_143171880 / FUN_14321c2a0), so the server pushed it. The client's own block (FUN_1434362a0 / FUN_1434e65d0:
  State, DesiredState, Authoring GameGuid, QueuePosition, QueueSize, GameEnabled …) shows the naming style only.
- **Searched:** both decompiles (no writer / builder of the packet; RTTI has the class and its KeyValue array only),
  re/out/strings.tsv, packet_ids.tsv (no instanced-game admin request id), the 0x10D dispatchers FUN_14343c3d0 /
  FUN_1434ee920.
- **Try next:** other / internal client builds (an admin build may build it), forum / dev posts on site-game admin
  tools (design/forum), runtime is impossible without a live server. Meanwhile ours: admin opt-in push (admin-pages.md §7).
- **2026-10-16:** ours built (admin-pages.md §11): pushed to admins on a site game with the setting AdminMonitorPush
  (default off), keys in the client block's style. Still open: live's keys and trigger.

### L82. Page-28 fields without a proven meaning and live's procedural layer / node model (admin-pages.md §3)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Need:** what 0x10A's N (+0x28), P (+0x2C), F (+0x30), the entity leaf's first u32 (printed "%d" before the name),
  the two undrawn u32 and the bool, the biome f32 (plotted on the "%" / "Spawns" graphs) and the layer "queued" count
  were; what a "node" and a "layer" (LayerId) were on live's procedural spawner, and how they relate to the shipped
  ProceduralPlacementSet*.txt tables (sets per BIOME_ID with MAX_SPAWNS, respawn minutes) vs the server-only
  EntityLists / EntityListEntities (L2).
- **Found so far:** labels and colours (header `Zone Spawn Count: %d (%d%% of nodes)  F:%d  N:%d  P:%d`, graph series
  names, list / entity lines) from FUN_1431685b0 / FUN_1431682f0 / FUN_1431680f0 (0.695 FUN_143212fd0 / FUN_143212d10 /
  FUN_143212b10); graph value sources from disassembly (re/out/agent_l1/dis_1431685b0.txt).
- **Searched:** the three draw functions and the reader chain in both builds, ProceduralPlacementVoxelInterface RTTI,
  strings around the page, gameData ProceduralPlacementSet*.txt headers.
- **Try next:** any server-side spawner code compiled into the client (search RTTI / strings for "Placement", "Spawner",
  "LayerTier", "EntityList" users beyond the packet); forum / dev posts on resource and creature spawning per biome;
  the LAYER_TIER_WHITELIST_ID column's other users.
- **2026-10-16:** Domino's post (forum 55352) describes live's model: per layer and per biome, many possible
  locations (= nodes) per spawnable type, min / max per island, respawn below the minimum within ~20 min at a random
  free location. The server's set placements (ProceduralPlacement.Region: set / entity per biome type) can fill page 28's
  biome / entity-list / entity tree (procedural-placement-695.md §4).
- **2026-10-16:** built that way (Game/AdminMonitor.ProceduralBody, admin-pages.md §11): layer = PLACEMENT_DATA_GROUP_ID,
  biome = set BIOME_ID, list = set, entity = set entity; N / P / F, the undrawn u32 / bool sent as 0 / 0 / 1 — ours until
  this lead is answered.

### L83. Storybricks monitor pages 30-31 on Landmark's server (admin-pages.md §4.1)
- **CLOSED 2026-10-17 (pass 3, CLOSE (FOUND negative)):** Pass 2 proved it; nothing new. Do not answer 0x4FC / 0x4FD (trace/0695/agent-leads3-a.md)
- **Need:** whether Landmark's live server answered 0x1E/0x4FC StorybricksRequestDiagnostics (and 0x4FD
  RequestDataErrors) and what the two text blobs (state +0x48 / +0x60) held.
- **Found so far:** the page (FUN_14316b340 / FUN_143215d60) draws "STORYBRICKS AI SUMMARY" / "… SELECTED CHARACTER
  DETAIL" and two server text blobs; the request (FUN_143171ee0 / FUN_14321c900) is sent only by an admin client
  (vtable +0xB0, always false in retail); ClientStorybricksDebugManager is a registered Command module whose handler
  returns 0 (architecture-leads.md L4). Forum hits for "Storybricks" are 2014 EQNext / Landmark plans, no shipped
  Landmark feature found yet.
- **Searched:** both decompiles (page, request, Command dispatcher, module registry), design/forum/all text search.
- **Try next:** wiki dump (design/wiki) and patch notes for any Storybricks-driven NPC in Landmark 2016; the
  ClientStorybricksDebugManager methods for what data it expected.

### L84. Mail reading window shows no attachment rows on the 0.695 client (Game/Mail.cs SendRead)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing)):** Runtime 2026-10-17: the rows show; no change. (trace/0695/agent-leads3-c.md)
- **FOUND 2026-10-17 (runtime, re/test/mail_probe.js):** opening a founder letter on 0.695 runs the read handler and the window fill, builds one row per attachment (items 435 / 436 / 437 / 441), and the window shows the four icons with Take All (screenshot). The data path works; the 10-15 empty strip was not a server or client-code problem. No change.
- **Sweep 2026-10-17: NEEDS-RUNTIME** — Window fill and item builder: same calls and keys as 0.589 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** why the 0.695 reading window (EmailMessageWindow) showed an empty attachment strip for a mail holding
  2 × item 231 plus cash on delivery 10, and for one holding 20 coins (2026-10-15, social test stack,
  scratchpad screenshots read695.png / cod695.png, taken with PrintWindow from the background window). Taking and
  paying still worked (client coins 47 → 67, 62 → 52, items in the bag), so the packet itself is read.
- **Found so far:** the same in both builds: ReadMessageResponse reader (0.589 FUN_1432c77e0 / 0.695 FUN_143374610),
  attachment reader (FUN_1432c6c60 / FUN_143373a90), the handler (FUN_14358a8f0 / FUN_14363d820) and the window fill
  (FUN_143587660 / FUN_14363a660: types 5 / 6 only set IsCashOnDelivery / CurrencyCount, other types become rows
  with AttachmentSlot / AttachmentType), and the UI files (EmailMessageWindow.swf / .lua / .xml identical md5).
  Changed: the item-to-UI builder (0.589 FUN_142e86b00, 213 lines → 0.695 FUN_142f258f0, 138 lines), which still
  fills ItemGUID / Quantity / ItemID / name / Description from the client's item definition table (DAT_145431a50
  +0xb8), with no early exit. 0.589 showed the rows on screen (MODLOG MAIL ATTACHMENTS 2026-10-10, focused window).
- **Searched:** the four function pairs above, the UI files.
- **Try next:** a focused screenshot (rendering of an unfocused Scaleform window), then the 0.695 FUN_142f235f0 /
  FUN_143636830 (attachment copy) against 0.589, and whether the row needs the item definition in the client
  table (definition 231 present?) — lualog of ReadEmailMessage arguments on 0.695.

### L85. LightweightToFullNpc field values for creatures (Network/Message/Model/PlayerUpdateLightweightToFullNpc.cs)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: ADVANCED [P]** — Packet +0x94 → entity +0x158c is an ActorCompositeEffect id that the actor plays once loaded (FUN_142eb12d0 ← FUN_142eb6a10). 0 = none, which is what we send. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED + server change** — The apply copies several fields unconditionally whose client defaults are not 0. We overwrite chat colours (0xFFFFFF / 0xD4E2F0 / 1) and the expected speed (8.0f) with 0 Built / done: client defaults sent (69bb053). (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED (existing)** — full-npc-695.md §4 has the layout and defaults; live values need a capture (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what live put in the 0x26/0x5C fields for a creature (attachments, the three strings, +0x90 / +0x94,
  +0xb4..+0xc0 incl. the name text id +0xbc, +0x208.. resource setup, +0x264 state, Target, effect / resource lists).
- **Found so far:** full layout and the apply function FUN_142cb9f50 (which entity field each one sets); the server
  sends the constructor's defaults (no capture of the packet exists in re/ or the PacketDumps).
- **Try next:** name the entity fields the apply function writes (ProxiedCharacter offsets +0x147c, +0x434*8, +0x219c,
  +0x494*8 ...) from their other readers; NpcDefinitions / creature tables for per-creature values; forum screenshots
  of creature nameplates.

### L86. Build Site Expansion item definition (Game/Claims.cs MaxClaimsPerOwner; TASKS 3.5)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: ADVANCED (small)** — The Attached Build Site Expansion (texts 571177 / 571251, image set 10465) needs no item: attached sites were removed for the 2016 relaunch. The plain expansion is built. (trace/0695/agent-leads3-b.md)
- **Sweep 2026-10-17: ADVANCED** — Forum: consumed on use, tradable, sold in the Marketplace; item id still missing (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the item the 0.695 Site Management "Unlock More Build Sites" row leads to (OpenMarketplaceTagSearch("Site
  Expansion")): its item definition (id, icon, flags, use ability) so a player can raise PlayerMaxRootClaims by one up to
  eight (MaxRootClaimsAllowed). Texts exist: name 571204 / 571176 / 571246 / 571250 / 571254, description 571205, bundle
  571247 (0.695: "one Build Site Expansion ... Use your Site Management window to place new sites").
- **Found so far:** definitions come only from the live login definitions dump (ItemDefinitionManager), which has the
  Build Site Flag (105), Attached Build Site Flag (591) and the Inventory / Vault / Design expansions (3710-3712) but no
  Build Site Expansion (re/out/item_names.tsv). Wiki: gold flag icon with a "+", bought with Daybreak Cash, used from
  the bag to add a slot (design/wiki/pages/Build Site Expansion.wiki). Every player has 1 slot now.
- **Try next:** other packet dumps / captures for a definition with name text 571204; the image-set icon ids next to
  the Build Site Flag's; forum_raw 54424-8 / 54818-1 (mention the texts); Marketplace product lists in the dumps.
### L87. Wording of the 3-day upkeep reminder mail (Game/SiteUpkeep.cs MailWarning)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — — (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — No wording. In 2014 the reminder was its own Notification Window type (P1590). The wiki says the mail often failed to send. (trace/0695/agent-leads2-c.md)
- **Need:** live's subject and body for the reminder sent three days before a site expires (wiki Build Site
  Management; patch notes 7 Aug 2014 "an upkeep payment reminder sent out 3 days before a claim expires").
- **Found so far:** no client text for it (CodeStringMappings has only Claim.UpkeepMail.* expired, UpkeepMailMaxAdjusted
  and Claims.GaqExpiring, whose GAQ creator is never called); the server sends its own sentence in the style of the
  client's letters.
- **Try next:** forum_raw / archived threads quoting the mail ("will expire", "reminder"), screenshots of the inbox in
  forum posts and the wiki File: pages, captures.
### L88. How much more frequent iron became (P5796, 7 Jun 2016)
- **CLOSED 2026-10-17:** user decision 2026-10-17: leave iron density unchanged (no source for a number; the vein files are identical in 0.589 and 0.695).
- **Pass 2, 2026-10-17: ADVANCED [P]** — All 88 ContinentVeinData*.vdt are byte-identical in 0.589 and 0.695, so the 7 Jun change was server-only data. The earlier step on 26 Apr is P4808. (trace/0695/agent-leads2-c.md)
- **Need:** live's new iron vein densities ("Iron should be a little more frequent underground and on the surface").
- **Found so far:** the client's ResourceVeins.txt is byte-identical in 0.589 and 0.695 (not in the 57 changed tables), so
  the change was in server-only data. Iron veins today use the client table's DENSITY_SURFACE / DENSITY_CAVE.
- **Try next:** forum posts / screenshots after 7 Jun 2016 comparing ore counts; the 2017 final CDN manifest for any
  server-side vein file; player gathering guides on the wiki dump.

### L89. Temporary invisibility to creatures after a revive (P5832, 15 Jun 2016)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Already matches (5 s grace, dropped on attack). (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED [P]** — P4717 (26 Apr 2016) says "a few seconds to recover". A player (5 May) describes what ends it. The server's 5 s and drop-on-attack match. (trace/0695/agent-leads2-c.md)
- **Need:** how long a revived player is ignored by creatures and what counts as "entering combat" (live dropped it then).
- **Found so far:** no client data for it (Character.Revive.AbilityId 1170 is unused by the client; no stealth effect in
  the 0.695 tables); the server has no such rule yet.
- **Built 2026-10-15 (ours):** 5 s grace (Health.ReviveGrace), dropped when the player damages a creature.
- **Try next:** forum_raw threads about dying in caverns / chaos caverns (Jun 2016), patch notes before 15 Jun mentioning
  the invisibility's introduction, ability 1170 in any item / ability dump (gameData695\items_summary.tsv).

### L90. Gear passive numbers changed on 7 Jun 2016 (audit-equipment-goals-695.md U1)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** server-side numbers in no client table; the wiki pages (Reinforced Coat of Crashing Waves, Frenzied Slayer's Battle Axe) carry none. Ours are built and documented (passives d6005bd); re-open if a post with the numbers turns up. (agent-leads4.md §2)
- **Pass 3, 2026-10-17: UNCHANGED** — — (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — No numbers anywhere. (trace/0695/agent-leads2-c.md)
- **Need:** Frenzied Slayer's trigger chance before / after (P5747); Coat of Crashing Waves damage and radius (P5748).
- **Found so far:** server-side numbers, in no client table; the descriptions are unchanged between builds.
- **Try next:** forum threads after 7 Jun 2016 ("Frenzied", "Crashing Waves"), combat feedback posts, videos.

### L91. Source of four weapon recipe items (U2)
- **CLOSED 2026-10-17 (pass 2):** The four weapons were never craftable on live. Their tracker goals 786 / 750 / 788 / 842 are missing from the capture, while all 13 sibling Replicator weapons have theirs. The "missing weapon recipes" notes of May / Jun 2016 name other weapons. Built: RetiredRecipeItems (01e6ad6). (trace/0695/agent-leads2-c.md)
- **Need:** where 3996 Katana of Crashing Waves, 3999 Desperado's Deadly Arsenal, 4000 Lightbringer's Justice, 4001 Silent
  Shadow's Condemnation came from ("missing weapon recipes", Jun 2016).
- **Found so far:** recipes 2450 / 2453-2455 have no station mapping in either build; VISIBILITY_REQUIREMENT 1303 / 1306-1308;
  likely weapon-kill tier goal rewards (L42).
- **Try next:** goal reward bundles in the achievement capture, wiki weapon pages, forum posts.

### L92. Which tutorial step unlocks the modular castle design items 4098-4106 (U3)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED (+confirm)** — The wiki's Modular Thinking steps require Window / Long Room / Entry designs before the goal. This fits the built Building Block Basics reward. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED** — Dev: castle designs are earned "throughout the tutorial" (P5750) and "earn more through achievements" (P4224 block). There is still no per-goal list. (trace/0695/agent-leads2-c.md)
- **Found so far:** no goal in the Feb 2017 capture rewards them.
- **Try next:** wiki Getting Started pages, forum tutorial threads after 7 Jun 2016.

### L93. Were Getting Crafty / Building Block Basics / Modular Thinking retired on 7 Jun 2016? (U4)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** Modular Thinking still existed after 7 Jun 2016 (16 Jun post), so the chain is built from the wiki as it stood; record ids exist only in captures. Re-open path: a capture of a character past goal 685. (agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — — (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED [I]** — Modular Thinking still existed after the 7 Jun final pass (ginsann, 16 Jun 2016: players get stuck in it). (trace/0695/agent-leads2-c.md)
- **Found so far:** the captured player had 685 Becoming a Luminary in progress, so later chain goals don't show.
- **Try next:** wiki goal pages and their history, forum posts; affects TASKS 5.6.

### L94. Where a player can stand in an outpost placed in live's slots (Outposts.StandingSpot)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing)):** Runtime 2026-10-17. (trace/0695/agent-leads3-c.md)
- **FOUND 2026-10-17 [runtime, harness]:** `outpost 0` (Ahnzul Ancestralshrine, 406, cavern 457) teleported the test copy to <3025, 60.6, -1826.4> (StandingSpot + 1 m, ~913 m under the surface); after the load the player stood on the ruin's floor and was still there 15 s and 50 s later (dev `pos`; screenshot scratchpad l94-2.png) — no fall, no surface rescue. The earlier y −2000 falls were `tpo` teleports onto the cave-piece floor (L155), not the standing spot.
- **Sweep 2026-10-17: NEEDS-RUNTIME** — — (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** a teleport target inside a ruin stamped into a .cpa ruin slot that the 0.695 client accepts.
- **Found so far (2026-10-15):** slots start ~8 m under the cave-piece floor; outpost 0's template is solid from its
  bottom to ~32 m under the middle (plinth). Teleports to the cave floor there fell to y −2000; to the first template air
  above solid (StandingSpot, y 12) the client moved the player to the surface (its stuck-in-terrain rescue). Crystal
  trips therefore go to the cavern's cave-piece floor as before. Whether the client had the stamped cells when it
  checked is unknown.
- **Try next:** look at a ruin in game (user), log which overlay cells the client was sent before the teleport, check
  the client's teleport-into-solid test (FUN_? in the 0.695 teleport handler), compare with the old cell placement
  (worked: players stood inside cavern_ruins / first 0.695 stamps).


### L95. Spawner row 40 "Spawn With Weapons Equipped" (577229; P6511)
- **2026-10-27 (feature/creature-weapons): capture-calibrated actors + quiver.** Courage `5IewoKPQE2BqKg6Q` EquipmentBase dumps → `NetworkCaptures/_research/combat-loot-probe/model-weapons.tsv`: live NPC bows = HorseBow (+ Halas quiver), staffs = Dwarf staff, plain 1H/2H = Blade1H/Blade2H Dwarf, dagger/Ogre 2H as named. Shared type defaults / Scalewind bows&staffs swapped off kobold [I]; affinity/epic kept where uncontradicted. Quiver recipe **(A)** proven: outer 2 + attach 11 + bool 1 after bow (`agent-creature-quiver.md`). Still open: per-prop uniqueness (capture is model+Wield only), NPC shields (none in dump), quiver art variants beyond Halas.
- **2026-10-18 (cweap 4aee43f): best guess built and ON by default.** Weapon type is client data [P]: each creature prop's palette icon names it (Barren_Sword_Fire, Edbonguard_Daggers_Earth, Giant_Club_Earth, Nothing; dev post 15 Mar 2016 "icons indicate the type of creature, and its weapon and damage types"). The actor is ours [I]/[W]. Still unproven: the exact actor and tint per creature, shields (off hand slot 11 not traced), how bows look on creature bodies. Try next: 2016 videos / screenshots of creatures (the 15 Mar 2016 post images, Chaos Caverns / Outposts streams), a capture of a live NPC full-data attachment list.
- **2026-10-18 (agent assets, dce93ea / merge 827fb93): sending side built** — Game/CreatureWeapons.cs puts a weapon model in a creature's main hand (slot packet after the full NPC data, again on Equip / Unequip), CREATURE_WEAPONS default false (the table is name-matched [I]: Scalewind Ranged → kobold bow, Caster → kobold staff); dev `creatureweapon <guid|near> <Wield_*.adr|off> [tint]`. [P anim data] the Barren skeleton set replaces the 1H-sword attacks. Still open: live's weapon per creature (Barren, Highfall, Ebonguard, NovaTech, Stoneskin, bosses) — server data; try next: forum / video stills of creatures' weapons (trace/0695/agent-assets.md).
- **2026-10-17 (agent storyhide, trace/0695/agent-storyhide.md):** lead 1 — both carriers proven (full NPC attachment list +0x28 / EquipmentSetCharacterEquipmentSlot → FUN_142ebd620); missing is the per-creature weapon data (server-only on live; every Barren shares model 1457). Lead 5 — Npc / Giant / Chomper animation networks have WeaponIdle conditions, so Equipped / Sheathed may show on non-bipeds [I weak]: test `storyweapons <guid> 1` on a Darkfang Specter. L20-e (prop +0x490) is a mover enable flag.
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Built as a drop-down; option values are the server's (see L18). (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED** — Only P6511. (trace/0695/agent-leads2-c.md)
- **Need:** the row's control type and values: EditableProperties 0.695 row 40 `npc_spawn_weapons_equipped` has VALUE_DEFAULT
  109, VALUE_MAX 0, VALUE_MIN 0, STEP 0 (unlike every slider / checkbox row), and what the creature does with it.
- **Found so far:** texts 577234 Weapon Visuals, 577235 Equipped, 577236 Sheathed (Story Node row 41, 2.23); creatures
  have no weapon equip / sheath support on the server yet.
- **Try next:** the settings window's row builder for a row with step 0 (FUN_142c15ee0 / FUN_142c1eae0), PropSettings
  records live sent (captures), RulesetObjectSettings / PropertyEditor ActionScript in re/as3_695.
- **2026-10-16 (TASKS 2.29 / 2.23 pass):** row 40's shape (VALUE_DEFAULT 109, MAX 0, MIN 0, STEP 0) is exactly that of
  the other drop-down rows (20 Mood 89, 21 Stance 81, 22 Emote 51, 25 Text Location 5: max / min / step 0, default =
  an option value of the unshipped EditablePropertyValues.txt, L18), so row 40 is a drop-down; its options are most
  likely the Weapon Visuals texts 577235 Equipped / 577236 Sheathed [I]. Row 41 `npc_weapon_visuals` (Story Node) is
  odd: default 106 but max 2, min 0, step 1 (a 0-2 slider shape). Still missing: the creature side. Searched: 0.695
  decompile strings (no "sheath", "holster", "WeaponDrawn"); AnimationSelectionVariables has selection 274 {Weapon 0,
  WeaponIdle 0, Class 0} (an unarmed state) and Weapon 1 / 2 rows with weapon classes 1010 / 1011 — candidates for
  sheathe / draw on a biped network [I, not proven]; the NPC full packet (LightweightToFullNpc +0x28) carries
  CharacterAttachmentData, the other candidate (weapon model attached or not). Spawner Links 577074 / 577075 "...Equip /
  Unequip weapon" (0.589) wait on the same answer. Not built: an option with no proven effect would be a dead setting.

### L96. How live showed Story Node text over a creature (6.33 / 2.22)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing)):** Runtime + built (9fd07a0, a2f6ced). (trace/0695/agent-leads3-c.md)
- **FOUND 2026-10-17 [runtime, harness]:** `creature kobold 0 5` → the client sent FullCharacterDataRequest and got LightweightToFullNpc; then DisplayPropText mode 4 / 5 with the **kobold's guid** as speaker (dev `npctext`, 895a573) showed the bubble over the kobold's head (scratchpad bubble-1 / bubble-3). The 2026-10-15 failure was a lightweight creature. Built (9fd07a0): the server remembers per client which creatures it promoted (WorldSession.FullNpcs, by transient id) and story bubbles / announce lines are spoken by the creature for those clients, by the spawner prop for the rest. Route verified (a2f6ced, scratchpad route-1.png): with the player as fallback speaker and the kobold 5 m to the side, the bubble showed over the kobold.
- **Sweep 2026-10-17: NEEDS-RUNTIME** — Same +0x50 table as L30 and L58 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the packet / speaker live used so a story creature's text appeared in a bubble over the creature (and a
  question's Yes / No there).
- **Found so far [P]:** DisplayPropText's handler FUN_142f859b0 looks the speaker up only in the props' hash table
  (param_1 +0x50, 0x1003 buckets, key at entry +0x2b0); tested 2026-10-15: a story creature guid shows nothing, a prop
  guid shows the bubble. A chat Say line from the creature (Chat.NpcSay) showed no bubble either.
- **Interim (ours):** the bubble's speaker is the creature's spawner prop.
- **2026-10-16:** the chat line packet ChatPacketFromStringId (0.695 FUN_143364980 case 4) finds its [*speaker*] with
  FUN_142f85390, the same +0x50 table (key at +0x2b0) — and players are found there (the /sit relay works), so the table
  is not props-only: a story creature is missing from it, perhaps only until LightweightToFullNpc (built since 0cc86a8,
  L98) promotes it. Re-test DisplayPropText with the creature guid after a full-NPC promotion. Meanwhile the Story
  Node's "Announce Emotes in Chat" lines use the spawner prop as speaker too (StoryTools.AnnounceEmote).
- **Try next:** the chat packet's bubble flags (ChatDocumentClass / SpeechBubbleWidget callers for NPC speakers), the
  NPC actor's speech-bubble attach (FUN_142ebfcb0 callers other than DisplayPropText), patch notes on NPC speech bubbles.

### L97. Free-for-all PvP flag InstancedGames_PvPAllowed
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN):** 573081 "PvP Allowed" is only IgGeneralSetting 62 (setting 11, rule set 8, Adventure) [P]; our PvPAllowed for 2 / 5 / 7 matches IsGameFFA defaults (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — No new static path. The interim (PvPAllowed for 2 / 7) agrees with IsGameFFA defaults. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED [P]** — The client's hostility rule is confirmed. IsGameFFA (setting 7) defaults to 1 exactly for rule sets 2 and 7, which matches what we built. (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED** — IgRuleSets HAS_FFA_TEAM_BY_DEFAULT = 1 only for rule sets 2 and 7; setting 11 exists only for rule set 8 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether / when the live server set the game setting `InstancedGames_PvPAllowed` for free-for-all games: the
  0.695 client treats team-0 players as enemies of everyone only when it is set (FUN_142ea9630, FUN_1434ec610;
  trace/0695/hud-progress-695.md §5), otherwise as nobody's enemy.
- **Interim 2026-10-16 (ours):** rule sets 2 / 5 / 7 send PvPAllowed = 1 in GetConfigurationResponse (7fb8d67);
  IgGeneralSetting has the setting only for rule set 8 (Adventure, off).
- **Try next:** the IgGeneralSetting / IgRuleSets rows of the 0.695 tables for the Deathmatch rule set, the
  GetConfigurationResponse settings list the server sends, captures.

### L99. Underground caverns under the Pioneer landscapes (TASKS 6.48 step 2)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN (built)):** Type-2 anchors in Pioneer.FindCavern (88fa629). MaxCavernDepth 180 m stays ours [I]; no client value exists (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED [P data]** — Pioneer Underground .cpa: one type-2 beginning location per isolated cavern (957 on 440 … 1007 on 442; each in its own connected cavern). Our FindCavern walks all 23,168 piece-104 middles instead. Built: type-2 anchors used 88fa629. (trace/0695/agent-leads3-c.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — The 0.589 caves folder (211 files) has none for 417-421 either (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** where the caverns under Pioneer lands 417-421 are, for new-player cave build sites (P4116 "Second
  priority places build sites in an underground cavern on a Pioneer Landscape"; P4361).
- **Searched 2026-10-16:** the 0.695 test copy's CommonData/Voxel/caves: ContinentCavePlacements[...]_Surface /
  _Underground.cpa exist only for the 28 classic continents (391-416, not 409), none for 417-421;
  VoxelContinentToLayerMapping gives only 417 an underground layer (495 "TEST_1Cavern", CAVE_DENSITY_PCT 0.0001);
  re/out/fileindex_695.tsv has no other 417-421 cave file.
- **Interim:** Pioneer.FindCavern reads the placement file when one exists (none today), so step 2 finds nothing and
  new characters fall through to a classic landscape (step 3).
- **Try next:** the 0.589 install's caves folder, the client's procedural cave generation for a layer without a .cpa
  (VoxelCaveTileSets / VoxelContinentCaveTileMapping readers in the 0.695 decompile), forum posts on Pioneer cave sites.

### L100. How much higher live raised the central Spires (TASKS 6.49)
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN (built) + runtime verify):** Spire.cs now stamps the floor on the median ground + `Raise` 0.8 m on the 0.4 m grid (Spire.cs:502-519). The raise is [I]; T2 confirms the client view (trace/0695/agent-leads4.md)
- **2026-10-17 (in-game T1 + agent spire):** confirmed: Spire 21 m low in a pit, players under the deck (lightweight props have no collision until the full prop arrives). Fix in progress on local/spire: floor on the median ground, 0.4 m grid, +0.8 m raise [I], full props sent at once, home Spire kept at (0, 0) with ground fill (user decision 2026-10-17).
- **Pass 3, 2026-10-17: ADVANCED (major) [P]** — Both Spire templates hold no solid voxel: an air box 124 × 68 × 124 m with a 0.4 m vertex-only floor. The Hub model's AABB is y −20.94 … +43.74 m, so its base sits on the template floor. The server stamps the floor ~21 m (±8 m from cell rounding) below the ground. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED [P chronology]** — base_portal_spire.otd (the server's) is the post-7 Jun template: it has the Parlor (P5725) and the Repair Station (P5409). portalspirebase.otd is pre-11 May. The raise was for grass. (trace/0695/agent-leads2-c.md)
- **Sweep 2026-10-17: ADVANCED** — Two Spire templates; every prop is 0.8 m higher in portalspirebase.otd (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the offset behind P5647 (25 May 2016) "Central spires are now raised slightly higher out of the ground".
- **Found so far:** base_portal_spire.otd is the only Spire template in either client; Spire.cs stands its Spire prop
  on the ground (no offset). No client table names a per-landscape Spire position (StaticZoneData gives all landscapes
  one GEOMETRY_OFFSET; the centre is local (0, 0)).
- **Try next:** screenshots / videos of a Spire after 25 May 2016 (platform height above the grass), portalspirebase.otd
  (compare with base_portal_spire.otd), the server-side placement in any captured zone data.

### L101. Which client code reads item flag byte 2 bit 7 (UniqueEquip) (TASKS 6.54)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN [P]):** The client shows "Unique Equipped" only from tooltip flag 0x02 (0.695 FUN_1435176a0, caller FUN_143768e20 reads tooltip +0x1c8) and refuses only on TransferItemResponse 0x22. No code reads definition +0xe. The rule is the server's, as built. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: UNCHANGED (neg, wider)** — The item data source exposes NoTrade (+0xc bit 7) and IsEquippable (+0xd bit 5) but no unique key. No code tests +0xe bit 7. (trace/0695/agent-leads2-b.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — No code tests definition +0xe & 0x80 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** proof that definition +0x0E bit 7 is UNIQUE_EQUIP.
- **Found 2026-10-16:** the bit is set on exactly the 21 equipment-slot-3 "Accessory" items of the dump and on nothing
  else; the client's unique-equip dialog FUN_142f1eb90 opens from TransferItemResponse reason 0x22 (FUN_142f202e0).
  Built on that (ItemDefinition.UniqueEquip, Inventory.DuplicatesUnique, tooltip bit 2).
- **Try next:** the 0.695 Lua item binding (ItemDefinition accessors next to NoTrade) and code testing +0xe & 0x80.

### L102. Outposts as build sites (the name banner on entry)
- **CLOSED 2026-10-17:** built (9a5894e, OutpostSites.cs): outposts go to the client as client-only site records, so its own walk-on banner shows the name and "Created by", without owners, upkeep or Site Management (agent-outposts.md). In-game look in TESTING-NEEDED.
- **Need:** whether to register each outpost as a site record so the client shows its own entry banner ("The name of the
  Outpost appears in white text, along with the date it was created", wiki Outposts) and the builder, as live's
  player-built outposts were build sites. Today Outposts.cs sends a chat line "Outpost: <name>".
- **Risk:** site records drive build permissions, upkeep, the Site Management window and the map; an outpost site
  would need an owner nobody can use and must stay out of the owner lists.
- **Try next:** the client's claim-entry banner path (OnEnterClaim → HudStateManager, FUN_142dc7540 area) for which
  record fields it shows; then decide (user).

### L103. Colours of outfit tint aliases (TASKS 4.17, trace/0695/cosmetics-695.md §1-2)
- **CLOSED 2026-10-17:** every alias's colour is in ArtTints (agent-leads2-b.md); all built outfits were worn and match their icons; the 2016 Green / Blue sweaters use GreenGreen00 / Colony_Light_09 (worn: all green / navy blue, ada7c10). Their icons stay placeholders (no new icon in 0.695).
- **Pass 2, 2026-10-17: FOUND (colour source) / Green-Blue sweater ADVANCED** — Every alias's RGB is in the ArtData that the server sends (ArtTints.json, a live capture). All runtime screenshots match it. Live had no 4th or 5th Holiday alias. Built / done: 2016 Green / Blue sweaters = GreenGreen00 / Colony_Light_09, worn-tested (ada7c10). (trace/0695/agent-leads2-b.md)
- **ADVANCED 2026-10-17 [runtime, harness screenshots]:** worn on the test copy and compared with the icons: Skeleton_Light_00..05 = White, Pink, Orange, Cyan, Green, Red (text order confirmed); Holiday_Light_00 / 02 / 03 = Green & Tan, Red & Brown, Brown & Green (confirmed); Colony_Heavy_05 silver and _02 green confirmed, but **_03 shows dark navy (not red) and _04 black / white (not blue)**; Founder_01 red / grey, Founder_00 blue, and the placeholder **Founder_08 is orange / black** (matches the Orange icon). Year Zero 00..03 unclear from behind. Dev `weartint <alias>` added (1b55bcf); the sweep of all 15 Colony_Heavy aliases on the flight suit found 13 deep red and 12 blue, now used for Merry Red / Festive Blue (cosmetics-695.md §6). Left open: which alias live used for the 2016 Green / Blue sweaters (no 4th / 5th Holiday alias exists; placeholders stay).
- **Sweep 2026-10-17: NEEDS-RUNTIME** — — (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** which ArtTints alias gives which colour, to pick the tint of each built outfit (Skeletal Bodysuits
  Skeleton_Light_00..06, Holiday Sweaters Holiday_Light_00/02/03, Year Zero Anniversary_00..03, Tech gear Founder_*),
  and which existing alias / icon live reused for the 2016 Holiday Sweater (Green) / (Blue) and Tech Sergeant's Gear
  (Orange). Today: [I] picks (mesh default, text order, Battle Armor names of the shared Colony_Heavy aliases).
- **Searched (2026-10-16):** all 0.695 packs (8.2 GB) and loose files for the alias strings (only ArtTints.txt), the
  .adr / .dma of the Holiday mesh (hashed DMAT params; no Jenkins OAAT / lookup2 / CRC-32 match of the alias names),
  AlexTestTintSemanticTables.txt (format only), decompile strings "TintSemantic" / "ArtTint" (none; only
  "BadTintAliasHashes.txt"), 0.695 ImageSets (no new outfit icons).
- **Try next:** wear each built outfit in game and compare with its icon (cosmetics-695.md §6); the client's tint
  loading path (reader of ArtTints.txt, writer of BadTintAliasHashes.txt) for the colour source.

### L104. Player waypoints on the server (admin /goto waypoint, 0x1E / 0x430)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** The personal waypoint never reaches the server (L136). /goto waypoint answers with an error line. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED [P]** — The only waypoints the server holds are the zone "HUB / WARP" waypoints (0x32/0x14 ZoneWaypoint, 0x13 Cooldown). /goto waypoint has no player data to use. (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED** — The map waypoint is client-only (see L136) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** where the client keeps the map waypoint a player places (world map "PlaceWaypoint", FUN_143054920 area)
  and whether any packet carries it to the server: live's /goto waypoint [name] (AdminCommandPacketGotoWaypointRequest,
  trace/0695/admin-commands-695.md) warped an admin to a player's waypoint, so the zone server knew it.
- **Searched (2026-10-16):** the server has no waypoint store; the admin command functions only send the target guid /
  name. Not yet: the world-map Lua/AS3 (re/as3, UI scripts) for the waypoint setter, the PlayerUpdate / ClientUpdate
  subs for a waypoint field, SendSelf for a saved waypoint.
- **Try next:** grep the decompile for "Waypoint" writers (FUN_142d... PlaceWaypoint path) and the map UI's data source;
  a runtime capture while placing a waypoint on the map.

### L105. The invalid-guid global and the default run speed constant (admin commands)
- **CLOSED 2026-10-17:** DAT_145462af8 = 0; DAT_144c410f0 = 1.0f; "default" sends 0.0; built (/run). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — DAT_145462af8 = 0; DAT_144c410f0 = 1.0f; "default" sends 0.0 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the value of `DAT_145462af8` (the client's "no guid", compared everywhere a guid may be unset; the admin
  commands send it when the admin typed a name) and of `DAT_144c410f0` (/runspeed "default" and the inc/dec floor,
  metres per second). The server treats 0 and 0xFFFFFFFFFFFFFFFF as "no guid" and maps /runspeed onto stat 5
  (default 12) meanwhile (trace/0695/admin-commands-695.md).
- **Searched (2026-10-16):** re/decomp_all_695 text has no initialiser for DAT_145462af8 and no literal for
  DAT_144c410f0 (both are data; the decompile shows only their uses).
- **Try next:** read the two addresses from the exe's .data / .rdata (re/tools or Ghidra MCP read_bytes: DAT_144c410f0
  is a float in .rdata; DAT_145462af8 is set by a static initialiser — find the CRT init entry that stores to it).

### L106. ClientTime receive handler cases (0x1E / 0x42A, FUN_1433ba260; 0.589 FUN_14330d470)
- **CLOSED 2026-10-17:** all 7 cases decoded from the jump table; built (/clienttime). (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — All 7 cases decoded from the jump table (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what the client does with each mode 1-7 of a server-sent AdminCommandPacketClientTime (set, hour, pause,
  sync, warp, advance, reverse): the handler is a 7-way jump table (DAT_1433ba644) the decompiler left as an indirect
  call in both builds. The server echoes the admin's own packet back on the assumption that live's zone server relayed
  it to the client's clock.
- **Try next:** Ghidra MCP: disassemble the jump table targets at 0x1433ba644 + 4 × (mode − 1) and decompile each
  case block (they are inside FUN_1433ba260's range); or read them with re/tools from the raw bytes.

### L107. Systems behind the logged-only admin subs (vehicles, set model, ammo, weapon sets, placed effects, free-fly hide, job profiles)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** User decision (no vehicle / weapon / ammo data, PlacedEffect is a debug tool). Nothing new in the client. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED [P]** — Vehicles.txt has 0 rows, so vehicles can't be used. PlacedEffect Create plays an effect and prints a debug line; its Destroy does nothing. The SetModel path exists. (trace/0695/agent-leads2-d2.md)
- **Need:** for each decoded-but-unbuilt admin packet (trace/0695/admin-commands-695.md: 0x49C SpawnVehicle, 0x4F0
  SetModel, 0x499 AddAmmo, 0x4EE WeaponSet, 0x4EF PlacedEffect, 0x4F1 FreeFlyHide, 0x3EC Profile*) the client system it
  drives and the packets live sent back: the Vehicle family (mount / dismount, "You must exit your vehicle to use the
  /goto command."), the player model override (PlayerUpdate temporary appearance, cf. /profile select's
  PlayerUpdatePacketRemoveTemporaryAppearance 0x26 / 10), ammo slots on weapons, weapon-set profiles
  ("/weaponset profile <profileDefinitionId>"), placed effects, free-fly visibility bit 35 of entity +0xa20, and job
  profiles (ranks, job experience). Live's game at launch had none of these exposed to players except vehicles' absence.
- **Try next:** per system, the family trace (trace/families) for the S→C packets the client reads, then decide with
  the user which are part of the launch-era game.

### L108. Where four weapon recipe items came from (item-sources-audit-695.md §3.4)
- **CLOSED 2026-10-17 (pass 2):** The four weapons were never craftable on live. Their tracker goals 786 / 750 / 788 / 842 are missing from the capture, while all 13 sibling Replicator weapons have theirs. The "missing weapon recipes" notes of May / Jun 2016 name other weapons. Built: RetiredRecipeItems (01e6ad6). (trace/0695/agent-leads2-c.md)
- **Need:** the live source of 3996 "Recipe: Katana of Crashing Waves", 3999 "Recipe: Desperado's Deadly Arsenal",
  4000 "Recipe: Lightbringer's Justice", 4001 "Recipe: Silent Shadow's Condemnation" (teach Replicator recipes 2429 /
  2432-2434). Their own recipes 2450 / 2453-2455 are default-learned, CAN_CRAFT_WITHOUT_STATION 1 and have **no**
  CraftingStationRecipeMappings row in 0.695 — the same shape as the retired wisp recipes (2394-2397, 2416; wisps
  removed 26 Apr 2016, P4704) and Colonist's Patrol Armor 5111 / 5112 (Lumen mapping removed in 0.695) — so the
  Lumen Station did not sell them. The weapon lines match the 500-kill armour goal rewards (Water / Affliction /
  Radiance / Shadow), which hints at goal rewards [I], but no goal in the capture or on the wiki gives them.
- **Searched:** gameData695 Recipes / RecipeComponents / CraftingStationRecipeMappings, the item list grantIds, the
  goal capture reward bundles, Achievements.Tiers.cs (wiki goal pages), the wiki dump (no pages for the four weapons),
  patchnotes.tsv (no hits).
- **Meanwhile:** the server keeps them as random finds (RecipeBook.RandomUnknownRecipeItem), so they stay obtainable.
- **Try next:** forum posts / videos naming these weapons, the 1000-kill or later goal tiers, other item captures.

### L109. The "Events" world's id and name text (TASKS 3.19, trace/0695/event-islands-695.md §6)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — Teleport.Worlds columns {Name, GUID, Claims, ClaimCapacity, CurrentWorld}, sorted by Name string (TeleportSpireWidget.as:32, :120). No id. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED [I]** — The Events world was reached via the teleport menu and was not in the login world list (58006 #3). (trace/0695/agent-leads2-c.md)
- **Sweep 2026-10-17: ADVANCED [P]** — The world list name is a text id, so 567016 "Events" is the only candidate (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the world id and name text id live's Locations list (TeleportBase sub 3) and zone details used for the
  "Events" world (forum 58006, 3 Oct 2016).
- **Found so far:** the only client text "Events" is 567016 (0.589 and 0.695); its one code use is a list-section
  header in 0.695 part_0081 ~4217 (written next to 567017 "Actions"), not a world list. The server uses world id 3
  (ours) and 567016 [I]. The live server-list world names are 560911-560926; no "Events" / "Museum" world name exists
  near them.
- **Searched:** re/out/text_695.tsv, text_589.tsv, the 0.695 decompile for 0x8a6e8, gameData695 tables, design/forum.
- **Try next:** screenshots of the 2016 teleport menu (forum 58006 image, YouTube "Halloween Party Island" tour, wiki);
  the client's Teleport.Worlds data source / TeleportWindow ActionScript (re/as3_695) for how a world without a server
  list entry is shown; a text id whose data-source name mentions events.

### L110. Event island content and rules (TASKS 3.19)
- **CLOSED 2026-10-17 (built e08249f behind EVENT_ISLAND_EXPIRE_ON_CLOSE, default off):** closed-island sites expire through Claims.Expire; letter wording [I].
- **Pass 4, 2026-10-17: BUILD (setting, default off)** — Live expired left-over sites when an event island closed (forum 55925 #1, 58077) [P]; ours keeps them. Spec B3, destructive → user's switch (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: UNCHANGED** — Expiry is the user's decision. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED [I, 2 reports]** — The event island had a Spire: "Kylen's build next to the spire" and "far from the spires" (58006 #11, #12). (trace/0695/agent-leads2-c.md)
- **Need:** whether sites on the event islands counted toward the player's site limit / needed a flag, had upkeep, or
  were free; whether the islands had a Spire; the developers' own party builds on them ("Tons of fun stuff to see",
  forum 58077); and the expiry of left-over sites when an island closed (58077: "Any build sites that were left behind
  on the halloween island will be expired this morning").
- **Found so far:** the server applies the normal site rules there, stamps a Spire once the island has baked heights
  (as on every island), and keeps sites on a closed island (no expiry built). No .otd template with a seasonal name
  ships in 0.695 (terrain-695.md §1.1), so the party builds are not in client files.
- **Searched:** design/forum (57978, 57987, 57999, 58006, 58077, 58108, 2015 55778 / 55919), terrain-695.md.
- **Try next:** forum / wiki / YouTube tour of the islands (site counts, a Spire in screenshots); 2015 holiday notes
  (55919 / 55925) for the beta rules on the Museum world; then decide with the user whether to expire sites at the switch.
- **Found 2026-10-16:** forum 55925 (hotfix notes 10 Dec 2015): "the Halloween Party island will close next week. You don't have to do anything, though feel free to remove your builds early if you wish to use the claim flags or materials" — island sites used normal claim flags and the server cleared them at the close; 55919 (9 Dec 2015): the winter island was made to "allow claims to be placed"; with 58077 (Nov 2016, sites left behind "will be expired this morning") live expired left-over sites when an island closed. Whether to do the same here (it deletes players' builds) stays the user's decision (TESTING-NEEDED decisions).

### L111. Landmarks of Landmark: live's exact placement, the award model, and the banner's wording (TASKS 3.20)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — Depends on L100: landmarks are placed beside the Spire. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED** — Dev: banner sites can be edited by staff only, and landmarks were "placed by hubs" on slopes. A player: a 25-voxel levelling base, left sticking up on flat ground (this matches our rule). Banner label format "<name> by <creator>". (trace/0695/agent-leads2-c.md)
- **Sweep 2026-10-17: ADVANCED** — Forum 57555 (not searched before): placement beside each spire; older winners fill the extra landscapes (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** (1) where exactly live put each landmark beside its Spire and at what height (built: nearest dry, level spot
  beside the Spire, the build's lowest solid layer on the lowest ground under it [I]); (2) whether the award model
  `Theme_Awards_Showcases_LandmarkOfLandmarks01` (PropDefs 1735 "Landmarks of Landmark Winner") stood beside each one
  (built: yes, 3 m outside the site on the Spire's side [I]); (3) which of the client's two Willows End templates live
  placed for Liberation / Endless River (`willows_end_castle` chosen; `nevrai_willows_end` unused); (4) how the two
  entries "combined on 1 claim" were laid out (Overland Express + passenger + sleeper car; Landmark Boy + Landmark Girl:
  built in a row along +x [I]); (5) the banner's description and date strings (record strings 3 and 4: ours) and whether
  live used contributor format 1 (`name^guid^voxels`, links to the Showcase, P1387) instead of format 0 (names only).
- **Searched (2026-10-16):** forum 57799 (all 7 posts), 57814 (wrap-up), patch notes P1385-P1388, wiki "Landmarks of
  Landmark" + "A-Z (List)", the 0.695 client (templates, record reader FUN_142da1a50 / FUN_142db5ca0, walk-on
  FUN_142dc7540, contributors FUN_142db82e0, AS3 ClaimWalkOnTextWindow); trace/0695/landmarks-of-landmark-695.md.
- **Try next:** screenshots / videos of the spires after 3 Aug 2016 (the wiki's Facebook winner albums, archive.org
  captures of the forum's image links, Twitch / YouTube "Landmark" streams of Aug - Dec 2016), later forum threads naming
  a landmark's location ("next to the spire on ..."); for (5) the 2014 / 2016 contest announcement threads (wiki links:
  "landmarks-of-landmark-2016-winners.56134", "landmarks-of-landmark-all-winning-entries-june-4.37542") for the dates and
  descriptions.

### L112. The three tutorial goals' live records (TASKS 6.57, goals-audit-695.md "Tutorial goals built from the wiki")
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — — (trace/0695/agent-leads3-b.md)
- **Need:** live's own record data for Getting Crafty / Building Block Basics / Modular Thinking (built as 9001-9003
  with ids, icons and sort indices of ours): (1) achievement and objective ids, icons; (2) that they followed 685
  Becoming a Luminary (built [I]; the 25 May 2016 rearrangement P5541 may have moved them); (3) whether the objectives
  came one at a time and in which order (built: steps, order in goals-audit-695.md [I]); (4) the reward items: which
  goal gave the Modular Castle designs 4098-4106 (built: Building Block Basics, 8 items with a file [I]; this also
  answers spec-design-categories.md U-DC3 only by inference) and the exact Architecture Tools set of Modular Thinking
  (built: 4358 / 4359 / 4391 / 4392 / 1742 / 4393 from Domino's list and P5544); 4106 Modular Castle Window with Wall
  has no client .otd; (5) how live counted the Blocks tab, Selection tool, Paint and the three Fine Tuning paste steps
  (built: UI events 12 / 17 / 18 / 19, tool switches 10 / 6 / 2, operation kind 6, paste mode fields) and whether a
  crafted "Smelted Copper (3)" counted crafts or items (built: crafts, one craft makes 150).
- **Found so far:** texts in both clients (576237-576292, 577049); the wiki pages; patch notes P4787, P4788, P4815,
  P4984, P4994, P5324, P5361, P5541, P5544, P5750; forum 57063 (Domino's starter-design list), 57064 #138, a player ("multiple steps
  which follow each other"), 57451 ("players keep getting stuck in modular thinking"); the capture's Getting Started
  records (h3[6] predecessor, h3[9] sort 1001-1005, bundle 1051).
- **Searched:** the Feb 2017 capture (no record, no reward bundle with 4098-4106 / 4358-4393 / 1742), re/out/achievements.tsv,
  design/wiki pages (Getting Crafty, Building Block Basics, Modular Thinking, Getting Started = deleted page),
  design/forum/all (56999, 57023, 57063, 57064, 57105, 57170, 57451), trace/0695/patchnotes.tsv, gameData695 Recipes,
  items.json, re/as3 TweakMode / BuildModeWidget, 0.589 decompile (paste writer, Paint cursor).
- **Try next:** YouTube / Twitch new-player videos of May - Jun 2016 and early 2017 that show the Journal's Getting
  Started page (objective order, rewards shown, icons); archive.org captures of the wiki pages' images
  (Getting_crafty.png, Building_block_basics.png, Modular_thinking.png show the Journal entry); a packet capture or log
  of a character past 685; forum threads after 7 Jun 2016 about the tutorial ("Modular Thinking stuck").

### L113. Procedural placement: launch-era sets, column units, and live's spawn model (TASKS §7 Procedural placement, procedural-placement-695.md)
- **CLOSED 2026-10-18 (built):** min/max respawn model built (3c8f9c4); launch-era sets server-only (alpha sets ship).
- **Pass 3, 2026-10-17: ADVANCED (proposal)** — As L2: concrete min / max / respawn-elsewhere default. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED [P forum]** — A Dec 2015 player post shows server-placed "environmental props" (huge boulders) inside claims that could not be deleted. (trace/0695/agent-leads2-d2.md)
- **Need:** (1) the placement sets for the launch surface biomes 47-52 (the shipped ProceduralPlacementSets name only
  the alpha-era biomes 18-31; the server bridges by BIOME_TYPE_ID and shared ground materials [I]); (2) how the
  ProceduralPlacementTool mapped a MASK onto the world (built: pixel = (local + 4000 m) × MASK_SCALE, wrapping [I]),
  the unit of ProceduralPlacementSetEntities OFFSET_X/Y/Z (built: centimetres [I]; OFFSET_Y reaches -50), the exact
  meaning of AVERAGE_DISTANCE (built: one jittered candidate per AVERAGE_DISTANCE cell [I]), ENCROACHMENT_PRIORITY
  (built: higher places first, footprints may not overlap [I]), SOIL_SUITABILITY (all 1) and PLACEMENT_DATA_GROUP_ID
  (built: all groups of a biome together, 17 / 18 left out as tests [I]); (3) whether live's launch landscapes showed
  server-placed rocks / bushes / flowers (the sets' decoration; built behind ProceduralPlacementDecor, default off);
  (4) live's respawn model in numbers: per-zone minimum / maximum per entity and respawn at another free node of the
  biome (Domino, forum 55352; built: regrow in place after MIN..MAX_RESPAWN_MINUTES) — the rows of L2.
- **Found so far:** sets / entities / materials decoded (PROP_ID = PropDefs, MATERIAL_ID = VoxelMaterials whose
  ARRAY_INDEX is the baked grid's surface code [P]); altitude 3000-4000 = global metres (local + 2000) [I, fits sea
  level]; masks' coverage and tiling (DualForests tile seamlessly; Forest / Clumps 4096² do not); the dev post on spawns.
- **Searched:** 0.695 decompile and exe strings (no loader, no column names), both installs' CommonData
  (ProceduralPlacementTool holds only Masks and CaveMeshes; HowToGenerateVeins.txt names Continent_XXX.ini /
  .bat under EQNext\Dev\Runtime\Tools\ProceduralPlacementTool, not shipped), gameData VoxelWorldSettingBiomes (alpha vs
  OpenBeta world settings), design/forum/all (55352, 57252 "trees not respawning"), design/wiki (Striped Wood).
- **Try next:** screenshots / videos of the launch continents (2016) for rocks and bushes as separate props (props are
  selectable / outlined, voxel rocks are not) and for tree density per biome; forum threads on "tree respawn" after
  Jun 2015 and on Old Growth / Tundra tree counts; any EQN / Landmark dev build or archive with Continent_*.ini; the
  CaveMeshes folder's naming for the tool's coordinate conventions.

### L114. FOUND 2026-10-16 (B.10) — How a world change reached another world's servers; the scope of a live gateway (live-server-architecture.md "0.695" §3, architecture/recreation-design.md)
- **CLOSED 2026-10-17 (pass 3, CLOSE (FOUND 2026-10-16)):** No new work; B.10 built on it (trace/0695/agent-leads3-a.md)
- **Need:** what the server did when a player picked another world in the teleport window (TeleportRequest {u32 world,
  u32 zone}, writer FUN_1430c8600) or a Lost Crystal sent them to another world (P5734): a transfer under the same
  gateway connection (one gateway for several worlds, or a hand-over between gateways), or a trip through the login
  server (CharacterTransferServerRequest / Reply, Login 0x12 / 0x13, reader FUN_143a54ad0 / FUN_14399cbf0).
- **Found so far:** the client connects to a gateway only in run state 0xA (FUN_142d53aa0 / FUN_142cba700) and does not
  reconnect on ClientBeginZoning; ClientBeginZoning carries the world id (+0xa8) and a different world is handled like a
  different zone (event-islands-695.md §6); server-only ids TransferPlayerToServer 0x09, PlayerTransferServerRequest /
  Reply 0x0B / 0x0C, WorldInitiatedPlayerTransferServer 0x16 (zone base registry FUN_1434ba530 / FUN_14340c950, no
  client code). Groups, friends, guilds and custom chat channels worked across worlds (P4905-P4907, P5841).
- **Searched:** 0.695 decompile for the registry names (part_0107 only), trace/families/Login_protocol.md (0x12 / 0x13
  layout, no trigger), trace/systems/session.md, gateway-service.md, patchnotes.tsv (world / server / transfer).
- **Found 2026-10-16:** the client never sends CharacterTransferServerRequest. In the 0.695 exe its sender FUN_143a5cc50, the wrapper FUN_143a5cd10, the login connection's generic send FUN_143a53090 / FUN_143a530e0 and the cast helper at 0x143a53960 (null check, rcx − 0xd60, jmp into FUN_143a53100) have no pointer anywhere in the file and no call / jmp from other code (scratchpad vascan.py / relscan.py: every 8-byte VA and every E8 / E9 rel32). The only callers of FUN_143a53100 are those dead paths. So a world change never went through the login server from the client: it is a re-zone on the same gateway connection (ClientBeginZoning with the other world id, no reconnect — above). On live, that means the gateway, or a hand-over between gateways, moved the connection to the other world's zone. Ours: the gateway routing table is already keyed by (world, channel) (B.2), so B.10 is a gateway re-route plus ClientBeginZoning, with both worlds' zones registered on one gateway.
- **Try next:** callers of the CharacterTransferServerRequest builder (0.695 FUN_143a53100 / 0.589 FUN_14399b220) and
  of the Login connection after character select (is the login link kept open in play?); the client's handling of
  ClientBeginZoning when the world id changes (FUN_142d58cd0 / FUN_142cbf940 branches); NetInfo.log / LoginClient.log
  lines a live player posted after changing world (forum, YouTube); the EU worlds (Satisfaction, Understanding) as a
  hint that worlds ran in other data centres (different gateway addresses).

### L115. World list history: merges and closures 2015-2016 (live-server-architecture.md "0.695" §2)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** History; the server list is configurable (WORLD_NAME_ID, .env) and the 2016 five + Events are known (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED [P]** — Apr 2015: 10 worlds. 7 May 2015: 4 worlds. 21 Mar 2016: Museum islands closed (P4235), EU worlds gone (I). Aug 2016: 5 worlds. (trace/0695/agent-leads2-c.md)
- **Need:** which worlds were live when (the client ships 15 world names, 560911-560926), when and how worlds were
  closed or merged between 2014 and the launch list of five (Courage, Determination, Liberation, Rebellion, Serenity;
  forum 57799), what happened to characters and claims on a closed world, and whether the EU worlds and the Foundation
  Museum world still ran at launch.
- **Found so far:** P0010 (Feb 2014 more worlds), P1455 (Jul 2014 Foundation Museums world), P3478 (May 2015 "multiple
  worlds as before", names unchanged), forum 55218 (Jun 2015 player: "removing the plethora of servers"), wiki Servers
  and Landscapes (closed-beta list of five incl. museum and EU), P6529 / P6560 (Events world 2016), P3966 (Foundation
  Museum worlds bounced on their own, Dec 2015).
- **Searched:** trace/0695/patchnotes.tsv (merge / consolidat / new world / world names), design/forum (server merge,
  world merge, consolidating worlds, closing / fewer servers), design/wiki (Servers and Landscapes, Spire).
- **Try next:** forum threads around the May 2015 wipe and the Mar / Jun 2016 wipes (server list announcements),
  archive.org captures of the launcher / server status pages, YouTube character-select screenshots (world list with
  population), the Login ServerListReply fields (population, locked, EU flag) for what live reported per world.

### L116. Moving the player a short way without a loading screen (user request 2026-10-16; Game/Teleport.cs)

- **CLOSED 2026-10-18 (FOUND [P/I strong], fixed 6f277fe, default 60 m 3f600a2):** the freeze came from TeleportToLocation +0x30 = zero facing vector (identity quaternion) → NaN player visibility boxes → octree double link → QSV cycle (agent-visibility-analysis.md). Round 83: 32 plain hops 2-45 m, no freeze.
- **REOPENED 2026-10-18 (user request):** agent-short-teleport.md — octree + client remove bug; round 77: 12 hops ≤9 m fine, the 13th (7 m, 25 s after a re-zone) froze the client. Next: Frida visibility hooks (re/hooks/visibility) to find the racing thread / path; then a server rule (e.g. short moves only when the client's streaming is idle) or none.
- **CLOSED 2026-10-17:** root cause found (QuerySimpleVisibility list cycle in the client); the server cannot fix it — teleports keep the loading screen. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND (existing)** — Root cause in OPEN-LEADS "Found" (QuerySimpleVisibility list cycle); the server cannot fix it (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** a same-landscape teleport that just moves the character (no ClientBeginZoning, no loading screen) without
  ever hanging the 0.695 client.
- **Found so far:** the 0.695 TeleportToLocation handler (FUN_142d59f30) moves the player (FUN_142d5ce80 + vtable +0x418,
  called twice) and re-centres the terrain engine (FUN_1431589a0, +0x970d1) only when the destination is not covered
  (FUN_143155b40) or, with no engine, farther than VoxelFarm setting 0x2c55a58 (FUN_143152c40, default 80 m:
  DAT_144714810). Test 2026-10-16 on the test copy (ShortTeleportDistance 60): hops of 3, 20, 45 m went without a
  loading screen; the 4th (3 m back) hung the client at once with nothing else in the burst (server sent only
  ClientUpdate 0xC; the client sent Synchronization, KeepAlive, a Command packet, then stopped). A server position
  update for the player's own entity is ignored: FUN_142ce6340 (PlayClient vtable +0x208, the 0x7F handler) returns
  when the entity is in the locally controlled set (+0x32f68). Default back to 0 (85b793d).
- **Try next:** Frida on the test copy during short hops (never detached while hooked): which branch of FUN_142d59f30
  ran on the hanging hop (FUN_143155b40 result, whether FUN_1431589a0 was called) and the call stack of the hang
  (QuerySimpleVisibility FUN_144009a40?); if the re-centre is the cause, send short moves only when the destination is
  inside what the engine has loaded (the client's own cell requests show its loaded set) or with flag +0x41 (0.695's
  FUN_142d5ce80 stores the new position first, unlike 0.589 — the "Waiting for Teleport..." screen may now end at the
  right place; it is still a loading screen, but shorter than a re-zone).

### L117. Story Node mood / stance / emote on creatures that are not player-like bipeds (TASKS 2.23; PropSettings.DropDowns)
- **CLOSED 2026-10-17 (pass 3, FOUND (pass 2)):** Emote / stance / mood play only on player-network actors. Skipping the send for other models is optional. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED** — No creature network has an Emote_Index or Stance_Index switch or any emote clip. The player network drives kobolds, lesser giants, goblins, slaug, void goliaths, skeletons, ogres, dark elves, kerra, dwarves and Iksar. (trace/0695/agent-leads2-b.md)
- **Need:** whether the Story Node's Mood / Stance / Emote selections play on every story creature, and which network
  each creature model uses; live's option values (EditablePropertyValues.txt, L18) if they were not AnimationSelection ids.
- **Found so far [P]:** the 0.695 lists (4 moods, 8 stances, 29 emotes) all resolve to AnimationSelection rows of the
  **player** network PlayerAnimationNetwork64.mrn (decoded 2026-10-16 with the switch NodeDef layout of
  trace/systems/social.md; scratch script switch.py): StanceSwitch|Switch1 (node 14546) = Stand, Kneel, Placeholder,
  Dance, Sit_Chair, Laydown, AFK_States (Stance_Index 0-6; Sitting 351, Lying Down 480); outer StanceSwitch (14556) =
  Switch1, Drunk, Skip, Skip1, Sneak (101-104); Emotes_SingleShot|Nuetral Switch3 (11868) = UsePotion, Call, Cough, Flex,
  Give, Sneeze, Thanks, Welcome, Yawn, Come_Here (Emote_Index 22-31), Switch4 (11880) = Stretch, Hug (32-33). [I, strong]
  Beckon = Come_Here 455, Yell = Call 450 (the nine extra Story Node emotes match the nine EmoteSingle rows 449-456 / 817;
  the lines 572119 "beckons everyone closer", 572114 "yells loudly").
- **Not known yet:** the creature models' own networks (Barren / Scalewind / kobold / slaug...): only the player network
  is extracted (re/out/anim). A creature whose network lacks Stance_Index / Emote_Index shows nothing.
- **Try next:** list the .mrn files in the packs (re/tools/packfind.py "\.mrn$") and map each creature model
  (Models.txt → ArtSourceActors / adr) to its network; decode their Emote_Index / Stance_Index switches the same way;
  runtime check: `propset` a Story Node's emote to 449 (Cough) on a Barren spawner and watch.
- **Found 2026-10-16:** the 0.695 networks' string tables (read from the test copy's packs by fileindex_695 offsets; scratchpad mrn_scan.py): Emote_Index is in PlayerAnimationNetwork64, NpcAnimationNetwork64, GiantAnimationNetwork64 and PetAnimationNetwork64 only; Stance_Index also in ChomperAnimationNetwork64 and LargeBipedAnimationNetwork64; Kobold, Boss, Amorphous, FieryFungi, ScorpionBush, WarMachine, Flinger have neither. So a Story Node emote / stance can only show on creatures driven by the Npc / Giant (stances also LargeBiped / Chomper) networks; on the others the client has no control to set and shows nothing — the server sends the same either way. Still open: whether the Npc / Giant networks' index values are the player network's (decode their switches like the player's), and which network each creature model uses (Models → ArtSourceActors).

### L118. FOUND 2026-10-16 — Facing on arrival at a Starting Location / Respawn Point (TASKS 2.16, 2.24; P5825)
- **CLOSED 2026-10-17 (pass 3, FOUND (existing)):** 2026-10-16. (trace/0695/agent-leads3-c.md)
- **Need:** how the live server made a player arriving at a Starting Location (travel, respawn) or a Respawn Point face
  the prop's direction ("The Starting Location prop now has a directional arrow to show which way the arriving player
  will face. You can rotate the prop", 16 Jun 2016, P5825; wiki: facing = prop rotation).
- **Found so far:** the arrow is the prop's model (client side). Our arrivals re-zone (Teleport.Rezone →
  ClientBeginZoning, rotation field +0x70 of the 0.695 packet object, reader FUN_142c23310) or move
  (TeleportToLocation vec4 rotation), both sent with the identity rotation; whether the client turns the character to
  either rotation is not traced (the 0.695 zoning handler was not found under the 0.589 address FUN_142cbf940).
- **Try next:** the 0.695 handler of PacketClientBeginZoning (dispatcher case 0x23) and of TeleportToLocation
  (FUN_142d59f30): follow packet +0x70 / the vec4 rotation into the player actor (heading field); then send the prop's
  rotation from StoryTools.RespawnSite / Graveyards (Starting Location) through Teleport.ToZone.

### L119. Highfall Catapult numbers and the gear-recipe day (TASKS 3.13, 6.3)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — — (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED (small)** — A player measured the rock's flight to a wall as about 1 s. "1 armor or 1 weapon" is read as "one of each" by a player. A pre-cap player data set gives daily recipe totals. No hour for the daily reset. (trace/0695/agent-leads2-c.md)
- **Need:** the catapult's field of fire (cone width, minimum and maximum range), rate of fire, rock flight speed, blast
  radius, damage and health; whether its forward axis is the prop's +Z; which of the five AnimationSelection rows with
  MESSAGE "Activate_01" (272, 283, 346, 404, 410) live sent, and whether the animation's own CompositeEffectEvents
  track already plays the release (Creatures.ThinkSiege also plays 12825). For P4438 ("only 1 armor or 1 weapon recipe
  per day"): one of each or one in total, and the hour the day turned over.
- **Found so far:** P5728 (one facing, distant targets only), wiki "Highfall Catapult" (Elite, Fire, ~60 blocks, AoE +
  knock-down; a block = one 0.4 m voxel, so ~24 m [I]); the .mrn messages Activate_01 / Ouch / Death; composites 12825 /
  12826. No ability rows for NPCs in the client tables (server data). Ours: cone ±30°, 10-28 m, every 5 s, 16 m/s, 4 m,
  1600 hp / 120 per rock; recipes: one armour + one weapon per UTC day (Pacing).
- **Searched:** gameData695 AnimationSelection / Models, the catapult .adr and WarMachineAnimationNetwork64.mrn (pack
  strings), composite_effects.tsv, wiki pages Highfall Catapult / Creature Prop, patchnotes.tsv ("catapult", "per day").
- **Try next:** forum / video footage of a catapult build (July 2016 dungeon-crawl threads, design/forum/all
  2016-07-08-57649), Morpheme .mrn event tracks (what fires on Activate_01), dev posts about the 1 Apr recipe limits.

### L120. Chaos Cavern resource drops and creature healers (TASKS 6.16, 6.21)
- **Note 2026-10-30:** cavern resource rolls and creature/chest loot tables moved to host-editable
  `Resources/Content/loot/` + `/data/content/loot/` (`ContentTables`, AGENTS #22). Rates still ours `[I]`; Courage
  bag-preview outcomes + forum loot-chat soft-nudged humanoid kills and chest item mix
  (`NetworkCaptures/_research/combat-loot-probe/loot-outcomes.md`, `forum-loot.md`). Still need inventory-after-pickup
  combat capture and clean chest-open capture for full bag inventories.
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 3, 2026-10-17: UNCHANGED** — Only the Barren Lightbringer has a heal (wiki "Ability 1: Rejuvenate"). It is already built. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED (small)** — P4696: healers chase enemies when they are not healing. The wiki ties healer/support to Radiance. No rates. (trace/0695/agent-leads2-c.md)
- **Need:** which resources cavern NPCs dropped (P4353 "random resource rewards"), which counted as "rare" (P4536) and
  at what rates; which creatures were healers (P4696), how much and how often they healed, and their heal effect.
- **Found so far:** underground resources per the wiki "Resources" table (Iron, Silver, Gold, Obsidian, Alabaster; Ruby,
  Emerald, Diamond). Healer candidates from the prop dump: the radiance minions Scalewind Shaman (2465 / 2777), Ebonguard
  Priestess (2521 / 2817), Stoneskin Riftmender (2606 / 2850); their wiki pages are empty templates. Composites 12571 /
  12572 PFX_Combat_Radiance_Heal_Impact / Release exist. Ours: gems 8 % per roll, else 1-3 of an uncommon resource; heal
  15 % of life every 4 s to the most hurt of its side below 75 % within 15 m.
- **Try next:** forum posts on cavern loot (Apr 2016), videos of outposts with Priestess / Shaman spawners, the 0.589
  pack strings for NPC ability names, L19 (NPC ability sets).

### L121. Whether the movement body's state word carries jumping / falling (TASKS 6.19)
- **CLOSED 2026-10-17 (pass 2, FOUND):** Writer BaseClient::BroadcastLocalPlayerMovement FUN_142ca4ce0. Every bit is mapped to a player flag. The animation-parameter hashes name most of them. 0x10 = jumping, 0x20 = falling, 0x400 = landed, 0x1000 = gliding, 0x40 = not moving. 0x10000000 adds a support-entity tail. Built / done: Airborne uses the state word; bit table in docs/protocol.md 4.3 (fdd43b1). (trace/0695/agent-leads2-b.md)
- **ADVANCED 2026-10-17 [runtime, harness]:** the server now logs each change of the state word (6a6072e). Test copy, `tpo 0 15 0 nosnap` / `tpo 0 40 0 nosnap` over open ground: the word was 0x10000441 standing on the ground, dropped to **0x441** right after each teleport into the air and returned to 0x10000441 about 3 s later (the fall time plus the teleport); at login 0x441 → 0x41 (type 1, briefly) → 0x441 → 0x10000441 on landing. So bit **0x10000000 = on the ground / supported** [I: timing, 2 falls + login]. Still open: walking (0x401 vs 0x10000401 — needs real input), a jump, and the client writer of the word. Until walking is seen the server keeps the vertical-speed test (Consumables.Airborne). Second run (`tpclaim`): 0x441 → 0x41 → **0x21** (type 0, vy −3.1 m/s, falling) → 0x401 → 0x441 → 0x10000441 — so 0x20 shows up while falling and 0x400 / 0x40 look like movement / standing-still bits [I].
- **Sweep 2026-10-17: NEEDS-RUNTIME (+ADV)** — The client airborne test has no ability or item-use caller (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** how the server can tell a player is jumping or falling (P4702: the Cozy Campfire is "no longer castable while
  jumping or falling").
- **Found so far:** the client keeps the flags at player entity +0x1450 (0x20 jumping, set by FUN_142ec3460; 0x40
  falling, FUN_142ec17a0; debug names "Player IsJumping" / "Player IsFalling", part_0066 ~5039). Its cast errors have no
  airborne case (strings.tsv FUN_14351ebd0) and no client Requirement tests it, so the refusal was server-side. The
  movement body's state uint (type 0 tail, type 1 alone) is 0x401 moving / 0x441 standing (docs/protocol.md 4.3); its
  other bits are not decoded. Meanwhile Consumables.Airborne reads the vertical speed of the latest movement update
  (|vy| > 2.5 m/s within 0.75 s, ours).
- **Try next:** log the state uint of type 0 / type 1 bodies while jumping and falling in the 0.695 test copy (server log
  only, no Frida); trace the client's movement-body writer to see where the state word comes from.

### L122. Group members on other worlds and between worlds: party-frame fields, logout, content trips (TASKS B.9; social-service.md §10)
- **CLOSED 2026-10-17 (pass 3, ADVANCED [P] → CLOSE-BY-DESIGN):** 0.695 FUN_142df41b0 / FUN_142df4460: IsInZone needs both +0x100 and +0x108 equal to the viewer's; +0x10c is a text id (DAT_145431178 +0x10, "InvalidWorld" on a miss). Our Groups.cs already matches; group lifetime across worlds is server-internal (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED (built)** — The name-text fix is in (Groups.cs:544-547). The rest is unchanged. (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED [P]** — +0x10c and +0x104 are name TEXT ids; linkdead null = 0; bonus range is 200 (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what live put in a group member record for a member on another world or logging out — the u32 at +0x100
  (compared with the viewer's own for "IsInZone", FUN_142d560a0), the u32 at +0x10c (looked up as a world for
  "WorldNameId", FUN_142d56350; we write 0), the link id at +0xf8 ("IsLinkdead" when not the client's null value
  DAT_14535d490) and whether positions were sent across worlds; whether a group survived a logout / world change and
  for how long; whether a Chaos Cavern group trip offer (InviteToGroupContent 0x24) reached members on other worlds.
- **Found so far:** P4906 (26 Apr 2016) proves groups spanned worlds; the client side above (0.589 row fillers; GroupsBase
  "same" in 0.695). Ours meanwhile: the zone writes +0x100 = 1 and +0x10c = 0 as before; the social service sends a
  member on another world with +0x100 = 0 and the position zeroed, marks a member in the 30 s presence grace linkdead
  (+0xf8 = 1) and removes them after it; cave trips go to members on the same world only.
- **Searched:** trace/0695/patchnotes.tsv ("group", "party", "same world"), coverage-695.md §0x2B, the 0.589 decompile of
  FUN_142d56350 / FUN_142d560a0 / FUN_142d3ebe0.
- **Try next:** the DAT_145351bf8 vtable +0x10 lookup (world id or name text id?) and DAT_14535d490's value in the
  0.695 decompile; what the client's own member record (+0x100 / +0x108) is compared against when the viewer changes
  world; forum / wiki "party" / "group" posts from 2016 (linkdead, "offline" in the party window, groups after
  relogging); a capture of two grouped clients on one world (our server) to see the client's reaction to world 0.

### L123. Player Studio follow and recommendations (InGamePurchase 0x34 / 0x35 / 0x37 → 0x39 / 0x3A; TASKS B.4 step 8)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Follow built; Recommendations has no reply class and no shipped caller (pass 2) (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED [P layout / I reply]** — The Recommendations request body is {u32 100, u32 5}. It has no reply class, so the likely answer is StoreBundleSearchResults. (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED [P]** — AddFollowing reply layout and Lua event decoded (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what the server answered to CommerceFollowCreator 0x34 / UnFollowCreator 0x35 (0.695 0x35 / 0x36) and
  CommerceRecommendations 0x37 (0.695 0x38): the layouts of CommerceAddFollowing 0x39 / RemoveFollowing 0x3A (readers
  FUN_143182fc0 / FUN_143183390; 0.695 FUN_14322da80 / FUN_14322de50, "renumbered", bodies same) and whether a
  recommendations reply exists; what a "follow" was keyed by (account or character; across worlds) and where the
  client shows it (AccountStore's first string list = followings, Commerce.SendAccountStore).
- **Found so far:** the send sites (world-channel-service.md §3.1: FUN_1431a2390, FUN_1431c55e0 / FUN_14324d270,
  FUN_1432706c0; 0x37 FUN_1431a5ea0); no Recommendations reply class in either RTTI; the spec's `following` table is
  not created by the commerce service (built 2026-10-16) and the requests stay unanswered as in-process.
- **Searched:** world-channel-service.md §3.1, trace/families/0045, coverage-695.md §0x45, docs/commerce.md.
- **Try next:** decompile FUN_143182fc0 / FUN_143183390 and their handlers in FUN_1431adf70 (what they fill, which
  data source); the 0.589 / 0.695 UI ActionScript for a Follow button (re/as3, 0.695 export) and the Lua bindings
  (luabind.tsv "Follow"); forum / wiki "Player Studio follow".

### L124. Commerce service behaviour that is inferred, not proven (TASKS B.4 step 8; world-channel-service.md §6.3 / §7)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** (a) no consumer of OnReceiveServerStatus; (b) / (c) answered; (d) web pages (L137) (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED [P]** — (b) The Contest window closes at once on submit, with no wait and no timeout. (c) Creator character names are shown (hotfix 1 Apr 2016). (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — OnReceiveServerStatus has no consumer in any 0.695 UI, Lua or pack (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** (a) what live's ServerStatusResponse 0x0E bool meant (the service answers true while up, the world false
  while it is unreachable; FUN_1431ab880 only hands it to Lua OnReceiveServerStatus, which no 0.589 UI script uses);
  (b) what the client does when SubmitContestEntry gets no answer (the world now answers ContestEntryResults false
  while the service is away); (c) whether live's Player Studio products, reviews and portraits were per account,
  per character or per server (we key rows by (world, character) and match a player's own rows on the realm = the
  character database the worlds share); (d) whether the Showcase pages were one site for all servers (they stay per
  world here, spec §6.6 option) and how a design bought on another server got its screenshots (a design taken from
  another realm gets no images: only the voxel snapshot travels).
- **Found so far:** FUN_1431ab880 (0.589): reads one byte, nothing after it, calls OnReceiveServerStatus(bool); no
  "store unavailable" text in either build (spec §7); wiki Player Studio / Showcase: sold for Daybreak Cash
  (account-wide currency); docs/showcase.md: pages on store*.landmarkthegame.com.
- **Searched:** the spec's sources (§2), coverage-695.md §0x45 / §0x98, the 0.589 decompile of FUN_1431ab880 and
  FUN_14319fc60.
- **Try next:** 0.695 UI Lua / ActionScript for OnReceiveServerStatus and the Contest window's timeout; the forum
  archive (design/forum/all) for "Player Studio" + "server" / "character"; the 0.695 Lua for portrait use
  (BindPlayerPortrait); a client test of the Contest window with the service stopped (TESTING-NEEDED 2026-10-16).

### L125. How live's zone handed a build edit to the Environment Server (TASKS B.3; terrain-service.md §5.5)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN):** Id table fits forwarding ApplyDiff unchanged (pass 2); internal layouts are ours (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED [P ids]** — In the VoxelConstruction id table, the client requests are 1-9 and the zone↔Env twins start at 0xF. ApplyDiff has no twin, and 0x12 ZoneRequestFailedCellList is the Env→zone reply. Both fit forwarding the request unchanged. (trace/0695/agent-leads2-d2.md)
- **Need:** whether live's zone forwarded ApplyDiffRequest 0xF4/1 itself to the Environment Server or applied it
  against its own copy; the internal layouts of the Env Server messages (with L48).
- **Found so far:** the client's id table pairs the other client requests with zone-side names (ClientSelectionAreaRequest
  0x2 → ZoneSelectionAreaRequest 0xF; OffGridPasteClientRequest 0x5 → OffGridPasteZoneRequest 0x13; ClientUndoRedoRequest
  0x3 → UndoRedoRequest 0x10) but has **no zone-side twin of ApplyDiffRequest** — so live may have forwarded the client's
  ApplyDiffRequest unchanged (as ours now does, with our own envelope), or the zone applied it. EnvServerResponse 0xF4/0x15
  and ZoneRequestFailedCellList 0x12 are the Env → zone names. Ours (2026-10-16): no voxel copy in the zone, every write a
  transaction in the service, budget and filters sent as data (§5.5).
- **Searched:** trace/0695/architecture-leads.md L8 (server-only ids), re/out/agent_arch/serveronly_695.tsv,
  terrain-service.md §1.3.
- **Try next:** strings / RTTI near cVoxelConstructionPacketId* in both exes for a "Zone" ApplyDiff name; the 0.695
  admin monitor page 33 drawer for per-job fields that name the zone; dev posts on the voxel database / Environment
  Server (design/forum/all: "environment server", "voxel database", "apply diff").

### L126. ZoneDisplayNotification 0xF4/0x1B: when live sent it, which texts, and the bool (TASKS B.3; terrain-service.md §6)
- **CLOSED 2026-10-17 (pass 3, CLOSE-BY-DESIGN (use rule)):** Both bool paths proven; which texts live sent is server-internal. No packet dump with 0xF4/0x1B exists (Resources/PacketDumps: 8 files, none VoxelConstruction). Text ids beside 562155 are unrelated (ids are allocated by date) (trace/0695/agent-leads3-a.md)
- **Pass 2, 2026-10-17: ADVANCED** — Server-only text keys found: 1,906 CodeStringMappings keys appear in no client code, Lua, UI or AS3. The parameter-free ones (231) are the candidates for a bare text-id packet, e.g. Claim.SetTime.Error 577525 and Harvest.Fail.* (trace/0695/agent-leads2-a.md)
- **Sweep 2026-10-17: ADVANCED [P]** — 562155 is the text of Claim.LostClaimServerConnection (L142) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** what live used VoxelConstruction ZoneDisplayNotification for, which text ids, and the meaning of the bool.
- **Found so far [P]:** reader 0.695 FUN_14315b9d0 (0.589 FUN_1430b1310): u32 text id at +0x18, bool at +0x1c (default
  1 when absent); handler FUN_1431614a0 (0.589 FUN_1430b6880): text looked up through DAT_145431178 vtable +0x10; bool
  set → BaseClient::ShowSystemMessage (FUN_142d56570, named by its own profiler string); bool clear → DAT_145431a10
  vtable +0x28, the call that also prints "You have been invited to join group..." [I: the chat window]. We send text
  562155 "Unable to process the request. Connection to the Build SiteServer is unavailable." (same in 0.589 / 0.695)
  with the bool set when a remote Environment Server is away (Building, BuildingClipboard).
- **Searched:** the 0.695 decompile around FUN_143161530 (the dispatcher case), re/out/text_589.tsv / text_695.tsv.
- **Try next:** other text ids whose string table row sits beside 562155 (Construction.* / Claim.* server texts); the
  packet captures (Resources/PacketDumps) for a 0xF4 sub 0x1B; a client test of both bool values.

### L127. Ruin layers and outpost placement in the Environment Server (TASKS B.3; terrain-service.md §4.5, L76)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 2, 2026-10-17: ADVANCED [P]** — The page prints a ruin layer only by its list index. The record has no layer id or landscape field. (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED** — TerrainObjectDeploymentGroups.txt is the engine's terrain-object (DPO) system, not ruin layers (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** live's ruin-layer rule — what a layer is, what Desired counted, when a placement Failed — so placement can
  move from the zone (Game/Outposts.cs ChooseSlots / FillCaverns) into the service, and the zone's placement markers
  (outposts695d.txt, spire_base*.txt, landmarks_of_landmark.txt) become the service's records.
- **Found so far:** the monitor page's "Ruin Layer Info" record (Desired +0, Active +4, Failed +8, Last ID +0xC; spec
  §1.1); forum "Chaos Caverns without ruins/lost crystals" (2016-04-12). Built 2026-10-16 behind RuinLayers (off): one
  layer per landscape, every ruin_* / cavern_ruins_* stamp one instance (ruins.tsv), Failed always 0, reverted
  instances not removed; the zone's markers stay authoritative (copied into ruins.tsv / stamps.tsv at the import).
- **Searched:** terrain-service.md §1.1 / §4.5, trace/0695/ruin-stamping.md, terrain-695.md §4.
- **Try next:** the 0.695 drawer FUN_143163fe0 for the layer ids it prints; TerrainObjectDeploymentGroups.txt (server-only
  table, loader FUN_143be10a0) — whether it names ruin layers; dev posts on Chaos Cavern ruin counts.

### L128. Which Environment Server operations live counted under job types 4, 5, 6 and 17 (TASKS B.3; spec §1.1, L76)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Need:** the meaning of "Cell Temporary Modifications" (4), "Cell Generation" (5), "Cell Composite Update Lod" (6)
  and "Misc Cell Ownership Reusable" (17), and whether Total Jobs counted finished or current jobs.
- **Found so far:** only the names (FUN_143c74ef0 / FUN_143bb4ef0) and the page layout. Ours [I] (JobCounters.cs): digs
  and heals → 4; nothing → 5 (no server-side generation); cell body fetches → 6; owned-by lists and censuses → 17; the
  Heal tool and dev reads / writes → 7; Total = finished since start.
- **Searched:** terrain-service.md §1.1, re/out/agent_terrainsvc/envmon_695.c.
- **Try next:** the 0.695 decompile around FUN_143c74ef0's callers (any other user of the job-type enum, e.g. a
  per-type priority or a debug command); the client's EnvironmentServer* strings.

### L129. How live picked the landscape and cavern of an Underground Adventure / Lost Crystal trip (TASKS 3.14 F2)
- **CLOSED 2026-10-17 (pass 4, CLOSE (built)):** Caves.TravelRandom rolls the type-3 beginning locations (Caves.cs:28-34, :414-430), the client's own server-half roll. Entry is stale (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED [P mechanism, I use]** — The server-half GetRandomCaveBeginningLocation ("Player attempting to roll random cave") rolls among the type-3 cavern-graveyard pieces, never the current one. It returns the piece's global position and cave id. (trace/0695/agent-leads3-c.md)
- **Pass 2, 2026-10-17: ADVANCED [I, 3 reports]** — Before 7 Jun, trips stayed on the same landscape and repeated the same caverns. P5734 answers that ("other worlds and biomes each time"). P5927 then extends it to the button. Built: NextCave prefers another landscape and cave biome (01e6ad6). (trace/0695/agent-leads2-c.md)
- **Need:** live's rule for "Chaos Caverns in any zone" (P5927, 30 Jun 2016) and "other worlds and biomes" (P5734):
  uniform over landscapes or caverns, weighted by players / biome, the player's own landscape left out or preferred,
  and whether every landscape's outposts existed at once or were filled by the Environment Server's ruin-layer jobs.
- **Found so far:** the client sends no destination (TeleportBase 0xFC sub 0x0B / 0x0D, "same" in 0.695); the server
  half of the pick is compiled into the client but never called: VoxelFarm::CaveManager GetRandomCaveBeginningLocation
  (0.695 FUN_143f84420) picks a cave index other than the current one, optionally filtered by node +0x60 (L51). Ours
  (Game/Caves.TravelRandom, Outposts.NextCave, 2026-10-16) [I]: a landscape at random, then a cavern; a landscape whose
  outposts aren't stamped yet is queued and the trip goes to a ready one; the crystal picks any placed outpost not
  locked out, and each use queues one random unplaced landscape.
- **Searched:** trace/0695/audit-outposts-695.md, architecture-leads.md (CaveManager), design/forum (Chaos Caverns
  threads), patch notes P5734 / P5927 / P5928 / P6092.
- **Try next:** the CaveManager's load path (who fills +0xd78 / +0x1480 / +0x14b8, L47 / L51) for a per-continent
  list; dev posts or streams on where trips landed after 30 Jun 2016; TerrainObjectDeploymentGroups.txt (L127).

### L130. The Spire's Local Travel underground portal crystals on the other landscapes (Game/WarpNetwork.cs; TASKS 3.14 F2)
- **CLOSED 2026-10-17 (pass 4, CLOSE (built)):** Per-landscape crystals (WarpNetwork.Prepare / NodesFor), client confirm cost (CostOf :363-374). Discovery: the client keeps no state [P pass 3]; ours "all open" stays a choice (trace/0695/agent-leads4.md)
- **Pass 3, 2026-10-17: ADVANCED [P]** — Node +0x24 is not a discovered flag. It is the node's location key, compared from-vs-to for HubCost. The client has no discovery state; the +0x248 default (11674) has no reader. Cost bug: the client shows hub → crystal = 1 Lumen, but the server charges 2. Built: warp cost fixed 88fa629. (trace/0695/agent-leads3-c.md)
- **ADVANCED 2026-10-17 (design + client):** every island [I]: the 17 Dec 2014 notes describe a leyline hub on each
  underground level through Local Travel, and a player that day complained no island (of any) showed the layers.
  **Discovery [P, 2015]:** the 5 May 2015 notes say leyline crystals (surface and caves) "can be teleported to using the
  Local Travel option" once discovered — ours opens every node. The 0.695 warp manager's constructor (FUN_1435031a0,
  part_0108 ~13700) stores the cost item 1579 and composite 11674 (the "Leyline discovery" fanfare) among its defaults;
  the node record (reader FUN_143502390) is {u64 node, u32 type, u32 biome, u64 cell, f32 x, y, z, u32 +0x24} — the
  trailing u32 is unread by us and may be the per-player discovered flag [I]. Try next: the reader of node +0x24 and
  of manager +0x248 (the fanfare) to prove a discovery mechanic, then build per-character discovered crystals.
- **ADVANCED 2026-10-17 [runtime, harness]:** on landscape 391 (Arid Thicket) the server sent "warp network 91/391: 82 node(s) in 2 layer(s)"; `terminal hub 1` opened Local Travel and `luacode Teleport.SetCurrentLayer(1)` showed 391's underground crystal markers (scratchpad l130c-391-l1.png; layer 0 the hub, layer 2 none). So the client takes and draws a network for a non-home landscape. Note for tests: 391 loads slowly the first time (stuck ~50 % for minutes, allocator slots) — wait for "loading screen is now hidden". Whether live listed crystals off the home landscape stays [I].
- **Sweep 2026-10-17: NEEDS-RUNTIME** — — (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether live's warp network (WarpNetworkZoneNetwork 0x113 sub 2) listed underground portal crystals on
  every island, and which hub each used; today the server sends them for the home continent only (NodesFor:
  OnHomeContinent), while every landscape now has Chaos Caverns, outposts and treasure chests.
- **Found so far:** the packet carries (world, zone) and the client files networks per landscape (FUN_143454ec0); every
  island's Underground .cpa has exactly 81 Graveyard01 pieces (tile 111), as 406 (counted 2026-10-16 over the 39-byte
  tile records); Spire.HubGuidOn(zone) exists per landscape.
  Not built: per-landscape nodes and crystals (guid space per landscape, Enter on re-zone).
- **Searched:** Game/WarpNetwork.cs class notes, coverage-695.md 0x113 (both subs "same").
- **Try next:** check the client's Local Travel map on another island with a network sent (test copy); then build
  per-landscape nodes (crystal guid = landscape base + n, as the outposts).

### L131. Lost Crystal trips to "other worlds" (P5734, 7 Jun 2016)
- **CLOSED 2026-10-18 (built 7e9af69, two-world harness verified):** "other worlds" = servers; trips hand over to another world of the gateway (client showed Courage); CAVERN_TRIPS_OTHER_WORLDS default true. [I]: even world weighting, no return trip.
- **Pass 3, 2026-10-17: UNCHANGED** — No post-7 Jun report of a trip landing on another world. (trace/0695/agent-leads3-b.md)
- **Pass 2, 2026-10-17: ADVANCED (reverses the sweep) [I strong]** — 2016 dev notes always use "world" for the server ("between landscapes or worlds", "same world to remove a group member", "on a new world called Events"). P5734 most likely means other worlds (servers), not only landscapes. Built: NextCave prefers another landscape and cave biome (01e6ad6). (trace/0695/agent-leads2-c.md)
- **Sweep 2026-10-17: ADVANCED** — The game's own "other worlds" means the Travel window's Other Worlds button → landscapes (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether a Lost Crystal could send a player to another world server (live had several per region) or the
  note means other landscapes; and if so, how the handover looked (the client's NextUndergroundReply has only a u64).
- **Found so far:** our world servers can hand players over (CrossWorld.Travel, TASKS B.10) but a crystal trip stays on
  this world's landscapes (Outposts.NextCave). The audit read "other worlds" as out of scope while one world runs.
- **Searched:** audit-outposts-695.md (P5734 row), patch notes, Travel.cs.
- **Try next:** forum / Reddit posts after 7 Jun 2016 describing crystal trips (world name in chat after a trip); the
  client's handling of a ClientBeginZoning carrying another world id after a NextUndergroundReply.

### L132. Live's navigation mesh: format, parameters and what page 32 counted (TASKS 6.61)
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client-side or archived source can show more; the server's current behaviour is documented per lead in trace/0695/agent-leads4.md (numbers ours [I] where noted).
- **Pass 2, 2026-10-17: ADVANCED [P RTTI + forum]** — There is no DebugPathQueryResponse class. Players' 2016 reports describe a voxel-aware navmesh that was rebuilt on edits and built lazily. (trace/0695/agent-leads2-d2.md)
- **Sweep 2026-10-17: ADVANCED (neg)** — No navmesh library in the 0.695 RTTI (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** how live's zone pathing built its navigation meshes (cell size, agent radius / height, climb, how voxel
  sections were "stitched", what "dynamic silhouettes" and "avoidance obstacles" were — props and creatures? P5781 says
  props' collision areas blocked creatures), the 0x1E / 0x4AA PathingDataMonitorUpdateResponse layout that fed page 32,
  and the 0x4A7 / 0x4A8 DebugPathQuery pair (request = two points + a byte from a two-click debug state, FUN_1436e3780).
- **Found so far:** page 32's field names and offsets (admin-pages.md), the request's channel string "Pathing" (not
  "ZoneCommand": a separate pathing back end?), the registry names 0x4A2-0x4AA, the Storybricks per-character
  "Pathing: %s" debug line and the `sb_pathing` draw toggle (pages 30 / 31). Ours is a column grid with our own numbers
  (trace/architecture/pathing.md §2-3, [I]).
- **Searched:** re/decomp_all_695 strings (navmesh, NavMesh, Pathing, Silhouette, Avoidance, hkai: no Havok AI names),
  coverage-695.md, families/001E_Command.md, the fileindex of both installs (no navmesh files by name), patch notes
  (P0805, P4225, P4266-P4267, P5265, P5781, P5924, P5979).
- **Try next:** the 0.695 exe's statically linked libraries for a navmesh library (RTTI / class names around the
  Storybricks code); the 0x4A8 response's reader if any (pkt.py on the registry entry); dev streams of the creature
  pathing demo (P4321, 22 Mar 2016 livestream) for what creatures did on build sites and around props.

### L133. Player-edited terrain and props in creature navigation (TASKS 6.61)
- **CLOSED 2026-10-17 (built 73dc97c, harness round 60):** site solidity grid + prop footprints; story creatures path around player builds. Still [I]: live's use of PATH_BLOCK_RADIUS, crates not blocking.
- **Pass 4, 2026-10-17: BUILD (behaviour [P forum], mechanism ours)** — Story creatures keep a straight step on build sites (Creatures.NavMode returns null for `c.Story`), so they walk through player walls; live's pathed around walls / props (P5781, 2016 reports). Spec B1: a build-site solidity grid from the Environment Server + prop footprints (trace/0695/agent-leads4.md)
- **Pass 2, 2026-10-17: ADVANCED [forum]** — On live, creatures pathed around player walls and doorways and broke when a floor material changed, so edits did reach the navmesh. (trace/0695/agent-leads2-d2.md)
- **Need:** a cheap way for the creature navigation to see player edits — dug tunnels / filled holes in the Chaos
  Caverns, builds and dug ground on build sites (story creatures), and placed props as obstacles (live: P5781).
- **Found so far:** the grid reads only the generated world (cave pieces, baked surface heights) and the stamped ruins;
  story creatures on build sites therefore keep the old straight step, and cavern creatures don't see player digging.
  Reading edited cells per column would expand them (~0.4 MB each) or ask the Environment Server per step (latency).
- **Searched:** Game/Pathing.cs design notes; TerrainEdits / EnvironmentServer APIs (no coarse solidity summary).
- **Try next:** a coarse solidity summary per edited cell kept by the Environment Server (1.2 m blocks, like Sight's
  ruin grids) and pushed with its CellVersionsChanged, so the zone can drop and rebuild the nav tiles of changed cells;
  prop footprints (Game/ModelFootprints) as blocked columns on build sites.

### L163. Desperado's Deadly Arsenal (3038): which button ran each of its three named attacks
- **CLOSED 2026-10-17 (built 80a386c):** Desperado three-attack kit from input group 78 and rows 703-711 [I timings].
- **Pass 4, 2026-10-17: BUILD (optional, lowest) + FOUND [P]** — Desperado's input group 78 has three casts: LMB 793, RMB 794, E 795 [P]. The four "never craftable" weapons (3028, 3029, 3038, 3039) are exactly the launch weapons with an E cast [P] → a 3-attack generation that never went live [I strong]. Its attack damage formulas are client rows 703-711 [P]. Spec B4 (trace/0695/agent-leads4.md)
- **Need:** the slot (Basic / Power / Special E) of 573167 "Run n' Gun" (desc 573170: revolver shot, 25 % chance of
  Dirty Fighting 573184 for 4 s; params 703 / 706), 573168 "Get 'em Good" (573171: shank stab, Dirty Fighting = bleed
  3 × over 3 s; 704 / 707) and 573169 "All or Nothin'" (573172: dive forward with a mid-air burst, then a shank hit;
  710 / 708 / 711 / 709). Every other kit has two named attacks (Basic + Power; WeaponCombat.Kits), so this weapon used
  the Special (E) slot too, or a combo [I].
- **Searched (2026-10-17):** client tables (no row names 573167-573172), the wiki dump (no page for the weapon) and the
  forum scrape (no hit for its name or attack names); its recipe 3999 is retired (RecipeBook.RetiredRecipeItems).
- **Server today:** GenericKit ("Basic Attack" / "Power Attack", Affliction).
- **Try next:** the weapon's model → WeaponAnimationClasses → InputActionGroupEntries for that class (does it have an E
  cast?), AbilityParameterDefinitions rows 703-711 (which are DoT vs hit), videos / dev posts of the weapon.

### L164. RequestPlayerPositions (Command 0x1E sub 0x24 → 0x25): flags, element bools, who called it
- **CLOSED 2026-10-17 (pass 4, CLOSE-BY-DESIGN [P neg]):** No `RequestPlayerPositions` string in 0.589 UI/ScriptsBase.bin, UI/Admin (DebugAIWindow, DebugStateMachineWindow, TemplateView), UI/TestMod, nor in the 0.695 UI folder. No shipped caller in either build, so the flags / bools never reach a live player (trace/0695/agent-leads4.md)
- **Need:** the meaning of the request's two u8 flags (sender FUN_1435d3cb0 writes 1, 1), each PlayerPosition's three
  bools and four u32 (FUN_143385790; we write x, y, z, 1 as floats [I]), and who called `Ui.RequestPlayerPositions`
  (FUN_1435c8060) on live — no shipped 0.695 script does; the client's case 0x25 reads and frees the list.
- **Searched (2026-10-17, agent chat5):** UI/Default and Resources/Scripts Lua, UI/ScriptsBase.bin, re/as3, re/as3_695.
- **Try next:** the 0.589 ScriptsBase.bin and UI Lua; a Frida log of the native if a caller turns up.

### L165. Outpost rotation: live's period, share and trigger
- **CLOSED 2026-10-18 (pass 4, CLOSE-BY-DESIGN):** no client source for live's period; P5830 (16 Jun 2016) shows live placed outposts at run time [P], already followed. Rotation is a setting (OUTPOST_ROTATION_HOURS, default off = live's "at all times" placement, P4808). (agent-leads4.md §2)
- **Need:** how often live moved outposts between ruin slots and how many at a time (built behind OUTPOST_ROTATION_HOURS,
  default off, 779229a). Sources so far: help page ChaosCavern_2Outpost_FTE.html "change regularly" (may mean garrison
  state), P4808 "at all times", May 2016 players asking to "cycle them with each server reset".
- **Try next:** forum threads after June 2016 on outposts moving; the Environment Server page's Desired / Active /
  Failed / Last ID counters per ruin layer (admin page 33) in any capture; DespawnExportedTemplateRequest 0xF4/0x19 use.

### L166. HeroicMovement 0x0105 sub 3 from the 0.695 client is unreadable to the server
- **CLOSED 2026-10-17 (fixed 158ff76):** HeroicMovement {u16, u8 sub, u64 entity}; sub 3 SwimmingUpdate; relay restored (regression since b653b73).
- **Need:** the layout of the client's HeroicMovement (0x0105) packet, sub 3 (body seen: 03 01 00 00 00 00 00 00 00 00
  00 00 00 00), sent once around zone-in; the server logs "unreadable" (seen before and after 2026-10-17's numbering
  change, so not caused by it).
- **Try next:** the 0.695 writer of HeroicMovement (grep the decompile for the class name), then a server model.

### L167. Swimmers' state for players who come into range later
- **CLOSED 2026-10-18 (built ac6ecf8):** the client keeps the last SwimmingUpdate per character and has no full-state path on entity add, so the server now queues the swimmer's current state after AddLightweightPc (HeroicMovement.IntroduceSwimmer from PlayerManager.Introduce; dive flag set when under the surface [I]).
- **Need:** whether live sent a swimmer's current SwimmingUpdate (HeroicMovement sub 3) to a player arriving in range;
  the relay (158ff76) only forwards changes.
- **Try next:** the client's swim receiver for a full-state path on entity add; forum reports of swimmers shown standing.

### L168. Live's movement-effect rows for sprint / boots / glide (follow-up of L160)
- **Need:** live's AbilityParameterDefinitions rows and durations per ability, hold / channel semantics (TRIGGER_ON 2 →
  CancelCast [I]), Dash Soles / Roll Wraps movement, Leaper's take-off damage (row 429?).
- **Built so far:** Game/MovementEffects.cs with shapes from the 14 Oct 2014 dev post [I].
- **Try next:** forum / video measurements of sprint speed and jump heights; the boots' wiki pages; a capture.

### L169. PurchaseBundleWithInGameCurrency 0xD0: meaning of Lua args 1 / 3 and the 0x4B result fields
- **Need:** which argument is the bundle and which the quantity (arg 2 is checked but not sent); what the 0x4B u32s mean.
- **Searched:** luabind_695, b695 UI natives, lua_all, as3 / as3_695, decompile callers (no shipped caller).
- **Try next:** other client builds' scripts; runtime capture if a caller turns up.

### L170. Did live melee use server-started targeters (PlayerUpdate 0x27/9)? (from L140)
- **2026-10-18 FOUND [P] + BUILT (2fc0c49 / merge d11cead):** CasterInfoReply +0x118 targeter list (entry: u32 id, u32 type ^0xA142E048, u32 origin, u32 follow, f32 delay, u32 max targets, f32 duration, f32 start / end angle, f32 re-hit s, f32 radius, shapes); melee Cone swings send a Sweep; FindTarget 0x27/0x24 {u64 hit, u64 cast guid, u32 cast hash, u32 targeter, 16 × 0} checked against the server cone; ClientTargeterEnd 0x27/0x23 closes; no report → server resolves after the window (MELEE_TARGETERS=true). Block = attacker's 'was I blocked' check, not sent. Open: our creatures passing the client's attackable check, live's bit-45 / block component, sweep numbers.
- **Need:** whether ClientTargeterEnd / FindTarget were reachable on live (only after the server starts a targeter).
- **Searched:** the client's ability table is stripped (agent-l140.md).
- **Try next:** forum / video descriptions of melee targeting; patch notes on 'targeting'.

### L171. BaseBroadcastPacket 0x4F: an operator "system broadcast" (agent-s2c-sweep.md §5 S2C-a)
- **Need:** the exact body of 0x4F and whether live ever sent it; if wanted, an operator broadcast from the web admin
  (live default: not sent; a toggle per the live-default rule).
- **Known [P]:** handler FUN_142cd9130 prints "System Broadcast: %s" / "Broadcast from %s: %s" (System / Zone / World
  formats, Global.Text.%d ids) in chat and queues the `BroadcastMessage` on-screen notification (NotificationManager
  defines it). The client has no sender: AdminCommandPacketBroadcast* exist only as id-table names, no compiled class.
  Proposed body {u16 kind (0 player / 1 system …), u32 text id or 0, str sender, str message} [I].
- **Searched (2026-10-18, s2c sweep):** callgraph walk of every never-sent S→C handler in re/decomp_all_695 (Lua /
  notification / chat / system-message sinks); design/ wiki and forum (no live mention).
- **Try next:** pkt.py on FUN_142c17410 for the layout; forum / patch notes for broadcast or maintenance messages;
  ask the user whether an ops broadcast is wanted.

### L172. PacketClientPendingKickFromServer 0x65: countdown kick warning (agent-s2c-sweep.md §5 S2C-b)
- **Need:** whether and when live sent it (admin kick, shutdown, idle kick) and its body (the seconds).
- **Known [P]:** zone dispatcher case 0x2b → ShowSystemMessage `KickedFromServerTimedMessage` with a seconds
  countdown. No live trigger found. Related: L68 (0x4C shutdown is only a chat line).
- **Searched (2026-10-18, s2c sweep):** the handler's sinks; design/ (no kick / maintenance warning found).
- **Try next:** the forum for kick or maintenance warnings ("disconnected in …"); the handler's reader for the field
  layout; it could front `/kick` or the shutdown notice.

### L173. WeaponHitFeedbackPacket 0x32/0x10: a hit-confirm sound (agent-s2c-sweep.md §5 S2C-c)
- **CLOSED 2026-10-18 (leadsfix, FOUND [P]):** the 12-byte WeaponHitFeedback layout has no effect on 0.695 (ReticleHandler is missing; the sound data has no GLOBAL_HUD). No server change.
- **Need:** whether live sent it on each hit (an audible hit confirmation) and its body.
- **Known [P]:** case 0x10 → FUN_1433ff920: ReticleHandler:OnEnemyHit / OnAllyHit (undefined in 0.695) **and**
  FUN_1431f8000 (SoundPlayer DAT_145431358) picks a sound from its ActorSoundEmitter config (+0x3d0) and plays it
  (FUN_143db04a0). The visual hit marker comes from health updates (Health.cs). re/tools/verdicts.tsv: parked.
- **Searched (2026-10-18, s2c sweep):** handler sinks; wiki / forum / notes (no "hit sound").
- **Try next:** read the SoundPlayer config at +0x3d0 (hash 0xcfd6cc9b, FUN_1431f76a0): if it names Landmark combat
  sounds, live used it; then the body layout (pkt.py) and a live-default toggle.

### L174. Guild invitations list, News row removal, full guild data (agent-s2c-sweep.md §5 S2C-d)
- **Need:** whether live sent GuildInvitationsPacket 0x83/0x14 (pending invites, e.g. to players who were offline),
  GuildRemoveLogUpdatePacket 0x83/0x1B (removing a News row) and GuildDataFullPacket 0x83/0x12, and what the 0.695
  guild window shows for each.
- **Known [P]:** FUN_142ef25f0 fills the client's pending-invite list (+ FUN_1433da9d0); no Lua / notification sink
  for the three. Server today: invites re-offered after a relog within 5 minutes, News rows (GuildLogEntries).
- **Searched (2026-10-18, s2c sweep):** handler sinks in re/decomp_all_695.
- **Try next:** the 0.695 GuildWindow AS3 data sources for invitations and news; the forum on whether invites survived
  logout; GuildUpdateAppData against the server's AppDataFieldSub.

### L175. PacketTargetClientNotOnline 0x1A: which live path sent it (agent-s2c-sweep.md §5 S2C-e; follows L67 d)
- **Need:** the live path that produced it (the world wrapping a client message for a target,
  ClientToTargetClientsZoneWrapped 0x19), so the server knows when to send it.
- **Known [P]:** {StringFixed<32> name}, zone case 6, chat line PlayerNotOnline = text 560709 (L67 d,
  agent-leads2-d1.md). No 0x19 client sender exists. Whisper to an offline player is already answered as PacketChat
  kind 3 (Chat.cs:261).
- **Searched (2026-10-18, s2c sweep):** handler sinks; the client's senders (none for 0x19).
- **Try next:** which client messages the world wraps with a target; candidate use: tells / guild lines when the
  social back end is down.

### L176. InstancedGamePlayerRemovalPacket 0x10D/0x14: side effects our removal may miss (agent-s2c-sweep.md §5 S2C-f)
- **Need:** whether our removal sequence (Membership(false) + State 0 + ObserverRegistration 0, InstancedGames.cs:1045)
  leaves chat channel 0x11 or observer camera mode 0x20 set on the client, which live's removal packet resets.
- **Known [P]:** case 0x12 → FUN_1434e8cd0 → FUN_1434e2330 clears game / observer (+0x38 / +0x40), runs
  CloseEndGameScoreboard (global.lua) and OnCloseGameInProgressDialog, and resets chat channel 0x11 (FUN_14336f480).
- **Searched (2026-10-18, s2c sweep):** handler sinks only.
- **Try next:** read FUN_14336f480(…, 0x11) and FUN_142d4eaa0; a harness check after leaving a site game (chat
  channels, camera); or send 0x10D/0x14 after our sequence and compare.

### L177. Recipe cooldowns and closing the crafting window (agent-s2c-sweep.md §5 S2C-g)
- **Need:** whether live sent PlayerRecipeDataUpdatePacket 0x42/0x1E (data names CooldownRemaining / CooldownTotal:
  recipe cooldowns) and RecipePacketZoneCraftingWindow 0x42/0x24 to close the station window when the player walked
  away.
- **Known [P]:** ZoneCraftingWindow {u32, u32, bool}: bool 0 closes, 1 refreshes (L143; FUN_142f39870 → FUN_142f381b0 /
  FUN_142f3c630(…, 1)). Both are data-driven or a window close, so the sink scan does not cover them.
- **Searched (2026-10-18, s2c sweep):** handler sinks.
- **Try next:** the CraftingWidget AS3 for CooldownRemaining; recipe / station tables for cooldown columns; the forum
  for the crafting window closing when walking away.

### L178. How a ProxiedProp's model actor gets its motion state (agent-platform-collision.md §8.5 L-pad-actor)
- **CLOSED 2026-10-18 (leadsfix, FOUND [P]):** FUN_142eb1950 C14 pushes the interpolated pose into actor +0x6E0's graphics object (vfunc +0x40); gated only by the 0x4D local-management release (round 88).
- **Need:** the writer of actor +0x2D8 (the actor's motion state) for a ProxiedProp, so the visible Magical Platform
  model follows the 0x7F records.
- **Known [P]:** writers seen so far: actor vfunc +0x100 FUN_143d0cf70, called directly only by FUN_142ec0000
  (projectile physics), and the entity +0x240 handle path FUN_143501530. Round 81: pad spawned with mode 2 and owner 0,
  records every 500 ms, the model still looks static, the rider sends ~33 updates/s.
- **Searched (2026-10-18):** the whole 0x7F → SetTransform path gate by gate, statically (agent-platform-collision.md §8).
- **Try next:** the round-82 hook (hook_platform.js, §8.4): if `moved` > 0 with a static model, trace ProxiedProp's
  actor creation (vfunc +0x58 by +0x1EB0, DAT_145431080).

### L179. The rider's ~33/s movement bodies on a moving pad (agent-platform-collision.md §8.5 L-pad-rider-bodies)
- **CLOSED 2026-10-18 (leadsfix, FOUND [P layout]):** the platform block travels only in type 0 bodies and the relay forwards it unchanged. Built: time-ordered flush per mover (a68c880, MovementFanout.InTimeOrder). Runtime split of body types: hook_leadsB.
- **Need:** a server-side log of the rider's movement bodies while on the pad (flag 0x10000000, +0x80 platform
  transient id, +0x70 offset): it tells whether the client already treats the pad as moving (gap actor-only, L178).
- **Known [I]:** FUN_142ecc7f0 adds flag 0x10000000 and the platform transient id at +0x80 when the rider's ground
  object is a moving entity (agent-e3-platform.md §2.3).
- **Searched (2026-10-18):** static only (agent-platform-collision.md §8.1 point 5).
- **Try next:** a debug log of those bodies in the world's movement handler; check that the positions travel along the
  pad's leg.

### L180. New-player site slate slab (audit-wiki-gaps.md gap 12)
- **Built 2026-10-18 (leadsfix 40ebd6d):** claim3x3 template (525x5x524, 763,000 solid voxels) stamped solid-only under new Pioneer surface sites, material PIONEER_SLAB_MATERIAL (26), top layer on the ground voxel, 40 m Starting Location corner left as ground; PIONEER_SLAB claim3x3|claim1x1|off; dev `pioneerslab`. **Still open:** which template live used and its exact height. Try next: wiki image Landmark-pioneer-landscape.jpg, Verlin EQL's Pioneer video, an older VoxelMaterials table (was index 53 ever a slate?).
- **Need:** the shape, material and placement of the "slate slab" on new players' automatically placed sites.
- **Known:** update notes 30 Jun 2016 (design/forum/patches/2016-06-29-57596): "Corrected some issues with the slate
  slab placement on new player claims." [P]; probably a floor under the Starting Location [I]. Server today: the new
  Pioneer site gets only the Starting Location (Pioneer.cs Place; Claims.EnsureStartingLocation).
- **Searched (2026-10-18, wiki-gaps audit):** the 2016 patch notes and developer posts.
- **Try next:** Daybreak / starter designs, ExportedTemplates, the tutorial goals ("Your Home is Your Castle",
  Building Block Basics); forum threads around 29 Jun 2016 on new-player claims; then stamp it under the Starting
  Location on new Pioneer sites.

### L181. Voice chat (Vivox, Voice 0xB9): what live sent, and a clean "unavailable" answer (audit-wiki-gaps.md gap 13)
- **Layout documented 2026-10-18 (leadsfix ac7d358):** VoiceLogin / JoinChannel field roles and channel types in VoiceHandler comments and docs/protocol.md; the server sends no 0xB9 (the clean "unavailable" state). **Still open:** live's Vivox account naming, URL and channel URIs; self-hosted voice.
- **Need:** the 0xB9 layouts, what live sent (Vivox account / channel URIs for the Local and Guild channels), and
  whether the client survives a "voice unavailable" answer.
- **Known:** wiki Chat: voice Local / Guild channels, push-to-talk [W]; client VivoxWrapper and VoiceChatWidget
  (trace/UI-AUDIT.md:881-888) [P]. Earlier notes wrote it off (trace/systems/social.md:45 "not needed",
  social-service.md:168 "out of scope"); reopened here (nothing is written off). Server today: Telemetry.cs:94 only
  logs VoiceChatEvent.
- **Searched (2026-10-18, wiki-gaps audit):** wiki Chat, the client UI audit.
- **Try next:** the 0xB9 family readers and the VoiceJoinChannel sender in re/decomp_all_695; answer VoiceJoinChannel
  cleanly so the widget does not wait [I]; self-hosted voice stays a later option.

### L182. Travel to Friend cooldown (audit-wiki-gaps.md gap 15)
- **CLOSED 2026-10-18 (leadsfix, FOUND [P], built 6031b28):** 2016 had no cooldown (removed 5 May 2015); default 0. A non-zero FriendTeleportCooldownSeconds goes into 187's caster info +0x70 (→ record +0x14c, timer keyed by +0x7c, started on CastSuccess when +0x80 set) and the client refuses with 562080 itself. Stat 53 has no client reader. Follow-up: L188 (185 / 186).
- **Need:** the cooldown length of Travel to Friend (ability 187) and the meaning of calculated stat 53
  cStatTeleportCooldown.
- **Known:** wiki Chests: "There is a 'cool down' period for traveling instantly, so teleport to friend may not work if
  it was used recently." [W]. Server today: never refuses a repeat; stat 53 is sent as 0 (Abilities.cs:442-470,
  DefaultCalculatedStats.cs).
- **Searched (2026-10-18, wiki-gaps audit):** wiki rule pages.
- **Try next:** the ability record / CasterInfoReply for 187 (cooldown field); the client's readers of stat 53 (a
  hotbar cooldown?); the forum. Then a server setting, off until a source-backed default exists.

### L183. Mail attachment stack cap of 9,000 (audit-wiki-gaps.md gap 16)
- **CLOSED 2026-10-18 (leadsfix):** the 9,000 cap is server-side; the client has no cap. Already built (Mail.cs OverStackCap). Only source the wiki [W].
- **Need:** whether live capped a mailed stack at 9,000, and whether the client or the server enforced it.
- **Known:** wiki Trading: "The largest stack of gathered materials … that can be sent at once it 9,000." [W]. Mail.cs
  has no cap.
- **Searched (2026-10-18, wiki-gaps audit):** wiki Trading; Mail.cs attachment checks.
- **Try next:** the client's mail compose / attach path for a limit; if there is none, a server cap with result 5
  ("attachment is invalid").

### L184. Live's trade delay on store purchases (audit-wiki-gaps.md gap 7)
- **CLOSED 2026-10-18 (leadsfix, built e225311):** 12 h [P dev, forum 2014-03-26-26036]; STORE_TRADE_WAIT_HOURS default 12; tooltip reply u64 at item record +0x120 = TradeableAfter Unix seconds → Lua FraudTimer → "UI.TradableInWithTime". Open sub-point: whether ClientItem add / update packets also carry the timer.
- **Need:** the waiting period before Marketplace purchases became tradeable, and whether the client's item record
  carries the time.
- **Known:** client text 577356 (Fantasy Parlor Pack): "there is a waiting period before purchased items become
  tradable" [P]; wiki Trading "12-24 hours", wiki Pocket "after a time limit" [W]. Server today: store items are
  tradeable at once (Store.cs:485). Proposed: TradeableAfter + STORE_TRADE_DELAY_HOURS, default 24 [I]
  (audit-wiki-gaps.md §2 item 6).
- **Searched (2026-10-18, wiki-gaps audit):** wiki Trading / Pocket, client texts.
- **Try next:** dev posts and patch notes for the exact period; the item record / tooltip readers for a
  tradeable-after time.

### L185. Player Studio: live's approval / rejection counts, reviewer rules, staff queue (audit-wiki-gaps.md gap 10)
- **Reasons built 2026-10-18 (leadsfix 1104357):** the staff queue shows the peers' verdict codes ("Rejected for: …"; type names [I] from the 18 Jun 2014 notes). **Still open:** live's approval / rejection counts (ours 3 / 3) and subtype names. Try next: archived Player Studio FAQ pages.
- **Need:** how many peer approvals / rejections decided a product on live, who could review, and how the staff queue
  after a rejection worked.
- **Known:** wiki Player Studio: reviewers had spent "a minimum of 1000 Daybreak Cash"; the creator's name does not
  appear; "If an item is rejected, it will then be queued for review by staff" [W]. Server today: anyone but the
  creator reviews, the review copy names the creator, enough rejections set Rejected with no staff step
  (Commerce.cs:993-1012, 1099-1104).
- **Searched (2026-10-18, wiki-gaps audit):** wiki Player Studio / Showcase.
- **Try next:** forum and dev posts on Player Studio review thresholds; the review popup's client texts.

### L186. How far GetStateAtTime extrapolates past the newest 0x7F record (audit-bandwidth-2026-10-18.md R7 d)
- **CLOSED 2026-10-18 (leadsfix, FOUND [P], built 6fdcf7f):** the extrapolation limit is +infinity (agent-leadsB); straight legs (|Quaternion.Dot| > 0.99999) at 1000 ms, turning legs 500 ms.
- **Need:** the extrapolation limit K: how far (time) control-mode-2 playback (FUN_1435a53e0) runs along the record's
  velocity (+0x1F0) past the newest record before it holds.
- **Why:** R7(d) (1000 ms records on platform legs with no turn: ~50 % less moving-platform traffic, about 126 → 63 B/s
  per viewer) is safe only if the client extrapolates at least that far; otherwise the platform moves in steps. Until
  then the 500 ms rate stays.
- **Known [P]:** it picks the two records around the render time and blends them; it "extrapolates a little past the
  newest and then holds" (agent-platform-collision.md §8.2 C11). The tail of the function has not been read.
- **Try next:** read the tail of FUN_1435a53e0 in re/decomp_all_695 for the clamp; the history and render times in
  the hook_platform.js log.

### L187. Control mode 2 render delay (agent-platform-collision.md §8.3; audit-bandwidth-2026-10-18.md §2.2)
- **CLOSED 2026-10-18 (leadsfix, FOUND [P]):** render delay = max(age, 0.15 s); the pad model, its collision and a rider share it. No server change.
- **Need:** the delay the client really uses for mode-2 playback, whether the visible pad then lags its collision body
  and rider by that delay, and what live's clock sync gave for it.
- **Known [P]:** age at arrival = entity +0x1684; delay +0x48AC = max(age, 0.15 s); render time = clock − delay;
  GetStateAtTime works within the last ≥ 1 s of records. A wrong clock offset only shifts or flattens playback (our
  records carry ServerClock.Milliseconds; Synchronization 0xC3 answers with the same clock).
- **Try next:** the hook's age1684 and render times against the server's send times; compare the rider's positions
  (L179) with the pad's records.

### L188. Spire / My Site travel cooldown (abilities 185 / 186) (leadsfix 2026-10-18, follows L182)
- **Need:** live 2016's cooldown, if any, on Travel to Spire (185) and Travel to My Site (186).
- **Known [P]:** both still use the 1098 capture's caster info: +0x70 = 60 s with +0xa4 = 1, so the client enforces a
  60 s cooldown itself (same path as L182: record +0x14c, CastSuccess starts the timer). Whether live 2016 sent that
  is unknown; the capture is from the 0.589-era build.
- **Try next:** 2015-2016 patch notes and dev posts on travel cooldowns (design/forum, design/wiki Travel / Spire
  pages); then a setting per ability (live default once a source exists).

### L189. SendSelf CastKeySeed (+0x753f0) (ss4 2026-10-18)
- **Need:** whether live randomised the seed per login, and what the server did with the per-cast key it feeds.
- **Known [P]:** FUN_142e0f810 ("send cast to zone") mixes it into a per-cast value (→ FUN_142e04a90); the only live
  capture sent 0x5BB9EB0B. The server sends 0 and never checks the cast key.
- **Searched:** 0.695 decompile users of +0x753f0; server code.
- **Try next:** the cast request's field that carries the mixed value and whether any server reply echoes it; a second
  capture (other session) to compare the seed.

### L190. CurrentAdventureId (+0x114) and PlayerUpdate 0x36 SetCurrentAdventure (ss4 2026-10-18)
- **Need:** the meaning of SetCurrentAdventure's payload and the +0x118 / +0x130 / +0x148 arrays.
- **Known [P]:** only PlayerUpdate 0x36 (dispatcher FUN_142cdfa60 case 0x35, reader FUN_142c2b4b0) writes them; no
  client reader of the stored values found. Live sent 0.
- **Searched:** player-global, +0xf18 and ClientPcData-method accesses.
- **Try next:** Lua / AS3 names with "Adventure" (re/as3_695, UI scripts); the 2016 Adventure (game) system docs in
  design/wiki; xrefs from the GameFinder / instanced-game code.

### L191. February 2017 build: strong fingerprints of Landmark64.exe, Locale\en_us_data.dat / .dir — **done 2026-10-20**
- **Had:** SHA-256 of the three Feb 2017 files so LaunchPad can verify that build fully.
- **Resolved:** genuine copies matched Daybreak size+CRC32; pinned in `landmark-0695-files-sha256.csv`. Exe build
  string is `0.699.1.132863` (January's is `0.695.1.132647`); pack version stays `0.695.1.132647`.
  - `Landmark64.exe` 91222528 / ED231C3C → `84D465705AEAA98E9D176DE5EAF02BD17649133FBC411001553D64B38B22CBF5`
  - `Locale\en_us_data.dat` 1394363 / 10E110DF → `C455D39621241D8FAF66DC223C55F7837C09A46CB7796F4FCDEC4B7169753527`
  - `Locale\en_us_data.dir` 636374 / 7C48670B → `08B9EA2A8C902AA14B381C15A81E47D5CBB269616C618E5A7C037E414A18180A`
- **Prior notes kept:** CDN blob path still can't reconstruct hashes without Daybreak's key; AllowFebruaryWeak is now a
  no-op on current LaunchPad.

### L192. Emote / animation details not provable without a live capture (animaudit 2026-10-18)
- **Need:** (a) the live packet for emote chat lines (we send them like /playdead's); (b) whether /talking ends on move
  (then remember it like a stance); (c) /yell vs the client's shout command; (d) "/emote" free-text vs our "/emote <n>";
  (e) whether the six-shooter shooter's own client animates its shots; (f) which clip live played for potions (UsePotion
  287?) and dynamite on watchers; (g) stance / mood on the player's own client across a cross-landscape re-zone;
  (h) building pose and revive as seen by watchers; (i) shields (off hand slot 11) on players / creatures.
- **Try next:** client tests for (b) (c) (d) (e) (g); 2016 videos for (f) (h); decompile for (c) (d).

### L193. Hotbar placement on the active bar (invequip 2026-10-18)
- **2026-10-21 (auditshift, docs/client-to-server-shifts.md Shift-A1..A3):** 0.589 preferred the bar on screen (action bar manager +0x78); 0.695 never sends the active bar (Ui.SetActiveActionBar FUN_1435c8290 sends nothing). Also open: whether live placed the slot when the equip was refused, and whether a default (type 1) slot holding the item counted as "already on the bar". Next: a runtime capture of 0.695 switching bars.
- **Need:** which bar live placed a right-clicked tool on (the client never tells the server the active bar; ours
  starts at bar 0), and what ToolBase "switched build tool to Location (7)" right after an equip means.
- **Try next:** client's active-bar index in the ActionManager data source / Lua; the ToolBase sender after equips.

### L194. Weapon channel limits, charged follow-ups, Recon volley numbers (weaponinput 2026-10-18)
- **Need:** live's longest hold / per-hit numbers for the channel attacks (ours: 3-5 hits, the 2 s block), whether a
  charged Basic's follow-up (1045, 1057, 825, 822, 826, 816, 1164) was charged too (inferred from the longer follow-up
  windows only), the Recon volley's reach / arc / shot count / spacing / 6 Energy cost (ours), and which animation index
  the left-click follow-up played (sword index 1 = 316/317 lunges forward — the user saw it as "a charge").
- **Searched:** AbilityEx (empty), AbilityParameterDefinitions, wiki weapon pages, patch notes, the live SendSelf
  loadout (zeros), PacketDumps (no weapon ability-info replies).
- **Try next:** a live 0x27 capture (cast-continue / cast-success timing), 2016 combat videos, the animation network's
  index conditions for the combo step.

### L195. Why very deep cavern arrivals get no collision (cavefall 2026-10-18)
- **Need:** the client-side reason a player arriving >~1,600 m below the ground (graveyard 38 on 406: 2,028 m below a
  1,423 m mountain) falls through, although the plinth cell is stored and the client holds physics until the player's
  cell and the one above are loaded (FUN_143324910). Fix in place is a depth filter + fall guard (ours).
- **Try next:** Frida on the cell request / collision build for a deep arrival (LOD / streaming depth limit in the
  client's VoxelFarm view distance), client logs VoxelFarm.log / CellRequests during such a trip.

### L196. Observer-sync leftovers (auditobserver 2026-10-21; details docs/observer-sync.md OL-1..OL-6)
- **Need:** (1) guild tag under names — live showed it (hotfix P1525); client packet 0xDF {u64, string} →
  FUN_142cdf330 → entity +0x21a8, full-character string 3, DisplayPlayerGuildNames option; what live put in it is
  unknown; (2) a dead player's body on other screens (remote knocked-out handler FUN_142ead890 is empty); (3) calculated
  stats in the full character possibly sticking on observers after a buff; (4) build-preview setup for walk-ins;
  (5) mining dust / crafting-station-in-use visuals for observers; (6) short combat states for late arrivals [I].
- **Try next:** see docs/observer-sync.md per item; 2016 videos for (1) (2) (5).

### L197. Request-variant leftovers (auditcallers 2026-10-21; docs/request-variants.md)
- **Need:** the old leader's rank after a leadership transfer [I] (ours: the new leader's former rank); which
  right-click rows live showed per target [I]; Guild.RankSetDescription (FUN_1435b8b10) sends the same packet as
  Guild.SetDescription with nothing naming the rank; LuaClaimVendor.SetItemPriceBySlot "slot" meaning; feedback mail to
  a contest winner on another world (guid looked up in this world's DB only).
- **Try next:** live captures / dev posts for the guild items; a 0.695 window calling RankSetDescription.

### L198. Allocator slots on many-core PCs beyond DirectThreadCount (castleprefab 2026-10-21)
- **Need:** a way (without modifying the client) to keep threads within the 64 allocator slots on 16+ thread CPUs:
  the Resource::Manager Decompression (FUN_143a8d560) and TaskManagerJob (FUN_143a98090) pools start one thread per
  logical processor, uncapped; the design preview thread (FUN_143f99120) starts late and likely gets none. Also open:
  where the finished preview thread is released; whether MaxVoxelsForPreview (default 41,500, FUN_14219be10) is read
  in 0.695; whether every pool thread takes a slot (inferred).
- **Try next:** ClientConfig / UserOptions keys those pool constructors read (thread-count overrides?); launching the
  client with a processor affinity mask from LaunchPad (no game-file change) and measuring slots with re/tools/slots.py.

### L199. Paste area vs the client's preview (placeoffset 2026-10-21)
- **CLOSED 2026-10-25 (Frida + server log):** Identity: area vs packet = `(1,1,1)` → −1 on each axis. Turned: client
  folds min-corner shift into the packet on flipped axes and skips that +1 there (yaw90: packet−area =
  `(−sizeZ,+1,+1)`; blanket −1 put flipped axis one low vs area/preview). Server: `PasteOriginAdjust = -1` only where
  `shift[i]==0`; `Dot` matches preview `Σ s_j·row_j`. Confirmed in-game 2026-10-25.
- **Was:** turn-dependent manual nudges; size-margin guess (out — identity `area−copy 0,0,0`).

### L200. Client calls the server could make for the slot problem (2026-10-21; trace/0695/client-commands-for-slots.md)
- **Known [P]:** no reachable client call resizes the Decompression / TaskManagerJob pools; DirectThreadCount is read
  once at start-up; ~632 of ~694 console variables are developer-only and hidden in retail (FUN_142bfbdc0(1) in
  FUN_142ce9580); no server packet pushes graphics settings. GameSettings.SetMTR(0) stops the render worker thread
  (frees ~1 slot [I], costs FPS, never re-enable with SetMTR(1)).
- **Try next:** slotmap.py before / after SetMTR(0) on a full table; who reads the claim-wide LOD flag; whether render
  distance limits terrain cell loading; whether cached meshes skip contouring; whether CEF / voice threads use the game
  allocator.

## Found

- **ROOT CAUSE FOUND 2026-10-16 (Frida, test copy; re/test/teleport_trace.js + .py):** a Stalker probe of the frozen main
  thread shows it spinning in FUN_1440cd6d0 = QuerySimpleVisibility (its profiler scope; DeepVisibility\VisibilityObject.cpp;
  0.589 FUN_144009a40), walking a visibility-cell object list that has become a cycle (next = plVar3[2]). Reproducible:
  plain TeleportToLocation hops of 3 / 10 m never break it; after a hop of 25 m or more, the next hop back freezes the
  client (3 of 3 runs; a 4th run with the +0x41 wait-screen byte froze on hop 1 inside the forced engine re-centre). So a
  plain teleport that crosses a visibility cell leaves the player's visibility object linked in the old cell's list too;
  the loop shows when that cell is drawn again. A re-zone is safe because it rebuilds the scene. Not fixable from the
  server; only moves inside one visibility cell (cell size: next to find in VisibilityObject.cpp) could skip the
  loading screen. Kept: ShortTeleportDistance default 0. Other route: make re-zones faster (WIRE-TIMING logs).
- **Re-zone time measured (2026-10-16, 700 m same-landscape re-zone):** the client logs "World Ready" and sends
  ClientIsReady 1 s in (FUN_142d58210), but sends nothing for the next 7 s (no keep-alives): its main thread is busy
  rebuilding the scene, and 23 queued packets arrive together; the server answers ClientIsReady within ms. So the
  re-zone's screen time is client work; the server cannot shorten it much.

### L9. The 13 extra bytes in the 0.695 LoginRequest
- **CLOSED 2026-10-17 (pass 2, FOUND):** The client's own packet printer FUN_143a57510 names all six strings: Session, Fingerprint, Locale, ThirdPartyAuthTicket, ThirdPartyUserId, ThirdPartyId Built / done: comment names (69bb053). (trace/0695/agent-leads2-a.md)
- **Need:** which field carries the extra 13 bytes (seen by the auth server during the 0.695 test against Docker).
- **Found so far:** the request builder is identical in both builds (re/decomp_all part_0134 ~4440 vs
  re/decomp_all_695 part_0135 ~8602: same code, addresses only); every field is a string (StringFixed<32> ×5,
  StringFixed<5>), so the difference is a *value* (config, launch arguments, a build / system string), not the
  layout. The raw bytes were only in the auth container's output, gone after `compose down`.
- **Try next:** log the raw LoginRequest (both clients) on the next 0.695 login and compare field by field; then find
  where that field's value comes from in the client.
- **Found (2026-10-15):** the writer (0.695 FUN_143a54110) writes the opcode byte and six length-prefixed strings
  (credentials StringFixed<32>, <2048>, locale StringFixed<5>, three StringFixed<32>). The auth reader took strings 3-4
  as u32s; with the 0.695 locale "en_us" that read length 5 + "en_u" and left "s" + three empty strings = 13 bytes
  (the logged remainder starts 73 = 's'). Reader fixed (AuthServer LoginRequest.cs).

### L5. Gateway external opcode 9 (commands for the gateway)
- **Need:** its class name and what the gateway answers.
- **Searched:** RTTI Gateway::ExternalPackets::* (no class for 9).
- **0.695 checked:** RTTI has the same 14 Gateway:: classes, none for opcode 9.
- **Try next:** the gateway API's packet factory (ExternalGatewayApi vtable), strings.
- **FOUND 2026-10-15 (agent #8, architecture-leads.md L5):** no class exists — FUN_14337d580 / 0.589 FUN_1432d0790
  builds a bare `Gateway::ExternalPackets::BasePacket` with opcode 9 and appends the ClientCommandPacket body; the
  client's receive switch FUN_143a4bd20 / 0.589 FUN_143993e40 handles gateway opcodes 2, 4, 5, 7, 8 only, so the
  gateway answers through tunnelled packets. Which commands the gateway listed: only `/gatewayprofiler` is named by
  the client (page 24 text); the rest not found yet. (Coordinator: move to Found.)

### L6. What selects the TCP login API
- **Need:** what makes the client use Login::ExternalLoginTcpApi instead of the UDP one.
- **Searched:** static callers of both ctors (none), strings ("LoginUdp_10").
- **Try next:** ClientConfig.ini / LocalConfig.ini keys read near the login setup, the 0.695 decompile, indirect
  calls.
- **FOUND 2026-10-15 (agent #8, architecture-leads.md L6):** nothing selects TCP. LoginApiWrapper's connect
  FUN_1430b3a70 / 0.589 FUN_14300d6e0 always builds the UDP API (FUN_143a58420 / 0.589 FUN_1439a0540, `LoginUdp_10`);
  the TCP ctor has no caller, pointer or inline copy. New lead L50 (GatewayId in CharacterLoginRequest).
  (Coordinator: move to Found.)

### L1 (13 of 14). Procedural placement mask bitmaps — found 2026-10-15
- **Answer:** loose files in `CommonData\ProceduralPlacementTool\Masks\` of both installs (0.589 Steam and 0.695, same
  sizes): PPP_Mask_Clumps001_Field02 / Inner02, PPP_Mask_Forest_Clearing01 / MainTrees01 / MainTrees02 / Spots01 /
  Undergrowth01 / Undergrowth02 (4096×4096), PPP_Mask_DualForests_Field01 / ForestA01 / ForestA02 / ForestB01
  (1024×1024), PPP_Mask_White01 (32×32); all 24-bit uncompressed BMP. 20 further unused masks in the same folder.
  No client code reads them (no string in either exe); CommonData\Voxel\veinData\HowToGenerateVeins.txt describes
  ProceduralPlacementTool as an offline dev tool, so masks + ProceduralPlacementSets are server / tool input.
  TilingTestZZ.bmp is still open (L1 above).
- **Source:** re/out/fileindex_589.tsv, fileindex_695.tsv; trace/0695/agent-L1-L2.md (Task 2).

### L2 (schemas + packet). ProceduralPlacementEntityLists / ...EntityListEntities — found 2026-10-15
- **Answer:** not shipped and never loaded by the client; the names are only labels in the 0x10A
  ProceduralDataMonitorPacketUpdateResponse ctor (FUN_142bcf5d0; 0.695 FUN_142c69030). The client holds both schemas:
  EntityListEntities = ID, ENTITY_LIST_ID, ENTITY_LIST_USE_TYPE_ID, ENTITY_ID, ENTITY_GROUP_ID, MIN_SPAWNS_PER_ZONE,
  MAX_SPAWNS_PER_ZONE (default 10000), SPAWN_WEIGHTING (float, default 1.0), LAYER_TIER_WHITELIST_ID (default -1)
  (FUN_143c5fa30 / FUN_143c603e0); EntityLists = ID, NAME, LAYER_TIER_WHITELIST_ID (FUN_143c616d0 / FUN_143c61b10).
  The 0x10A response carries rows of both tables plus per-layer / per-biome / per-entity-list spawn stats; full wire
  layout (reader FUN_142bb4ba0 → FUN_142b937a0, sub-readers FUN_142b9c5d0 / FUN_142b9dbb0 / FUN_142b9dcc0 and their
  element readers) and the 0.695 equivalents (identical) are in the source file. Row data still open (L2 above).
- **Source:** trace/0695/agent-L1-L2.md (Task 3).

### L98. Story creature faction on the client (nameplate colour) — FOUND 2026-10-16 (trace/0695/full-npc-695.md: disposition +0x147c 0 hostile / 1 neutral / 2 ally from LightweightToFullNpc +0x90 or SetDisposition 0x26/0x14; built 0cc86a8)
- **Need:** how live told the client a story creature's faction (Ally / Neutral / Enemy), so allies don't show red
  enemy nameplates (seen 2026-10-16: ally Slaug Stormslashers fighting Barren Flameblades on a cave site, red names).
- **Found 2026-10-16 [P]:** hostility FUN_142ea9630 compares entity +0x1448 (the lightweight packet's FactionId: the
  signed byte after the u32 SpawnId, struct +0x60, logged by FUN_142cddaa0; 1-3 = the PlanetSide VS / TR / NC rows of
  Factions.txt) and, for an NPC whose FactionId is outside 1-3, is hostile exactly when entity +0x147c == 0. +0x147c is
  set from the full NPC data +0x90 (FUN_142d532f0 ← LightweightToFullNpc, never sent by us) or fixed to 1 by
  FUN_142cdd710 (another spawn path). So every NPC we spawn reads hostile (red name).
- **Try next:** LightweightToFullNpc (PlayerUpdate 0x5C) layout up to +0x90 (0.695 reader, FUN_142d532f0's caller), then
  send it for Ally / Neutral story creatures with +0x90 = 1 (and on faction change); check what FUN_142cdd710 spawns.

### L134. Key of a prop's editable property in the spawn's settings list (movers-695.md §3)
- **CLOSED 2026-10-17:** key = one-at-a-time hash of the EditableProperties STRING name; used by the movers. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — Key = one-at-a-time hash of the EditableProperties STRING name (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** how the client keys a prop's editable property values (spawn +0x340 list → actor +0x4c00 hash): the
  teleporter looks up enable_teleportation as **0xa632764b**. Needed to send non-default mover settings (one-way pads,
  launch speed / angle, trajectory height) with the spawn.
- **Searched:** Jenkins lookup2 of the name (any case), CRC32, FNV-1a — no match.
- **Try next:** the vfuncs 0xd0 / 0xd8 the Thrower calls on its area (how it builds the key from AreaProperties
  EDITABLE_PROPERTYn), the EditableProperties loader (key = hash of STRING with a prefix?), the Setup window's write path
  (PropSettings EditSettings) and a runtime read of actor +0x4c00 after the Setup window changed a value.

### L135. How live moved a Magical Platform on the wire (movers-695.md §5.5)

- **CLOSED 2026-10-18 — VERIFIED round 88 (Frida + screenshots):** the pad moves and carries the rider (rider x 105 → 108.9 → 103.8 → 108.1, y 1205 on the pad; docks shift against the camera). Needed four server fixes: arrival hold over props (97b5728), control mode 2 (e3da328), spawn owner 0 (e8cbad8), and the release after every spawn — every client takes local management of every prop it spawns (FUN_1430aafd0 → FUN_142c98c00) and drops 0x7F for it; PlayerUpdate ManagedObject 0x4D {guid, viewer's guid, 0} right after the spawn releases it (02306ee; hook: unsetLocal from FUN_142cdd450, f4=0).
- **2026-10-18 FOUND [P], built:** the pad never moved because prop spawns sent control mode 0 at +0x19c (entity +0x41A0; FUN_142eb1950 plays 0x7F records only when ≠ 0) — moving part now 2 (e3da328) and spawn owner 0 (e8cbad8: the owner's locally-managed set dropped 0x7F); arrivals over props hold 4 s (97b5728, round 75/76: the player stands on the pad). Rider carry is the client's own (FUN_142bbb7e0, body +0x51 bit 0x10 from SlidingFriction area 14) [P]. Ride check: round 81 (agent-e3-platform.md).
- **Pass 2, 2026-10-17: UNCHANGED (+forum)** — Forum: the pad turns with the dock rotation as it moves. The rider test is still needed. (trace/0695/agent-leads2-d2.md)
- **E-leads 2026-10-17: ADVANCED — server-driven movement via position update 0x7F; collision refresh once per second (agent-e-leads.md).**
- **Need:** the packets that move the platform prop between its docks so riders move with it (types 27 / 28 are
  server-side area properties; client rails are a predefined table).
- **Searched:** the PlayerUpdate rail messages (predefined rail ids), the client area property list (no moving-object
  class besides the edit proxy).
- **Try next:** prop position updates the client accepts for a placed prop (PlayerUpdate 0x72 MobMovement / ManagedPosition
  0xC7 / UpdateLocation on a proxied prop), the SlidingFriction property (keeps riders on), the composite effect
  EFX_Prop_Movers_MovingPlatform_Fantasy01, forum videos of platforms.

### L136. Map waypoints: personal (Ctrl + click) and shared with the group (E.2 re-check of SetWaypointPacket)
- **CLOSED 2026-10-17:** Ctrl + click waypoints are client-local (5 slots); no packet; nothing to build. (sweep: agent-leads-sweep.md)
- **Sweep 2026-10-17: FOUND** — Ctrl+click waypoints are client-local (5 slots); no packet is sent (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** whether a waypoint placed on the world map (patch 30 Oct 2014: "With the map open, press CTRL + Left Click
  to place a way point. This will stay on the map, and in the world"; roadmap 29 Apr 2014: "Players can create
  waypoints and share them with their group" — Completed) ever reaches the server, i.e. whether live sent / answered
  SetWaypointPacket 0x32/0x12 (0.695 builder FUN_1433fdd30, fields u32, u8, vec4) and ZoneWaypointPacket.
- **Found:** the builder has no direct caller; Lua Ui.SetWaypoint / Ui.RemoveWaypoint send it and no 0.589 / 0.695
  script calls them; the input action "PlaceWaypoint" (context "WorldMap") is registered in FUN_143054920 (0.695);
  ClientWaypointManager (vtable 144708e58) keeps AbsoluteZoneWaypointEntry lists; the Hud has Personal and Platoon
  waypoint indicators.
- **Try next:** the handler of the "PlaceWaypoint" input action (input action table → its callback); a runtime check
  with the user's mouse (Ctrl + click on the map while the server logs 0x32); group sharing (Platoon indicator) would
  need the server to relay to group members.

### L137. Live's Showcase / Marketplace web pages and the client natives they called (E.2)


- **2026-10-18 (browser, archive.org CDX):** store.landmarkthegame.com has no captures; www.landmarkthegame.com/showcase/ has captures 2014-05-10 … 2017-02 (single-page app: ?_escaped_fragment_=/gallery, /marketplace, /competition, search URLs — the 2017-01-06 /marketplace and /gallery snapshots are the newest full pages). archive.org then went 'temporarily offline'. Next: open https://web.archive.org/web/20170106104425/https://www.landmarkthegame.com/showcase/?_escaped_fragment_=/marketplace and its JS bundle for a My Store / follow section; CDX for forums thread 46891 pages 2-4.
- **2026-10-18 (commerce2 90e5f68 / merge 1f6bbd1):** following + digest also run in the commerce service (COMMERCE_ADDRESS); 0xD0 PurchaseBundleWithInGameCurrency answered (Store.Buy, PlayerUpdate 0x4B {u32,u32,u32} [P layout]). Remaining: archive.org items above; new lead L169 for 0xD0's argument meanings.
- **2026-10-18 (agent digest, 6c2e99a / merge 52af835): BUILT what live had** — Update Notes 27 Aug 2014 (design/forum/patches/2014-08-27-46891): My Store → Start Following creators or tags (grouped tags = all of them), a daily in-game mail of newly approved Marketplace / Gallery items; the 2016 wiki Showcase page still describes it. The 0.695 client has creator following only (FollowCreator / UnFollowCreator; no tag natives or packets) [P], so tag following was on live's web pages [I]. Built: stored follows (showcase_follow), Following tab on our Showcase page, daily digest (SHOWCASE_FOLLOW_DIGEST=true, hour 10 UTC ours [I]; mail text ours [I], sender 563379 Player Studio [P]); ServerStatusRequest 0x0D answered 0x0E true. Native coverage table in the agent report (MODLOG). Still open: archive.org copies of live's My Store follow page / a real digest mail (wording, hour, limits) — try with the browser; the commerce-service port and 0xD0 PurchaseBundleWithInGameCurrency are being built (local/commerce2).
- **Pass 2, 2026-10-17: UNCHANGED** — Needs archive.org. Target hosts taken from the exe are listed below. (trace/0695/agent-leads2-d2.md)
- **Need:** which InGamePurchaseStoreScreen natives (90 in 0.695; luabind_695.tsv) live's web Showcase pages called
  through the in-game browser (InGameMarketplaceBrowser, CEF) — e.g. FollowCreator / UnFollowCreator (Showcase "My Store
  → Start Following", 27 Aug 2014, with a daily mail of new items from followed creators and tags), store bundle
  contents, recommendations, server status. Those pages were Daybreak's, not shipped, so the client files can't show
  their calls; our pages (WorldServer Resources/Web/showcase.js) talk to our server directly.
- **Try next:** archive.org copies of the Showcase pages (landmarkthegame.com / Daybreak store URLs the client opens —
  read the URL from the client's settings / Lua), their JS for the native bridge names; patch notes for each store
  feature; then build what live had (following + daily mail digest is described in P-notes 27 Aug 2014).

### L138. Server-only tables shipped in the client folder, and client tables of EQN / PS2 systems (tables-unread-695.md)
- **CLOSED 2026-10-17:** all tables classified (agent-l138-a / c1 / c2.md, tables-unread-695.md): server-only tables superseded / test data; ProjectileDefinitions live → built (Projectiles 0xEE); the open parts continue as L156-L162.
- **Need:** what live's server did with SlotDefinition (167 slots with target-profile requirements and capacities),
  RequirementGroups / RequirementSets, LoadoutSlots / LoadoutEquipSlots / LoadoutConstants, WeaponSetDefinitions,
  ProfileTypes, VoxelTiers, DamageTables (no client loader: server data); and whether the client systems in
  tables-unread-695.md §C were live in Landmark (projectiles, curves, merchant sets, fire modes, skills …).
- **Try next:** match SlotDefinition / WeaponSetDefinitions ids against the server-only ability and profile data
  (server-only-data.md), the requirement ids against Requirements.txt users in the client (ability / item / tool use),
  the wiki pages for each feature; nothing here is called unused until proven (E.2 rule).

### L139. Creature projectiles in flight on the client (PlayerUpdate LaunchProjectile 0x31)
- **CLOSED 2026-10-17:** the LaunchProjectile handler is `ret 0`; the visible path is Projectiles 0xEE (L156), built. (sweep: agent-leads-sweep.md)
- **2026-10-17:** the visible projectile path is the Projectiles family 0xEE (FUN_14318fbe0, ProjectileDefinitions), not PlayerUpdate 0x31 — built and tested (L156).
- **Sweep 2026-10-17: FOUND (existing)** — The LaunchProjectile handler is `ret 0` (OPEN-LEADS L140 note) (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** the layout and meaning of PlayerUpdateLaunchProjectilePacket (0.589 reader FUN_142bb5ae0 → FUN_142b939b0,
  a large ProjectileLaunchData: u64 +0x140, u32 ×N, lists, strings …; handler case 0x30 → FUN_14311f0e0) so the
  Abomination's goo (ProjectileDefinitions row 54, model 1396, bone Tongue_05) is seen flying. The server already resolves
  the spit as a dodgeable projectile (Creatures.Spit / ResolveShots, 2026-10-17) with release / impact effects only.
- **Try next:** pkt.py on FUN_142b939b0 with names from FUN_14311f0e0's use of each field; the projectile manager's
  ProjectileDefinitions lookup (client FUN_142bc5360).

### L140. Client→server messages parked by the E.3 triage (re/tools/verdicts.tsv "parked", E.3)
- **2026-10-18 (agent-l140.md): TRIAGED all 58 parked rows** — build list (Evac to Safety teleport subs 0x1E/0x51 / 0x52 / 0x3F6 freeze the player without a TeleportToLocation answer; Combat 0x24 subs 2-4; hidden /party subs; accept-and-log rows) being built on local/evac; the rest closed as unreachable on retail 0.695 with the evidence in the report (§2-§4). Open as L170: whether live melee used server-started targeters (0x27/9).
- **Need:** for each parked class, proof either way that live Landmark used it: group looking-for-more / auto-group
  (Lua Groups natives without a UI caller), lobby / mini-game / encounter-matchmaking invitations, repeating activities,
  requisitions, client objectives, EQN housing, engine paths (auto-attack, client targeters, interact cancel, attach /
  detach object, facial anim, area timers, trial item lines, prop list request, claim snapshots, claim supplemental data,
  map teleports, guild petitions, player reports, help request / offer, chat bubble colour, coin store, respawn
  locations), profile leaderboards, task select.
- **Searched:** 0.589 / 0.695 UI and scripts (ui_server_map_695: no callers), C++ caller walk (cpp_paths.py), runtime
  opcode_stats (none of them seen unanswered in play).
- **Try next:** wiki / patch notes / dev posts per feature name; Frida on the sender functions during normal play; a
  feature that turns out live gets a TASKS item.
- **2026-10-17 FOUND (L139):** the client's projectile launch that PlayerUpdate LaunchProjectile reaches is an empty
  function in both builds — 0.589 FUN_14311f0e0 and 0.695 FUN_1431ca140 are `ret 0` (bytes c2 00 00 in Landmark64.exe;
  0.695 dispatcher case 0x30 → FUN_1431ca140). Landmark's client draws no projectile from that packet, so the server
  sends none; the goo shows through its release / impact composite effects (as the server does). (trace/0695/
  functions.tsv mis-pairs 0.589 FUN_14311f0e0 with 0.695 FUN_1430747c0, a VoxelAtlas loader.)

### L141. When live made loot pinatas ownerless (TASKS E.4)
- **CLOSED 2026-10-17:** answered by the loot-rule research (agent-loot-rule.md): fungus terrain loot is ownerless; built (bcc3329). (sweep: agent-leads-sweep.md)
- **E-leads 2026-10-17: ADVANCED — client side proven (NotifyOwnerlessLootClaimed 0xFB/4 to every viewer); the live trigger rule is not found (agent-e-leads.md).**
- **Need:** the rule under which a loot bag / chunk lost its owner, so that other players near it could claim it
  (client: owner guid none → RequestClaimOwnerlessLoot 0xFB/7, FUN_14307d0f0; server side built 2026-10-17).
- **Searched:** patch notes (only "Reward chests now give loot to the whole group", 28 Sep 2016), wiki "loot" pages.
- **Try next:** forum / dev posts on loot sharing, group loot and creature drops; the client's pinata timers
  (FUN_14307c780 states, ownership change on a SpawnLoot update?).

### L142 — Claim server connection lost (ClaimsPacketLostClaimServerConnection)
- **Sweep 2026-10-17: FOUND** — 0xF9/0x12 has no body and shows text 562155 as a system and chat message; nothing is blocked (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** when live's zone told the client its claim server was gone, and what the client then blocks.
- **Known:** live ran claims as its own service (live-architecture replica, trace/architecture/); the emulator keeps
  claims in the world server, so the case never happens yet.
- **Try next:** the 0.695 handler of the packet (UI text / disabled tools), then send it from the split-service build
  when the claim service drops.

### L143 — Server-driven windows and notices with no known live trigger (E.5)
- **Sweep 2026-10-17: ADVANCED [P]** — ZoneCraftingWindow closes (bool 0) or refreshes (bool 1) the crafting / repair window (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** which live system sent each: LoadingScreenPacket {string, string} (NotificationManager:LoadingZone),
  RecipePacketZoneCraftingWindow {u32, u32, bool} (station-less crafting / repair window), InstancedGamePropStatePacket
  (per-prop control labels of an instanced game, game manager +0x1e0), RewardBasePacket (reward family).
- **Found 2026-10-17:** LoadingScreenPacket's handler calls Lua NotificationManager:LoadingZone(message, color, …), which
  in the shipped Resources/Scripts/NotificationManager.lua shows an on-screen notification (EVENT_SHOW_NOTIFICATION);
  the loading screen's own text is GameData.LoadingScreen "Message" (LoadingScreen SWF). Candidate live use: the
  destination text of a zone / island transfer.
- **Try next:** the 0.695 handlers' callers and UI reactions (re/out/ui_server_map_695.md), the 0.695 data tables for
  items / abilities that open a crafting window, patch notes and forum for loading tips and Zone Control.

### L144 — What earned grief points in Landmark (GriefBase 0xC0)
- **Sweep 2026-10-17: ADVANCED [P]** — Level ≥ 4 blocks the Knife / QuickUse slots and abilities without definition flag 0x10; the earning rule is still open (evidence: trace/0695/agent-leads-sweep.md).
- **Need:** live's rule for grief points / levels (1-3 warnings, 4 equipment lock, 5 suspension) and what each level
  blocked. The client's receiving side is live in 0.695 (FUN_142ee1bf0, texts, HudHandler:OnGriefLevelChanged with no
  Lua handler); the server can send it (Game/Grief.cs) but nothing awards points yet.
- **Known:** PlanetSide 2 used grief for friendly fire / team kills; Landmark had PvP areas and claim permissions.
- **Try next:** patch notes / forum for "grief" (PvP, claim griefing reports), the 0.695 equipment-lock checks that read
  the grief level (field +8 of the grief object), the Report Player flow.

### L145 — Admin console command table at 0x1447e9698 (DebugPathQueryRequest)
- **E-leads 2026-10-17: FOUND — VoxelPathFindVisualizeCursor vtable slot 83 (tool PathFindVisualize, not in the palette); request {vec4, vec4, u8} (agent-e-leads.md).**
- **Need:** what the pointer table holding FUN_14379b600 (AdminCommandPacketDebugPathQueryRequest 0x1E / 0x4A7) is,
  and whether the retail client can reach it (slash command, admin-player flag, or admin client only).
- **Try next:** read the table around 0x1447e9698 (neighbouring entries, name strings), find its users.

### L146 — Items inside a claim (ClaimsPacketIdDebugAddItemClaim, ClaimRequestMoveItemToPlayer)
- **E-leads 2026-10-17: FOUND — a claim's inventory is its placed props' items; replies 0xF9/0x0D and 0x37; 0x23 is client → server (agent-e-leads.md).**
- **Need:** whether a Landmark claim held items (the /item claimAddItem and claimItemToInventory admin commands move
  items into / out of a claim; Claims 0xF9 / 0x60 and 0x23).
- **Try next:** the 0.695 client's claim data for an item list (claim entry fields, a claim container data source),
  the claim vendor / claim storage in patch notes and the wiki.

### L147 — Live's Daybreak Cash prices (store, 6.60)
- **Need:** the real DBC prices of the Lumen packs (Box 650 … Hoard 13,000 Lumens), the six pet figurines and the
  Superb / Legendary Reward Chests; the store uses 1.3 Lumens per DBC (the Lumen pack ladder vs DBC denominations,
  [I]) and placeholders (pets 650, chests at their Lumen Station price). Report: trace/0695/agent-store-entitlements.md.
- **Try next:** archived Showcase / Marketplace pages, forum price threads, Player Studio fee posts.

### L148 — Player Studio design prices
- **Need:** live charged a design's creator-set DBC price; the store gives designs free. The listing price field
  (Commerce) and the creator share, converted to Lumens at the store rate.

### L149 — Founder items with no client record
- **2026-10-18 (agent assets, dce93ea): Regalia FOUND + built** — Noble's Regalia = Entities_Trailblazer_000_Chest.adr (icon 754, texts 560077 / 560091), Courtier's = Entities_TrailBlazer_001_Chest.adr (icon 755, 560075 / 560089) [P meshes / texts; I which is which, strong: icon renders match the wiki]; items 900344 / 900345 (ours) in the founder package. Still open: their tints, Ring of Bounty, Mastercraft Bracer, Mega Pocket, Founder's Pickaxe 434, the Founder title.
- **Need:** the original Founder's Pickaxe, Ring of Bounty, Mastercraft Bracer, Mega Pocket, Noble's / Courtier's
  Regalia, the title Founder — none has an item / title row in 0.695; and live's Spires chest that restored missing
  Founder's Pack items (prop 898). Live granted per account; the server grants per character.

### L150 — Graveyard leads (agent-graveyards.md §3, L-a … L-j)
- Area types 36 / 37 meaning; live's surface graveyard positions and template; whether the map's red circles are the
  graveyard exclusion zones; stamping the full cavern graveyard template (only the centrepiece stands); which props
  carried areas 25 / 27 / 3 / 4 (incl. the Spire's); Underground Adventure trips ending at a graveyard; the 100 m layer
  threshold vs the client's cave tier. Details and what to try: trace/0695/agent-graveyards.md §3.

### L151 — Membership leads (agent-membership.md §6, M1 … M10)
- What the client does with SendSelf's member flag; membership screens / benefit texts reachable in 0.695; live trial
  key redemption flow; etc. Details: trace/0695/agent-membership.md §6.

### L152 — Creature ability leads (agent-abilities.md §4.3, E1 … E5)
- Rank lines with no creature (Vicious Slash, Electro Shot, Fire Slag, Venom Blast); whether live creatures used ranks
  2 / 3 on their own; what the specials did beyond damage; ability names in the Combat Node settings window; 0.589's
  +0x40 read; chat lines for the older emotes. Details: trace/0695/agent-abilities.md §4.3.

### L153 — Per-account entitlements (agent-store-entitlements.md §6 H)
- Live granted founder items per account; the server grants per character. Details in the report.

### L154 — Play Mode visibility details (agent-playmode.md)
- Whether builders in build mode saw hidden story tools on live (patch note says "visitors"); Game Table / Score /
  Victory Node visibility in Play Mode; 848 = Respawn Point "enabled" look is from the name; the prop spawn's
  "enabled" flag (always false) is read by area code — trace it.

### L155 — Saved height -2000 after underground dev teleports
- Same as L94 (teleports onto that cave floor fall to y -2000); kept here for the save-path question.
- After `tpo` moves beside outpost 0 (each move re-zones underground) the character was saved at y = -2000 (the remote
  frame offset's height) and logged in over a void. Find which re-zone / position update stores the frame offset
  instead of the real height (WorldSession.RemoteFrameOffset, Teleport.Rezone) — dev-only path so far.

### L156. Projectiles 0xEE: the unnamed Create fields and the Delete impact (trace/0695/agent-l138-c1.md §1)
- **CLOSED 2026-10-17 (pass 2, FOUND (except +0x4c)):** +0x58 = homing target entity guid (re-resolved every frame), +0x64 = fixed homing point (wins over the guid), +0x60 = seconds for the turn rate to fade to 0. +0x4c (→ projectile +0x3c) has no reader Built / done: homing fields named (69bb053). (trace/0695/agent-leads2-a.md)
- **Need:** what Create's bool (+0x40), u32 (+0x48, re-aimed with the definition's +0x28 when it has gravity), vec3
  (+0x4c), u32 (+0x44), u64 (+0x58), f32 (+0x60, divides +0x44) and vec3 (+0x64) / u64 (+0x70) mean, and whether
  Delete plays the definition's COLLISIONEFFECT_ID itself.
- **Found so far [P]:** layouts from the 0.695 readers FUN_14318ce10 / FUN_14318d480 / FUN_14318d320; the handler
  FUN_14318fbe0; the shooter guid (+0x38) gives the start bone (FUN_1436d5c40); a unit direction is scaled by the
  definition's speed, any other length is the velocity (FUN_143c9fa70). Built: Game/Projectiles.cs, dev `projectile`.
- **Try next:** client tests varying each field; FUN_1436d70c0 (update) and FUN_1436d6b60 (end) for the impact effect.
- **ADVANCED 2026-10-17 [runtime, re/test/projectile_probe.js]:** the first Create was rejected (the sub went out as a
  4-byte int, 99 bytes vs the reader's 97; fixed 22f780f); then the shot stayed at its start: Create +0x48 is an **f32
  maximum range** — the base update FUN_143c9fca0 clamps the shot to it every frame (0 = pinned) [P]; +0x40 = gravity on,
  +0x44 = homing turn rate, +0x70 = homing start time [P]. With the range sent (67ad2d5) catapult rocks (41) and skull
  tosses (54) fly from all sides and end where Delete puts them; the Abomination's spit now flies from its Tongue_05 bone
  (start point moved by the client, FUN_1436d5c40) at 25 m/s, single and triple (b7b6dfc; scratchpad proj5-sheet.png,
  abom-sheet.png). Still open: +0x4c / +0x58 / +0x64 meanings.
- **Impact effect [I strong, 2026-10-17]:** Delete (FUN_1436d6b60) only stores the end (hit flag, point, target), draws
  debug boxes and lets FUN_1436d5d90 mark the shot done once it passes the point; the destructor FUN_1436d5b60 only
  removes the actor; none of them reads the definition's COLLISIONEFFECT_ID. So the impact composite is the server's to
  play (as Creatures.ResolveShots does) — no double splash.

### L157. Which ProjectileDefinitions row each player weapon fired (bows, staves, wands, rayguns)
- **Need:** the weapon → projectile row mapping (server data on live). Rows 85-121 are one arrow / bolt per affinity,
  22 / 27-36 / 58 staff basic attacks, 23-26 / 45-48 / 56-57 / 68-82 bow trails (agent-l138-c1.md §1).
- **Try next:** match row names to weapon item models / affinities [I]; patch notes on Raygun arcs (25 May 2016).
- **ADVANCED 2026-10-17:** rows named by their trail effects [P]: arrows 85 Physical, 86 Affliction, 87 Fire, 88 Lightning,
  89 Shadow, 90 Ice, 91 Radiance, 92 Water; bolts 94 Fire, 95 Radiance, 96 Lightning, 97 Ice, 98 Shadow, 100 Affliction,
  108 Water, 109 Physical. Built (be4ab3f, WeaponCombat.ProjectileRow): bows fire the arrow of their kit's affinity,
  other ranged weapons the bolt [I: per weapon live used server data]; harness (dev `fire`, projectile_probe): Fire bow →
  row 87 from the R_Weapon bone, Keldarain staff (Water) → 108. Hits stay instant (the shot is visual) [I].
- **Creatures (2026-10-17):** Abomination spit 54, Highfall Catapult rock 41 (sent at the server's 16 m/s), healer radiance
  bolt 112 (matched by its release 11574 Radiance_Wand_Ability01 and impact 11649) — all harness-verified with the probe.
  The ScorpionBush stinger rows (14-17, 37-40) and the kobold spear (4) have no server creature: no 0.695 prop or wiki
  page names a Scorpion Bush, so they look like EQN-prototype rows [I] (not built; reopen if a source turns up).
- **Outpost archers / casters (785b579):** the audit's F4 item (audit-outposts-695.md) — Ranged / Archer / Caster spawners
  now use the ranged behaviour and shoot the affinity arrow / bolt their name implies [I], dodgeable like the spit;
  harness: p2704 Barren Ranged → 85, p2724 Ebonguard Caster → 109, p2837 Ebonguard Firemancer → 94.

### L158. The keyword map in the ability info reply (KeywordDefinitions, agent-l138-c1.md §5)
- **CLOSED 2026-10-18 (built 1ffa388 / merge c5f76c6):** the tooltip reply (Inventory 0x2D / 0x12) record's 4th u32 list is the only source of item keywords (FUN_142f17a30) [P]; item definitions carry none. Weapons, chest armour, accessories and pets now carry their affinity keyword (ids 1-7 checked against the client texts) [P]; which items live tagged [I].
- **Need:** which side of each map entry is the keyword id, so weapon attacks can show their affinity / attack-type
  keywords in tooltips (we send the map empty); and where item tooltips get their keywords.
- **Try next:** the agent report's §5 try-next steps.
- **ADVANCED 2026-10-17:** map layout settled [P]: per pair the first u32 is the map key (node +0x18) and the second the
  value at node +0 (FUN_142c34f90 / FUN_142afbb00), which the tooltip FUN_14376c210 looks up as the keyword. Built
  (WeaponCombat.KeywordsOf): affinity keyword + Ranged 56 / Melee 57 + Basic Attack 59 on basics [I: which keywords live
  sent]. Harness (re/test/keyword_probe.js): the client parses them, error flag 0 (Fire bow {1, 56, 59}, Water staff
  {2, 56, 59}). Open: seeing the tooltip text (needs a hover), item keywords.

### L159. MerchantSets / NPC merchant list (0x9E sub 0xA): live in Landmark?
- **CLOSED 2026-10-17: not live in Landmark [I strong].** Notes sweep of design/forum (2014-2016): the only developer word
  is the 24 Apr 2014 "once our Merchant and Currency systems are implemented"; player posts through the launch notes
  (21 Mar, 1 Apr, 13 Apr, 16 Jun 2016) still ask for NPC vendors / a way to sell to players; the wiki lists merchant
  stalls only as decorative props. The client path (0x9E sub 0xA, MerchantSets "Generic Merchant") stays unbuilt;
  user decision 6.52 (Vendor Booth) is separate.
- **Found so far:** the client path works (handler, Lua, window); the 24 Apr 2014 notes call merchants future work and
  the 1 Apr 2016 notes say the vendor booth was not being developed for Landmark [I strong: not live].
- **Try next:** final sweep of the 2016 notes and forum for any merchant NPC; then close.

### L160. Client-executed effects in CasterInfoReply for sprint / boot / glide abilities (agent-l138-c2.md §3d)
- **CLOSED 2026-10-18 (FOUND [P], built 1ffa388):** in retail 0.695 the ClientExecutedEffectData is the client's only sprint / glide / double-jump / leap path (the controller's own sprint runs only for an admin client: FUN_142b21000 returns 0) — without it Shift and the boots did nothing. Built Game/MovementEffects.cs for 326 and boots 320 / 321 / 322 / 327 / 328 / 542 (MOVEMENT_EFFECTS=true). Rows / durations [I] (14 Oct 2014 dev post shapes). Still open (new lead L168): live's exact rows, hold / channel semantics, Dash Soles / Roll Wraps, Leaper's take-off damage (row 429?).
- **Pass 2, 2026-10-17: ADVANCED** — ClientExecutedEffectData decoded: 14 u32, type hashes for the seven effect classes, and a 3-phase curve of AbilityParameterDefinitions ids + durations. The client gates casts on the effects. The 326 capture carries none (trace/0695/agent-leads2-a.md)
- **Need:** whether live's CasterInfoReply for sprint (326), the boot abilities (320-328, 432, 542, …) and gliding carried
  ClientExecutedEffectData (AbilityParameterDefinitions rows); ours sends none.
- **Try next:** the report's §3d steps; compare a live capture of those abilities if one exists.

### L161. The "retrieve missing Adventurer's Tools" grant (ItemGroupMappings group 6, text 562716; agent-l138-c2.md §2)
- **BUILT 2026-10-17 [P source, I rule]:** the Spire template holds prop 884 "Resupply Chest" — the chest of the 31 Mar 2015
  hotfix ("missing tools can be obtained from the chest at spires"). Using it now gives back each of the item-group-6
  starter tools (3253 pick, 3259 axe) the player has none of, with text 562716 (Spire.Resupply; harness: 2 given back,
  dev `resupply`). Whether live also restocked other things there stays open.
- **Need:** what triggered it and where (2015 note: missing tools came from the Spire chest); built optional after that.

### L162. CharacterStatDefinitions SEND_TO_REMOTE_CLIENT stats 21, 22, 24-29 (agent-l138-c2.md §3b)
- **CLOSED 2026-10-18 (FOUND [P], built 1ffa388):** stats 21, 22, 24-29 are Sprint / Forward / Back / Strafe acceleration and deceleration times (StringHashToValue.txt) [P], copied into every character's animation (FUN_142eaf290); now sent to others in LightweightToFullPc's nested UpdateStat (values from the live capture [P]). Also: the server sent the local player no character stats at all — the 37 live stats now go at login (LIVE_CHARACTER_STATS=true). Open only whether live changed them in play (one capture).
- **Need:** what these stats are and whether other players' clients need them in PlayerUpdate UpdateStat (ours: empty).
- **ADVANCED 2026-10-17:** CharacterStatDefinitions has no names (all NAME_ID 0). If its ids follow
  CalculatedStatDefinitions [I], they are 21 GrappleCooldown, 22 HealthRegenRate, 24 ManaBonus, 25 ManaRegenRate,
  26 PropHarvestTier, 27 DoubleJump, 28 Glide, 29 Fly; CalculatedStatDefinitions itself flags no stat SEND_TO_OTHERS.
  The table's only client reader is never called (agent-l138-c2.md §3b), so this is at most a server rule. Low
  priority. Try next: whether a remote player's glide / fly animation needs those stats (two-client test).
