# Terrain (VoxelFarm)

Landmark's world is a VoxelFarm voxel terrain. The client already contains the whole generator: it builds the
untouched world itself from data files in its install (`CommonData/Voxel/…`). The server only has to store and send
**what players changed**. This document explains the formats involved and how `Game/Terrain/*` uses them.

## Cells

- A cell is a cube of **40 × 40 × 40 voxels**.
- At level `L` a cell spans `40 · 2^L` world units (decimetres). Live edits happen at **level 2**: 160 dm = 16 m
  cells, so one voxel is 0.4 m.
- **Cell id** (u64): `level << 60 | x << 36 | (y · 64) << 18 | z`. The low 6 bits of the y field are always 0.
- **Voxel index** inside a cell, used everywhere in the server and in the masks below: `y + 40 · (x + 40 · z)`.
- Global position of a cell corner = cell coordinate × cell size, in global decimetres. Local metres = (global −
  zone origin) / 10.

## Getting cells to the client

1. At zone-in the server sends a **manifest** (TerrainCell 0x0A): per layer 0–3, `list<{u64 cell, u32 layer
   version, u8 flag}>`, packed with the block codec behind a u32 size. It lists every cell that differs from the
   generated world.
2. The client requests those cells (**ClientCellRequest**, 0xEC/1).
3. The server answers with **CellsUpdate** (0xEC/5).
4. Later edits are pushed straight away as CellsUpdate. Players beyond the interest range (256 m) only get a
   21-byte **ModifiedCellManifestUpdate** (0xEC/0x0B), and their client re-requests the cell when it needs it.

### CellsUpdate entries

`{u64 cell, u32 version, list<{u32 layer, u32 layer version, u8 flag, u32 raw size, blob}>}`

- **Version 0 means the layers are BLOCK data (voxels).** The client stores them, composites them and re-meshes the
  cell itself. This is what the server sends for every edit, so the server never needs a mesher.
- Any other version means the layers are finished meshes (see "Mesh format"). The client only re-bakes those when
  it requested them, so they are useless for live updates.

### Compositing over the generated world

Layers 0–3 are composited **on top of the client's own generated terrain**:
- an empty voxel keeps the generated terrain;
- an air voxel (material 0) carves;
- a solid voxel adds.

So the server stores sparse *overlays* (`TerrainEdits`), one per edited cell **and layer**, and persists them in
`TerrainEdits/*.cell` (file name = the cell id with the layer in bits 56-57; layer-0 names are the plain cell id).

### Layers (what each one is for)

The client names them (FUN_143bb4ef0) and the captured live manifest uses them the same way:

| Layer | Client name | What goes there | Live manifest |
|---|---|---|---|
| 0 | Cell Poi | Server world stamps: the Spire, outposts | 38,190 cells, levels 2-10 |
| 1 | Cell Permanent | Player building on build sites | 11,162 cells, levels 2-10 |
| 2 | Cell Timed Persistent | Digging outside build sites (heals after ~20 min), ore veins | 125,246 cells, levels 2-4 |
| 3 | Cell Transient | Unused | none |

- **The client keeps a player's own edits in layer 1.** It commits an edit of a level-2 cell into layer
  `level - 1` (FUN_143f5d2c0) and, once the per-cell reply confirms it, writes its own copy of the cell to its disk
  cache as layer 1 and updates its own manifest entry (HandleCellUpdateResponse FUN_143f52b60). Build edits must
  therefore be stored in layer 1 on the server too: stored in layer 0, the client drew both copies after any refetch
  (old shapes flickering in and out).
- **A CellsUpdate must carry every layer.** Layers missing from the packet keep the client's old copy
  (FUN_143f5ce70 → FUN_143f1af90), so the server always sends layers 0-2, empty where it has nothing.
- **Requests carry the client's held versions** (4 × u32 per cell, FUN_143f56980): when they match, the answer is
  UpToDateCells; a layer the client holds that the server doesn't is sent back empty.
- **Layer version = CRC32** (FUN_142ac1800) of the layer compressed by CompressBlockData (FUN_143f85d50, level 6);
  the server uses its own counters, so a builder re-downloads their own cells once per login.
- **Busy client.** A cell with an unconfirmed edit that receives server data, or whose per-cell reply says
  "not ok", is flagged; while any cell is flagged the build tool drops new edits (ClientConstructionManager
  FUN_1430b4e90 → FUN_143f532e0) until the server's data for it arrives. So nothing is pushed to the builder for
  their own edits, and every edit cell gets a reply.

