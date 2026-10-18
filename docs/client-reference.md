# Client reference (Landmark64.exe)

Where things are in the retail client, for contributors who want to check or extend a finding. Addresses are
function entry points with image base `0x140000000`, in the 0.589.1.128895 exe unless a section says 0.695 (the
server speaks 0.695; the other documents give 0.695 addresses next to many of these). Names in the first table are the
ones given in our Ghidra project; the others keep Ghidra's `FUN_<address>` label.

## Networking and entities

| Address | Name | Role |
|---|---|---|
| 142c83660 | Zone_DispatchPacket | Zone packet dispatcher (opcode → handler through a byte table). |
| 142c45fa0 | PlayerUpdate_Dispatch | PlayerUpdate 0x26 sub-opcode switch (case labels shifted by one). |
| 142c4c900 | Zone_HandleRemotePosition | Position 0x7F; drops non-full updates whose movement version differs. |
| 142c4d630 | Zone_HandleSynchronization | Synchronization 0xC3. |
| 143b3e5e0 | NetClock_ApplySync | Sets the client's server-ms clock. |
| 142b88bc0 | Movement_ReadBody | Movement body reader (types 0–3). |
| 142b72520 | Stream_ReadUInt2BitLength | 2-bit-length unsigned integer. |
| 142b72690 | Stream_ReadPackedSignedInt | Packed signed integer. |
| 142cb22d0 | Stream_ReadString | u32-length string. |
| 142b80420 | SendSelfToClient_Read | Login self packet. |
| 142b8d680 | AddLightweightPc_Read | Other players. |
| 142b8fb40 / 142b87b80 | LightweightToFullPc_Read / FullPcData_Read | Full character data. |
| 142b8cfc0 | — | Lightweight entity base (props and NPCs). |
| 142b8dc00 | — | AddLightweightProp reader (base + prop part). |
| 142ba65c0 | — | Prop physics-body block (mode 0–5). |
| 142c44700 | — | `HandlePlayerUpdateAddLightweightProp` → SpawnProxiedProp. |
| 142b90ef0 | RemovePlayer_Read | |
| 142baf500 | UpdateCalculatedStats_Read | |
| 142ee66e0 | EntityManager_FindByTransientId | |
| 142ab4950 | FatalAbort_BadBeef | The `0xBADBEEF` abort hit when SendSelf isn't consumed exactly. |
| 1439f5840 / 1439f5950 | — | Block codec encode / decode (`A1 B2 C3 D4` + zlib). |
| 143952200 / 1439522d0 | — | SOE UDP compression inflate / deflate. |

## Session lifecycle

| Address | Role |
|---|---|
| 142c3b490 | Client run-state machine (per-frame). States: 7 WaitForCharacterList, 0x15 WaitingForReloginSession, 0x16 StartingRelogin, 0x17 ShuttingDown. |
| 142c6ee40 | Logout to character select (entered from Lua `Ui.Logout`, idle kick, or a gateway disconnect). |
| 142c24730 | Leave zone: sends `ClientLogout` 0x14 and waits up to 2 s for the server to close. |
| 142d58330 | Gateway OnDisconnect handler: shows "disconnected" and may call 142c42f00 → 142c6ee40. |

### Loading bar percentages (0.695, traced 2026-10-18)

The bar shows `GameData.LoadingProgress` row 0 `Progress` (LoadingScreen SWF `ProgressWidget.as` prints it as is). The
client keeps it at BaseClient +0x32e60 and sets it with FUN_142d4fbd0 (only upwards unless forced). Run-state names:
FUN_142ca93d0; per-frame run loop FUN_142cd4e60; transitions FUN_142d5b290 (logs `TransitionClientRunState`).

