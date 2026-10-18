# Format: Surface Cache

`CommonData/Voxel/surfaceCache/ContinentSurfaceCache<continent>.csc`: a file the client ships, its precomputed map of
where the ground is per column of cells. Decoded from the client's reader; the **meaning of the values was fitted**
against the server's baked heights of 28 landscapes, so treat it as *not proven* beyond that fit.

Back to [Asset Documentation](Asset-Documentation).

## Layout

| Field | Meaning |
|---|---|
| `u8` | Version (1) |
| `u32 x, y, z` | The grid's origin in global cells |
| `u32 w, h` | Texels of level 0 (columns along x, rows along z) |
| body | 9 levels of `w × h` u16, rows along z; each next level halves x, y and z, with `w' = (w + 1 + (x & 1)) / 2` (h likewise) |

- Texel (row r, column c) of level k is the column at global cell (x + c, z + r); cells are `16 · 2^k` m.
- **Value** = `low | count << 10`: the ground of the column lies in cells `y + low` … `y + low + count`. Bits 8-9 are
  always 0.
- It is **not a height**: taking the middle of the range as the height is off by 1.6 m (median) and up to 40 m. The
  server does not use it for heights ([Format: Terrain Heights](Format-Terrain-Heights) does that).

## What it is used for

Checking that a landscape the client generates from the bitmap names the server sends is the one live shipped: the
right names reproduce about 96-98 % of an island's own cache (`tools/terrain/csc.py` in the repository). Full notes:
`docs/terrain.md`, "Surface cache".