### Coarse levels (`TerrainLod.cs`)

The client draws distant terrain from coarser cells (level L+1 = twice the edge). Without server copies there it
generates the original ground: walls appeared only up close and dug terrain floated (2026-10-13). Copies are made per
layer (layers 0/1 to level 6, layer 2 to level 4) with VoxelFarm's octree rule (VoxelFarm::updateBlockLOD,
FUN_142a15480, compiled into the client but not called): a parent voxel takes the solid/air state and material of the
child at its corner, and its vertex from the surface inside its 2×2×2 children (VoxelFarm solves a QEF; the mass point
of the children's vertices is used). WorldServer.json `TerrainLodCopies` (default on).

**Memory.**
- An expanded cell is 384 KB: flags 64,000 B, u16 materials 128,000 B, 3-byte offsets 192,000 B.
- So each cell is kept in its compact zlib form (a few KB) and expanded only when touched. Cells idle for 30 s are
  packed again, and at most 64 stay expanded.
- With the 23 Outposts stamped (about 1,950 cells) that is about 32 MB, instead of about 730 MB.
- Dev `terrainmem` reports the counts; dev `gc` runs a full collection and reports the heap.

## Block format 2 (`BlockData.cs`)

The layer blob is the client block codec (`A1 B2 C3 D4`, big-endian size, zlib) around this structure:

| Field | Meaning |
|---|---|
| `u32 2` | Format version. |
| 8000 bytes | Occupancy bitmask of the 64,000 voxels, **most significant bit first**, voxel index order. |
| `u32 n` + n × u8 | Flags for each occupied voxel. Bit 0 = has a material, bit 1 = has a vertex offset, 0x20 = offset is the centre (0.5, not stored), 0x40 = material 0 (air), 0x80 = palette index 1 (not stored). |
| `bool` | Palette in use (fewer than 256 materials). |
| palette | `u32 n` + n × u16 materials, then `u32 n` + n × u8 **1-based** indices. Without a palette: `u32 n` + n × u16 materials. |
| `u32 n` + bytes | Vertex offsets. **n counts bytes**, 3 per offset voxel. |

- **Materials** are `VoxelMaterials.txt` ARRAY_INDEX values (column 1), not the ID column. For example 79 = Sand.
- **Offset byte** = `floor((clamp(u, −1, 2) + 1) · 85 + 0.5)`, where `u` is the surface vertex position in voxel
  units from the voxel's corner (0.5 → 128). The byte order is x, y, z.
- **Vertex-only voxels** (offset flag, no material) carry the surface position on the generated side of an edit.
  Clients send them with every smooth edit (line slopes, spheres, fills) and keep them in their own cache (one
  player's cache held 67,662 in 45 cells), so the server stores them as sent (`KeepVertexOnlyVoxels`, default on).
  Dropping them made lines saw-toothed and fills stair-stepped once the cell came back from the server. The server
  never generates such voxels itself: a 2026-10-09 test mode that did was blamed for a client freeze (the same day's
  browser reload was the likelier cause, but server-made ones stay off).

### Building diffs

`ApplyDiffRequest` (VoxelConstruction 0xF4/1) carries, per touched cell:
- an 8000-byte **changed** mask (same bit order);
- the client's whole edited cell in block format 2.

The server applies only the changed voxels, checks claims and material costs, saves, and broadcasts the result.

## Mesh format (for reference)

Requested geometry cells (version ≠ 0) carry, per layer, a zlib blob with:
- two optional meshes: `u8 present`, then CellMesh;
- a bounds block;
- an AABB in **cell units**.

A CellMesh holds:
- positions: `u32 n` + n × 3 u16, where cell coordinate = (v − 32767.5) / 26214 (0..1, up to 1.25 for seams);
- normals: `u32 n` + n × 3 u8, where n = (v − 127) / 127;
- then four u32 arrays:
  - blend per vertex: secondary material << 8 | weight, 0xff00 = none;
  - material per vertex: secondary << 16 | primary;
  - flags per vertex: 0; a triangle whose three corners are all 0xff is dropped;
  - the triangle index list.

The server only uses this for the `testcell` developer command.

## Ground height (`TerrainHeights.cs`)

- The client samples ground height with `ContinentMapHeightmapTerrain` (vtable +0x58, `FUN_143fc3e00`). It takes
  global decimetres, a scale of 40 and LOD 2.
- `tools/terrain` drove that function inside a running client (read-only, through Frida) to bake a 4000 × 4000 grid
  every 2 m. The result is `gameData/TerrainHeights406.bin`, about 27 MB.
- The server uses it to:
  - put trees, plants and the Spire on the ground;
  - snap teleports;
  - keep dug holes from adding air above the generated surface;
  - know how deep a swing is.
- Ranges for continent 406: heights 736–1727 m, sea level about 1000 m.

## Materials under the pick (`TerrainMaterials.cs`)

- **Biome bitmap:** `CommonData/Voxel/materials/textures/Continent_BiomeIds_406.bmp`, 1250 × 1250. The blue
  channel is the `VoxelBiomes` id: 54 ocean, 50 old-growth forest, 47 volcanic. The zone origin is at the image
  centre, at 6.4 m per pixel, with z running down the image.
- **Biome → material:**
  - `VoxelBiomeMaterials` gives the biome's procedural material names.
  - Keywords map those to items: Dirt 44, Sand 45, Stone 46, Ice 76.
  - Deeper than 4 m below the swinger always gives Stone.
  - The hole's shell uses the biome's dirt/rock/sand material (297/289/287), only from 0.6 m below the ground.

## Ore veins (`OreVeins.cs`)

- **Cell map:** `CommonData/Voxel/veinData/ContinentVeinData406.vdt`. From offset 460 it holds sections of
  `{u32 groups, groups × (u32 layer, u32 VoxelBiomes id, u32 1, u32 n, n × u64 sorted level-2 cell ids)}`. Layer 0
  lists the surface biomes' cells (406: VolcanicSurface 47, OldGrowthForestSurface 50, OceanSurface 54, about
  736-1712 m local height), layer 1 the cavern biomes' cells (AncientForestCavern 41, DeciduousCavern 62, about
  -992-624 m). It classifies cells; it does not place veins.
