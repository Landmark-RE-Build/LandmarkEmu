# MODLOG — Landmark 0.589 reverse-engineering journal

Single place for what we've learned about the client: names, ids, offsets, formats, traps and test procedures.
Private research notes (game internals). Not part of the emulator repo; never publish decompiled code.
Addresses are for Landmark64.exe v0.589.1.128895 (image base 0x140000000).

## Intake, recon and route (Universal Modder mod-any-game steps 0-2)
- **Game:** Landmark (Steam 297810), owned by the user. Pinned build: 0.589.1.128895 (x64), at
  `Z:\SteamLibrary\steamapps\common\Landmark`. Upgrade to 0.695 later, gated by client build.
- **Idea:** recreate the whole game's server so the user's own client can play again (servers shut down Feb 2017).
- **Online vs offline:** the official servers no longer exist; we only run a private server the user controls.
  `um scan`: no anti-cheat, no loader, "unknown native engine" (ForgeLight).
- **Knowledge base:** no field notes for Landmark, ForgeLight or SOE games (searched the bundled knowledge base
  2026-10-08; `um kb search` needs PyYAML in the system Python).
- **Route: reimplementation (server side).** The client is the oracle and stays unmodified; we read it with
  Ghidra (headless + pyghidra-mcp) and confirm live with read-only Frida hooks. Why: there's no server code to
  patch, and the user wants no changes to game files. Base code: kirmmin/LandmarkEmu via the PheebeUK fork.
- **Done means** (per feature): working in the real client, verified by screenshot + logs, committed.

## Lab safety (step 3)
- Only game file changed: `ClientConfig.ini` `Server=127.0.0.1:20042` (original kept as `ClientConfig.ini.orig`).
- Backup: `um backup create "Z:\Landmark Emu\backups\landmark-config" --name landmark-config` (ClientConfig,
  UserOptions, UISettings; first snapshot 2026-10-08 10:42).
  Restore: `um backup restore landmark-config --to "Z:\Landmark Emu\backups\landmark-config"`, then copy the
  files back into the game folder.
