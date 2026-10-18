# Format: Terrain Heights

`TerrainHeights<zone>.bin`: the ground height of a landscape's generated (unedited) terrain on a regular grid. Ours.
The server uses it to put trees, plants and the Spire on the ground, to snap teleports, to keep dug holes from adding
air above the surface, to know how deep a swing is, to place starting build sites and creatures. Without the file
those stay off for that landscape.

Back to [Asset Documentation](Asset-Documentation).

## Where it comes from

The client already computes the height a character stands on. The scripts in `tools/terrain` (in the repository) call
that function inside a running client (read-only hooks; no game file is changed), sample a 4000 × 4000 grid every 2 m
centred on the zone origin, and pack the result. The released `LandmarkEmu-terrain-heights.zip` holds a grid for each
landscape; the Docker setup service unpacks it into the game data folder.

Files are named by **zone id** (the continent id for the islands 391-421; the event islands 460 / 461 / 462 are
continents 436 / 437 / 438).

## Layout

| Field | Meaning |
|---|---|
| `char[4]` | `LMH1` |
| `i32 size` | Samples per side (the grid is size × size) |
| `f32 step` | Metres between samples (2) |
| `f32 minX`, `f32 minZ` | The local position of sample (0, 0), in metres |
| `f32 baseY`, `f32 unit` | Height = `baseY + value × unit` metres |
| `i32 length` + bytes | Deflate-compressed `u16` heights, row-major with z as the row; `0xFFFF` = no sample |
| `i32 length` + bytes | Deflate-compressed `u16` surface codes the sampler reported (meaning not decoded; not material ids) |

Heights between samples are bilinear. About 27 MB per landscape on disk, about 32 MB in memory; grids of landscapes
nobody uses are dropped again (the home continent's is kept).

Code: `LandmarkEmulator.EnvServer.Core/Terrain/TerrainHeights.cs`; scripts: `tools/terrain/README.md` in the
repository.
