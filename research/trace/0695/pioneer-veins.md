# Ore veins on the Pioneer lands: why they stuck out, and the rule now

Written 2026-11-01 after the user's report "on the pioneer lands ores stick out like this" (screenshot on Pioneer land
464, character TestPlayer2: a copper-coloured block about 2-3 m above the grass with an orange ore patch around it). Fix on
branch `local/pioneer-veins` (worktree re/wt-veins). Lead: OPEN-LEADS L265.

Marks: [P] proven (read / measured), [I] inferred.

## 1. The client's rule

- **Ore only repaints solid generated ground [P].** 0.695 FUN_143fdb110 (= 0.589 FUN_143f17220, same size 2513) reads the
  cell's padded 44³ source block (flags at param_5, u16 materials at +0x14cc0, index (y·44+z)·44+x+0xF7A) and writes
  only when the source voxel has flag bit 0 and a material 1..0xFFFE (and the ore material is 1..0xFFFE): out flags |= 1,
  out material = ore, a written-mask bit. It never makes a voxel solid (re/decomp_all_695/part_0163.c:2702-2716). Side
  detail: with VeinProvider+0x48 bit 0x80 about 10 % of those voxels get material 0 instead (meaning not known).
- **That code is server code compiled in [P].** Nothing in the client calls ApplyMaterialVein FUN_143fd8f50
  (VeinProvider.cpp; walk FUN_143fda7a0 → apply FUN_143fdb110); its only references are .pdata / unwind entries. Live's
  server ran it on its own generated cells, so it knew which voxels were solid.
- **Surface start [P].** FUN_143fdc6a0 (= 0.589 FUN_143f187b0): a random surface-pool cell (FUN_14407a430 on
  VeinProvider+0x40), a random x / z in it (LCG FUN_143fdd850), the height from the terrain (VeinProvider+0x38 virtual
  +0x58, which also gives the normal; FUN_143fd91b0 samples the same query on a grid), refused unless
  0 ≤ h − cellY·cellSize ≤ cellSize, then h += rand(SURFACE_OFFSET_MIN..MAX) × −10.0 (DAT_1449a43f8; both 0 for every
  v2 vein), a biome / tier check (FUN_143fa0cd0 through +0x30, reading inferred), NEARBY_STD_DEV (radius 0 for v2:
  skipped); direction −normal (part_0163.c:3188-3236).
- **What the height query is [P].** In the procedural branch of VoxelFarmCore init FUN_143ff9e30 the VeinProvider gets
  the procedural generator FUN_1440230d0 at +0x30 and the core's virtual +0x50 provider at +0x38 (FUN_143fdce70): veins
  start on the very terrain the client draws (part_0163.c:25583-25648, 3389-3394).
- **No wire mode "paint only if solid" [P].** Block layers merge with flags OR'd, the material where bit 0 is set, and
  the composite overrides the generated grid per bit (FUN_1440498d0, FUN_142aa8830); the mesher counts any material other
  than 0 / 0xFFFC as solid (ContourEx FUN_142ac4590), a voxel without flag 2 is drawn at its cube's centre. So the server
  must itself make sure a voxel is solid on the client before sending ore there (docs/terrain.md "How the client draws a
  dig").

## 2. Why the Pioneer veins stuck out

- **The server's Pioneer ground is an estimate [P].** TerrainHeights464-468.bin (16 KB, 501² every 16 m) were made by
  re/tools/csc_heights.py from ContinentSurfaceCache440-444.csc, because the Pioneer terrain is not built by the heightmap
  sampler the 2 m bake hooks (MODLOG 2026-10-17). The cache says only that the plateau's ground lies in the 16 m cell
  1024-1040 m (low 18, count 0 on 139,351 of 251,001 texels of 440); the script writes cell bottom + 5 m = 1029.0 m.
- **The client draws lower [P].** From the 2026-11-01 log: pick hits on grass beside the dug veins at 1027.82-1027.95 m,
  TestPlayer2's feet 1027.90 m at (140.0, −309.8), rings and feet down to 1027.42 m near (127-129, 170-176); 1029.3-1029.5 m
  near the Spire.
- **So the vein layer wrote ore into the client's air [P].** OreVeins started every plateau surface vein at 1029.0 m and
  `GeneratedSolid` took every voxel under 1029.0 m as rock; Compose sent them flag 1 + ore, solid on the client, drawn at
  cube centres: a stair-stepped block on the grass, the flat patch where the vein meets the real ground. Replay of the
  session's own generator (19 regions incl. claim 81's ClearSite, counts identical to the log): 449 surface veins, every
  one with ore in the client's air; 24,761 ore voxels whose sample lies at or above a 1027.9 m ground (38,136 against
  1027.4 m). The two veins TestPlayer2 dug (amaranthine 1674, copper 1661, start (142.6, 1029.0, −311 / −307)) had their top
  at 1028.60 m in the client's own first pick hits: 0.7-0.8 m proud. The reported 2-3 m needs ground 2-3 m under 1029 m,
  possible on lower parts of the plateau or the coast blends [I].
