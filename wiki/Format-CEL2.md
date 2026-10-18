# Format: CEL2

`TerrainCellBlobStore.bin`: a store of terrain cells taken from captured CellsUpdate packets, written by the research
tool [LandmarkEmulator.Parser](Project-Parser) (`pack-terrain`, `merge-cel2`). Ours.

Back to [Asset Documentation](Asset-Documentation).

**Status:** a research and capture-replay format. The current server programs do not read it (the world serves its
own edits from its terrain store, see [Format: World Data Folder](Format-World-Data-Folder)); it is kept for working
with captures.

## Layout

| Field | Meaning |
|---|---|
| `u32` | Magic `CEL2` (`0x324C4543`) |
| `u32 n` | Number of cells |
| n × cell | See below |

Each cell, as the 0.695 client reads a CellsUpdate entry:

| Field | Meaning |
|---|---|
| `u64` | Cell id ([Format: Terrain Cells](Format-Terrain-Cells)) |
| `u32` | Cell version (0 = block data, else meshes) |
| `u32` | Number of layers |
| `u32` | The first layer's id (0-3); 0 when the cell has no layer |
| `i32 length` + bytes | Everything after the first layer id: the first layer's version, flag, size and data, then each further layer whole (its layer id first) |

The tool also copies the captured cell manifest beside it. Code: `LandmarkEmulator.Parser/TerrainCel2Packer.cs`.