| % | Where |
|---|---|
| 1, 2, 4, 5, 12, 15 | Start-up (FUN_142d12360): graphics, app services, managers, mandatory / UI assets, end of init. |
| +1 per frame up to 25 | StartingLogin (6), FUN_142d5cbc0. It runs once there, so a direct (ticket) login leaves **16**. |
| +1 per frame up to 89 | WaitForCharacterList (7); 50 − x in WaitForCharacterSelectLoad (8); LoggingIn (9) forces 0. |
| unchanged | NetInitialize (10), **Connecting (0xb)** until SendSelfToClient has made the player: nothing updates the bar, so a direct login sits at 16 % (0 % after character select). The client takes the server's packets here for up to 1000 ms per pass (FUN_142df6120(…, 1000) → FUN_142b60910); one packet that takes longer (the old one-packet item list, ~25 s) freezes the window. |
| 20 + (28·a + 48·b)/100, at most +3 per frame, capped 98 | WaitForFirstZone (0xf) / WaitForZoneLoad (0x14) via WaitForWorldReady FUN_142d5e8f0 (a, b: asset and scene progress). |
| 49, 50 | WaitForConfirmationPacket (0x10), FUN_142d58210 (sends ClientIsReady). |
| 50 − remaining·k | Running (0x12) until the scene is processed (FUN_142cd6830 → FUN_142d5cbc0); 100 when it is (FUN_142cc9bd0), then HideLoadingScreen resets it to 0. |

## Inventory, equipment, abilities

| Address | Role |
|---|---|
| 14331fc60 | `Inventory_RequestEquipItem`: Lua `EquipItem` → InventoryPacketEquipByGuid / EquippedRemove. |
| 142da4a00 / 142da4dd0 | Equipment 0xCB dispatcher / SetCharacterEquipmentSlot reader. |
| 142e784e0 | Tooltip reply reader. |
| 142b832a0 | ItemUpdate reader. |
| 142d63690 | CasterInfoReply reader. |
| 142d71070 | Can-cast check (error codes). |
| 142d77f20 | Recast time from HarvestSpeed. |
| 142dbf9c0 | Mining ring size from HarvestingArea. |

## Terrain

| Address | Role |
|---|---|
| 1430a8410 | TerrainCell 0xEC dispatcher. |
| 143f52db0 | `VoxelCellBlockCacheImpl::HandleCellsUpdate`. Version 0 → block data (143f52470). |
| 143f86db0 / 143f87bf0 | SerializeBlockData / UnSerializeBlockData (block format 2). |
| 143f1cde0 | CellGeometry::Decompress (mesh layers). |
| 1430ae820 | CellMesh deserialize. |
| 14360db80 | Client mesher (reference for the mesh arrays). |
| 143fc3e00 | ContinentMapHeightmapTerrain ground height (vtable +0x58). |
| 142a0d650 | Vertex-offset byte encoding constants. |
| 143615110 | `VoxelFlora_PlaceOnTriangle`: grass seeding per triangle. |
| 1430aca80 | VoxelClientImpl → ClientConstructionManager commit (ApplyDiffRequest). |
| 1430b6560 | VoxelConstruction Response result codes. |

## Claims, props, crafting

| Address | Role |
|---|---|
| 142d1ff50 | ClaimsBase 0xF9 dispatcher (switch on sub − 1). |
| 142d01c40 | ClaimRecord reader. |
| 142d00720 | UpdateClaimsAOI reader. |
| 142d00990 / 142d1a610 | ClaimManagmentData reader / UI fill. |
| 142d15900 | "May build here" claim check. |
| 143b5b480 | Permission mask for a player in a claim. |
| 142f08e10 / 142f1ab30 | PropData / placement session. |
| 142e9acd0 | RecipeBase 0x42 dispatcher. |

## Social and UI

| Address | Role |
|---|---|
| 142d4c840 / 142d45590 | GroupsBase dispatcher / header reader. |
| 14348c040 | FriendData reader. |
| 1432c75d0 | Mail InboxListResponse reader. |
| 14358a7d0 | Mail manager: handles the inbox reply, calls Lua `OpenEmailInbox`. |
| 14358a220 | MailEnableWindow handler (sends the inbox request). |
| 1430624b0 | TemplateBase 0xF0 dispatcher. |
| 1430669c0 | Fills the Designs list (`Character.Templates`). |
| 143061750 | Design voxel storage limit (default 3,000,000 plus bonuses). |
| 1432a93e0 | Chat packet writer. |
| 14312fb00 / 143cd37b0 | GameTimeSync handler / SetTime. |

