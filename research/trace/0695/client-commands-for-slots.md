# What the server can make the 0.695 client do about the 64 allocator slots (research, 2026-10-21)

Research only. Nothing was patched, nothing was changed in the emulator, no client was started. Everything below
was read from `re/decomp_all_695` and `re/out` (luabind_695.tsv, rtti_695.tsv, packet_ids_695.tsv).
Background: [allocator-slots-options.md](allocator-slots-options.md), [allocator-slots-fix-design.md](allocator-slots-fix-design.md).
Marks: **[P]** proven (read in the code), **[I]** inferred (reasoned, not measured).

## Plain-language summary

The question was whether the server can tell the game to use fewer threads, so the 64 project rule "fast lanes" (slots)
stop running out on big CPUs. **Mostly no.**

- **The big thread pools can't be changed from outside once the game is running.** The two pools that grow with the CPU
  (Decompression and TaskManagerJob, one worker per logical processor) are created once at start-up. No console
  command, Lua function, settings file key or server packet changes their size later [P]. `DirectThreadCount` is
  the only thread setting in a file. It lives in ClientConfig.ini and is read only at start-up [P].
- **Most of the client's console settings can't be reached.** The client has about 694 console variables (for example
  `MaxVoxelsForPreview`, `cl_asset_timeslice`, `enable_threaded_physics`, `r_voxelstop`). The retail game hides 632
  of them: at start-up it turns on a "retail" switch, and the command lookup then skips every developer variable [P].
  The 62 left visible are camera, window, font and character-creator settings. None of them touches threads or
  terrain.
- **One thing does free a slot while the game runs: switching multi-threaded rendering off**
  (`GameSettings.SetMTR(0)`). On the next frame the game stops and joins its one render worker thread [P]. That frees
  **one** slot, which the next slot-less thread that allocates (most likely a stuck contour thread) can take [I]. It
  lasts until the game is restarted and costs some FPS [I]. One slot can tip a borderline PC (about 16 threads), but it
  can't help a 22-24-thread CPU, which is short 14-20 slots.
- **A few settings may cut terrain work** (claim-wide LOD off, shorter render distance, lower flora), but it is not
  proven that any of them makes the contour threads mesh fewer cells. All of them change and save the player's own
  graphics options.
- **Some calls would make things worse:** toggling voxel smoothing reloads the terrain (more meshing). Switching MTR
  back on while the table is full creates a new render thread with no slot (frame-rate collapse). Changing the overall
  quality preset rewrites several settings and pops up a "restart required" dialog.
- **Security note:** the client's Lua has `package.loadlib` (it can load a DLL from disk). The server's Lua path could
  therefore reach native code. Never use it, and consider adding `loadlib` / `require` / `package` to the dev-command
  block list.

So nothing reachable from the server fixes the slot problem. The real fix stays on the client side: the LaunchPad CPU
cap P1 (+ P3), still waiting for the user's decision. The server-side levers worth measuring are about *behaviour*
(section 6), not client calls.

---

## 1. How server-sent commands reach the client (recap) [P]

- `ClientLua.RunCode` binds a console command `lmemuN` to Lua code (`BindCommandToLua` FUN_142edab50) and runs it with
  `Ui.ProcessChatCommand("/lmemuN")` (FUN_1435c6cc0). Plain Lua code can call any Lua native, so
  `GameSettings.SetMTR(0)` is written as code. `RunWithArgs` is not used for natives, because native table functions
  get the table as their first argument (ClientLua.cs notes).
- Slash / console text goes to the generic command queue: executor **FUN_142bfcaa0** (log `CommandQueue.log`
  "Doing: %s", "**** Unrecognized command - %s"). It looks the name up with **FUN_142bfcf20(name, DAT_145422f91, type)**.
- `Ui.DoCommand` / `DoCommandWithStatus` (FUN_1435cfbf0 / FUN_1435d0000) hand the text to the ClientCommandProcessor
  (PlayClient +0x31f00, FUN_1433b84f0). That ends in the same queue.
- Dynamic commands (CommandList 0x1E/0x44-0x47) only add names that forward to the server (`__sendworldcommand`);
  they never reach client internals.

## 2. Console commands and console variables (the CommandQueue registry)

### 2.1 How they are registered [P]
- One global registry `DAT_145422fa0`: a hash table with 256 buckets (FUN_142bf9470). Entries are added by
  **FUN_142bfb8b0(registeredFlag*, command, changeCallback)**. A callback in the third argument goes into the
  command's array at +0xc8/+0xd0; FUN_142bfb630 calls it after every change. That is how a variable acts at run time.
