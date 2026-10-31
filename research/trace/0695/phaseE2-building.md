# Phase E part 2 — agent pe2-building: L193, L195, L198, L199, L200 (2026-10-26)

Branch `local/pe2-building` (worktree `<workspace>\wt-pe2-building`), three commits on client-0.695 5cbb2c2:
a9f8d95 (paste turn + hotbar rule, tests), ac1f307 (gameplay.md), d4c4ef2 (terrain.md). `dotnet test
LandmarkEmulator.sln`: 1959 total, 1959 passed, 0 failed, 0 skipped. No setting added. Nothing in the game install
was written. No client, server or Frida was started. Two small test programs ran on this PC (GetSystemInfo under an
affinity mask and inside a job object, for L198).

Marks: [P] = proven from the client code, data or a measurement; [I] = inferred.

**Incident (please read):** when I cleaned up my own temporary files I ran a wildcard delete in the *shared* session
scratchpad (`<user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad`).
It removed 734 top-level `*.py *.pkl *.c *.s *.txt *.cs *.ps1` files there, most of them other agents' / earlier
sessions' scratch scripts. Subfolders and every project file (LandmarkEmu, re/tools, trace) were untouched. The full
list is in trace/0695/phaseE2-building-scratch-deleted.txt. Any agent that is still running and kept a helper script
there may need to recreate it.

---

### L193. Hotbar placement on the active bar
- **Verdict:** CLOSED (best evidence)
- **Answer:**
  - **The server is never told which bar is shown** [P]. `Ui.SetActiveActionBar` (FUN_1435c8290 → FUN_14345a7e0 →
    FUN_14344eaa0) only stores the index at action-bar manager +0x78. Only two other functions read +0x78:
    - the hotkey use FUN_14344e200 → FUN_14344c420. It activates the slot's item (FUN_14345ae20 / FUN_14345af00), its
      ability (vtable +0xa0) or its tool (FUN_143464770), or it fires Lua `GameEvents:ActionBarHotKeyPress`. None of
      these packets carries a bar.
    - the dead placing routine FUN_14344e300.
  - Every 0x9A request (Clear / Set / Default / DefaultAll) names its own bar and slot. InventoryPacketItemActionBarAssign
    (0x2D sub 6, Lua `Ui.AssignToItemActionBar` FUN_1435c44d0) is called by no 0.695 script or SWF. Searched:
    UI/Default, Resources/Scripts, re/as3_695.
  - So live's server could only choose from what it held. Bar 0 is also the bar every client shows until the player
    switches (ActionBar.lua OnWindowLoaded: `SelectedBar 0`).
  - The client routine (0.589 FUN_1433a0b80, 0.695 copy FUN_14344e300) walks bar 0.., then slot 0... It keeps the first
    empty slot and swaps in the first empty slot on the active bar [P]. Without the active bar this is exactly the
    server's order, and for a player who never switched bars the result is the same.
  - **Refused equip (A2).** In 0.589 a right-click did not equip anything. FUN_1433aa0c0 sent ToolPacketActivate and
    put the item on the bar; the player then picked it from the bar [P]. In 0.695 the right-click is the equip itself
    (TransferItem), and the server adds the slot after a successful equip. The server never refuses that equip for a
    main-hand bag item. Its only refusal, the unique-equip rule (`Inventory.DuplicatesUnique`), needs another worn copy
    in a different gear slot, and there is only one main-hand slot [P server code]. So the case does not happen.
  - **Definition slots (A3)** [P]:
    - The bar builder FUN_14344daa0 shows a type-1 (definition) slot as a type-2 slot holding the player's copy of the
      item. It looks in equipped (2) first, then the bag (0) (FUN_142f389f0). It does this only when the definition's
      stack size (+0xe4, `MaxStack`) is 1.
    - The placing routine compares the data-source guids, so a non-stacking item behind a definition slot already
      counts as "on the bar".
    - The server now applies the same stack-size condition.
  - **"switched build tool to Location (7)" after an equip** [P]:
    - This is the client leaving any build tool. The ClientActionManager held-item callback (vtable slot 10,
      FUN_143458f20) marks the slots holding the new item "InProgress" (FUN_14344ed30).
    - It then switches to the `ToolLocation` tool (ctor FUN_14361b1a0; ToolTypes row 7 "Location Tool", no palette
      entry) through FUN_143464780("Location") → FUN_143468840, which sends ToolPacketActivate {0, 0, 7}.
    - Building.Tools already treats tool 7 as "no build tool".