## Client data the server reads

From the server's game data folder (`GameDataPath`; in Docker `server-data/gamedata`, which the `setup` service fills
with every `Resources/*.txt` table and `Locale/en_us_data.dat` of your install, see docker.md) and, for terrain,
straight from the install (`GameDirectory`; in Docker your `GAME_DIR`, mounted read-only). Among them:

| File | Used for |
|---|---|
| `Resources/ProfileDefinitions.txt`, `BlackListEntries.txt`, `Locale/en_us_data.dat` | Character creation, name filter, all text (ids hashed with Jenkins lookup2 of `Global.Text.<id>`). |
| `InventoryDefinition.txt`, `ActionSlotDefinitions.txt` | Container and equipment slots, hotbars. |
| `Recipes.txt`, `RecipeComponents.txt`, `CraftingStations.txt`, `CraftingStationRecipeMappings.txt` | Crafting. |
| `PropDefs.txt`, `PropRecipeComponentMaps.txt` | Props and their cost. |
| `VoxelMaterials.txt`, `VoxelBiomes.txt`, `VoxelBiomeMaterials.txt`, `VoxelProceduralMaterials.txt` | Terrain materials, build costs. |
| `VoxelResourceGeology.txt`, `ResourceVeins.txt` | Ore veins. |
| `CommonData/Voxel/…` (install) | Biome bitmap, vein data. |
| `TerrainHeights<zone>.bin` | Ground heights per landscape, baked from the running client (`tools/terrain`; the release's `LandmarkEmu-terrain-heights.zip`). |
| `icons/<image set>.png` | Item icons for the Showcase pages, exported from the client's images (showcase.md "Icons"). |

The item definitions are not read from the install: the server sends the live item list it was captured with
(`LandmarkEmulator.WorldServer/Resources/PacketDumps/44-CommandBase`, protocol.md §5).

Other useful client tables (read when researching):
- `Models.txt`: model ids → `.adr`.
- `EquipmentSlotDefinitions.txt`.
- `Currency.txt`.
- `LootPinata.txt`.
- `Requirements.txt`.
- `StringHashToValue.txt`: client constants.

## UI and scripts

- **Start / Escape menu:** `UI/Default/Data.xml`, data source `StartMenu`. Each row names a Lua callback in
  `Resources/Scripts/global.lua`:
  - `ToggleTemplatesWindow`, `ToggleClaimsManagement`, `Logout`, …
- **Windows:** `UI/Default/<Window>.xml` + `.lua`.
  - Flash movies (`.gfx`) are in the `.pack` archives.
  - Flash talks to Lua through `CallLua…` and to data sources (`GameData.*`).
- **Asset archives:** `Resources/Assets/Assets_NNN.pack`. A chunked directory, **big-endian**: `u32 next chunk
  offset, u32 file count`, then per file `u32 name length, name, u32 offset, u32 length, u32 crc`.
  - Model collision is declared in each `.adr` (`CollisionData`).

## Client logs worth reading

All in `<install>/Logs/`:

| Log | Shows |
|---|---|
| `Landmark PlayClient (Test) (x64).log` | Run states, zone connect, loading. `SlowFrameMs=N` marks a main-thread frame of N ms; column 5 of every log is one sequence number shared by all the log files of a client run, so the lines other logs wrote between two of its lines show what that frame did. |
| `NetInfo.log` | Zone details, disconnect reasons. |
| `LuaErrors.log`, `FlashErrors.log`, `Ui.log` | UI errors (e.g. "DS … not found" = a data source the server never filled). |
| `ItemDefinitions.log`, `ActionBarManager.log`, `CalculatedStat.log` | Data the client accepted or rejected. |
| `EmbeddedBrowser.log` | The in-game browser. |
| `LoadingScreen.txt` | When the loading screen drops. |
