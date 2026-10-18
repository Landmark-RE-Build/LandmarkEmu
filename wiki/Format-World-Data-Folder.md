# Format: World Data Folder

What a world server keeps on disk besides its databases. Ours. With Docker this is `server-data/worlds/<WORLD_KEY>`
(`WORLD_DATA_DIR`), mounted at `/data`; the container links each path below from the program's folder to it, so it
survives rebuilding the container. Without Docker the files lie next to the world server program.

Back to [Asset Documentation](Asset-Documentation).

Back up this folder together with the database before upgrades or before switching on a service with a one-time import.

## Contents

| Path | What |
|---|---|
| `TerrainEdits/` | The terrain store: every edited cell per landscape, the heal queue, the stamp markers (below). With an Environment Server the store is in `envserver/` instead and this folder keeps only the placement markers |
| `envserver/` | The Environment Server's own folder when it runs (`ENVSERVER_DATA_DIR`) |
| `felled_trees.txt`, `gathered_nodes.txt` | Trees and plants cut down and waiting to grow back |
| `Contests/`, `Portraits/` | Contest screenshots and character portraits (in the commerce service's volume instead when it runs) |
| `SiteImages/`, `TemplateImages/`, `TemplateProps/` | Build site screenshots, design images and design props |
| `Reports/`, `Telemetry/` | Player reports and client telemetry |
| `ItemAudit.txt`, `CombatGear.txt` | Generated reports (what each item does on this server), written by dev commands |
| `opcode_stats.tsv` | Packet counts for the admin monitor |
| `console.log` | The world's log |
| `dev_commands.txt` | Lines appended here run as dev commands ([Command Documentation](Command-Documentation)) |

## The terrain store

**`cells.pack`** (one per landscape folder): every edited cell in one append-only file.

| Field | Meaning |
|---|---|
| `char[4]`, `u32` | `LMCP`, format 1 |
| records | `u64 key` (cell id \| layer << 56), `u32 version`, `i32 length` (−1 = the cell was emptied and dropped), `i64` save time (Unix seconds), then `length` bytes: the packed cell |

A save appends the cell's new state; at load the last record of a key wins. A record cut short by a crash ends the
file and is cut off. When the file holds more than twice its live bytes (plus 32 MB) it is rewritten with only the live
records (to a temporary file first, then moved into place). Older stores had one `<key>.cell` file per cell (`u32
version` + packed cell); they are moved into the pack once and kept, no longer read.

The **packed cell** is the server's compact form of a cell's voxels: zlib of the flags, materials and offsets of the
40 × 40 × 40 voxels (the same fields as block format 2, [Format: Terrain Cells](Format-Terrain-Cells)).

**`heal-queue.log`**: pending heals of destroyed ground, one tab-separated line per record: `A` (an entry: id,
landscape, due time in ms, x, y, z, radius, the cells it touched as hex ids separated by commas, half height, flags) or
`D` (healed). Appended as destruction happens, compacted at start, so heals happen on time across restarts.

**Markers** (small text files): what has been stamped once and must not be stamped again, e.g.
`spire_base.txt` / `spire_base_<zone>.txt` (where the Spire stands and the ground filled under it),
`graveyards_<zone>.txt`, the outpost lists, `pioneer_slabs.txt`, `landmarks_of_landmark.txt`, and `layers.v2` (a
one-time move of build-site voxels from layer 0 to layer 1 is done).

Code: [EnvServer.Core](Project-EnvServer-Core) (`CellPack.cs`, `HealQueue.cs`, `TerrainEdits.cs`).