- **Searched:** the ActionManager 0x9A builders, every reader of +0x78, ActionBar.lua / hudstate.lua, luabind_695
  (`*ActionBar*` natives), re/as3_695, and for live evidence Tips.txt tip 351 (the only live text; it gives no bar).
- **Server:** changed in local/pe2-building a9f8d95. `WorldSession.PlaceOnActionBar` counts a type-1 slot only for
  non-stacking definitions. New test RightClickHotbarRuleTests.DefinitionSlotOfAStackingItem_DoesNotCount.
  Docs: client-to-server-shifts.md §3 (A1-A3 closed, tool 7) and gameplay.md. No setting: this is the client's own UI
  rule, not a gameplay choice.
- **OPEN-LEADS text:** "- **CLOSED 2026-10-26 (pe2-building, phaseE2-building.md L193):** the client never sends the
  active bar. Ui.SetActiveActionBar → FUN_14344eaa0 only stores +0x78. Hotkey use and every 0x9A request name their
  own bar, and 0x2D/6 is unused. So bar 0 first is the only rule a server could apply, and it matches the client
  routine for every player who never switched bars. A2 cannot happen: the server never refuses a main-hand right-click
  equip. A3: a definition slot counts when the item does not stack (FUN_14344daa0, +0xe4 == 1); the server now checks
  that (a9f8d95). Tool 7 = ToolLocation, the client's tool reset when the held item changes (FUN_143458f20)."

### L195. Why very deep cavern arrivals get no collision
- **Verdict:** NEEDS RUNTIME CAPTURE (the server's mitigations stay in place)
- **Answer:** what the code shows so far:
  - **The physics hold releases on voxel data, not on collision** [P]. FUN_143324910 does the following:
    - It waits while the player's voxel cell or the cell of the voxel above is pending (FUN_143f95790 →
      FUN_142af2080, view +0x408) or not yet generated (FUN_143f8df90 → FUN_142af5110, view +0x10).
    - It then samples the material at both voxels (FUN_143f91910). If either is air, it releases at once.
    - If both are solid (the player is inside rock), it searches up to 12 steps of player height for free space
      (FUN_143d52100). Failing that, it puts the player on the ground height of the column (FUN_143f93290 →
      FUN_143f93190).
    - It never checks the cell under the player (the cavern floor) or whether a collision mesh (VoxelCollisionMesh)
      exists yet.
    - So a cave arrival in air is released as soon as its own cell's data exists. If the floor's collision is not
      built by then, the player falls. This fits the 2026-10-18 log: −101 m/s right after the loading screen dropped,
      and no "pushed to the surface", which is what an arrival inside rock would get.
  - **The view follows the player and is not limited in height** [P]. The camera/character position goes to the
    VoxelFarm view unchanged (FUN_142cd6830 → FUN_143159850 → FUN_143f9ad40). The clipmap boxes (FUN_142b1ced0) are
    symmetric in y, from a fixed radius preset (table 0x14499d2c0; the runtime setter FUN_142d51820 is unreferenced).
    So depth below the ground does not move the player out of the finest ring.
  - **What decides whether a cell is generated** [P structure, I effect]:
    - VoxelContinentSurfaceCacheManager (FUN_143fe72f0) gives every level 2..10 cell one of four classes: below the
      column's surface (3), surface (1), above (2), or outside its y range (0). Its y range runs from the continent's
      lowest surface cell up. On 406 that is local ~720 m at the finest levels, so every cavern lies below it (class 0).
    - Each cave placement asset keeps its own grid of the same kind (lookup FUN_143fa63a0, combination FUN_143fe98f0).
    - Cave tiles carry only 4-5 LODs (Graveyard01 and most pieces 4, Cavern* 5; read from the shipped .cav headers).
      So above cell level 5-6, caves are not represented and only the terrain classes apply.
  - **Why ~1,600 m below the ground: not found yet.** The data so far: passes at 747 m and 1,419 m, falls at 1,768 m
    and 2,028 m, and at y −780 / −895 the client was still loading after 35 s. These fit a mechanism tied to cells of
    ~1-2 km (cell levels 8-9). One example: a coarse cell wholly below the surface, holding no cave data, that hides
    the cavern from the generation queue [I]. No code path doing that was found.