- Command object: vtable +0x00, name string +0x28/+0x30, type (vtable +0x08): **0 float, 1 int, 2 bool, 3 string,
  4 vector** (setters FUN_142bfbf20 / bfbed0 / bfbf70 / bfbde0 / bfbe70), 5/6 function, 7 alias. Value pointer
  +0x100. **Byte +0xf8 = "developer only"**: base ctor FUN_142b7c980(obj, name, devOnly) stores the third argument
  there.
- Static variables are built by atexit-registered initialisers (`FUN_142b23aa0(&cmd+0x28, "name")`,
  `…Internal::FloatCommand::vftable`, `+0xf8 = 1`, `+0x100 = &var`). Member commands of `ClientCommandProcessor`
  (part_0101 ~14690, `MemberFunctionCommandProtected<…CheckCCPPlaying>`) pass `devOnly = 0`.

### 2.2 The retail switch hides developer variables [P]
- PlayClient start-up **FUN_142ce9580** (the function that loads UserOptions.ini / ClientConfig.ini) calls
  **FUN_142bfbdc0(1)** → `DAT_145422f91 = 1`. Nothing ever sets it back: FUN_142bfbdc0 is its only writer and that call
  its only caller.
- FUN_142bfcf20 then returns a command only if `restricted == 0 || cmd[+0xf8] == 0`. Every lookup passes
  `DAT_145422f91`: the executor, the typed setters, `alias` and `clearcvar`. So **every developer variable is
  "Unrecognized command" in retail**. That includes code paths that set them by name.
- Census (scratch script over the decompile, 694 variables found: 367 float, 180 bool, 101 int, 30 vector, 14 string,
  plus `alias` / `clearcvar`): **632 hidden, 62 visible**.

### 2.3 The 62 visible ones (none touches threads, jobs, memory or terrain) [P]
`force_windowed`, `soemote_enabled`, `soemote_first_time`, `tp_fov`, `fp_fov`, `charCreate*` (9),
`headZoomMultiplier`, `voice_volume_max`, `browser_scroll_rate`, `livedriver_feature_enabled`, `webcam_device_id`,
`cl_alpha_cutout`, `cl_render_target`, `only_show_fg_in_letterbox_area`, `window_*` (11), `ws_*` (8),
`cl_enable_font_load_alerts`, `r_font_ft`, `r_ft_*` (21), `alias`, `clearcvar`.

### 2.4 Hidden variables that would have mattered (unreachable in retail) [P name/type/var; effect I]
| Name | Type | Variable | Would do |
|---|---|---|---|
| `MaxVoxelsForPreview` | int | DAT_145295470 | cap on the design-preview voxel count |
| `cl_asset_timeslice`, `cl_asset_timeslice_loading` | int | DAT_1451f2c94, DAT_1451f2d90 | per-frame asset work budget |
| `enable_threaded_physics`, `enable_threaded_proxies`, `physics_job_start_early` | bool | DAT_1451f295a, DAT_145430b29, DAT_145430b10 | physics on jobs |
| `r_voxelstop` | bool | DAT_145453df9 | stops voxel/terrain updates (checked in FUN_1431596b0) |
| `disable_physics_when_voxels_loading` | bool | DAT_145249539 | – |
| `Physics_performance_estimate_*`, `physics_simulation_rate_throttle_threshold` | float | – | physics budget |
| `voxeldebug*`, `voxel_debugdraw*`, `voxelwater*` | int/float | – | debug only |

None of them changes a thread count; even unlocked, they would only change how much work runs on threads that
already exist.

### 2.5 ClientCommandProcessor slash commands (all visible) [P]
57 handlers (rtti_695: `MemberFunctionCommandProtected<ClientCommandProcessor,…>`), all gated by `CheckCCPPlaying`
(FUN_1433a2810: game state 3 or 6-9). The ones that touch rendering or memory:

| Command | Handler | Gate | Does | Useful for slots? |
|---|---|---|---|---|
| `/shadows On\|Off\|Auto\|Low…Uber\|Custom\|PCF\|maxlocal\|pcbias…` | FUN_1433b4380 | admin client or admin-player flag (+0x7b0d8) | shadow quality / params, FUN_142cbf700 | no (GPU work) |
| `/flora On\|Off\|Radius n` | FUN_1433a5710 | same admin gate | flora draw on/off, radius | at most less flora job work [I] |
| `/fog`, `/zrange` | FUN_1433a1c90 / FUN_1433a2750 | (not read) | fog / z range | no [I] |
| `/memownerreport` | FUN_1433afa90 → FUN_142c09710 | playing only | writes `ownerreport.txt` **while holding the allocator's global lock 0x145426420** | diagnostic only; causes a hitch |
| `/oomreport <enable> [file] [reserve]` | FUN_1433afc20 | playing only | arms an out-of-memory report (10 MB reserve) | no |

