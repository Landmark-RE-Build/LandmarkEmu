# Environment Server: the terrain / voxel service of a world (Phase B spec, agent #14, 2026-10-15)

The world's voxel back end: the layered voxel store (Poi / Permanent / Timed Persistent / Transient), the destruction
layer and its timed heals, cell versions and the zone-in manifest, build edits, undo / redo, pastes, voxel ownership
(Voxel Vision), ore veins, LOD copies, exported-template stamps and the ruin layers of the Chaos Caverns. The client
calls this back end the **Environment Server** (evidence §1.1), so this spec uses that name; the coordinator merged the
old "Environment Server jobs" topic (#16) into this one (2026-10-15). The zone server stays the only thing that talks
to the client about terrain (§3.1).

Shared contract: [internal-transport.md](internal-transport.md). Addresses: **0.695 / 0.589** (re/decomp_all_695 /
re/decomp_all; mapping from trace/0695/functions.tsv). "Inference" marks anything the code doesn't prove.
Scratch: re/out/agent_terrainsvc/ (f4_dispatch_695.c, envmon_695.c).

---

## 0. Summary

1. **The Environment Server is the voxel back end.** The client's monitor page for it (FUN_14320ea00 / FUN_143163fe0)
   counts 19 job types named by FUN_143c74ef0 / FUN_143bb4ef0: four "Cell <layer> Lod" (the four voxel layers),
   "Construction Apply Diff Cell", "Construction Undo Redo", "Construction Gather Ownership Info", "Construction Paste
   Template/Clipboard (Off-grid)", "Vein Spawn Vein", "Misc Manifest Compression"... (§1.1). The construction replies
   the client handles are named after it (VoxelConstructionPacket**Env**OwnershipUpToDate / DataResponse 0xF4/0x16,
   0x17), and the zone ↔ it messages are in the client's id table (EnvServerResponse 0xF4/0x15, Zone* requests, §1.3).
   This also explains P0026 "our voxel database ... 64-bit processes". One service, not two.
2. **Every terrain message from the client goes on channel 0 (Zone)**: ClientCellRequest, PendingRequestTimeout,
   ApplyDiffRequest (FUN_14315c980, channel argument 0 from FUN_14315a820 / FUN_143165810), ClientSelectionAreaRequest
   (FUN_14315cb20 from FUN_1431642e0 / FUN_1431649d0 / FUN_143165440 / FUN_143165700, channel 0), ClientOwnershipRequest,
   ClientUndoRedoRequest, ResetAll / ResetTemporaryCells (FUN_143158bb0, channel 0). So the zone server received them and
   talked to the Environment Server behind it — the **Client* → Zone* request pairs** in the id table show the zone
   forwarding (ClientSelectionAreaRequest 0x2 → ZoneSelectionAreaRequest 0xF; OffGridPasteClientRequest 0x5 →
   OffGridPasteZoneRequest 0x13; ClientUndoRedoRequest 0x3 → UndoRedoRequest 0x10).
3. **Channel 3 carries only the build-cursor previews**: all eight WorldEditing 0x10C senders use channel 3 (Clear
   FUN_143086080 / FUN_142fe6140, Box FUN_143085f20 / FUN_142fe5fe0, Sphere FUN_143086400 / FUN_142fe64b0, Shape
   FUN_143086280 / FUN_142fe6340, Selection FUN_143086110 / FUN_142fe61d0, PreviewSetup / Update / Fill per
   channels-695.tsv). channels.md listed only four of them on channel 3 (correction §10). Who relayed channel 3 on live:
   **not found yet (L75)**; our build relays them in the zone (today's Game/WorldEditing.cs), with a gateway relay as
   the alternative (hand-off #13).
4. **Corrections to earlier traces:** TerrainCell 0xEC/0x6 is **client-handled** (`TerrainCellPacketMaterialChangeInfo`,
   reader FUN_1431468f0 / FUN_14309bfd0, case 6 of the 0xEC dispatcher part_0087 ~22230), not a server-only message as
   architecture-leads.md L8 lists it (the id table spells it `cTerrainCellPacketCellMaterialChangeInfo`, so the
   class-name match failed). The layer names docs/terrain.md cites from FUN_143bb4ef0 are the Environment Server's
   **job-type** names (types 0-3 = the four layers' LOD jobs).
5. **The emulator today** runs all of this inside the world process (Game/Terrain/*, Building, BuildingClipboard,
   WorldEditing, Outposts, Spire; §2). 3,168 edited cell layers on disk in the dev world (21 MB, median 2.6 KB per
   file), 944 ownership grids (avg 286 B), ~32 MB of compact overlays in RAM for ~1,950 cells (MODLOG 2026-10-12).
   Main divergence from live: the **heal** is per cell (a cell untouched for 20 min loses its overrides outside build
   sites; every new dig restarts the clock), where live queued one pending heal per destruction with its own due time
   and "a new destroy doesn't cancel an earlier pending heal" (dev, 27 Jul 2016). Ruins: 23 `cavern_ruins_*` stamped
   once; live kept a *desired number of ruins per ruin layer* and counted failures (§1.4).
6. **Spec (§3-§7):** one `envserver` container per world serving every landscape of that world; the zone keeps a
   replica of every cell's layer versions (manifest and UpToDate answers without a round trip, needed by the re-zone
   rule) and an LRU of finished CellsUpdate payloads; edits are one request / one reply with the zone's permission
   boxes and the player's material budget; heals become a persisted queue; job counters feed the admin page (#16).
   **Decided 2026-10-16 (§5.5): no voxel mirror in the zone; every voxel write is one transaction in the service.
   Built off by default (§7 "Built"), untested on Docker / with the client.**
7. New open leads **L75-L80** (§9).

---

## 1. Client evidence

### 1.1 The Environment Server's job types and ruin layers (monitor page 33)

`EnvironmentServerMonitorState` page drawer FUN_14320ea00 / FUN_143163fe0 (re/out/agent_terrainsvc/envmon_695.c):

- "========= Environment Server Information =========", "Job Summary" with columns Name / Active / Pending: for job
  type 0..18 it prints `name, active (+0x20 + 4·i), pending (+0x6C + 4·i)`, then "Total Jobs" (+0xB8).
- Job type names, FUN_143c74ef0 / FUN_143bb4ef0 (the same function docs/terrain.md uses for the layer names):

| # | Name | What it maps to in this spec |
|---|---|---|
| 0 | Cell Poi Lod | LOD copies of layer 0 (server stamps: Spire, ruins, outposts) |
| 1 | Cell Permanent Lod | LOD copies of layer 1 (build-site building) |
| 2 | Cell Timed Persistent Lod | LOD copies of layer 2 (destruction / digging, heals; ore veins) |
| 3 | Cell Transient Lod | layer 3 (empty in the live manifest capture) |
| 4 | Cell Temporary Modifications | timed changes: the heal queue (inference) |
| 5 | Cell Generation | server-side generation of base-world cells (inference; the server needs them only for its own uses, e.g. pathing — not built, §3.7) |
| 6 | Cell Composite Update Lod | composite of the layers per cell / LOD (inference) |
| 7 | Construction Apply Diff Cell | ApplyDiffRequest, per cell |
| 8 | Construction Template | design / template placement (PasteClaimArea, SpawnExportedTemplate) |
| 9 | Construction Undo Redo | UndoRedoRequest 0xF4/0x10 |
| 10 | Construction Gather Ownership Info | ClientOwnershipRequest → EnvOwnership* (Voxel Vision) |
| 11 | Construction Paste Template/Clipboard | paste (ZoneSelectionAreaRequest action 4) |
| 12 | Construction Paste Template/Clipboard Off-grid | OffGridPasteZoneRequest 0xF4/0x13 |
| 13 | Construction Clear Exported Template | DespawnExportedTemplateRequest 0xF4/0x19 |
| 14 | Construction Area Iterator | box operations: HealClaim / DeleteClaimArea / RevertTerrain / ResetTemporaryTerrainDamage |
| 15 | Vein Spawn Vein | ore vein spawning |
| 16 | Vein Validate Cells | vein checks (disturbed veins, cells) |
| 17 | Misc Cell Ownership Reusable | ownership grid upkeep (inference) |
| 18 | Misc Manifest Compression | building the compressed modified-cell manifest (TerrainCell 0x0A) |

- "Ruin Layer Info": per layer a 16-byte record (list at +0xC8, count +0xD0) printed as
  `Layer n: Desired (+0, label string 0x14472a400 "Desired") Active (+4) Failed (+8) Last ID (+0xC, signed)`.
  So the Environment Server kept a **desired number of ruins per ruin layer**, spawned them (Last ID = last ruin
  instance id) and counted failed placements.
- Forum support: "Each Chaos Cavern has at least one ruin tucked away inside of it" (2016-03-03-56109); "We still have
  some Chaos Caverns without ruins/lost crystals. Continuing to work on this." and "Stalagmites in the Chaos Caverns
  should no longer sometimes poke through the floor of a ruin" (2016-04-12-56901, update notes 13 Apr 2016); "Outposts
  (ruins)" (2016-05-04-57065). Outposts and ruins are the same thing.
- The request that fills the page: AdminCommandPacketEnvServerDataMonitorUpdateRequest 0x1E/0x4AB; the reply 0x4AC has
  no reader in retail and the page can't be opened (architecture-leads.md L3 / L4). The **struct**
  `ClientEnvironmentServerMetrics` above is still the best spec for the admin page's data (hand-off #16).

### 1.2 Messages the client sends (all channel 0)

| Message | Sub | 0.695 / 0.589 | Channel proof |
|---|---|---|---|
| TerrainCell ClientCellRequest | 0xEC/1 | FUN_14314fb60 / FUN_1430a5030 entries; send FUN_1430acba0 (0.589) | channels-695.tsv Zone:1 |
| TerrainCell PendingRequestTimeout | 0xEC/2 | FUN_143157570 / FUN_1430aca10 | Zone:1 |
| TerrainCell ResetAllCells / ResetTemporaryCells | 0xEC/8, 0xEC/9 | FUN_143158bb0 / FUN_1430ae050 → FUN_1431494f0 / FUN_1431496a0 (…, 0, 1, 0) | channel 0; **no caller found (L78)** |
| VoxelConstruction AddTemporaryBlockRequest | 0xF4/0 | FUN_1430b4fd0 (0.589) | Zone:1 |
| ApplyDiffRequest | 0xF4/1 | writer FUN_14315acb0 / FUN_1430b0510; send template FUN_14315c980 / FUN_1430b2860 | callers pass 0 (FUN_14315a820, FUN_143165810) |
| ClientSelectionAreaRequest (copy / paste) | 0xF4/2 | ctor FUN_14315e0a0 / FUN_1430b3990; send FUN_14315cb20 / FUN_1430b2a00 | channel 0 in all four callers |
| ClientUndoRedoRequest | 0xF4/3 | FUN_1430bc070 / FUN_1430ba020 (0.589) | Zone:4 |
| ClientOwnershipRequest | 0xF4/4 | FUN_143130d60 / FUN_143086450 | Zone:1 |
| OffGridPasteClientRequest | 0xF4/5 | ctor FUN_14315e5a0 / FUN_1430b3c90, built in FUN_1431645b0 / FUN_1430b9970 from the paste tool FUN_1436d00b0 (off-grid branch) | handed to a deferred job (FUN_143159fe0 → FUN_143160cf0); **send site / channel not found yet (L77)**. Not handled by the emulator. |
| WorldEditing 0x10C subs 0-7 | 0x10C | §0 item 3 | **channel 3** |

### 1.3 Messages the client receives (handled) and the server-only ids

Client-handled (0xF4 dispatcher FUN_143162830 / FUN_1430b7bf0, re/out/agent_terrainsvc/f4_dispatch_695.c; 0xEC dispatcher
part_0087 ~22140-22380):

- 0xEC: 3 ClientCellUpdateResponse, 4 UpToDateCells, 5 CellsUpdate, **6 MaterialChangeInfo**, 7 CellBlockDamage,
  0xA ModifiedCellManifest, 0xB ModifiedCellManifestUpdate, 0xC ZoneProcessingComplete, 0x10 DetectVeins,
  0x11 EndDetectVeins.
- 0xF4: 6 ClipboardData, 7 Response, 8 ZoneUnknownPropList, 9 ZoneMissingPropList, 0xA ResponseObserved,
  0xB AdminForceInstancesSetting, 0x16 EnvOwnershipUpToDateResponse, 0x17 EnvOwnershipDataResponse,
  **0x1B ZoneDisplayNotification** (reader FUN_14315b9d0 / FUN_1430b1310: `u32 text id, bool`; handler FUN_1431614a0 /
  FUN_1430b6880 shows the text; the emulator never sends it — the natural carrier for "terrain unavailable", §5).
- MaterialChangeInfo 0xEC/6 body (reader FUN_1431468f0): `u16, u32 n × {u64 cell, i32 min[3], i32 max[3], bit set
  (FUN_1431483c0) = BoxPartialBitSet<40,40,40>}, u64`. The u64 keys a block event (FUN_1436bb0e0 / FUN_143608360 map
  +0x98, the coordinator that CellBlockDamage 0xEC/7 and ZoneProcessingComplete 0xEC/0xC also use) and goes to the
  voxel core per cell (FUN_143f94230 → FUN_14401ecf0 / FUN_143f5b010). So: "these voxels of these cells change
  (material = the u16, inference) as part of block event X" — a mining / destruction effect sent before the
  CellsUpdate that carries the same transaction. Field meanings beyond that: L79.

Server-only ids (no client class or reader; architecture-leads.md L8, re/out/agent_arch/serveronly_695.tsv), 0.589
numbering:

| Family | Ids | Reading (who → whom; inference from the names) |
|---|---|---|
| VoxelConstruction 0xF4 | 0xC HealClaimRequest, 0xD PasteClaimAreaRequest, 0xE DeleteClaimAreaRequest | claim side → Env: whole-site operations (site removal / restore / design placement) |
| | 0xF ZoneSelectionAreaRequest, 0x13 OffGridPasteZoneRequest, 0x10 UndoRedoRequest, 0x11 RemoveUndoById | zone → Env: the client's requests forwarded; undo history lives in the Env Server |
| | 0x12 ZoneRequestFailedCellList, **0x15 EnvServerResponse** | Env → zone: results |
| | 0x14 / 0x1A ExportContestClaimRequest / Response | zone → Env → zone: a site exported as a template (contests) |
| | 0x18 / 0x19 Spawn / DespawnExportedTemplateRequest | → Env: stamp / clear an exported template (ruins, Landmarks of Landmark) |
| TerrainCell 0xEC | 0xD AdminSetBotValues, 0xE AdminEnableMaterialVeins, 0xF AdminResetMaterialVeins | admin → Env |
| Claims 0xF9 | 0x33 RevertTerrain, 0x34 RevertAllTerrain, 0x46 ResetTemporaryTerrainDamage, 0x59 BlockVeins, 0x47 ClearAllProceduralProps | claim service → zone (→ Env for the terrain ones) |
| Command 0x1E | 0x4AB / 0x4AC EnvServerDataMonitorUpdate Request / Response | admin page |

### 1.4 What the developers said (live-server-architecture.md)

- "The world is made of layers of voxel data. On the bottom level is the world itself. On top of that, is your data.
  Destruction is applied on top of that ... applying a volume of negative space at the topmost layer"; "it puts in a
  pending 'heal' request with the desired time", and a new destroy does not cancel an earlier pending heal
  (27 Jul 2016).
- "To prevent our voxel database from crashing under load, we are converting it to 64-bit processes" (P0026, 2014).
- Wiki Gathering: land heals about 20 minutes after destruction; gathering only on unclaimed land.
- The live manifest capture (Resources/PacketDumps/TerrainCellManifest.bin, one world, 2016): layer 0 38,190 entries
  (levels 2-10, flag 1 on 34,157), layer 1 11,162 (levels 2-10, flag 1 on 9,039), layer 2 125,246 (levels 2-4, flag 1
  on 387), layer 3 none; 2.27 MB raw, **1.39 MB compressed** in one packet (parsed here; flag meaning L80).

---

## 2. The emulator today (what moves)

### 2.1 Code (LandmarkEmu/LandmarkEmulator.WorldServer)

| File | Lines | Role |
|---|---|---|
| Game/Terrain/TerrainEdits.cs | 1381 | Overlays per (landscape, cell, layer) (`Key = cell | layer << 56`, :41), compact zlib form with ≤ 64 expanded (:189-223), Load (:126), Flush on tick (:238), **Regenerate** = the heal (:314-371), Sphere digs (:375), RevertBox (:663), ApplyDiff / Prepare / Commit (:815-925), Read / Write / MaterialDelta (undo), versions (:1127-1196), BuildUpdate (CellsUpdate with all 3 layers, ore composed into layer 2, :1199), ManifestEntries (:1236), Announce / Broadcast (≤ 256 m full data else 21-byte notes, :1257-1314), one-time layer migration (:1317) |
| Game/Terrain/TerrainStore.cs | 379 | Cell id math, CellsUpdate / manifest / BlockDamage / ProcessingComplete builders, block codec, SendManifest (calls OreVeins.EnsureAround first), the captured mesh store (dev only) |
| Game/Terrain/BlockData.cs | 240 | SerializeBlockData format 2 |
| Game/Terrain/TerrainLod.cs | 344 | LOD copies (layers 0/1 to level 6, layer 2 to level 4; rebuilt 20 s after edits go quiet) |
| Game/Terrain/OreVeins.cs, VeinGenerator.cs, VeinOverlay.cs | 620 + 305 + 85 | .vdt pools, region generation (256 m) around players, ore composed under layer 2, disturbed veins move after 1200 s; prospecting overlays (0xEC/0x10, 0x11) |
| Game/Terrain/VoxelOwnership.cs | 386 | Voxel Vision grids, 0xF4/4 → 0x16 / 0x17, files Ownership/<zone>/<cell>.own |
| Game/Terrain/CavePieces.cs, Game/Caves.cs | 210 + 168 | .cav + .cpa (cave air), cavern list, floor finding |
| Game/Terrain/TerrainHeights.cs, TerrainMaterials.cs, BuildMaterials.cs | 188 + 267 + 88 | baked ground heights (~32 MB per loaded landscape), biome / yield materials, material → cost item |
| Game/Terrain/VoxelTemplate.cs | 118 | .otd reader + Stamp (layer 0) |
| Game/Building.cs | 732 | 0xF4 handler: ApplyDiff (permission per cell `Claims.BuildCheckIn`, stale-version check, materials `PayFor`, Commit, Response, per-cell 0xEC/3, Broadcast, ResponseObserved), Heal (mask-only cells), undo / redo history (in memory, per player), ownership, selection |
| Game/BuildingClipboard.cs | 581 | copy / paste (0xF4/2 → 0xF4/6), design placement, props of a paste, MissingPropList |
| Game/WorldEditing.cs | 192 | 0x10C relay (160 m, newest per builder every 100 ms) |
| Game/Outposts.cs, Game/Spire.cs | 524 + 261 | stamp 23 cavern_ruins_* once (outposts.txt) and the Spire base (spire_base.txt) into layer 0, MarkPermanent |
| Network/Handlers/TerrainHandler.cs | 106 | ClientCellRequest / PendingRequestTimeout: UpToDate when the held versions match, else CellsUpdate; 20 s de-duplication of timeout resends |
| Network/WorldSession.cs ~1131-1150 | — | 0x10C bypasses the flood limiter; 0xEC / 0xF4 have their own limiter (200 burst, 50/s) |

Callers outside Game/Terrain (grep of every `TerrainEdits.* / TerrainStore.* / OreVeins.* / VoxelOwnership.* /
TerrainLod.* / CavePieces.* / VeinOverlay.*` call): Abilities (digs: Sphere, Broadcast, OreVeins.ItemFor / Disturbed),
Building, BuildingClipboard, Claims (RevertBox, OreVeins.ClearSite), SiteContents (RevertBox), SiteDesigns (Read,
OwnedBy), Templates (cell math, Unpack), Consumables (prospecting: OreVeins.Nearest, VeinOverlay), Outposts / Spire
(stamps, MarkPermanent, Flush), Teleport / MiscHandler (SendManifest), Caves (CavePieces.IsAir ×6), WarpNetwork (CellAt),
HostedService (Load, tick: Regenerate, OreVeins.Update, Flush, MigrateToLayers, TerrainLod.Update, WorldEditing.Update),
DevCommands (≈40 calls), plus `ToGlobal` / `ZoneOrigin` coordinate helpers in Props, Chests, Creatures, Gathering,
Pioneer.

### 2.2 Data on disk (dev world, measured 2026-10-15, read-only)

`server-data/worlds/dev/TerrainEdits/` (container path /data/TerrainEdits, WorldServer `AppContext.BaseDirectory/TerrainEdits`):

- `<cellhex>.cell` = `u32 version + BlockData.Save` (zlib of format 2); home continent in the root, other landscapes in
  `<zone>/` (416, 417, 419, 421 present). **3,168 files, 21.3 MB**, all level 2; layer 0: 1,899, layer 1: 1,269,
  layer 2: 0 (digs heal within 20 min and their files are deleted on Flush). Size median 2,640 B, mean 6,887 B, p90
  18,084 B, max 73,525 B.
- `Ownership/<zone>/<cell>.own`: 944 files, 1.5 MB (avg 286 B).
- Markers: `layers.v2` (one-time migration done), `outposts.txt` (cavern, ruin, corner cell), `spire_base.txt` (corner
  cell 159896 199 159774), `version-samples/` (research).
- Written with `File.WriteAllBytes` (not atomic: a crash mid-write can leave a truncated cell; risk §8).

### 2.3 Memory and rates

- Overlays: ~32 MB compact for ~1,950 cells, up to 64 expanded × 384 KB = 24 MB (docs/terrain.md "Memory"; MODLOG
  2026-10-12). Caves LOD 0 data ~35 MB (docs/terrain.md "Caves"). Heights ~32 MB per loaded landscape. World process
  ~930 MB total (MODLOG 2026-10-12). The Environment Server would hold overlays + LOD copies + veins + ownership + caves
  (estimate 150-250 MB; measure in step 7 of §7).
- Traffic seen (re/out/coverage.md / trace/families/00EC_TerrainCell.md play counts; dev opcode_stats.tsv of the
  current run): CellsUpdate 3,185 (older capture) / 613 (current run), ModifiedCellManifestUpdate 1,158 / 127,
  ModifiedCellManifest 222 / 9, ClientCellUpdateResponse 769 per 67 ApplyDiffs (≈11 cells per edit), EnvOwnershipData
  2,548 for one Voxel Vision request (one site = ~2,550 cells, protocol.md §9).
- Sizes: one CellsUpdate for an edited cell carries layers 0-2 (≈ the cell file size; "~12 KB each" measured at login,
  TerrainHandler.cs:35); a login fetched 31 edited cells, requests up to 131 cells were seen, big brushes touch 140+
  cells (MODLOG 2026-10-13); edit processing 1-4 ms small, 100-270 ms for 20-43-cell removes (MODLOG 2026-10-13).
- Client timers that bound latency: a cell answer later than 3 s → PendingRequestTimeout; the scene draws generated
  ground after 6 s (FUN_143f571d0); a pending fetch blocks the build tool (FUN_143f532e0). The client waits for the
  0xF4/7 Response before its next edit.

---

## 3. Service boundaries

### 3.1 Who talks to the client

The **zone server stays the only sender of terrain packets to a player**. Reasons: every terrain request arrives on
channel 0 at the zone (§1.2); the paste rule "TerrainCell sub 3 for every listed cell **before** the new voxels"
(protocol.md §9) and the re-zone rules ("nothing else for that player in the same burst", one re-zone at a time;
docs/terrain.md "Long teleports") need one ordered stream per player, which only the zone has. The Environment Server
never sends to the gateway in this design (it could with TunnelPacketToExternalConnection, but ordering against the
zone's own packets would be lost). Its replies carry client packet bodies verbatim where one exists, so the zone just
forwards them.

### 3.2 What moves into the Environment Server

| Owned data / job | Today | Notes |
|---|---|---|
| Overlays per (landscape, cell, layer 0-3) with versions | TerrainEdits | single writer per landscape |
| LOD copies (job types 0-3, 6) | TerrainLod | |
| Heal queue (job type 4) | TerrainEdits.Regenerate | rewritten as a queue (§4.3) |
| Undo / redo history per player (job type 9) | Building.history | server-only UndoRedoRequest / RemoveUndoById name it |
| Voxel ownership grids (job types 10, 17) | VoxelOwnership | |
| Ore veins: pools, regions, disturbed timers (job types 15, 16) | OreVeins / VeinGenerator | |
| Exported-template stamps: Spire base, Landmarks of Landmark, ruins (job types 8, 13) | Spire, Outposts (voxel part) | |
| Ruin layers {desired, instances, failed, last id} | Outposts.Choose / outposts.txt | §4.5 |
| Compressed manifest per landscape (job type 18) | TerrainStore.BuildManifest | zone caches it (§5) |
| Box operations (job type 14): site revert / heal / delete / reset temporary damage, BlockVeins | Claims, SiteContents | |
| Read-only inputs: .cav / .cpa, .vdt, .otd, VoxelMaterials / BuildMaterials, TerrainHeights (vein starts, dig shells) | loaded at start | same files, mounted read-only |

### 3.3 What stays in the zone

- Packet parsing / replies, per-player rate limits, the re-zone hold queue, interest ranges (who gets full cell data
  vs a 21-byte note), ResponseObserved effects, prospecting overlays (VeinOverlay: the zone asks the Env Server for
  nearest veins), WorldEditing relay (channel 3; L75).
- Claims and permissions (or the claim service, when it exists): the zone sends the **allowed boxes** with each edit.
- Inventory and materials: the zone sends the player's **material budget** and charges the delta the reply returns.
- Gameplay on terrain: mining yields (TerrainMaterials, heights), trees, creatures (Caves.FloorNear / CavePieces.IsAir
  for cave floors — the zone keeps its own read-only copy of the cave data), outpost gameplay (garrison, Lost Chest,
  Lost Crystal, "Eureka!" offer), the Spire's props, CaveManager-style underground teleports.
- Clipboard per player (the copy's voxels come from the client in ClientSelectionAreaRequest; the zone keeps them and
  sends them with a paste).

### 3.4 Environment Server is not a channel back end

Cells and edits are on channel 0, so the gateway's ChannelIsRoutable can't tell the client the Env Server is down;
outages are handled by the zone (§5). (If channel 3 is ever routed to it, that one channel could be reported.)

---

## 4. Data, storage and the job model

### 4.1 Layers (as live: docs/terrain.md "Layers", §1.1, §1.4)

| Layer | Job name | Content | Persistence | Heals |
|---|---|---|---|---|
| 0 Poi | Cell Poi Lod | server stamps: Spire base, ruins / outposts, Landmarks of Landmark | file per cell | never; cleared by DespawnExportedTemplate |
| 1 Permanent | Cell Permanent Lod | building on build sites (the client keeps its own edits here) | file per cell | never; cleared by site removal (HealClaim / DeleteClaimArea / RevertTerrain) |
| 2 Timed Persistent | Cell Timed Persistent Lod | destruction outside sites (digs, mining, explosions) + ore veins composed underneath | file per cell + heal queue | by the heal queue |
| 3 Transient | Cell Transient Lod | nothing (none in the live capture) | — | — |

### 4.2 Storage layout (the Env Server's own volume, `server-data/worlds/<key>/envserver/`)

Keep today's formats so the data moves by copying (migration §7 step 4): `TerrainEdits/` as now (cells, Ownership/,
markers), plus:

- `heal-queue.log`: append-only records `{u64 id, u32 landscape, u64 due unix ms, u8 kind (box | cell list), box
  (6 × f64 global dm) or cells (u32 n × u64)}`, and `heal-done` records `{u64 id}`; compacted into `heal-queue.snap`
  at start and every hour. Loaded at start; overdue entries run first.
- `undo/<player guid>.bin` (optional, phase 2): undo history survives an Env Server restart (today it's lost on any
  restart). Live kept it server-side (RemoveUndoById); depth 20 as today.
- `ruins.tsv` replacing outposts.txt: `layer, instance id, template, cavern, corner cell x y z, spawned at`, and
  `ruin-layers.tsv`: `layer, desired, last id, failed`.
- `stamps.tsv` replacing spire_base.txt: `stamp id, template, landscape, corner cell` (Spire, Landmarks of Landmark).
- All writes: write to `*.tmp` then rename (atomic on the same volume).

### 4.3 Heal queue (rewrite of TerrainEdits.Regenerate)

**Built 2026-10-15 in-process** (EnvServer.Core/Terrain/HealQueue.cs, TerrainEdits.HealCell; text format
heal-queue.log in TerrainEdits/ rather than the binary layout below): one entry per destruction = its sphere + cells +
due; at due time the sphere's layer-2 voxels heal except those held by a still-pending entry or inside a site. Tested
on 0.695: two overlapping digs healed at their own times (CellsUpdate to the client each time); a heal due after a
world restart happened on time.

- Every destruction that writes layer 2 (dig sphere, mining, explosive) adds **one entry** with due = now +
  `HealDelay` (20 min, wiki Gathering) covering the cells it touched; later destructions add their own entries and
  never move an earlier one (dev, 27 Jul 2016).
- When an entry is due: in each of its cells, layer-2 voxels that the entry's destruction set (or, simpler first
  version, all layer-2 voxels in the entry's cells that no *later, still pending* entry covers) go back to empty;
  version bump; the cells are pushed to the zones (CellVersionsChanged, §5.2). Build sites never hold layer-2 voxels
  because placing a site resets them (Claims 0xF9/0x46 ResetTemporaryTerrainDamage → box operation); the claim boxes
  are still checked as a safety net (pushed to the Env Server with ZoneAddClaim / ZoneRemoveClaim style messages).
- Granularity of live (per volume vs per cell) and whether a heal restored only the destroyed volume: **not found yet
  (L76)**; the per-entry cell list is the closest model the dev text supports.
- Ore veins: unchanged (composed under layer 2; a disturbed vein moves after RESPAWN_TIME_DIRTY 1200 s — job type 16).
- Legacy: today's Regenerate also heals layer-0 cells that are not permanent (pre-layer edits). The migration keeps
  that behaviour once (moves such voxels to layer 2 with a heal entry) and then layer 0 never heals.

### 4.4 Jobs

One job queue per landscape, worker pool sized by CPU (edits are single-writer per landscape; LOD, vein, manifest and
ownership jobs run in the background). Every job has a type from the table in §1.1; the service keeps Active / Pending
counters per type and Total Jobs exactly like `ClientEnvironmentServerMetrics`, so the admin page (#16) can show the
client's own layout. Job type 5 Cell Generation is defined but unused until a server feature needs base-world voxels
(pathing; not in scope).

### 4.5 Ruin layers and exported templates

- **Ruin layer** = a set the Env Server keeps filled: `{layer id, desired count, active instances, failed placements,
  last instance id}` (§1.1). A placement job picks a cavern (largest open caverns, ≥ 400 m apart — today's
  Outposts.Choose rule; live's rule not found yet), stamps the template into layer 0, records the instance, and reports
  it to the zones (instance id, template, corner, the template's props / spawner props) so the zone builds the
  outpost's gameplay. A placement that finds no space counts as Failed (live: "Chaos Caverns without ruins").
- What a ruin layer *is* (a continent cave layer, a tier, a template set): **not found yet (L76)**. First build: one
  ruin layer per landscape with caves, desired = the number of complete `cavern_ruins_*` templates (23 today); the 35
  re-saved `ruin_*` templates of 0.695 (terrain-695.md §4, 1,750 spawner props) become a second set once L76 / TASKS
  0.T3 decide their role.
- **Exported templates**: SpawnExportedTemplateRequest / DespawnExportedTemplateRequest (0xF4/0x18 / 0x19) stamp /
  clear a template in layer 0 and mark its cells permanent: the Spire base (Spire.cs), Landmarks of Landmark beside
  each spire (TASKS 3.20), ruins. Seasonal islands (TASKS 3.19) need only that the Env Server serves those landscapes
  (no caves, no veins: terrain-695.md §7).
- ExportContestClaimRequest / Response (0xF4/0x14 / 0x1A): read a site's layer-1 voxels (+ the zone's props) into a
  template (contest entries; today Contests / SiteDesigns read TerrainEdits directly).

---

## 5. Internal messages and zone-side caching

### 5.1 Framing

internal-transport.md: one TCP stream zone → Env Server (`ENVSERVER_ADDRESS=envserver:20300` in `.env`), frames
`u32 length, u16 internal opcode, body`; messages are `ServicePacket {u16 family, u32 sub, body}` using the client's
family / sub where a name exists (0.589 numbering). Every request body starts with `u32 request id, u32 landscape`
(StaticZoneData zone id; 0 = home continent today) and replies echo the request id. Global positions in dm (f64),
cells as VoxelFarm u64 ids. Bodies below are **ours** (the client never sees them; L48 stays open for live's layouts).

### 5.2 Messages

Zone → Env Server:

| Family / sub | Name | Body (after request id, landscape) | Reply |
|---|---|---|---|
| 0xEC/0x1 | ClientCellRequest (fetch) | `u32 n × u64 cell` (only cells the zone's version index lists and its payload cache misses) | 0xEC/0x5 per cell: the exact client CellsUpdate body (all layers, ore composed) |
| 0xEC/0xA | ModifiedCellManifest (fetch) | `u64 known version stamp` | the compressed client manifest body + version stamp, or "unchanged" |
| 0xF4/0x1 | ApplyDiffRequest | `u64 player guid, u32 n × box (allowed build boxes, 6 × f64), material budget u32 n × {u32 item, u32 count, i32 credit voxels}, u8 op flags`, then the client's ApplyDiffRequest body unchanged | 0xF4/0x15 EnvServerResponse |
| 0xF4/0x10 | UndoRedoRequest | `u64 player, u8 0 undo / 1 redo, boxes, budget` | EnvServerResponse |
| 0xF4/0x11 | RemoveUndoById | `u64 player, u64 edit id (0 = all)` | — |
| 0xF4/0xF | ZoneSelectionAreaRequest | `u64 player, boxes, budget, u32 action (4 paste), origin cell + voxel, 3×3 turn, clipboard blocks (client block list format)` | EnvServerResponse |
| 0xF4/0x13 | OffGridPasteZoneRequest | as 0xF with the off-grid transform (layout after L77) | EnvServerResponse |
| 0xF4/0x4 | OwnershipRequest | `u32 n × {u64 cell, u32 cached version}` | per cell the client 0xF4/0x16 or 0x17 body |
| 0xF4/0xC / 0xD / 0xE | HealClaimRequest / PasteClaimAreaRequest / DeleteClaimAreaRequest | `u64 claim, box`, (paste: template id + corner + turn, budget) | EnvServerResponse |
| 0xF9/0x46 | ResetTemporaryTerrainDamage | `box` | — (CellVersionsChanged) |
| 0xF9/0x33 / 0x34 | RevertTerrain / RevertAllTerrain | `u64 claim, box` / landscape | — |
| 0xF9/0x59 | BlockVeins | `u32 n × box` (claims: no veins inside) | — |
| 0xF9/0x48 / 0x49 / 0x4B | ZoneAddClaim / ZoneRemoveClaim / ZoneAddAllClaims | `u64 claim, box` (only the boxes) | — |
| 0xF4/0x14 | ExportContestClaimRequest | `u64 claim, box` | 0xF4/0x1A ExportContestClaimResponse: template blocks |
| 0xF4/0x18 / 0x19 | Spawn / DespawnExportedTemplateRequest | `u64 stamp id, template name, corner cell, u8 turn` | EnvServerResponse |
| ours `TerrainDigRequest` | — | `u64 actor, f64 x y z centre, f32 radius, u16 material (0 = carve), u8 flags (ore counting, shell)` | EnvServerResponse with ore voxels mined per material |
| ours `VeinQuery` | — | `u8 kind (ensure-around / nearest / near), f64 x y z, f32 range` | vein list (VeinOverlay format) |
| ours `CaveQuery` | — | cavern list / random cave beginning (CaveManager equivalent, L51) | positions |
| 0xEC/0xE / 0xF | AdminEnable / ResetMaterialVeins | `u8` | — |
| 0x1E/0x4AB | EnvServerDataMonitorUpdateRequest | — | 0x1E/0x4AC: `u32 19 × active, u32 19 × pending, u32 total, u32 n × {u32 desired, u32 active, u32 failed, i32 last id}` (= ClientEnvironmentServerMetrics) |
| ours `EnvAdminCommand` | — | dev commands (terrainmem, regendelay, clearcell, ownblock …) as text | text |

Env Server → zone:

| Family / sub | Name | Body |
|---|---|---|
| 0xF4/0x15 | EnvServerResponse | `u32 request id, u32 result` (the client's Response codes: 0, 1 Failed, 0xC materials, 0xD props, 0xE permissions, 0x11 template voxels, 0x1A), `u32 n × {u32 item, i32 count}` material delta to charge / refund, `u32 n × {u64 cell, u32 3 × layer version}` changed cells, `u32 n × u64` stale cells (resend the server copy), `u8 observed op + 2 × f64[3] bounds` (ResponseObserved), `u32 n × prop` (pasted props for the zone to place), `u32 voxels`, `u64 edit id` |
| 0xF4/0x12 | ZoneRequestFailedCellList | `u32 n × u64` cells whose edit failed (zone resends them) |
| 0xEC/0xB (list) | CellVersionsChanged | `u32 landscape, u64 sequence, u32 n × {u64 cell, u32 layer code, u32 version, u8 flag}` — the client's ModifiedCellManifestUpdate entry layout, batched; pushed for heals, other zones' edits, veins, LOD, stamps |
| ours `RuinInstance` | — | `u32 layer, i32 instance id, template, corner cell, u8 spawned / removed` |
| ours `EnvStatus` | — | up / degraded, per-landscape loaded |

### 5.3 Zone-side cache (latency)

1. **Version index** per landscape: every (cell, layer) the Env Server holds with its version, LOD copies and vein
   cells included, kept in step by CellVersionsChanged (sequence numbers; a gap or a reconnect → full resync by
   ModifiedCellManifest fetch). Size: the live world had 174,598 entries (13 B each raw = 2.3 MB); ours is ~4k + LOD
   + veins. Used for: the zone-in / re-zone **manifest without a round trip** (Teleport.Rezone sends it right after
   ClientBeginZoning, docs/terrain.md), UpToDate answers, and filtering requests (cells not in the index are answered
   UpToDate at once — most requests).
2. **Compressed manifest** per landscape (job type 18): fetched once, refreshed when the version stamp changes; the
   per-player vein regions (OreVeins.EnsureAround) are added as manifest notes after the vein query returns, as today.
3. **CellsUpdate payload LRU**: key (cell, 3 layer versions) → client CellsUpdate bytes, cap 64 MB (≈ 6,000 cells at
   the measured mean). Many players fetching the same Spire / outpost / site cells hit the cache; an edit evicts by
   version. The PendingRequestTimeout de-duplication (TerrainHandler, 20 s) stays in the zone.

### 5.4 Bandwidth rules (memory: bandwidth efficiency)

To players, unchanged: full CellsUpdate only within 256 m, otherwise one 21-byte ModifiedCellManifestUpdate; nothing
pushed to the builder for their own edit cells; LOD notes only after 20 s of quiet; WorldEditing newest-per-builder
every 100 ms within 160 m. Inside the server: CellVersionsChanged batched per 50 ms tick and per landscape; heals
batched per minute; payloads only on request (no push of voxel data to zones); CellsUpdate built once per version (LRU
on both sides).

---

### 5.5 The remote split: no voxel mirror, every write a transaction in the service (decided 2026-10-16)

The open question of TASKS B.3 ("the remote split needs a zone-side mirror or ApplyDiff moved into the service — Building
reads voxels synchronously") was decided from the code:

- **What the zone actually reads synchronously.** Building's ApplyDiff read the store per cell only to (1) check the
  layer-1 version the client edited from (stale check), (2) take the "before" values for the undo record and the
  material difference (TerrainEdits.Prepare), (3) write (Commit) — all inside one handler call, with PayFor between
  (2) and (3). Undo / redo (MaterialDelta + Write per cell), the Heal tool (Read / MaterialDelta / Read / Write per
  cell) and paste (MaterialDelta, then Read / Write / Read per cell, BuildingClipboard) have the same shape. The
  permission check (ZoneTerrain.MayApply → TerrainEdits.MayApply) is arithmetic on the mask and the zone's claims: it
  never reads the store. Outposts / Spire / Landmarks of Landmark only stamp, mark permanent, flush and revert boxes;
  Abilities / VoxelDestruction dig with a per-voxel callback built from the zone's sites; TerrainHandler and
  ZoneTerrain read versions, CellsUpdate bodies and the manifest.
- **A mirror was rejected:** the zone would hold every overlay (~32 MB compact + up to 24 MB expanded today, more with
  LOD copies and veins) and apply every write twice, while heals, vein moves, LOD refreshes and stamps happen in the
  service — so the service would have to push voxel data to the zone (against §5.4 "payloads only on request") and two
  writers would touch the same cells (against §3.2 / §4.4 "single writer per landscape").
- **Chosen: each voxel operation is one transaction in the service**, with the zone's checks done before it
  (permissions) or sent along with it (data, never callbacks): ApplyEdit (stale check + Prepare + material check +
  Commit; answer = the undo record + the material difference + the stale cells), WriteCells (undo / redo, paste),
  ClearCells (the Heal tool), the dig Sphere with a VoxelFilter (the sites' Play Mode rules and Linking Areas as boxes
  and cylinders instead of the callback), OwnedBy with a box. The **material budget** (§5.2) carries, per material item,
  the items in the bag and Building's voxel credit; the service refuses the whole edit with 0xC before writing anything
  (PayFor's arithmetic, MaterialBudget.Covers), and the zone charges exactly the returned difference on the same
  world-thread turn (§8 "budget race"). The in-process store runs the same transactions (LocalEnvironmentServer), so
  Building has one code path.
- **Latency budget:** the client waits only for the 0xF4/7 Response before its next edit and gives up on a cell after
  3 s (PendingRequestTimeout); one request / answer on the Docker network is well under a millisecond against 1-4 ms
  (small) / 100-270 ms (20-43-cell removes) of store work measured in-process (MODLOG 2026-10-13). The frequent reads
  never cross the network: the **version index** answers Has / LayerVersion(s) / CurrentVersion and builds the
  manifest; the **CellsUpdate cache** (64 MB LRU) and **batched fetches** (BuildCellsUpdates: one round trip per cell
  request or broadcast, 64 cells per message) serve the bodies.
- **Kept in the zone (deviations from §3.2):** the undo / redo **history** (it also holds the props a paste placed and
  prop moves, which are zone data; the service does the voxel half — no UndoRedoRequest / RemoveUndoById yet); the
  **placement markers** of Spire, outposts and Landmarks of Landmark (Game/Outposts.cs, Spire.cs and
  LandmarksOfLandmark.cs still decide placement; the service records what it stamped in stamps.tsv / ruins.tsv, L127).
- **Messages** (LandmarkEmulator.Internal/Environment/EnvironmentMessages.cs): one envelope — request = ServicePacket
  {family, sub, u32 request id, u32 landscape, payload}; every answer = EnvServerResponse 0xF4/0x15 {u32 request id,
  u32 result, payload}; the client's ids where a name exists — 0xEC/1 (cell fetch), 0xEC/0xB (CellVersionsChanged, plus
  a u8 reason), 0xF4/1 ApplyDiffRequest, 0xF4/4 ownership, 0xF4/0x18 SpawnExportedTemplateRequest (stamps), 0xF9/0x33
  RevertTerrain, 0x1E/0x4AB the job counters — and ours from 0x10000 (hello, version index, keep-out boxes, player
  positions, sphere, areas, veins, WriteCells, ClearCells, Read / Write / MaterialDelta, OwnedBy, census, dev text). The
  service sends the version changes a request caused before its answer on the same link, so the zone's index is
  current when its call returns. A template goes by file name (the service reads its own copy of the game's templates
  folder, so a decoded ruin of up to ~0.6 GB never crosses the network) or, read from bytes (Landmarks of Landmark),
  inline as a minimal .otd.
- **Keep-out boxes instead of callbacks:** the build sites (Claims.AllBoxes) and Spire platforms (Spire.PlatformBoxes)
  go to the service whole, on connect and whenever they change (checked every 5 s); the heal, the layer move and the
  vein spawner use them as Claims.BoxesIn / Spire.Touches did. Until a zone has sent them the service runs no heal, no
  vein spawning and no layer move (an empty list would let a heal take site voxels); the persisted heal queue catches
  up. Permanent-cell marks (Spire base, ruins) are re-sent on every connect, before the boxes (the service keeps them in
  memory only, and the legacy layer-0 heal must never touch a stamp).
- Packet families touched, checked against the 0.695 trace first (trace/0695/coverage-695.md): TerrainCell 0xEC subs
  1-5, 0xA, 0xB and VoxelConstruction 0xF4 subs 1-4, 6, 7, 0xA, 0x16, 0x17, 0x1B are "same"; nothing the client receives
  changes layout. New to the client: ZoneDisplayNotification 0xF4/0x1B (§6), read in the 0.695 decompile (reader
  FUN_14315b9d0: u32 text id, bool; handler FUN_1431614a0: bool set → BaseClient::ShowSystemMessage FUN_142d56570,
  else the chat-output object [I], L126).

## 6. Behaviour when the Environment Server is down

The zone marks the landscape's terrain "unavailable" when the link drops (and on EnvStatus degraded):

- **Cell requests:** cells in the version index whose payload is cached → answered normally; cells not in the index →
  UpToDate (correct: nothing edited there); cells in the index but not cached → UpToDate as well (the client keeps its
  cached copy or the generated ground instead of stalling 6 s and blocking its build tool), and the zone remembers
  (player, cell) to send a ModifiedCellManifestUpdate when the Env Server returns so the client refetches.
- **Edits / undo / paste / ownership:** refused at once: Response 0xF4/7 result 1 (Construction.GeneralError), a
  ClientCellUpdateResponse ok for every cell (never leave a cell pending), the cached server copy re-sent where cached,
  and a ZoneDisplayNotification 0xF4/0x1B with a text id telling the player building is unavailable (text id to pick
  from the client's text table; "Connection to the Build Site Server is unavailable" 562155 is the closest existing
  one). Voxel Vision: every cell answered UpToDate.
- **Digs / mining:** the swing hits but yields nothing and changes nothing (no item without a terrain write).
- **Heals, veins, ruins:** pause; the persisted queue catches up on return (overdue heals first).
- On reconnect: full version-index resync, the notes from above, then normal service. Players stay connected
  throughout (the gateway and the zone are untouched).

---

## 7. Build and test plan (isolated Docker copy; the running dev servers stay untouched)

Per BUILD-PATH rule 7 and 11:

1. **Archive**: git tag `envserver-v0` + `archive/terrain-v2/` (Game/Terrain, Building*, WorldEditing, Outposts, Spire,
   TerrainHandler).
2. **Library split, no behaviour change**: move the store side into `LandmarkEmulator.EnvServer.Core` (class library)
   behind an `IEnvironmentServer` interface; the world server uses an in-process implementation. Regression: the
   re/test scenarios regen, voxelvision, holoseeker, outpost, caverns, rezonecave, teleport, adventure, safepoint,
   localtravel, the Frida probes terrainpkt.js / cellreq.js (every terrain packet decodes exactly), VoxelDataErrors.log
   mismatch counts as in docs/terrain.md.
3. **Messages + framing** (§5) in LandmarkEmulator.Shared with round-trip unit tests; the remote `IEnvironmentServer`
   client in the zone with the version index and payload LRU (§5.3).
4. **Service host** `LandmarkEmulator.EnvServer` (console app, tick: jobs, heal queue, Flush, LOD, veins) and the data
   migration: copy `TerrainEdits/` into the Env Server volume; outposts.txt → ruins.tsv, spire_base.txt → stamps.tsv.
5. **Heal queue** (§4.3) and **ruin layers** (§4.5) behind config switches (`HealQueue`, `RuinLayers`), old behaviour
   kept until verified.
6. **Docker**: Dockerfile target `envserver`; compose service `envserver` (own volume `/data` =
   server-data/worlds/<key>/envserver, read-only /game and /gamedata, health check = a ping message), world gets
   `ENVSERVER_ADDRESS` and `depends_on: envserver (healthy)`. Isolated copy: `docker compose -p lmemu-envsvc-test`,
   world port 29000/29001, network `lmemu-envsvc-test`, image tags `:envsvc-test`, a **copy** of the dev world's data
   in tmp/envsvc-test (never the dev volume); removed afterwards.
7. **Tests against the isolated copy (test client via re/dlogin.sh, direct connect)** — verified by the client's
   reaction, not server logs:
   - login fetch (31 edited cells, VoxelDataErrors.log 0 mismatches at zone-in), re-zone into a cave (manifest right
     after ClientBeginZoning, no hang), Spire stands;
   - build / remove / paint / smooth / heal tool / undo / redo / copy / paste / design placement / Voxel Vision
     (2,550 cells); a second client sees the edits (≤ 256 m) or gets notes (> 256 m);
   - dig with `regendelay 60`: heal happens on time, **survives an Env Server restart** (restart between dig and due);
     two digs in one cell heal at their own times;
   - stop the Env Server mid-session: requests answered, edits refused with the message, no client stall
     (ClientSceneProcessingErrors.log "ServerResponsePending"), start it: notes arrive, cells refetch;
   - measure: per-edit latency (zone log, compared with 1-4 ms / 100-270 ms today), cache hit rate, internal bytes / s,
     `docker stats` memory of world and envserver, manifest build time.
8. Docs (LandmarkEmu/docs/terrain.md + docker.md), MODLOG, TESTING-NEEDED.md, then merge into the root compose.

**Built 2026-10-16 (steps 3-6, off by default; untested on the Docker stack and with the client).** Decision §5.5.
- Step 3: messages in LandmarkEmulator.Internal/Environment (not LandmarkEmulator.Shared: the Internal transport is
  where every service's messages live now); converters in EnvServer.Core/Terrain/EnvironmentWire.cs; the zone's
  Game/Terrain/RemoteEnvironmentServer.cs (version index with sequence numbers and a resync on a gap, manifest from the
  index, CellsUpdate LRU, batched fetches, down behaviour §6, reverts / vein clears kept for the reconnect, permanent
  marks re-sent); IEnvironmentServer now carries data only (ApplyEdit / WriteCells / ClearCells / BuildCellsUpdates,
  VoxelFilter, VoxelBox, MaterialBudget; BuildMaterials moved into EnvServer.Core). The solution has no unit-test
  project: a scratch smoke test (service + remote client in one process over loopback, temporary data) passed every
  check — budget refusal, edit, compare report, index = store, fetched body = in-process body, cache hit, manifest =
  in-process manifest, stale cell, undo / redo, Heal tool, read / write / census / owned-by / ownership query, revert box,
  filtered and unfiltered fill sphere; service stopped → unavailable, edit refused, Voxel Vision up to date, stamp
  throws; service restarted → reconnect and index = store; dig → heal pushed → index = store; the one-time import and
  the marker records.
- Step 4: LandmarkEmulator.EnvServer (console host; EnvHub = one work thread for requests and upkeep, as the world
  thread was; EnvServer.json / environment: Network 20500 — not the 20300 of §5.1, which is the gateway's internal port
  —, DataPath, ImportFrom, GameDirectory, GameDataPath, Continent, HealQueue, RuinLayers, RuinLayerDesired,
  TerrainLodCopies, KeepVertexOnlyVoxels, HealthFile); DataImport.cs = the one-time copy of the world's TerrainEdits
  (temporary folder + rename, marker import.done) with the zone's markers recorded as ruins.tsv / stamps.tsv; cell files
  written atomically (TerrainEdits.AtomicWrites, on in the service only).
- Step 5: HealQueue (default on = the queue verified in-process 2026-10-15; off pauses heals and loses nothing — the
  old per-cell heal no longer exists to switch back to) and RuinLayers (default off: ruins.tsv / ruin-layers.tsv,
  Desired / Active / Failed / Last ID in the job counters, RuinRecords.cs; placement stays in the zone, L127). Job
  counters in the client's layout (JobCounters.cs; type mapping [I], L128), shown on /admin/monitor via ServiceStatus
  and answered to 0x1E/0x4AB.
- Step 6: Dockerfile stage envserver, docker/envserver/docker-compose.yml (volume server-data/worlds/<key>/envserver, the
  world's folder read-only at /world-data for the import, /game and /gamedata read-only, health check = an alive file
  the loop touches every 10 s instead of a ping message, aliases envserver and envserver-<key>), root compose profile
  envserver, docker/world EnvServerAddress from ENVSERVER_ADDRESS, .env.example. No depends_on from the world (a
  dependency on a profile service fails while the profile is off): the zone waits for the service itself
  (EnvServerStartupWaitSeconds, 120 s). Test stack: re/test/socialtest up.sh ENVSVC=1 (TESTING-NEEDED.md "Environment
  Server in its own container"). Step 7 (the client tests) is open.

---

## 8. Risks

- **Ordering / double senders** — avoided by keeping the zone the only sender (§3.1); any later "Env Server talks to
  the gateway directly" optimisation must respect the paste and re-zone rules.
- **Stale version index** → a client keeps an old cell. Sequence numbers + resync on gaps; the client's own versions
  (CRC32 of its layer, adopted on Commit) stay authoritative for layer 1.
- **Edit latency**: the client blocks its next edit until the Response; one round trip inside one host is ~0.2-1 ms,
  but a 1,024-cell ApplyDiff can be several MB to forward. Keep the original body as-is (no re-encoding).
- **Budget race**: the inventory may change between the budget and the charge; the zone holds the player's inventory
  for the duration of the call (per-session serial handling) and charges exactly the returned delta.
- **Memory duplication**: caves and heights loaded by both containers (~70 MB). Acceptable; can move behind CaveQuery
  later.
- **Heal semantics change** is visible to players (holes heal on their own timers) — announce in TESTING-NEEDED.
- **Crash safety**: today's non-atomic file writes (§2.2) — fixed by temp + rename in the new host.
- **One Env Server per world vs per landscape**: live's P0026 says "processes" (plural); per-landscape sharding is
  possible later because every message carries the landscape.

---

## 9. Open leads added (trace/0695/OPEN-LEADS.md)

- **L75** who relayed the channel-3 WorldEditing packets on live (gateway, a voxel back end, or the zone).
- **L76** heal-queue granularity and timing on live; what a "ruin layer" is; job types 4 / 5 / 6 / 17 in detail.
- **L77** OffGridPasteClientRequest 0xF4/5: send site, channel and body (deferred job FUN_143159fe0 / FUN_143160cf0).
- **L78** ResetAllCells / ResetTemporaryCells 0xEC/8 / 9 sender FUN_143158bb0 / FUN_1430ae050 has no caller found; what the
  server did with them.
- **L79** MaterialChangeInfo 0xEC/6: meaning of the u16 and the u64, when live sent it.
- **L80** the u8 flag of manifest entries (live: mostly 1 in layers 0/1, mostly 0 in layer 2) and the "set 1" layer
  codes 5-8 of ModifiedCellManifestUpdate.
- **L125-L128** (2026-10-16, with the build of §7 steps 3-6): how live's zone handed ApplyDiffRequest to the Env Server
  (no zone-side twin in the id table); ZoneDisplayNotification 0xF4/0x1B's use and bool; ruin-layer placement in the
  service; which operations live counted under job types 4 / 5 / 6 / 17.

---

## 10. Corrections for other files (coordinator to merge)

- trace/architecture/channels.md "Channel 3": add WorldEditingBoxUpdate / SphereUpdate / ShapeUpdate / SelectionUpdate
  (0.695 FUN_143085f20 / FUN_143086400 / FUN_143086280 / FUN_143086110, third argument 3); channel 3 = all eight 0x10C
  messages (+ the non-Landmark vehicle / event ones already listed).
- trace/live-server-architecture.md: the bullet "Voxel channel: terrain cell requests (static count: 26 send
  templates)" under "Which back end each message went to" is stale (cells are on channel 0); the table row "Voxel
  database / terrain service" → "Environment Server (voxel database, construction and vein jobs, ruin layers)".
- trace/0695/architecture-leads.md L8 table: remove TerrainCell 0xEC/0x6 from the server-only list (client-handled
  MaterialChangeInfo, §1.3).
- LandmarkEmu/docs/terrain.md "Layers": the names come from FUN_143bb4ef0 = the Environment Server's job-type namer
  (types 0-3 are the layer LOD jobs).
- trace/families/0009_TerrainCell.md is a catalogue artefact (ResetTemporaryCells is 0xEC/9).
- trace/families/010C_WorldEditingClear.md / 00F4_VoxelConstruction.md / 00EC_TerrainCell.md server statuses are older
  than Game/WorldEditing.cs, ResponseObserved and ClientCellUpdateResponse (all sent now; regenerate with trace.py).

---

## 11. Existing research used

| File | What it already settled (not re-traced here) |
|---|---|
| LandmarkEmu/docs/terrain.md | cell ids, manifest / request / CellsUpdate flow, block format 2, layers 0-3 and their use, CRC32 layer versions, busy-client rule, LOD copies, memory model, caves (.cav / .cpa), veins, heights, templates (.otd), re-zone rules, protocol probe results, mesh cache, allocator-slot slow loads |
| LandmarkEmu/docs/protocol.md §8-§9 | 0xEC and 0xF4 layouts: ApplyDiff, Response codes, selection / clipboard / paste order rule, ResponseObserved, ownership request / grid format, WorldEditing layouts and relay rules |
| trace/systems/terrain.md | VoxelCellBlockCache handlers, scene timer 6 s, conflict flag, request versions, layer-1 ownership of client edits, rewrite plan (terrain v2) |
| trace/systems/building.md | ApplyDiff tail fields, ResponseObserved trace, Response / undo trace, statuses per sub |
| trace/systems/claims.md | snapshot save / restore messages (never sent), LostClaimServerConnection, site sizes |
| trace/systems/gathering.md | 20-minute regrowth, gathering only on unclaimed land, vein / tree regrowth status |
| trace/families/00EC, 00F4, 010C, 0009, 00F9 | per-message builders / readers and play counts (older statuses, §10) |
| trace/FAMILIES.md, trace/managers.md, trace/server-only-data.md | family index; VoxelDbDataManager (client definitions loader FUN_143f07a10, not the voxel database), VoxelDamageManager, ClientWorldEditingEffectManager; TerrainObjectDeploymentGroups.txt is a server-only table (loader FUN_143be10a0) |
| trace/live-server-architecture.md | P0026 voxel database, 27 Jul 2016 layer / heal text, zone crash notes |
| trace/architecture/channels.md + channels-695.tsv | channel per client message; corrected here for 0x10C |
| trace/0695/architecture-leads.md | L8 server-only id inventory, caves offline (.cpa), CaveManager, L3 / L4 monitor reachability |
| trace/0695/terrain-695.md | no terrain protocol change in 0.695; ruin_* templates; event islands without caves / veins |
| MODLOG.md | terrain sections 2026-10-08 … 10-14 (live edits, layers, regrowth, veins, ownership, outposts, compact overlays, flood limiter, versions), slow-load allocator slots, cave re-zone |
| memory: cave-teleport-hang, bandwidth-efficiency, landmark-terrain-progress | one re-zone at a time / hold packets; coalesce and interest ranges |

---

## 12. Hand-offs

- **#13 gateway:** channel 3 (WorldEditing previews) may be relayed in the gateway, which sees every channel-2
  position (it even sends PlayerUpdatePositionGatewayToClient); decide with L75. The Env Server needs no gateway route.
- **#16 operations / admin page:** show the Env Server metrics with the client's layout (§1.1, 0x1E/0x4AC body §5.2):
  19 job rows (name, active, pending), total, ruin layers (desired, active, failed, last id); scheduled restarts must
  let the Env Server flush (heal queue, cells) before the zones go down.
- **Claim service (when built, #12 / coordinator):** send ZoneAddClaim / ZoneRemoveClaim boxes, ResetTemporaryTerrainDamage
  on site placement, BlockVeins, RevertTerrain / HealClaim / DeleteClaimArea on site removal to the Env Server too.
- **Zone gameplay (coordinator):** outpost gameplay consumes RuinInstance; 0.695 ruin_* spawner props → server spawns
  (TASKS 0.T3) once a ruin layer uses them.
- **#3 / packets:** OffGridPasteClientRequest layout (L77).