- **Ores of this version:** `ResourceVeinGroups` 11 "Landmark v2 Surface" and 12 "Landmark v2 Caves", their veins in
  `ResourceVeinGroupVeins`, each `ResourceVeins` row ("v2: metal: copper: common") with DENSITY_ON_SURFACE /
  DENSITY_IN_CAVES. Surface: copper 6, tin 5, iron 5, marble 4, amaranthine 3, agate 2, sapphire 1. Caves: silver 30,
  iron 30, alabaster 25, gold 20, ruby 20, emerald 20, obsidian 15, diamond 15. RESPAWN_TIME_DIRTY is 1200 s.
- **Vein placement:** the client generates the vein shapes itself (not decoded yet). Until then a stable hash of
  the cell id makes 3 % of surface cells and 15 % of cavern cells hold one vein, its ore drawn by those weights.
  Mining (35 % of swings in a vein cell), the prospectors and the vein overlay all use the same cells.

## Grass (flora)

- The client scatters grass per mesh triangle (`FUN_143615110`). Its random seed is a hash of the triangle's
  centre position and the flora id (`x·104959 + y·1301081 + z·15486703 + id·0x95341`).
- Grass therefore only re-rolls where triangles change. The server sends nothing for it.
- Open question: whether a dig re-triangulates the whole cell on the client side.

## Trees and plants

- Trees are **server entities** (AddLightweightProp with a `Biome_*_Trees_*` model), not terrain. The client looks
  a tree up by entity guid when it is chopped. See gameplay.md.
- Collision comes from the model's `.adr` (`<CollisionData fileName="….cdt"/>`). Two tree models have none:
  344 GnarledPine_Large01 and 350 Birch_Medium01. They are not used, because players walked through them.

## Caves (`CavePieces.cs`, `Caves.cs`)

The client builds caves itself from pre-made pieces; the server reads the same files to know where cave air is.

