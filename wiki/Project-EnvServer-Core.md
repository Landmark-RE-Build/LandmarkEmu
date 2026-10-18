# LandmarkEmulator.EnvServer.Core

**Library.** The terrain store: everything about voxels the server keeps, used in-process by the world server and by
the [EnvServer](Project-EnvServer) program alike (`IEnvironmentServer`, with `LocalEnvironmentServer` as the
implementation both run).

Back to [Code Documentation](Code-Documentation).

## Main files

| Path | What |
|---|---|
| `Terrain/IEnvironmentServer.cs`, `LocalEnvironmentServer.cs` | The store's interface and its implementation |
| `Terrain/EnvironmentWire.cs`, `EnvironmentTypes.cs` | The messages between a zone and a separate Environment Server |
| `Terrain/TerrainStore.cs` | Cell ids, the modified-cell manifest, CellsUpdate bodies |
| `Terrain/TerrainEdits.cs` | Player and server edits as sparse overlays per cell and layer; digging, filling, stamping |
| `Terrain/BlockData.cs` | The voxel block format (format 2) the client composites over its generated world |
| `Terrain/CellPack.cs` | `cells.pack`, one append-only file per landscape holding every edited cell |
| `Terrain/HealQueue.cs` | Destroyed ground heals back after its delay; persisted in `heal-queue.log` |
| `Terrain/TerrainLod.cs` | Coarse copies of edits for distant viewing |
| `Terrain/TerrainHeights.cs` | The baked ground-height grids (`TerrainHeights<zone>.bin`) |
| `Terrain/TerrainMaterials.cs`, `BuildMaterials.cs` | What material a swing or a fill gives, build costs |
| `Terrain/OreVeins.cs`, `VeinGenerator.cs` | Ore veins |
| `Terrain/CavePieces.cs` | The client's cave pieces (where caverns are air) |
| `Terrain/VoxelTemplate.cs` | The client's voxel templates (`.otd`) for stamps |
| `Terrain/VoxelOwnership.cs` | Voxel Vision: which voxels belong to whom |
| `Terrain/SiteSolidity.cs` | Coarse solidity grids of build sites for creature pathing |
| `Game/GameDataFiles.cs` | Where game data files are found |

Formats: [Format: Terrain Cells](Format-Terrain-Cells), [Format: Terrain Heights](Format-Terrain-Heights),
[Format: Voxel Templates](Format-Voxel-Templates), [Format: World Data Folder](Format-World-Data-Folder), and
`docs/terrain.md` in the repository.