- **Not the cause [P]:** the continent files (the .vdt's cells are in 464's frame; the cave veins all lie at 829.8 m or
  deeper) and Compose itself (it writes only what Place filtered).
- **Classic islands are fine [P].** Their 2 m bake is the client's own ground: 0 of 1,856,776 ore voxels over 132
  regions of 406 have their sample at or above it.

## 3. The surface cache's floor (measured 2026-11-01) [P]

For every 2 m bake sample, the bottom of the lowest cell the cache gives the 3 × 3 texels around it
(`(y + low) × 16 − 2000`), compared with the baked ground (scratchpad cscfloor.py; copies in <scratch>\veinfix):

| Landscapes | Samples | Bake below the 3 × 3 floor |
|---|---|---|
| 391, 393-408, 410-417, 419, 460-462 (28) | 16 M each | none (smallest margin 0.46-0.83 m) |
| 403 | 16 M | 1 sample, 0.06 m |
| 418 | 16 M | 27 samples, up to 0.64 m |
| 420 | 16 M | 35 samples, up to 0.82 m |

The own texel alone misses more (up to 2.79 m on 415). With a 1.2 m margin (0.82 m plus the 2 m bake's own error between
samples, p99 0.38 m on 406, rounded up to 3 voxels) no sample of the 31 landscapes lies below. On 464 every measured
client ground (1027.42-1029.5 m) lies in the cache's 1024-1040 m band.

## 4. Live

- Surface ore everywhere [P]: wiki Copper / Tin / Iron Ore, Marble, Agate, Sapphire, Amaranthine "Area Found: All
  landscape surfaces"; Exploring.wiki: on the Pioneer Landscape "you'll find plenty of resources"; Beginner Guide: surface
  ore as "unusually-colored spots on the ground" (flush patches, not lumps).
- Surface iron not on Pioneer lands [P]: hotfix 26 Apr 2016 (design/forum/all/2016-04-26-56999 line 248): "Iron deposits
  are now more dense in Chaos Caverns and available on the surface of (non-Pioneer) Landscapes as well". The shipped
  ContinentVeinData440-444.vdt (identical in 0.589 and 0.695) still list vein 247 (440: 6197); the builder FUN_144073160
  adds every group-11 vein to every continent, so the exclusion was the server's [I].
- Pioneer terrain [P]: flat, no cavern networks (21 Mar 2016), isolated caverns with no ruins / monsters (P4300); players
  mined their caverns ("I get more minerals on the Pioneer islands if I tunnel down into the caverns below", 2016-05-04
  thread line 3769).

## 5. What the server does now

- `OreVeins`: a landscape whose height grid is coarser than 2 m (`TerrainHeights.StepOf`; the Pioneer lands today) has
  `CoarseGround`: `GeneratedSolid` takes as ground the cache's floor (`SurfaceCache.FloorAt`: §3 less 1.2 m) instead of
  the estimate, so ore only lies where the client's ground is certainly solid (and not cave air).