- **Pieces:** `CommonData/Voxel/caves/*.cav` (client `VoxelFarm::CaveTileAsset`, load FUN_1444343d0 → parser
  FUN_144433800). Little-endian: `u32 version 1, f64 voxel size (4 dm at LOD 0), f64[8] box (min x,y,z,w, max
  x,y,z,w; dm from the piece origin), u32 nLod`, then per LOD `{u32 version 3, i32 min[4], i32 max[4] (inclusive voxel
  index box), u32 n + n bytes type RLE, u32 nx·ny·nz, u32 n + n × u32 column offsets (n = nx·nz), u32 n + n bytes
  offset RLE, u32 3·nx·ny·nz, u32 n + n × u32 offset column offsets}`, then `u32 nGap` × room-gap records.
  - Column (x, z) = `(x − minx) + nx·(z − minz)`, running up y from miny.
  - Type runs `(u8 count, u8 type)`: 0 carves to air; 1..N repaint an already solid voxel with the tile's cave
    material (they never add rock). Offset runs `(u8 count, u8 ox, oy, oz)` as in block format 2.
  - Cave LOD 0 = cell level 2 (0.4 m voxels).
- **Tile ids:** `VoxelCaveTileDefinitions` ID → .cav file (104 = CavernMiddle01).
- **Placement:** `ContinentCavePlacements[N]_Underground.cpa` / `_Surface.cpa`: `u32 version (14), u32 n`, then n ×
  39-byte records `{u32 tile, f64 x, f64 y, f64 z (global dm: the piece's origin, its x/z centre), u32 rotation 0-3,
  u8, u32 property group, u8, u8}` (FUN_143edfd60); a lot more data follows (classifications) that the server skips.
  Rotation turns x/z only: source index of world-relative (jx, jz) is rot 0 (jx, jz), 1 (−jz, jx), 2 (−jx, −jz),
  3 (jz, −jx), with j = round(Δ / 4 dm).
- **Combining:** the cave layer runs after the terrain generator: a voxel is air when any covering piece has type 0
  there. No noise or blending; DRAW_OUTER_SHELL is 0 for every tile. Continent 406: 5,817 placed pieces, 34 shapes,
  about 35 MB of LOD 0 data.
- **Server use:** the dig smoothing shell never adds rock where `CavePieces.IsAir` is true; Travel → "An Underground
  Adventure" re-zones to the home continent and lands on the floor of a random cavern (piece 104: air from about
  −76 m to +75 m around the origin; arrival scans down from origin + 10 m).
- Logging in at a cave takes the client about 30 s; the server only has to keep the cell traffic flowing.

## Long teleports (re-zoning)

A plain TeleportToLocation over a long distance makes the client re-centre its terrain engine inside a normal frame.
Into a deep cave that hangs the renderer (Scene Draw → QuerySimpleVisibility, `FUN_144009a40`) until the client's
3-minute watchdog kills it (`0xBAADC0DE`). Its "loading screen" byte (+0x41) does not help: state 0x13 checks
world-ready at the character's stale stored position.

So teleports over 300 m go through the client's own zone change (`Game/Teleport.cs`, `Rezone`):

1. **ClientBeginZoning** (0x23) with the current zone name and zone id 406. The client shows the loading screen,
   builds a fresh terrain engine and goes to WaitForZoneLoad (state 0x14). It also **drops every entity except the
   player** (`FUN_142ee4930`).
2. **SendZoneDetails** right after it. The new engine stays idle (focus 0,0,0, its +0x78 flag set) until it gets the
   continent settings ("VoxelFarmCore::LoadSettings").
3. The client sends **ClientIsReady** again. The server answers with ClientUpdateDoneSendingPreloadCharacters,
   ZoneDoneSendingInitialData and AreaDefinitionSetDefinitions (the flags WaitForConfirmationPacket,
   `FUN_142cc5120`, waits on), the **cell manifest** (TerrainCell 0x0A sets the engine's "manifest received" flag,
   core +0x5a75, via `FUN_143ed1000`; without it the engine never streams), and the entities again
   (`PlayerManager.Reintroduce`).
4. The loading screen drops once the engine's focus reaches the player (`FUN_142a4e050`), about 10 s into a cave.

Since 2026-10-10 every move re-zones (`LoadingScreenDistance` 0), short ones included: plain TeleportToLocation hung
the renderer in QuerySimpleVisibility even for a few metres, and also for a zero-length move (2026-10-11). The re-zone
is over when its ClientIsReady arrives (`Teleport.FinishRezone` clears `RezoneTarget`). Two rules found with the site
games (2026-10-11):

- **One re-zone at a time.** A second ClientBeginZoning while one is loading left the client busy for good; `Rezone`
  retargets the pending one instead (up to 60 s).
