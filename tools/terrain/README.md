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
