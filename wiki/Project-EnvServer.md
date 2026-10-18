# LandmarkEmulator.EnvServer

**Program** (Docker service `envserver`, optional, compose profile `envserver`; one per world). A world's Environment
Server: its terrain and voxel store in a process of its own, as live ran one.

Back to [Code Documentation](Code-Documentation).

## What it does

The world's zone process dials it (`ENVSERVER_ADDRESS`, port 20500 on the Docker network) and stays the only program
that talks to game clients about terrain. The service answers every request with the same store code the world runs
in-process ([EnvServer.Core](Project-EnvServer-Core)), on one work thread, together with the store's upkeep: the heal
queue (`ENVSERVER_HEAL_QUEUE`), ore-vein regions, flushing to disk, coarse (LOD) copies, and the stamps of the Spire,
ruins and Landmarks of Landmark.

After each change it pushes the changed cell versions to every linked zone (not the cell data: a zone fetches a cell's
body when a player needs it), and every 30 seconds it compares the whole version index again.

On its first start with an empty store it copies the world's `TerrainEdits` folder once (`ENVSERVER_IMPORT_FROM`;
marker `import.done`). Going back to the in-process store needs that folder copied back (`docs/docker.md`,
"Environment Server", in the repository).

## Main files

| Path | What |
|---|---|
| `EnvServer.cs`, `HostedService.cs` | Start-up and a graceful stop (finishes the work thread, writes the last changes) |
| `EnvServerConfiguration.cs` | `EnvServer.json` ([Settings Reference](Settings-Reference#envserverjson)) |
| `EnvHub.cs` | The links, the work loop and the version pushes |
| `DataImport.cs` | The one-time copy of the world's terrain edits |
| `JobCounters.cs` | Counters for the admin monitor page |
| `RuinRecords.cs` | Ruin-layer records (`ENVSERVER_RUIN_LAYERS`) |

Data formats: [Format: Terrain Cells](Format-Terrain-Cells), [Format: World Data Folder](Format-World-Data-Folder).
