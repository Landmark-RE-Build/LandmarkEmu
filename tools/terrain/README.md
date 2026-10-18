# Terrain height grid

The world server needs the ground height of the generated (unedited) terrain to place trees, tell how deep a
swing is, and later to place creatures. The client already computes it: its heightmap terrain function
(`ContinentMapHeightmapTerrain` vtable +0x58, `FUN_143fc3e00` in the 0.589 Landmark64.exe) returns exactly the
height a character stands on. These scripts sample that function from your own running client and write
`TerrainHeights<continent>.bin` into the game data folder. No game file is changed.

1. Log in to the world with one client running (any spot on the continent).
2. `python run_heightbake.py` (needs `frida`). It hooks the sampler and, inside the terrain worker threads' own
   calls, samples a 4000×4000 grid every 2 m centred on the zone origin (about a minute). Progress is written to
   `heightbake/progress.log`.
3. Close the game first, then stop the runner (detaching hooks from a running game can crash it).
4. `python pack_heights.py <GameDataPath> 406`. This writes about 27 MB.

Format: see `LandmarkEmulator.WorldServer/Game/Terrain/TerrainHeights.cs`.

## Several landscapes, the 0.695 client and the event islands

`bake_all.py <GameDataPath> <dev_commands.txt> <client Logs dir> <zone> [zone ...]` bakes one landscape after another
in one client session: it writes `landscape <zone>` to the world's dev command file (the server re-zones the client
there), waits for the loading screen to drop, samples and packs `TerrainHeights<zone>.bin`. The hook picks the
sampler's address by the client's build (0.589 `FUN_143fc3e00`, 0.695 `FUN_144087ab0`). Files are named by **zone
id**, which is the continent id for the islands 391-421 but not for the event islands: zones 460 / 461 / 462 are
continents 436 / 437 / 438 (`landscape 460` works even while that island is not on the Events world).

After baking an event island, check it against the client's shipped surface cache, which also tells whether the
bitmap names the server sent are the ones live used (`csc.py`; docs/terrain.md, "Surface cache"), with GAME_DIR and
GAME_DATA_DIR set as in docker/worlds/*.env:

    python csc.py pick 460 436 427 424 437

The right names give about 96-98 % on the island's own cache (every baked island 391-421 does against its own) and
side with it on the texels where the candidates differ.
