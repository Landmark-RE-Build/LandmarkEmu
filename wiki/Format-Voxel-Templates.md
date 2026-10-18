# Format: Voxel Templates

Two client file formats the server reads from the player's install to build server-side structures: **voxel
templates** (`.otd`) and **collision meshes** (`.cdt`). Decoded from the client's readers.

Back to [Asset Documentation](Asset-Documentation).

## Voxel templates (`CommonData/Voxel/templates/*.otd`)

Prefab structures: the Spire base, castles, floating islands, building platforms, outpost ruins, the cavern
graveyards, the Pioneer site slabs. The server stamps them into the terrain (layer 0) and places their props.
Version 6:

| Field | Meaning |
|---|---|
| `u8` | Version (6) |
| `u32 n` × cell | `u64` cell id (level 2; x / y / z relative to the template's corner), `u64` the same id, `u32` type (4), `u32` length, blob; types 2 / 3 add a `u32`. The blob is the block codec around an 8000-byte changed mask + block format 2: exactly a build-edit cell ([Format: Terrain Cells](Format-Terrain-Cells)) |
| `u32 × 3` | Size in voxels (the Spire base: 310 × 170 × 310 = 124 × 68 × 124 m) |
| `u32 n` × `{u16 material, u32}` | Material list (empty in the Spire templates) |
| varint `n` × prop | `u64` id, `u32` prop definition, `u32` model, `f32 × 3` position in metres from the corner, `f32 × 4` rotation, `f32 × 3` scale, `u64`, `u64`, `u32`, varint `n` × 16 bytes, varint `n` × 20 bytes `{u32, u32, u64 linked prop, u32}` |

**Varint**: bit 0 is the sign, bits 1-2 the number of extra bytes, the value is the rest (>> 3). Inside a template cell
the voxels are in the usual order (`y + 40 · (x + 40 · z)`).

Not every field's meaning is known (the unnamed `u64` / `u32` fields of a prop are *not proven*).

## Collision meshes (`CommonData/Collision/*.cdt`)

A model's `.adr` names its collision file; the files lie loose in `CommonData/Collision`. Read for the Spire:

| Field | Meaning |
|---|---|
| `char[4]` | `CDTA` |
| `u32 × 4` | 1, 0, 1 (one mesh), 0 |
| `u32 n` × `f32 × 3` | Vertices in model space (metres) |
| `u32 n` × `u16 × 3` | Triangles; in this winding `(b − a) × (c − a)` points *into* the solid |
| rest | A cooked copy of the same mesh (`u32` length first) |

Code: `LandmarkEmulator.EnvServer.Core/Terrain/VoxelTemplate.cs`; notes: `docs/terrain.md`, "Voxel templates".