- Decompiled output, hooks and the catalogue stay in `re\` (outside the repo). Never publish decompiled code.
  2026-10-09 (user: the emulator will be open source, explain everything decoded): client function addresses ARE
  kept in code comments and in LandmarkEmu/docs/ (client-reference.md) as reference points; formats are written
  up in our own words in docs/protocol.md, terrain.md, gameplay.md. `um publish check`: 0 failures, only
  address warnings (expected). Run it before sharing anything.

## Working rules
- Before driving input: `um win drive --proc Landmark64 idle` (if under ~60 s, the user is active: ask first).
- Circuit breaker: the same failure 3 times → stop, write down what's known here, change approach or ask.
- Kill processes by exact PID only.
- Ghidra MCP and headless batch runs can't use the project at the same time.

## Ghidra names (saved in the project via pyghidra-mcp)
Zone_DispatchPacket 142c83660 · PlayerUpdate_Dispatch 142c45fa0 · Zone_HandleRemotePosition 142c4c900 ·
Zone_HandleSynchronization 142c4d630 · NetClock_ApplySync 143b3e5e0 · Movement_ReadBody 142b88bc0 ·
Stream_ReadUInt2BitLength 142b72520 · Stream_ReadPackedSignedInt 142b72690 · Stream_ReadString 142cb22d0 ·
AddLightweightPc_Read 142b8d680 · LightweightToFullPc_Read 142b8fb40 · FullPcData_Read 142b87b80 ·
RemotePosition_Unserialize 142bb4530 · RemovePlayer_Read 142b90ef0 · Synchronization_Read 142b8b330 ·
UpdateCalculatedStats_Read 142baf500 · SendSelfToClient_Read 142b80420 · EntityManager_FindByTransientId 142ee66e0 ·
ProxiedCharacter_GetAppearanceSet 142e05f70 · ProxiedCharacter_SwitchToTemporaryAppearance 142e2b3d0 ·
ProxiedCharacter_SetScale 142e26bd0 · ProxiedCharacter_ApplyBaseActorRaceGender 142c0e2b0 · FatalAbort_BadBeef 142ab4950

## Tools and setup
- Ghidra 12.1.4 headless, JDK 21, project `re\Landmark589`. Run scripts with:
  `analyzeHeadless.bat "Z:\Landmark Emu\re" Landmark589 -process Landmark64.exe -noanalysis -readOnly -scriptPath re\scripts -postScript <Script.java> <args>`
  - Batch quirks: arguments can't contain `|`, `)`, `= 1` or commas. Use `+` as a list separator.
  - Set `GHIDRA_HEADLESS_MAXMEM=8G` for big runs; close the games first.
- Scripts in `re\scripts`: DumpAt (decompile addresses + callees), DumpVtable, DumpPacketReader (by string),
  FindGlobalUse, FindOffsetUse (functions using a scalar), ProbeAddr, FindOperand, CatalogPackets.
- Frida (read-only hooks): `python re\run_frida_all.py <hook.js> <log>` attaches to every Landmark64.exe.
- Universal Modder game automation:
  - `um win shot --hwnd <h> out.png --scale 0.4`
  - Input: `re\focus_hwnd.ps1 -Hwnd <h>` then WinDrive (`scanmode on`, `hold 0x57 1000`, `click x y`).
  - Two game windows share a process name, so always focus by window handle first.
  - `re\walk_to.py` autopilots a character using hook_dist_fast.js positions.

## Ghidra project gaps (important)
- Auto-analysis left big switch statements undecoded (e.g. the PlayerUpdate dispatcher FUN_142c45fa0 had only 80
  of its instructions disassembled). Code inside them, and calls to constructors only reached from there, is
  invisible to reference searches. The decompiler still shows it (as `func_0x…` calls).
- CatalogPackets.java recovers these in memory: 130 jump tables, 73 missing functions. Save them into the project
  (or use a live Ghidra via MCP) so it only needs doing once.

## Click points (1920×1017 client area)
| Screen | Element | x, y |
|---|---|---|
| Character select | "Dev Server" world row | 1540, 424 |
| Character select | Play | 960, 924 |

## Keys
W forward, S back, A/D turn (D increases atan2(dz, dx)), Shift heroic move, Space jump.
Plain mouse movement doesn't turn the camera.

## Zone dispatch
- Zone dispatcher FUN_142c83660(client, ?, data, len): case = byte table DAT_142c874b4[opcode-1].
- Zone client vtable at 0x1445bc780 / 0x1445e2418: +0x208 → FUN_142c4c900 (0x7F position), +0x288 → FUN_142c45fa0
  (PlayerUpdate 0x26).
- PlayerUpdate wire format: u16 0x26, then ONE byte sub-opcode (switch(sub-1)). Our enum's 4-byte 0x2600XX00 values
  add a stray byte; use 3-byte values like 0x260058.

## Packets confirmed in game
| Packet | Opcode | Reader | Notes |
|---|---|---|---|
| SendSelfToClient | 0x0F | FUN_142b80420 | 0.589 tail differs after Unknown68; must be consumed exactly or the client aborts (0xBADBEEF). Unknown1 = transient id. |
| PlayerUpdate AddLightweightPc | 0x26/0x58 | FUN_142b8d680 | guid, transient (2-bit uint), identity, i8, model id, u32, pos v4, rot v4, scale f32 (+0x110), 3 bools, mount guid, 2×u32, 5 bools, movement version byte (+0x12d, must be 1), 3×u64, 2×u32, u64, byte |
| PlayerUpdate FullCharacterDataRequest | 0x26/0x5E (C→S) | — | u64 guid; reply with LightweightToFullPc |
| PlayerUpdate LightweightToFullPc | 0x26/0x5B | FUN_142b8fb40 + blob FUN_142b87b80 | bool compressed, byte-array blob, then movement body, PlayerUpdateUpdateStat (26 00 53, guid, list), CalculatedStats (29 00 2C 00, guid, list). Blob found by transient id. Blob field after 3 strings = temporary appearance model: must be 0 or the body is hidden. |
| PlayerUpdate RemovePlayer | 0x26/0x01 | FUN_142b90ef0 | i16 version 0, u64 guid |
| Position (zone→client) | 0x7F | FUN_142bb4530 | 2-bit uint transient, movement body (FUN_142b88bc0), 4 floats = scale (0 or equal = no change) |
| Synchronization | 0xC3 | FUN_142b8b330 | 6× u64 + bool (not doubles). Client clock: ServerTime ms (FUN_143b3e5e0). |
| ClientUpdate UpdateCalculatedStats | 0x29/0x2C | FUN_142baf500 | guid, list of (u32 id, f32 value); dropped if the entity doesn't exist yet |
| TerrainCell ClientCellRequest / UpToDateCells | | | u32 sub-type after a u16 base (2 pad bytes) |

## Movement body (FUN_142b88bc0)
type byte, u32 timestamp (server clock ms), version byte, then signed 2-bit-length ints / 10^precision:
- 0x00 full: position (3, p2), rotation quaternion (4, p5), velocity (3, p5), 4×p2, 4×p2, p1, p5, flags
- 0x01: one 2-bit uint
- 0x02: position (3, p2), velocity (3, p5)
- 0x03: rotation (4, p5), 4×p2
- Non-full updates are dropped unless the version byte matches the entity's stored version (FUN_142c4c900).

## Items (2026-10-08)
- Item definitions are server-sent: one packet `1E 00 4B 00` (Command / ItemDefinitions), then u32 size,
  u32 count, then per item u16 compressed size, u16 decompressed size, LZ4 block. Client logs each one in
  `Logs\ItemDefinitions.log` ("Received item definition data, id: N").
- The packet dump we already send at login (`Resources\PacketDumps\44-CommandBase`) is the live item list:
  2,885 items. `re\items_decode.py` decodes it exactly (0 bytes left per item) into `gameData\items.json`;
  `re\items_names.py` adds English names (2,822 named) and `gameData\items_summary.tsv`.
- Item layout = kirmmin item-support decoder + two trailing u32s (zero in all items).
- Key ids: Copper Ore 34, Tin Ore 35, Iron Ore 36, Silver Ore 37, Gold Ore 39, Dirt 44, Sand 45, Stone 46,
  Plain Wood 71, Burled Wood 72, Striped Wood 73, Smelted Copper 108, Smelted Tin 109.
- Local (client) data: Recipes.txt (ITEM_DEFINITION_ID = output), RecipeComponents.txt (COMPONENT_ID = item id,
  COMPONENT_COUNT), InventoryDefinition.txt, Item* tables; art in 279 .pack files.

## Action bar, tools, equipment (2026-10-08)
- Bars are sized ONLY from SendSelfToClient's hotbar list (FUN_142b9e930 → bars at ClientActionBarManager+0x28):
  u32 bar count, per bar u32 id + u32 slot count, per slot u32 bar, u32 slot, u32 type, u64 value. A bar sent
  with 0 slots rejects every later ActionManager UpdateSlot (`Logs\ActionBarManager.log`: "illegal slot id").
  Slot types: 1 = item definition id (client rebinds it to the matching item → type 2), 2 = item guid,
  3/4 = abilities. Constants ActionBarCount/ActionSlotCount = 10 in StringHashToValue.txt.
- Using a tool from the bar sends Inventory 0x16 TransferItem: loc1, loc2, guid, qty, bool. The guid is the tool
  the player picked. Hand empty: (main hand), (bag, any), guid, 1. Something held: (bag, any), (main hand), guid
  of the NEW tool, 0 = one request meaning "wield this, held tool to bag". Same request for the tool already held
  = keep it. Server must answer every transfer with 0x19 TransferItemResponse = request echoed + u32 result.
- Moves must use ItemUpdate 0x29/3 (FUN_142b832a0 → FUN_142f2ff70: guid, count, durability, u32, u64, slot,
  container, 2×5 u32, 2 u32). Delete + add unbinds action bar slots (greyed icon, lost highlight).
- Wielding shows via Equipment 0xCB, 1-byte sub (dispatcher FUN_142da4a00): 1 SetCharacterEquipment,
  2 SetCharacterEquipmentSlot (FUN_142da4dd0: u32 profile, u64 char guid, u32 equip slot, u64 item guid,
  2 strings, then attachment: model .adr, texture alias, tint alias, decal, u32 tint, u32 composite effect,
  u32 slot, u32, bool), 3 Unset (u32 profile, u64 guid, u32 slot), 4 SetSlots. Confirmed in game.
- Tooltips: requests 0x0F/0x10 (item location, then u64 guid) and 0x11 (u32 def id, u8). Reply 0x12
  (FUN_142e784e0 → FUN_143463610): u16 sub, bool definition-tooltip, bool full, item record FUN_142e78e10
  ([0] name text id, [1] description id, [2] image set, [3], [4] def id, 3 u32 lists + 1 list, blob =
  01 + compressed definition chunk exactly as in the definitions packet, ..., u64 guid at [0x40], ...),
  then a list of related records and a u32. Working in game. 'No categories' line still shows (the first
  list is not categories, or category data is missing).
- Abilities 0x27, u16 sub. Holding a tool → client asks CasterInfoRequest (sub 2, u32 ability) until answered.
  Reply sub 3 (reader FUN_142d63690, layout in re/caster_info.py; captures for 221/1098/326/1122 fit exactly).
  Key fields: +0x1c kind hash (0xDFB05E5F ok), +0x20/+0x24 cost pool/amount, +0x28 ignore cost pool
  (else error 3 when pool 2 is empty), +0x8c re-pick tool mode (main-hand item's type-3 ability → Mining,
  else Logging/Reaping; FUN_1433aac10), +0x80/+0x84 max/min range (+ stat 0xF), +0x78/+0x7c cast/recovery
  seconds, +0xac animation id (245 = lightstone use), +0x34 name id, +0xc0 description id.
- Cast: client sends sub 1 AbilityCastAttempt (u64 cast guid, u32 hash, u32 seq, u32 ability, target pos...).
  Can-cast errors (FUN_142d71070): 2/15 pending cast, 3 cost pool, 5 cooldown, 14 busy. Server answers sub 7
  CastSuccess (backtimed header: sub again u16, u8, u32; caster u64, cast guid u64 + hash, ability, anim,
  u32, u32, bool). Mining state confirmed with a Frida trace (hook_actionstate.js, hook_cast2.js).
- Mining is server-driven: TerrainCell 0xEC/7 CellBlockDamage and 0xEC/5 CellsUpdate are server → client.
  Client sends only the cast + target. Next: terrain cells (TerrainCel2Packer.cs, docs/packet-dumps-terrain.md).
- Building tools: ToolTypes.txt (2 Select, 3 Smooth, 5 Heal, 6 Remove, 10 Add, 11 Paint, 13 Line, 17 Fill;
  7 Location = voxel cursor). Client sends Tool 0x117 sub 1 ToolActivate (u32, u32 tool type).
- GamePacketWriter has no u16 overload: (ushort) values are written as 4 bytes.
- Never kill a Frida runner while the game runs with Interceptor hooks in hot code: detaching mid-call crashed
  the client. Kill the game first.

## Building, props, crafting: what blocks them (2026-10-08)
- Build palette (wrench, top right): tabs = PaletteCollection{Templates,Props,Materials,Shapes,Tools}
  (ClientCategoryPaletteManager). Materials list (FUN_1436914c0): a material shows only if its availability
  type (VoxelClient → VoxelClientImpl+0x2e8 → +200 StandardDefinitionManager<VoxelBiomeDefinition>, record +0x30;
  default 2 = hidden) is 1 and its requirements pass (DAT_145352480 vtable +8). The admin/free-build check
  (game object vtable +0xb0) is hard-wired false in 0.589. Palette is empty today.
- Building edits go through VoxelConstruction 0xF4 (sub 1 ApplyDiffRequest = client-computed diff, 4
  ClientOwnershipRequest, 3 undo/redo, 2 selection, 5 off-grid paste; server: 0x16/0x17 ownership responses,
  0x0A ResponseObserved). Client requirements seen: VoxelCursorWithinValidClaim, ItemIsBuildingTool.
  Claim flag item = 105 'Build Site Flag'.
- Using the campfire (3894) casts ability 1098 at a target position; lightstone = 221. Both captured
  CasterInfoReply bodies exist. Server must allow casts of bag (consumable) items and spawn the prop / buff.
- PropBase 0xED (u8 sub) in 0.589: 2 prop data (FUN_142f08e10 → placement session FUN_142f1ab30, which needs
  the prop entity to exist already), 4 save reply, 6 pick-up reply, 8 delete reply, 10 list reply (7-u32
  entries), 0x0B volume reply, 0x10 undo/redo, 0x11 multi-prop preview, 0x15 edit text, 0x17 tints, 0x1C edit
  settings. Our login '145-PropBase' dump (sub 0x0A, model names, 'Freeport campfire') is the 0.695 layout.
- Prop spawn WORKS: PlayerUpdate 0x62 AddLightweightProp (handler FUN_142c44700, SpawnProxiedProp). Key base
  fields: +0x40 literal name / +0x58 name text id, +0x64 Models.txt id, +0x70 scale vec4, +0xd0 position
  (client: (pos*10 + origin doubles at +0x228) - zone origin, *0.1 → use zone origin + local pos), +0xe0
  rotation. Prop part: u64 owner, collision block (u8 type 1 + u32 flags), list, u32, u64, bool. Client then
  asks FullCharacterDataRequest (0x26/0x5E) for it; LightweightToFullProp is sub 0x63 (not built).
- World props/NPCs are lightweight entities: PlayerUpdate AddLightweightProp/Npc share base FUN_142b8cfc0
  (u16, i8, u64 guid, 2-bit transient id, 2 strings, u32, u32, i8, u32, vec4 pos +0x70, 3 strings, u32, u32,
  3×vec4, u32, u32, model info, ...). Prerequisite for campfires, crafting stations and claim flags.
- Crafting packets (Crafting:: namespace) have no readers in the catalogue yet; crafting happens at
  station props (RecipePacketPlayerOpenCraftingWindow).
- TerrainCell 0xEC: u16 base + u32 sub (dispatcher FUN_1430a8410). 7 CellBlockDamage = list{u64 cell,
  list{u16 block index in 40x40x40 cell, u8 health %}} + u64 transaction; transaction 0 → ignored (empty
  handler), else crack visuals only. 6 MaterialChangeInfo (u16, u32 sub, u16, list{u64 cell, 3×i32 min,
  3×i32 max, bitset}, u64) → material-change effect only. Geometry changes only arrive as 5 CellsUpdate
  (VoxelFarm::CellIdVersionFlagsAndData blobs) → mining/building need the server to produce VoxelFarm cell
  data (format not decoded; the repo's Parser/TerrainCel2Packer handled 0.695 captures).
- Crafting: RecipeBase 0x42, u16 base + u32 sub (client dispatcher FUN_142e9acd0): 2 start resp, 4 cancel
  resp, 5 complete resp, 7 craft-window enable resp (u32 result, u32 station, u64; 0 → OnOpenCraftingWindow),
  8 add recipe, 9 remove, 0x1b default learned list, 0x1e player recipe update, 0x24 zone crafting window
  (u32, bool). Client requests: 0x23 PlayerOpenCraftingWindow(u32 station) via Lua RecipeListSelectStation,
  sub 1 start request. Stations are props: item → prop def (item u35_39[3], PropDefs.txt) → crafting station
  (CraftingStations.txt, CraftingStationRecipeMappings.txt) — the prop→station link is server data.
  SendSelf crafting section: u32 known recipe ids, (u32,u64) list, u32 hidden ids, full recipe records.
- Command 0x1E sub ids are registered as (sub<<24 | 0x1E00) in FUN_14340fc20 (e.g. InteractRequest 9,
  ShowRecipeWindow 0x1B — ignored by the 0.589 client).
- CellsUpdate (0xEC/5) decoded: list{u64 cell id, u32 version (0 = revert to generated), list{u32 layer
  (0..3), u32 layer version, u8 flag, u32 len + blob}}, list{u64 transaction}, bool, bool. Handler
  VoxelCellBlockCacheImpl::HandleCellsUpdate FUN_143f52db0. Layer blob = u32 raw size + [A1 B2 C3 D4, BE u32
  size, zlib 1.2.3 stream] (codec FUN_1439f5950/5a80, encoder FUN_1439f5840). Decoded by
  CellGeometry::Decompress FUN_143f1cde0: 2× {u8 present, serialized VoxelFarm::CellMesh (u16-quantized
  vertex positions, u8 normals, u32 index/material arrays)}, then a 3-entry bounds block. So terrain updates
  are FINISHED MESHES, not voxels. Building sends ApplyDiffRequest (VoxelConstruction sub 1) with a voxel
  diff; the server is expected to apply it and send back meshed cells.
  => Real terrain editing needs a server-side voxel store + VoxelFarm-compatible mesher + the procedural
  base terrain (a cell update replaces the whole 40^3 cell). Multi-week reimplementation.
- Mining needs the server to produce terrain cell data (CellsUpdate 0xEC/5 = VoxelFarm cells; damage
  0xEC/7). The client only sends the cast + target.

## Traps (each cost hours)
1. Remote frame: positions about OTHER characters = local + zone origin / 10 = (+2558400, +2000, +2556450).
   Origin is in ZoneCoordinateInfo (25584000, 20000, 25564500, doubles).
2. Spawn movement version must be 1.
3. Temporary appearance field hides the body.
4. Synchronization must give every client the same server ms clock.
5. Duplicate sessions of one character make the client treat itself as a remote player (body vanishes, no
   collision). Replace stale sessions on enter.
6. ClientConfig.ini `Server=127.0.0.1:20042` is the only file changed in the game folder.
7. TransferItem's first location is the destination. Reading it the other way saved the pick at bag slot
   0xFFFFFFFF and hid it (now rehomed at load).

## Entity layout (ProxiedCharacter, partial)
- +0x1c0 position (v4), +0x1d0 rotation; +0x1456 bit 0x10 = temporary appearance active;
  +0x1f50 / +0x1fe0 appearance sets (list of slot/option, race at +0x80, gender at +0x84);
  +0x2138/+0x213c expected race/gender; +0x888 movement version; +0xcb4 lightweight flag.
- Local player entity: [[0x145352440] + 0x282c8].
- Entity lookup by transient id: FUN_142ee66e0(manager, &id).

## Game logs worth reading
`Z:\SteamLibrary\steamapps\common\Landmark\Logs`: AttachmentProcessor.log (model parts added/removed),
ActorFailures.log, ClientMountLog.txt, NetInfo.log, Landmark PlayClient (Test) (x64).log.

## Measurements
- Remote movement lag after the clock fix: ~100 ms (1–2 m at a run).
- Client sends ~10 position updates/s, gaps 21–211 ms; the server adds no jitter (MovementTiming log).

## Props, stations, crafting: working (2026-10-08, later)
- Tools: `re/decomp.py out.c <addr|callers:addr|strrefs:text|symrefs:Class::vftable|vslot:Class::vftable:N|scalar:0xOFF[:write]>`
  runs Ghidra headless via pyghidra in ~10 s (no MCP needed). Full export of every function: `re/decomp_all.py`
  on a project copy (`re/export_project`) → `re/decomp_all/part_NNNN.c` + `index.tsv` (grep these).
  Ghidra MCP: needs MCP_TIMEOUT=180000 (startup ~104 s), set in `.claude/settings.local.json`.
- Palette placement: client PropBase 0x12 (u32 prop id) → server spawns AddLightweightProp → client asks
  FullCharacterDataRequest → server LightweightToFullProp (PlayerUpdate 0x63 = LightweightToFullNpc base
  FUN_142b8f230 all empty + string, u32, byte array) → then PropBase 2 PropData with the +0x6c bool TRUE (else the
  session FUN_142f1ab30 is skipped). Save (sub 3): 6 vec4 = position, rotation quat, scale, old position.
  SaveReply sub 4: header, u32 MUST be 0, u64 guid.
- Held prop is valid only if the claim check FUN_142d15900 passes; it passes outright when SendSelf
  Unknown27 (client player +0x287) is true → WorldServer.json "FreeBuild". Otherwise needs claims (TODO).
- Interaction: server flags entities with PlayerUpdate 0x6B DynamicInteractionFlagging (bool, guid list) → client
  sends 0x69 InteractionInfoRequest (guid list, repeats every ~5 s) → server 0x6A InteractionInfoReply entries
  (u64 guid, u32 type, 4×u32 colour, u32, f32, f32 range, u8); type 3 = interactable, range must be set.
  Click → Command 0x1E u16 sub 9 InteractRequest (u64 guid) → RecipeBase 7 (u32 0, u32 station, u64) opens window.
  Clicking entities needs the game's cursor mode (mouselook swallows clicks; a right-click toggled it once).
- Prop→station: PropDefs NAME_STRING_ID == CraftingStations NAME, else model keyword (Sifter 28, Forge 48).
- Crafting window lists recipes from ClientRecipeManager RecipeData map (+0x78), filled ONLY from SendSelf recipe
  records (4th list). Each record needs a non-empty u32 list (= output item categories) or it gets no row.
  RecipeBase 1 start request: u32 recipe, u32 count, u64. Responses: 2 (bool, string), 5 (bool, u32, string).
- BUG FIXED: ClientUpdate ItemUpdate blocks are 5 u32 + 2 lists each; ours lacked the lists and the client
  (which requires exact consumption) dropped every update. Same "consume exactly" rule applies to most packets.
- Open: SendSelf recipe records add ~118 KB per login (bandwidth); component item ids missing from our 0.695
  item list (e.g. 65, 67); props are not persisted; placing doesn't consume the station item; claims.

## Terrain: cell mesh format (2026-10-08)
- CellsUpdate layer blob = u32 raw size + zlib(level 6) stream (codec FUN_1439f5840/5950) of:
  for m in 0..1: u8 present; if present CellMesh::Serialize (vtable+0x20) / Deserialize (+0x18, FUN_1430ae820):
    u32 n + n×(3×u16) positions (IntToFloat<u16,32767,SeamRangeScale>), u32 n + n×(3×u8) normals
    (IntToFloat<u8,127>), then 4 × (u32 n + n×u32) arrays (semantics TBD: indices/materials...).
  then bounds block (FUN_14360a360): u8 bitmask, per set bit (of 3, MSB first) 4×3 floats;
  then 2 × vec4 (AABB min, max).  Compress(lod<5, layer<4) is FUN_143f1bdb0 (client-local cache encoder).
- No diff-relay path exists: VoxelConstruction ResponseObserved (sub 10) is only an effect (op type, bounds),
  Response (7) carries result codes (0xc materials, 0xd props, 0xe permissions, 0x11 template voxel cap).
  Observers only get CellsUpdate meshes → the server must mesh. Client contains the whole VoxelFarm engine
  (Generator, ContinentMapHeightmapTerrain, contour.cpp) to port from.
- Cell ids: level<<60 | x<<36 | (y*64)<<18 | z (y low 6 bits always 0); level-L cell = 40·2^L world units (dm),
  world = zone origin + local*10. Live edits exist at levels 2..10 (finest 2 = 16 m cells).
- Manifest (0x0A) raw = per layer 0..3: u32 n + n×(u64 cell, u32 layer version, u8 flag), packed (A1B2C3D4+BE size+zlib)
  behind u32 size. Runtime update 0x0B ModifiedCellManifestUpdate: u64 cell, u32 code (1-4 set0 layer0-3, 5-8 set1),
  u32 version, u8 flag → client re-requests the cell. 0.589 has no ResetAllCells handler.
- ClientCellRequest (sub 1): u16 pad, u64, u32 n, n×(u64 cell, 4×u32 layer versions).
- VERIFIED: server-made cell geometry is accepted (client requests it at zone-in and Decompress returns 1; the generated
  cell is replaced).
- VERIFIED 2026-10-08: SERVER TERRAIN RENDERS (textured flat quad, testcell variant 10). Full CellMesh semantics (read
  from the client mesher FUN_14360db80 + live dumps of real meshes, re/hook_mesher.js):
  - positions u16×3 = IntToFloat: v = cell × 32767.5 × 0.8 + 32767.5 → cell = (v − 32767.5) / 26214 (0..1 = the cell,
    up to 1.25 for seams). Normals u8×3: n = (v − 127) / 127.
  - a0 per vertex = blend: (secondary material << 8) | weight; 0xff00 = none (all real terrain samples).
  - a1 per vertex = material id (secondary << 16 | primary); real ones seen 390, 292, 216.
  - a2 per vertex = flags, 0 (a triangle whose 3 corners are all 0xff is dropped by the mesher).
  - a3 = triangle index list (3 per triangle, vertices deduplicated by hash).
  - Geometry AABB (2 × vec4) is in CELL UNITS (0..1), not metres: client places it with FUN_14360d9d0
    local = (index × size − origin) × 0.1 + value × size × 0.1. Bounds block mask 0 is normal.
  - The two meshes per layer are cloned in order into the baked cell (FUN_143614b80); real far cells put water in
    medium 3 of the client's own 4-slot array. Layer 0 = LOD 0.
  - Client's own cache encoder: "OnGetBakedCell::Compress" FUN_1430abf40 (different container, same meshes).
- Live updates (ManifestUpdate 0x0B → client re-request → CellsUpdate) still DON'T re-render (traced 2026-10-08,
  re/hook_live.js): the re-request comes from the dirty list (+0x1f0 → +0x430, FUN_143f56980), which creates no scene
  entry (+0x448: bytes0-2 CacheResponsePending, byte3 ServerResponsePending, +8 CellType Blocks/Geometry), so
  HandleCellsUpdate (FUN_143f52db0) takes the "unrequested" path → FUN_143f56ca0(set=1): geometry goes into the set-1
  LRU cache (+0x2f0) and the cell is invalidated (FUN_143f4eb30) but nothing re-bakes the visible cell
  (FUN_143614b80 not called). Requested path differs only by FUN_143f59470 re-bake mark for set 0 (Blocks).
  Hypothesis: observers in the original got set-0 BLOCK data (voxels) and re-meshed locally. Next: decode the Blocks
  layer format (CellBlocks.cpp / BlockData.cpp) and try sending set 0 (ManifestUpdate code 1-4 vs 5-8).
  ClientCellUpdateResponse (sub 3: u64 cell, bool, string) = confirm/reject the editor's own pending edit (+0x510).
- Block events (2026-10-08, re/hook_blockev.js, hook_evdone.js):
  - CellBlockDamage 0xEC/7 = u16 pad, list{u64 cell, list{u16 block, u8 health %}}, u64 transaction. Parsed and queued
    as VoxelClientBlockEvent (FUN_143608230); per cell it holds VoxelCellMaterials + BoxPartialBitSet<40,40,40>.
  - CellsUpdate with exactly ONE cell links its transaction list to that cell (FUN_143608210).
  - ZoneProcessingComplete 0xEC/0xC = u16 pad, u64 transaction → completes the event (FUN_143608490 →
    FUN_1435b3d60 → observers FUN_1435b2e10: effects + initiating ability). Verified all three steps fire
    (dev cmd livecell / blockdmg) — but NO geometry change: completion only notifies observers.
  - ResponseObserved 0xF4/10 = sound/particle effect only (FUN_1430bbd60), confirmed.
  - CellsUpdate always builds CellGeometry (FUN_143f1bb00); CellGeometry vt+0x10 returns 0 → cache set 1. Only set-0
    data (CellBlocks, vt+0x10 non-null) gets the re-bake mark FUN_143f59470. Scene entries (+0x448) are transient
    (only while the scene processes a cell); a baked cell has none, so pushes go "unrequested".
  - CellBlocks format: DecompressBlockData FUN_143f86240 (A1B2C3D4 zlib) → UnSerializeBlockData FUN_143f87bf0
    (palette HashMap<u8,u16>, u8/u16 arrays; 539 lines, not decoded yet).
  - Building = VoxelConstruction ApplyDiffRequest 0xF4/1 (client → server, the editor applies locally), answer
    Response 0xF4/7 (codes 0xc materials, 0xd props, 0xe permissions, 0x11 voxel cap).
  - Real live-server manifest (Resources/PacketDumps/TerrainCellManifest.bin): layer0 38190 cells, layer1 11162,
    layer2 125246 (levels 2-4, flag 0), layer3 0.
  - OPEN: what makes an observer's already-baked cell re-scene. Next candidates: ClientConstructionManager
    ::OnPreCommitCellBlocks (part_0087 ~7405), who creates CellBlocks objects, PendingRequestTimeout.
- Mining with the pick = ability cast: AbilityBase 0x27 sub 1, ability id 1018 (0x3FA) + target point → needs the
  ability pipeline before any VoxelConstruction traffic appears.
- Testing gotcha: relog.sh used to leave old clients running (3 at once) and report the OLDEST window → screenshots of a
  client that never got the test cell. Fixed: relog.sh now stops running clients (exact PIDs) first.
- OPEN: live re-requests land on the "unrequested" path (pending bytes per layer/set are 0) and are not re-baked.
- Contour input (VoxelCellMaterials::SetMaterials(contour)): 44³ grid (40 + 2 apron), u8 flags (bit0 solid) + u16 material.
- Tools: re/cell_test.py; dev 'testcell L x y z [height] [extent] [variant]'; re/run_frida_wait.py attaches at game start.

## Claims (2026-10-08)
- ClaimsBase 0xF9: u16 + u8 sub; client dispatcher FUN_142d1ff50 switches on sub-1 (AOIUpdate is sub 4!).
- AOIUpdate (reader FUN_142d00720): bool fullUpdate (kills all client claims first), list<ClaimRecord> adds,
  list<u64> removes. Only requires no read error (not exact consumption).
- ClaimRecord (FUN_142d01c40): u64 claim id, u64 OWNER guid, u64, string name, u64, 3 strings, u32,
  3×f64 min + 3×f64 max (world units, dm), u32×3, list, list, list(FUN_142d022d0: u64, string, u32, u8), u32, u64,
  bool, u32, u32, list(03890), 5 strings, u64, struct(cffe50: u64,u32,u8,5×u32,u8,u32,string,list(u32,string)),
  u64, string, list(03e70), 4×u32, u32, u32, list(03b50). re/claim_test.py builds one.
- VERIFIED: with owner = player guid the claim check (FUN_142d15900) passes and the build toolbar appears.
- Prop palette counts/costs come from PROP RECIPES (PropDefs PROP_RECIPE_ID → PropRecipeComponentMaps →
  RecipeComponents), e.g. Mistletoe = 20 Cut Plain Wood. Server now checks/consumes those (FreeBuild off).
- Full client decompile export done: re/decomp_all/part_0000..0234.c (+ index.tsv), 642 MB — grep it.
- Placement flow VERIFIED end to end (claim 3):
  - Right-click Build Site Flag (item 105) -> client sends Command 0x1E sub 0x4C CommandPacketInteractionItem (u64 item guid).
  - Server answers Command sub 0x4D InteractionClaim, body u32 2 (client FUN_143311590; other values = error code)
    -> client switches to controller 0x1C ControllerClaimPlacement (top-down view, box preview).
  - Client box: ToolClaimPlacement default "70 100 70" m (overridable by a client setting, hash 0x1fb1a684), centred on the
    requested point -> server claim = centre +/- (350, 500, 350) world units.
  - Mouse 1 -> PlaceClaimRequest 0x1A (f64 centre GLOBAL, f32×4 flag point local, u64) -> Response sub 6 result 1 ->
    confirm dialog -> 0x1B Confirm -> Complete sub 7 result 2 + record. Flag consumed.
  - Global = local × 10 + origin (client world +0x5a08; here 25584000, 20000, 25564500).
  - UI Lua sends ClaimManagmentRequest 0x1C (empty); reply ClaimManagmentData sub 8: u64 claim id, list of
    {bool, u64, string, bool, u64, string, u32, u32}.
- ItemAdd (0x29/2) blob starts with bool "definition follows" — send false, then the item: live adds now show.
- InventoryPacketActivateItem 0x2D sub 0x15 (u64 guid) = right-click a prop item -> Props.BeginPlacement(paid by item).
- Client repeats prop Save ~2/s while the gizmo is open; server persists only when changed.
- GamePacketWriter has no ushort Write overload: (ushort) silently writes 4 bytes. Use WriteUShort.
- Frida: killing run_frida_all.py (timeout) while hooks are attached crashed the game twice — let it run or detach cleanly.
- Prop moves after placement: relayed to others 1 s after they settle (despawn + spawn), untested with 2 clients.
- Open: member permissions; upkeep timer; claim visuals in
  AOI for others verified only by code.

## SOE compression (2026-10-08)
- Client UdpCompressionHandler (FUN_143952200 inflate / FUN_1439522d0 deflate): u8 flag (1 = compressed) + the
  client's block codec (A1 B2 C3 D4, BE u32 raw size, zlib; FUN_1439f5840/5950), compresses only bodies > 0x17.
  Our old bare-zlib body broke login; fixed. With compression every non-session packet (incl. Ack) carries the flag.
- Login works compressed, BUT zone payloads are RC4 ciphertext (gateway ToggleEncryption(true) at LoginRequest), so
  zlib never wins (829197 bytes in → 829197 out). Left OFF. Bandwidth must be cut at the game layer.

## Live terrain + mining (2026-10-08) — WORKING
- The answer to live updates: a CellsUpdate entry with cell VERSION 0 is BLOCK data (FUN_143f52db0 → FUN_143f52470),
  not geometry. The client builds CellBlocks, stores them in cache set 0, queues (+0x330) and delivers to the scene
  (FUN_143f57680 → FUN_143fc9c00), which re-meshes. Unrequested pushes work live; at zone-in they arrive requested
  (re-bake mark FUN_143f59470). Server never needs to mesh edits.
- Block layer blob = client codec (A1B2C3D4, BE size, zlib) of SerializeBlockData format 2 (FUN_143f86db0 writer /
  FUN_143f87bf0 reader): u32 2, 8000-byte occupancy bitmask (MSB first), u32 n + flags per occupied voxel, bool
  palette (<256 materials), palette: u32 n + u16 materials, u32 n + u8 1-based indices (index 1 implied by flag 0x80);
  else u32 n + u16 materials; then u32 n + 3-byte offsets (flag 0x20 = centre 0.5 → byte 128, not stored);
  flag 0x40 = material 0 (air). Voxel flags bit0 material, bit1 offset. 40³ voxels, index = y + 40·(x + 40·z).
- Layers 0-3 composite into layer 4 OVER THE CLIENT'S OWN GENERATED TERRAIN: an overlay with air voxels carves,
  solid voxels add, empty voxels leave the generated terrain. So the server only stores what players changed.
- Server: Game/Terrain/BlockData.cs (serializer + save/load), TerrainEdits.cs (level-2 overlays, sphere edits,
  persistence in bin/.../TerrainEdits/*.cell, manifest entries, zone-in serving, interest-range broadcast: ≤256 m
  full voxels, beyond only a 21-byte ManifestUpdate). Dev: blockcell, livecell, blockdmg.
- Mining: ability 1018 (every pick) → Abilities.Mine: target must be ≤12 m from the player and not in someone else's
  claim; carves a sphere (radius 1.0-1.5 m by pick tier); yield is Dirt (1 per 4 voxels) until the generator is
  ported (the cast carries no material). The pick aims along the camera centre line, not the mouse cursor.
- Open: smooth hole surfaces (air voxels with centre offsets look blocky); real material yields (needs the terrain
  generator: ContinentMapHeightmapTerrain + VoxelMiningResources/VoxelResourceGeology); building — ApplyDiffRequest
  0xF4/1 (diff object, HashListMap<u16,int> material usage, CellIdAndPermanentVersion[], ConstructionArea[]) is sent
  by the StampBrush tool (id 4); the build palette's Materials tab is empty (ClientCategoryPaletteManager source not
  found yet), so placement couldn't be triggered.
- SOE compression note in its own section; build-mode ability 186 now answered.

## Chat + multiplayer check (2026-10-08)
- PacketChat 0x13 sub 1 (client writer FUN_1432a93e0): after the sub: u16, u16 channel, u64 from, u64 to,
  name {u32,u32,u32,string first,string last} ×2, string message, vec4 position, then trailing fields (passed through).
  Server (Game/Chat.cs) fills the real sender (guid + name), relays say (channel 0) within 100 m, other channels to all.
  Chat.System(player, text) = a "[Server] says:" line (used when digging in someone else's claim is refused).
- Two clients at once (relog.sh: KEEP=1 bash relog.sh test-account2.txt): verified chat both ways, the other player's
  nameplate, both workshops (props), claim protection (LawTest refused inside MrLawKing's claim) and the dug terrain.
- Logging: axe primary ability = 1020 (CasterInfo asked on equip; ToolActivate 7). Trees are client-generated flora,
  so the server can't yet validate a hit → not rewarded yet (needs the flora/generator port).

## World data on disk + material yields (2026-10-09)
- The client's world generator data lives in the install, readable by the server (never modified):
  CommonData/Voxel/: surfaceCache/ContinentSurfaceCache{N}.csc (u8 1, 5×u32 incl. 501×501, then u16 grid of record
  indices + 168 KB records — not decoded), veinData/ContinentVeinData{N}.vdt (ore veins, 1.8 GB total), ocean/*.ocm,
  caves/ContinentCavePlacements[N]_*.cpa, raw/ContinentRawHeightMap215.raw, VoxelWorldSettings.xml (biomes,
  height layers, tile sets), materials/textures/Continent_{BiomeIds,Shape,HeightLayerIds}_{N}.bmp.
- Our zone = continent 406 (client log "ContinentId recieved 406"; SendZoneDetails names OpenBetaContinent_406 and the
  three bitmaps; continent offset = our zone origin, size 8000).
- Biome bitmap: 1250×1250, BLUE channel = VoxelBiomes id (406: 54 OceanSurface, 50 OldGrowthForestSurface,
  47 VolcanicSurface); zone origin at the image CENTRE, 6.4 m/pixel, z down the image (verified: player on a shore).
- Game/Terrain/TerrainMaterials.cs: biome → VoxelBiomeMaterials → procedural material names (TextManager) →
  Dirt 44 / Sand 45 / Stone 46 / Ice 76 by keyword; deeper than 4 m below the swinger → Stone.
  Config: WorldServer.json GameDirectory (install path), Continent (406).
- Verified: shallow dig on the coast → Sand, deep → Stone. Next: ores from veinData (.vdt) and the .csc surface cache.
- Vein data (ContinentVeinData406.vdt, 8.3 MB) partly decoded: header doubles (27200, 37280), u32 36884, u32 2 depth
  bands; band boxes in global dm (y 27500-40000 and 9000-27500, x/z covering the continent from 25544000/25524500),
  band geology lists (VoxelResourceGeology ids: band1 41/62/65, band2 41/47/50/54/62); then groups (layer, biome,
  1, n) + n sorted level-2 cell ids: biome 54 → 211563 cells, 54 (layer 1) → 3658, 50 → 187296, 47 → 44594; then from
  offset 3577416 a different per-geology structure (not decoded). Geology ids name their ores in ResourceVeins
  DESCRIPTION ("G2 Aquamarine", "Aqua/Iron/Aquamarine", "Silver/Topaz (L)"); materials per geology in
  VoxelResourceGeology (MATERIAL1/2 + pct). The client ships NO ore-material→item table (it was server-side), so the
  plan is: vein geology description words → item names (Iron Ore 36, Copper Ore 34, Tin Ore 35, gems...).
- Text ids resolve with Jenkins lookup2("Global.Text.<id>") over en_us_data.dat (re: scratchpad textid.py; server:
  TextManager).
- ORE VEINS DONE (Game/Terrain/OreVeins.cs): .vdt from offset 460 = sections {u32 groups, groups × (layer, geology,
  1, n, n×u64 sorted level-2 cell ids)} (406: 5 geologies, 1,011,573 cells); ends where the next section is invalid.
  Geology (VoxelResourceGeology) MATERIAL1-3 + shares → material ore names from ResourceVeins descriptions (single-
  material rows first, then 2-part "X/Y") → items (Copper 34, Tin 35, Iron 36, Silver 37, Gold 39, gems...; guesses:
  Tungsten→Druidium 38, Cobalt→Indicite 40, Tourmaline→Tormeral 52, Aquamarine→Skystone 53, Topaz→Sunstone 54,
  Amethyst→Amethyn 55). Swing in a vein cell: 35% ore (a quarter of the normal yield count), else surface material.
  CORRECTION (2026-10-09): in 0.589 ids 38, 40-43, 52-55 and 60 are building BLOCKS (Druidium/Indicite/Rubicite/
  Mithril/Etherium/Tormeral/Skystone/Sunstone/Amethyn/Moonstone Block), not ores. 0.589 raw resources (dev command
  `finditem`): ores Copper 34, Tin 35, Iron 36, Silver 37, Gold 39; Dirt 44, Sand 45, Stone 46, Marble 47,
  Amaranthine 48, Obsidian 49, Alabaster 50; gems Agate 51, Emerald 56, Sapphire 57, Ruby 58, Diamond 59; woods
  Plain 71, Burled 72, Striped 73; Ice 76; Tree Fiber 921, Tree Sap 922; Lumicite 920/923-929/981. Higher metals
  exist only as Smelted (112 Druidium = Tin+Iron alloy, 114-117) and Blocks. Vein ores without a raw item now give
  nothing (falls back to the ground material).

## Terrain heights + trees (2026-10-09)
- TREES ARE SERVER ENTITIES: the client's PropDestruction looks a tree up by ProxiedCharacter guid (FUN_1430bd...,
  "Unable to find the ProxiedCharacter associated with the given GUID"), builds "<model>_Destroyed". No tree data in
  any client table (VoxelFloras = grass/rocks decoration only). Tree models: Models.txt "Biome_*_Trees_*" (133).
- Spawned as PlayerUpdateAddLightweightProp with a tree model: the axe outlines it (harvest target) and cast 1020's
  tail carries the targeted entity guid at +36 (u64). Swinging only fires with the cursor over a harvestable entity.
- GROUND HEIGHT: ContinentMapHeightmapTerrain vtable +0x58 = FUN_143fc3e00(this, x, z, scale, p5, p6 out valid,
  p7 out u32 code, p8 out u16, int lod, double p10 = DBL_MAX) → ground y, all in GLOBAL decimetres (local = (g -
  origin)/10). Called from getContourDataRegular FUN_143fc3730 with scale 40, lod 2. Verified exact vs player Y.
  Heightmap tiles: CommonData/Voxel/materials/Voxel_HeightMaps_*.set = 18 tiles of (SET_SIZE/4)² u16 (not ported).
- Baked with tools/terrain (run_heightbake.py: Interceptor.replace + sampling inside the worker's own calls, 4000²
  every 2 m in 51 s; pack_heights.py → gameData/TerrainHeights406.bin, 27 MB). y 736..1727 m; ocean floor below
  ~1000 m (sea level ~1000); biome 50 land 1005-1575, 47 1018-1662. The p7 "code" isn't a VoxelMaterials id.
- Game/Trees.cs: 64 m regions, 8×8 jittered candidates, seeded per region; OldGrowth (50) jagged/gnarled/diamond
  pines 16%, Volcanic (47) Lavastorm trees 5%, else birch/oak 3%; skip y < 1002, slope > 0.7, claims. View 160 m,
  drop 224 m (hysteresis), checked once a second per player. 5 hits (8 for large) × 2-4 wood (Plain; large →
  Burled; volcanic → Striped), 15% Tree Sap on felling; felled → RemovePlayer to players with that region; regrow
  30 min; felled list saved to felled_trees.txt. Verified in game: forest renders on the ground, chop ×5 fells it,
  wood lands in the resource bag.
- Dev: `tree [dx dz]` (stand by the nearest tree), `finditem <text>`.
- Open: the client's own "destroyed"/falling animation (cPlayerUpdatePacketIdDestroyed — find opcode/format),
  stumps, reaping (plants → Tree Fiber?), tree collision.
- PlayerUpdate 0x2D Destroyed (decompiler case labels in PlayerUpdate_Dispatch are shifted by one: "case 0x2c" is
  sub 0x2D): body u64 guid, u32 A, u32 B, u32 C, bool keep (false → entity vtable+0x178(1) = destroyed);
  then vtable+0x208(entity, B, &A). Reader rejects trailing bytes. Sent with zeros: no visible change yet (untested
  which trunk). Felled trees are just removed for now. Dev: `send 26002D <guid LE> 00000000 00000000 00000000 00`.

## Gathering: plants, tools, action bar (2026-10-09)
- Tool abilities (finditem shows them): picks 1018 (Adventurer's/Charmed/Goliath/Whirlwind/Farseer), 136 Hewer's,
  138 Miner's, 140 Foreman's, 142 Overseer's, 169(+173) Settler's/Explorer's/Trailblazer's Pickaxe; axes 1020
  (Adventurer's Logging/Charmed/...), 176 (Logger's/Woodcutter's/Treesplitter's/Lumberjack's); sickles 153 Weaver's/
  Adventurer's (1761), 155 Gatherer's, 156 Alchemist's, 157 Shaman's.
- Item text (descriptions): Tree Fiber 921 "found on all trees"; all Cloth and Lumicite are Replicator crafts
  (fiber/cut gems); the sickle "Gathers once from all size plants" → the gathered things are the placeable plant
  items: Amberleaf 735, Cotton 736 (Large 2803, Perfect 2804), Earthblood 737, Sunblossom 738 (2811, 2812),
  Vilebloom 739 (2813, 2814), Flax 740, Jute 741, Silkfall 742, Gloombulb 743, ...
- Plant models: Biome_Generic_Plants_<Amberleaf|Cotton|Earthblood|Sunblossom|Vilebloom|Deathweave>_<Common|
  Uncommon|Rare|Loot>01 (Models 464-499). Spawned like trees (Game/Gathering.cs, node index 64-127 in a region).
  They get NO hover outline and the sickle cast carries no target guid (no pick collision), so a swing picks the
  plant within 3 m of the cast's aim point (camera-centre ray). Verified: Cotton Plant gathered, plant removed.
- Action bar: right-clicking a bag tool sends ActionManager 0x9A sub 2 {u32 bar, u32 slot, u32 type (0 empty,
  1 item def, 2 item guid), u64 value}, the same layout as UpdateSlot (sub 5). Server validates, saves
  (character_action_slot) and echoes UpdateSlot; SendSelf hotbars overlay saved slots. Verified across relog.
- Harness: camera pitch/yaw works with right button held + many small `rel` steps (one big rel does nothing).

## Building investigation (2026-10-09) — not working yet, findings so far
- The build palette that matters is the 4th tray tab (cubes icon), shown on your own build site: tools (Add [1],
  Remove [2], Select [3], Paint [4], Smooth [5], Fill [6], Heal [7], Line [8], [9]), shapes, and materials
  ("Terrain (129)": Dirt 44, Sand 22, Stone 61, Snow 2) with ∞ counts. The 3rd tab ("materials", ElementTray
  MaterialList) stays empty: it binds GameData.Character.Materials, which the client only fills after
  Voxel.ElementPanelEnable(true) (Lua; called by the MaterialMenu HUD widget, never opened here). Quantities for
  that list come from currencies: Currency.txt TYPE 2 = building materials (68 Plain Wood, 69 Burled, 70 Striped,
  71 Dirt, 72 Sand, 73 Stone, 74 Marble ... 87 Etherium, 88 Snow, 89 Lava, 90 Ruby, 91 Amethyst); server sends
  PlayerUpdate 0x49 cPlayerUpdateCurrency {u64 guid, u32 n, n × {u8 currencyId, u32 qty}} (verified received:
  FUN_142f33fd0) and SendSelf carries a currency list too. VoxelMaterials CURRENCY_ID/ITEM_ID tie materials to
  currencies/items (Cut Plain Wood 2000 → 68, Dirt 44 → 71, Stone 46 → 73, Smelted Iron 110 → 80 ...).
- Requirements: ItemIsBuildingTool = ItemHasCategoryGroup 38 (Requirements.txt 16), VoxelCursorWithinValidClaim
  (3). Neither is evaluated during a click today (requirement evaluator FUN_143248b00 hooked: no calls).
- WorldEditing 0x010C (cPacketIdWorldEditingBase) = cursor broadcasts for other players' views: sub 0 Clear,
  1 BoxUpdate ({u32 ?, vec4 min, vec4 max, string "stamp_box", u32, u64}; a 0.4 m single-voxel box), 3 Shape,
  4 Selection, 5 PreviewSetup, 6 PreviewUpdate. Sent on clicking with Add selected; the cursor itself is not seen
  in screenshots and ClientConstructionManager (OnPreCommitCellBlocks FUN_1430b8f90 = vtable slot 0) never runs,
  so no ApplyDiffRequest (0xF4/1) is sent. ToolBrush vtable 0x144707498 (inner brush at +0x20, e.g. vt
  0x14471aa10); ToolBrushNetworkProxy exists. Input "Place" handled in ToolBrush slot 2 (FUN_1436293f0).
- Harness pitfalls: in build mode number keys pick tools (the hotbar is greyed), Esc opens the main menu, the
  wrench (1890,25) toggles the tray like Tab, F1-F5 are build HUD modes.
- Next: find what commits a brush stroke (hook VoxelFarm edit/apply functions on click; check whether the brush
  needs a held building-tool item in category group 38 — item categories come from the server's item
  definitions, so a tool item may need a category the 0.695 dump lacks).

## Claims: management, upkeep, permissions (2026-10-09)
- ClaimsBase 0xF9 subs (client registrations): 1 SendAllClaimBasicInfo, 2 UpdateClaimBasicInfo, 4 UpdateClaimsAOI,
  5 UpdateClaimInfo, 6 PlaceClaimResponse, 7 PlaceClaimComplete, 8 ClaimManagmentData, 9 EditClaimItemData,
  10 UpkeepManagmentData, 11 UpkeepReminder, 12 UpkeepBankAddFundsResponse, 13 SendInventoryDetails, ..., 0x30
  DeleteClaimItemReply; client requests 0x19..0x40 (0x1C RequestClaimManagmentData, 0x1E RequestEditClaimItemData
  {u64, u8}, 0x20 RequestUpkeepManagmentData, 0x2A SetItemName, 0x2B SetItemDescription, 0x2D SetItemBasicPermissions
  {u64 id, u32 mask, u64, bool}, 0x2E SetItemIndividualPermission {u64 id, u8, string name, u8, u32 mask, bool},
  0x2F DeleteClaimItem {u64 id, bool, bool}, 0x31 RecoverFlag, 0x33/0x34 RevertTerrain, 0x38+ snapshots).
- ClaimManagmentData (8): u64 UpkeepSecondsLeftAtRate + list ClientClaimManagmentItem {bool IsGroup, u64 id,
  string Name, bool HasOverridingSettings, u64 ParentGroup, string CategoryName, u32, u32}.
- UpkeepManagmentData (10): u64 expiry (Unix s), u32 RootClaimsOwned, AttachedClaimsOwned, PlayerMaxRootClaims,
  PlayerMaxAttachedClaims, MaxRootClaimsAllowed, MaxAttachedClaimsAllowed, warning, MaxDaysUpkeep, 3 more u32.
- DeleteClaimItemReply (0x30): u64 id, u32 result (≠2 refreshes the window). Server: remove site, revert overlay
  terrain in the box, return the owner's props there, give back the flag. Verified.
- Permissions: masks No Touching 0, Play Around 0x003, Pillage & Plunder 0x007, Get Inspired 0x107, Let's
  Decorate! 0x10F, Ultimate Power 0x11F. Client FUN_143b5b480: owner → 0x11F; else per-player map, per-guild map,
  else claim +0x2d0 (default) / +0x2d4 / +0x2d8. Claim record after the bounds: 3 u32 default masks, then lists
  {u64 guid, u32 mask} players, guilds, then a third list. FUN_143485c00 checks (mask & required) with result 0 ok.
- EditClaimItemData (9) = u64 id, bool, full claim record; applied only if id == client's selected (+0x400), set
  when the client sends 0x1E (FUN_142d32b70). The window doesn't send 0x1E here yet, so the Special Access list
  doesn't refresh; requests carry claim id 0 (server uses the player's own site).
- Upkeep: LastOnline set on enter/leave; sites of owners away > 30 days removed (hourly).
- PropBase PickUp 5 {u64 guid, u8} / Delete 7 {u64}: replies 6/8 {u64 player, bool ok, u32 error (10 = no claim
  permission, 3 inventory full, 6 invalid location), u64 guid}.

## Health, death, respawn (2026-10-09)
- No fall damage in 0.589 (40 m drop: no packet, full health).
- Death window: EncounterBase 0x2C body {u16 type, u32, u32 (stored at client +0x32f28)}; 0x7D shows
  RespawnHandler ("You have died."), 0x7E hides. Respawn types (RespawnHandler.lua): SELF 10 ("Revive Here", costs
  Lumen item 1579 × 15 — client IsReviveSelfEnabled counts the item, GetReviveItemRequiredCount = 15; with fewer the
  button silently does nothing), GRAVEYARD/HUB 11/12 (shown when respawn locations exist: ClientUpdate 0x20
  RespawnLocations, list of RespawnLocation records — not decoded). Client request: PlayerUpdate 0x3B
  {u32 type, 9 more bytes}. PlayerUpdate 0x3C RespawnReply exists (unused).
- ClientUpdate 0x01 Hitpoints {u32 cur, u32 max}: posts a health event (combat text); does NOT move the HUD bar.
  PlayerUpdate 0x03 UpdateHitpoints is the same for other entities.
- HUD health bar = Resources (ResourcesBase 0xC4: 1 SetCharacterResources, 2 SetCharacterResource, 3
  UpdateCharacterResource; header u16 op, u32 sequence (must increase), u8 sub, u64 guid; record FUN_142b7f0c0 =
  u32 + map, 10 u32, u32, u64, u64, u64, u64, 3 bools, u32). ResourceTypes: Health 1, Mana 2, Stamina 10, Armor 11,
  RevivePoints 12, Shield 14, Repairable 15, Stagger 16. Resources.txt rows give INITIAL/MAX/REGEN. Open.
- DONE: health bar = ResourcesBase 0xC4 sub 2 SetCharacterResource. Body after the u16 opcode: u32 sequence (per
  player, increasing), u8 sub, u64 guid, record: u32 resource id (4), u32 type (1 Health), u32 variable count (0 →
  max from Resources.txt MAX_VALUE 900; entries {u32 key, u32, u32, u32}), 10 u32 (current value first; read order
  [0],[1],[3],[4],[6],[7],[5],[8],[9],[2]), u32, 4 × u64, 3 bools, u32. A wrong count CRASHES the client.
  Regen 50/s server-side. Verified in game.

## Creatures + melee combat (2026-10-09)
- No creature definitions in the client (AbilityEx.txt empty; no NPC tables): server data. Models exist:
  Char_Biped_Kobold_{Melee 1569/1570, Caster 1571/1572, Runt 1573/1574, Boss 1575}, Quadruped_Wolf 1589,
  GiantBiped IronGolem 1610, MondoBiped GolemIron 224.
- PlayerUpdate 0x59 AddLightweightNpc = lightweight base only (FUN_142b8cfc0, spawn FUN_142c43fe0). Works with our
  prop packet minus the prop part. NPCs aren't harvest targets: tool casts on them don't fire.
- Weapon (e.g. Electrified Assault Axe 3027): client asks CasterInfo for combo abilities 760, 761, 816, 1181, 1182
  (not on the item). Unanswered → no attacks. Answered → casts arrive with an aim point; the tail's target guid is
  the player's own (no entity targeting) → server resolves hits by aim point.
- NPC hit points: PlayerUpdate wire 26 00 03 {u64 guid, u32 cur, u32 max, u32}. Movement: Position 0x7F +
  full type-0 body (template captured from our client: pos p2, rot p5, vel p5, 8 × p2, p1, p5, tail 05 10 00).
- Open: creature attack animations/effects, real ability animations for weapons (AnimationId), creature names
  over heads, loot pinatas, creature health bar UI, server-side aggro on other players' sessions.

## Friends (2026-10-09)
- FriendBase 0x4D, u8 sub (client ClientFriendsDataManager dispatcher, part_0108 ~36600): 1 List {u32 n, FriendData×n},
  2 Online {string name, u64 guid, bool}, 3 Offline {u64 guid}, 4 Message, 5 UpdatePositions, 6 Add {FriendData},
  7 Remove {u64 guid}, 8 Rename, 9 Status, 10 profile.
- FriendData (FUN_14348c040): name {3×u32, first, last}, u64 guid, bool online; offline → u64 last online;
  online → 4×u32, bool (true skips 3×u32), u32, bool, 5×u32; then a string.
- Requests are Command 0x1E subs: 0x10 AddFriendRequest {string name, u64 guid} ("/friend add Name" sends the name;
  adding from a player menu sends "" + guid), 0x12 RemoveFriendRequest {name struct} (FUN_14351df90, UI only),
  0x13/0x14 Confirm request/response (unused: lists are one-way), 0x18 FriendsPositionRequest.
- The client prints "X has been added as a friend." itself on sub 6 — no server chat line needed.
- Client sends FriendBase sub 0x0B on entry {u32 1, 3 strings} (window settings) — logged and ignored.
- Server: Game/Friends.cs + character_friend table (migration CharacterFriends). List once on entry; online/offline
  notes go only to players who list that character (one DB index lookup). Verified with two clients: add, list,
  online dot, "X is now online/offline" (offline after the 180 s heartbeat timeout, now pruned each second). Open: remove (UI clicks on the list didn't register in tests), friend positions / teleport to friend.

## Groups (2026-10-09) — working (commit after dc3e727)
- GroupsBase 0x2B: u8 sub, u32 result, u32, then the packet's fields (client header reader FUN_142d45590,
  dispatcher FUN_142d4c840: subs 1-0x19, 0x24). Client "/invite Name" sends sub 1 Invite: u32 1, u32 0, u32 0,
  {u64, u32 raid?, inviter {u64 guid, name, u8}, target {u64, name, u8}, u32} (FUN_142d3eb10, player FUN_142d3fdc0).
- Server sub 1 with result 1 shows the invite to the target (FUN_142d4da90); result 4 = other; else error text.
- Invite popup answers via Notifications 0x0001 (not GroupsBase): sub 2 {u32 1, u32 popup id, u32 type 2, ...}
  on open, sub 3 {u32 1, u32 popup id, u32 choice} on click; Accept = 4.
- AddGroup 0x11 {u32, group}: group = {u32 id, u64 leader, u8}, u8, string, u32, u32 n + n×{u64 guid, member},
  u32, bool. Member (FUN_142d3ebe0) = player {u64, name, u8}, 5×u32, 3×u32, 2 bools, u32, u64, 11×u32, u64, 2×u32,
  bool, u32 n. AddMember 0x12 {u32, u32 group id, member}.
- RemoveMember 0x16 {u32, u32 reason, u64 guid (+u64 by, bool if reason 4)}: reasons 0 ok,1 err,2 invalid,3 left,
  4 kicked,5 faction,6 swapped (strings UI.GroupPacketRemoveMember*). RemoveGroup 0x15 {u32, u32 reason, u32 id}:
  3 too small, 4 disbanded. SetGroupOwner 8 {u32 state 1 req/2 applied/3 err, u32, u64 guid, u32}.
- Client commands: /party invite|leave|disband|kick Name (kick = sub 5 {u32,u32,u64 kicker,u64 kicked,u8});
  /invite Name. Promote is menu-only (right-click menus can't be driven by injected input) — untested.
- Party chat = chat channel 8, relayed to group members only. Verified with two clients: popup, Party window,
  crown, markers, leave, kick, disband, party chat.

## Guilds (2026-10-09) — working
- Guild window: N key (user, 2026-10-09; not yet checked against our packets). Also shown: Guild section in the Friends window (K), Guild chat tab (channel 5), MOTD.
  Commands (client table FUN_142ae3120 registrations): /guild_debug create 'Name' (quotes required) | accept invite X
  | decline invite X | disband, /guildinvite Name, /guildmotd text, /guildquit, /guildkick, /guildremove.
- GuildBase 0x83 + u16 sub. Client: 1 Create {string}, 4 Invite {u64 guild, u64, string name}, 5 InviteReply
  {u64, u8 accept}, 0x0B Motd {u64 guild, string, …}, 0x0D Quit {u64 guild}.
- Server: 0x13 GuildData {guild, names} — applied only if consumed exactly. guild = core {u64 id, name, 6 strings,
  u64 leader, string, u64, 3×u32, motd, string, u64}, members u32 n × {string guid-as-decimal (!), u64 guid, u32
  rank, u64, string, u8 online, u64}, u32 n invitations, u32 n × {u32 key, log}, u64, u32 n × {…}, bool,
  {bool, string, u32 n × string}; names u32 n × {u64, string}.
- 0x0E UpdateRank {u64 guild, rank {u32 level, name, u32, string, u32, u32 n × permission, u32, u32, u32 n × …,
  string, u32 id}, u32 action}. Permission names (client checks by name): DisbandGuild CreateGuildInvite
  ReplyGuildPetition RevokeGuildInvite ViewGuildInvites TransferGuildLeadership ChangeMemberRank KickFromGuild
  ViewGuildLog ModifyGuildLog ModifyRanks UpdateMotd UpdateDesc RenameGuild.
- 0x0B Motd {u64, motd, by, u64}; 0x15 AddMember {u64 guild, member, string, u64, u64, string};
  0x16 RemoveMember {u64 guild, string, u64, string}; 0x18 Online {u64 guild, u64 member, u8}; 2 Disband {u64}.
- Open: the GAQGuildInvite popup doesn't show for our 0x17 (accept via slash works); /guildkick; rank editing.

## Smooth mining (2026-10-09) — in progress
- Offset encoding (client FUN_142a0d650 constants -1, 2, 85, 0.5): byte = floor((clamp(u,-1,2)+1)*85+0.5), u = vertex
  position in voxel units from the voxel's corner (0.5 centre ≈ 128). Byte order = (x, y, z) in our axes (server
  index y + 40(x + 40z)): verified on the client's own POI blocks (Cache/2/Voxel/406/VoxPoiBlocksW_000.pack, 26
  A1B2C3D4 blobs; decoder scratchpad poiblocks.py).
- POI ground truth: both air (material 0) and solid voxels next to a surface carry vertices ON the surface (air
  vertex leans toward the solid neighbour, solid toward the air one, up to a voxel beyond its own).
- Carving now skips voxels above the generated ground (baked heights): the grass around a hole keeps its
  generated look (it changed because explicit air voxels above ground altered the rim's material blend).
- Open: holes still show steps. Offset-only voxels (flag 2, no material) seem ignored by the client, so the
  generated solid side keeps its centre vertices; next: solid shell with the right ground material, or confirm with
  the POI control test (dev "poitest", poi_test.bin) that our pipeline renders POI shapes smoothly.
- Dev: carve dx dz radiusDm [dy], digmode n, timeofs seconds (21600 looked like day), poitest [dx dz].

## Building — WORKING (2026-10-09)
- The earlier "client never commits" was wrong: with the 4th tray tab's Add tool selected, each click sends
  VoxelConstructionBase 0xF4 sub 1 ApplyDiffRequest (Frida: VoxelClientImpl slot 11 FUN_1430aca80 → CCM OnPreCommit).
- Body after sub: u8 (FF), 24 bytes, u32 n, n × {u64 cell id, u64 cell id, u32 4, u32 length, A1B2C3D4 blob}, then
  material usage (u32 n × {u16 material, i32 count}), cell versions, areas (ignored).
- Blob raw = 8000-byte changed mask (MSB first, server voxel index) + SerializeBlockData format 2 of the cell.
- Server (Game/Building.cs): per-voxel Claims.MayEdit, apply to overlays, save, broadcast except builder.
- Offsets count in format 2 is a BYTE count (verified on client POI blocks).
- Open: VoxelConstruction Response (sub 7) not sent (client seems fine), material costs (∞ in tray), undo/redo
  (subs 3/0x10), selection/copy-paste (2, 6, 5), heal claim (0xC).
- Remove, Smooth, Paint use the same ApplyDiffRequest (verified: 1, 25, 1 voxels applied).
- Voxel data materials = VoxelMaterials ARRAY_INDEX (col 1), not ID (79 = Sand). Costs: currency (col 36) → item
  (71 Dirt 44, 72 Sand 45, 73 Stone 46, 88 Snow → Ice 76, 68-70 → cut wood 2000-2002, 74-77 → 121-124,
  78-87 → smelted 108-117), else ITEM_ID (col 40); 4 voxels per item, remainder carried; refunds on removal.
- Response sub 7 {u32 request type (1), u32 result, bool}: 0 ok, 0xC InsufficentMaterials, 0xD props, 0xE
  permissions, 0x11 too many template voxels (FUN_1430b6560). Without it, after a server resend of the cell the
  client sends TerrainCellPendingRequestTimeout and stops editing; never resend a cell the client is editing.
- Tray slots tied to an item show live inventory counts (Sand 10 → 9).
- From the user's video: mining should show a ring indicator while a tool is equipped, and mined rock flies into
  the player (gather effect). Both missing here — todo.

## Top-left menu bar + Travel (2026-10-09)
- Icons left→right: (menu) Escape-style list, Showcase (cart; embedded browser, blank — store URL), Designs voxel
  storage, Inventory, Notifications, Friends, Goals, Travel. Opening windows sends UiBase 0x32 sub 0x17 {u8 window
  id, u32} (0x19 guild, 0x18 goals, 0x07, 0x1A, 0x17, 0x04, 0x0E, 0x1D) — notifications only.
- Travel ("Where Do You Want to Go?"): My Sites → "Select Destination" list → cast 186 (target = own pos!) → server
  teleports to own site after 4 s (WORKING). Others' Build Sites / My Friends: nothing sent yet (lists empty).
  Guildmates opens guild window. Spire → AbilityCasterInfo for 185. Other Worlds → 0xFC/2 RequestLocations (reply
  0xFC/3: u8 sub, u32 n × {u32,u32,u32, u32 m × 7 u32}, u32 k × {u64,u64,u64,u32,u32,u32,u8,string,u64,u64,u32,u32},
  reader FUN_1430c4150; fires GameEvents:OnOpenTravelMenu). Underground → 0xFC/0xB RandomUndergroundRequest.
- 0xFC TeleportBase subs: 2 RequestLocations, 3 Locations, 4 Request, 8 IntraZoneRequest, 10 ManagedRequest
  {type 1/7 guid, 4 zone hash, 2 none}, 0xB RandomUnderground. 0x1E/0x3F6 ManagedToLocation.

- FREEZE CAUSE (2026-10-09): overlay voxels with flag 2 (vertex) but no material bit hang the client mesher (froze
  when walking near the build site). Never write them; TerrainEdits.Load strips them; Deserialize drops them.

- FREEZE CAUSE #2 (likely): the client creates InGameMarketplaceBrowser at login (ClientConfig MarketplaceUrl =
  https://store.landmarkthegame.com, +#!/gallery). DNS fails (-105) → CheckForAutoReload with doubling backoff
  (2,5,8,16,33 s, ...); each reload first does a CasApi session/create to auth.daybreakgames.com (still answers;
  "Session expired" on the freeze). All 3 freezes (07:12:39, 07:16:52, 07:20:48 local) were at a reload; logs
  in Landmark/Logs/EmbeddedBrowser.log, BrowserCef.log. Not triggered by LoadWelcomeScreen (tested). Needs a
  ClientConfig change (user's call) or hosts redirect (user's call).

## Day/night — WORKING (2026-10-09)
- GameTimeSync 0x37 {u64 timestamp (seconds, in the "double"), f32 cycle speed, bool}. Client ClientGameTimeManager
  (vtable 0x144659ae0): handler FUN_14312fb00 → SetTime FUN_143cd37b0: scene seconds = h*3600+m*60+s of the
  timestamp (UTC), then advanced at cycle speed. Was night because the timestamp was the player's real clock.
- Game/WorldClock.cs: shared clock, 9.1x, starts 09:00; dev "timeofday <h>". Verified noon in game.

## Mining feel (2026-10-09)
- Ring = MiningDecal (ActionStateMining slot 24), size from calc stat 3 HarvestingArea (FUN_142dbf9c0 → SetParam 0).
- Recast = 50 / max(1, stat 13 HarvestSpeed) + base (FUN_142d77f20): send 100.
- Every cast must get a reply, else client error 15 (cast pending) → "Ability on Cooldown" forever.
- Cast 1018 tail: f32 1.0, vec3 dir, 7 × 0, then RING POINT vec3 at tail bytes 44-55, then 1.0. Dig there.
- Chunks: LootPinataBase 0xFB/1 SpawnLoot {u32 id, u32 def (LootPinata.txt: 21 stone, 14 dirt, 19 sand…), vec3,
  4×u32, u64 owner, string, u32 kind 3, 3×u32}; auto-scoop flies to owner. Spawn above ground.

## Tree collision, Ghidra MCP (2026-10-09)
- Prop collision comes from the model's .adr (`<CollisionType type="4119585228"/>` + `<CollisionData fileName="X.cdt"/>`).
  The u8 "collision type" block in AddLightweightProp's prop part (reader FUN_142ba65c0, entity +0x2b0, passed to
  vtable+0xd0) is a movement/physics-body mode (types 0-5 with shape floats), not model collision.
- Of our tree models, 344 GnarledPine_Large01 (type 3099813281, no .cdt) and 350 Birch_Medium01 have no collision
  mesh → players walked through them. Removed from the spawn lists (commit 68e185b).
- Read-only pack reader: scratchpad packfind.py (Resources/Assets/*.pack, big-endian chunk directory).
- Ghidra MCP after the PC crash: server itself answers initialize in ~9 s; a stale re/Landmark589.lock was moved
  to re/lock_backup. Claude Code's connect still timed out at 30 s → reconnect via /mcp.
- Flora (grass) placement: FUN_143615110 per mesh triangle (caller FUN_14360ea20). Seed = hash of the triangle
  centroid (+param_5 origin): x*104959 + y*1301081 + z*15486703 + floraId*0x95341, LCG *0x7ff8a3ed+0x2aa01d31;
  density per flora layer (FUN_143612170), slope gate (+0xdc..+0xe0). Fully deterministic from geometry → grass only
  re-rolls where triangles change. Open question for the next in-game test: does a dig re-triangulate the whole
  cell (client simplification) or only near the hole? Hook: re/hook_flora.js (counts/hashes centroids per call).
- Right-click Equip: Lua EquipItem (table 1446d9ce0 → FUN_143505840) → FUN_14331fc60 sends InventoryBase 0x2D
  sub 3 InventoryPacketEquipByGuid {u64 item guid, u32 profile, u32 equip slot id} or, with no item, sub 2
  InventoryPacketEquippedRemove {u32 equip slot id, u32 profile}. Handled (InventoryHandler.HandleEquip/Unequip).
- Verified in game 2026-10-09: right-click gear in the bag sends TransferItem 0x16 dest (owner 1, container 2,
  slot 2 = main hand!) for any gear → server now uses the item's own gear slot (boots → 7). Right-click worn gear
  sends dest (1, bag 0, slot 0) → server picks the first free bag slot when slot 0 can't swap. Both work.
- Claims: answering 0x19 with the full list (SendAll) made the client re-send 0x19/0x27/0x35 several times a
  second — 0x19 is now left unanswered (the list goes out on zone-in). 0x27/0x35 replies are fine (once each).
- Escape/start menu = UI/Default/Data.xml UIDataSource "StartMenu" (Lua callbacks in Resources/Scripts/global.lua).
  Verified 2026-10-09: Inventory, Goals Journal, Map, Designs open. Designs needed TemplateBase 0xF0 sub 2
  EnvironmentListResponse {u32 n, n×0x130 TemplateBasicData, u64} (dispatcher FUN_1430624b0) — sent empty on
  zone-in; storage then shows 0/6000000. Without it: placeholder row + Lua error Templates.GetTemplateUrl(nil).
  Build Site Management sends ClaimManagmentRequest 0x1C (answered with sub 8 + upkeep sub 10) but showed nothing
  with 0 sites; the window loaded fine in earlier sessions when a site existed.
- Client logs worth checking: Logs/FlashErrors.log (missing data sources), Logs/LuaErrors.log, Logs/Ui.log.
- Logout → character select WORKS (verified 2026-10-09, then Play back into the world). Flow: Lua Ui.Logout →
  FUN_142c6ee40 (state 0x15 WaitingForReloginSession) → CharacterSelectSessionRequest 0xF6 → server 0xF7 {bool,
  session} → state 0x16 → FUN_142c24730 sends ClientLogout 0x14 and waits ≤2 s for the SERVER to close → state 7
  (login server). The server must close the connection on 0x14: otherwise the client's own disconnect fires late,
  the gateway OnDisconnect handler (FUN_142d58330 → FUN_142c42f00) starts another logout and the client shuts
  down ("Lost connection to the gateway while waiting for a relogin session").
- 2026-10-09 landscapes, per-landscape state: TerrainEdits/OreVeins/TerrainHeights/TerrainMaterials, Gathering,
  Creatures, Claims and Props all keyed by StaticZoneData zone id (home 406 keeps its old keys/folders; DB columns
  claim.zoneId / world_prop.zoneId, migration ClaimPropZone). Players only see same-landscape players, props, sites,
  terrain broadcasts and say chat. Pioneer lands refuse hand-placed sites; new characters (no position, no site) get
  a granted site on the least-crowded baked Pioneer land (Game/Pioneer.cs; dev command `pioneer`).
- dotnet ef while the Debug servers run: use `--configuration Release` (copy AuthServer.json into the Release bin
  first, remove it after) so the locked Debug DLLs are not rebuilt.
- UI events: Lua calls Ui.UIEventTriggered(n) — 32 help browser (global.lua), 13 build mode hidden (hudstate.lua),
  7 marketplace browser. Others (0x1A inventory, 0x18 journal, 0x1C inbox) come from native code. The server now
  logs every event (Achievements.UiEvent) so window-open goals can be mapped from observed ids.
- 2026-10-10 UI event ids decoded from the Flash UI (re/tools/swf_uievents.py → re/out/ui_events.txt): each widget
  calls Ui.UIEventTriggered(n). 2 BuildMode opened, 4 Teleport(Travel) opened, 5 Lumen count button, 6 claim
  management, 8 claim rename, 9-12 BuildMode tabs (12 Build, 11 Prop, 10 Tint, 9 Templates), 14 travel button,
  17-19 TweakMode buttons, 20 Repair, 21 claim mgmt tab, 22 Equipment, 23 Contacts, 24 Journal, 25 Guild,
  26 Inventory, 27 inventory tab, 28 Inbox, 29 SocialShare, 30 journal category click, 31 TweakMode. Matches the
  live ids seen earlier (24/26/28). Posted keys and clicks don't reach the game UI: test goal hooks with the dev
  command `uievent <id>`.
- SendSelfToClient header fields named (docs/protocol.md 3.1); played time / logins / creation date now sent.

## Night of 2026-10-10: grapple relay, tool packet, hover outlines, prop tints
- HeroicMovement 0x105 (client: sub 1 FireGrapplingHook, 2 SetHeroicMovement, 3 SwimmingUpdate, 4 Terminate):
  body u8 sub, u64 entity (the sender's own guid), data. Relayed to same-landscape players within 200 m with the
  entity forced to the sender (PlayerManager.RelayHeroicMovement). The pull itself is client physics; range/speed
  come from calculated stats 19 GrappleMaxDistance (60) / 20 GrappleShootSpeed (40) / 18 ReinSpeed / 21 Cooldown
  (stat container = local character +0x7e8, FUN_142c319e0). The 1 s Terminate seen when aiming at a far hill =
  hook travelled its max distance without hitting.
- ToolBase 0x117 sub 1 ToolPacketActivate (FUN_1433aa0c0) = build tool switched; informational, now dropped quietly.
- Hover outlines: interactable props (stations/chests: DynamicInteractionFlagging + InteractionInfoReply) DO get the
  gold outline (verified on a crafting station). Sickle/plants: ActionStateReaping has no target hook (vtable slot
  22 returns false; ActionStateLogging's returns the picked entity) — 0.589 reaping is an arc tool with no
  single-target outline. Not a server bug.
- PROP TINTS: tray tab 3 (UI event 10) sends PropBase 0x18 AvailableTintsRequest; answered with 0x17 (the captured
  live palette, now Resources/PacketDumps/PropTints.bin with the player's guid patched in; layout in
  Props.SendTintList). Picking a swatch with a prop selected sends 0x16 SetTintRequest {u32 n, n×u64 prop, u64
  player, u32 alias hash}; hash → alias from the palette; saved in world_prop.tintAlias (migration PropTint), sent
  in the prop spawn's tint-alias string and broadcast as PlayerUpdate 0x7A SetPropTint {u64 guid, string alias}
  (client → FUN_142e26580). Verified: palette shows "Basic Tints (60)", request decoded, DB saved.
  NOT yet seen rendering: the test harness logs in direct (no login server), so the client never gets the ArtData
  tunnel blob (AuthServer ArtTints.json, 128 GlobalTint_* definitions) that turns an alias into colours. Real logins
  get it. ArtData also exists as zone packet 0x116 sub 1 (reader FUN_142bae270, 8 lists) if direct sessions need it.
- Dev: `propdelete <guid>`, `finditem` now prints the prop an item places.
- Build cell responses: TerrainCell 0xEC sub 3 ClientCellUpdateResponse {u32 sub, u64 cell, bool ok, string} per
  edited cell (client handler → VoxelFarm core +0x5a60 vfunc +8). Verified: after a build, no
  TerrainCellPendingRequestTimeout within 75 s (was ~50 s).
- Goals "Props To You" (achievement 480): objectives 739 place chest, 740 open chest, 1305 open prop tab, 1310 place
  Simple Stool, 1312 tint Simple Stool, 1313 Starting Location, 2156 Target Dummy. Stool goals accept every
  "Simple Stool (...)" item's prop. Tint verified: 1312 → state 2.
- Harness gotchas: the Goals tracker panel (left) overlaps the build tray tabs at 1024×576 and eats clicks — close it
  (red X at 212,153) first. Hot reload does NOT run initialisers of newly added static fields (null until a restart).
  Orphaned dotnet-watch children (parent gone) keep obj/*.pdb locked: kill them, then touch a .cs file.
- User's own client logs (read-only) show only a 121-line burst of "ActionBars.Bar0 not found" during each load
  (UI polls before the bars arrive) — harmless.
- EMOTES (2026-10-10): CommandBase 0x44 dynamic command list = u16 sub, u32 n × string, u32 source (1 zone); the
  client registers each as /name (DynamicCommands.log). Typing one sends Command 0x1E sub 0x43 ClientCommandPacket
  {string name, u32 n × string args} ("/emote 4" → "emote", ["4"]). Unregistered names: client says "Command not
  supported". Emote names are client text 559123-559192 (wave, beg, bow, cheer, cry, flirt, groan, giggle, shoo,
  laugh, no, point, shrug, sob, talk, taunt, nod, dance...). Animations: AnimationSelection "EmoteSingle" rows,
  Emote_Index 0-21 (AnimationSelectionVariables); "EmoteLoop" rows (236, 243, 252, 265) loop until 253; stances
  (Stance_Index: 223 stand 0, 224 kneel 1, 246 dance 3, 248 skip 102, 250 drunk 101, 251 sneak 104, 249 = 103) loop
  until 223. Matched by eye: 217 wave, 230 cheer, 229 bow, 240 point, 239 shrug, 242 taunt, 231 cry, 247 cough.
  Played with AbilityPlayAnim on the player's guid for everyone within 80 m (verified from a second client).
  A selection sent while the character moves is ignored, so the server sends "stand" once the player has stopped
  after walking > 0.5 m out of a stance (Game/Emotes.cs). Test: scenario emotes (Frida rpc cmd → FUN_14330b700).
- Harness: re/test/restart.sh (restart under the watcher + orphan cleanup + CS2012 retry); re/test/anim.sh (emote
  survey filmed from LawTest); client.js rpc cmd("text") runs any slash command (the client's own "freeflyhide on"
  also goes through FUN_14330b700 — a free camera exists for future visual checks).
- BUILD UNDO/REDO (2026-10-10): Ctrl+Z / Ctrl+Y (and the top-bar arrows) send VoxelConstruction 0xF4 sub 3
  {u32 0 undo / 1 redo}; the client does NOT change its own view. Server (Game/Building.cs): every applied edit is
  recorded per player as sparse before/after overlay voxel values (TerrainEdits.Read/Write; 20 deep, redo cleared by
  a new edit); undo/redo re-checks claim permission, charges/refunds materials (TerrainEdits.MaterialDelta over the
  sparse values) and broadcasts the cells to the whole landscape incl. the builder. Verified: 5 adds → 5 undos
  removes the pile on the builder's screen; sand refunded/charged. Client cell cache (VoxelCellBlockCacheImpl,
  core +0x5a60): HandleCellUpdateResponse (sub 3) decrements a per-cell pending count; HandleCellsUpdate ignores
  block updates for a cell while its pending count > 0 — so every ApplyDiff must be answered with sub 3 first.
  ApplyDiffRequest tail after the cells: {u64 cell, u32 x, y, z, u32 sx, sy, sz} edit box, u32 n × {u16 material,
  i32 voxels} usage, then per-cell versions (all 0 seen). ToolBase 0x117 sub 2 UndoStackChange {u32, u32, string
  list} only feeds the UI's "Tool.UndoStack" (not sent yet).
- Restart rule: adding OR removing a static field is a rude edit (server restarts, direct test clients exit).
- Build tools survey (2026-10-10, ToolPacketActivate ids): Add 1?, Select 2, Heal 5, Line 0x0D, Fill 0x11. Add,
  Remove, Smooth, Paint, Fill and Line all commit through ApplyDiffRequest (Line: two clicks, 191 voxels / 2 cells)
  and undo/redo works on them. Heal sends nothing on untouched ground. Select sends VoxelConstruction sub 2
  AddTemporaryBlockRequest {u8 FF, u32, u64 cell, u32 ×…} (selection preview, no reply needed so far); Ctrl+C is
  client-side; Ctrl+V shows a paste box (confirm control not found at 1024×576 — a confirmed paste should arrive as
  ApplyDiff).
- Guild roster Location: member game data is an XML element <MemberGameData LastOnline BattleRank LastZone
  LastWorld LastContinent/> (FUN_143b92760); Zone/World are NAME TEXT ids. Fixed ("Liberation:Grand Fjord").
- World map (M): renders a flat colour on the test copy (white on 406, blue on 416). The map is a live top-down
  camera render ("camera_world_map_*" console toggles), and the test copy runs RenderDistance 400 (user's own
  options: -1). Not a server packet issue as far as found — needs a look on the user's own client.
- Cross-landscape loads can take ~90 s on this PC ("Finish SCENE: Total Process Time: 90469 ms",
  Logs/ClientSceneProcessing.log) — slow, not hung.
- COMBAT (2026-10-10): 0.589 combat items are gear: held weapons (main hand, equipped slot 2: e.g. 3040
  Gunslinger's Six-Shooters), class chest armour (def slot 8, e.g. 1530 Armored Brawler's Doublet → 1102) and
  accessories (def slot 3) that grant abilities; dev `weapons` lists them. Holding the six-shooters puts the client in
  ActionStateCombat (reticle); a left click casts ability 799 (not listed on the item). AbilityEx.txt is empty, so
  ability effects are server-side design: every attack does WeaponDamage (70 ±15 %) for now. Reticle casts carry the
  player's OWN guid at tail +36 and their own position as the target point; the aim is in the tail as camera
  position (vec3, w = 1.0) + unit direction (found by scanning, AbilityCastAttempt.AimRay). Creatures.Shoot hits the
  first creature within 1.5 m of the ray (30 m). Verified: kobold shot at ~5 m and killed (7 shots), loot given.
  Pulverizers (212/214/216/218, abilities 162/164/166/168) now dig wide holes (radius 18-34) without collecting.
- BUILD SITE MANAGEMENT (2026-10-10, needs a 1600×900 test window: windows are larger than 1024×576 — set in the
  test copy's UserOptions.ini; both test clients share that file and save it on exit, so edit it with no test client
  running): site list label "<name> (Zone 0)" came from ClaimManagmentData's last two u32s = world, zone (client
  compares with game +0x378cc world / +0x378d0 zone; mismatch appends the landscape name, unknown → "Zone %d").
  Info tab = EditClaimItemData (sub 9) {u64 id, bool isGroup, record}: isGroup must be false; the tab shows record
  +0x38 (name) and +0x50 (welcome message, "Epic Story"; default text "Welcome!"). SetItemName 0x2A / SetItemDescription
  0x2B {u64 claim, string, u8 1} arrive on EVERY keystroke → applied after a 1.5 s pause (Claims.AfterTyping).
  Permissions tab renders (default access dropdown, special access list, Add).
- SWINGS SEEN BY OTHERS (2026-10-10): a player's client animates its own tool use from its action state, but other
  clients showed the swinger standing still (verified from LawTest's window). AnimationSelection pairs Enter/Release
  with variable Class: 1001 (226/227) pick overhead swing, 1002 (254/255) axe side chop, 1004 (256/257) sickle low
  sweep, 1006 (258/259) unknown, 1007 (261-264) channeled (gun?). Server now plays Enter then Release (+350 ms) on the
  swinger for others within 80 m (Emotes.ShowSwing). Verified from LawTest: wind-up, strike, idle.
  The CastSuccess handler (FUN_142d7a370) would also play +0x3c AnimationId on the caster entity for any cast that
  isn't the receiver's own pending one — an alternative path.
- All 12 scenarios pass (39 checks) after tonight's changes.
- WEAPON ATTACKS SEEN BY OTHERS: with the six-shooters held, class 1007 Enter 261 (crouch, aim) + Release 262 (two-handed
  shot) shows the attack; the stance then persists — 262/264 keep it, 223 "stand" doesn't end it, a lone 259
  (class 1006 Release) returns to idle with a small arm movement. Server: Emotes.ShowAttack plays 261/262 towards the
  aim (ray origin + 10 m) and 259 2.5 s after the last shot. Selection 1 "Attack" alone shows nothing. Harness:
  re/test/observe.sh [gun] sets up the two-client observer layout.
- GRAPPLING HOOK VERIFIED (2026-10-10): aimed at ground ~28 m away, Q pulled MrLawKing there (client physics), and
  LawTest saw the rope fired from MrLawKing (HeroicMovement relay). Client sends sub 1 Fire, sub 5 {u32 1}
  (HeroicMovement on), sub 4 Terminate. Earlier "no pull" = target beyond GrappleMaxDistance (60).
- MAIL (2026-10-10): MailBase 0xF8 u32 sub. C→S: 1 ComposeRequest {u64, u32 n × PlayerName To, u32 m × PlayerName,
  string subject, string body, u32 k attachments}, 6 InboxListRequest, 8 ReadMessageRequest {u64 id},
  10 DeleteMessageRequest {u64 id}. S→C: 4 ComposeResponse {u32 result, + PlayerName if 1/0xD}, 7 InboxListResponse,
  9 ReadMessageResponse {u64 id, u32 result, u32 kind, PlayerName, subject, body, u64 sent, u32, u32 attachments,
  u32 html blob}, 0xB DeleteMessageResponse {u64 id, u32 result}, 0xE NewMailInboxNotification (empty), 0x13 Enable.
  PlayerName = {u32, u32, u32, string first, string last}. MailData = {u64 id, u64, PlayerName, subject, body, u64
  sent (Unix s → "29d 23h" left), u32 read, u16 attachments, u32 kind flags (0 player mail)}. Results: table at
  0x14521a980 (0 Success, 1 unable to find user, 5 attachment invalid, 0xD inbox full, 0xE sent too many, 0x14 not
  found, …). Verified compose → recipient's inbox → read (window shows From/Subject/body, Delete/Reply) → delete.
  ReadMessageResponse on success has a u64 sender BEFORE the PlayerName (missing it = the client silently drops the
  packet and the window never opens); a failure ends after the kind flags. Attachments (items/coins) are refused
  (result 5) for now. (UI/ScriptsBase.bin's "size_t size mismatch" in LuaErrors.log is harmless — GameEvents
  handlers like ReadEmailMessage still run; test with client.js rpc lualog to see which Lua calls happen.)
- MAIL ATTACHMENTS (2026-10-10, verified end to end in the UI): drag a bag item onto a compose slot → 0x14
  AttachItemToComposeRequest {u32 type 1, u64, u32 bag slot, u32 count, u32 container, bool} → 0x15 {u32 result,
  echo}. Compose then carries u32 n × {u64 id, u32 type 1, u64 item guid, u32 count, u32, u32}. Read reply
  attachments: u32 n × {u64 attachment id, u32 type, body}: type 1 {u64, u32, u32, u32} (shown as placeholder def
  0x392), type 2 = the client item record FUN_142b87110 (= our InventoryItem.Write WITHOUT its last byte — that
  byte is a 0.695 field; with it the packet is dropped), type 3 currency {u32, u32}, type 4 u64 list. Take All →
  0xF ConsumeAttachmentRequest {u64 mail, u64 attachment (0 = all), u8} → per attachment 0x10 {u64 mail, u64
  attachment id, u32 result}; the window removes each id and decrements NumAttachments (FUN_14358abb0).
- Harness: two test clients at the same window position → clicks can land on the other client even when the target
  is foreground; close (or move) the one not under test. re/winrect.ps1 prints a window's rectangle.
- COPY/PASTE (2026-10-10, in progress): Ctrl+C / Select tool sends VoxelConstruction sub 2 =
  ClientSelectionAreaRequest (NOT AddTemporaryBlock; that is sub 0): {u8 FF, u32, u64 cell id, u32 x, y, z, u32
  sx, sy, sz, …} plus (per the constructor FUN_1430b3990) PropData list, u16→int material map, string,
  CellIdAndPermanentVersion list, voxel blob, u32 1, u8, string, u8. Server must answer sub 6 ClipboardData
  (reader FUN_1430b0d80): u8 sub, PropData list (FUN_142f0a340), material map (FUN_143059430: i32 n × {u16, u32}),
  cell block list (FUN_143059940: i32 n × {u64 cell id, block FUN_143058d90}), 3 × u32 dimensions, u32, bool
  hasVoxels → client builds VoxelFarm::CClipboardData (FUN_1430b7b10 → FUN_143ad6ff0) and only then can paste.
  Paste = sub 5 OffGridPasteClientRequest (constructor FUN_1430b3c90) or ApplyDiff. The server only holds player-built
  overlays (TerrainEdits), which is exactly what designs copy. Designs ("+" in the Designs tab) are built from the
  same clipboard (Lua Templates.SaveTemplate → selection tool vfunc 0xf0). Next: decode the request writer and the
  block format FUN_143058d90, answer with overlay voxels, then handle OffGridPaste.
- COPY/PASTE DONE (2026-10-10): copy = sub 2 action 0 with one block (40³ grid, box origin at 0,0,0; y fastest).
  The server counts solid voxels per material into ClipboardData's material map (the paste tool refuses to start on
  an empty map: cursor vfunc 0x138 FUN_1436d6ac0 checks clipboard+0x110 / +0x18) and keeps the grid per player.
  Paste = the SAME sub 2 with action 4 (writer FUN_1430b96a0 via VoxelPasteCursor vfunc 0x298 FUN_1436db660; NOT
  OffGridPaste sub 5), no voxels, origin + 4×4 int rotation; the server writes its copy (Building.Paste in
  BuildingClipboard.cs), charges/undo like a build. Must answer TerrainCell sub 3 for every cell in the request's
  cell list BEFORE broadcasting, or the paster's client ignores the update (cells pending). Verified in game: slab
  copied, pasted beside it, visible; undo refunds. Rotation mapping (M·s, rows first) still unverified with Q/E.
  Harness notes: WinDrive "click"/"mdown" need coordinates; the Select tool box needs a slow mdown→moves→mup
  (separate lines with sleeps); Ctrl+V types into chat if the chat box has focus — use the Frida rpc instead
  (scenarios/adhoc/pastedrive.js: paste/fire/select/tools/clip). Leftover: MrLawKing's 2 test pillars (cell
  20270810BD02701C in TerrainEdits/416 also holds older edits, so not cleared).
- DESIGNS DONE (2026-10-11): save (sub 2 action 3 → TemplateBase 3), list on zone-in, place (4 → 5, then paste with
  the design guid), rename (0xA → 0xC), delete (6 → 7); stored in table `template` (raw block list). Verified in the
  UI: saved "TestDeiign" 17×1×18, placed it (paid 76 sand; refused with "needs 76" when short), renamed to
  "Sand Slab", deleted. GOTCHA: GamePacketWriter has no Write(ushort) — a ushort argument binds to Write(uint) and
  writes 4 bytes; use WriteUShort (cost me a 2-byte misalignment: Collected instead of Personal, author "null",
  no paste preview). Screenshot uploads (0x0107 requests with "images/templates...") are ignored, so design tiles
  show a placeholder icon. Harness: scenarios/adhoc/tmpledit.js renames/deletes through the client's own functions;
  tmplpeek.js dumps the client's design cache.
- UNDO STACK (2026-10-11): ToolBase 0x117 sub 2 UndoStackChange {u32 undo, u32 redo, list<string> names} sent after
  each recorded edit and undo/redo (Tool.UndoStack list). Hot-reload hazard: changing a method/ctor signature makes
  the first call throw 'deleted method implementation' and kills the server — restart.sh after such edits.
- COINS (2026-10-11): character.coins (migration CharacterCoins); WorldSession.ChangeCoins persists + ClientUpdate 0x29
  sub 0x13 CoinCount {u32} (player +0x72a38, Lua GetPlayerCoinCount). Trade coin offers move coins on completion
  (tradecoins 9/9). Mail: compose coins = type-1 attachment with guid 0; COD = type 5 {u32}; read reply type 6 coins,
  type 5 COD ("Cost to retrieve item(s)" + Pay C.O.D., verified on screen); paying = ConsumeAttachment of the COD id,
  coins mailed back to the sender; items refused with 0x1F until paid (mailcoins 10/10). Dev: coins +n|-n|=n.
  DISPLAY NOT SOLVED: inventory/mail "gold coin" counts read coin item 915 in container 8 (FUN_143588e70); our
  item-def dump has no 915 and 0.589 InventoryDefinition.txt has no container-8 slots — a synthetic def + container
  size reached the client but the stack was never placed (reverted). PlayerUpdate 0x49 CurrencyChange {u64, n×(u8,
  u32)} only feeds Character.Currency rows (updated only with the build element panel flag). Daybreak Cash slot =
  InGamePurchase 0x45 sub 0x2D WalletBalanceUpdate {u32} (FUN_142f32300, row "DBC"); user thinks the other slot is
  Lumens. GOTCHA: GamePacketWriter has no Write(ushort) (binds to uint).
- COINS = LUMENS (2026-10-11, superseding the coins column): StringHashToValue Currency.ItemDefinitionId=1579 (Lumen)
  — the client's 'coin' everywhere (top bar, inventory gold slot, mail send/COD, trade) is the stack of item 1579 in
  currency container 8 (self packet must declare container 8 with size>0, visible flag false). Inventory.Add keeps
  one stack there; ChangeCoins = Give/TakeItem(1579). Column dropped (DropCharacterCoins). Verified: top bar and
  inventory show 50. Daybreak Cash slot = InGamePurchase 0x45 sub 0x2D WalletBalanceUpdate {u32} (shows 100 with
  dev 'send 45-00-2D-00-64-00-00-00'); not implemented (premium/real-money, marketplace absent). User pointed both out.
- PASTE ROTATION VERIFIED (2026-10-11): in paste mode E = rotate gizmo, mouse WHEEL turns 90° (Q/E alone don't);
  request matrix [0 0 1; 0 1 0; -1 0 0] placed the 13x2x2 strip exactly where/how the preview showed. Writing
  clipboard +0x28 via Frida does not reach the request (the cursor keeps its own transform).
- DESIGN SCREENSHOTS (2026-10-11): upload = 0x107 sub 1 (routed in WorldSession's unknown-opcode branch: enum
  can't match >0xFF opcodes); stored TemplateImages/<guid>_<n>_<size>.jpg; tiles fetch over HTTP from ClientConfig
  MarketplaceAssets (test copy now http://127.0.0.1:19001; the user's own install still has the dead Daybreak CDN —
  their call to change). Verified: ImgTest tile showed its thumbnail after relog.
- SALVAGING (2026-10-11): station 41 (Magical Monument, item 900 / prop 881). RecipeBase 0x19 AddItem {u32 recipe,
  u32 slot, u64 item} -> 0x1A {bool, u32 recipe, u32 slot, u64 item}; 0x18 Start {u32 recipe, n x {u64, str, str,
  u32 icon, u32}}. Component types: 4 item category, 5 crafting category group (CraftingCategoryGroupMap
  Definitions.txt), 6/7 any. Verified: pick salvaged via recipe 993 -> 276 Lumens shown. Harness: adhoc/salvage.js
  (Lua SalvagingStationAddItem + start); the recipe must be selected in the window first; the Frida game-thread
  hook only runs while the client window has focus.
- LUMEN STATION (2026-10-11): station 55 (prop 2565 / item 3265, name text clashes with the salvager -> override).
  918 recipes; prices are RecipeComponents rows reached only via RecipeRecipeComponentMaps.txt (RECIPE_ID 0) —
  GameTableManager.ComponentsOf merges both (dedup per recipe+component). Verified recipe 1880: -130 Lumens,
  +7500 item 121. All 12 scenarios pass.
- CREATURES (2026-10-11): Skeleton 1457, Slaug 1241, Ogre 1456 (their .adr name PlayerAnimationNetwork, as kobolds)
  render + fight (verified: all three killed LawTest). Wolf/Deer/Abomination use NpcAnimationNetwork, Giant/Chomper
  their own networks — untested. Camps by biome. Melee needs |dy| <= 3 m (creatures follow the height map; players on
  rocks/caves were hit from 10 m above). Revive Here = 15 Lumens (Health.ReviveCost) — click not re-tested (user
  came back to the PC; input stopped). re/idle.ps1 prints idle seconds (run with -ExecutionPolicy Bypass).
- WOLF/DEER (2026-10-11): Spirit Wolf 1463 / Deer 1462 render (NpcAnimationNetwork) but the client ignores our
  creature movement for them (server wolf chased to 2 m, client showed it at the spawn point while it bit LawTest).
  Dev-only (creature wolf|deer), not in camps. Needs RE of how NPC-network actors take position updates.
- FLOOD GUARDS: chat 5 burst / 1 per 1.5 s; zone packets 300 burst / 100 per s per session (RateLimiter). The
  client throttles rapid chat itself, so the guard can only trip for modified clients.
- HEAL TOOL: Heal = VoxelEraserCursor; apply -> FUN_1430ba4d0 -> VoxelFarm heal (FUN_143ed53f0) which diffs against the
  client's base layer; our overlays ARE layer 0, so heal finds nothing and sends nothing. Needs overlays on a
  separate layer (or base = generated terrain only) — design change, not done.
- CLAIM SNAPSHOTS: packets exist (0x38 Save {u64 claim,u32 slot}, 0x3A Restore, 0x3C rename, 0x13/0x15 confirm
  requests, 0x16 restore response) but no code in 0.589 calls the request writers (no xrefs, no pointers) — dead.
  Wolf and kobold are both ProxiedCharacter (vtable 14460f108) on the client, so position handling is shared; the
  NPC animation network must move the visual by root motion (needs locomotion animation selections). Harness:
  adhoc/actors.js lists client entities (transient id, class).
- GOALS (2026-10-11): objective text ids now parsed (Objective.TextId); 77 '<item> crafted' objectives auto-mapped
  to 76 items by name; hooks Killed(1016), Traded(845), DesignPlaced(783). Verified 1179 and achievement 484.
- GOALS by text (ae7efe5): Achievement.NameId + Objective.TextId parsed; ProgressByText index. Hooks: harvested,
  recipe unlocked (+ recipe item name), blocks placed (+ material name via BuildMaterials.NameOf), <name> completed.
  Verified 774 Blocks placed.
- GOALS: groups (joined, minutes grouped via Achievements.Update tick), blocks removed, biome builds, '<item> placed'
  (09b9b8b). One-off LawTest client crash during an all.sh run (dump 22500); rerun passed.
- HEAL retest: MODLOG says layers 0-3 are LODs, so layering isn't the heal issue. Heal likely targets damaged/dug
  terrain; a pick strike dug a patch at MrLawKing's site but the camera hid it before the heal test. Next: aim the
  heal tool at freshly dug ground (Frida 'fire' with the mouse over it) and log what it sends.
  Heal retest: green heal cursor shows over a freshly dug patch, but clicking still sends nothing (no F4).
- HEAL (2026-10-10): ApplyDiff cells whose packed blob is only the 8000-byte changed mask (no BlockData) are Heal
  edits ("revert these voxels to generated"). Server rule for now (user: verify later): repair terrain, keep player
  blocks -> overlay voxels that are air (flag 1, material 0 = dug/mined) are cleared to "no override"; solid overlay
  voxels stay. Free, undoable ("Heal"), claim-checked; reply sub 7 + cell responses, the full cell is resent to the
  healer (their client already reverted every voxel under the brush) and changed cells broadcast to others.
  Verified: heal on a freshly placed sand block -> "healed 0 voxels", block reappears under the cursor.
  Client observation: heal only sends when the brush hits voxels in the client's own edit store that are SOLID
  (a just-placed block). On our own fresh Remove holes (and older server-sent digs) it sends nothing, even with
  a larger brush. That hints the original heal reverted player additions (removed placed blocks) - if the user
  confirms, flip Building.Heal to clear solid overrides too.
- DEATH WINDOW FREE RESPAWN (2026-10-10): with no Lumen the window only had "Revive Here" (15 Lumen) -> stuck dead.
  ClientUpdate 0x29/0x20 RespawnLocations (reader FUN_142b83ae0; record FUN_142b95320: u64 guid, u8 type, vec4 pos,
  4 x u32, string name, u32, bool, u32, bool, u32 n x {u32,u32}) is now sent on death with one type-11 entry
  (build site centre, else where they fell). The client then shows a "Flee" button (ShowGraveyardRespawn checks for a
  type-11 entry); ReviveAtGraveyard -> PlayerUpdate 0x3B type 11 -> Health.HandleRespawn. Verified via
  adhoc/revive (Frida call of the Lua path; synthetic clicks don't reach the death window). The client never sends
  0xCD GetRespawnLocations by itself here.
- PROPS FROM THE PALETTE (2026-10-10):
  - BUG: GameTable.GetEntry refused ids above MaxId, and MaxId was the LAST ROW's id. RecipeComponents.txt isn't
    sorted (last 4730, max 5853): 998 of 3193 components were unreachable, so RecipeCost came back empty and those
    props placed for free (e.g. lantern 751 needs 50 x item 112). MaxId is now the real max and GetEntry just looks
    up. Only RecipeComponents was affected (checked every loaded table). Dev "propcost <prop>" shows cost/afford/knows.
  - Palette PlacePropById (0x12) now spends a carried prop item of that kind when there is one (InventoryHandler path),
    else the prop recipe's materials (palette count = how many you can afford, e.g. Standing Torch 758 = 100 wood).
  - KnowsProp: crafting recipes that no "Recipe:" item teaches (e.g. 316 Standing Torch) count as known; before,
    they could never be learned, so the prop could never be placed.
  - Tool [9] = "Linking Area Tool" (prop Links, ObjectScripting 0x111: SupportedActionsEventsAndDefaults, bindings
    add/list/remove, PotentialLink). Not implemented. Prop setup packets still unhandled: 0x0F Duplicate, 0x13
    UpdatePropText, 0x14 EditPropText -> 0x15, 0x19 SetPropName {u64 guid, string <=128}, 0x1A SetPropEnable
    {u64 guid, bool}, 0x1B EditSettings. PropDefs col ALLOW_USER_RENAME gates renaming.
  - Harness: tray tab clicks need two clicks (first is swallowed); clicking a palette entry starts placement.
- PROP LIGHTS & EFFECTS (2026-10-10): torches/candles/lamps/mote emitters were dark. Their flames, lights and
  particles are InteractableCompositeEffectSets (128 sets; rows per state, e.g. set 3 EFX_Prop_Fire_Torch: 0 OFF,
  1 ON = composite 10907 @ FX_Attach) driven by the SERVER through UpdatePropInteractionState 0xFD (reader
  FUN_142b94680, applied FUN_1430065e0): u64 prop guid, u32 behaviour (1 = ClientPropInteractableCompEffectState-
  Behavior; 0 SimpleAnimation, 2 Setting, 3 MorphemeRequest), u32 key, {u32 state (set row index, -1 clears),
  u32 previous (-1 = new: starts at once), u64, u32 n x i32 [set id, 0, start offset ms (x0.001), apply-change
  flag (must be non-zero when changing a state already shown)], u32 n x string}, bool. No client table maps props
  to sets: Game/PropEffects.cs matches by model name, mote-emitter descriptions ("torch fire (blue) mote emitter"),
  light-orb/spotlight colours, then light-source keywords: 154 props. Sent after each prop spawn.
  Verified: torches burn (flame + smoke), lantern glows; dev "propenable <guid> 0|1" switches one off/on.
- PROP SETTINGS: PropBase 0x1A SetPropEnable {u64, bool} (on/off, broadcast), 0x19 SetPropName {u64, string}
  (PropDefs ALLOW_USER_RENAME; goes in PropData's name string, no floating label), 0x13 UpdatePropText {u64, string}
  + 0x14 EditPropTextRequest {u64} -> 0x15 reply {u8 sub, u64 player, u64 prop, string, u32, bool}, 0x0F Duplicate
  {u64 prop, u64 player, u8, u8} -> new placement of the same prop (paid). Stored in world_prop customName/text/
  disabled (migration PropSettings). UI paths for these not clicked yet (prop menu needs real input).
- EQUIPMENT CONDITION (2026-10-10): client Condition = FUN_1435585b0(item +0x148 wear (float), definition +0x168
  durability set) = (DURABILITY_COUNT - wear) / DURABILITY_COUNT. +0x148 is the item record's "Unknown12" (float
  bits) and the LAST u32 of ItemUpdate (FUN_142f2ff70 copies packet +0xf4 there); the ItemUpdate "durability" u32
  is NOT it. Definition durability set = 5th u32 after quality (weapons 2, armour 3, tools/materials 0).
  DurabilitySet.txt: 2 = 9834 pts, 0.009/hp dmg, 984/death; 3 = half. Server: equipped weapons/armour wear by hp
  actually lost and per death (character_item.wear, migration ItemWear); ItemUpdate only when the % changes; at 0 %
  weapon damage is halved ("no longer functioning at maximum strength", text 575602). Repair: RecipeBase 0x21
  {u64 item, u32 currency, u32 cost} at station 56/57 (CraftingStations PARAM1 1579 Lumen, FLOAT_PARAM1 0.004/pt)
  -> wear 0, reply 0x22 {u32 sub, bool}. Verified: one death = wear 992, condition 89% (scenario durability).
  Repair UI not clicked yet. Tools in 0.589 have no durability set (never wear).
- UI SURVEY (2026-10-10): HUD icons open their windows (menu, Showcase placeholder, Notifications, Friends, Goals
  tracker, Travel). Escape does not close windows in this client. Logout from the menu: the client sends 0xF6
  CharacterSelectSessionRequest only when it has a login-server/gateway link (handled: 0xF7 with a new session);
  the direct-connect test copy instead logs "Could not return to character select" and shuts down — expected.
- TOOLTIPS (open): weapon tooltips show "There are no categories associated with this item!" and "Ability Name
  Not Found" (x4). Category defs ARE loaded (Frida: DAT_145352458 has 158, 26 = "Weapon" 558518); the tooltip
  (FUN_1436a9020) loops the definition's own category list (param_1[0x37]/[0x38]) and ability list ([0x40], 0x38
  each, name text id at +0x3c looked up via DAT_145351bf8). Not resolved yet.
- WOLF/DEER: positions at entity +0xa50/+0x1010 stay at the spawn for BOTH kobold and wolf (not the live position);
  kobolds visibly move, wolves don't. Movement is applied by Zone_HandleRemotePosition (FUN_142c4c900) -> entity
  vtable +0xd0 for both; the difference is inside the Morpheme NPC network (root motion). Parked.
- Tool [9] Linking Area / prop "Setup Script": SetupScript (Lua 143548e40) -> ObjectScripting 0x111 sub 0xF
  SupportedActionsEventsAndDefaults {prop guid}; reply {u8, u64, u32, list of {u32,u32,u32,...}}. Rules data
  (which prop supports which actions/events) is not in the client; parked.
- Tooltip ability names: the tooltip asks CasterInfo for the weapon's combo abilities (axe 3027: 760, 1181, 1182,
  761) and shows the reply's NameId; unknown ids got the generic 1098 layout (name 0 = "Ability Name Not Found").
  760/761 are now named Basic/Power Attack (texts 565568/565569); 1181/1182/816 still unknown. Not seen in the
  UI yet (synthetic hover only works while the cursor is free).
- HELD-BUTTON MINING (2026-10-10): ActionStateMining's per-frame input update FUN_143677b40 sets a held flag
  (DAT_1453aad30) on Select press and, while Select stays pressed (+0x68/+0x8d), re-fires whenever slot +0x130 says
  the ability can repeat: ClientActionManager +0x28 (FUN_1433aa490) = not casting (+0x70 -> +0x24) and
  FUN_142d80f30 = cost (ability +0x14c) affordable in resource ability +0x7c. Two blockers in our CasterInfoReply
  for tools: (1) Ua0 = ability id lands in the cost-resource id (+0x7c) -> "can't afford" -> no repeat; now 0;
  (2) CastTime/RecoveryTime land in the global cooldown (ability +0x9c/+0xa0 -> player +0x75620): 0.75 s GCD made
  every quick retry fail with can-cast error 14 (also repeated CLICKS failed); now 0 for harvest tools, with U68=1
  (ability +0x68: pace = 50 / harvest speed stat 13 + base, FUN_142d77f20) and stat 13 = 100 (0.5 s).
  Also: a swing that hits already-dug air digs on along the aim (up to 3 steps); an empty swing gives nothing.
  Verified: 3 clicks -> 3 digs (was 1). Held repeat can't be simulated here: synthetic mouse-down gives
  "just pressed" but never "is pressed" (+0x68), so the flag is cleared the same frame — needs a real mouse.
  Kind hash notes: 0xDFB05E5F instant; 0xF7150354 = charged (client sends 0x27/0x0E FinishChargedCast on release);
  0x216B5DCB = no cast on press. Client debug: ClientInputActionManager +0x1964 > 0 enables its InputAction log.
- LOOT PINATAS / DROPS (2026-10-10): LootPinataBase subs: 1 SpawnLoot, 2 RemoveLoot {u32 id, u64 player}
  (FUN_1430774e0), 3 PickupLootResponse, 4 NotifyOwnerlessLootClaimed, 5 PickupLoot (C->S), 7
  RequestClaimOwnerlessLoot (C->S). The client vacuums a pinata when its owner is near (FUN_14307d0f0; not while
  swimming), sends PickupLoot, removes it on the response. Pinatas never collected (player walked off) stayed for
  the whole session: now removed by the server (chunks 10 s, bags 60 s). LootPinatas is the shared drop module:
  SpawnChunk(item) picks the LootPinata.txt model by name (285 items: woods, plants, ores, gems, earths; overrides
  kept), SpawnBag(contents) drops a loot bag (def 5) whose items are given on pickup. Creature kills now drop a bag
  where the creature fell (was: straight into the bag). Trees/plants throw a chunk per harvest. Verified: bag
  vacuumed within 1 s on land, items added; uncollected chunk removed at 10 s. Dev: slay [r], pinatafor <items>.
- Shared module: Game/GameDataFiles.cs (Rows(file), row.U/I/F/S) for the gameData/*.txt tables.
- ITEM FUNCTIONS (2026-10-11). Survey of all 2,885 items by category: re/out/items_by_category.txt; every item's
  name + description: re/out/item_names.tsv. How the client uses an item: Lua Inventory.ActivateItem(guid)
  (FUN_14352b700) -> FUN_1433aa0c0: items with an ability (type 4 "use") are CAST (AbilityCastAttempt), items
  without one send Inventory 0x15 ActivateItem {u64 guid}. Test copy: client.js rpc activate(guid) runs that call on
  the game thread (queued from the input-poll hook; calling from Frida's thread once deadlocked the main loop ->
  watchdog FUN_142ab3ab0 crashed the client with 0xbaadc0de after ~3 min).
  * Consumables (cast path, by ability id): stat potions (Sharp-Eyed 7, Minor Yield 17, Furious 12 %, Greedy 3 %,
    Nimble 13 %, Highstep 6, Moonlight 4, Spirit of the Wolf 5, Alpha Boost 17 for 2 weeks), Lightstones (+15 light
    30 min / 1 h), healing potions 443-445 (heal over time, refused at full health or on cooldown), detectors
    (Ore Prospector / Ground Sounder: nearest vein of ore / gems within 64 m via OreVeins.Nearest; Cave Seeker /
    Chaos Cavern Sounder: nearest cave), Portal Shard (Spire.TravelTo), Bauble of Flight (stat 29 fly while on a
    build site the player may edit), Repair Bot Beacon (repair station prop for 5 min), High Yield Dynamite
    (4 m crater, resources collected), Cozy Campfire (10 min; heals nearby, Enduring Warmth at full health).
    Buffs.cs = shared timed effects (stat buffs folded into CalculatedStats, heal over time, cooldowns, toggles).
    Harvest Bonus (stat 17) now raises dig yields.
  * ItemUses (ActivateItem path): Builder's Bags/Chests unpack into what their description says (24; "Woven X" is
    "X Block" in 0.589); Bags of Sifted Material give random tier 1-2/1-3/1-4 ores, gems and stones.
  * Titles: PlayerTitleBase 0x94 subs 1 Add {u32 id, u32 type (1 prefix / 2 suffix), u32 name text}, 2 Remove,
    3 UpdateAll, 4 RequestSelect (C->S, Lua SelectPlayerTitle). PlayerUpdate sub 0x1C PlayerTitle {u64 guid, title}
    (the PlayerUpdate dispatcher switches on sub-1: case 0x1B). Title id = the "Title: X" item id; text = the string
    next to the item name (leading space = suffix). Verified in client memory: 1 owned, selected 3901.
  * Creature recipes (116 items in categories 273-279) unlock placing their creature prop (RecipeBook.KnowsProp).
  * Unlocks.cs + table character_unlock (characterId, kind, value): titles owned/shown, props taught.
  * Shared code: SubRequest8/16/32 base classes replaced 20 copies of the "sub-opcode + body" request reader;
    PlayerManager.Introduce; Abilities.Excavate (picks, pulverizers, dynamite); Props.Despawn.
  Scenarios: consumables, itemuses, creaturerecipe, dynamitecampfire (all pass).
  Not done yet: storage expansions (bag size is fixed at 40 server-side), refining fuel, Player Design items,
  NovaTech Holoseeker ability 1019, Builder's Chest bonus box.
- VEIN OVERLAY (2026-10-11, verified on screen): TerrainCell 0xEC subs are u32 on the wire (body starts with 2
  zero bytes). 0x10 DetectVeins {u32 n × {u32 ResourceVeinGroupVeins id, f64×3 from, f64×3 to (global dm), u32},
  f32 range, f32 fade, f32 duration, u32 n × material filter, u32 overlay id}; 0x11 EndDetectVeins {u32 overlay id}.
  The client only knows the tiered group veins (ResourceVeins 178-247): our vein geologies map by material.
  Holoseeker shows all veins while equipped; Ore Prospector / Ground Sounder show theirs. NovaTech Extractor
  chops/reaps the targeted node, else digs. Builder's Chest bonus = Tradesman's sifted bag + 25 % outfit.
  Storage expansions verified (client bag 60 after one). Spire travel from a Pioneer landscape now re-zones (was:
  dropped into the sea). Test harness: client.js uses one shared game-thread queue per client (env LMEMU_TESTQ2)
  run from the Lua dispatcher hook — stacking a hook per scenario run made Frida fail after ~30 runs.
  Held-click mining: SendInput mouse (WinDrive) still produces no cast/turn in-game; needs the user's real mouse.
- HELD MINING VERIFIED by the user (2026-10-11): holding keeps swinging, animates, about one dig a second.
  Recipe: harvest CasterInfo CastTime = RecoveryTime = 0 (any cast/recovery time = global cooldown → "ability on
  cooldown"), U68 = 1, Ua0 = 0; the client repeats ~4 casts/s (two per swing), so the server paces digs per tool
  (Abilities.TooSoon: 50 / harvest speed s, tool table ToolStats) and sends the swing animation to the swinger too
  (Emotes.ShowSwing includeSelf). Mining ring scale 1 (2 was clipped on steep ground). Carving near-surface voxels
  explicitly fixed floating shards on slopes. Multi-layer carve (air to layers 1-3) wedged the client's reliable
  stream — off. Open: on some mountains a top surface layer isn't carved and the ring draws under it.
- MOUNTAIN TOP LAYER + RING (2026-10-11, user-verified): digging now removes the mountain's top surface (the explicit
  near-surface carve, OpenAirMargin 2 m, was the fix; layer 1-3 carving not needed). The mining ring and sickle cone
  are client decals that are NOT drawn at low graphics/effects quality — the test client had been set to lowest
  (now original settings; low profile kept as testclient/UserOptions.ini.low). Nothing server-side controls this.
  Axe/sickle on the wrong node: the client lets any tool target any node, the server refuses and now says which tool
  is needed ("That plant needs a sickle." / "That tree needs an axe.", 5 s throttle).
- TOOLS FEEL (2026-10-11, user-verified): own swing animation = CastSuccess AnimationId (+0x3c → FUN_142d5dfa0
  animation selection) Enter at confirm (pick 226, axe 254, sickle 256) and a second CastSuccess with the Release
  (+1) 350 ms later. PlayAnim to the own player is unusable: its handler (FUN_142d7da00) always sets heading +
  "Translation" towards the point, turning the player and camera. Tool reach: CasterInfo MaxRange 6 m
  (Abilities.BaseToolReach, + Reach stat 15); beyond it the client turns the ring red (invalid); server allows
  2.5 m slack. Sickle/axe aim at the ring point (cast target point = player position). Right-click world objects:
  no menu (prop menu only for editable props). Underground: smoothing shell only within 4 m of the surface (it
  filled cave air with voxels). Per-tool reach bonuses: user said hold off.
- VOXEL TEMPLATES (.otd) + SPIRE (2026-10-11): format decoded (docs/terrain.md "Voxel templates"): cells are ApplyDiff
  cells (8000-byte changed mask + format 2), props carry model + transform. The Spire stamps base_portal_spire.otd once
  (marker TerrainEdits/spire_base.txt; corner cell (159896, 199, 159774)) and places its 7 template props; verified on
  screen (tiled walkways/walls, portal beam). Spire collision (helper trace): client path identical to trees, no packet
  field gates physics; the model's collision is only 4 pillars ~30 m out — the middle is open by design.
  Design items: 32 items ("Castle Hallway - Short"…) map to templates by name; using one adds a design
  (Templates.AddFromTemplate; template cell list = design block list). Designs now paste all cells (clipboard per cell).
  Test leftovers removed from MrLawKing's bag (moonlight, potion, prospector, cave seeker, 10000 cotton block).
- WORLD MAP: renders fine with the test client's original settings (flat colour was the low-settings profile). Done.
- TOOLTIP CATEGORIES (parked, findings): the tooltip object is Tooltips::ItemTooltipInfo (vtable 1446cab60); its
  "Categories" line reads the int array at +0x1b0 (ptr +0x1b8, count +0x1c0), which stays 0. The reply record's first
  u32 list (where we send the categories) lands in the array at +0x48 (26 seen at +0x58). Sending them in the second
  list too changed nothing (tooltip likely cached). Next: find what fills +0x1b0 (FUN_143463610 → vfuncs +0x38/+0x40).

- 2026-10-10: design placement verified: dev "placedesign <guid>" pastes a design via Building.Paste; Castle Hallway - Short (32076 voxels, 8 cells) rendered at the build site. Scenario re/test/scenarios/designplace.json.
- 2026-10-10: Designs window categories traced (FUN_1436966c0 + list builder): IsOnMarketplace -> Player Studio, IsDesignerTemplate -> Starter Castle (Template.Category.Daybreak, text 576947), else Personal/Collected (+0x10c). Designer template name = text id at TemplateBasicData +0xfc (else "Daybreak Template (Invalid Name)"). Design-item designs now designer templates (author "Landmark Team"): Starter Castle, real name, free of storage, rename refused (error 3). Client data: Resources/ExportedTemplateCategory(Map).txt, ExportedTemplates.txt.
- 2026-10-10: Creature movement fixed: AddLightweightNpc control-mode byte (+0x19c -> entity +0x41a0) now 2; client per-frame update (FUN_142e130e0) only follows server positions when non-zero. Wolves/deer run with animation; kobolds unchanged. Dev "npcmode n".
- 2026-10-10: deer (Damage 0) flee: within 8 m or when hit, run away at 1.3x run speed until 22 m (Creatures State.Flee).
- 2026-10-10: dev "itemaudit" writes ItemAudit.txt (server dir): per-kind counts, unhandled cast abilities (none left; 173/1019 are passives), items with no use. Fixes from it: 313 prop-teaching recipe items (any category), 20 design aliases (51 design items total), title fallback search (29 titles). Still no use: materials/currency (expected), 12 recipes whose output name differs (Brazier (Standing), Hanging Lamp...), Build Site Contents 914, Player Design 2433, Dungeon Builder's Starter Set 3760, 5 castle design items with no unambiguous .otd, unnamed test items 4444-4498/4519. Helms/gloves cat 177/178 have EquipSlotId 0 (check client equip rules). Scenarios proprecipe, designalias pass.
- 2026-10-10: world update loop guards each system (log once/min per system) after a hot-reload null in Consumables.UpdateHoloseekers killed the server thread. RecipeBase 0x23 (UI station open, 55 Lumen shop) / 0x06 (window closed) handled.
- 2026-10-10 (open): design export to item: Lua ExportToItem(templateGuid) (FUN_14352a680) sends TemplateBase 0x1B ExportToItemRequest {u64 design}; item 2433 'Player Design'; recipient gets TemplateBase 0x11 AddOwnershipResponse (reader FUN_143058780: u64, u64, bool, str, u64, str). Trades/Mail recreate items (new guids) so the item->design link must move with the item (like Chests.Move).
- 2026-10-10: Build Site Contents done (SiteContents.cs, Unlocks kind 7, Props.StowedZone 0, dev lapsesite; scenario sitelapse 3/3). Full suite 25/25 before it. Creature hitpoints now sent with spawns; NPC indicator not yet observed (hover/target untestable with synthetic input).
- 2026-10-10: Player Design items done (DesignItems.cs): export copies the design onto item 2433 (template row owned by the item guid), use re-owns it to the player (SaveTemplateResponse), trades move it to the new item, mail refuses it. Scenario designexport 2/2; trade/tradecoins/mailcoins still pass.
- 2026-10-10 (open): NPC name/health indicator still not seen. Max health is now known at spawn, but hit creatures never showed one either, so another term of requirement 21 (NotProp = TargetIsProp 106 op 2, or TargetNotPropProfile 22/39) probably fails for lightweight NPCs. Trace the TargetIsProp requirement factory before changing anything.
- 2026-10-10: tried PlayerUpdate 0x53 UpdateStat {u64 guid, list{u32 stat, i8 kind, u32 base, u32 mod}} stat 1 = max health (TargetMaxHealth FUN_143656820 reads stat 1 at entity+17000) + hit points at spawn: still no NPC indicator; reverted both (bandwidth). Next suspect: NotProp (TargetIsProp type 106) for lightweight NPCs, or the indicator only on target/hover.
- 2026-10-10: refactor: WorldSession.TakeFromStack (remove from one stack + delete/update to client) used by Chests, DesignItems, Salvaging, Trades, item delete. trade/tradecoins/designexport/gear/unoutfit pass.
- 2026-10-11: HEAL now clears placed blocks too (refund like Remove), i.e. back to natural terrain (0f74912); in-game check pending (needs a real click).
- 2026-10-11: TERRAIN REGENERATION (Landmark wiki "Gathering": land heals ~20 min after destruction; gathering only on unclaimed land). TerrainEdits.Regenerate: each minute, cells unchanged for 20 min lose every override outside build sites (Claims.BoxesIn), broadcast; empty cells' files deleted on Flush; LastEdit = file time at load; Spire stamp cells permanent (MarkPermanent). Dev regendelay <s>. Scenario regen passes.
- 2026-10-11: ORE rework. ContinentVeinData406.vdt sections = (layer, VoxelBiomes id, 1, cells): layer 0 surface biomes 47/50/54 (736..1712 m), layer 1 cavern biomes 41/62 (-992..624 m) -> a surface/cavern CELL MAP, not veins (old code read biome ids as VoxelResourceGeology ids: wrong ores, overlay everywhere). v2 vein groups: ResourceVeinGroups 11 "Landmark v2 Surface" (copper 6, tin 5, iron 5, marble 4, amaranthine 3, agate 2, sapphire 1 = DENSITY_ON_SURFACE) and 12 "Landmark v2 Caves" (silver 30, iron 30, alabaster 25, gold/ruby/emerald 20, obsidian/diamond 15 = DENSITY_IN_CAVES); RESPAWN_TIME_DIRTY 1200 s. Vein cells: stable hash share 3%/15% (stand-in until the client's vein generator is decoded). Overlay draws each cell with its ResourceVeinGroupVeins id. holoseeker/farseer/consumables pass.
- 2026-10-11: CAVES decoded (helper): .cav/.cpa formats in docs/terrain.md; CavePieces.IsAir on the server (verified vs Python reader); shell no longer limited to 4 m, never fills cave air; cave travel re-zones to 406 and lands on the cavern floor (verified in client: y 293 -> 292.3, cave rendered). Vein generator decode in progress (same helper).
- 2026-10-11: ORE VEINS = SERVER STATE (helper decode: client never spawns veins; VeinProvider spawn uses a global RNG; server writes ore into cells). Built: VeinGenerator.cs (walk FUN_143f168b0 / cells FUN_143f17f90 / apply FUN_143f17220, matches Python ref exactly), OreVeins.cs store (full vdt: pools 447,111 surface / 590,282 cave, 15 vein kinds, 36,884 veins; per-256 m region generation around players with fixed seed; surface start = ground, -normal; cave start = .cav air/rock boundary; ore layer flag 1 + ARRAY_INDEX composed under edits in TerrainEdits.BuildUpdate; ManifestEntries/Announce (21-byte notes); Sphere counts ore voxels; 1 ore per 8 voxels; disturbed veins move after 1200 s). Verified: region gen ~30-60 veins / 50-120 cells per region; digging into a cave gold vein gave Gold Ore (39). Not yet seen visually (client minimized, user at PC). Scenarios farseer/holoseeker log text -> "vein segment".
- 2026-10-11: ORE VERIFIED IN CLIENT: seabed tin vein (320, 975, 275 local): ore drawn as a rocky blue-grey patch blending with the terrain (flag 1 material-only works, no cube artifacts); Farseer Pick overlay traces the vein from that patch into the slope; mining it gave Tin Ore (44 ore voxels -> 5 tin) + dirt.
- 2026-10-11: NPC NAME/HEALTH BARS DONE: live Frida walk of requirement 21 (ExpressionNodeLogicalAnd): IsPc op2=1, IsProp op2=1, IsProfile 39 op2=1, TargetMaxHealth=0. TargetMaxHealth = FUN_143656820 -> resource container at entity+0x4268 (FUN_143ad0540: max of resource type 1), filled by ResourcesBase SetCharacterResource for that guid (not UpdateStat). Creatures now get a Life resource (closest Resources.txt Life row, value scaled) on spawn and every hit. Verified: 'Kobold' nameplate + green bar that drops on hitnear.
- 2026-10-11: WEAPON ATTACKS: cast ids classified from InputActionGroupEntries (ACTION_TYPE 1, ACTION_ID = ability; TRIGGER 9 = Primary/LMB, 10 = Secondary/RMB, 11 = Special/E; DISPLAY_ON_TOOLTIP 0 = combo follow-up): 760/799/1049 basic, 816/1051 basic combo, 761/801/1050/1181/1182 power. Wiki weapon pages: Basic free, Power costs 55-74 Mana, chained stages hit harder. Server: damage x1.0/1.3/1.8/2.1/2.4, Energy cost 0/0/60/70/80 (WeaponAttacks.cs, Energy tracked server-side, sent after paying). NPC network (NpcAnimationNetwork.mrn) has Attack/Ouch messages (Wolf/Deer/... clips) so AnimationSelection 1/4 work for all creatures. Open: floating combat text (HUD FloatingCombatText indicator, trigger not traced); live weapon test needs real clicks.
- 2026-10-11 (open): FLOATING COMBAT TEXT: PlayerUpdate 0x66 CombatResourceEvent (reader FUN_142b8e060): u64 source, u64 target, u64, i32 change (neg = damage), u32 resource type (1 health, 11 armor), u32 x3, bool, u32; dispatcher (part_0065 ~13500) -> FUN_142fee940 (health damage) / FUN_142fee3f0 (armor) when the local player is source/target -> FloatingCombatTextManager, gated by flag DAT_14536f720 (console togglefloatingcombattext, default 0 in .bss; client.js cmd didn't toggle it) and FUN_142feeee0. Now sent per hit; still no numbers even with the flag forced on in the test copy -> trace FUN_142feeee0 / FUN_142fee940 next.
- 2026-10-11: STARTER CASTLE: Getting Started "Your Home Is Your Castle" places Castle Courtyard / Wall Entry / Tower - Inline / Wall - Medium designs that are not items -> every player had the Starter Castle set (ExportedTemplateCategoryMap category 1 = 28 templates; 28 castle design names in the text table). Mapping by name + size families (walls 12x50 at 24/72/168 + rampart corner/entry; rooms 86 m high: room_v2, tower_(corner), tower_(4_way); towers 124 m: tall_tower, tall_tower_(corner), tall_tower_(inline)); Modular Castle Window with Wall has no .otd (27/28). Served virtually (guids 0x5354...+index), cannot delete/rename. Design items with those names aliased to the same files. Goal hooks: Place <name> design, 1273/1274 basic/power attack, 835 open Lumen Station. Dungeon Builder's Starter Set (3760): 2 Barren Flameblade (3162) + Barren Bowman (3156). designitem/designplace/designalias pass; designexport/itemuses not re-run (C: full from pagefile while user's Minecraft ran; cleaned to 9 GB).
- 2026-10-11: HOTBAR question (user, test client): hotbar is saved per character (character_action_slot) and reloads — verified Farseer Pick in slot 2 after relog. The earlier loss could not be traced (log overwritten by the restart); CharacterDatabase.Save now logs failures (they were fire-and-forget and vanished) and BuildHotbars logs a saved slot dropped for a missing item. itemuses scenario moved to LawTest: MrLawKing's 40-slot bag is full of the user's test items (unpacks correctly refused with "need more room"). designexport 2/2, itemuses 8/8.
- 2026-10-11: FLOATING COMBAT TEXT SOLVED: wire opcode was wrong. PlayerUpdate dispatcher switches on (sub - 1): case 0x1b = PlayerTitle 0x1c, case 0x66 = CombatResourceEvent -> wire 0x260067 (0x260066 is NpcCharacterPersistStateUpdate). With the fix the whole path runs (fee940 -> feeee0 target lookup -> fee730 -> fecf70 add -> fed4b0 per-frame -> HUD indicator 0x1a draw) and a red damage number floats over the creature (screenshot). Still needs DAT_14536f720 (togglefloatingcombattext, BoolCommand) on: default 0, no key binding, chat "/togglefloatingcombattext" doesn't reach the console queue, no ini key -> in 0.589 numbers were off by default for players. Adhoc traces: re/test/scenarios/adhoc/fcttrace, fctdraw, fctcmd.
- 2026-10-11: SPIRE (user report): respawn/travel landed under the platform (offset +6 m Z is inside the Spire pillar; client ground snap = natural terrain below the stamp) -> Spire.Arrival (spire foot + (7.2, -0.5, -6.5), beside the Portal Crystal) and Teleport.To never snaps on the Spire footprint (verified screenshot). Trees/plants skip Spire.Covers (template 124x124 m + 2 m). Template props: 1871 Portal Spire (Hub), 575 Portal Crystal, 2942 The Crimson Parlor, 2894 Replicator (station 53), 2895 Repair Station (56), 489 Mailbox, 884 Resupply Chest; first three + chest still decorative. Replicator/Repair/Mailbox flagged (DynamicInteractionFlagging) + named; client never sent InteractionInfoRequest without hover -> needs the user's click test.
- 2026-10-11: SPIRE INTERACTION: client asked FullCharacterDataRequest (0x5E) for every Spire guid and got nothing -> entities stayed lightweight (+0x147c = 2 vs 0 for placed props), never queued for InteractionInfoRequest despite the 0x40 flag, proximity/pick registration and state 1. Spire.SendFull answers LightweightToFullProp; now the Replicator (…5350…04) is requested every 5 s near it. InteractionInfoReply handler FUN_142ee6b90: entry type -> +0x4a28 state, types 2/3 set +0x1457 bit 2 (outline), range -> +0x4118. Test crash 16:49 (client 9528): AV in ProxiedCharacter dtor FUN_142dee1b0 during a re-zone, adhoc Frida script attached; not reproduced without it. LoadingScreenDistance = 0: every teleport re-zones. User: "spire has no collision" (earlier trace: model collision only the 4 outer pillars) - recheck after full-prop fix.
- 2026-10-11: HOVER OUTLINE: InteractionInfoReply entry = guid, u32 type (-> +0x4a28), 4 x u32 colour, u32 (-> +0x4a2c), f32 (>= 0 -> FUN_142e23e10), f32 range (-> +0x4118), u8 outline (-> +0x4a30). Highlighter FUN_143324bd0 clears the outline while +0x4a30 = 0 (we always sent 0). Render object +0x42c: 0x80 outline allowed (from entity +0x1459 bit 0), 0x40 outline on (FUN_143c45310). Colours FUN_14357b090: mode 1 (in reach) (2,1,0) gold, mode 2 (out of reach) red, 3 (0.5,2,0.5), 4 green, 5 yellow. Verified on Spire Mailbox (red at 14 m). Spire Replicator window opens (user).
- 2026-10-11: OUTLINE/INTERACT findings (user wants hover-only outline): entity outline slots 0-6 (FUN_142e26fb0, entity +0x1ef8.. per slot); slot 3 = InteractionProcessor highlighter (FUN_143324bd0) fed by ProcessAutomaticInteractions (FUN_143327260): nearest flagged entity in the proximity grid (game +0x32030), gold in reach / red out of reach. Live test with real mouse (user OK): moving the cursor on/off a Replicator changed no slot; only slot 3 was set; in play the cursor is locked to the camera, so there is no separate hover outline for props. Server has one switch (reply outline byte) covering both. Interact key in InputProfile_Default.xml is F ("Interact"), but in this build F holstered the tool (Inventory 0x16) and sent no InteractRequest; E = weapon Special. The user interacts by clicking. Auto-interact: entities with +0x4140 > 0 send InteractRequest when within that distance.
- 2026-10-11: user asked to remove the proximity outline -> InteractionInfoReply outline byte back to 0 for props (verified: no outline standing next to the Spire Replicator). Hover outline: user reports it works; to be checked by the user whether it survives.
- 2026-10-11: HOVER CURSOR: InteractionInfoReply's 4 u32 after the type are cursor ids (Cursors.txt), not colours: FUN_142e26a10 stores +0x2188 (interactable, in reach), +0x218c (interactable, out of reach), +0x2190/+0x2194 (other). Hover handler FUN_143328810(processor, game[0x6570] = entity under cursor) sets the cursor when: entity flagged, player +0x1459 bit 0x10 clear, PropProcessor (+0x596f0) +0x1d8 == 0, DAT_145352538+0xb4 == 0, controller (game+0x31ec0) mode != 1. Hover sends nothing to the server (only clicks: Command 0x16 select, Target 2, Command 9 interact). Usable props: cursor 10 in reach, no outline. User: cursor showed until they interacted with the Replicator; all conditions read fine afterwards.
- 2026-10-11: STUCK HOVER CURSOR (client UI, not server): opening the crafting window, its Flash UI calls HudStateManager:OnSetCursor(widget, 2) (Resources/Scripts/hudstate.lua -> Lua Ui.SetCursor, FUN_14351cb80 at 0x14351cbbc -> CursorManager (game+0x322c8) override +0x60 via FUN_142fcdcc0). OnCloseCraftingWindow never resets it (only BuildModePalette/Contacts/Watchlist do), and the override beats the hover cursor (FUN_142fcc9a0: +0x60 first, then +0x70 slots [hover, layer]). Override is cleared only by Lua SetCursor, the tool loop (only 15-26) or init. Fix would be one line in hudstate.lua (client file; not modified per no-game-file-mods). Verified with a write hook while the user used the Replicator.
- 2026-10-11: CLIENT AUDIT TOOLING (user: "decompile everything, see what needs the server, hook it up, how many unknowns"):
  - JPEXS FFDec 26.3.0 (Z:\Tools\ffdec, official GitHub release, user OK) decompiled all 80 UI widgets + Hud.swf -> re/as3/<widget>/scripts (ActionScript).
  - re/tools/coverage.py -> re/out/coverage.md: every stack-built packet class (538 client->server, 692 server->client) with opcode (base, sub) and direction (sent = passed to *(DAT_145351b08+8)), matched to the server (handlers, routers' case labels, payload sub bytes) + runtime counts. Static estimate: C2S 136/538 handled; S2C 93/692 sent. Heap-allocated packet classes (e.g. Recipe*RequestPacket) not caught yet.
  - Server Network/OpcodeStats.cs: counts every received/sent zone packet by first 4 wire bytes -> bin/.../opcode_stats.tsv (merged each minute); coverage.py ranks "seen in play, not handled".
  - re/tools/luabind.py -> re/out/luabind.tsv: 2081 native Lua bindings ({name, fn} tables in .rdata), 410 send packets (fn or callees reach the sender), with packet classes.
  - re/out/ui_server_map.md: 376 UI->Lua calls: 158 client only, 106 into Lua scripts (to follow), 44 send handled packets, 25 with gaps, 41 unmatched. Gaps: barber shop ConfirmCustomization, instanced games join/leave/respawn, RateClaim, ToolPacketActivate, refining/upgrade station ops, SalvageItem, palette AddEntry, vehicle management.
  - CURSOR: CraftingWindowWidget/RepairWidget set OnSetCursor("RepairWindow", 2) on mouse over AND out (client bug); WatchList/Contacts reset to 0 on roll-out. Original behaviour; server can't clear it.
- 2026-10-11: CLIENT LUA + LUMENS + BARBERSHOP (685e2d3):
  - Command 0x1E sub 0x53 InteractionRunClientLua (reader FUN_1432d5aa0, handler FUN_143311920): {string fn, f32 x3} -> client calls the Lua function (FUN_142e3d110) with 3 numbers. Server-side driver for the client UI (Game/ClientLua.cs, dev "lua").
  - HUD/barbershop Lumens read UI data source Character.Currencies, filled only by PlayerUpdate 0x49 PacketPlayerCurrencyChange {u64 guid, u32 n x (u8 currency, u32)}; currency 1 = Gold Coin. Now sent at login + every coin stack change.
  - Barbershop: CharacterCustomization.StartCustomization -> FUN_143001510 -> HudStateManager:OnOpenBarberShop. Options per slot from CharacterCreateOptionsManager (DAT_14535a6e0; CharacterCreateOptions.txt: id, race, gender, slot, actor, parent, ..., DEFAULT_UNLOCKED) filtered by DEFAULT_UNLOCKED or knowledge (player +0x7ae28, set by 0x11C sub 2 AddKnowledgeResponse {u32 n, i32 ids, u32 mode 3=add}). In the TEST COPY the option lists are empty (all 16 race1/gender1 hairs are default-unlocked) -> likely the manager is only filled through character select, which the direct-connect patch skips (same as the grey tints the user saw). To verify on the user's real client.
  - PlayerSet 0x11C sub 1 captured: u32 1, u64 guid, u32 n, n x (slot, option, tint) [12 B], u32 skin tint, string, u32 1, u32 1.
  - ZoneMessageOpcode: bases >= 0x100 written as 0x1xx00 (ToolBase 0x11700, ClaimVendorBase 0x11200...) can never match the wire (base u16 LE: 0x117 -> "17 01" = enum 0x1701). Fix when each is implemented.
- 2026-10-11: ArtData 0x116 TintDefinitionManagerData (wire 16 01, reader FUN_142bae270: u16 opcode + 8 lists) -> Zone_DispatchPacket case 0x8d -> FUN_142c6ec20: FUN_143af3410 (tint manager from files) + FUN_142ffa8e0 (CharacterCreateOptionsManager map from CharacterCreateOptions.txt etc.). Never sent before -> empty options map (barbershop dead) and probably the grey tints. Now sent (8 empty lists) at first ClientIsReady; options map 184 entries; barbershop purchase verified (mohawk, 15 Lumens). Test harness client.js: request kind 3 'createopts' (load only; the build needs this packet).
- 2026-10-11: SPIRE FALL-THROUGH FIXED: a re-zone builds a fresh voxel engine that meshed the destination before it had the changed-cell manifest (sent only at the re-zone's ClientIsReady), so stamped/edited cells (Spire platform) had no collision; client requested 0 cells after re-zone. Teleport.Rezone now sends TerrainStore.SendManifest right after ClientBeginZoning + ZoneDetails. Verified: lands at 1203.7 and stays. Note: plain TeleportToLocation hangs the renderer (QuerySimpleVisibility FUN_144009a40) - never use it outside FinishRezone.
- 2026-10-11: AUDIT PROGRESS: all 165 UI SWFs decompiled (re/as3), window Lua scripts (testclient/UI/Default/*.lua) included in re/tools/uimap.py; re/tools/verdicts.tsv marks checked no-work items (dev world-edit/area tools, ToolPacketActivate informational, unreachable UpkeepManagementWindow, no vehicles/merchants). UI data sources listed in re/out/ui_datasources.md (118). Remaining UI->server gaps (29): ClientInventory.MoveOverflow; RadialMenu.Assign/RemovePaletteSlot; Voxel.SelectShape (PalettePacketAddEntry); Voxel.TweakModeConfirm; Ui.SelectPlayerTitle; claim music/contest/instanced games/RateClaim; Recipe.CancelRecipe, Forge upgrade tab; Teleport.TravelConfirmation (warp); Ui.CancelEffect (needs buff icons: Character.AbilityEffects); bug/claim reports; PoN store.
- 2026-10-11: HOTBAR: ActionManager 3 DefaultSlot {bar, slot} / 4 DefaultAllSlots handled (client resets itself; server forgets saved rows) (54650c3).
- 2026-10-11: BUILD PALETTE (WIP, commit after 54650c3): PaletteBase 0xF5 u16 sub: 1 Create, 2 Delete, 3 AddEntry, 4 DeleteEntry, 7 SlotUpdates (C->S); 5 PaletteUpdate (S->C, handler FUN_142e74800, reader FUN_142b8c490: string name, u64 id, u32 n x (u32 slot, u32, u32, u64), bool, u32). RadialMenu.AssignPaletteSlot (FUN_14354aca0) needs HUD palette id (game+0x32360 +0x28) > 0. Server now sends a palette (id 0x5041000000000000|guid); client manager (DAT_1453524a0+0x753e8, vtable+8 lookup) holds it; HUD id still 0: HUD loads palettes via FUN_143393b50(hud,id); FUN_143395950 takes DAT_145351da8+0x1528 when HUD id == -1 and requires build mode (FUN_142c4e800(game,1)). Next: check HUD in build mode, find the default-palette source, capture AddEntry/SlotUpdates bodies, persist.
- 2026-10-11: CLIENT PACKET ID REGISTRY: FUN_143419ce0(value, "cPacketId<Base>", "c<Family>PacketId<Name>") registers 1174 ids by name (value = sub<<24 | base<<8), families PlayerUpdate (126), Command/Admin (380 each), Claims (92), ClientUpdate (45), VoxelConstruction, Prop, Mail, TerrainCell, ProfileStats, Chest, LootPinata, HeroicMovement, KnownProp, Tool, Interaction... -> re/out/packet_ids.tsv (re/tools/packetids.py). Confirms PlayerUpdate 0x67 = CombatResourceEvent, 0x66 = NpcCharacterPersistStateUpdate, 0x49 = Currency. Rule: PlayerUpdate subs are 1 byte (wire 26 00 xx); ClientUpdate/Command subs are u16. Fixed two 4-byte PlayerUpdate sends that the client dropped: SetPropTint (0x7A: other players never saw tint changes) and EffectTagCompositeEffectsEnable (0x1F). PlayerUpdateSetPlayerBio 0x80 does not exist in the client.
- 2026-10-11: SENDSELF: docs/sendself.md maps SendSelfToClient blocks to ClientPlayer offsets and the managers/families using them (achievements 0x38658, titles 0x595b0, currency 0x75d68, equipment 0x75e70, experience 0x75f18, items 0x76938, crafting 0x7aa00, known props 0x7adf8 = model Unknown40, customization knowledge 0x7ae30 = Unknown41, class progression 0x7a9d0, inventory filters 0x7ae60...). Known props list is sent empty -> learned props (PropTaught) never reach the client; KnownProp 0x115 (sub 1 AddPropResponse S->C, sub 2 CreatePropItemRequest C->S) unhandled; enum KnownPropBase 0x11500 wrong (should be 0x1501), likewise ToolBase, DesignBasedStateMachineBase, InteractionBase, DebugDrawBase.
- 2026-10-11: SENDSELF FULLY ATTRIBUTED: SendSelfToClient_Read fills ClientPcData (ctor FUN_142f27f70, dtor FUN_142f296c0 name every member by vtable). New: 0x7ae48 = customization knowledge type 4 (FUN_1435d7a50: AddKnowledge type 3 -> 0x7ae30, 4 -> 0x7ae48); 0x75088 bool + 0x75090 set = ClientFirstTimeEventManager (enabled, seen); next u32 list dropped (FUN_142f31eb0 empty); 0x75670 CharacterStat; 0x2b0 HashListMap<int,int>; 0x75ac0 signature phases; 0x114/0x118/0x130/0x148 plain fields/arrays; 0x753f8 cooldowns; 0x75638 effects; 0x75bf0 AcquireTimers; 0x75df0 DefinitionVariableManager (lists 0x75e00, 0x75e30); 0x75fc0 inside experience; 0x76a58 class loadouts; 0x76db8 missions (5 lists); 0x77198 skill instances (0x771a0 points, 0x771d0 list).
- 2026-10-11: FIRST-TIME EVENTS (help pages): FirstTimeEventBase 0x6D u8 sub: C->S 1 Trigger {u32} (Ui.TriggerFirstTimeEvent from ClientTimerDefinitions.txt timers; only when enabled and unseen, FUN_143580bf0), 3 Clear {u32, -1 all}, 4 Enable {bool} (debug); S->C 5 Script {string fn, string params, u32 event, bool clear} (FUN_143580670 runs fn with comma-split params via FUN_142e3fdf0/FUN_142e3d110, then marks), 2 State {u32, bool clear} (FUN_1435802c0). Pages: HelpPages/*_FTE.html opened by Lua ShowHelpWindow(size, page, position) (Scripts/global.lua, reports UI event 32). We sent SendFirstTimeEvents = false, so none ever ran. Game/FirstTimeEvents.cs: UI events -> events, pick/axe equip, first gear wear; seen ids = Unlocks kind 9, off = kind 10; dev "fte <id>|clear". Verified on LawTest: Inventory help opens once; seen set survives relog (no repeat trigger requests).
- 2026-10-11: ITEM RECORDS vs LIVE: parsed the captured SendSelf items with the server model (scratch tool) and compared with item definitions: ClientItem +0x10 ItemRandom block (FUN_142b87690: quality, 4 u32, List<ItemRandom::StatInfo> {stat, text -1, f32 value, f32 1, bool 1, f32 value, f32 1, f32 1}, List<CatalystRandomInfo> 3 u32) twice (+0x10, +0x68), then +0xc0 f32 = RefItemQuality COST_MULTIPLIER. Live quality == definition quality for every item. Tool stats live: Adventurer's pick/axe 3=100, 13=50, 12=80 (ItemCategoryStatMappings: cats 2, 3, 34 → 12, 3, 13). Player Design item 2433: +0x128 {design id, name, author}. Server now fills quality, multiplier, tool stats and the design block (Inventory.FillDefinition, DesignItems.ItemBlock). Item tooltip opened via FTE Script packet (dev luas): quality colour shown; stats text not shown (other builder, to trace); "There are no categories" despite intact category list (to trace).
- 2026-10-11: ITEM TOOLTIPS: Inventory 0x12 ItemTooltipReply record (FUN_142e78e10) → Tooltips::ItemTooltipInfo slot 7 FUN_1436a3a80: int lists src+0x18 → +0x180, +0x30 → +0x198, +0x48 → +0x1b0 (the "Categories" line + display category FUN_1436a5ac0 gating stat lines); stat list +0xc0 entries {stat, name text, f32 value, f32, f32 base} (FUN_143466d50; value via FUN_142dbf1f0 display multiplier). Server sent the third list and stats empty. Now: categories in list 3, tool stats with CalculatedStatDefinitions NAME_ID. Verified: "Pick", 80 Harvesting Damage / 50 Harvesting Speed / 100 Harvesting Size.
- 2026-10-11: PROP PLACEMENT REFUSALS (user bug: placing off-claim left a prop that couldn't be moved or cancelled, stuck in the place animation): PropBase sub 2 header FUN_142f09c10 = {u64 player, bool success, if false: u32 error, u64}; FUN_142f1ab30 on failure resets placement (FUN_142fe7780) and shows the error: 4 chest cap, 5 reason text, 6 PropLocInvalid, 7 no-trade item, 8 rule set, 10 no claim permission, 11 soulbound chest, 13 not in edit mode. SaveReply (sub 4, FUN_142f1c7b0) failure: "That object can not be placed there." Server never refused. Now: placement start off a build site (or without build permission) → sub 2 refusal error 10, nothing spawned; Save outside → SaveReply failure error 6 (gizmo stays). Props need a claim (unclaimed land stays open to mining/terrain edits). Verified refusal via dev "beginprop #<item>"; palette path (Prop.StartPlaceNewObject) not drivable from the server (Lua call channel makes Table:Method).
- 2026-10-11: CRAFT CANCEL: RecipeBase 3 CancelRequest (no body; FUN_142e9f790, only sent when the client itself started the craft) → 4 CancelResponse {bool, string} (printed, state reset). Server drops the job and refunds its components (dev craft/cancelcraft). Verified refund (30 components back).
- 2026-10-11: OVERFLOW: container 7 (ContainerId.OVERFLOW, data source Character.Overflow, inventory third tab shown only while non-empty; expiry countdown from item timed delete). Bag-full items were dropped; now go to overflow (kept, no expiry: live value unknown). InventoryBase 0x1C RequestOverflowAbsorb {u8 0} (Lua ClientInventory.MoveOverflow) tops up bag stacks then moves stacks to free slots (ItemUpdate container/slot). Verified: 3 items to overflow at 40/40, retrieved after freeing space (13/40 shown).
- 2026-10-11: WARP NETWORK (not implemented): WarpNetworkClientRequestPacket (0x113 sub 4) {u32 type, u32, u32, u64, u64}: type 1 UpdateZoneNetwork (Lua, sent automatically per zone, 25 seen), type 2 WarpToNode (TravelConfirmation → FUN_143453bf0). Server answers WarpNetworkZoneNetworkPacket (sub 2, reader FUN_1434504c0: layers → nodes, costs base/zone/continent/place/type at +0x230..0x240). Nodes are Portal Spire hubs / Portal Crystal nodes; ControllerLocalTravel (FUN_142fb55e0) opens the travel confirmation when interacting with one. Needs world portal props first.
- 2026-10-11: CONTESTS: RatingBase 0xA7 u8 sub; 0x1D RequestContestsRequest (Lua RequestContests; repeated ~30× when unanswered) → 0x1E RequestContestsReply {u64, u32 n, n × 0x58-byte entries FUN_142d02390} (FUN_142d09110, fills data source "Contests"). Now answered empty; asked once per login. AddVote (8, Lua RateClaim: strings "Claims", claim id, vote) / CanVote (0x18 → 0x19 {u64, string, bool, u64}) / VoteReply 0x12 logged, not implemented.
- 2026-10-11: BUFF ICONS (not implemented): data source Character.AbilityEffects (BuffsWidget columns Name, GUID, descriptionId, imageSetId, nameId, secondsTotal, stackCount, isGood) built by FUN_142d87fe0 from ClientEffectData map ClientPcData+0x75638; filled by Abilities AbilityPacketEffectStart (0x27 case 0xC, reader FUN_142d64890 after header FUN_142d65900: u64 +0x20, u32 +0x28 ... large) handled by FUN_142d7b690. No client table holds effect icons/names (lookup via ability manager DAT_145351c48+0x32070). Ui.CancelEffect needs this first.
- 2026-10-11: BUFF ICONS DONE: Abilities header (FUN_142d65900) = u16 sub (again), u8, u32. Sequence per buff: EffectStart 0x0C (FUN_142d64890: u64 key, u32 key, u32 effect id, 6 u32, bool, u32, 4 u32, u32, u64, u64 target +0x78, u32, 4 u32, 4 f32 rot +0xa0, u32 anim type +0xb0 (0 none), u64, u32, 4 f32 +0xf0, 5 u32, 4 f32 +0x100, 8 u32, bool, u32) puts it on the character (+0x240); EffectInitialized 0x1C (FUN_142d64620 → FUN_142d7b4f0 → FUN_142f2b140: u64, u32, u32 id, u32, u32, f32 duration, u64 caster, u32 name, u32 desc, u32 image set, u32 good type 2, u32, u32 stack, u32) adds the icon (row GUID = id); EffectEnd 0x0D (FUN_142d64420: u64, u32, u32 id, u32, u64, u64 target +0x40, u32, f32, u32 type, then if type != 1 two u32 — missing them = parse fail) → +0x230 lookup on the character, +0x248 (LocalProxiedCharacter FUN_14327dd60 → FUN_142f32a50) removes icon. Type 6 avoids harvest messages. Cancel button: RequestCancelEffect 0x1E {i32 id}. Verified (hooks adhoc/effecthook.js + screenshots): potion icon with countdown, removed on cancel, stays over time.
- 2026-10-11: CLAIM RATING: Lua Claims.RateClaim (FUN_1435329d0) sends RatingBase sub 8 AddVote twice ("Claims", then "Contests"); writer FUN_143534b40: string category, string item id (claim id decimal), string, string, f32 score 1.0, u64. VoteReply sub 0x12 (FUN_142d02880): u64, string item id, bool, u64 total (stored on the client's list entry). Server: one vote per player per site (Unlocks kind 11), not on own site, replies with total (Game/Ratings.cs; dev ratevote). Verified storage + duplicate refusal.
- 2026-10-11: SITE MUSIC: ClaimsBase 0x42 SetMusicOverrideId {u64 claim, u32 music (ClaimMusic.txt: 1 none, 2 Tundra, 3 Tropical, 4 OldGrowth), u8 1} (FUN_142d334c0, which also sets its own record +0xf4). Others learn it from ClaimInfoUpdate (sub 5, reader FUN_142d029f0) kind 0x16, field +0x278 (first u32 after the time block) → claim +0xf4 → FUN_142d29390 (walk-in) plays via FUN_14347e590. Server: owner's Unlocks kind 12, broadcast 0x16 on change, one 0x16 per site with music after the AOI full update at login. Verified with hooks (adhoc/musichook.js): applied 3, music lookup 3; resent after relog.
- 2026-10-11: BUILD PALETTE SAVED: PaletteUpdate entry = {u32 slot key, u32 slot, u32 type, u64 value} (FUN_142b9bf70 map + FUN_142b8c560). AddEntry (sub 3, FUN_142e75cc0 from AssignPaletteSlot args slot, type, value) {u64 palette, u32 slot, u32 type, u64 value}; DeleteEntry (sub 4, FUN_142e75f10) {u64 palette, u32 slot}, then SlotUpdates (7, logged). The radial menu lists palettes while the HUD id is -1 (FUN_143397410) and picking ours selects it (FUN_143393b50); assigning then goes through the server. New table character_palette_entry (migration 20261013090000), palette re-sent whole after each change. Verified: migration applied, saved slot parsed by the client at login (adhoc/palettehook.js). Radial-menu selection itself not driven (needs real input).
- 2026-10-11: LIVE UI TEST (user allowed real input, test client): buff right-click cancel → AbilityRequestCancelEffect → icon gone (PASS). Off-site palette placement → "You do not have permission to edit that prop." (error 10 text), nothing stuck (PASS). On-site placement + Escape: client sends Save repeatedly during the gizmo (first Save already places/pays) then PickUp (sub 5) on cancel; the prop's composite effect (PropEffects) outlived the despawn (floating wisps = likely the user's "stuck showcase"). Fixed: Props.SendRemove switches the effect off before RemovePlayer; verified wisps fade. First-time help pages fired in real play (Designs, Blocks, Props). Not reachable in 0.589 UI: radial palette (BuildModePaletteWidget hotkey commented out; no PALETTE drag source in the normal build bar) and the site music list (not shown in Site Management Info tab).
- 2026-10-11: HELD-SWING FLOOD: holding a gathering tool sends ~30-60 AbilityCastAttempt/s (item stats on or off; server paces digs with TooSoon and answers each). Client pacing branch (FUN_142d71800 → controller game+0x31ec0 float +0x280 = FUN_142d77f20 recast + rec +0x9c + rec+0xa0) never ran: +0x280 stayed -1 (hook adhoc/recast2.js). Gate: FUN_142c320b0(game)==2, FUN_142f67f20(controller), record byte +0xd7. Record = CasterInfoReply packet layout (+0x68 = U68 confirmed by hook), so +0xd7 = top byte of the u32 at +0xd4; experiment setting it inconclusive (live input failed to reach the game). Open.
- 2026-10-11: MINING YIELDS BY SLOPE + BIOME WALLS: each swing used to pick any of the biome's resources (a flat beach gave stone/sand/dirt at random). Now YieldAt filters the biome's procedural materials (VoxelBiomeMaterials) by slope: VoxelProceduralMaterials ANGLE_MIN/ANGLE_MAX = up component of the ground normal (OceanCliff 0-0.7, Ocean Beach/Shallows/Valley 0.7-1); slope from the baked height grid (TerrainMaterials.SlopeAt, ±0.8 m). Hole walls (TerrainEdits.ShellMaterial) = the biome's own voxel material for the yield (procedural → VoxelProceduralMaterialMaterials → VoxelMaterials ARRAY_INDEX) instead of the fixed forest set. Verified: 6 flat-beach digs → dirt/sand only, walls sand-coloured. Still random among the flat materials (the client's placement noise isn't ported).
- 2026-10-11: SITE GAMES (Game/InstancedGames.cs, docs/instanced-games.md): a Game Table (IgRuleSetPlacementProps 1346 Deathmatch, 1347 Team DM, 1226 Team Zone Control, 2313 Adventure) on a build site hosts a game; rules resolved from IgRules/IgRuleTypeParameters/IgRuleParameters (positional overrides) + IgGeneralSetting (min/max players, countdown). Packets decoded: InstancedGameBase 0x10D (wire 0D 01; enum fixed to 0x0D01, GetOpcodeBytes pads odd hex) u32 sub: 3 Scoreboard (f64 values!), 6 State {state, next, ms, guid}, 7 Event (kill feed), 8 Request {i16 type, i16 result, i16 value, i16, i16, u64 game, u32, string} (type 6 = membership), 9 HUD (round timer end = synced ms clock), 0x14 TeamMembers. Site record / ClaimInfoUpdate 0x11 carry the game (FUN_142cffe50 → entry +0x140) for the walk-on window. Join/leave (walk-on buttons), countdown, running round with HUD timer, PvP weapon hits inside the game (Abilities → InstancedGames.Hit), kills/score, respawn at spawn props after the delay (no death window), end scoreboard, reset. Verified on LawTest: HUD "Deathmatch" + "Time Left" counting from 10:00, client entry shows game/state/players, manager membership on join/leave, 4-round stress 32/32. Not done: property editor/authorship, queues, observers, zone control points, Adventure story tools; winner banner shows "Team 1" (UI Lua not readable). Also fixed: duplicated AttackGoal call per weapon swing.
- 2026-10-11: RE-ZONE HANG RULES: Teleport.FinishRezone was never called, so RezoneTarget was never cleared; now called from the repeat ClientIsReady (only clears; no extra TeleportToLocation). Client hangs in QuerySimpleVisibility (FUN_144009a40 looping over its visibility list) reproduced and isolated: plain re-zones 0/8 hang; hit points or instanced-game state/HUD or a claim info update in the same burst as ClientBeginZoning hung 1 in 4 respawns and 2 in 3 round starts; a zero-length move fell through to plain TeleportToLocation (distance 0 is not > 0) and hung too. Fixes: Teleport.To always re-zones when LoadingScreenDistance is 0; a second re-zone while one loads retargets it; site games hold a moved player's packets until arrival + 1 s; Claims.SendGameInfo skips players mid-re-zone; the death window's graveyard respawn sends hit points + window close on arrival (Health.Arrived). Verified: 32/32 game stress, 3/3 death-window respawns, cave/Spire tp_test 4/4.
- 2026-10-11: STORY TOOLS (Game/StoryTools.cs, docs/story-tools.md): ObjectScripting (0x111, wire 11 01; enum GameScriptingBase fixed to 0x1101) decoded: C→S 0x0C..0x16 (supported objects / events / actions / actions+events+defaults, add / list / remove / remove all / restore defaults / potential link / remove+add), S→C 1..11. Definitions {u32 id, u32 text, u32 sort}. All 291 link sentences recovered with their text ids (re/out/link_texts.tsv, by hashing Global.Text.<id>); ids = text ids, assigned by prop name (torch, door, switch...) and story-tool prop ids (2325 Logic Filter, 2330 Travel Point, 2344 Story Node, 2345 Dice, 2367 Timer, 2655 Starting Location, 2898/2899/2900 Focus/Damage/Reset, 1590 Linking Area) and spawners. Links in world_prop_binding (migration 20261014090000). Play Mode (ClaimsBase 0x43; merged with the existing SetAdventureStatus) now broadcasts ClaimInfoUpdate kind 0x13 and the record carries the mode (+0x348 → entry +0x370). Runtime: toggles, locks, cogs, dice, timer, nodes, damage, reset, areas, spawners (Creatures.SpawnStory/DespawnStory/DamageStory, death callback), clickable switches. Verified: the client's link editor accepts the definitions (links.json 11/11), runtime chain switch→torch→timer + spawner creature (storyrun.json 14/14), on screen the torch goes out and a Slaug spawns. run.py: capture step + {hex:name}/{cap:name}. Not yet: property editor (settings), creature travel/faction/emotes, Starting Location events.
- 2026-10-11: PROP SETTINGS WINDOW (Game/PropSettings.cs): PropBase 0x1B EditSettingsRequest {u64 prop} → 0x1C reply {u8 sub, u64 player, u64 prop, bool, properties {i32 key hash, record: string label, u32 label id, string key, f32 value, default, max, min, step, u32 help id}, dropdowns, string name, string text}; client builds checkbox (0..1 step 1) or slider (FUN_142f18330); edits come back as UpdatePropInteractionState 0xFD type 2 {u64 prop, u32 2, i32 key hash, u32 value...}; key hash = one-at-a-time of the key string (verified: the client's own setter's key matched ours). Settings per kind from EditableProperties; table world_prop_setting (migration 20261015090000). Story runtime now uses timer length/auto-restart, dice weights, damage-node amount (of the node that set the chain off), spawner respawn delay/enabled. Verified propsettings.json: settings window opens with 2 settings, timer set to 5 s via 0xFD, Play Mode switch → timer → torch out after 5 s. Not yet: dropdowns (Story Node mood/stance/emote/display), heal option.
- 2026-10-11: STORY NODE: settings drop-downs (list B: u32 key, record, options {u32 text, u32 value, bool}) for Mood/Stance/Emote from the client's emote words (559123-559192; stance/emote values = AnimationSelection ids). Creature actions from the node that set the chain off: stance/emote animations (Creatures.PlayStoryAnimation → AbilityPlayAnim), mood kept, Say text = node's prop text in say chat (Chat.NpcSay, creature guid + name). Screenshot verified: real Settings window with Mood/Stance/Emote drop-downs, Text Duration slider, "Announce Emotes in Chat" checkbox and the Text editor. storynode.json 11/11. Dev: proptext, propset.
- 2026-10-11: STORY CREATURE AI: Travel Point walk (scripted, before aggro; arrival → home + "creature arrives" Links), faction enemy/neutral/ally (neutral/ally never aggro, drop chase), focus on Focus Node (or the activator when pass_through_look_at), stop focusing, combat enter/exit → spawner Links. travelpoint.json 8/8.
- 2026-10-11: STARTING LOCATION: Claims.ArrivalOf (Starting Location prop 2655 on the site, else centre) used by My Sites travel (ability 186), the death window's Build Site respawn and dev tpclaim; StoryTools.ExpectArrival + PlayerArrived (from Teleport.FinishRezone) fire "teleports to / respawns at this Build Site" after the re-zone. Story PropEffects skip players mid-re-zone. startloc.json 7/7 (landed at the start location 8 m off-centre).
- 2026-10-11: ADVENTURE GAMES: rule set 8 cooperative (no PvP), creature kills score (Creatures.Kill → InstancedGames.CreatureKilled), difficulty tier on the walk-on (setting 10), rounds put the site in Play Mode with a fresh story (Claims.ApplyMode, also used by F5) and take it out at the end; Adventure table 2313 linkable with "Give reward and teleport to a Chaos Cavern" (577090) = complete. adventure.json 13/13.
- 2026-10-11: ZONE CONTROL: control points (prop 1224) captured by a lone side within 5 m × scale over Time to Capture, Points Per Capture + Event 9, score ticks; HUD ControlIndicators {+0x34 progress %, +0x30 colour side, +0x3c owned, +0x38 owner side} (ControlIndicator.as reads Progress/ProgressColor/Owned/OwnerColor). Fixed: owned points showed an empty bar (progress now 100 in the holder colour). zonecontrol.json 8/8; indicator seen on screen.
- 2026-10-11: PROP ANIMATIONS (Game/PropAnimations.cs): UpdatePropInteractionState behaviour type 0 (ClientPropSimpleAnimationStateBehavior; registration order 0 simple anim, 1 comp effect, 2 setting, 3 Morpheme request, 4 restriction, 5 graphics) {u64, u32 0, u32 set, state {u32 index, u32 index, u64, ints [set,0,0,0], strings}, bool}; FUN_1433c8a90 → FUN_143bad550(entity+0x4d40, set=ints[0], index=state[1]). Sets from SimpleAnimationSets.txt chosen by prop name. Story on/off now swings doors etc.; moving props start shut in Play Mode (switches start off: scenarios updated to "turns on"). door.json 8/8 (client door entity runs set 1 index 0); storyrun/storynode/propsettings/adventure all pass.
- 2026-10-11: GAME TABLE SETTINGS: the Game Table's prop settings window lists its rule set's IgRuleTypeParameters (names/ranges; bools as checkboxes); saved values (ig_rule_param_<id>) override the rule set per table from the next round (InstancedGames.RulesFor; Rules now keeps Values + Parameters, ApplyValues). Seen on screen: Time Limit 600 / Point Limit 20 / Points Per Kill 1 / Penalty Per Death 0 / Self Kill -1 / Respawn Delay 6. gamesettings.json 9/9.
- 2026-10-11: WARP NETWORK (parked, decoded more): WarpNetworkZoneNetworkPacket (0x113 sub 2, reader FUN_1434504c0) = u16 base, u32 sub, u32 world (+0x24c), u32 zone (+0x248), layers {u32 n; u32 layer id, nodes {u32 n; u64 node guid, record {u64, u32 type, u32, u64, u32, u32, u32, u32}}, u32}, u32. Nodes are entities the client knows (guid → +0x2a0 index). Local travel (FUN_142fb55e0): standing node + the HUD-selected node → cost = base (+0x230) + zone change (+0x234) + world change (+0x238) + place change (+0x23c) + type-2 nodes (+0x240) → GameEvents:OnOpenTravelConfirmation; confirming casts the warp ability (manager +0x244) and sends WarpNetworkClientRequest type 2 {u32 2, u32 world, u32 zone, u64 from, u64 to}; type 1 {u32 1, world, zone} asks for a zone's network. Portal props: 575 Portal Crystal (one in the Spire template), 1600 Ley Line Portal Node, 1871 Portal Spire (Hub), 1881 Underground Portal Crystal. Parked: no client data says where nodes stand (the live server had them); the Travel window already covers Spire / sites / friends / other landscapes.
- 2026-10-11: HOLE RIMS EXPERIMENT: TerrainEdits.RimMode (dev rimmode) puts rim vertices (sphere point above the baked ground) onto the ground instead of skipping them; side-by-side on a flat beach (dev carvefwd <ahead> <right> <radius dm>, carves in front of the player; holes told apart by a small marker hole after a first misreading) the new mode gives the cleaner rim, the old one a notched/blocky rim → default RimMode 1. Mountain-top rims: re-check with the new mode. WorldSession.Facing added.
- 2026-10-11: CONTESTS (Game/Contests.cs): RequestContestsReply entry = {string key, string name, u32 id, u32 state (1 listed), u64 submit start, u64 vote start, u64 judge start} (FUN_142d02390; the Contests data source lists state 1 only); SubmitContestEntry Claims 0x41 {u64 site, string contest, u32 n byte arrays (screenshots), u8} (FUN_142d07b90) → 0x18 ContestEntryResults {bool, string entry name, string contest name} (ContestSubmission.Success/Fail). Definitions in bin Contests/contests.json (default "Community Showcase"), entries in entries.json, screenshots in Contests/shots. Client gates sending on a server setting (hash 0xB5389567 in DAT_145351c18+0x2b68; source not found) and the web gallery manager. Dev: contests, contestenter. Held-swing test with synthetic input: no casts reach the game (needs the user's mouse).
- 2026-10-11: OBSERVERS + CANVOTE: InstancedGame ObserverRegistration 0x15 {u32, u64 game, u32 flag} both ways (handler FUN_1434391f0: flag 1 → manager +0x40 = game, OnOpenGameInProgressDialog; else close); observers get state/HUD/scoreboard. RatingBase 0x18 CanVoteRequest (4 strings + u64) → 0x19 {u64 site, bool may vote, u64 time} (FUN_142d024b0 → claim entry +0x360/+0x368, only used for contest-tagged sites). Lua InstancedGame.EnterObserverMode via the FTE script channel sent nothing (needs walk-on context). Swimming (HeroicMovement sub 3) is already relayed raw (coverage tool misses it).
- 2026-10-11: HARDENING: GamePacketReader/ProtocolPacketReader.ReadBytes (and so every ReadString) allocated the length the packet claimed before reading it: a forged string length of ~4 GB made the server try to allocate it. Now refused (EndOfStreamException, packet dropped by the existing catch) when longer than the bytes left, and copied in one block. Reviewed: WorldSession catches read and handler exceptions (InvalidPacketValueException disconnects); fragment reassembly never allocates from the claimed total (400-packet window); every packet-driven loop reads per pass; mail duplicates are summed per item. Limits added: Links max 64 per prop / 1000 per site (same Link twice = existing one), WorldSession.StorageLimiter (30 burst, 10/s) on Link add/remove/edit (refused → normal failed reply), ContestLimiter (2, then 1 per 30 s) on contest entries; prop settings are kept in memory at once and written to the database in one batch a second (PropSettings.Flush on the server tick) since sliders send every step.
- 2026-10-11: GAME EDITOR (Game/InstancedGames.Editor.cs, docs/instanced-games.md "Game editor"): the client's own RulesetObjectSettings window (Game Settings / Prop Settings tabs, Defaults / Apply / Enable-Disable Game) is server driven. Lua Claims.RequestGainActiveAuthorshipOfGame → request type 1 (no client reply handler): server sends GetConfigurationResponse (sub 0xD: current + defaults blocks {LocalizedString name, LocalizedString, map code → {u32 IgGeneralSetting id, string code, string, string value, u32}}, per-IgRules-row parameter definitions {u32 type, string, u32 name text, string, string min, string max} and values {string value, u32 rule type, u32 index}) and PropRecipe (sub 4: u64 game, bool enabled, bool has required, items {u32 id, LocalizedString name, u32 rule index, u32 role, u32 slot, u64 prop, bool include, u32 team, bool optional}, bool open → OnOpenInstancedGameSettings); type 2 / game 0 closes. Apply/Defaults = SetConfigurationRequest sub 0xE (u64 game, block, values, u8); Enable/Disable sub 0xF/0x10; Add = PropRequest sub 0xA {u64 game, rule, role, team, slot} → normal placement (bag item, else palette rules); Remove = PropRemove sub 0xB {u64 prop, u64 game, rule, role, slot} → pickup; Go To = TeleportIntraZoneRequest 0xFC sub 8 {f32 x,y,z,w, u64, u8} (served only to editors, on their game's site). Roles from IgPropRoles/IgPropRoleProps; general settings from IgGeneralSetting + RefIgGeneralSettingsTypes (client shows PLAYER_ADJUSTABLE rows; read live from its table DAT_14535a748+0x4d0). Saved per table: ig_general_<n>, ig_rule_param_<n>, ig_game_disabled; RulesFor applies player limits, difficulty, observers, PvP. Client quirk: map value reader FUN_1434270b0 stores its last u32 over the code string's vtable low half (harmless). ALSO FIXED: spawn point props (1223/1367/1371, and 2701/3012/3013) used model 1739 from the later-build prop dump, absent from 0.589 Models.txt → the client created no entity (spawn points were invisible). Props.FixMissingModels maps missing ids by internal name (→ 1699); restored props take the current model. Verified (screenshots + scenarios igprobe/igeditor/igprops): window titled Deathmatch with General / Game Type Settings, Apply returned 14 settings (saved values = sent), Disable/Enable toggles the client's state, Remove picks up, Go To re-zones to the spawn point, Add routed (refused without materials). Dev: gameedit [off]. Unknown: which in-game control calls RequestGainActiveAuthorshipOfGame (Lua unreadable).
- 2026-10-11: CHAOS CAVERN TRIP: completing an Adventure (Adventure table action 577090 "Give reward and teleport to a Chaos Cavern"; Chaos Caverns = the underground caverns) now, after the 10 s scoreboard, takes its players out of the game and 2 s later (game packets must not share a burst with the re-zone) sends each to a random cavern (Caves.TravelRandom). adventure.json 15/15. Reward = the completion score only (no reward data in the client).
- 2026-10-11: WEAPON COMBO NAMES: tooltip names for all weapon combo abilities by their input kind (WeaponAttacks.KindOf): basic + basic combo stages (760, 799, 1049, 816, 1051) 'Basic Attack' 565568, power + stages (761, 801, 1050, 1181, 1182) 'Power Attack' 565569, special/E 'Tertiary' 565570 (no more 'Ability Name Not Found').
- 2026-10-11: MISSING PROP MODELS (all 14 now resolved): besides the spawn points, Random Node 2657, Timer Node 2659 and Counter Node 2901 (in 0.589 PropDefs) had dump model ids this client lacks → invisible. Their dump name is empty ('landmark_v2:'), so the PropDefs name text is used ('[auto] Theme_Widgets_DMWidgets_RandomNode01'); the Theme_Widgets_DMWidgets_* model rows (1666/1667) create no entity in this build, the Char_SimpleObject_RuleSetObjects01_DMWidgets_* ones do (Dice 1657, Timer 1659, Logic 1655, as used by the working story widgets), so that variant is preferred and node widgets without one get the Logic Node. Verified by entity probe (scenarios widgetmodels/widgetprobe).
- 2026-10-11: GAME EDITOR TRIGGER: the shipped UI Lua (testclient/UI/ScriptsBase.bin strings) never calls RequestGainActiveAuthorshipOfGame; only the PropRecipe handler fires OnOpenInstancedGameSettings. The Game Table's Settings (PropBase 0x1B, FUN_142f17c10: fire-and-forget) now opens the game editor when the table hosts its site's game (PropSettings.SendSettings → InstancedGames.EditTable). gamesettings.json 9/9, screenshot: editor opened from the table's settings.
- 2026-10-11: WINNER BANNER: client scoreboard handler FUN_143434580 builds GameData.InstancedGameScoreboard.ScoreboardBanner from the scoreboard result (+0xc0): -1 nothing; team id t (1-based) → InstancedGames.VictoryMessage with winner = InstancedGame.Teams.DefaultName_t (else 'Team t'), colour palette[t % 13]; 0 → (one team, manager winner name +0xd0 set) Victory, else GameOverMessage / DrawMessage. Server bug fixed: EndRound sent the scoreboard before switching to Victory, so the result went out as -1; now the final scoreboard carries it (team winner = index + 1, draws and FFA 0). But this client's StringHashToValue.txt has none of the message keys (VictoryMessage/GameOverMessage/DrawMessage/Teams.DefaultName_*), so the banner keeps its default 'Team 1' text: a 0.589 client data gap, not fixable server-side (screenshots FFA + TDM draw). Probes: bannerprobe/bannerprobe2; igstate shows the manager winner name.
- 2026-10-11: SAFE POINT: ZoneSafeTeleportRequest (0x7C, no body; FUN_142c709d0) is sent by Lua EvacLocalPlayerToSafepoint (FUN_14354b210: player outside the zone's bounds, i.e. fallen out of the world) and TeleportToSafePoint; it was unhandled (player stuck). Now Health.EvacuateToSafePoint: nearest respawn site (Graveyards, as the death window's free respawn), else back onto the ground; at most once per 10 s. safepoint.json (verified: evacuated to Build Site, repeat ignored).
- 2026-10-11: PvP hits in site games now send CombatResourceEvent (0x260067) to attacker and victim like creature hits (floating damage numbers; shown by 0.589 only with its floating combat text toggle on). Not harness-tested (needs two players aiming); sitegame/sitegame3 still pass.
- 2026-10-11: STORY CREATURE CLICK + ROUTE: story creatures are flagged interactable on spawn (DynamicInteractionFlagging) and answered as usable in InteractionInfoReply; InteractRequest on one runs its spawner's 'When a Luminary clicks this creature...' (51877) Links with the player as activator (StoryTools.ClickCreature; dev storyclick also takes creature guids). '...Have this creature walk along their route.' (576662) fires the spawner's 'When this creature is told to walk along their route...' (576661), which builders link to Travel Points. Fixed: creature lookups matched props with no creature when given guid 0. creatureclick.json 14/14 (click turns the torch off; the route walk arrives at the Travel Point). Real-mouse click on a creature untested. Weapon equip/unequip Links still do nothing (needs NPC equipment visuals).
- 2026-10-11: LINKING AREA CREATURE EVENTS: 'When a creature enters / leaves this area...' (573163/573164) were offered but never fired; the area tick now tracks the site's story creatures (CreaturesInside) like players. Area test is now a volume (StoryTools.InArea: radius × scale across the ground, 1.5× that vertically) for players, creatures and 'damage creatures in area' — a sphere missed creatures walking on ground below the area. creaturearea.json 13/13; storyrun/travelpoint/storynode/creatureclick pass. Audit: every other offered story action/event is handled except weapon equip/unequip.
- 2026-10-11: SITE GAME QUEUES (Game/InstancedGames.Queue.cs): request replies type 5 queue position {i16 value1 (queue size), i16 position (GaqInstancedGameQueue +0x14c QueuePosition; 0 = 'Join Now'), i16 estimated wait s (+0x150 EstimatedWaitTime), LocalizedString name} → client Game Action Queue entry (FUN_14332dcf0 → FUN_14332ab20; display FUN_143339ea0: UI.LeaveQueue / UI.JoinNow); type 4 removes it. Joining a full / finishing game (or while others wait) queues the player; when places free the head gets position 0 and MaxQueueJoinTime (general setting 4, 30 s) to join, else dropped; Leave while queued leaves the queue; walk-on queue size from the queue. Dev: tpto <character> (to another player's landscape and spot), game shows the queue. gamequeue.json 10/10; sitegame/2/3, zonecontrol, adventure, gamesettings pass.
- 2026-10-11: BOSS DISPLAY: Combat EnableBossDisplay (CombatBase 0x24 → bytes 24 00, i16 sub 0x0A; reader FUN_14331c160, handler FUN_14331e620): {u64 entity, bool}: on sets entity +0x1454 bit 2 and adds it to the client's boss list (FUN_142ee6730 lookup on game +0x31e60). Story spawners named '... BOSS ...' (2706 Barren BOSS, 2711, 2716, 2720, 2725, 2730, 2867, 2873) now spawn their Ogre with it. Verified: client boss flag 1 on the spawned creature (bossdisplay.json); the boss health bar itself not yet seen on screen (the Ogre killed the test character first). Test tooling: run.py passes captured guids ≥ 2^53 as hex text (JS numbers lost digits of creature guids).
- 2026-10-11: BOSS DISPLAY REVERTED (3d98faf): in the full scenario run the test client crashed (0xc0000005 in ntdll, Windows event 1000) one second into a re-zone (loading screen shown) while a boss-flagged Ogre (EnableBossDisplay on) was in view and the character dead; the boss list probably keeps the entity across the re-zone teardown. Removing the boss outside a re-zone (Play Mode off) did not crash. Feature removed until that path is read in the client; packet layout kept in the docs history.
- 2026-10-11: FREE GAME MARKERS: site game markers (IgPropRoleProps: spawn points, control points, game markers) have no recipe (PropDefs PROP_RECIPE_ID 0), so the game editor's Add places them free (bag item first if any); picking up a free marker refunds nothing (no farming). igprops.json: Add → placing (model 1699).
- 2026-10-11: PLAYER REPORTS (Game/Reports.cs): bug report window (Ui.OnBugReportUiSubmit → FUN_14306f260(game +0x31f40, category, description)) sends BugSubmission 0x55: u16 base, u16 sub 1 (header FUN_14306db00), u32, string, u32, string = TinyXML <BugSubmission Category BugDescription><PlayerData ... location, camera, client version, graphics device, equipped items .../></BugSubmission>; 'report this build site' (FUN_14306f5e0, only inside a claim) sends ClaimReportSubmission 0x56 the same way with <ClaimReport ReportDescription>. The client shows its own 'submitted' message. Saved to Reports/bugs and Reports/sites (XML, headed by character/landscape/position/site/owner), 2 at once then 1 a minute. Old unused SubRequest8 stubs for these opcodes removed (the sub is a u16). bugreport.json (file verified).
- 2026-10-11: BOSS CRASH CAUSE (read in the client): entity removal FUN_142ee4d70 unlists an entity from the boss list (entity manager +0x281e0, FUN_142ee9c60) only when its +0x1454 bit 0x8 is set; EnableBossDisplay sets bit 0x2 and lists it, so a removed boss stays in the list (dangling) and the re-zone path (FUN_142ee4930 → FUN_142ee9a20 over +0x281e0) touches it. A server must send EnableBossDisplay off before the boss leaves a client's view (despawn, death, out of range) and before that client re-zones. Feature stays out until that is done carefully (re-zone burst rules).
- 2026-10-11: CLIENT CRASH FIXED — RE-ZONE DURING PLACEMENT: re-zoning while a prop placement is unfinished crashed the client (0xc0000005 in ProxiedCharacter destructor FUN_142dee1b0 at +0x2dee239/+0x2dee241, deleting entity +0x4998; Windows event 1000). Reproduced in isolation (crashB.json: game editor Add → tpclaim), not caused by story creatures (crashA.json: 2 re-zones with an interactable story creature in view, fine). Fix: Teleport.Rezone first cancels the player's unfinished placements (Props.CancelPendingPlacement: PropBase sub 2 refusal with error 0 = silent client reset FUN_142fe7780, then the prop is removed) and sends the zoning 1 s later from the tick (the re-zone counts as pending meanwhile; RezoneNow drops a deferred one if another re-zone goes out first). crashB.json 5/5, no crash event. Applies to every re-zone (death-window respawn, travel, editor Go To, dev tp).
- 2026-10-11: FULL REGRESSION (60 scenarios, 3 runs + reruns): after the placement re-zone crash fix every scenario passes. Test-harness fixes, not server bugs: scenarios leaving the character dead or in Play Mode (creature scenarios now end with playmode off + respawn; deathrespawn starts with a respawn), area props placed in mid-air on slopes (creaturearea closer to the player), Holoseeker overlay check waited 8 s for a 25 s refresh, rezonetwice expected a fixed site id (run.py rpcis now takes 're:<regex>'), sitegame3 depends on sitegame/sitegame2 running first. One run lost the LawTest client with no crash report after ~45 attached runners (Frida script destroyed at the start of sitegame3); a fresh client passed everything.
- 2026-10-11: BOSS DISPLAY BACK, CRASH-SAFE: BOSS spawner creatures get EnableBossDisplay on spawn as before, but the server now tracks which players were shown each boss and switches it off before every removal from a player's view (Creatures.RemoveFor at death, despawn, out of range, story despawn) and before that player re-zones (Teleport.Rezone: Creatures.ClearBossDisplays → zoning deferred 1 s like the placement cancel). bosssafe.json 7/7: boss flag on, re-zone with the boss in view, Play Mode off, respawn — no crash (Windows event log clean). On screen no boss bar seen at 8 m out of combat (bosslook screenshot); to be checked in a fight.
- 2026-10-11: GUILD INVITE POPUP: the GAQGuildInvite entry is not made by GuildBase 0x17 but by the client's notifications: base 0x01 (enum Notifications 0x100 = bytes 01 00; Zone_DispatchPacket case 0) → GameActionQueue::HandleNotificationPacket FUN_143336df0: u32 type 1 ListNotifications, u32 n × Data (FUN_143329330, 0x198 B: u64 id, u32 type, u32, u64 from, PlayerName, u64 to, PlayerName, u64 subject, u64, u32) → FUN_143337930 by type (6 guild invite → GaqItemGuildInvite FUN_14332b4f0, subject = guild id; 2/3/4/7/9 other kinds). Game/Notifications.cs sends it on /guildinvite (besides UpdateInvitation); accepting sends InviteReply (already handled). Verified with a read-only trace (adhoc/gaqtrace.js): handler got 118 bytes, reader ok, entry type 6 subject 1, guild invite item built. Not seen on screen in the unfocused test window (to check with the user). Note: a first try wrote bytes 00 01 for the base — wrong, base is 1.
- 2026-10-11: GUILD ROSTER ACTIONS: the guild member right-click menu (RadialMenuButtons 42 Guild.PromoteMember, 46 DemoteMember, 44 Kick; menu type 6 already offered) sent requests the server ignored. Now: GuildKick 0x0C {u64 guild, u64 member, string} (native Kick FUN_143508640, writer FUN_142e44120) needs KickFromGuild and a lower-ranked target; GuildChangeMemberRank 0x0A {u64 guild, u64 member, string, u64, string, u32, string, u8, u8, u32} (native FUN_143507e30, writer FUN_142e442b0) moves Member ↔ Officer (a rank named in the strings wins), broadcast back as ChangeMemberRank {u64 guild, u64 member, string, u64 by, string by, u32 rank} (reader FUN_142e457c0). New Officer rank (id 3, level 2; Member level 3): invite, kick, change ranks, MOTD, view log. guildranks.json 7/7 (requests fed with dev guildpacket since the server's client-Lua channel calls Table:Method, which adds self and makes the one-argument natives refuse). Clients stayed alive after the broadcasts.
- 2026-10-12: ABILITY COLLISION REPORT: the client's Abilities 0x27 sub 0x18 (built in the character controller's contact handling FUN_142dd0960 / FUN_142dd1610) was logged as an unreadable packet. Layout {u64 reporter, u32 n + n × u64 entity touched, u32 n + n × vec4 contact point} (seen: player 1 touching story prop 0x2000135). Model AbilityCollisionReport + a trace-only handler; the server keeps its own hit checks.
- 2026-10-12: TEST HARNESS: the "alive" step read the Windows Responding flag once; Windows sets it after 5 s without message pumping, which a loading screen or the bug window's system scan can cause for a moment (full run 5: bugreport / crashA / bosssafe "responding False" with the client fine seconds later and no crash event). It now re-checks every 3 s for up to 15 s; a real hang still fails.
- 2026-10-12: OPCODE PARSE BUG (since 8a4515a, 2026-10-11): WorldSession.GetOpcode first tried a ONE-byte match; when DebugDrawBase (wire bytes 00 01) got the value 0x0001 that one-byte check caught every packet starting with byte 01, i.e. all client Notifications (bytes 01 00), which were then dropped as "unreadable DebugDrawBase". DebugDrawBase was the only enum value below 0x100, so the one-byte form is removed; 2/3/4-byte matching is unchanged.
- 2026-10-12: GROUP INVITE ANSWERS CORRECTED (read in the client): the Notifications packet the client sends (sub 3 = UpdateNotification {u64 notification id, u32 state}, FUN_14332f4b0 / FUN_143338890) carries state 4 = "Game Action Queue entry finished" after Accept AND after Decline (GameActionQueue.Respond → FUN_143338690: choice 1 → entry slot 4 then slot 7, choice 0 → slot 3 then slot 7; slot 7 FUN_14332f940 sends state 4) and on Remove. The old reading "sub 3 choice 4 = Accept" therefore also accepted on Decline. The real answer for a group invite is GroupsBase Join (sub 2; GaqItemGroupInvite slots 4/3 → FUN_143350700(DAT_14535d4d0, 1/0) → FUN_142d535a0, writer FUN_142d3d9c0 after header FUN_142b79760 {u16 base, u8 sub, u32 1}): u32 0, u32 response (1 accept / 2 decline), u32, invite echoed. The client only sends it when its stored invite (game +0x593a0, reader FUN_142d3eb10) is valid: key != the client's "no group" constant, inviter / invitee guids set, each player block's last byte 1-3, raid flag 0. The server sent key 0 and bytes 0, so no Join was ever sent. Now: Invite key = inviter guid, player bytes 1; HandleJoin reads the response at body offset 8; Notifications sub 3 is logged only. Scenario groupinvite (Decline then Accept through the popup's own functions; client.js groupaccept / groupdecline / groupinvite).
- 2026-10-12: ARRIVAL AFTER A RE-ZONE: creatures and surface objects are held back while a player is "arriving" (from a teleport until the client reports a position within 50 m of the destination, or 60 s). An idle client sends no position, so for a minute nothing was streamed to it: in full run 5 the bosssafe boss spawned 45 s after LawTest's teleport went only to MrLawKing (identified by SOE stream: LawTest's chat reply and the boss packets were on different sequences) and LawTest's client had "no entity". The re-zone confirmation (repeat ClientIsReady → Teleport.FinishRezone) now ends the arrival 5 s later (WorldSession.ArrivalConfirmed); the 5 s keep the guard while the loading screen finishes.
- 2026-10-12: COMMIT 9e06a4d (opcode parse fix, group invite Join, arrival after re-zone, queue buttons, collision report, palette SlotUpdates). Verified: rerun of every scenario that failed or did not run in full run 5 — all pass (groupinvite 9/9, guildinvite 4/4 now accepts through GameActionQueue.Respond, bosssafe 7/7, crashA 5/5, crashB 5/5, gamequeue 10/10, rezonetwice, safepoint, sitegame 1-3, story*, trade*, zonecontrol ...). No WARN/ERROR lines left in the server log.
- 2026-10-12: TEST HARNESS: (1) regen unclaimed LawTest's build site and never restored it, so later tpclaim scenarios failed ("has no build site"); it now places the site back at the usual spot. (2) The client logs itself out after ~30 min idle (two clean ClientLogout 5 s apart in full run 5 — not a crash). (3) run.py {num:name} = a captured decimal value; client.js gaqrespond(id, choice) (GameActionQueue.Respond path FUN_143338680), groupaccept / groupdecline / groupinvite.
- 2026-10-12: CHECKED, PARKED: Merchant window (CoinStore 0x9E) opens only on a server OnOpenMerchant event and 0.589 has no merchant NPCs (adding one would be invented content). Portraits (Fotomat 0x98: PortraitDataRequest once per player, PlayerImageData reply carries rendered image data) need a rendering service. In-game store (InGamePurchase 0x45) is the real-money store: needs a user decision. Friend positions (Command 0x18) are only reachable from a console command.
- 2026-10-12: SHOWCASE STORE + GALLERY (commit 429f110, docs/showcase.md): the HUD Showcase = InGameMarketplaceBrowser, an embedded CEF (Chrome 33) browser at ClientConfig MarketplaceUrl with the client's routes #!/marketplace, #!/gallery, /#!/marketplace/search/...SearchTags= (strings at 0x14519e120..140). Official pages were Daybreak's (store*.landmarkthegame.com; EvalLUA/SteamId are only injected for file:// or those domains, FUN_143aa6450/6530); client Marketing tables are empty. Built: free store (326 outfits slot 1, 1008 decorations by PropCategory, 55 designs; icons via re/tools/export_icons.py → gameData/icons, 4938 PNGs from Resources/Images/*/ DDS), gallery of every build site with Visit, web API with per-session token sign-in. Verified in game (screenshots: store page signed in as LawTest with icons; gallery; take gave Artisan's Outfit to LawTest; bogus token refused; MrLawKing visited LawTest's site). Scenario showcase 14/14.
- 2026-10-12: CLIENT LUA RUNNER: the FirstTimeEvent Script channel (FUN_142e3fdf0) calls Lua-defined table functions with no self (ShowURL got its URL as self → LuaErrors.log "attempt to index a string value (local 'self')") and native table functions with the table first (probe adhoc/docmd.js: Ui.DoCommand got (table, "Ui")). ClientLua.RunCode = BindCommandToLua("lmemu", code) (global, exact args) + Ui.ProcessChatCommand("/lmemu") (reads the last arg) → console command runs the code (console lower-cases: "Ui" failed). Commas: load("...\44...")(). Dev: luacode, storetoken. Harness: run.py steps http (with capture) and shot (window screenshot to re/test/shots).
- 2026-10-12: WINNER BANNER FIXED: the banner script (ScriptsBase.bin) does GFxMovie:Invoke('setText', ScoreboardBanner data source text) and the data source only ever holds "Team 1" (missing VictoryMessage strings). The server now runs SoeGui.GetWindow('ScoreboardWinnerBannerWindow'):GetChild('GFxMovie'):Invoke('setText', '<text>') 1.2 s and 2.4 s after the Victory state for players and observers. Screenshot re/test/shots/banner_end.png: "LawTest Wins!". Scenario banner 6/6.
- 2026-10-12: BUILD SITE VENDORS (commit 74826bc, docs/vendors.md): the "merchant we were missing" is the Claim Vendor (player shop): prop 1491 "Build Site_Vendor_Object", ClaimVendorBase 0x112 (enum fixed to wire 0x1201), client ClaimVendorWindow + ClaimVendorHandler.lua. Built buy flow (coins, owner paid by mail with coins attached), listing/price/unlist from the Showcase "My vendors" tab (client has the natives but no owner screen), stocked vendors can't be picked up (PickUpReply error 9), claim_vendor_item table. Client bugs patched at run time (ClientFixes.cs): ClaimVendorHandler calls ClaimVendorScreen but the natives are LuaClaimVendor; BuyItem takes exactly 2 args. Scenario vendor 11/11 (screenshot shots/vendor_window.png: Vendor window listing "Lightstone 10"; LawTest got mail "Sold: 2 × Lightstone" with 20 coins). NPC merchant (CoinStore 0x9E, MerchantWindow, MerchantSets "Generic Merchant") documented, not built (no merchant NPCs in 0.589). Point-of-Need upkeep (0x46 type 1 {u32 1, u8, u32 days, u64, u64}) confirmed free.
- 2026-10-12: PORTRAITS (commit 6c875f1): FotomatBase 0x98 sub 2 PortraitDataRequest {u64 char, "Headshot"/"Portrait"} answered with sub 3 PlayerImageData (empty appearance block + PNG) from stored images; client self-uploads (multipart to [Portrait] UploadUrl, after AddClientPortraitCrc 0x75 {crc32}) accepted at /portrait/upload only when the CRC came from that character's session (tested: right CRC stored, wrong file / other character refused). Finding: no 0.589 window shows portraits (BindPlayerPortrait imgps:// unused; party frames have no faces); capture trigger untraced.
- 2026-10-12: OPCODE ENUM: converting the remaining 0x1XX00 bases to wire form broke nothing in the build but would have stopped WorldSession's raw paths (they only run when GetOpcode finds no match: HeroicMovement relay, design screenshot uploads, warp/social/world-editing). Reverted; the enum now says why they stay.
- 2026-10-12: CLIENT CRASH (full run 7, banner scenario start; dump %USERPROFILE%\AppData\Local\CrashDumps\Landmark64.exe.9836.dmp, analyzed with new re/tools/dumpstack.py): access violation in ntdll (write to 0x24) on a terrain worker thread: FUN_143ed5a30 → FUN_142a4f290 → FUN_143ed9610 → FUN_143f4e2f0 → FUN_143f0d9a0 → FUN_144326960 (posts a job, FUN_1439dbac0 enters the critical section at target +0x16c90) — a job posted to a torn-down or invalid object. 1 s after "World Ready" of a tpclaim re-zone (ClientIsReady 4 s earlier), right after the adventure scenario's cavern trip + respawn. First time this signature appears in hundreds of re-zones: logged as an intermittent client terrain race; watch for repeats (then look at what the server sends to the terrain engine around re-zones: the manifest at BeginZoning and again at ClientIsReady). Other dumps there: 9528 = the placement crash fixed in 798afca; 4152 / 28076 (Oct 10-11) = allocator faults FUN_142ab39d0 / FUN_142ab3ab0.
- 2026-10-12: HELP / LINKS (commit ee98b74): ClientSettings carried PlanetSide 2 help/shop URLs (captured blob) → now this server's #!/help and #!/marketplace; Showcase Help tab. Escape-menu Help (Ui.OpenSupportUrl) opens ClientConfig PetitionUri in the SYSTEM browser (a test call opened the user's Opera at help.daybreakgames.com — never call it in tests). ClientLua.RunCode now binds lmemuN per call (two calls in a row lost the first: Showcase sign-in was lost behind ClientFixes).
- 2026-10-12: AUDIT: 234,539 functions decompiled; only 5 packet functions have undecoded switch tables (Encounter, MiniGame, admin/skill-admin dispatchers, one generic) and no UI uses those systems. Captured blobs still sent: LoadWelcomeScreen, ExperienceBase, ZoneCoordinateInfo, SkillBase sub 5 (default skill point/line list), InitializationParameters "live", ClientGameSettings, ActivityBase, LoadoutsBase — reviewed, nothing player-specific or wrong found except ClientSettings (fixed). Client error logs: tint-alias / "failed to apply default tint" only on direct test logins (the real client via the login server has none); global.lua:960 only when the harness calls mail natives directly.
- 2026-10-12: VOXELFARM / TERRAIN PROTOCOL CHECK (docs/terrain.md "Protocol check"): a read-only Frida probe (re/test/scenarios/adhoc/terrainpkt.js) checks after every client terrain reader that the stream ended exactly at its end with no error flag (the dispatcher FUN_1430a8410 doesn't check; a wrong layout would be used with zeroed fields). Over full runs 8-9 on both clients: CellsUpdate 191/191, ModifiedCellManifest 42/42, ManifestUpdate 642/642, DetectVeins 45/45, no bad packet. ClientCellRequest entries {u64 cell, u32 × 4 versions} always carry versions 0 (cellreq.js) → answering with full data is right. New: ClientPendingRequestTimeout (0xEC/2 {u16, u64 context, u64 cell}) answered like a request (was unhandled). Mesh cache (meshcache2.js): hundreds of "version mismatch" per session in VoxelDataErrors.log came from ore-vein regions generated only on arrival and announced cell by cell after the zone-in manifest → client loads cached mesh as stale, re-meshes, fetches, re-meshes. Fix prepared (OreVeins.EnsureAround before the manifest; Teleport.RezoneNow sets the destination before the manifest, which also fixes a cross-landscape re-zone getting the old landscape's manifest).
- 2026-10-12: COMMIT 10f13e1 — ORE VEINS IN THE ZONE-IN MANIFEST: OreVeins.EnsureAround generates the vein regions around a player before their manifest (TerrainStore.SendManifest); Teleport.RezoneNow sets zone/position before the manifest (cross-landscape re-zones got the old landscape's manifest); TerrainEdits.Announce(except) so the arriving player isn't sent the same cells twice. ClientPendingRequestTimeout (0xEC/2) answered like a cell request. Measured (testclient VoxelDataErrors.log per client session): before 19-58 mesh "version mismatch" per session (21 at zone-in in the last one); after, 0 at zone-in, ManifestUpdate per session ~320 → 0; a client restart with no terrain edits in between → 0 mismatches (the remaining ones follow real edits: the regen scenario, regrowth). Probe: every terrain packet still decodes exactly (CellsUpdate 105/105 + 2/2, Manifest 20/20 + 10/10, DetectVeins 19/19). Scenarios holoseeker, rezonetwice, teleport, adventure, safepoint, regen, startloc pass.
- 2026-10-12: FULL REGRESSION RUN 9 (51 scenarios, server + both clients with terrain probes attached): 49 passed first time; trade failed on a count left over by vendor (MrLawKing kept the 2 Lightstone bought from the vendor; vendor.json now takes them back) and vendor lost both clients to the client's 30-min idle logout. Idle logout read in the client: BaseClient::CheckIdleTime FUN_142c0dec0 logs out when now - game +0x32f18 (last activity, set by FUN_142ca40f0) exceeds client setting 0x14842c99 = 1800 s (a second limit 0xa5dee4e9 on game +0x32f10 is unset). Test harness: client.js marks activity on every scenario load (rpc idle shows the timers). Reruns: vendor 11/11, trade 8/8 → all 51 pass.
- 2026-10-12: GUILD INVITE AFTER RELOG (a5f81d8): the client sends its unanswered Game Action Queue entries at UI shutdown (Notifications 0x01 sub 1 ListNotifications from GuiOnShutdown FUN_142c3f3c0 → FUN_1433302e0; seen: 1 entry, type 6 guild invite from MrLawKing, state 2). The server keeps invitations (5 min) and now resends UpdateInvitation + the notification at the next login; tested: invite, clean client logout (harness rpc idleout = the client's own idle logout), relog → popup back (notification 3), accept → joined.
- 2026-10-12: UNANSWERED REQUESTS DOCUMENTED (9124333, protocol.md §14.1): InGamePurchase 0x1A/0x15/0x3F/0x36 at every login start the real-money Daybreak Cash store (an AccountInfoResponse makes the client ask for the Station Cash price list), so they stay unanswered while the store is free; warp network, AddNotification, tool switch etc. are informational. The coverage tool's other "not handled" rows (contests, CanVote, portraits, site games, bug reports, titles, loot, swimming) are handled through routers it can't see.
- 2026-10-12: SERVER→CLIENT READ CHECK: new re/tools/s2cprobe.py generates a Frida probe hooking the client reader of every packet class the server sends (catalogue readers + constructor-built ones via new re/tools/ctorreaders.py; 234 readers) and checks the stream ends exactly with no error. First result: RatingPacketCanVoteReply 0/18 (reader ran past the end: the server omitted the site-key string) → fixed (5ce7dee), now 11/11. False alarm explained: BacktimedAbilityPacket's FUN_142d65900 only peeks at the header. Also re/test/scenarios/adhoc/zonedispatch.js (Zone_DispatchPacket / PlayerUpdate_Dispatch return values; the zone-level value is not meaningful for every base). Client idle limit = StringHashToValue System.ClientIdleTimeout 1800 (client data; left as the game's AFK logout).
- 2026-10-12: PACKET FIXES (ef6802d, found by the s2cprobe reader checks): ObjectScripting ListBindingsResponse per link ends with two fixed 4 × u32 arrays (the client dropped every list holding a link); inline item records (trade AddItem, chest lists, mail) now end where the 0.589 reader ends (InventoryItem.WriteRecord; the trade window had dropped every AddItem, chest items after the first were misaligned); "LoadWelcomeScreen" 0x60 dropped (no 0.589 handler). Run 10: 79/79.
- 2026-10-12: UNKNOWNS RE-CHECKED (docs/unknowns.md): every captured packet traced to its client use — ClientGameSettings all fields (interaction range, impact threshold 12 / scale 110, two lookup switches, an optional check), ZoneCoordinateInfo = zone origin/size/bounds (StaticZoneData has them per zone; all landscapes 400-423 share the captured values; seamless continent 430 zones 440-455 differ → worklist), InitializationParameters unused, WorldDisplayInfo = crash-report field, QuickChat empty as live, AreaDefinitions exact, Inventory 0x18 = GrapplingHookEquipped. The 5 "undecoded switches" decoded (re/tools/decodeswitch.py + re/decomp_jt.py on a project copy re/ghidra_copy): Encounters, MiniGame, AdminCommand, SkillAdmin; the fifth was a misread virtual call.
- 2026-10-12: SYSTEMS INVENTORY (SYSTEMS-WORKLIST.md, re/tools/inventory.py): 171 packet families, 875 open items (static); 383 client data files the server doesn't read (177 with content, 143 empty in 0.589).
- 2026-10-12: ENCOUNTERS (0ce255a, docs/encounters.md): EncounterBase 0x2C decoded and built as the client drives it (DetailsResponse sets "may enter", ZoneIsReady → RequestEntrance → PlayerEnter/State; Terminate + Shutdown); duels via /duel /duelaccept /dueldecline /duelleave (registered dynamic commands). Searched every client file for duels: only protocol, DUEL_ENCOUNTER_ID (0), AutoRefuse option, unused DuelRequest popup type — no menu, no text. duel.json 11/11.
- 2026-10-12: ABILITIES: definitions are not in the client — AbilityDefinitions.txt is a server-only file (its loader in the exe is shared server code that never runs on the client). Known: 91 abilities on captured items + ~25 referenced by client data (travel 185-187, revive 246/1170, Heroic Movement 326, telegraph tells 41-47/60/61/103, input gates 496/558/560/562/610, test ids 1-8).
- 2026-10-12: REVIVE + TELEPORT (22533d4): reviving another player decoded (Ui.RevivePlayer → action 6 → cast 246; target needs state bit 7 + RevivePoints > 0); server sends RevivePoints on death and revives on cast 246; the live "dead" state signal for remote clients is still open (bit 7 via UpdateCharacterState makes the remote entity drop out of the lookup). Teleported players' positions now reach others at once. Steam: Landmark app 297810 depot 297811 manifest 4876286683328705192 build 1170900 = exe 0.589.1.128895; the test client links UI/Locale/CommonData/GraphicsData/HelpPages to the real install.
- 2026-10-12: DEATH AND REVIVE AS LIVE (c3c627c): the client's own packet registry has the full set — PlayerUpdate 0x39 KnockedOut (now sent to everyone on a death; layout decoded, read exactly, kill feed), 0x3A KnockedOutDamageReport, 0x3B/0x3C Respawn/RespawnReply, 0x3D ReadyToReviveResponse, 0x42/0x43 Start/StopRevive, 0x44 ReadyToRevive (sent on cast 246; the dead client auto-accepts), 0x50 HideCorpse, 0x52 mission respawn timer, admin force-respawn/revive. In 0.589 remote players are always base ProxiedCharacter whose knocked-out handler is empty, and state bit 7 makes SetupActorGroup drop them, so the right-click revive can't reach the cast — likely unfinished in this build, not proven (docs/gameplay.md).
- 2026-10-12: PLAYERUPDATE REQUESTS (1d8cfc8): 0x18 RequestStripEffect → Buffs.Cancel; 0x1A PreferredLanguages kept; 0x0A RemoveTemporaryAppearance broadcast; 0x73 SetEnabled / 0x77 EmoteStringById logged; Command 0x17 FreeInteractionNpc; AbilityEffectUpdatesRequest (every zone load) re-shows active buffs.
- 2026-10-12: TREES/PLANTS FULL FORM (aba2bea): the client requests full data for every gathering node (shown as lightweight props); now answered with LightweightToFullProp like other props.
- 2026-10-12: IGNORE LISTS (d52e093, Game/Ignores.cs): Command 0x20 IgnoreRequest {u64 guid, name, u8 add} captured from the client; IgnoreBase 0x72 List/Add/Remove decoded (FUN_143494180); table character_ignore; chat from ignored players filtered, their trade/group/guild invites refused. ignore.json 7/7.
- 2026-10-12: SHUTDOWN WARNINGS (f494540, Game/Shutdown.cs): WorldShutdownNotice 0x5F {u32 seconds, string, u32}; dev "shutdown <s> [msg]" countdown with warnings and a clean host stop, "shutdown cancel"; on-screen text verified (shots/shutdownnotice_window.png).
- 2026-10-12: UI INVENTORY: 167 client windows (re/as3); triage by data + UI. Systems with no 0.589 content: missions, activities, vehicles, implants, weapons table, leadership, housing names, social wall, collections (empty tables) — protocol answers only, no invented content.

- 2026-10-12: PLAYER STUDIO CREATE (656c993, docs/commerce.md): InGamePurchase 0x1A AccountInfo answered ("US"/"USD"; Player Studio needed a US account with a zip code — wallet reply byte, FUN_1431b4c10); PlayerStudioRegistered 0xFE00 at entry; CommerceProductCreate 0x2F parsed (ProductItem = u64 guid, u32 type, u32 qty, u64, u8, string; writer FUN_143181750) → 0x30 status; product rows in player_studio_product. psprobe 2/2.
- 2026-10-12: PLAYER STUDIO PEER REVIEW (4ea5527): the client itself polls 0x3F GetProductForReview (FUN_1431a64b0) and, given a product in 0x40, makes its own GAQ type-9 popup "GAQMarketplaceItemReviewsPending" (no server notification). Product layout (FUN_143184930) mapped to the review window's columns (FUN_143647020); the design's string = TemplateData XML (rapidxml, CreatorName/image URLs). Vote 0x42 {u64, u32 verdict: 1 approve, 0 reject, 2-7 reasons the 0.589 client never offers} → 0x43 {bool}. One vote per player and product, never your own; thresholds PlayerStudioApprovals/Rejections (config, default 3). Result mail: new mail.kind column — kind 0x10/0x20 makes the client write PlayerStudio.AcceptedItem/RejectedItem with the subject as the item (FUN_143586ba0). Table player_studio_review; dev psreview <id> approve|reject, psoffer (test). psreview 7/7, psmail 2/2. (One run lost both clients — closed by another session, not a crash.)
## 2026-10-12 — Separate play server (for the user's real client)
- New play stack in `Z:\Landmark Emu\play` (frozen copy of build 656c993): AuthServer (UDP 20042, web 5000) +
  `LandmarkPlay.WorldServer.exe` (UDP 19002), databases `lmemu_play_auth` / `lmemu_play_character` (copies).
  Start with `play.bat` (servers + real client) or `play.bat -NoGame`.
- The dev auth server was stopped; the PLAY auth now holds 20042/5000 (same process name, so start.ps1 won't start the
  dev one). The dev world server stays on 19000 with lmemu_auth / lmemu_character; dlogin.sh test logins are unaffected.
- Test scripts stop servers by the name LandmarkEmulator.WorldServer, so they never stop the play world server.
- Do not stop LandmarkPlay.WorldServer or the AuthServer in Z:\Landmark Emu\play while the user is playing.
- Auth fix (play AuthServer rebuilt with it): CharacterLoginReply.Guid = the chosen character's id instead of 1, so new
  characters made at character select log in as themselves.
- Play account tester3 (lmemu_play_auth id 3, password = tester2's; test-account3.txt) with no character, so the client opens character creation (0.589's CharacterSelection.lua only does that for an account with 0 characters). Start: play.bat -Account test-account3.txt
- Play server renamed: zone_server nameId 560940 ("Main Server"; 62147 = "Dev Server"). Launchers: play.bat (tester / MrLawKing), play-lawtest.bat (tester2), play-new-character.bat (tester3, empty).
- 2026-10-12: PLAY SERVER UNDONE (user request): LandmarkPlay.WorldServer and the play AuthServer stopped; the dev AuthServer runs again on 20042/5000 (start.ps1). Launchers play.bat / play.ps1 / play-lawtest.bat / play-new-character.bat and test-account3.txt moved to play/launchers (nothing starts the play stack now). The play folder and databases lmemu_play_auth / lmemu_play_character are kept until the user says to delete them. The auth fix c875828 (character login reply carries the chosen character's id) stays: it's in the main code. The play stack's Showcase/store sign-in failed because its web page listener could not take port 19001 (the dev server holds it).
- 2026-10-12: PLAY FOLDER moved to the Recycle Bin (user request); its databases lmemu_play_auth / lmemu_play_character still exist. PROPS OUTSIDE BUILD SITES decided (user: as the original game): the 0.589 client's placement checks (FUN_142f1e540 preview, FUN_142f068f0 box check → FUN_142d15900) only accept a spot on a claim where the player has build permission, unless the dev BypassClaimPermissions flag is set, so the server's build-site-only rule stays.
- 2026-10-12: WEAPON ANIMATIONS (user report: weapons played no animation). The live ability definitions (which AnimationSelection an attack plays) are server data, but the client has the rest: each wielded model's actor (.adr) sets the animation Class (hash -775574123: 1010 sword, 1011 bow, 1012 staff, 1014 2H sword, 1015 daggers, 1017 wand, 1018 katana, 1022 short staff, 1024 1H axe, 1026 heavenly bow, 1027 power cannon, 1028 great axe, 4 shrink ray, 5 gun, 6 polearm, 7/8/3 axe/blunt...; gameData/WeaponAnimationClasses.txt from re/tools/weaponclasses.py), and AnimationSelection rows per Class + AttackIndex (Enter/Release pairs, one-shot Attack). Server (WeaponAttacks.AttackAnimation): basic combo stage n → index n, power → index 3 (sword: one-shot lunge 429), special → 4; Enter sent in the cast success, Release after the selection's AttackTime (an Enter alone leaves the character stuck in the wind-up — seen with selection 483). Onlookers get the same pair (Emotes.ShowWeaponAttack). Main-hand weapons with no abilities (Blade of Assault...) now count as weapons. Tested with real clicks on LawTest (Blade of Assault): 732 → 277/278, combo 874 → 316/317, power 733 → 429; visible swings. Effects (impact / trails) next.
- 2026-10-12: WEAPON FIXES (user: right click did nothing, Shift while walking looked bugged): Heroic Movement 326 went down the weapon path once main-hand weapons without abilities counted as weapons (a sword swing on every sprint cast) — excluded; power attacks played the one-shot 'Attack' lunge 429, which shows nothing outside its stance — Enter/Release pairs now win (sword power = charged swing 483/484; order 3, 11, 13, 10). Verified by log with real clicks.


## 2026-10-12 — Claims: launch size, buffer rule, basic info, record parent field (runtime-verified)
- Placement cursor box measured with a read-only hook on FUN_142d28700 during placement: x -395..-185, y
  925.4..1105.4, z -297.4..-87.4 local m = 210 × 180 × 210 (constant ClaimCursor.ClaimSize "210 180 210"; the "70 100
  70" in FUN_1435aef40 is only the fallback default).
- Pre-check FUN_142d28700(claims mgr + 0x470, box, 0): mgr = *(game+0x314f0)+0x96fc8 (FUN_142c31df0). Index +0x470
  holds ROOT sites as centre points (inserted by FUN_142d134f0 → FUN_142d142c0 from SendAllClaimBasicInfo records
  whose parent field is 0); buffer float at mgr+0x480 = 285. Called on the game thread with test boxes
  (re/testcopy/probe_claimcheck4.js): a point is refused within 285 m of a centre, a 105-half box within 390 m.
- DAT_14535c5e0 is the NULL guid sentinel (0), not the player guid: record field +0x08 of both ClientClaimBasicData and
  the full claim record is the PARENT GROUP (0 = root site), +0x10 the owner. We wrote the owner twice, so the
  client filed every site as an attachment of a missing group and never indexed it.
- 0xF9 sub-dispatcher switches on sub-1 (also Templates 0xF0 +2, InstancedGame 0x10D +2, ObjectScripting 0x111 +1):
  re/tools/subnames.py now derives each dispatcher's offset from the sub its case blocks write.
- InteractionClaim (Command 0x4D) result: 2 = enter placement, else the claim error text (FUN_143311590 →
  FUN_142d29240). DeleteClaimItemReply: 2 = done (FUN_142d29200).

## 2026-10-12 — UI audit: reticle hit indicators / hit marker (CombatPacketLastDamageTime), Armor (runtime-verified)
- Reticle.HitIndicator1-3 (Combat HUD reticle) and GameEvents:ShowReticleHitMarker are both fed by Combat
  LastDamageTime (0x24 sub 0x0D, reader FUN_14331e310 = dispatcher FUN_14331da00 case 0x0C): {u64 guid}. The handler
  (LocalProxiedCharacter FUN_142e248b0) stamps GrapplingHook.LastDamageTime and calls ReticleManager FUN_143156ad0:
  guid == own guid (entity +0x278 vtable +0x28) → ShowReticleHitMarker; otherwise the attacker's bearing from the
  camera goes into the next HitIndicator row ("Value" degrees), fading over Reticle.HitIndicatorFadeTime 1000 ms.
- Server: Health.Damage(…, sourceGuid) sends it to the victim with the attacker (creature or player), and
  Health.HitConfirmed sends the attacker their own guid (creature hits, PvP). Scenario re/test/scenarios/
  hitindicator.json 3/3: kobold hits → the kobold's guid reaches FUN_143156ad0; hitnear → guid 2 and
  GameEvents:ShowReticleHitMarker fired. client.js got a reticlelog rpc.
- LocalPlayer.Resources.Armor: resource type 11 (ResourceTypes.txt; LocalProxiedCharacter::OnResourceChanged maps
  types to Resources.<name>). The 0.589 HUD loads only CombatHudWidget (GfxHudWidgets.xml: the Armor / Energy HUD
  widgets are commented out) and the character sheet lists Armor only when a row exists; launch armor gave health and
  damage bonuses (wiki Armor Weight), not an armor pool — so no Armor resource is sent, as live at launch.

## 2026-10-12 — Voxel Vision (voxel ownership), UI audit runtime probe (runtime-verified)
- UI audit now checks the running client: client.js rpc dsrows reads any UI data source's row count on the game
  thread (FUN_142ccaae0 lookup + FUN_142ccaea0); re/test/scenarios/dsprobe.json → trace/ui-runtime.tsv; uiaudit.py
  uses it, marks windows the 0.589 UI never loads (manifest.xml / GfxHudWidgets.xml), and keeps hand-verified
  verdicts. Result: ok 77, local 66, n/a 11, GAP 2, unknown 2 (was 13 GAP / 28 unknown). Refining / Upgrade tabs are
  n/a (0.589 CraftingStations.txt has only Generic, Salvaging, Repair); the MegaPalette, Time, ClaimsList, Contests,
  Recipes and scoreboard "gaps" were false (filled at runtime / 0x10D labels are case+2).
- VOXEL VISION (launch feature, wiki "Voxel Vision"): Game/Terrain/VoxelOwnership.cs. ClientOwnershipRequest
  0xF4 sub 4 {u32 n, n × {u64 cell, u32 cached version}} → per cell 0x16 UpToDate or 0x17 EnvOwnershipData with the
  owner grid (format in docs/protocol.md §9: 10³-chunked masks + 1-byte owner indices, entry 0 = nobody). Ownership
  is recorded in TerrainEdits: ApplyDiff / paste / undo / redo / heal take the builder; digs, RevertBox (site
  removal), ClearCell clear. Grids saved in TerrainEdits/Ownership/<zone>/<cell>.own.
- Test: dev ownblock (a 4³ cube as the player's voxels) + client.js rpc luaevent ("GameEvents:OnOpenVoxelVision").
  Scenario voxelvision 3/3: 2548 cells requested and answered, VoxelOwnership.Ownership = 1 row; the Builder List
  window opens with the Voxel Vision tint (re/test/shots/vvshot_open.png).
- SITE MANAGEMENT SPECIAL ACCESS LIST: the claim record has two player lists: list 1 {u64 guid, u32 mask}
  (FUN_142d044a0, permission checks) and list 3 {u64 guid, string name, u32 access, bool} (FUN_142d022d0 at record
  +0x130), which EditClaimItemData (0xF9 sub 9) feeds to the dialog (FUN_142d19b90 →
  ClaimEditDialog.ClaimItemAdvPermissions, only for the site the dialog edits, claims mgr +0x400). We sent list 3
  empty, so players given Special Access never showed. Now written with names (cached at load / on grant), and a grant
  re-sends the edit data. Dev grantaccess <player> <hex mask>; client.js rpc claimedit <site> (Lua
  RequestClaimItemDialogData → FUN_142d32b70). Scenario advperm 4/4 (row appears on grant, goes on revoke).

## 2026-10-12 — Spire Local Travel (warp network), runtime-verified
- UI audit of Lua calls (coverage → uimap → uiaudit regenerated): Teleport.TravelConfirmation sends WarpNetwork
  requests the server dropped. Traced: Spire menu "Local Travel" = ControllerLocalTravel (camera mode 0x1E); network
  0x113 sub 2; ActivateTerminal 0xFC sub 1; node markers (HUD indicators, category 0x13) for the layer the player
  stands in; node ids are entity guids. Full format in docs/protocol.md §14.0.
- Game/WarpNetwork.cs: hub = the Spire's "Portal Spire (Hub)" prop; 81 underground portal crystals (prop 1881) at the
  home continent's cave Graveyard01 pieces (tier 1, biome from the property group). The network is re-sent on every
  landscape change (the client only asks once). Clicking the Spire crystal → ActivateTerminal (Landscapes window);
  clicking an underground crystal → the Local Travel map. Warps charge the client's cost: 1 Lumen to the hub, 2 to a
  crystal (its NodeCost / ZoneCost lookups miss the data's keys, which end in a space, so its defaults 1 / 5 apply).
- Request order found at runtime: {type, zone, world} (the first reply under swapped ids gave no markers). The
  surface layer is 0 (the camera's layer is the cave tile's tier at the player).
- Tests: localtravel 8/8 (network 82 nodes, ActivateTerminal sets the client's node, warp via the client's own
  WarpToNode builder charges Lumens and lands at the crystal, back to the Spire), warpmarkers / warplayers (hub
  marker on layer 0, 81 crystal markers on layer 1; shots re/test/shots/warplayers_layer0/1.png). client.js rpcs:
  warpto, warpnode, travelstate, travellayer, hookcount. Dev: terminal <hub|c<n>|hex> [1].
- Open question: whether live made players discover crystals before travelling (the client has a Leyline-discovery
  fanfare id); here all nodes are open.
- 2026-10-12: placed Mailbox props (PropDefs 489) are usable and open the inbox (wiki Mailbox), like the Spire's.

## 2026-10-12 — Launch creature placement: Fiery Fungus surface, Chaos Caverns monsters (runtime-verified)
- Audit finding (wiki "Monsters (PvE)", launch June 2016): only Fiery Mushrooms on the surface, everything else in the
  Chaos Caverns. The server had kobold / skeleton / slaug / ogre camps on the surface. Now: surface regions get Fiery
  Fungus only; each of the home continent's 520 caverns (Underground .cpa) gets a group around the floor under its
  middle: the cave biome's Chomper + Slaug variants (creature props' models and names), an Abomination, two Fiery
  Fungus. Streamed by 3D distance; cavern creatures walk the cave floor (Caves.FloorNear) and aggro by 3D distance.
- Behaviours from the wiki: Chomper stationary spore cloud (composites 11676/11611), Abomination keeps range and spits
  (11627/11625), Fiery Fungus arms at 3.5 m and explodes after 1.5 s for 5 m (10918/10922; drops dirt/stone; a hit sets
  it off), Slaug melee.
- VoxelTemplate: version-2 templates (the 33 cavern_ruins_* Outposts and ~260 more) have one more byte per prop before
  the two lists; re/tools/otd.py lists any template's size and props. Outposts (Elites, Lost Chests, Lost Crystals) next.
- Test: scenario caverns 2/2 (rezonecave: Putrid Chompers, Rotten Slaugs, Abomination, Fiery Fungus around the player,
  a Slaug attacking; a fungus next to the player explodes), shot re/test/shots/caverns_cavern.png.

## 2026-10-12 — Outposts (Elites, Lost Chests, Lost Crystals), Spire crystal-only terminal (runtime-verified)
- Game/Outposts.cs: the client's cavern_ruins_* templates (player-built contest ruins) stamped into the largest open
  caverns (≥ 400 m apart; remembered in TerrainEdits/outposts.txt, cells kept from regrowth), their props placed;
  garrison from the faction's "Spawner: Ruins: …" props (2 patrollers, 2 melee, ranged, caster, BOSS = Elite with the
  boss display) added to the cavern's creature group; Lost Treasure Chest (2930: glow 11279, open 12830; everyone
  within 50 m gets their own loot) and Lost Crystal (2654: dormant until the Elite dies, light 10621; 5 s channel, not
  in combat, a hit cancels; loot: recipes, Cozy Campfire, essences, Lumens; fanfare 11674; then NextUndergroundOffer
  0xFC sub 0x0C → the client's "Eureka!" dialog; Reply sub 0x0D → another outpost). Outpost name in chat on approach.
- Memory: an overlay cell is ~384 KB in RAM, so only the 10 compact ruins (12-27 cells; ~210 cells) are placed; the big
  ones (100-245 cells) wait for compact overlay storage.
- Test outpost 3/3 (Frozen In Time, Ebonguard garrison + Elite; chest loot; crystal after the Elite → Eureka dialog;
  shots re/test/shots/outpost_*.png). Dev: outpost <n>, outpostuse <n> chest|crystal.
- User report "dropping through the Spire, its collision is gone": not reproduced (LawTest arrives and stands on the
  platform; full-prop requests answered). Since the Local Travel commit the whole Spire structure (prop 1871) was
  flagged interactable; now only the Portal Crystal (575) is, as live.

## 2026-10-12 — Compact terrain overlays in RAM; all 23 Outposts (user approved)
- TerrainEdits.Overlay keeps each edited cell in BlockData.Save's zlib form and expands it only on access (property
  Blocks; LastUse = Environment.TickCount64, cheap in the per-voxel loops); CompactIdle (every 5 s from Flush) packs
  saved cells idle ≥ 30 s and the least recently used beyond 64 expanded; regrowth re-packs what it only inspected;
  Outposts flush + compact after each stamp. Dev terrainmem / gc.
- Outposts: the full list of 23 complete ruins; ruins missing from TerrainEdits/outposts.txt get new caverns (13 added
  this start). 1,950 edited cells: ~32 MB compact (was ~384 KB each expanded → ~730 MB).
- Measured (world server, after one compacting GC, now also run once after start-up): ~930 MB working set, was ~1,040
  MB with 10 Outposts; before the start-up GC it showed ~1.19 GB of start-up garbage. Shipwreck Combined (outpost 19)
  loads fine in the client; the expanded cache peaked at its 64-cell cap.
- 2026-10-12: LOOT CHESTS (wiki "Chests"): Game/LootChests.cs — 124 "Treasure Chest" props (2144) at the continent's
  surface-cave rooms / dead ends and cavern middles (deterministic share of the .cpa sites, on the cave floor), golden
  glow 11279, open 11228; everyone within 50 m gets their own resource loot; back after 20 min. Streamed by 3D distance.
  Test chest 1/1 (shot re/test/shots/chest_chest.png). Dev: chest [go|open].
- 2026-10-12: LOOT TOAST: Inventory LootNotification (0x2D u16 sub 0x1A {string name, u32 quality, u32 count, u32 icon}) on loot-bag pickup and harvest gains (party shares too) → the client's 3 s 'Copper Ore x12' toast with icon and quality colour (shot re/test/shots/toast_toast2.png). Dev lootbag <item> [n]. REACHABLE-GAPS triage: its statuses miss helper-based sends (e.g. Trades EndSession is sent); WeaponHitFeedback (0x32) has no ReticleHandler in 0.589 → n/a.
- 2026-10-12: HARVEST FAILURES the client's way: Gathering.HarvestFail → Abilities EffectEnd (0x27 sub 0x0D) with fraction 0 and reason (2/5 tier, 3 prevent, 4 multiple, 7 pulverizer + resource, 8 not allowed) → centred Harvest.Fail.* text (FUN_142d7aca0). Wrong tool → 3 'It is not possible to harvest that.', mining in a build site → 8 'You cannot harvest here.' (were chat lines). Tier failures don't apply at launch (wiki Pick: no linear progression). Shot re/test/shots/harvestfail_prevent.png. Dev harvestfail <reason>.
- 2026-10-12: DAILY GOALS (wiki "Daily Goals"): Game/DailyGoals.cs. The client knows 21 dailies (texts 575703-575723);
  the capture had 2 records (669, 679), the other 19 are built from 669's record (id = 575040 + name text, objective =
  id + 588, objective text / count / Lumen reward / icon 9338 + id patched) and appended to the achievement template
  (917 achievements). Each character gets 3 a day (Unlocks.DailyGoal = day × 100 + index), unfinished ones with
  progress carry over, the rest drop; AchievementInitialize only carries the character's dailies; on a new day online
  players get a fresh Initialize. Counting: kills (Warpath, faction hunts / Monster Hunt, weapon-affinity Wielders),
  harvest (Common / Shrewd Miner, Lucky Logger), Lost Crystals (Cavern Crawler), likes (Critic), walking onto others'
  sites once each a day (Tourist). Verified: LawTest got 676/678/680, client AllAchievements = 899 (898 − 2 + 3).
- 2026-10-12: CAVE SEEKER the client's way: Buffs.StartClientEffect sends Abilities EffectStart (0x27 sub 0x0C) with
  effect type DetectCave (0x6eb699a5), strength 1, fade 1 s, duration 1 h, start = server clock → FUN_1432777f0 →
  CaveDetectionManager (FUN_14307de40) → VoxelClientImpl draws nearby caves green; toggling off sends EffectEnd
  (FUN_14307df30 removes it). Renewed hourly and on zone load (Buffs.Resend). Replaces the invented chat cave report.
  Test caveseeker 2/2 (hookcounts) + shots caveseeker_before/_on/_off.png.
- 2026-10-12: WEAPON COMBAT REBUILT (Game/Combat/): every weapon cast was "base damage on one creature". Now the held
  weapon's kit (21 named Basic / Power attack pairs from the client texts, 31 items) decides shape (cone, around, first
  hit + splash, pierce, area at aim, dash path, block stance, channels, lingering areas) and rules ("second hit in a
  row", "if Burning gain Frenzy", "fully charged → Condemn" …); the seven affinities' 21 properties (wiki weapon pages)
  are states (Statuses.cs): creature attack / movement gating, DoTs, damage taken / dealt, the client's state effects
  for their duration, player buff icons (property name + affinity buff icon 12206-12218). Fire Erupt rolls, Water Flow +
  Surge (half the energy as damage), Lightning Energize regen, Earth Regrowth, Affliction Infect, Shadow Siphon,
  Radiance Rejuvenate. Armour MAEs (28 chests) and accessory boosts (21) from their descriptions (Gear.cs). Creature
  hits on players go through blocks / Harden / Illuminate shield / armour (WeaponCombat.Incoming). Attack caster info
  carries the weapon's attack names; re-sent on weapon change and Frenzy / Supercharge. HeldWeapon is main hand only
  (chest armour with a combat ability was taken as the weapon). Creature guid lookup prefers the living creature (dev
  spawns reuse guids). Test weaponkits 9/9 (Flow, Freeze, Burn, Condemn/Torment, Crush, Exalt, Rot, Supercharge; shots
  weaponkits_staff.png frozen ogre + Flow icon, weaponkits_buffs.png Supercharge icon). Numbers are ours (server data lost).
- 2026-10-12: CHARGED ATTACKS: the kits' "Charge a powerful …" attacks get the charged caster-info kind (0xF7150354,
  max 1.2 s / min 0.15 s at reply +0xd0/+0xd4). Client charges while held and sends FinishChargedCast {u32 ability, u32,
  vec4 camera, vec4 aim} on release; server times the hold (60-100 % damage, full ≥ 1.14 s), attacks with the release
  aim, then answers (CastSuccess mid-charge would end it). CancelCast handled; stale charges released after 6.2 s.
  client.js castability (FUN_142d71800 on the game thread, ability manager game+0x32070) + chargestate; test charge 5/5
  (Frost Lord's Fury Frozen Domain: full hold freezes, 0.3 s tap = 25 %). Dev revive, god on|off for creature tests.
- 2026-10-12: AREA EFFECTS: PlayCompositeEffect target type 2 TargetLocation {vec4 offset, vec4 position, vec4 rotation}
  (TargetLocation vtable 1445d7af0, reader FUN_142b6a1b0) → HitEffects.PlayAt. Lingering areas and area attacks show the
  client's own effects for their time: Blizzard / ice storm 11528 BubbleStorm, Conflagration field 11613 FireRing_05m,
  blazing cyclone 12729 Fire_Tornado_FireRing_05m, Static Bomb 12784, Rolling Thunder 12782, Frozen Domain 11533,
  Obliteration Turn 12732, Agonizing Volley 12737, Consecrate 10300, Sludge Storm 11638. Dev fxat <effect> [m] [ms]
  (shot re/test/shots/fxat_tornado.png: the fire ring on the ground in front of the player).
- 2026-10-12: UI AUDIT HOLE FIXED: uimap.py only counted CallLuaFunction("...") strings in widget code and skipped the
  wrapper classes, so widget calls through wrappers (GuildWrapper.RankSetName, ClientInventory.SetItemsToSlots.apply)
  were never seen. Wrapper methods are now mapped to their Lua function (CallLuaFunction or rest.unshift). 17 new UI
  gaps: Refining-station calls (no refining station in 0.589 → verdict), ActivateItem / Templates.SetName (handled;
  verdicts), and real ones fixed below. UI calls: 0 gaps again.
- 2026-10-12: BAG SORT: Inventory / Chest / Vault "sort by" → ClientInventory.SetItemsToSlots → InventoryBase 0x2D
  sub 0x0E RearrangeItemSlots {u32 container, u32 n, n × {u32 slot, u64 guid}} (FUN_14352cfb0, writer FUN_14352df30).
  Inventory.Rearrange applies the permutation (bag only; refused when an item isn't there, a slot is out of range or
  repeated; unlisted items in listed slots move to free ones). Dev sortbag; test sortbag.
- 2026-10-12: GUILD WINDOW EDITS: GuildBase 0x0E UpdateRank C→S = the whole edited rank {level, name, permissions,
  rank id (0 new)} + action (1 set, 2 remove) — captured from Guild.RankAdd / RankSetName / RankAddPermission /
  RankRemovePermission / RankToggleAllPermissions; 0x0F GuildDescriptionUpdate {u64 guild, 3 strings} (also what the
  0.589 RankSetDescription sends, with no rank in it); 0x11 GuildUpdateAppDataField {u64 guild, string GuildGameData
  XML: emblem, recruiting, rating, OpenToApps, GuildKeywords = tag}. Guild core: Description = 6th string (+0x108),
  app data = the string after the leader (+0x168). Custom ranks (ModifyRanks; leader keeps all permissions; Leader /
  Member can't be removed; removed rank's members → Member; up to 10), description and app data (UpdateDesc) stored in
  new guild columns (migration 20261021090000_GuildDetails: description, appData, ranks JSON). Permission checks use
  the guild's ranks. Re-sending GuildData resets the client's rank definitions, so ranks always follow it
  (ResendGuild). client.js luarun (luaL_loadstring FUN_143924270 + lua_pcall FUN_143917400 in the UI Lua state) runs a
  window's Lua calls with arguments; run.py "{s:name}" substitutes captures inside strings (sql steps too). Test
  guildranks 9/9.
- 2026-10-12: UI AUDIT, DATA SOURCES: data sources built from variables (new DataSource(datasourcePath) …) are now
  resolved to their string constants (36 more names). Result: ok 84, local 81, n/a 12, 0 gaps / unknowns; the two
  "gaps" (mail compose) are fed by AttachItemToComposeResponse sub 0x15 (Mail.Attach; mislabelled case in
  messages.tsv), the five runtime-named ones verified by row counts (dsprobe2: ActionBars.Bar1 10, MainMenu 6, map claim
  filters 5, review rejection types 4 / 2).
- 2026-10-12: WEAPON AMBIENT EFFECTS: equipment attachments carried composite effect 0; now the item's ambient effect
  (definition u17) goes into SetCharacterEquipmentSlot (all equipment sends use WorldSession.BuildEquipmentSlot). Only the
  five pre-launch weapons have one (1503 11825, 1293 11826, 1296 11824, 1517 11823, 1529 10429); shot
  re/test/shots/ambient_bow.png (Dragonbone Bow burning). Dev ambients.
- 2026-10-12: UNIVERSAL VAULT + STACK SPLIT / MERGE: vault props (407 Void Vault — own sites only, 752 Rustic Stone
  Vault, 780 stone safe) are usable; using one sends Command 0x1E sub 0x50 InteractionVault {u16 sub, u64 entity}
  (FUN_1433120f0 → FUN_142e84350 → GameEvents:OnOpenVaultWindow; the 12-byte form with an extra u16 was ignored).
  Game/Vaults.cs remembers the open vault; TransferItem to / from container 1 only within 13 m of it. MoveTo accepts
  the vault (size ItemUses.VaultSizeOf); Inventory.MoveCount handles quantity drags (Shift split dialog, Ctrl one item:
  SoeDragManager) — a new stack on an empty slot or added to the same item up to its stack limit (MaxStack); vault
  sorting uses the vault's size. Dev vaultnear / vault; tests vault (window hook), vaultuse 4/4 (split 4, merge 3).
- 2026-10-12: PARTY FRAMES: member records were all zeros (empty bars). Decoded the member (FUN_142d3ebe0) against the
  party row filler (FUN_142d56350 / FUN_142d560a0): +0xf8 link id (≠ 0 = IsLinkdead; the client's sentinel is 0),
  +0x100 world, +0x104 zone (ZoneName), +0x108 instance (same world + instance → IsInZone / Distance from +0x110 xyz),
  +0x140 knocked out, then resources u32 n × {u32 type (1 health, 2 energy, 11 armor), u32 current, u32 max}. Members
  now carry health, energy, zone, position and KO; Groups.Update sends UpdateMember (0x13 {u32, member}, FUN_142d4fb50,
  matched by guid) to the group when a row changes (health, energy / 10, zone, 5 m, KO) — at most once a second.
  Shot re/test/shots/partyframes_party.png (LawTest at 500/900 after a hit). client.js peek; tests partyframes 3/3;
  test-state fixes: sitegame / regen clear MrLawKing's claim, hitindicator / sitegame revive LawTest, creaturearea makes
  its story creature neutral (dev storyfaction) so it walks instead of chasing — subset rerun all pass.
- 2026-10-12: POTION COOLDOWNS ON THE HOTBAR: caster info +0x70 (model U70, record +0x14c, read by FUN_142d79400 for
  the cooldown) now carries an item ability's cooldown — healing potions 10 / 25 / 45 s (Consumables.CooldownOf, also
  what the server enforces), so the client greys and counts the slot itself. Test potioncd 2/2 (client record cooldown
  10.0; a second drink inside it isn't consumed). client.js ability now prints the cooldown.
- 2026-10-12: SITE SCREENSHOTS (Site Management → Screenshots): Claims.TakeScreenshot captures 1366×768 + 128×128 and
  uploads both as MarketplaceImage 0x107 {u64 image id ((char<<32)|time), u64 site, "" folder, size, u32, index, JPEG}
  (now routed to Game/SiteScreenshots.cs instead of refused as a design image), then sends InGamePurchase 0x45 sub 0x49
  ModifyScreenshots {u32 action 1 taken / 2 clear / 3 make primary, u32 index, u64 site, images}; the window hid its
  close button until ModifyScreenshotsResponse 0x45 sub 0x45 {u64 site, u64, bool} (FUN_143183890 / FUN_1431a9a70 →
  GameEvents:OnScreenshotResponse), which was never sent. Shots stored per site/index, served at
  /uploads/lm/images/claims[N]/(small|large)/<site>.jpg. Found through the UI-map name collision (Claims.TakeScreenshot
  vs Templates' binding of the same name: uimap takes the first binding — open: namespace-aware bindings). Test
  sitescreenshot 3/3 (real capture stored).
- 2026-10-12: UI MAP TOOLING FIXED TWICE MORE: luabind.tsv now names each binding's namespace (the table's register
  function: FUN_143924880(L, &table) then lua_setglobal FUN_143918180(L, name); 1522/2081 resolved), so uimap picks
  Claims.TakeScreenshot rather than Templates' binding of that name; and the packet-class regex missed classes starting
  with "Packet" (PacketInGamePurchase…, PacketTeleport…). 14 more UI gaps surfaced: router-handled (radial menu,
  teleport locations / random cave, product create) and n/a (real-money loyalty store, travel queue) → verdicts, and
  one real: WORLD MAP SITE SEARCH — Ui.WorldMapClaimTagFilterUpdate → CommerceSearch 0x45 sub 0x3D {string query,
  u32}; answered with CommerceSearchResponse 0x3E {string query, u32 n × {u32, u32}, u32 count, u32 n × u64 site}
  (reader FUN_143184530 → map filter FUN_142ceb420) with the landscape's sites whose name / tags hold every word
  (Claims.Search). Test mapsearch 2/2. UI calls 0 gaps, data sources 0 gaps.
- 2026-10-12: SITE DESIGN + MATERIAL REFUND ON DELETE / LAPSE (the client's delete dialog: "All resources … returned to
  your inventory. A copy of your current Build Site will be created and stored in your Designs list"; lapse mail "A
  Design of each expired Build Site has been saved"): Game/SiteDesigns.cs builds a design from the owner's voxels inside
  the site box (VoxelOwnership.OwnedBy + TerrainEdits.Read) — multi-cell block list like a prefab, natural ground left
  out — before the terrain reverts (Claims.Delete and SiteContents.Pack), sends it to the owner's Designs if online, and
  on delete refunds the materials at the build rate (BuildMaterials.VoxelsPerItem). Test sitedesign 5/5 (6×6×6 block →
  design of 216 voxels, 54 Sand back, design placed on a new site).

- 2026-10-12: REFUSED CASTS ANSWER CastFail (0x27 sub 6, reader FUN_142d63390, handler FUN_142d79ee0): the client stops
  the cast, clears its casting / charging state, drops the pending cast (+0x478, by cast guid + hash) and takes back the
  hotbar cooldown it started (FUN_142d87d90, CooldownSearchFunctor). Used for: not enough Energy (weapons, combat gear),
  a cast without an item that gives it, and a consumable refused (full health, cooldown). Before, refusals answered
  CastSuccess, so a refused potion still greyed its slot for the cooldown. AbilityStartFailed / StartCanceled (0x10 /
  0x11) only act on abilities started with AbilityStart — n/a. Test castfail.
- 2026-10-12: TELLS (/whisper, /reply; chat channel 1 — the client's chat format table: 0 Say, 1 Whisper "[X] whispers:",
  2 WhisperEcho "To [X]:"): were relayed to EVERYONE like shouts. Now only the named player gets them; the sender gets
  TellEcho (0x13 sub 5 {u64 target, name block, string, bool}, FUN_1432aa700 / FUN_1432c0f90); unknown names answer
  PacketChat kind 2 "User '[X]' not found.", offline characters kind 3 "User '[X]' is offline." (FUN_1432b7bc0 case 1).
  Ignored senders' tells are dropped silently. Test tells 3/3 (the receiver's /reply comes back, proving the client
  stored the whisperer).
- 2026-10-12: SITE PROP LIMITS IN BUILD MODE: ClaimsBase 0x5F UpdatePropCategoryCount {u64 site, n × {u32 name text, u32
  current, u32 max}} feeds ClaimDialogs.PropLimits ("n/50 Chests" in BuildModeWidget). Game/PropLimits.cs sends it on
  entering a site and to everyone on a site when a chest is placed or picked up. Test proplimits 3/3.
- 2026-10-12: FREE PROPS GIVE NOTHING BACK: the Starting Location (wiki: free, a new one can always be placed) and other
  free placements no longer refund an item when picked up, and a lapsed site no longer packs them into Build Site
  Contents (they go with the site). Fixes sitelapse ("2 props packed").
- 2026-10-12: GROUP INVITE RESULTS IN THE CLIENT'S OWN WORDS: the Invite packet with type 2 and a result code (+0x18,
  table FUN_143b5f4a0) replaces the English chat lines — 0 "[X] has been invited to your party.", 5 full, 9 "not
  found", 13 "already has a pending party invitation" (new check), 14 "already in a party", 16 "[X] declined your party
  invitation.". Unanswered invites now lapse after 2 minutes with RemoveInvitation (0x2B sub 0x17, FUN_142d4ea30;
  codes 3 / 4 "Your party invitation to / from [X] has expired.", which also drops the invitee's popup).
- 2026-10-12: PLAYER STUDIO: IsTemplateValid (0x45 sub 0x4C) is answered (0x4D {u64 design, bool}) — the client holds
  the ProductCreate of a design that contains other designs until it hears back (FUN_1431b7c20 / FUN_1431ad850).
- 2026-10-12: SYSTEM LETTERS: the client's letter texts use <br>, but the mail window escapes markup in bodies (the
  lapse letter showed "<br><br>"); system mail now turns them into line breaks. System mail logs its id.
- 2026-10-12: GAP TRIAGE TOOLS: coverage.py counts sub-opcodes switched on in any file that names a handler's request
  class (Travel.cs etc.); reachable.py leaves out classes with a verdict (re/tools/verdicts.tsv) and lists them at the
  end. ~120 verdicts added (PS2 / Free Realms / EQN leftovers, billing, login-server, raw-relayed HeroicMovement, …).
  REACHABLE-GAPS now: C→S 68, S→C ~20, all remaining checked one by one.
- 2026-10-12: TEST HOUSEKEEPING: combat scenarios take back the weapons they wield; cleanbag empties LawTest's test junk;
  persist / castfail start from known counts; designplace puts MrLawKing's site 420 m from LawTest's (centres must be
  390 m apart); sitelapse takes the Build Site Contents from the lapse letter and re-claims LawTest's site at the end;
  startloc makes sure LawTest has a site.
- 2026-10-12: CLIENT-WORDED RESULTS, round 2 (each traced to the client's handler and its text table):
  Friends — FriendMessage (0x4D sub 8 {u8 code, u64, name, name}) code 0x0C "Player [X] does not exist."; the client
  prints add / remove itself (server duplicates removed); the play client has no /friend remove (Ui.RemoveFriend).
  Guilds — GuildNotification (0x83 sub 0x19 {string guild, name, u8 code}, table at 0x1451356d0) for create (1 / 2 / 3 /
  13), invite (4 / 5 / 7 / 8 new duplicate check / 0 full) and rank change (14); a member who leaves or is kicked now
  gets RemoveMember about themselves ("You have been removed from the Guild.", guild chat closed) instead of Disband,
  which had told them "[guild] has been disbanded.". Learned props — KnownProp AddPropResponse's second field is the
  prop name text, so the client shows "You have learned a new prop" itself; designs added — TemplateBase 0x11
  EnvironmentAddOwnershipResponse ("You have learned a new Design"). Durability — the client says "Some of your gear is
  broken." itself (FUN_142e876e0); duplicate removed.
- 2026-10-12: /sit — the client's EmoteStringById (0x26 0x77, text Emote.SitOther) is relayed to players within 80 m as
  ChatPacketFromStringId (0x13 sub 4 {u64 speaker, u32 text, 3 bools, u32, u64, u64, u32}): "[X] sits down.".
- 2026-10-12: LAPSED SITE CHESTS: opening Build Site Contents puts a chest's items and the chest into the overflow tab
  (wiki Chests); Inventory.AddToOverflow.
- 2026-10-12: CHEST LIMIT PER SITE at placement start: error 12 with (Chests text 573121 << 32 | 50) → the client's "You've
  reached the limit of 50 placeable props for category: Chests".
- 2026-10-12: CONTESTS move through their phases by date (entries → voting → judging; operators' state 4 closes) and
  count votes: Claims.RateClaim's second AddVote (category "Contests") counts once per player per entry while voting,
  with its own VoteReply. dev contestphase <id> entries|voting|judging. Test contestvote.
- 2026-10-12: TESTS CHECK THE CLIENT'S OWN TEXT: client.js chatlog captures what the client displays —
  ShowSystemMessage (FUN_142cbd1b0), chat window lines (FUN_1432beb70) and notifications (vtable +0x28 of
  *DAT_145352490) — so tells, friends, groupinvite, guildinvite, emotesit, proprecipe and designitem assert the line
  the player sees, not just the packet. run.py log steps now substitute {s:capture}.
- 2026-10-12: PUBLIC RELEASE PREP. LandmarkEmu/start.bat + start.ps1: first run asks for the Landmark folder (auto-detects
  Steam libraries), checks .NET 10 / MariaDB, creates database user lmemu_local with a random password, builds Release,
  copies the game's Resources/*.txt + Locale/en_us_data.dat into gameData (plus data/WeaponAnimationClasses.txt and
  LandmarkEmu-terrain-heights.zip if present), writes AuthServer.json / WorldServer.json (127.0.0.1 only, web API on
  http://127.0.0.1:5000), offers to point ClientConfig.ini at the server (backup .orig), creates the account; answers in
  launcher-settings.json (git-ignored). Later runs: servers + login + game. Docker/ops removed. WorldServer
  GameDirectory has no built-in path any more (your local WorldServer.json now sets it). Research tools take paths from
  re/tools/paths.py (LANDMARK_DIR / LMEMU_WORKSPACE); shell scripts from their own location (LMEMU_ROOT).
  re/tools/export_public.py stages the public copy in export/public (server source at HEAD, research notes with
  decompiled code blocks removed, decoded tables, notes) and refuses to finish on secrets / personal paths / client
  files. Launcher verified end to end on the staged copy against the test client (build, configs, account, login).
  export/release/LandmarkEmu-terrain-heights.zip (747 MB, all 29 landscapes) is for a GitHub Release asset.
- 2026-10-12: REA (rea-agents 6.4.0) installed and run on the test client (full Ghidra analysis to
  re/rea/Landmark64.rea-snapshot, background). Gap hunt against the decompile while it runs:
- 2026-10-12: OTHER PLAYERS' BUILD CURSORS: WorldEditingBase 0x10C (Clear / Box / Sphere / Shape / Selection / Preview
  setup + update / Fill; 17k+ received in play, verdict wrongly "unreachable") is now relayed to players within 160 m
  (Game/WorldEditing.cs). The client draws them itself (ClientWorldEditingEffectManager FUN_142fe3ae0, FUN_1433b9530;
  one effect per builder guid). Newest packet per builder at most every 100 ms; identical repeats only to newcomers;
  Clear(0) to viewers who leave range and when the builder leaves; packets whose guid isn't the sender's are dropped.
- 2026-10-12: EDITING A PLACED PROP: the prop tool's pick sends PropAccess (0xED sub 1, FUN_142f15300) and waits for
  the server's sub-2 answer before opening the gizmo; nothing answered, so moving an existing prop never started (seen
  in play: 3 requests, no gizmo). Props.Access answers with the prop data (owner, or Let's Decorate on its site) or
  refusal 10 / 13 (Play Mode). Props.Save accepts decorators too (within decorate access). Test propaccess.
- 2026-10-12: REVIVE CHANNEL: cast 246 sends StartRevive (0x42 {dead, reviver, 3000 ms}) to players near the body
  (reviver's HUD bar, every client records the reviver), ReadyToRevive after the channel, StopRevive (0x43) if it
  breaks off; result lines use the client's CombatSpam texts 564592 / 564593. dev reviveby <name>. Test revivechannel.
- 2026-10-12: DESIGN LIST REQUEST (0xF0 sub 1, Lua GetList) answered with the list (was dropped).
- 2026-10-12: OpcodeStats.Unanswered counts every request that reaches a handler's default branch (and raw / no-handler
  packets) into opcode_stats.tsv; coverage.py lists them ("runtime truth") — its static "sub in handler" match was
  fooled by case labels in shared handler files (Template 0x12 showed handled).
- 2026-10-12: CHARGED ATTACKS NO LONGER TRIP THE FLOOD LIMITER: while a power attack charges the client sends
  Abilities 0x27 0x17 DelayedTargetInfo every frame (617 in 2 s for 733; ClientAbilityManager vtable slot 2 →
  FUN_1433ad3e0). Those used up the 100/s packet budget and the server dropped other packets (seen twice in play,
  including MrLawKing's own session at 11:09). They are now discarded before the limiter (the server aims from the cast).
- 2026-10-12: CAPSTONE ANNOUNCEMENTS: completing an achievement whose reward is a capstone title (the Dramatic, Blockhead,
  the Collector, the Creator, Warmaster, Elementalist, Researcher, Fashion Mogul, Luminary; mapping from the live
  capture's reward bundles) sends AchievementBase 0x0F GlobalAnnounce to everyone online with the client's text
  (575528-575537, 575546: "[X] has become a Blockhead!"); the client prints it on the System channel and on screen
  (FUN_142d921c0). dev achreset <id>. Test achannounce.
- 2026-10-12: MESSAGE OF THE DAY: PacketMOTD (0x5A {string subject, string body}) once per login from WorldServer.json
  MotdSubject / MotdBody (client shows it as a system message and in UI.MessageOfTheDay). Not yet tested in the harness.
- 2026-10-12 (stopped here, out of credits): next = run scenarios propaccess, revivechannel, achannounce, then check
  re/out/uisinks.md rows (ChatPacketEnterArea, TradeRejected, Mail attach response, ZoneDisplayNotification) and diff
  REA's procedure/string lists (re/rea/procedures.json, strings.json; job run_lists.sh) against re/decomp_all/index.tsv.
- 2026-10-13: REA RESULTS: REA's Ghidra run of Landmark64.exe found 234,612 functions vs our 234,539; the 73 extra are
  PlayerUpdate packet constructors our project never made functions of (re/tools/rea_missing.py → 
  re/out/rea_missing_functions.c). They confirm 65 PlayerUpdate sub-opcodes and name two unknowns: 0x4B
  PlayerInGamePurchaseResult, 0x55 PlayerUpdateNonPriorityTargetCharacters (trace/playerupdate_subs.md).
- 2026-10-13: LaunchPad.exe (32-bit, built 2026-05-13: Daybreak's current shared launcher, not Landmark's 2016 one) is a
  CEF web shell that self-patches from manifest.patch.daybreakgames.com, loads a patcher DLL and shows Daybreak's login
  pages (lpj.daybreakgames.com) before launching the game with a session id. Not useful for the emulator: it needs
  Daybreak's live services and may update itself / the install. play.bat replaces it.
- 2026-10-13: PROP RIGHT-CLICK MENU now offers the client's full prop button set (RadialMenuButtons.txt type 1):
  Duplicate always, Settings / Script / Light on-off where the prop has them (was adjust, reposition, pick up, delete).
- 2026-10-13: BUILD MODE PAGE REMEMBERED: Tab re-created the build widget on Designs while the client's active category
  stayed on the last tab (panel and tool out of step). ClientFixes wraps Ui.SetActivePaletteCategory and restores the
  last page (OnSetTab) plus the category on OnWidgetLoaded. Verified: Props → Tab off → Tab on comes back on Props
  (UI events 9 then 11; scenario adhoc/pagememory).
- 2026-10-13: ADMIN MENU: WorldServer.json "Admins" (start.bat adds its own account + server.ini Admins=). Admins type
  /admin → Admin page in the in-game browser (Game/Admin.cs, served at /admin with the Showcase token): god mode,
  revive, coins/items, travel, claim, play mode, time of day, creatures, shutdown, any dev command, per target.
  Verified: page opens in the test client, commands run (logged "Admin X ran"), bad tokens refused.
- 2026-10-13: TAB HOLSTERS THE HELD ITEM: build mode on moves the main-hand item to the bag (remembered), build mode
  off equips it again (Game/BuildModeHolster.cs; the hotbar's own DoAction does the same TransferItem moves).
  Verified with the pick (adhoc/holster, adhoc/holstershot). Harness: client.js request queue now served by the
  newest attached script only (generation at queue+28), so a lingering runner no longer eats later probes' answers.
- 2026-10-13: FINE-TUNE (Prop.AdjustObject) FIXED: PropAccess replies carried the new-placement flag (+0x6c), so
  FUN_142f1ab30 took the new-placement path and never started the tweak handles (state 7 → FUN_142f21d20). Cleared for
  placed props. Verified by the user on the test client: torch moved 5 m and rotated, both saved (world_prop row).
- 2026-10-13: TINTS FIXED (5692c4c): the client's TintDefinitionManager (DAT_145351d90; lookup FUN_143c10110 by alias
  hash +0x268 and type +0x26c or "ANY") is REPLACED by ArtData 0x116's lists on arrival (FUN_142c6ec20 → FUN_143c113c0;
  list 7 = tint groups, element reader FUN_142b96540). We sent eight empty lists (since 2026-10-11), so the table was
  empty (runtime probe adhoc/tintmgr: 0 entries) and no tint showed — props, items, character defaults. The SetTint
  path itself worked (ED 16 requests saved and broadcast). Now the world server sends the login server's ArtTints.json
  (Game/ArtTints.cs, linked into the build; 1114 aliases, 735,806 bytes once per login). Verified: 1114 entries, the
  client log's "failed to apply default tint" is gone. Also: editable props flagged/answered interactable (right-click
  verified by the user), prop right-click menu only in build mode (BuildModeHolster.InBuildMode).
- 2026-10-13: Editable-only props (fences, decorations) no longer show the interaction cursor (CursorInReach 0); they
  stay pickable for right-click / tint. Tint after relog confirmed by the user (stool kept Sky Blue).
- 2026-10-13: Editable-only props are interactable ONLY in build mode (the interact / held-tool cursor showed on
  fences outside it). Props.RefreshEditable re-sends InteractionInfoReply for nearby editable props on every build-mode
  toggle (client applies unsolicited replies, FUN_142ee6b90). Verified in the log while the user pressed Tab: on → 11
  props type 3; off → decorations type 0, the 3 usable props stay 3.
- 2026-10-13: MISSING VOXELS AFTER LEAVING A CLAIM: ApplyDiff broadcast excluded the builder (their client drew the
  edit itself), so the builder's VoxelFarm manifest (+0x150, FUN_143f5cc90; reload check FUN_143f4e3b0) kept the old
  cell version and a reloaded cell showed the older copy. Now TerrainEdits.NoteVersions sends the builder a
  ModifiedCellManifestUpdate per changed cell. Mining, paste, undo and heal already included the actor. Fresh login:
  client fetched 31 edited cells. To confirm with the user: build, walk >300 m away, come back.
- 2026-10-13: "Black Halloween voxels missing": Halloween Black (VoxelMaterials index 695, CURRENCY_ID 73) is built
  from Stone (item 46, 4 voxels per Stone); LawTest had 211 Stone and the builds needed up to 2000, so the server
  refused them (correct) — but the builder's client kept drawing the refused edit until the cell reloaded. ApplyDiff
  refusals (materials, permission) now re-send the server's cells (Building.UndoLocalEdit → TerrainEdits.Resend), as
  Heal already did. The client's own message is "Construction.InsufficentMaterials" (FUN_1430b6560).
- 2026-10-13: BUILD COSTS = ITEM_ID (47c48f1). Materials tray (BuildView.as: IsFree → "∞", else Quantity) is filled by
  FUN_1436f4180: IsFree = free build || cost item 0; cost item = material record (FUN_1430a75b0, keyed by VoxelMaterials
  ID) +0x28 → +0x90; Quantity = count of that item held (FUN_142e80a10). Runtime read of all 712 records
  (adhoc/matcost): cost item == ITEM_ID (column 40) for every one. The server had used CURRENCY_ID (column 36) first →
  137 mismatches (∞ terrain textures and Halloween Black charged Dirt/Sand/Stone/Snow; Marble vs Cut Marble etc.).
  Now ITEM_ID only; 0 = free (wiki "Free-to-Use Building Materials"). Voxels built under the old rule refund nothing
  when removed if their material is now free.
- 2026-10-13: "GAME CRASHED" = client timeout (90 s without server data) after the reliable stream stalled: a
  MultiPacket carried a 255-byte Data packet with the length as a single 0xFF byte (FF 00 09 02 3F …). Our reader
  took 0xFF + u16 (0x0009), cut it apart ("unknown protocol packet 0000023F"), seq 0x023F was never received and every
  resend failed the same way. Fixed in MultiPacket.ReadLength (shared with DataStreamInput): 0xFF followed by a u16
  below 0xFE is the single-byte form (the client's writer FUN_142ad3650 only uses the long form from 0xFE up).
  Replay test (scratch): crash shape → 255 + 68-byte packets; FF 00 FE and FF 01 00 still read as 254 / 256.
  The AuthServer process still runs the old build until its next start.
- 2026-10-13: Build cost near ore veins: TerrainEdits.MaterialDelta now treats vein ore (OreVeins.Compose) the overlay
  doesn't set as existing ground — kept ore costs nothing, removed ore refunds nothing (was: "needs 10 × Cut Marble"
  while building sand next to Raw Marble). Builder manifest notes (5103b1e) were sent per edit and made the client
  re-fetch every edited cell at once (3-11 CellsUpdates per click): spamming build/delete stalled behind them. Now
  queued per builder (TerrainEdits.NoteVersions/UpdateNotes) and sent when the builder is 128 m from the cell.
- 2026-10-13: TERRAIN LOD (Game/Terrain/TerrainLod.cs): edited areas get level 3-6 block copies (each parent voxel =
  2x2x2 majority of its children; set when >= 4 children set, solid with the most common material when most are
  solid, else air). Manifest + Answer + Has serve them like overlays; built at start (406: 663 copies in 3.9 s, 416:
  142 in 0.8 s, background) and refreshed incrementally after 20 s without edits on a landscape. Verified with the
  experiment (dev lodbuild): the client requested 67 level-3, 7 level-4, 1 level-5 copies, and the user saw removed
  terrain stay removed while moving. Copies skip ids the captured live store has (home continent).
- Build tool "nothing happens" while spamming: the client drops edits while its voxel engine has a pending fetch
  (ClientConstructionManager FUN_1430b4e90 checks FUN_143f532e0). Deferred builder notes fired mid-edit (big brushes
  reach cells > 128 m away), the fetch answered a newer version than the noted one, the request hung 50 s. Notes now
  also need 15 s without touching the cell. Build permission: Claims.BuildCheckIn per cell (was per voxel); typical
  timings now 1-4 ms small edits, ~100-270 ms for 20-43-cell removes.
- 2026-10-13: ROOT CAUSE of "removed terrain comes back" + "build tool hangs": the per-session flood limiter (300 burst,
  100/s) was exhausted by WorldEditing 0x10C build-cursor packets (up to 506/s while building) and dropped terrain
  cell requests (one 131-cell request at 10:46:10 never reached the handler). Unanswered → after the client's 6 s
  SCENE timer (FUN_143f571d0, ClientSceneProcessingErrors.log "ServerResponsePending: 1") it drew generated ground,
  and the pending fetch made ClientConstructionManager refuse new edits. Fix (WorldSession): 0x10C handled before
  the limiter (WorldEditing throttles its relay), 0xEC/0xF4 on their own limiter (200 burst, 50/s). Client facts:
  CellsUpdate entries clear the scene's server-pending flag regardless of version (HandleCellsUpdate FUN_143f52db0);
  ClientCellRequest per-cell 4×u32 are the client's cached layer versions (hash-like values, e.g. 1156681913).
  Terrain code archived first: git tag terrain-v1 + archive/terrain-v1/.
- 2026-10-13: Build edits up to 1024 cells (was 64: 140-cell brushes were refused with no per-cell replies, cells
  stayed pending on the client → tool hung, its drawing discarded). Unreadable edits answer every cell read. Speed:
  per-phase timing in the build log; apply phase was 660-850 ms for ~140 cells (per-voxel always-true lambda, bitwise
  mask scans, 384 KB vein scratch per cell) → 0.2-0.45 s total now; the reply goes out before the undo-after record
  (1ff29f9, live at next restart). Client "busy" list (FUN_143f532e0) read at runtime during a stall: empty.
- 2026-10-13: TERRAIN V2 (502afd0, b810f20; old code tagged terrain-v1 + archive/terrain-v1/). Client trace in
  trace/systems/terrain.md, format notes in docs/terrain.md "Layers" / "Coarse levels". Root causes of the flicker /
  old shapes / blocky refetches: the client keeps a player's own edits in LAYER 1 (commits level-2 edits into layer
  level-1, caches them there on the per-cell OK) while we stored everything in layer 0, so it drew both copies; a
  CellsUpdate that leaves a layer out keeps the client's old copy. Now: overlays keyed (cell, layer) — build tools /
  paste / undo / heal → 1, mining + veins → 2 (heals), stamps → 0; one-time migration moved 86M build-site voxels
  (1,780 cells) to layer 1; every CellsUpdate carries layers 0-2 with own versions; requests with current held versions
  → UpToDateCells, stale held layers → sent empty; no pushes/notes to the builder. Coarse copies per layer with
  VoxelFarm's octree rule (corner child's state, mass-point vertex; updateBlockLOD FUN_142a15480), default on.
  Build edits in two passes (Prepare/Commit). Also today: flood limiter starved by 0x10C cursor packets (dropped cell
  requests), 64-cell edit limit, timeout resend dedupe, ITEM_ID build costs, real tint definitions (ArtData 0x116).
  Open: reproduce the client's CRC32 layer versions (SampleVersion collects samples) so builders don't re-download
  their own cells at login; Achievements "Saving objectives failed: Duplicate entry '2-1131'".
- 2026-10-13: BUILDING (module 2 of trace/REBUILD-PLAN.md, archived building-v1): ApplyDiffRequest fully decoded
  (trace/systems/building.md); server adopts the client's new layer-1 version per cell and refuses stale-base cells
  (re-sends them). Login requests 1 cell instead of 400-500. ApplyDiff parser now skips the u32 after type-2/3 cells.
- 2026-10-13: BUILDING ResponseObserved (0xF4 sub 0xA) — other players within 100 m now see build effects
  (placement/delete/heal/smooth/paint) for every applied edit. Packet and op→effect map traced and verified in the
  client with Frida (all 7 ops). Dev command `observed <op> [size m]`. ApplyDiffRequest +0xe0 = operation kind.
- 2026-10-13: BUILDING audit continued: undo answers Response(3,0) (refreshes Voxel Vision); design storage is
  counted in voxels like the client (6M + expanders, max 48M; refusal 0x11 offers expanders) instead of 100 designs;
  Response result codes documented (0x11, 0x1A). TemplateBase family trace sub numbers were 2 too high (server right).
  PropData decoded; design/copy props (live: server gathers props in the box, places them on paste, lists skipped)
  moved to module 3. Building → verify in REBUILD-PLAN.
- 2026-10-13: PROPS module started (props-v1 tag/archive; trace/systems/props.md). Prop moves are undoable (0x10
  UndoRedoNotification, one step per edit session). Copies and designs keep the props inside the box and place them on
  paste from the player's items; missing ones are listed by the client (VoxelConstruction 9 SkippedProps); undo picks
  them up. Dev: propmove, undo, redo, giveprop, copyhere, pasteat.
- 2026-10-13: PROPS text props: Rustic Sign / Fjorden Books / Gold Scroll get the Links "display the text" action
  with Text Location / Text Duration settings; PlayerUpdate DisplayPropText (opcode fixed to a one-byte sub, 26 00 79).
- 2026-10-13: PROPS: renamed props show their name for everyone (PlayerUpdate 0x7C UpdatePropName).
- 2026-10-13: CLAIMS module started (claims-v1): unknown names in Special Access get the client's own error (ResultResponse 0x10, result 0x17).
- 2026-10-13: INVENTORY module started (inventory-v1; trace/systems/inventory.md): TransferItemResponse results now the client's reasons (1 = success, chest permission / NO_TRADE / chest-in-chest messages); NO_TRADE flag decoded.
- 2026-10-13: INVENTORY: NO_TRADE items can't be traded or mailed (client reasons 0xA / 0x12 / 0xF in mail).
- 2026-10-13: Regression subset run (two runners overlapped by mistake → one-off flaky failures; each scenario passed in a clean run). Trade window icons verified in-client (tradeimg screenshots).
- 2026-10-13: PROPS: signs / books / scrolls with text can be read by using them (text window); saving sign text no longer re-opens the editor.
- 2026-10-14: LOGIN SPEED: the "Loading into world" wait was the item definition list (CommandBase 0x4B, 2,885 items,
  283 KB, as live sent it): the client opens ItemDefinitions.log for every log line (~5 ms each with real-time file
  scanning) = 15 s fresh, 30 s on a relog (it also logs "Overwriting existing ... in cache"). Measured with Frida
  (re/test/loadtime.py, adhoc/loadtime.js, logcost.js). FileLogLevel in ClientConfig.ini does not turn it off.
  Fix: a client coming back from character select (account session key still the one issued in 0xF7) keeps its
  cache, so the list is not resent (user verified: 30 s -> 2 s). Fresh logins still get the full list (most client
  lookups never ask for a missing definition). New: CommandBase 0x49 ItemDefinitionRequest answered with 0x4A
  ItemDefinitionReply. dlogin.sh clears the session key (a new process must get the list). The remaining relog stall
  (27 s once) was the client's first terrain build landing in the relog loading screen when logging out right after
  entering; a later relog took 14 s in total. re/tools/sample.py: read-only thread stack sampler.
- 2026-10-14: CHARACTER SELECT shows the equipped clothing/armour (login server: attachments from equipped items with
  a Char_ model, equip slot as the attachment slot, item tint alias; verified in the test copy's AttachmentProcessor
  log + screenshot re/test/shots/charselect_outfit.png). Was a random chest piece.
- 2026-10-14: ORE VEINS stay out of build sites (spawn spots touching a site refused; placing a site moves the veins
  inside it; dev command veinsweep). 24 veins were moved out of existing sites on 416.
- 2026-10-14: WORLD STATUS: world heartbeat (zone_server.heartbeat/playersOnline, every 5 s); the login server pushes
  ServerUpdate on change. The world stays listed as active (status bit 1 would make CharacterSelection.lua quit with
  "No Servers!"); Play while it is down → "Game Server not found." and the player stays at character select.
  World shutdown now runs the client's Ui.Logout for every player first (proper logout path; a dropped connection
  left the client freezing mid item-definitions on the next join). Restart the world with dev "shutdown 10".
- 2026-10-14: LOAD / LAG investigation (user's client):
  - Relogin keys now carry R + zone_server id + item-list fingerprint, so a relog to the same server after a restart
    skips the 2,885 item definitions (0->50% 30 s -> 3 s). Different server / changed items still get the list.
  - Shutdown runs the client's own Ui.Logout first (proper teardown), 0xF7 push only as a fallback after 8 s.
  - In-game lag + 1-2 min loads were the CLIENT's terrain mesh cache: Cache\2\Mesh\416\VoxMeshW_*.pack had grown to
    ~430 MB (two 200 MB packs) from repeated full re-meshes; each contour took seconds ("Avg contour" 2000-6000 vs 3-5).
    Moving the Cache folder aside fixed it (backup: backups/landmark-cache-2026-10-14). Fresh load 30 s, relog after
    restart 50 s with the cache staying ~108 MB. Proposed: launcher clears a landscape's mesh cache over ~250 MB.
  - Auth server had two threads spinning at 100% (5,480 CPU-s in an hour) until restarted; cause not found yet
    (re/test/runs/authcpu.log watches it).
  - Open: relog after a world restart still re-meshes for ~45 s (manifest is the same across restarts).
  - Loot pinata SpawnLoot fields after the position are item/count (was a position copy): full-bag pickups of
    Collections resources work.
  - Follow-up: with the cache rebuilt and the relog tag in place, a relog after a world restart takes 9 s in total
    (3 s + 6 s, scene processing 1.1 s). The 47 s relog was the first one after the cache had been rebuilt from empty.
    So the rebuild-over-and-over came from the item-list overwrite (fixed) plus the bloated cache, not the restart.
  - ROOT CAUSE of the fresh-start slowness/lag: real-time antivirus re-scans (~5 ms per open of a file the game just
    wrote; measured on a copy of the pack) on every cached-mesh read. ClientConfig [Terrain] UseCachedMeshes=off +
    CacheMeshes=off: fresh start 24 s (17 s item list + 7 s terrain), relog 8 s, smooth. The launcher now sets it
    (launcher-common.ps1 Set-ClientConfig). The user's own ClientConfig.ini has it (backup:
    backups/ClientConfig.ini.before-terrain-cache).

## 2026-10-14 Slow world loads (50-100%) — cause traced in the client
- **What the loading bar waits for after WaitForWorldReady completes:** the VoxelFarm scene. ClientSceneProcessing.log
  "Begin SCENE ... Process Pending: N" → "Finish SCENE: Total Process Time" (1.3 s fast, 150-300 s slow); the work is
  ContourThread::contourLoop (FUN_143ed5a30, 5 threads; "Avg contour" logs once per 5 s of contour time).
- **Cause:** the client allocator's per-thread cache table holds **64** slots (FUN_142b6b7f0, table 0x145347ed0, stride
  0x258, thread id first; used count 0x145347858). When full, a thread with no slot walks all 64 under a global
  critical section on every allocation, checking each owner with FUN_142b6e490 (OpenThread / GetExitCodeThread /
  GetProcessIdOfThread / CloseHandle). The client runs ~176 threads; ~55 slots are taken during startup, and at
  world-ready the contour threads race the embedded browser (UI Browser.Create → CEF, 14 libcef threads) and other
  new game threads for the last ~9. Verified 2026-10-14 07:15 by a read of the table: the two busy contour threads
  had no slot; read-only stack samples showed them in NtWaitForAlertByThreadId / NtOpenThread /
  NtQueryInformationThread / NtClose under malloc. Fast loads = the contour threads won the race.
- **Not the cause (ruled out with evidence):** area definitions (WaitForWorldReady completed at once every run; the
  `bAreaDefinitionsLoaded=0` lines were from another day), ore veins (none generated, still slow), server CPU spin
  (fixed separately, still slow), the mesh cache (on all along: UserOptions.ini `[Terrain] CacheMeshes=On`
  overrides the ClientConfig lines, which never took effect).
- Pool sizes are fixed in code; the only thread-count setting found is `[AssetDelivery] DirectThreadCount` (default 4).
- **Test 2026-10-14 07:29:** ClientConfig `[AssetDelivery] DirectThreadCount=10` (shipped) → 2: 171 threads, 58/64
  slots used (no thread without one), terrain scene 794 ms, Play → loading screen gone in 17 s. Launcher now writes
  it (commit 9ab20d6) and removes the dead [Terrain] lines. Backup: backups/ClientConfig.ini.before-directthreadcount.
  Confirmed at the old slow spot (-626, 302), fresh starts: 07:32 scene 1206 ms (58 slots, game start → screen
  hidden 14 s), 07:33 scene 854 ms (59 slots, 15 s). Same 6585 cells took 150-295 s before. Slot use climbs to
  ~61 while playing (late threads); only the world-entry race matters. Check a load: bash re/tools/loadcheck.sh.
- **Live history:** Daybreak forum thread 57166 (archive.org): devs confirmed the 50% hang was "related to number
  of cores" (13 Jun 2016), patched 13/14 Jun, 16 Jun (hex-core), 30 Jun (8-core), never fully fixed. Player
  workaround: msconfig fewer processors. Our 0.589 build (4 Jun 2016) predates all fixes. Also hit on later re-zones
  in a session (slot use grows while playing): watch teleport/cave loads.
