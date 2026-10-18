# Asset Documentation

The file and data formats LandmarkEmu works with: the ones decoded from the game client (so the server can read the
player's install or answer the client in its own formats) and the ones this project defined (patch packs, the
server's saved files). Each page summarises a format; the full research notes are the documents in `docs/` in the
repository.

The repository holds **no game files**. Formats of the client's files are described from reading the client, and the
server reads the files from each owner's own install. Anything not proven against the running client is marked
*not proven* on the format's page.

All binary formats are **little-endian** unless a page says otherwise. Types: `u8 / u16 / u32 / u64`, `i32`, `f32`,
`bool` (one byte), `string` (u32 length + bytes, no terminator).

## Game data and network

| Page | Format | Origin |
|---|---|---|
| [Format: Packet Framing](Format-Packet-Framing) | SOE UDP, the gateway and its tunnel channels, zone packets, the internal service transport | client protocol (decoded); internal transport ours |
| [Format: Game Data Tables](Format-Game-Data-Tables) | The client's `Resources/*.txt` tables, the text file `en_us_data.dat`, the client files the server reads from the install | client files (decoded) |

## Terrain

| Page | Format | Origin |
|---|---|---|
| [Format: Terrain Cells](Format-Terrain-Cells) | VoxelFarm cell ids, layers, the cell manifest, CellsUpdate, the voxel block format (format 2), the mesh format, the block codec | client protocol (decoded) |
| [Format: Voxel Templates](Format-Voxel-Templates) | `.otd` voxel templates (prefab structures) and `.cdt` collision meshes | client files (decoded) |
| [Format: Surface Cache](Format-Surface-Cache) | `.csc` continent surface caches | client files (decoded, meaning partly fitted) |
| [Format: Terrain Heights](Format-Terrain-Heights) | `TerrainHeights<zone>.bin` (`LMH1`) ground-height grids | ours (sampled from the client) |
| [Format: CEL2](Format-CEL2) | `TerrainCellBlobStore.bin`, captured terrain cells | ours (Parser tool) |
| [Format: World Data Folder](Format-World-Data-Folder) | What a world keeps on disk: `cells.pack` (`LMCP`), `heal-queue.log`, markers, other saved files | ours |

## LaunchPad and patches

| Page | Format | Origin |
|---|---|---|
| [Format: LMPatch](Format-LMPatch) | `.lmpatch` packs, `pack.json`, `LME1` in-place edits, `LMD1` deltas | ours |
| [Format: Strong Hash List](Format-Strong-Hash-List) | The built-in file lists (path, size, CRC32, SHA-256 / SHA-1) | ours (from Daybreak's file lists and genuine copies) |

## Documents in the repository

| Document | What it covers |
|---|---|
| `docs/protocol.md` | Transport and the layout of every packet family the server handles |
| `docs/terrain.md` | Terrain: cells, block and mesh formats, heights, materials, ore veins, caves, templates, surface caches |
| `docs/launchpad.md` | LaunchPad, the update, the strong list, patch packs, the server's patch folder |
| `docs/client-reference.md` | Where things are in the client, the client data the server reads, client logs |
| `docs/sendself.md` | The SendSelfToClient record field by field |
| `docs/gameplay.md` and the others | How each system works |
| `docs/unknowns.md` | Fields whose purpose is still unknown |

See also [Code Documentation](Code-Documentation) for the code that reads and writes each format.
