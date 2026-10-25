# Terrain (VoxelFarm)

Landmark's world is a VoxelFarm voxel terrain. The client already contains the whole generator: it builds the
untouched world itself from data files in its install (`CommonData/Voxel/…`). The server only has to store and send
**what players changed**. This document explains the formats involved and how `Game/Terrain/*` uses them.

## Environment Server and zone

The client calls the server's voxel back end the **Environment Server** (its monitor page lists the job types: cell
LODs per layer, apply diff, undo / redo, ownership, pastes, veins, manifest compression;
research/trace/architecture/terrain-service.md). The code is split the same way; by default both halves run in the world process
(see **Where it runs** below for the `envserver` container):

- **Environment Server** (library `LandmarkEmulator.EnvServer.Core`: `IEnvironmentServer`, implemented in-process by
  `LocalEnvironmentServer` over the stores `TerrainEdits`, `TerrainLod`, `OreVeins`, `VoxelOwnership`,
  `TerrainStore`, `VoxelTemplate`, with the read-only inputs both sides use: `BlockData`, `TerrainHeights`,
  `TerrainMaterials`, `CavePieces`, `VeinGenerator`, `GameDataFiles`; namespaces unchanged): the overlays per
  (landscape, cell, layer) and their versions, CellsUpdate and manifest bodies, build edits as one transaction each
  (`ApplyEdit`: stale check, cost against the player's material budget, write), undo / redo and paste writes
  (`WriteCells`), the Heal tool (`ClearCells`), digs, box reverts, the heal queue (`Regenerate`), template stamps
  (Spire, ruins), ownership grids, ore veins, coarse copies, the creature navigation's solidity grid of a build site
  (`SolidityOf`, `SiteSolidity.cs`, below), and the cost table `BuildMaterials`. It never sends anything to a player: it
  returns the cells that changed.
- **Zone** (world server: `ZoneTerrain`, `Building`, `BuildingClipboard`, `TerrainHandler`, `VeinOverlay`): reads the
  packets, checks claim permissions (`ZoneTerrain.MayApply`), sends the player's materials along as a budget and
  charges what the edit cost, keeps the undo stack, and is the only sender of terrain packets: full cell data within
  256 m or a 21-byte note (`ZoneTerrain.Broadcast` / `Announce`), the zone-in manifest (`ZoneTerrain.SendManifest`),
  Voxel Vision replies with the builders' names, notes for changed coarse copies (the `LodCopiesChanged` event).

The service keeps the page's job counters in the client's own order (`EnvServer/JobCounters.cs`; the page reader
FUN_14320ea00 prints, for job types 0-18, Pending at +0x20 + 4·i and Active at +0x6C + 4·i, then "Total Jobs" as the
two current totals): Pending = received and waiting in the work queue, Active = being worked on. Finished counts are
our own extra (Summary). Until 2026-10-17 the two columns were swapped and the total counted finished jobs.

`EnvironmentServer.Instance` is the zone's handle on it (set at start-up). The interface carries only data — the
destruction filter of a dig (`VoxelFilter`), a site box (`VoxelBox`), the material budget (`MaterialBudget`) — so the
same calls work over the network.

### Where it runs: world vs the `envserver` container

- **In the world (default)**: `LocalEnvironmentServer`, the stores in the world process, files in the world's
  `TerrainEdits/` folder — exactly as before the split.
- **Its own container** (`ENVSERVER_ADDRESS=envserver:20500`, Docker profile `envserver`; `LandmarkEmulator.EnvServer`,
  docs/docker.md "Environment Server"): the world uses `Game/Terrain/RemoteEnvironmentServer.cs` and every call becomes
  one request / answer (LandmarkEmulator.Internal/Environment/EnvironmentMessages.cs). The service runs the same
  `LocalEnvironmentServer` on one work thread (requests and upkeep: heal queue, vein regions, flush, LOD), in its own
  folder `server-data/worlds/<key>/envserver/` (`TerrainEdits/` in the same format, copied once from the world's folder
  on its first start; `stamps.tsv`, `ruins.tsv`, `ruin-layers.tsv`; cell files written to a temporary file and renamed).
  The world keeps no voxels, only (terrain-service.md §5.3, §5.5):
  - the **version index**: every (landscape, cell, layer) version the service holds, from a snapshot when it links and
    `CellVersionsChanged` pushes after every change (sequence numbers; a gap fetches the whole index again). Version
    checks, "up to date" answers and the zone-in **manifest** (built from it, no round trip) come from it;
  - a **cache of finished CellsUpdate bodies** (64 MB, least recently used out first), used while the index still has
    the versions they carry; missing bodies are fetched in batches (one round trip per cell request).
  The build sites and Spire platforms go to the service as boxes (it needs them for the heal, the layer move and the
  vein spawner, which in-process asked `Claims.BoxesIn` / `Spire.Touches`); the Spire / ruin permanent-cell marks are
  sent again on every connect. The placement markers (`spire_base*.txt`, `outposts695d.txt`,
  `landmarks_of_landmark.txt`) stay in the world's `TerrainEdits/` and the world still decides placement.
- **While the service is away** the world keeps running: cell requests are answered from the cache or "up to date"
  (those cells get a manifest note when it is back, so the client fetches them); building, undo, paste and the Heal
  tool are refused with the client's message 562155 "Connection to the Build SiteServer is unavailable"
  (ClaimsPacketLostClaimServerConnection 0xF9/0x12, which the client maps to that text); Voxel Vision shows what the client has; digs change nothing and give
  nothing; heals, veins and LOD pause in the service and catch up; a site deleted meanwhile is refused (with 0x12 too), a lapsed site
  waits for the next hourly check; a revert asked for meanwhile is sent when it is back. A stamp that is due (a new
  Spire, new outposts) fails instead of writing its marker, so it is tried again at the next start.
- Dev commands that use the stores directly (`veintest`, the captured-mesh tests `livecell` and the like) act on the world's own (empty) store
  when the terrain is remote; the ones going through the interface (`regendelay`, `clearcell`, `cellstats`, `lodbuild`,
  `terrainmem`, `ownblock`) reach the service.

### Solidity grids for the creature navigation (`SiteSolidity.cs`, OPEN-LEADS L133)

`IEnvironmentServer.SolidityOf(zone, box)` turns the stored voxels in a global dm box (a build site, or a Chaos Cavern
outpost players dug into) into a coarse grid for `Game/Pathing` (docs/gameplay.md "Creature pathing"):

- the composite the client draws: layers 0, 1, 2 of every stored level-2 cell meeting the box, a later layer's written
  voxel over an earlier one's; per voxel: unwritten (generated ground shows), air (material 0 / InteriorAir), solid;
- blocks of **3 × 1 × 3 voxels** (1.2 m across = one navigation column, 0.4 m tall); a block is *written* when any of
  its voxels is, *solid* when at least half of its written voxels are solid (Sight.AddRuin's rule; the ruins' blocks are
  3 voxels tall, but a player's doorway is unwritten air and a 1.2 m block under the lintel turned a 2.4 m door into
  1.6 m of headroom in the first test);
- only the stored cells are read: an expanded layer in place, a compact or spilled one decompressed (flags and
  materials only) into a scratch buffer outside the store's lock, so nothing stays expanded; the grid covers the stored
  cells' part of the box (a whole 210 × 180 × 210 m site: at most 175 × 450 × 175 blocks, two 1.7 MB bit sets);
- remote: one request VoxelConstruction 0xF4 / 0x10017 (ours; payload the box) answered with the grid, its bit sets
  deflated; counted as job type 14 "Construction Area Iterator" on the monitor page.

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

0.695 readers: packet FUN_1431464f0 (u16, u32 sub, the cell list, list<u64 transaction> FUN_143148b80, bool +0x48,
bool +0x49), cell list FUN_143148950 of `VoxelFarm::CellIdVersionFlagsAndData` (FUN_143146320), layer FUN_143146cf0;
HandleCellsUpdate (FUN_144016a90) branches on the version. An empty layer is 9 bytes.

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

The client names them (FUN_143bb4ef0 — the Environment Server's job-type namer: types 0-3 are the LOD jobs of the four layers) and the captured live manifest uses them the same way:

| Layer | Client name | What goes there | Live manifest |
|---|---|---|---|
| 0 | Cell Poi | Server world stamps: the Spire, outposts | 38,190 cells, levels 2-10 |
| 1 | Cell Permanent | Player building on build sites | 11,162 cells, levels 2-10 |
| 2 | Cell Timed Persistent | Digging outside build sites (heals after ~20 min), ore veins | 125,246 cells, levels 2-4 |
| 3 | Cell Transient | Unused | none |

**Heals (layer 2).** Every destruction (a dig, a mining swing, an explosion) adds one pending heal to the heal queue
(`EnvServer.Core/Terrain/HealQueue.cs`): its sphere, the cells it touched, and a due time (now + 20 minutes, dev
command `regendelay` for tests). When it is due, the layer-2 voxels inside that sphere (plus the 1.5-voxel rim where
the hole's wall vertices are) go back to the generated ground — except voxels a later, still pending destruction also
covers (its carved voxels; a vertex where a corner of its cube lies in that sphere), and the holes still open next to it
get their surface redone against the holes that are left (`TerrainEdits.RefreshOpenHoles`), so their walls close cleanly
where the ground came back. Entries made since
2026-10-16 heal inside build sites too (older log entries keep their old rule: site voxels kept): destruction on a site
in Play Mode (dynamite, 100 s; a Linking Area's "Destroy Voxels", 10000 s) carves the owner's build only in this layer,
so it comes back (`Game/VoxelDestruction.cs`; the log line gained two fields, the heal delay and the site flag). A new dig never postpones an older one's heal (a developer, 27 Jul 2016: each destruction is a volume
of negative space with its own pending heal and due time). The queue is kept in `TerrainEdits/heal-queue.log`
(append-only, compacted at start), so heals happen on time across server restarts. Layer-2 cells from before the queue
heal whole at their file time + 20 minutes.

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
  Dropping them made lines saw-toothed and fills stair-stepped once the cell came back from the server. Since
  2026-10-18 the server writes its own on the walls of dug holes (dig layer only; "How the client draws a dig" below).
  (A 2026-10-09 test mode that wrote some was blamed for a client freeze; the same day's embedded-browser reloads were
  the cause found for all three freezes, and client-made vertex-only voxels have gone back to clients from the server
  since 2026-10-14 without trouble.)

### How the client draws a dig (`TerrainEdits.Sphere` / `SurfaceVertices`)

Traced 2026-10-18 after "pickaxe digging looks too blocky" (0.695 addresses):

- **Samples sit at voxel corners.** The generator samples the ground at cell corner + index × 0.4 m (including a 2-voxel
  border: `ContinentMapHeightmapTerrain::getContourDataRegular` FUN_1440873e0 — x of padded index i = cell x − 2 voxels
  + i voxels; a column is solid from the bottom up to the height, and it keeps the density height − y per voxel).
- **Every cube with a sign change gets the mass point of its crossings.** `ContinentMapHeightmapTerrain::SmoothContourData`
  FUN_144086430: for the cube [i, i+1]³ (stored at voxel i), the 12 edges (corner table DAT_144a60ac0, edge pairs
  DAT_144a60a50), on each edge whose densities differ in sign the linear crossing d₀ / (d₀ − d₁), averaged, clamped to
  −1..2, offset byte = (u + 1) × 85, flag 2. That is the shipped ground's smooth encoding.
- **The mesher joins those vertices.** `ContourEx` FUN_142ac4590 (VoxelFarm contour.cpp): for every voxel and axis where
  the two voxels differ (material ≠ 0 and ≠ 0xFFFC = solid), one quad from the vertices of the four cubes around that
  edge (table DAT_144a608f0: (0,0,0), (0,0,−1), (0,−1,−1), (0,−1,0) across the other two axes); a vertex is cube + offset
  when the voxel has flag 2, else **cube + 0.5 — the centre, i.e. a 0.4 m stair step**. Position in the cell
  = (index + offset − 2) × 0.025 (constants DAT_144c411a8 2.0, DAT_144c41110 0.025, DAT_144c41158 0.5, DAT_144c401b8 85).
- **Block layers replace per bit.** Layers merge with flags OR'd, the material where bit 0 is set and the offset where
  bit 1 is set (FUN_1440498d0); the composite overrides the generated grid the same way (getBlockContourData
  FUN_142aa8830). A vertex-only voxel therefore keeps the generated material and moves only the vertex. Where block
  voxels meet generated ones inside a cube and the generated cube has a vertex, the client blends the two by a QEF of
  the nearby triangles' planes (FUN_142aa6030 → FUN_142aa6930 / FUN_142aa6290 → FUN_142aa2900 … FUN_142aa5700).

**Why pick holes were stepped (before 2026-10-18).** The server tested the voxel's centre against the sphere (half a
voxel off the client's samples), so the vertices it set were not the ones the client's quads used; it put vertices only
within 1.2 voxels of the sphere by the centre, and on the ground side only as solid "shell" voxels of a guessed
material from 0.6 m under the baked ground down (so as not to add rock above the real surface). Every cube of the top
0.6 m of the wall and the rim whose corner voxel was ground kept no vertex and was drawn at its centre; every carved
voxel also carried an explicit centre vertex. Measured with a copy of the client's pipeline (tests
`Rules/DigShapeTests.cs`, a 1 m pick swing on flat ground): 17 cubes at their centre, vertices up to 1.35 voxels
(0.54 m) off the ideal hole, 35 voxels of guessed rock written.

**What a dig writes now** (dev `digmode 8`, the default; `digmode 5` = the old shell, `0` = no vertices):
1. Air (material 0, no vertex) in every voxel whose corner is in the sphere (open air more than 2 m above the baked
   ground is left out, as before; ore under carved voxels is still counted).
2. For every cube with a corner in the sphere and both solid and air corners: the vertex at its corner voxel — on the
   ground side a vertex-only voxel. The density is the ground minus every open hole: min(ground height − y, distance
   to each pending dug sphere − its radius) (the baked height grid; cave air and block-layer states as the client has
   them; a corner the estimate can't explain puts its crossing mid-edge). The vertex starts at the mass point of the
   linear crossings (the generator's rule) and is moved onto the surfaces' tangent planes at the crossings (a QEF by
   averaged projections, as VoxelFarm places vertices where block data meets the ground), or projected onto the
   nearest surface when the planes don't meet on it; on a 2.5-voxel pick sphere the plain mass point sits a third of a
   voxel inside the curve.
3. Cubes the sphere cleared completely drop a vertex an earlier swing left; nothing is written where the build layer
   has voxels or a site's rules refuse destruction.

Same swing, same test: no cube at its centre, every vertex within 0.04 voxel (1.6 cm) of the ideal hole, no rock
added, and the cell update is a little smaller (496 vs 565 bytes for the touched cells). Overlapping held-button swings
make one shaft (max 0.05 voxel), and the walls show the ground's own strata instead of a guessed wall material.
Not proven yet: the exact vertices live's server wrote for a swing (no live dig cell has been captured; open lead).
Live sent VoxelFarm block data through this same client pipeline, and the client's own smooth brushes (spheres,
lines, fills) send air plus vertex-only voxels the same way.

### Building diffs

`ApplyDiffRequest` (VoxelConstruction 0xF4/1) carries, per touched cell:
- an 8000-byte **changed** mask (same bit order);
- the client's whole edited cell in block format 2.

The server applies only the changed voxels, checks claims and material costs, saves, and broadcasts the result.

**Where brush edits land (checked 2026-10-21).** Every brush (Add, Remove, Fill, Line, Smooth, Paint, Mesh Shapes)
sends the cell it edited; the server stores each changed voxel at the same cell and index, with the client's flags,
material and vertex, and sends exactly that back to everyone else (the builder is sent nothing: their client drew and
kept its own edit). Nothing on the server moves a brush's voxels, so an offset between a brush's preview and its
result cannot come from the server's copy (Tests/Rules/BuildPlacementTests: the blob as the client packs it, the
server's readers, Prepare / Commit, the CellsUpdate it sends). The dig rules (corner samples, `SurfaceVertices`) are
used only by digs (`TerrainEdits.Sphere` has no build caller).

### Pastes and designs (`Building.TurnedVoxels`, `OffGridVoxels`)

The paste is the one placement the server makes itself (the client sends only where and how turned). A voxel's two
parts live in different places: the material belongs to the grid point (the client samples at voxel corners), the
vertex to the cube [s, s+1]³ stored at s. A turn that flips an axis maps that cube onto the cube one voxel lower on
that axis, so the vertex goes to M·s − 1 there, not to M·s with the material. The cubes of the copy's surface whose
corner voxel is air (every face towards −x, −y, −z, and after a flip the faces turned that way) keep their vertices
as vertex-only voxels (target material kept; left out where the player may not build). Before 2026-10-21 the vertex
stayed with the material and air-corner vertices were dropped: turned pastes / designs had their surface up to 0.9
voxel off along each flipped axis, and smooth copies lost up to 0.7 voxel on their −x / −y / −z faces even unturned
(BuildPlacementTests, a sloped wedge meshed with the client's rule). The off-grid paste samples the nearest grid
point to (g − origin)·R⁻¹ (it took the voxel the target's centre fell in: one voxel off along flipped axes).

**Interior empty space (material 0xFFFC) is never stored** (`Building.PastedMaterial`). A copy marks the air its
build encloses with material 0xFFFC (FUN_143f9a150 → FUN_143f9c330; trace/0695/ruin-stamping.md §1), and the game's
designs are mostly that (Castle Long Room: 301,860 solid, 480,372 interior, 668,112 surrounding-air voxels; Castle
Courtyard 32,165 / 2,591 / 59,169). The client's paste (CClipboardData FUN_142a7eeb0) writes it as air (material 0) in
the Paste Air (2) and Paste Interior Air (3) modes, never on the box's first slice, and not at all in the default
mode; the server now does the same on both paste paths. Until 2026-10-21 the server wrote it raw into layer 1: the
mesher (ContourEx) draws 0xFFFC as air, so the design looked right, but the occluder builder of the client's cell
meshes counts it as solid. Per cell and axis, FUN_142ae77d0 takes the first and last voxel of every column that has
the material bit, a material ≠ 0 and an opaque VoxelMaterialLibrary entry (FUN_144002a40, no range check: entry 0xFFFC
is the copy of entry 0 that FUN_143ff41f0 writes at +0x37ff20, TRANSPARENT_TYPE_ID 0 → +8 = 0, FUN_143ffa310);
FUN_142ae7b40 finds the largest flat rectangle; FUN_142ae8610 keeps it when 100 points on it are "solid" by the same
test; FUN_143fdf790 stores the quads (CellGeometry +0x108, the mesh blob's "bounds block", FUN_143145780) and
VoxelVisibility (FUN_1437879d0) turns each into an occluder of the Deep software occlusion system. So every room and
doorway of a placed design became an occlusion quad: from inside, every terrain cell around it was culled while its
flora (not occlusion-tested) still drew — "nothing outside is visible" (user report 2026-10-21). At every start
`TerrainEdits.RepairInteriorAir` turns stored layer-1 0xFFFC voxels into air (the look they had) at a new cell
version, so clients refetch the cells and the coarse copies are rebuilt; `TerrainEdits.Write` and the coarse copies
(`TerrainLod.Downsample`) never store it either. Surrounding air (modes 2 and 4) and Remove voxels are still not
applied by the server's paste (TASKS 6.57). Tests: `Rules/DesignInteriorAirTests`.

**Paste origin vs preview (L199, 2026-10-24/25).** Frida on `FUN_1436cd920` (`tools/paste`): identity packets sit
one voxel above the area/preview corner on every axis (server origin − `areaVoxels` = `(1,1,1)`). Cursor size at
+0x240 is design size + 1; the three `dec`s make the request size. On a turned paste the client already folds the
min-corner shift into the packet on flipped axes and skips that +1 there (yaw90: packet − area =
`(−sizeZ, +1, +1)`); a blanket −1 then put the flipped axis one low. `Building.Paste` applies
`PasteOriginAdjust = -1` only on axes with `shift[i] == 0`.

### Tool switch and the edit effect others see (`Building.Tools.cs`)

Every build-tool activation sends **ToolBase 0x117 sub 1 ToolPacketActivate** (C→S, no reply; 0.695 writer
FUN_143452ac0 → FUN_143452480 + FUN_143452550, 0.589 FUN_1433a5960, same):

| Field | Meaning |
|---|---|
| `u16 0x117`, `u8 1` | opcode and sub |
| `u8 byItem` | 1 when an inventory tool (pick, axe …) was activated (ClientInventory.ActivateItem), 0 for a build tool |
| `u32 itemId` | the item definition id for the by-item form, else 0 |
| `u32 toolTypeId` | `ToolTypes.txt` ID for a build tool (10 StampBrush = Add, 17 FillStampBrush = Fill Brush, 6 SubtractiveBrush = Remove, 5 HealingBrush, 3 Smoothing, 11 PaintBrush, 9 PasteVoxel, 2 Selection, 4 TriggerVolume = Area, 8 Props, 13 FillBrush = Line …), else 0 |

Leaving build mode sends nothing. The server keeps the last tool per player and uses it for the **ResponseObserved**
(VoxelConstruction sub 0xA) it sends to players within 100 m of someone else's edit: operation 0 placement / 3 delete /
4 heal / 5 smooth / 6 paint, and the trailing bool = Stamp placement effect (true: Add tool, Mesh Shapes, paste) or the
Fill Brush's no-destroy one (false). The client (FUN_143166bd0) turns these into the WorldEditingEffect of brush 4 /
11 / 2 / 1 / 0 / 5. Without a known tool the operation comes from what the edit did (only removals → delete, only
repaints → paint, only additions → placement, else smooth).

**Mesh Shapes (0.695 build palette tab):** the 30 `VoxelShapes` rows 179-208 (`Theme_Meshshapes_*.dme`, no
SERVER_FILE_NAME, RESOURCE_COST 0) and the `MeshCategory*` tables are client-only. Picking one activates the StampBrush
(the packet above) and keeps the shape id in the client; the edit is the ordinary ApplyDiffRequest with the voxels the
client made from the mesh, so permissions, costs and broadcasts are the brush path's. The Fill tool's "Smoothing
Enabled" option is a client flag (Tool.ToggleCursorSmoothing). Trace: research/trace/0695/mesh-shapes-695.md.

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
  - A hole's walls keep the client's own generated material (vertex-only voxels, "How the client draws a dig"). The old
    solid shell (dev `digmode 5`) used the biome's dirt/rock/sand material (297/289/287), only from 0.6 m below the ground.

## Ore veins (`OreVeins.cs`)

- **Which file:** `CommonData/Voxel/veinData/ContinentVeinData<continent>.vdt`, by the landscape's continent
  (StaticZoneData CONTINENT_ID), not its zone id [P]. The client's loader FUN_143ff34d0 formats
  `Voxel\veinData\ContinentVeinData%d.vdt` with the VoxelFarm core's +0x5208, the same id ContinentOceanMap
  (FUN_143ff3730) and ContinentSurfaceCache (FUN_143ff3fc0) use; FUN_143f9aa30 / FUN_143f9aad0 / FUN_143f94580 set
  it from the zone's continent definition. So the classic islands read their own id and the Pioneer lands 464-468
  read 440-444 (`LandscapeContinents`, `OreVeins.VdtPath`). The shipped files agree: 388.vdt exists with no zone 388
  (zone 387 is continent 388), and 440-444.vdt exist while zones 440-444 are continent 430. The cave placements are
  named the same way (`CavePieces.PlacementPath`, loader FUN_143ff1310). The event islands' continents 436-438 ship
  neither.
- **Cell map:** `ContinentVeinData406.vdt`. From offset 460 it holds sections of
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

## Surface cache (`.csc`)

`CommonData/Voxel/surfaceCache/ContinentSurfaceCache<continent>.csc` is the client's precomputed map of where the
ground is (reader `FUN_14407b070` → `FUN_14407b810` in 0.695). The layout is from the reader; the meaning was fitted
against the server's baked heights of 28 landscapes (`tools/terrain/csc.py`).

- **Header:** `u8 version 1, u32 x, u32 y, u32 z, u32 w, u32 h`.
- **Body:** 9 LOD grids of `w × h` u16, rows along z. Each next LOD halves x, y and z, with
  `w' = (w + 1 + (x & 1)) / 2` (h likewise with z).
- **Cells:** texel (row r, column c) of LOD k is the column at global cell (x + c, z + r). Cells are 16·2^k m, so
  global metres = cell × 16 at LOD 0. The islands have x 159650, y 170, z 159528 and 501 × 501 texels: local
  x = 16c − 4000, z = 16r − 4002, and the y origin is local 720 m.
- **Value:** `low | count << 10`. The ground of the column lies in cells `y + low` … `y + low + count`. Bits 8-9 are
  always 0.
- **Not a height:** this rule gives `low` back from the 2 m height bake for 95.5-98 % of texels: floor((the lowest
  baked height over the texel's square, widened by one sample) − (y·16 − 2000) − 1) / 16). Taking the middle of the
  range as the height is off by 1.6 m (median) and up to 40 m. The server does not use it for heights.
- **What it is used for:** checking that a landscape the client generates from bitmap names the server sends is the
  one live shipped (`csc.py pick`, below).

## Event islands (zones 460-462)

The 0.695 client ships ocean maps and surface caches for continents 436 / 437 / 438. These are zones 460 Halloween
Party, 461 Winter Holiday Party and 462 Isle of Judgement in StaticZoneData. The islands have no bitmaps of their own.

Their surface caches match the developers' template island: 436 matches 427 / 429 and 437 matches 438. So
SendZoneDetails names these bitmaps (`Game/Landscapes.cs`):

| Zone | Continent | Shape bitmap | HeightLayerIds bitmap | BiomeIds bitmap |
|---|---|---|---|---|
| 460 | 436 | `Continent_Shape_424.bmp` | `Continent_HeightLayerIds_424.bmp` | `Continent_BiomeIds_427.bmp` (deciduous) |
| 461 | 437 | `Continent_Shape_424.bmp` | `Continent_HeightLayerIds_424.bmp` | `Continent_BiomeIds_437.bmp` (tundra) |
| 462 | 438 | `Continent_Shape_424.bmp` | `Continent_HeightLayerIds_424.bmp` | `Continent_BiomeIds_437.bmp` (tundra) |

The continent id (436-438) is sent as well. It picks the client's ocean map, surface cache and world settings.

What the server keeps for the islands:

- **Ground heights:** baked like every landscape, from a 0.695 client on the island (`tools/terrain/bake_all.py`,
  dev command `landscape 460`). The file is named by zone id: `TerrainHeights460.bin`.
- **Checking the bake:** `csc.py pick 460 436 427 424 437` checks the bake against the shipped cache. A good bake gets
  96-98 % against 436 and sides with 436 where the candidate caches differ.
- **Materials:** `TerrainMaterials.BiomeBitmapOf` gives the template biome bitmap.
- **No caves and no ore veins:** no `ContinentCavePlacements` or `ContinentVeinData` file exists for their
  continents 436-438 (the client names both by continent, "Ore veins" above).

### The Events world

- **Listing:** the Landscapes window lists one event island on a second world, "Events" (id 3, name text 567016). It
  (Its surface caches ship with the 0.695 client only.)
- **Choosing the island:** the setting `EventIsland` (Docker `EVENT_ISLAND`) picks the island at start-up. The values
  are `auto` (the default = live), `halloween`, `winter`, `judgement` and `off`. `auto` follows live's 2016 dates:
  Halloween Party from 3 Oct to 20 Nov, Winter Holiday from 21 Nov to 10 Jan.
- **World id:** on the island, SendZoneDetails and ClientBeginZoning carry the Events world id, and the Site
  Management list gives each site the world it is on.
- **Logging in on a closed island:** a character who logs in on an island that is now closed starts at the home
  Spire.
- **Sites on a closed island** (`Game/Landscapes.EventExpiry.cs`): the island open at each start is remembered in the
  world database (`world_prop_setting` row propId 0, key `events_island_open`, value = zone id, 0 = none). When a start
  finds a different island open (or none), the one remembered has closed. With `EventIslandExpireOnClose` (Docker
  `EVENT_ISLAND_EXPIRE_ON_CLOSE`, default true = live; it deletes builds, so back up first) its sites expire on the first world tick
  that reaches the Environment Server, through the normal expiry: a Design saved, the ground reverted, props and
  resources mailed, and a "The … Island has closed" letter carrying the Build Site Contents item (wording ours). Live
  expired them at the switch (forum 58077, 21 Nov 2016). Every site is logged. With false they stay, and the start-up
  log lists them. The first start with this code only records the island. Dev command `eventexpire` is a dry run of the
  state and of every site on 460-462; `eventexpire confirm <zone>` expires a closed island's sites at once.

Details and evidence: research/trace/0695/event-islands-695.md.

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
- **Felling a tree touches no terrain** (checked 2026-10-21 after a player's "every login is slow since I felled a
  tree"; test `TreeFellingTerrainTests`): `Gathering.Chop` sends PlayerUpdate RemovePlayer and frees the spot
  (`gathered_nodes.txt`), and that is all. No voxel is written, no heal is queued, no cell joins the zone-in manifest,
  no TerrainCell packet goes out. The next login there gets the same terrain data as before and one AddLightweightProp
  less. Measured with the server's own generator (40 spots per landscape, 160 m view): a login at the centre of a
  210 m site on a Pioneer land sees ~16 trees of ~5 models, one 40 m outside the site ~25 trees of the same ~5 models;
  on 406 52 vs 81 trees of ~8 models. Pioneer lands had no ore-vein cells then (fixed the same day, below), a classic island ~375 per login
  ring (8 / 34 / 115 within 64 / 128 / 256 m), on and off site alike.
- The client's terrain work at zone-in does not depend on entities: its scene is a fixed set of cells ("Begin
  SCENE: … Process Pending: 6186" in both captured first loads, ClientSceneProcessing.log, FUN_1440222c0), of which
  "Request Pending" are the ones listed in the manifest (they wait for the server). `Avg contour: %d` (main client log,
  ContourThread::contourLoop FUN_143f98880) is printed each time one contour thread has spent another 5 s meshing,
  so a long run of those lines means slow meshing (the allocator-slot race, "Slow loading screen" below), not a
  bigger scene.
- Fixed 2026-10-21: Pioneer lands 464-468 loaded no vein data. `OreVeins` opened `ContinentVeinData<landscape>.vdt`,
  but the client loads `ContinentVeinData<continent>.vdt` (FUN_143ff34d0, "Ore veins" above), here 440-444. The
  server now opens the continent's file (and `CavePieces` the continent's `ContinentCavePlacements`, so cave veins
  find the Pioneer caverns' walls). Per landscape: an offline run of the server's own loader and generator over the 0.695 install
  (every 256 m region generated, ground from the baked heights):

  | Landscape | Vein file before → now | Surface / cave pool cells | Veins in the vdt | Veins made (surface / cave) | Ore cells |
  |---|---|---|---|---|---|
  | 406 Sleeping Ridge | 406 → 406 | 447,111 / 590,282 | 36,884 | 22,347 / 5,193 | 49,430 |
  | 391 Arid Thicket | 391 → 391 | 536,775 / 627,740 | 37,757 | 23,590 / 5,521 | 52,580 |
  | 393-416 (not 409) | own id → own id | 434-556 k / 584-761 k | 36.9-39.8 k | unchanged | unchanged |
  | 464 Pioneer Jungle | none → 440 | 352,491 / 13,472,604 | 217,588 | 30,585 / 109,396 | 261,098 |
  | 465 Pioneer Tundra | none → 441 | 353,860 / 13,485,442 | 218,814 | 30,522 / 110,332 | 263,233 |
  | 466 Pioneer Desert | none → 442 | 344,192 / 13,454,178 | 219,257 | 29,770 / 111,498 | 263,496 |
  | 467 Pioneer Old Growth | none → 443 | 352,537 / 13,298,689 | 218,330 | 30,527 / 110,393 | 260,139 |
  | 468 Pioneer Deciduous | none → 444 | 352,490 / 13,085,379 | 214,097 | 30,603 / 107,677 | 258,000 |
  | 460-462 event islands | none → none (436-438 not shipped) | — | — | — | — |

  The Pioneer files hold ~20 times the classic islands' cave pool and ~6 times their veins (live's data, read as it
  is), so a Pioneer land carries about five times the ore cells: ~2,300 manifest notes per 3 × 3-region login ring
  instead of ~430. Their surface veins (~30 k, like a classic island's ~23 k) land on the baked ground, so the
  files' cells are in the landscapes' own frame.

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
  u8 beginning-location flag, u32 property group, i8, u8 beginning-location type}` (FUN_143edfd60); a lot more data
  follows (classifications) that the server skips.
  - **Cave beginning locations** (LoadContinentCaves FUN_143ffcde0; research/trace/0695/agent-leads3-c.md L51): byte @32 set
    marks one, and AddCave (FUN_143f813e0) files it with the type in byte @38:
    - **1** surface entrance: tiles 48-50 Enterance01-03 in the `_Surface` files (210-479 per island);
    - **2** Pioneer isolated cavern: one per cavern in the `_Underground` files of 440-444 (950-1007 per land) and 432;
    - **3** cavern graveyard: the 81 Graveyard01 pieces (tile 111) of each classic island's `_Underground` file.
    - Only type 3 enters the client's random-pick table (GetRandomCaveBeginningLocation FUN_143f84420, which logs
      "Player attempting to roll random cave": the server half of the cave roll).
    - Byte @37 (i8) goes to the tile-instance constructor: -1 on surface entrances, 0 underground.
  - **Isolated caverns** (Pioneer lands): each type-2 record starts a contiguous run of that cavern's records, up to the
    next flagged record (65-101 pieces, all at the anchor's height, plus one Cap01 above; ~460-600 m square). The
    server puts at most one new-player site in each (`Pioneer.FindCavern`: the run's CavernMiddle01 nearest its x / z
    middle).
  Rotation turns x/z only: source index of world-relative (jx, jz) is rot 0 (jx, jz), 1 (−jz, jx), 2 (−jx, −jz),
  3 (jz, −jx), with j = round(Δ / 4 dm).
- **Combining:** the cave layer runs after the terrain generator: a voxel is air when any covering piece has type 0
  there. No noise or blending; DRAW_OUTER_SHELL is 0 for every tile. Continent 406: 5,817 placed pieces, 34 shapes,
  about 35 MB of LOD 0 data.
- **Server use:** a dig's wall vertices treat cave air as air (a dig adds no rock; the old solid shell never added rock
  where `CavePieces.IsAir` is true); Travel → "An Underground
  Adventure" lands on the floor of a random cavern (piece 104: air from about −76 m to +75 m around the origin; arrival
  scans down from origin + 10 m) on any landscape with an Underground `.cpa` (391, 393-408, 410-416; setting
  `CavernLandscapes`), re-zoning there when it is another landscape. `CavePieces.IsAir` and `Caves.Floor` /
  `FloorNear` take the landscape (`ContinentCavePlacements[<continent>]`, `CavePieces.PlacementPath`: the continent id
  is the zone id for 391-421 and 440-444 for the Pioneer lands 464-468; before 2026-10-21 the server opened
  `[<zone>]` and so knew no Pioneer cave air; every
  island has the same GEOMETRY_OFFSET, so one local frame). Outposts are stamped into each landscape's ruin slots
  (`RuinSlots.cs`), lazily: docs/gameplay.md "Chaos Caverns on every landscape".
- **Manual template placements** (`RuinSlots.ManualTemplates`; .cpa version > 10, after the CaveTileObjectGaps):
  `u32 n` × `{u32 record version (= the file version), Room(record version), u32 a, u32 b, u32 c}` (reader
  FUN_143fa3b70, fields +0x58 / +0x5c / +0x60; Room = attachments + box as above). On the islands every record is
  `(a = 1..4, b = 4, c = 76)`, one per cavern, its box exactly graveyard_launch.otd (332 × 88 × 332 dm); the old
  continents carry 60, 61 (underground_portal_node.otd), 75 and another 76. **a is a quarter turn**: a non-square
  template's box keeps the template's own x / z extents when a is even and swaps them when a is odd (template 61:
  1,530 of 1,560 boxes; 75: 1,528 of 1,556), and a is spread evenly over 1-4 at every spot. Ours: turns = a mod 4 in the
  cave pieces' sense (which odd value is +90° is not in the files). The retail client parses these and never uses
  them (FUN_143ff7bc0, their only collector, has no reference): the server stamps the graveyards (docs/gameplay.md
  "Graveyards").
- **Turned stamps** (`VoxelTemplate.StampAt(…, turns)`): the template turns about +y inside its own box, so the box keeps
  its corner (box minimum = corner + 1 voxel). A voxel index carries a sample at grid point i and a vertex in the cube
  from i to i + 1 (the offset is measured from the voxel's corner), so mirroring an axis of size S takes sample i to
  S + 1 − i but the vertex of cube i to cube S − i with offset byte b → 255 − b (128, the centre, kept); a turned voxel's
  material and vertex come from neighbouring source voxels. Props turn with the same point map and get the turn after
  their own rotation. Checked on graveyard_launch: every sample and every vertex of the 90° / 180° / 270° stamps lands
  where the turned unturned stamp puts it (within the codec's 1/85 voxel). Unturned stamps take the old path.
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
   (`PlayerManager.Reintroduce`; placed props only within `PropStreamRange`, gameplay.md "Props and crafting"). Onto
   the Spire or into the air over placed props the props go first and the two
   zone-done flags wait 6 s / 4 s, so their collision exists before the player can move ("Voxel templates" below).
4. The loading screen drops once the engine's focus reaches the player (`FUN_142a4e050`), about 10 s into a cave.

Since 2026-10-10 every move re-zones (`LoadingScreenDistance` 0), short ones included: plain TeleportToLocation hung
the renderer in QuerySimpleVisibility even for a few metres, and also for a zero-length move (2026-10-11). The re-zone
is over when its ClientIsReady arrives (`Teleport.FinishRezone` clears `RezoneTarget`). The rules found since (the
first two with the site games, 2026-10-11):

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
- **Short moves (2026-10-16 / 10-18, OPEN-LEADS L116).** Once the burst rule above was known, plain moves
  were tried again for short hops (`ShortTeleportDistance`, Docker `SHORT_TELEPORT_DISTANCE`). On a 0.695 client
  some hops of 3-45 m passed and then one hung, with nothing else in its burst. Two client traces followed:
  - The code (research/trace/0695/agent-short-teleport.md): TeleportToLocation has no visibility code of its own. The player's
    visibility objects move by the same per-frame step as walking: the transform goes on the room's dirty list, each
    camera flushes the list before its query, and the flush updates a loose octree of visibility objects (Remove, then
    Insert, when an object leaves its node). The octree's Remove (FUN_1440cba30) has a weak spot [P]: when it doesn't
    find the item where it expects, it either unlinks it from another slot but corrects the wrong slot's count, or
    returns without unlinking it at all; the following Insert then links the item a second time, and the next query
    loops for ever in QuerySimpleVisibility. A race with another thread was suspected.
  - The run (Frida round 80, research/trace/0695/agent-visibility-analysis.md): every octree call ran on one thread, so no
    race. For about one frame after **every** plain hop, the player's four visibility boxes (the actor and three
    attached boxes, nothing else) had a non-finite box. Update stores it; the next Remove reads the NaN size, looks in
    the wrong list and misses; Insert links again; two lists share a tail and the query cycles. Whether a hop hangs
    depends on the octant the boxes are in when the NaN frame lands, and a stale link from an earlier hop can make a
    later harmless move hang, which is why the hangs seemed to follow long hops. Distance is not the cause.
  - The NaN came from the server [I strong]: TeleportToLocation +0x30 was sent as the identity quaternion
    (0, 0, 0, 1), but the client reads it as a **forward direction** (FUN_142ec1650 masks w off and normalises x/y/z),
    so the actor's rotation axes were zero and its matrix singular. A re-zone reads its rotation as a real quaternion
    with a guard against zero, which is why re-zones never hung.

  Since 2026-10-18 the server sends a flat unit facing vector `(x, 0, z, 0)` there (the player's facing, (0, 0, 1, 0)
  if that is degenerate; `Teleport.FacingVector`), never zero. Since then `ShortTeleportDistance` is 60 m by default
  (inside the client's 80 m terrain window; 0 = every teleport re-zones); a re-zone also deletes and re-adds every
  visibility item, which clears stale links. Translocation Pad hops, a few metres by design, always move in place (gameplay.md
  "Teleports").
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

The Spire (`Game/Spire.cs`) places its template props once per landscape (marker `TerrainEdits/spire_base.txt` for the
home continent, `spire_base_<zone>.txt` for the others, holding `merge x y z deck …`: the template corner as a global
voxel and any deck-carve box, or `deck none`).

- **What the template is:** both Spire templates (`base_portal_spire.otd`, `portalspirebase.otd`) hold no solid voxel:
  a 124 × 68 × 124 m box of air over a vertex-only floor. Stamping that air carved cliffs into hillsides; since
  2026-10-19 the air box is **not** stamped.
- **Where the Hub stands:** model 596 reaches 20.94 m under its pivot (`.dme` / `.cdt`); the main deck is at pivot
  −2.924 m. Placement seats the deck just above the highest baked ground under the ±48 m walk footprint (+ 0.8 m for
  P5647), so the ~18 m plinth and lower ramps sit in the landscape and players climb from natural ground. No
  embankment fill. Only columns where ground still pokes through the deck are written as air (layer 0, permanent).
- **Spot:** nearest to the landscape centre (including home — no longer locked to (0, 0)) whose walk footprint
  (±48 m) has ≤ 17 m height spread (about the deck→base plinth) and the 124 m box ≤ 22 m, clear of build sites;
  else the flattest deck ≤ 17 m within 3 km. Steeper than the plinth: no Spire (would float under the base plane).
- **Migration:** old `x y z` (cell) → `….cell-reverted`; old `voxel … fill …` → `….voxel-reverted`; a `merge`
  marker whose deck is still steeper than 17 m → `….steep-reverted` and a new flatter spot.
- **Arrival:** pivot + (7.2, −0.5, −6.5): over the central dais (top at pivot −1.537 m, ~10 m across); the deck is at
  pivot −2.924 m (where the template's Replicator, Repair Station and Crimson Parlor stand) and the raised arms at
  −1.144 m.
- **The deck's collision after travel** (2026-10-17, research/trace/0695/agent-prop-collision.md, Frida physics hooks; arrivals
  from another landscape fell through the deck until then):
  - ClientBeginZoning deletes every entity but the player, in place or not, so the Spire's props are sent after every
    re-zone (`PlayerManager.Reintroduce`).
  - Every prop is sent collidable (AddLightweightProp flag byte 2 bit 0x08, protocol.md §4.2); with 0 a model that
    finished loading after the prop's setup lost its collision.
  - The bodies are built 2.7-4.4 s after the zone load (where the terrain was cached the loading screen dropped sooner
    and the player fell). So a re-zone onto the Spire sends its props at ClientIsReady and holds the zone-done flags
    (DoneSendingPreloadCharacters, ZoneDoneSendingInitialData) 6 s; the client lets the player move only after them.
  - A prop's collision is built once, from its model's first complete update, and fails for good if the model's
    current level of detail is not loaded yet. Only a move re-runs it: after the arrival the server sends each Spire
    prop its own unchanged PlayerUpdatePosition 0x7F at 1.5, 3 and 6 s, to players within 300 m
    (`Spire.ReassertCollision`; 12 s and 24 s were dropped 2026-10-18, after every measured body build). Only after a
    real re-zone: a short move (plain TeleportToLocation: pad hops, evac / GM requests, in-place respawns) deletes no
    entity, so it sends no retries.
  - An unrequested `LightweightToFullProp` does nothing for collision; the client asks for each prop's full data
    itself, as for every prop (dev `spirefull on` pushes it anyway).
  - The same hold (4 s) protects a re-zone into the air over placed props, e.g. onto a Magical Platform (gameplay.md
    "Teleports").

### Collision meshes (`CommonData/Collision/*.cdt`)

A model's `.adr` names its collision (`<CollisionData fileName="….cdt"/>`); the files lie loose in
`CommonData/Collision`, not in the packs. Format (read for the Spire, 2026-10-17), little-endian:

| Field | Meaning |
|---|---|
| `char[4]` | `CDTA`. |
| `u32` ×4 | 1, 0, 1 (one mesh), 0. |
| `u32 n` × `f32 ×3` | Vertices, model space (metres; the Spire's span exactly its `.dme` AABB). |
| `u32 n` × `u16 ×3` | Triangles. In this winding (b − a) × (c − a) points *into* the solid: the Spire's walkable tops come out with normal y = −1, its base faces with +1. |
| rest | A cooked copy of the same mesh (`u32` length first). |

The Spire's (`Theme_Widgets_Teleporters_PortalSpires01.cdt`, 5,092 vertices, 6,220 triangles): central dais at
y −1.537 (r ≤ ~10 m), deck −2.924, four raised arms −1.144 along the model's axes (to ~46 m out), four ramps along
the diagonals from the deck (r ≈ 31 m) down to the base plane −20.94 (r ≈ 60 m), legs to the base plane, nothing
solid in between under the deck.

Inside a template cell the voxels are in the same order as everywhere else (`y + 40 · (x + 40 · z)`): the 2016
Landmarks of Landmark templates prove it (only that order keeps their builds inside their 175 × 175-voxel contest box).
`VoxelTemplate.StampAt` (corners on any voxel, used by the outposts and the landmarks) uses that order since 2026-10-16;
before, it used `BlockData.Index` (x first), which swapped x and y inside each cell whenever the corner wasn't on a cell
boundary.

### Landmarks of Landmark (`Game/LandmarksOfLandmark.cs`)

The contest-winning builds the developers placed beside every Spire (list of 3 Aug 2016, forum thread 57799; 145 entries:
5 live worlds × 29 landscapes, in `LandmarksOfLandmark.Table.cs`). The server shows one world's set (`LandmarksOfLandmarkWorld`
/ Docker `LANDMARKS_WORLD`: `auto` = the live world named by `WorldNameId`, else Courage).

- **Templates:** `landmarks_of_landmark_2016_winner_-_*.otd` (122 files, 2016 winners) and `lm_<creator>_*.otd` (99 files,
  2014 winners). A 2016 template is the whole 70 × 100 × 70 m contest box (175 × 250 × 175 voxels) and writes **air**
  everywhere the build isn't; a 2014 template writes only what was built. One file the client ships is damaged by a
  text-mode copy (`…aetherion_tower.otd`: every LF written as CR LF); it is repaired in memory using each cell's length
  and the zlib checksum (`UndoTextMode`).
- **When:** once per landscape, right after its Spire is built (`Spire.Ensure` → `LandmarksOfLandmark.Prepare`): the home
  continent at start-up, the others on first travel. Marker `TerrainEdits/landmarks_of_landmark.txt`, one line per
  template: `landscape, world, template, corner voxel x y z, size x y z`. Cells are marked permanent (no heal).
- **Where [I]:** the nearest spot to the Spire (rings 10 m apart) whose footprint has dry ground no more than 6 m uneven
  and keeps 20 m from every site and from the Spire's platform. Height: the build's lowest solid voxel layer on the lowest
  ground under it; template voxels below that layer are left out (the 2016 air box would dig a pit). Two entries were
  "combined on 1 claim" (the Overland Express with its two cars; Landmark Boy & Landmark Girl): their templates stand in a
  row along +x.
- **The site:** a claim record kept in memory (`Claims.AddServerSite`, re-registered at every start), owner guid
  `0x4C4F4C0000000000` (no character), Default Access Play Around, so nobody can build, mine or place props there. Its
  record has the admin template guid set (record +0x158 → client claim entry +0x138; `ClaimsPacketSetAsAdminClaim` sets
  it on live) and the five strings the walk-on window's contest panel reads (`FUN_142dc7540` → `ClaimDialogs.WinnerWalkOnText`):
  competition name "Landmarks of Landmark", build name "<title> by <creators>", description, year, contributors
  (`FUN_142db82e0` format 0: `"0 Name Name"` → `ClaimDialogs.AdminClaimContributors`).
- **Props:** the templates' props (spawners left out) and the award model `Theme_Awards_Showcases_LandmarkOfLandmarks01`
  (PropDefs 1735 "Landmarks of Landmark Winner", model 1369) 3 m outside the site on the Spire's side [I]; streamed to
  players within 320 m of the build (removed beyond 420 m), LightweightToFullProp on request.

## Protocol check (2026-10-12)

Every terrain packet the server sends was checked inside running clients. A Frida probe (not part
of this repository) hooks each client reader and checks after every packet that the reader's
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
cells it holds no data for (all versions 0, seen with a Frida probe), so answering every requested edited cell in full is
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
- The addresses above are 0.589's. In 0.695 the slot code is FUN_142c05050 (table 0x145427450, stride 0x258, used
  count 0x145426dd8, global lock 0x145426420; owner check FUN_142c07cf0), reached from the free path FUN_142c05ef0
  when the thread's TLS slot is empty (blocks up to 0x8000 bytes) [P]. `re/tools/slots.py` (research workspace) reads the 0.695 table by default (`LMEMU_CLIENT=589` for the old one). Options for lifting the limit: trace/0695/allocator-slots-options.md.
- Thread pools sized by the CPU (0.695) [P]: one thread per logical processor, no limit, for `Resource::Manager
  Decompression` (FUN_143a8d560) and `TaskManagerJob` (FUN_143a98090); processors − 1 up to 16 for the pool set up
  in FUN_143a6fcd0; capped: VoxelFarm contour threads 3 (FUN_143f94580), contour job queue ≤ 3 (FUN_143133360),
  Morpheme network ≤ 4 (FUN_1440b4440). So the more logical processors, the fuller the table, whatever
  DirectThreadCount says; threads started late (a design's placement preview, docs/gameplay.md "Placing a design")
  are the ones left without a slot.
- **Who wins the last slots is a race** [P code, I timing]: a thread takes its slot at its first small allocation,
  not when it starts. The pool workers (FUN_143a95170) take theirs at their first job; the three contour threads
  (`ContourThread::contourLoop` FUN_143f98880) only at their first meshing job, after the cell manifest. On a 16+
  thread CPU the table can't hold everyone, and the bigger the burst of decompression / task jobs before meshing
  starts (models of creatures, trees, props, chests...), the likelier the pools take the last slots first. Each
  `Avg contour` line in the client log is another 5 s of meshing by one contour thread, so an hour of them is a
  contour thread without a slot.
- **Server-side mitigation: the stream hold** (2026-10-21; docs/gameplay.md "Stream hold", setting
  `STREAM_HOLD_UNTIL_LOADED`, default on). While the loading screen is up the server sends only what the screen
  needs and what the arrival stands on (props under the player, the Spire's deck); creatures, trees, chests,
  outposts, the other props and the warp crystals wait for ClientLoading `{0}`, which the client sends only once
  its scene has no meshing pending (ShouldDrop FUN_142d54a00 → FUN_14314fa00 → FUN_142ae6b20) [P], or 90 s
  (`STREAM_HOLD_LIMIT_SECONDS`). So the contour threads start meshing before the asset burst wakes the pools. It
  changes only the order, not what is sent. It can't make room where the fixed threads alone fill the table; the
  client-side options (a CPU-count cap in LaunchPad, msconfig processors, E-cores off) are in
  trace/0695/allocator-slots-options.md and allocator-slots-fix-design.md. To compare, a player can switch the
  setting off and count the `Avg contour` lines and the ClientSceneProcessing.log `Finish SCENE` times per load.

**History (Daybreak forums, archived):** the live game had this bug. A developer's feedback thread on long load times
(23 May 2016) collected reports of loads stuck at 50%, mostly on 6- and 8-core CPUs with hyper-threading (i7-5820K,
3930K, 5960X), fast or slow at random. On 13 June 2016 the developers tied it to the number of cores and had a 100%
repro case; client patches on 13/14 June, 16 June (six-core machines) and 30 June (8-core loading, as players reported
the notes) only partly fixed it, and 8-core owners still reported it in November 2016. Players worked around
it by booting Windows with fewer processors (msconfig → Boot → Advanced → Number of processors). The 0.589
client (built 4 June 2016) predates all of these patches; LaunchPad sets `DirectThreadCount=2` for every build. Players also saw it on later zone changes in the same
session, not only on the first load: slot use grows while playing (58 → 61 in one session here), so re-zones
(teleports, caves) may hit it after a while.