- **Nothing else for that player in the same burst.** Hit points, instanced game state / HUD / scoreboard, or a site
  update sent with ClientBeginZoning hung the client in QuerySimpleVisibility (`FUN_144009a40` loops over its
  visibility list: an object linked in twice). Plain re-zones never hung (0 in 8); with those packets 1 in 4 respawns
  and 2 in 3 round starts did. The site games hold them back until the re-zone has finished, plus 1 s.
- **No re-zone during a prop placement.** Re-zoning while the player still had a placement open crashed the client in
  the ProxiedCharacter destructor (FUN_142dee1b0, deleting entity +0x4998). `Rezone` first cancels the placement
  (`Props.CancelPendingPlacement`: PropBase sub 2 refusal with error 0 resets the client silently, the prop is
  removed) and sends the zoning a second later (`Teleport.Update`).
- **No boss-flagged entities across a re-zone.** Combat EnableBossDisplay lists an entity the client's entity removal
  only unlists for flag 0x8, not the boss flag 0x2: a boss in view at a re-zone crashed the client. The server switches
  the display off before a boss leaves a player's view and before the player re-zones (`Creatures.ClearBossDisplays`,
  zoning deferred 1 s).


## Voxel templates (`.otd`, `Game/Terrain/VoxelTemplate.cs`)

The client ships prefab structures in `CommonData/Voxel/templates/*.otd` (the Spire base `base_portal_spire.otd`,
castles, floating islands, building platforms…). Reader `FUN_143470510`, version 6, little-endian:

| Field | Meaning |
|---|---|
| `u8` | Version (6). |
| `u32 n` × cell | `u64` cell id (level 2; x/y/z relative to the template's corner), `u64` the same id, `u32` type (4), `u32` length, blob; types 2/3 add a `u32`. The blob is the block codec around an **8000-byte changed mask + SerializeBlockData format 2** — exactly an ApplyDiff cell. |
| `u32 ×3` | Size in voxels (the Spire base: 310 × 170 × 310 = 124 × 68 × 124 m). |
| `u32 n` × `{u16 material, u32}` | Material list (empty in the Spire templates). |
| varint `n` × prop | `u64` id, `u32` prop definition, `u32` model, `f32 ×3` position in metres from the corner, `f32 ×4` rotation, `f32 ×3` scale, `u64`, `u64`, `u32`, varint `n` × 16 bytes, varint `n` × 20 bytes `{u32, u32, u64 linked prop, u32}`. Varint (`FUN_14346f8f0`): bit 0 sign, bits 1–2 extra byte count, value >> 3. |

The Spire (`Game/Spire.cs`) stamps its template once into the home continent (marker `TerrainEdits/spire_base.txt`
holds the corner cell) so that the template's Spire model (596) stands on the ground at the zone origin, and places
the template's seven props where it puts them.

## Protocol check (2026-10-12)

Every terrain packet the server sends was checked inside running clients. A Frida probe
(`re/test/scenarios/adhoc/terrainpkt.js`) hooks each client reader and checks after every packet that the reader's
stream ended exactly at the end with no error flag. The client's dispatchers don't check this themselves: a wrong
layout would be used with zeroed fields, not dropped. Over a regression run every packet decoded exactly:

| Packet | Sub | Reader | Result |
|---|---|---|---|
| CellsUpdate | 0xEC/5 | FUN_14309bbd0 | 191 / 191 exact |
| ModifiedCellManifest | 0xEC/0x0A | FUN_14309c0c0 | 42 / 42 |
| ModifiedCellManifestUpdate | 0xEC/0x0B | FUN_14309c1b0 | 642 / 642 |
| DetectVeins | 0xEC/0x10 | FUN_14309bd90 | 45 / 45 |

VoxelConstruction Response (0xF4/7, read inline: `u8 sub, u32 request, u32 result, bool`) matches
`Building.SendResponse`. Client→server packets are parsed by the server's own models (an unread tail would be logged
as "Failed to read entire contents"; none were).

ClientCellRequest entries are `{u64 cell, u32 × 4 layer versions}` (24 bytes, FUN_1430a5030). The client only asks for
cells it holds no data for (all versions 0, probe `cellreq.js`), so answering every requested edited cell in full is
right. ClientPendingRequestTimeout (0xEC/2 `{u64, u64 cell}`, sent when an answer took over 3 s) is answered like a
request.

