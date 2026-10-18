# Format: Terrain Cells

Landmark's terrain is a **VoxelFarm** world: the client generates the unedited landscape itself, and the server only
stores and sends the cells that differ from it (player builds, digging, server stamps such as the Spire and the
outposts). This page summarises the formats involved; the full notes are `docs/terrain.md` in the repository.

Back to [Asset Documentation](Asset-Documentation).

## Cells and ids

- A cell is a cube of **40 × 40 × 40 voxels**. At level `L` it spans `40 · 2^L` world units (decimetres). Edits happen at
  **level 2**: 16 m cells, one voxel = 0.4 m. Coarser levels are used to draw terrain far away.
- **Cell id** (u64): `level << 60 | x << 36 | (y · 64) << 18 | z` (the low 6 bits of the y field are always 0).
- **Voxel index** inside a cell: `y + 40 · (x + 40 · z)`.
- World units: global decimetres from the continent origin; the client's local metres are `(global − zone origin) / 10`.

## Layers

Each cell has up to four layers, composited **on top of the client's generated terrain**: an empty voxel keeps the
generated ground, an air voxel (material 0) carves, a solid voxel adds.

| Layer | Client name | What the server puts there |
|---|---|---|
| 0 | Cell Poi | Server stamps: the Spire, outposts, landmarks |
| 1 | Cell Permanent | Player building on build sites (the client keeps a player's own edits here too) |
| 2 | Cell Timed Persistent | Digging outside build sites and ore veins; heals back after about 20 minutes |
| 3 | Cell Transient | Unused |

Each layer has its own **version**; the client keeps the versions it holds and asks again when they change.

## Getting cells to the client

1. At zone-in: the **modified-cell manifest** (TerrainCell `0xEC` sub `0x0A`): per layer 0-3, `list<{u64 cell, u32
   layer version, u8 flag}>`, block-codec compressed behind a u32 size.
2. The client requests the cells it needs (`0xEC` sub 1).
3. The server answers with **CellsUpdate** (`0xEC` sub 5); later edits are pushed the same way. Players far away get a
   short ModifiedCellManifestUpdate (`0xEC` sub `0x0B`) and fetch the cell when they need it.

CellsUpdate entry: `{u64 cell, u32 version, list<{u32 layer, u32 layer version, u8 flag, u32 raw size, blob}>}`.
**Version 0** means the layers are voxel **block data**, which the client composites and meshes itself (what the
server always sends); any other version means finished meshes. A CellsUpdate must carry every layer (a missing one
keeps the client's old copy).

## Block format 2 (the layer blob)

The block codec (`A1 B2 C3 D4`, big-endian size, zlib) around:

| Field | Meaning |
|---|---|
| `u32 2` | Format version |
| 8000 bytes | Occupancy bitmask of the 64,000 voxels, most significant bit first, voxel index order |
| `u32 n` + n × u8 | Flags of each occupied voxel: bit 0 has a material, bit 1 has a vertex offset, 0x20 offset is the centre (not stored), 0x40 material 0 (air), 0x80 palette index 1 (not stored) |
| `bool` | Palette in use (fewer than 256 materials) |
| palette | `u32 n` + n × u16 materials, then `u32 n` + n × u8 **1-based** indices; without a palette `u32 n` + n × u16 materials |
| `u32 n` + bytes | Vertex offsets; n counts **bytes**, 3 per voxel (x, y, z) |

- Materials are `VoxelMaterials.txt` **ARRAY_INDEX** values, not its ID column.
- Offset byte = `floor((clamp(u, −1, 2) + 1) · 85 + 0.5)`, `u` being the surface position in voxel units from the voxel's
  corner (0.5 → 128).
- **Vertex-only voxels** (an offset, no material) carry the surface shape on the generated side of an edit; clients
  send them with smooth edits and the server keeps them (`KEEP_VERTEX_ONLY_VOXELS`).

**Build edits** (VoxelConstruction `0xF4` sub 1, ApplyDiffRequest) carry, per touched cell, an 8000-byte *changed* mask
in the same bit order and the client's whole edited cell in block format 2; the server applies only the changed voxels,
checks permissions and costs, saves and broadcasts.

## Mesh format (reference)

Requested geometry cells (version ≠ 0) carry per layer a zlib blob with two optional meshes, a bounds block and an
AABB in cell units. A mesh: positions `u32 n` + n × 3 u16 (cell coordinate = (v − 32767.5) / 26214), normals `u32 n`
+ n × 3 u8 (`(v − 127) / 127`), then four u32 arrays (blend, material, flags, triangle indices). The server only uses
it for a developer test command.

## Coarse copies

The client draws distant terrain from coarser cells (level L+1 = twice the edge). The server builds copies of the
edits there (layers 0 and 1 up to level 6, layer 2 up to level 4) with VoxelFarm's octree rule, so builds stay visible
from afar (`TERRAIN_LOD_COPIES`).

## Server storage

How the server keeps these cells on disk (`cells.pack`, the heal queue): [Format: World Data Folder](Format-World-Data-Folder).
Code: [EnvServer.Core](Project-EnvServer-Core).