- **Searched:** FUN_143324910 and every callee above; the ClipmapView code (FUN_142ae6b20 view settled, FUN_142ae6c50
  view update, FUN_142b1ced0 clipmap boxes, FUN_142ae8610 contour generate); the camera feed FUN_142cd6830 →
  FUN_143f9ad40; the surface-cache and cave-classification managers (FUN_143fe72f0, FUN_143fe98f0, FUN_143fa63a0,
  FUN_143fa5f20, FUN_143fe9ad0, FUN_143fea570); the .cav headers and ContinentSurfaceCache406.csc (min/max surface per
  LOD); world-travel.md, gameplay.md "Underground Adventure", the cavefall commit 0041759.
- **Server:** unchanged. These already handle it: the depth filter (random trips skip graveyards more than 1,600 m
  below the ground and arrivals below y −650) and the fall guard `CavernArrivalRescue` (default on), docs/gameplay.md.
- **Runtime capture needed** (two Underground Adventure trips on 406 with a test copy: graveyard 24, 747 m deep, which
  stands, and graveyard 38, 2,028 m deep, which falls; dev command to force the target):
  1. Hook FUN_143324910: its return value each frame, and for the frame that returns 1 the player's cell id, sVar2 /
     sVar3 (the two material samples).
  2. In the same frames, call FUN_143fe98f0 (cave class) and FUN_143fa63a0 (terrain class, levels 2..10) for the
     player's cell and for the cell 1-2 voxels below the arrival (the floor). Also record FUN_142af5110 (generated?)
     for the floor cell.
  3. Hook FUN_142ae8610 (contour generate) and log the cell ids it receives near the arrival, with times, to see
     whether the floor cell is ever contoured and when.
  4. Save VoxelFarm.log and ClientSceneProcessing.log of both trips. The difference between the two trips is the
     answer.
- **OPEN-LEADS text:** "- **2026-10-26 (pe2-building, phaseE2-building.md L195):** FUN_143324910 releases physics as
  soon as the player's own cell has voxel data and the player's voxel is air. It never checks the floor cell or a
  collision mesh, and the view is not limited in height (FUN_143f9ad40, FUN_142b1ced0). So the fall is collision built
  too late (or never) for the floor. Why that depends on ~1,600 m below the ground is not found yet (cell
  classification FUN_143fe72f0 / FUN_143fe98f0; cave tiles have only 4-5 LODs). Runtime capture plan in the report;
  the depth filter and fall guard stay."

### L198. Allocator slots on many-core PCs beyond DirectThreadCount
- **Verdict:** CLOSED (best evidence). Without a client change nothing in the game files or on the server keeps
  16+-thread PCs inside 64 slots; the server behaviour chosen is the stream hold.