- `PioneerSurfaceVeins` (WorldServer.json / EnvServer.json; Docker PIONEER_SURFACE_VEINS): `buried` (default) keeps the
  veins (same kinds, counts, x / z; the pool-cell test on the estimate as before) and starts them at that floor; `off`
  leaves surface veins out (cave veins stay). Neither is live (flush patches need the exact ground, L265); buried is the
  closest.
- `PioneerSurfaceIron` (PIONEER_SURFACE_IRON, default false = live): surface iron left out on Pioneer lands
  (StaticZoneData IS_NEWBIE_ZONE, `LandscapeContinents.IsPioneer`).
- Vein cell versions: `OreVeins.VersionBaseOf` gives a landscape whose layout differs from earlier server versions a
  block of 65,536 versions per layout (bits: 1 buried, 2 off, 4 iron left out, 8 Pioneer land; 0 = every classic
  island). The client's mesh cache redraws a stored mesh while the version matches; without this, clients would keep
  their cached lumps.
- Measured with the server's own code over the 0.695 install (<scratch>\veinfix\verify; old = client-0.695 HEAD
  814df329):

| | old | new (buried, no surface iron) |
|---|---|---|
| 464, session's 19 regions: surface / cave veins | 449 / 2,154 | 364 / 2,154 |
| ore voxels at or above 1024 / 1027.4 / 1027.9 m | 129,818 / 38,136 / 24,761 | 0 / 0 / 0 |
| highest ore top face | 1028.80 m | 1022.80 m |
| vein cell versions | 2-5 | 851,970-851,973 |
| 464 whole land: surface veins (starts) | 30,572 (44.5 % at 1029.0 m) | 24,709 (10,925 at 1022.8 m, 6,820 on the seabed at 734.8 m) |
| 406, 132 regions: veins / cells / ore voxels | 4,408 / 7,927 / 1,856,776 | identical, digest FAB7228D2EFD33AC both |

- Tests: LandmarkEmulator.Tests/Rules/PioneerVeinTests.cs (flat Pioneer land: no ore above the lowest possible ground,
  starts at the floor, every vein keeps ore; iron off / on; off mode; versions; classic island unchanged and flush;
  the floor's 3 × 3 rule).

## 6. Existing worlds

Veins are not stored: the vein layer is rebuilt from the seed at every start, as players come near, and the manifest
is built from it per zone-in. So nothing on disk needs rebuilding; the new layout counts from the next start of the
world (in-process store) or the Environment Server. Clients re-mesh each Pioneer vein cell they hold once (one "version
mismatch" line per cell in VoxelDataErrors.log); cells that held ore before and none now leave the manifest and are
re-meshed from the generated terrain the same way. Digs made into the old lumps are dig-layer air above the real ground
and heal as usual (~20 min). Veins disturbed by mining move as before (in memory only).

## 7. Open (L265)

- The exact Pioneer ground: hook the VeinProvider+0x38 height query (virtual +0x58) or the procedural generator
  FUN_1440230d0 on a patched test copy (when the user allows), or decode Continent_Shape_440 + the Museum world
  settings' biome heights. A 2 m bake makes the lands classic (`StepOf` 2): veins on the ground, flush patches as live.
- The same 1029.0 m estimate feeds other Pioneer features (not checked in code): the slab's fill / cut
  (pioneer-slab-live.md), dig shells, tree / plant heights, claim height checks (Claims.HeightRefusal).
- Whether live left surface iron off by server rule (the note says so; the vdt lists it).
- A 2016 Pioneer video showing ore patches on the ground (Olgrey mRL8wKF36u4, Verlin Lj7o-3o93Kg) would confirm the
  flush look and the kinds.
- VeinProvider+0x48 bit 0x80 (10 % of ore voxels get material 0): what sets it, and whether live used it.