Turning on the admin-player flag for everyone, just to reach /shadows or /flora, would also unlock the admin
commands. Don't.

## 3. Lua natives (luabind_695.tsv) that touch threads, terrain or settings

All are callable through `ClientLua.RunCode` (e.g. `GameSettings.SetMTR(0)`). Integer/boolean arguments are read
with `lua_tointegerx` (FUN_1439d07a0), so `0` / `1` work.

| Native | Code | What it does at run time | Threads started/stopped | Terrain work | Saved? | Verdict |
|---|---|---|---|---|---|---|
| **GameSettings.SetMTR(0)** | FUN_1435f67d0 → graphics `DAT_145431090+0x3f` | Per frame, ClientRenderManager FUN_1434b85d0 → **FUN_142e53460**: requested off + active → `FUN_143a95560` (drain) + **`FUN_143a95410` (stop and join the queue's workers)** on queue +0x2f8. On: `FUN_143a95260(+0x2f8, 1 thread, …)` [P] | **−1 thread** (the MTR render worker; it started at boot, so it held a slot [I]) | none | no: no writer of `UseMTR` in the exe; start-up reads `Rendering/UseMTR` default 1 (part_0066 ~7775) [P] | **only runtime slot release found**; costs FPS [I]; one-way per session |
| GameSettings.SetMTR(1) | same | starts a new render worker | **+1 thread**, slot-less if the table is full → every render-job allocation scans 64 owners | – | no | **never** while the table may be full |
| Ui.SetUserOptionsValue("Rendering","UseMTR","0") | FUN_1435d1c60 → FUN_142b555b0 on the options ini (`DAT_145431328+8`) + save FUN_14348c9f0 [P] | writes UserOptions.ini | next start: the MTR worker is never made (−1 slot from boot) | – | **yes**, the player's file | only as an opt-in; it changes the player's settings |
| Claims.SetAllowClaimWideLOD(0) | FUN_1435e6390 → options +0x1540, saved `General/AllowClaimWideLOD` | each update FUN_142cd6830 passes the current claim box to the terrain (FUN_1431596b0 → FUN_143f9a7d0: scene +0x5e4 flag, +0x5e8 box, +0x608 dirty) [P] | none | **less meshing inside claims [I]**: claim-wide LOD presumably keeps the whole claim at full detail (reader of +0x5e4 not found) | yes | candidate; needs measuring |
| Ui.SetRenderDistanceInPercent(p) | FUN_1435cfd10 → graphics +0x1b8 (only when +0x2ec==1, FUN_142e5b840) and `Rendering/RenderDistance` | far plane = min(environment distance, setting) (FUN_142e5c4f0); also env +0x32b0 [P] | none | **not proven** that the voxel scene loads/meshes fewer cells | yes | weak candidate |
| GameSettings.SetFloraQuality(q), /flora | FUN_1435f3310 → FUN_142d4ee80, apply FUN_142d53730 → FUN_143d70840(q−1) | flora density | none | flora is placed on job threads, not contour [I] | yes | small |
| GameSettings.SetTerrainQuality(q) | FUN_1435f2bc0 → FUN_143490f30 (+0xf50, `Rendering/TerrainQuality`) | **no reader of +0xf50 found** apart from the Lua getter (part_0113 ~18442) [P for direct reads] | none | none found | yes | no effect found |
| GameSettings.SetVoxelSmoothing(b) | FUN_1435f31a0 → FUN_142d529f0 → FUN_1431589a0(terrain,1) + FUN_1431ffae0 | **reloads/rebuilds the terrain scene** (FUN_143f95f10, FUN_143153b40) [P calls; I meaning] | none | **more** meshing | yes | harmful |
| GameSettings.SetCacheMeshes / SetUseCachedMeshes | FUN_142cbf530 / FUN_142cbf710 → voxel farm +0x228 / +0x229 (FUN_144020590 / FUN_144020850) | mesh disk cache; writing needs smoothing on (+0x40→+0x7a4) (FUN_1440121f0) | none | unproven (+0x229 reader not found) | yes | unknown; leave alone |
| GameSettings.SetOverallQuality(p) | FUN_1435f7f60 → preset FUN_142d50c60 | rewrites ~10 settings, then shows **Ui.RestartRequired / SettingsRestartMessage** (CreateConfirmationDialog) | none | – | yes | no (visible dialog, overwrites the player's choices) |
| GameSettings.SetGpuPhysics | FUN_1435f66b0 → FUN_14348ece0 | only stores `Rendering/GpuPhysics` | none at run time | – | yes | no |
| GameSettings.AutoDetectSettings | FUN_1435f6420 | re-detects the presets (not read in detail) | – | – | yes | no |
| Ui.DisableRendering / EnableRendering | FUN_1435c65a0 / FUN_1435c65b0 | stops drawing | none (render jobs idle) | none | – | no (black screen) |
| VoxelOwnership.ForgetInternalCache / ForgetLastOwnershipCache | FUN_143600810 / FUN_143600740 | claim-ownership UI cache | none | none | – | no |
| collectgarbage (Lua) | FUN_1439d28a0 | Lua GC | none | none | – | no |
| Browser.Create / Destroy, OpenMarketplaceTo, InGameMarketplaceBrowser | FUN_1435e18e0 … | in-game CEF browser | CEF threads live in libcef; when the browser is used, its callbacks run game code that may allocate and take slots [I] | – | – | don't open the browser automatically during loading |
| VideoStreamer.Init/Uninit, Voice.* | – | external SDK threads (own allocators) [I] | – | – | – | no evidence they use slots |
| Ui.OpenSupportUrl, Ui.LaunchBrowser, BrowseTo, … | – | **open the desktop browser** | – | – | – | blocked (ClientLua.ExternalBrowserNatives) |

### 3.1 The engine job queue: which pools exist, and which can shrink at run time [P]
`FUN_143a95260(queue, n, stack, prio, name)` adds n `JobQueueWorkerThread`s (worker loop FUN_143a95170).
`FUN_143a95410(queue)` stops and joins all of them. Creators: Decompression (n = CPUs), TaskManagerJob (n = CPUs),
contour job queue FUN_143133360 (min(CPUs,3)), VoxelCellJob, MorphemeNetworkThread, ParticleEmitter 4, Particles 1,
FileRemoteDelivery 4, AssetDeliveryIndirectJobQueue, DelayedAssetLoader::FileCreateQueue, LodChunkDecompressionThread,
SoftwareOcclusion, SceneVisibilityListJob, ProximityManagerTrackerJob, TcpDriverAsyncAddress, LocalResourceCache,
the terrain manager's CacheMonitorThread, the MTR render queue and three PlayClient queues.
`FUN_143a95410` is called only from: shutdown (FUN_142c772b0, FUN_142d59e40), graphics teardown (FUN_142e52390),
the terrain manager destructor (FUN_14314d5d0) and **the MTR toggle**. So MTR is the only pool the player (and so the
server) can shrink while the game runs.

### 3.2 The terrain manager is rebuilt on a re-zone [P structure]
Zone load **FUN_142cb3510** builds a new terrain manager (FUN_14314bbd0: voxel farm FUN_14314bda0 +
CacheMonitorThread) only when scene +0xf20 is 0, so the old one is destroyed first (FUN_14314d5d0 stops its queue).
Each re-zone therefore ends the old per-terrain threads and starts new ones (contour ×3 FUN_143f98880, VoxelCellJob,
CacheMonitorThread, …). The new ones claim slots again at their first allocation, and can take the dead threads'
slots through the 64-owner scan. That is roughly slot-neutral, but **every re-zone is a new race** [I].

## 4. Anything that makes the allocator reclaim slots

- The reclaim sweep FUN_142c059e0 runs every 10,000 large/locked allocations. The server could trigger it (Lua
  `string.rep` loops make large allocations), but **it only frees slots of dead threads**. A slot-less thread's own
  64-owner scan already takes those at its next allocation [P, allocator-slots-options §1]. No benefit.
- The "allocator off" byte 0x14542698b (P3b) can't be reached from Lua: the game opens only `_G package coroutine
  table string math utf8`, with no `debug`, `io`, `os` or FFI [P, luabind_695 0x1446da880-0x1446da8e0].
- So the only way the server can free a slot is **to make a slot-holding thread end**. In reach: the MTR worker (−1),
  and re-zones (they swap per-terrain threads; neutral).

## 5. Server→client packets that change these settings

None found [P for the names in packet_ids_695.tsv].
- `PacketClientGameSettings` 0x8E (zone case 0x3b, reader FUN_142c236c0): game rules and the time scalar
  (`GameEvents:OnSetGameTimeScalar`, FUN_142d4f050, FUN_143b90b60, FUN_143b94510). No graphics settings.
- `PacketClientSettings` 0xE9 (case 0x3c, FUN_142d513f0): three strings, an int and a bool (URLs / client
  settings). No quality settings.
- No packet name with Quality / RenderDistance / Lod / Thread / Memory / Config. `ReportRendererDump` is client →
  server.
- Live could not push graphics settings either; players changed them in Options or in UserOptions.ini.

## 6. Ranked list: what the server could do

1. **Nothing reachable fixes it.** On a CPU with 16 or more logical processors the boot-time pools alone fill the table
   [allocator-slots-options §2]. The fix that works is client-side: LaunchPad P1 (+ P3), waiting for the user's
   decision.
2. **`GameSettings.SetMTR(0)` through ClientLua.RunCode (best client call, small effect).** Frees 1 slot while the game
   runs [P mechanism, I that the worker held a slot]. Lasts until restart (not saved). Costs FPS [I]. Safe:
   no browser, no file write, no dialog. Never send `SetMTR(1)` afterwards. If built, it should be a server-owner toggle,
   off by default, and fire only when a client looks stuck (the server can't see slots; for example a load that takes
   much longer than usual). Expected gain: one stuck contour thread can recover. That may be enough on ~16-thread PCs,
   but not on 22-24 threads.
3. **Server behaviour (no client call, needs measuring with `re/tools/slotmap.py`) [I]:**
   - Keep re-zones to a minimum (each one is a new slot race for the terrain threads; see the cave-teleport and
     tree-load notes).
   - At zone-in, hold back the burst of props, NPCs and assets until the player's terrain has started meshing. Idle pool
     workers take a slot only at their first job, so fewer early jobs leave more slots free for the contour threads.
     To test: slotmap owner attribution with props held for N seconds.
   - Keep the landing scene small and send large TerrainCell batches (the tree case) after loading. A slot-less
     contour thread finishes a small job quickly.
4. **Optional opt-in settings (save the player's options, so only on request):** `Claims.SetAllowClaimWideLOD(0)`
   [I: less claim meshing], `Ui.SetRenderDistanceInPercent` lower [I: unproven for terrain],
   `Ui.SetUserOptionsValue("Rendering","UseMTR","0")` (1 slot from the next start).
5. **Don't call:** SetVoxelSmoothing (terrain reload), SetMTR(1), SetOverallQuality / AutoDetectSettings (dialog,
   overwrites settings), DisableRendering, /memownerreport during play (takes the allocator lock), anything that opens
   a browser, `package.loadlib` / `require` (native code from disk).

## 7. Security finding (no code changed)

`package` is opened in the client's Lua state (table 0x1446da890; `loadlib` FUN_1439d97b0, `require` FUN_1439d9ca0,
`searchpath`; Lua path getenv reads already noted in allocator-slots-options §2) [P that it is in the open list; I
that scripts don't remove it later]. Server-sent Lua (RunCode, and dev commands on others when DevClientCodeOnOthers
is on) could therefore load a DLL that already exists on the player's PC. `ClientLua.ExternalBrowserNatives` only
blocks browser natives. Suggestion for the user: also refuse `loadlib`, `require`, `package.` and `dofile` / `loadfile`
in dev-command text.

## 8. Open (copy into OPEN-LEADS L198 when convenient)

- Measure: slotmap.py before/after `GameSettings.SetMTR(0)` on a client with a full table. Expect used − 1, or a
  contour thread taking the freed slot.
- Who reads the voxel scene's claim-LOD flag (+0x5e4 of `voxelfarm+0x5a80`), and whether claim-wide LOD really
  enlarges the contour job.
- Whether the render distance (graphics +0x1b8) limits the voxel farm's cell / LOD radius at all.
- TerrainQuality (+0xf50): any indirect reader (a local copy of DAT_145431328)?
- UseCachedMeshes (+0x229): the reader, and whether cached meshes skip contouring at world entry (it would cut the
  slot-less contour cost).
- Whether CEF callbacks (in-game browser) and voice / Vivox threads allocate from the game allocator (slot users).
- The thread names of the MTR queue (DAT_1446ce118) and of the PlayClient queues +0x96a28 / +0x96cf8 / DAT_1456101b0.
- Cvar census method: a scratch regex over the decompile (pattern `FUN_142b23aa0(&X,"name")` + `XCommand::vftable`
  + `+0xf8`). Variables built another way could be missing. A Ghidra pass over all `FUN_142bfb8b0` callers would
  close it.
