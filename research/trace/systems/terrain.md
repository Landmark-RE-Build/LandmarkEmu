# Terrain (VoxelFarm) — client trace for the server rewrite (started 2026-10-13)

Old server code archived: git tag `terrain-v1`, copy in `archive/terrain-v1/`.

## Client objects
- `DAT_14535c2b8` voxel engine wrapper → +8 core → +0x5a60 `VoxelFarm::VoxelCellBlockCache` → +8
  `VoxelCellBlockCacheImpl` (VoxelCellBlockCache.cpp). Handlers: HandleCellManifest FUN_143f529a0,
  HandleCellUpdateResponse FUN_143f52b60, HandleCellsUpdate FUN_143f52db0, HandleUpToDateCells FUN_143f52fa0.
- Impl maps: +0x150 manifest (CellBlockManifestEntry: set, flag, 4 layer versions), +0x1f0 CellType, +0x300
  CellBlocks, +0x370 CellUpdateJobInfo, +0x448/+0x470 scene-pending cells (bytes 0..2 cache pending, +3 server pending),
  +0x510 local-edit pending map (count; +0x10 "server data arrived while a local edit was pending").
- VoxelClientImpl (vtable 14464c218): slot 10 FUN_1430aca10 sends ClientPendingRequestTimeout.
- ClientConstructionManager (DAT_1453747d8): FUN_1430b4e90 applies a tool edit only if FUN_143f532e0 (any +0x510 entry
  with +0x10 set) is false — otherwise the edit is silently dropped ("nothing happens").

## Rules learnt
- SCENE timer 6 s (FUN_143f571d0): cells whose server answer is late are drawn without it (generated ground) and logged
  to ClientSceneProcessingErrors.log ("ServerResponsePending: 1").
- CellsUpdate entry clears the scene's server-pending flag regardless of version; UpToDateCells clears it too and loads
  the client's cache copy.
- Incoming block layer: FUN_143f52470 → FUN_143f5ce70 stores each layer with the PACKET's layer version (in memory via
  FUN_143f1b4b0 and in the disk cache via FUN_143f0d7f0). If the cell has pending local edits (+0x510 count > 0), the
  update only sets the conflict flag (+0x10) — which blocks new edits.
- ClientCellRequest per cell: u64 cell + 4 × u32 = the layer versions the client currently holds. For cells the player
  edited these are client-side values (hash-like, identical content → identical value, e.g. 1413307746 for several
  fully-carved cells) — not our counters, so our manifest never matches and every login re-downloads all edited cells.
- Mesh cache (repository 0) is validated against an expected version; mismatch = re-mesh (VoxelDataErrors.log).
- ApplyDiffRequest (writer FUN_1430b0510): u64 +0x20, u64 +0x28, +0x30, cell list (FUN_1430b1d30: u32 n, n × (u64 key,
  u64 cell, u32 type, u32 len + blob, [u32 if type 2/3])), u64 +0x70, vec3 +0x78, vec3 +0x84, +0x90 list, u64 +0xc0,
  list +0xc8, u32 +0xe0, vec3 +0xe4, list +0xf0, u8 +0x108. The server reads only the cell list.
- Client edits carry vertex-only voxels (flag 2, no material) on the generated side of a surface; dropping them makes
  the server's copy blocky.

## Server problems found today (fixed in terrain-v1 line)
- Flood limiter dropped cell requests (0x10C cursor flood) — fixed.
- 64-cell edit limit refused big brushes without per-cell replies — fixed (1024).
- Timeout resends doubled traffic — fixed (dedupe 20 s).

## Open for the rewrite
- Version scheme the client uses for its own edits (where the hash-like layer version is computed) → serve the same
  so caches match (no re-download at login/teleport, no LOD flashes).
- Never send cell data for a cell with a pending local edit of that player (conflict flag blocks the tool).
- Transactions: ApplyDiff fields +0x20/+0x28/+0x70 vs CellsUpdate transaction list and ZoneProcessingComplete.
- Coarse levels (live had 2..10), layer use (client reports layer-1 versions on some cells).

## Layers, versions, LOD (traced 2026-10-13, part 2)
- Layer names (FUN_143bb4ef0): 0 "Cell Poi Lod", 1 "Cell Permanent Lod", 2 "Cell Timed Persistent Lod", 3 "Cell Transient
  Lod" (+ "Cell Composite Update Lod"). The CellBlocks object has 5 block slots (0-3 server layers, 4 = edit scratch).
- Live manifest (Resources/PacketDumps/TerrainCellManifest.bin): layer 0 38,190 cells at levels 2-10; layer 1 11,162 at
  levels 2-10 (8,694 at level 2); layer 2 125,246 at levels 2-4 only; layer 3 none. Nearly all versions are 32-bit
  hash-like values. Reading: POIs = layer 0, player builds = layer 1 (Permanent, with coarse copies up to level 10),
  dug terrain that heals = layer 2 (Timed Persistent, fine levels only), layer 3 unused.
- Layer version = CRC32 (FUN_142ac1800, standard table, init ~0, final ~) of the layer's COMPRESSED blob made by
  CompressBlockData (FUN_143f85d50: SerializeBlockData FUN_143f86db0 → codec FUN_1439f5840 level 6), 0 → 1,
  0x7fffffff → 0x7ffffffe. CellBlocks vtable +0xb8 (FUN_143f10690) re-serializes a layer and stores version +0x10+4L.
- Edit ack (HandleCellUpdateResponse FUN_143f52b60, cell + ok): pending count-- (ok=false → conflict flag); on ok the
  client writes ITS OWN cell to the disk cache as LAYER 1 with its computed version and updates its manifest entry
  (FUN_143f5cc90) — i.e. player edits live in layer 1 on the client. A conflict-flagged cell stays "busy" (tool refuses
  edits) until server CellsUpdate data for it arrives (FUN_143f52470 removes the entry when count is 0).
- Incoming CellsUpdate keeps layers the packet doesn't carry (FUN_143f5ce70 → FUN_143f1af90 copies them from the old
  cell). Our server sent edits as LAYER 0, so after a refetch the client held our layer 0 AND its own layer 1 with the
  same/different edits composited → doubled/stale shapes ("old renders, then blanks, cubes").
- Request (FUN_143f56980): a cell is requested when manifest versions ≠ held versions; entry = held versions. The
  server should answer UpToDateCells when the held version is current.
- VoxelFarm::updateBlockLOD FUN_142a15480: the block LOD downsampler; no callers in the client (only .pdata xrefs) —
  library code compiled in; candidate to port for coarse layer-1 copies.

## Rewrite plan (terrain v2)
1. Layers like live: build-site edits → layer 1, digging outside sites → layer 2 (heals, levels 2-4 only), server world
   stamps (Spire etc.) → layer 0. Manifest/CellsUpdate per layer; veins composed into the right layer.
2. Versions = CRC32 of the layer blob compressed at zlib level 6 (byte-identical serializer), so client caches match.
3. Never push cell data/notes to a builder for cells they just edited; answer requests with UpToDate when the held
   version is current; per-cell replies always (ok=false only with the authoritative cell following).
4. Coarse copies of layers 0/1 with a port of updateBlockLOD.
5. One-pass edit processing with compact undo (no per-voxel lists).