## Client mesh cache

- **Where:** the client keeps finished meshes on disk (`Cache/0`, `Cache/2`; repository 0 "VoxMesh").
- **Which client:** only one client may use it. A second client from the same folder logs "Another client is using
  the cache! Caching disabled!".
- **Store:** a mesh is stored with the version of its cell's highest non-empty layer (FUN_143ed9060 → FUN_143f0d9a0).
- **Load:** the stored version is checked against the version the cell has now (FUN_143f0e910 / FUN_143f0eb30). A
  difference logs "Failed to load mesh for cell … due to version mismatch:(stored:expected)" in VoxelDataErrors.log,
  deletes the entry and re-meshes.

Generated cells all expect 492747177. Ore veins used to be generated only once a player arrived in a region, and
then announced cell by cell. So every session the client:
1. loaded those cells from its cache, still expecting the plain-terrain version, and found them stale (they had been
   stored with the vein layer);
2. re-meshed them without the ore;
3. got the update and fetched them;
4. re-meshed and stored them again.

That was a few hundred mismatches per session, on the code path of a rare client crash (dump 9836: a terrain worker
posting a cache job). The server now generates the vein regions around a player before building their manifest
(`OreVeins.EnsureAround`), so those cells are in the manifest. `Teleport.RezoneNow` also sets the destination before
sending the manifest: before, a cross-landscape re-zone first got the old landscape's manifest.

Measured with the test clients (VoxelDataErrors.log, one client session each):

| Session | Mismatches |
|---|---|
| before the change | 19–58, most at zone-in |
| after the change, server just restarted | 0 at zone-in; 8 later, at cells the previous server run had edited |
| client restarted after scenarios that dig | 5, all cells the regen scenario had changed |
| client restarted, no terrain edits since | 0 |

Vein ManifestUpdates per session dropped from ~320 to 0.

## Slow loading screen: allocator slots

After WaitForWorldReady the loading screen waits for the VoxelFarm scene (ClientSceneProcessing.log "Begin SCENE …
Process Pending: N" → "Finish SCENE: Total Process Time"), meshed by five `ContourThread::contourLoop` threads
(FUN_143ed5a30).

- The client's allocator gives each thread one of **64** per-thread cache slots (FUN_142b6b7f0; table at
  0x145347ed0, 0x258 bytes per slot, owner thread id first; count at 0x145347858).
- A thread that finds the table full walks all 64 slots under a global critical section on **every allocation**,
  checking each owner with FUN_142b6e490 (OpenThread, GetExitCodeThread, GetProcessIdOfThread, CloseHandle).
- The game runs ~176 threads. About 55 slots go during start-up; at world entry the contour threads race the
  embedded browser (the UI's `InGameMarketplaceBrowser:PreLoad` → `Browser.Create`, ~14 CEF threads) and other new
  game threads for the rest. A contour thread that loses meshes at a crawl: scenes took 150-300 s instead of ~1 s.
- The shipped ClientConfig.ini has `[AssetDelivery] DirectThreadCount=10` (the code's default is 4), all started
  early. The launcher sets 2: 58 slots used, scene 0.8 s (2026-10-14).
- The `[Terrain] UseCachedMeshes` / `CacheMeshes` lines are overridden by UserOptions.ini (which the game rewrites),
  so they cannot switch the mesh cache off from ClientConfig.ini.

**History (Daybreak forums, archived):** the live game had this bug. The thread "[Feedback] Excessively long load
times? Let us know your system specs." (Domino, 23 May 2016) collected reports of loads stuck at 50%, mostly on 6- and
8-core CPUs with hyper-threading (i7-5820K, 3930K, 5960X), fast or slow at random. On 13 June 2016 the developers
reported "it is indeed related to number of cores" and a 100% repro case; client patches on 13/14 June, 16 June
("issues owners of hex core machines were experiencing") and 30 June ("client loading issues for 8-core processors",
as quoted by players) only partly fixed it, and 8-core owners still reported it in November 2016. Players worked around
it by booting Windows with fewer processors (msconfig → Boot → Advanced → Number of processors). Our client
(0.589, built 4 June 2016) predates all of these patches. Players also saw it on later zone changes in the same
session, not only on the first load: slot use grows while playing (58 → 61 in one session here), so re-zones
(teleports, caves) may hit it after a while.