- **Answer:**
  - **Pool sizes** [P]: Decompression (FUN_143a8d560, call at 0x143a8d7eb) and TaskManagerJob (FUN_143a98090) pass
    FUN_142b4e800() = `GetSystemInfo().dwNumberOfProcessors` straight to FUN_143a95260. No config key, cvar or packet
    is involved:
    - The cvar registrations (1,205 FUN_142b23aa0 names) hold no thread / job / pool count.
    - The only file thread key is `[AssetDelivery] DirectThreadCount` (FileLoader), worth one slot per step.
  - **Affinity mask: ruled out** [P measured 2026-10-26, this 12-thread PC]. A test program calling GetSystemInfo:
    - plain: 12 processors;
    - `start /affinity 3`: still 12 (process affinity 0x3);
    - inside a job object with JOB_OBJECT_LIMIT_AFFINITY = 0x3: still 12.

    `GetLogicalProcessorInformation` (the 3-thread pools) also reported 6 cores / 12 logical processors in every run.
    So LaunchPad can't cap the pools by affinity or a job object.
  - **What does change the count:**
    - Windows' own processor count: msconfig "Number of processors" / `bcdedit /set numproc`, or `bcdedit /set
      groupsize` (GetSystemInfo reports the processors of the caller's group) [I, Windows documentation, needs a
      reboot, whole PC];
    - BIOS SMT / E-cores off [I];
    - the client patches already designed (allocator-slots-options.md P1/P3, allocator-slots-fix-design.md: LaunchPad
      in-memory CPU cap). The user decides on those; not applied.
  - **Does every pool thread take a slot?** No [P]. The worker loop FUN_143a95170 runs FUN_143a948d0 (queue lock plus
    an optional listener, no allocation) and then waits for its first job in FUN_143a95600. The 2026-10-21 slotmap
    agrees: 30 of 53 engine-pool workers held a slot, and the 23 without one had not run a job yet.
  - **Where the finished preview thread is released** [P]. FUN_143f93a50 creates a PreviewGenerationThread (stored at
    scene +0x55f8) only when none is stored. The scene update FUN_143f93c90 (from FUN_143f941a0) checks it every frame:
    once the thread has ended (FUN_142b55fc0), it joins it (FUN_142b26200), drops the reference and clears +0x55f8. The
    destructor (FUN_143f8ba00) and the unwind path (FUN_143f8b1d0) release it too. So every design preview runs on a
    new thread that must win a slot at its first allocation; on a full table every preview runs slot-less.
  - **MaxVoxelsForPreview is not read in 0.695** [P]. Its variable DAT_145295470 is referenced only by the registration
    LEA (FUN_14219be10 at 0x14219be83); a scan of every RIP-relative displacement in .text found no other. It is also a
    developer cvar hidden in retail (client-commands-for-slots.md §2.2). So the 0.695 preview has no voxel cap.
- **Searched:** both pool constructors, FUN_142b4e800, the cvar registrations, the worker loop, FUN_143f93a50 /
  FUN_143f93c90 / FUN_143f8ba00, every literal 0x55f8 / [0xabf] in the decompile, a RIP-displacement scan of .text for
  0x145295470, the measurement in allocator-slots-options.md, and the two test programs.
- **Server:** matches already. The chosen behaviour is the stream hold (`StreamHoldUntilLoaded` /
  `STREAM_HOLD_UNTIL_LOADED`, default on; docs/terrain.md "Slow loading screen", gameplay.md, docker.md). docs/terrain.md
  gained a "What else was ruled out" paragraph (d4c4ef2).
- **OPEN-LEADS text:** "- **CLOSED 2026-10-26 (pe2-building, phaseE2-building.md L198):** the two pools size
  themselves from GetSystemInfo (FUN_142b4e800). An affinity mask or a job object does not change that (measured: 12
  under affinity 0x3 and under a 0x3 job). No key or cvar sets it. Only Windows' processor count / groupsize, BIOS
  SMT / E-cores, or the designed client patch (user decision) help. Pool workers claim at their first job (30/53
  measured). Each design preview gets a new thread, joined and released by FUN_143f93c90 when done.
  MaxVoxelsForPreview is never read in 0.695. Server: the stream hold."

### L199. Paste area vs the client's preview
- **Verdict:** NEEDS RUNTIME CAPTURE for one remaining constant. Most of the lead is resolved, and a server fix is in
  a9f8d95.
- **Answer:**
  - **The cursor's numbers** [P, FUN_1436cd920, identical to 0.589 FUN_1436d8690; disassembly 0x1436cd955..0x1436cdd8c].
    With P the cursor voxel, E the copy size and W the cursor matrix (rows +0x270..):
    - C = round(E·W) and b = round(½ − ½·W), both as row vectors (b is 1 on flipped axes).
    - The preview is drawn at Q = P + round(−C.x/2 + b.x, (C.y < 0 ? −C.y : 0) + b.y, −C.z/2 + b.z) − (1, 0, 1), with
      grid point s at Q + s·W (FUN_1436cc520 → FUN_143144730 → FUN_143687a50 / FUN_143689660).
    - The request's area corner is A = P − (⌊|C.x|/2⌋ + 1, 0, ⌊|C.z|/2⌋ + 1). The horizontal mask (1, 0, 1, 0) is
      written to DAT_1455f07f0 by FUN_1428a2040; confirmed by disassembly.
    - FUN_143c1a8a0 sends that corner rounded after adding 0.75 voxel (dVar7 = vs·3.0·0.25; FUN_142ed0430 rounds with
      +0.5), so the request origin is A + (1, 1, 1).
  - **Server bug found and fixed** [P]. Every client function that applies the paste matrix uses rows as the images of
    the copy's axes (s·W):
    - FUN_1436cd920's extent;
    - the off-grid bounds FUN_14315feb0;
    - the client's own clipboard turn FUN_143b93690 → FUN_143c6e7a0 → FUN_143c6eb20 / FUN_143c6fb50 / FUN_143c6fc60;
    - the mirror composed as D·R (FUN_143b93040).

    The wire carries those rows unchanged (FUN_14315ae20 / FUN_14315bdb0). Two problems in the server:
    - It used W column-wise (M·s), so 90° / 270° turns (and turn + mirror) came out turned the other way: the content
      half-turned inside the preview's box.
    - It aligned the turned grid points on the origin. FUN_143c6eb20 aligns the cubes instead: grid point size + 1 − s
      and cube size − s on a flipped axis. So the server put flipped axes one voxel low.

    The 2026-10-11 check ("placed exactly where/how the preview showed") used a uniform 13×2×2 strip, which cannot show
    either problem.
  - **After the fix** (`Building.PasteBase`, Dot / Offset by columns, props the same way): every voxel of all 48 quarter
    turns and mirrors, at any size, lands at the modelled preview + (1, 1, 1). That is one constant for every turn, the
    unturned paste's own (test `Paste_TurnedLikeThePreview`).
  - **Still open:** whether the preview mesh pieces are drawn on the lattice (then the server is 1 voxel high and +1 on
    x and z for every paste, and the fix is to place at origin − 1) or one voxel up (then the paste is exact). The piece
    matrices (FUN_143683530 +0x10/+0x50 in FUN_143687a50) were not traced to their source.
    - Weak evidence for "exact": the 0.589 strip test saw no height error. With a lattice preview the old server would
      have been 1 voxel up there.
    - Also noted: FUN_1436d0a00 lowers the preview's y once by the clipboard's lowest solid row (clipboard +0x120,
      FUN_143b92dc0) through cursor offset +0x2b0. Whether P carries that offset into the request is new lead NL-2.
- **Searched:** FUN_1436cd920 (decompile and disassembly), FUN_1436d00b0 / FUN_1436cc520 / FUN_1436cf440 /
  FUN_1436d0580 / FUN_1436d0a00, FUN_143c1a8a0, FUN_142ed0430, FUN_142e9d500, FUN_143144730, FUN_143faa660, FUN_1431642e0,
  the packet writer FUN_14315ae20, FUN_143b93040, FUN_143b93690, FUN_143c6eb20, FUN_143c6fb50, FUN_143c6fc60,
  FUN_143c70640, FUN_142a7eeb0, FUN_14400c770, FUN_143f99120, FUN_143b92dc0, MODLOG 2026-10-10/11, BuildPlacementTests.
- **Server:** changed in local/pe2-building a9f8d95:
  - `Building.PasteBase`, `Dot` / `Offset` now by columns; flipped offset bytes become 255 − b (FUN_143c70640);
  - props: position p·W and the turn quaternion built from W as is;
  - tests: Paste_TurnedLikeThePreview; existing paste tests moved to the row-vector form; the off-grid test now feeds
    the same rows to both pastes;
  - docs/terrain.md "How a turned paste is placed".

  Unturned pastes are unchanged.
- **Runtime capture needed:** one in-game paste. Copy a single voxel (or an L of three) on flat ground, paste it
  unturned, and compare the result with the preview: same place, or one voxel up and one along +x / +z. Then do the
  same turned 90° with an asymmetric shape; it should now match the unturned result. Send the server log line "pasted …
  (client area …, copy …, first voxel at …)". If the result is one voxel off on all three axes, place at origin − 1 in
  `PasteBase`.
- **OPEN-LEADS text:** "- **2026-10-26 (pe2-building, phaseE2-building.md L199):** the cursor's margins and rounding
  are traced (FUN_1436cd920 + FUN_143c1a8a0: request origin = area corner + 1). The server used the paste matrix
  column-wise and aligned grid points instead of cubes, so 90° / 270° turns were half-turned and flipped axes one voxel
  low. It is fixed to the client's own turn (FUN_143c6eb20, a9f8d95), and every turn now lands at one offset from the
  preview. Left: one unturned in-game paste decides whether that offset is 0 or (1, 1, 1)."

### L200. Client calls the server could make for the slot problem
- **Verdict:** CLOSED (best evidence). No server-reachable client call fixes it, and none is sent; the server's part
  is the stream hold.
- **Answer:** the "try next" items of client-commands-for-slots.md §8:
  - **Who reads the claim-wide LOD flag** [P]. The view update FUN_142ae6c50 passes the claim box (+0x5e8, when +0x5e4
    is set) to FUN_142b1ced0, which only widens the finest clipmap ring (level 0, the `local_c8 < 1` branch) to the
    whole claim. Claim-wide LOD therefore means more full-detail meshing inside large claims, never more threads.
    `Claims.SetAllowClaimWideLOD(0)` would shrink that work but rewrites the player's saved option; not used.
  - **Render distance vs terrain cells** [P]. The clipmap radii come from a fixed preset table (0x14499d2c0: entries
    {4}, {6,6,6}, {12,12,12} cells for the first levels) chosen at scene creation (FUN_143f94580, memento +0x1d4). The
    only runtime setter FUN_142d51820 → FUN_143f9a6d0 is unreferenced (refscan: no call / LEA / pointer). So
    `Ui.SetRenderDistanceInPercent` and TerrainQuality (+0xf50, no reader) change nothing the contour threads do.
  - **Cached meshes** [P for the flag]. UseCachedMeshes (voxel farm +0x229) is written (FUN_144020850) but never read in
    the decompile; only +0x228 (CacheMeshes, writing) is read (part_0164 ~1187 / 1206). No effect on contouring.
  - **CEF / voice threads** [P measurement]. None held a slot in the 2026-10-21 slotmap: every slot owner started inside
    the exe; the 25 threads outside the exe and the 24 third-party pool workers had none. So don't open the in-game
    browser during loading (unchanged advice).
  - **GameSettings.SetMTR(0)** [P mechanism, I slot]. It stops the one MTR render worker (FUN_142e53460 → FUN_143a95410),
    so at most one slot is freed. 16+-thread PCs are short of more than that: about 2.5 slot users per extra logical
    processor (measured 54 at 12 threads), so about 14-20 short at 22-24 threads. It costs FPS, can't be undone safely
    (SetMTR(1) would start a slot-less thread), and changes client behaviour; not used.
  - Thread names of the MTR / PlayClient queues and a Ghidra pass over every FUN_142bfb8b0 caller are not needed for
    the verdict: no thread-count cvar exists among the 1,205 registered names, and retail hides developer cvars.
- **Searched:** FUN_142ae6c50, FUN_142b1ced0, FUN_143f94580, FUN_143f9a6d0, FUN_142d51820 (refscan), the radii table
  bytes, +0x229 / +0x228 readers, the cvar name list, the 2026-10-21 slotmap result, client-commands-for-slots.md.
- **Server:** matches already. Stream hold (default on). No ClientLua call is made for slots. docs/terrain.md updated
  (d4c4ef2).
- **Runtime capture (optional confirmation, not needed for the verdict):** `LMEMU_CLIENT=695 python re/tools/slotmap.py
  <pid> 150 5` on a client with a full table, before and after `GameSettings.SetMTR(0)` sent through ClientLua.RunCode.
  Expect one fewer owner of type FUN_143a95170, or a contour thread FUN_143f98880 taking the freed slot.
- **OPEN-LEADS text:** "- **CLOSED 2026-10-26 (pe2-building, phaseE2-building.md L200):** claim-wide LOD only widens
  the finest clipmap ring to the claim (FUN_142ae6c50 → FUN_142b1ced0). Render distance / TerrainQuality don't touch
  terrain cells: the clipmap radii are a fixed preset (0x14499d2c0) and its setter FUN_142d51820 is unreferenced.
  UseCachedMeshes has no reader. No CEF / voice thread held a slot (slotmap 10-21). SetMTR(0) frees at most one slot,
  too few for 16+ threads. No client call is used; the server's part is the stream hold."

---

## New leads

- **NL-1 (L195 follow-up): what makes the floor of a cavern far below the ground late or missing on the client.**
  - Need: which client step leaves the floor cell without collision when the arrival is more than ~1,600 m below the
    ground (classification, generation queue or contour).
  - Searched: as in L195 above.
  - Try next: the runtime capture in L195. Then the code path that turns the classes from FUN_143fe98f0 /
    FUN_143fa63a0 into generation jobs (the callers of FUN_143fea1d0: FUN_143f90130, FUN_143f95700, FUN_143ff9460,
    FUN_143ffcaf0…).
- **NL-2: the paste cursor's offset +0x2b0 and the request.**
  - Need: whether the cursor voxel P (+0x98 / +0xa0) that FUN_1436cd920 starts from includes the cursor offset +0x2b0:
    the Translate nudges (FUN_1436d11d0) and the one-time drop by the clipboard's lowest solid row (FUN_1436d0a00, from
    FUN_143b92dc0 → clipboard +0x120). If it doesn't, a copy with empty bottom rows (or a nudged paste) lands above or
    beside its preview by that many voxels.
  - Searched: FUN_1436d0a00, FUN_1436d11d0, FUN_1436d0580, the cursor functions 0x1436c0000-0x1436dffff for writes of
    +0x98 / +0xa0 (only FUN_1436d6f50 and two resets; the per-frame raycast that sets P is in a base cursor class not
    found yet).
  - Try next: the base class of VoxelPasteCursor (RTTI) and its vtable +0x120 / +0x2b8 functions, or the same in-game
    paste as L199 with a copy whose bottom row is empty.
- **NL-3 (testing): turned pastes and designs after a9f8d95.**
  - Need: an in-game check that a 90° / 270° turned asymmetric paste (and a placed design turned 90°) lands turned the
    way the preview showed.
  - Searched: none (new code).
  - Try next: the TESTING-NEEDED entry "Placement vs preview", which the coordinator should extend with "asymmetric
    shape turned 90°". This file could not be edited here (rule 5).

## Runtime captures needed

1. **L195:** two Underground Adventure trips on 406 (graveyard 24 stands, graveyard 38 falls) with hooks on
   FUN_143324910 (return value, cell, sVar2 / sVar3), class lookups FUN_143fe98f0 / FUN_143fa63a0 for the player's and
   the floor cell, FUN_142af5110 for the floor cell, and FUN_142ae8610 (contour generate, cell ids and times), plus
   VoxelFarm.log and ClientSceneProcessing.log.
2. **L199:** one unturned single-voxel / L-shape paste, compared with the preview and the server's "pasted …" log line;
   then the same turned 90°.
3. **L200 (optional):** slotmap.py before and after `GameSettings.SetMTR(0)` on a full table.

## Summary

- **L193 is closed.** The 0.695 client never tells the server which bar is shown, so bar 0 first is the only rule a
  server can apply. It is also the client routine's own result for anyone who never switched bars. A refused equip
  cannot occur. Definition slots now count only for non-stacking items, as the client's bar builder does. The "tool 7"
  log line is the client's ToolLocation reset when the held item changes.
- **L199 found a real server bug.** The paste matrix was applied column-wise (the client uses rows), and turned copies
  were aligned on grid points instead of cubes. 90° / 270° pastes therefore came out half-turned, and flipped axes one
  voxel low. Both are fixed to the client's own turned-clipboard code. Every turn now sits at one fixed offset from the
  modelled preview; one in-game paste shows whether that offset is zero.
- **L198 and L200 are closed on best evidence.**
  - An affinity mask or a job object does not lower the CPU count the pools read (measured).
  - No config key, cvar or packet resizes the pools.
  - Render distance, TerrainQuality and UseCachedMeshes don't affect terrain work. Claim-wide LOD only widens the
    finest ring. SetMTR(0) frees at most one slot.
  - Each design preview gets a new thread that is released when it ends. MaxVoxelsForPreview is dead in 0.695.
  - The stream hold remains the server's behaviour; client patches stay the user's decision.
- **L195 needs a runtime capture.** The physics hold is proven to release on the player's own voxel data, never on the
  floor or a collision mesh. The depth dependence is narrowed to the generation / contour stage. The exact hooks to
  capture are listed, and the depth filter and fall guard stay in place.
