# Phase E open leads L135, L141, L4, L145, L146 — research agent report (2026-10-17)

Read-only research on the 0.695 client (re/decomp_all_695, <0.695 test copy>\Landmark64.exe read as bytes), the 0.695
tables (gameData695), the shipped scripts / AS3, the patch notes, the forum and wiki dumps and the server code.
Proof marks: **[P]** proven from client code or data (function / table row named), **[I]** inferred (reason given),
**[?]** still open (what to try next). Function addresses are 0.695 unless marked 0.589.

Scratch helpers used (scratchpad, not project files): `qw.py` (read qwords / strings / floats at a VA of the exe, .data
raw-size aware only for .text/.rdata), `ripscan.py` (every RIP-relative reference to a global in .text).

---

## 1. L135 — how a Magical Platform moved (TASKS E.1)

### Status
**Narrowed a lot; the wire path for moving the prop is found, the rider mechanics are not proven.** The platform was
moved by the server (types 27/28 are data-only in the client), the client accepts server movement of a placed prop
through the ordinary **PlayerUpdate UpdatePositionZoneToClient 0x7F** by transient id and has prop-specific code for it,
the speed / pause settings and their units are decoded, and the platform-relative position packet (0xB2) is proven
absent. Whether riders are carried smoothly by that path needs one runtime test.

### Evidence
1. **Types 27 / 28 are server-side data classes** [P]. RTTI has `AreaPropertyDataMovingObject` (vtable 0x14496bd18,
   type getter FUN_143db3560 → **0x1C = 28**) and `AreaPropertyDataMovingObjectWaypoint` (0x14496bcb8, FUN_143db3570 →
   **0x1B = 27**), but no `ClientArea…` class for either (the client factory FUN_1431ae4f0 list in movers-695.md §3).
   So the client loads the data and never runs a moving object — the server did.
2. **MovingObject settings** [P] (FUN_143db4060, the setting-apply slot of AreaPropertyDataMovingObject):
   - key `0x321630f0` → `+0x10 = value × 0.01` (float constant DAT_144c4103c = 0.01);
   - key `0x30725695` → `+0x0C = value` (int);
   - validity FUN_143db3bd0: `+0x10 ≥ 0 && +0x0C ≥ 0`.
   The keys are the **one-at-a-time hash** (re/tools/strhash.py `oaat`) of the EditableProperties names:
   `oaat("moving_platform_speed") = 0x321630f0`, `oaat("moving_platform_pause_delay") = 0x30725695` [P by computation].
   EditableProperties.txt: speed default **160**, min 50, max 1000, step 5; pause default **2**, min 0, max 30 →
   **speed 1.6 m/s (0.5-10 m/s), pause 2 s (0-30 s)** [P for the values; I that the units are m/s and seconds —
   ×0.01 of a "cm/s" editor value, and the pause is an int].
   **Bonus — L134 answered:** `oaat("enable_teleportation") = 0xa632764b`, exactly the key movers-695.md §3 could not
   explain; every editable-property key in the spawn's +0x340 list is the oaat hash of its EditableProperties STRING [P].
3. **The AreaProperties rows** (gameData695/AreaProperties.txt) [P]: id 48 = area 14 (the platform 1193), type 28,
   INT_PARAM1 1, FLOAT_PARAM1 **4**, editables `moving_platform_speed`, `moving_platform_pause_delay`, `code_test`;
   id 45 = area 15 (each dock 1192), type 27, FLOAT_PARAM1 **−1**, same two editables; id 50 = area 15 type 29
   (MovingObjectParamEditProxy, the dock's setup UI); id 44 = area 14 type 26 SlidingFriction (1, 1, 1).
   The waypoint data class copies three row fields (+0x50, +0x54, +0x58 of the row struct, FUN_143db2e90) [P]; which
   columns those are is [?] (the −1 is probably "waypoint order: auto" [I]).
4. **SlidingFriction is a physics material, not a carrier** [P]: ClientAreaSlidingFriction apply FUN_142fe1d90 looks up
   the prop by its guid (+0x70) and writes the three floats (+0x78, +0x80) into every collision shape of the prop's
   model (shape +0x2CC / +0x2D0 / +0x2D4). That is the 25 Sep 2014 fix "You should no longer slide off the Magical
   Platform when it's placed at a steep angle" (P1757).
5. **No platform-relative position system in 0.695** [P]: the registry names `cPacketIdPlayerUpdatePositionOnPlatform`
   = opcode **0xB2** (part_0107 ~8542), but there is no class, no builder (no `= 0xb2` header write), and the zone
   dispatcher FUN_142d1ccf0 sends 0xB2 to its default case (byte table DAT_142d20b44[0xB1] = 0x93 →
   FUN_143cd6f00 → module registry +0x78, all `return 0` except ClientContentManager, which only takes 0x118).
6. **Rails are not it** [P]: Rails.txt / RailPoints.txt are empty in 0.695 (header only), so MoveOnRail /
   MoveOnRelativeRail (PlayerUpdate 0x2A / 0x2C) have nothing to run.
7. **Server movement of a placed prop is supported** [P]: the 0x7F handler PlayClient vfunc +0x208 = FUN_142ce6340
   finds the entity by **transient id** (FUN_142f85340), applies the movement state (vfunc +0xD0), and then calls vfunc
   **+0x108** (`AsProxiedProp`: FUN_1430a8d90 `return this` in ProxiedProp, FUN_142e9dd30 `return 0` in
   ProxiedCharacter) and sets **ProxiedProp +0x4F00 = 1** ("moved"). Placed props are ProxiedProp entities
   (SpawnProxiedProp FUN_142f8a800 → ctor FUN_1430a7760) carrying the transient id of the lightweight prop spawn.
   The ProxiedProp update FUN_1430ab000 then re-registers the prop's collision (FUN_1430ac390 → FUN_1430a82a0 or a
   rebuild) when +0x4F00 is set **and** the last refresh (+0x4F08) is none or more than **1000 ms** ago, clearing the
   flag and stamping the time. So: the model is interpolated smoothly like a creature, **its collision follows at most
   once per second** [P].
8. By contrast the SimpleAnimation behaviour (UpdatePropInteractionState 0xFD type 0, Game/PropAnimations.cs)
   drives the interpolator ProxiedProp +0x4D70 every frame and re-registers the collision every frame
   (FUN_1430ab000 → param_1[0x30] vfunc +0x18 then FUN_1430a82a0) [P] — but only for the 21 fixed sets of
   SimpleAnimationSets.txt (set 8 "Sliding Platform (4z)" is a fixed 4 m slide, not the dock-to-dock mover) [P].
9. Design [P, patch notes]: P1537 "You can reposition the two end pieces to make the platform move in any manner …
   you can even rotate the ends in any axis, which in turn rotates the pad as it moves!"; P1709 (16 Sep 2014) players
   falling off fixed; P1898 (14 Oct 2014) "a gap between the Moving Platform and their docks" fixed; P2668 (26 Jan 2015)
   "Linking and Triggering now works correctly with the Moving Platform"; P6098 (13 Jul 2016) the platform can no
   longer be resized "as this was causing it to not function".
10. Not the "managed" system [P for what it does]: PlayerUpdate 0x4D ManagedObject {u64 object, u64 old manager,
    u64 new manager} (handler FUN_142cdd450) hands simulation of an object to a client (FUN_142c98c00 when the new
    manager is the local player) and ClientUpdate 0x24 UpdateManagedLocation {u64 entity, vec4 pos, vec4 dir, bool,
    u8} snaps an entity's controller (case 0x23 of the ClientUpdate handler). Neither names a platform; using them for
    riders would be invention [I].

### Server change (exact)
New partial `Game/Props.Platforms.cs` (class `Props`, next to Props.Movers.cs):
- `sealed class MovingPlatform { ulong Group; PlacedProp Platform, DockA, DockB; float Speed; int PauseSeconds;
  long StartMs; }`, built in `BindMover` when a pack 5 (Magical, platform 1193 + docks 1192), pack 8 (Glass, 1421 +
  1422) or pack 6 (Three-dock, 1288 — parts per PlacementPackProps) is expanded or loaded.
- Constants: `MovingPlatformSpeedKey = 0x321630f0`, `MovingPlatformPauseKey = 0x30725695` (oaat hashes);
  `Speed = (setting ?? 160) * 0.01f` m/s, `PauseSeconds = setting ?? 2`.
- Motion as a pure function of time (no per-tick state, nothing to send while nobody is near): leg length
  `L = |B − A|`, leg time `T = L / Speed`; cycle `A → pause → B → pause → A`; position = lerp, rotation =
  `Quaternion.Slerp(rotA, rotB, t)` (P1537). End points: the dock positions offset towards each other so the platform
  stops flush (P1898) — offset = half the platform length + half the dock depth along the leg [I; tune in game].
  With three docks, visit them in PROP_INDEX order and back [I].
- Wire: `PlayerUpdatePosition` 0x7F `{2-bit-length transient id of the platform, type-0 movement body, 4 × f32 1}`
  exactly as creatures (`Creatures.MovementBody` / `MovementFanout.Queue`), with two changes: an overload
  `MovementBody(Vector3 pos, Quaternion rot, Vector3 velocity)` writing the full quaternion (x, y, z, w at precision 5)
  instead of a yaw, and velocity = the leg direction × Speed while moving, 0 while paused (state 0x441).
  Send at each leg start / stop and every 500 ms while moving, only to players within the prop view range
  (bandwidth rule) [I for the rate].
- Keep `Platform.Spawn.Position / Rotation` in memory at the current point so a late viewer's AddLightweightProp
  spawns it where it is; persist only the placed position.
- Links / triggers (P2668): StoryTools "Platform" kind already toggles on / off — when off, freeze the clock offset.
- Send the setting list (+0x340) with the oaat keys once settings land (this closes L134 too).

### Remaining unknowns
- [?] Whether a player standing on the platform rides it: the collision refresh is ≤ 1 Hz on the 0x7F path (point 7).
  Test: place a Magical Platform, stand on it, run the existing dev probe
  `propslide <guid> dx dy dz <seconds>` (DevCommands.cs) for a slow 4 m slide, watch whether the player stays on, and
  read ProxiedProp +0x4F00 / +0x4F08 with Frida during it (test copy only). If riders fall, trace next: the
  ProxiedProp branch of the ProxiedCharacter update FUN_142eb1950 (~line 622, `vfunc +0x108 != 0 && +0xF4`), which
  drives param_1[0x30] (+0x180, the controller with set-position +0x28 / velocity +0x40 / +0x50) per frame.
- [?] The three waypoint row fields (+0x50/+0x54/+0x58): map the AreaProperties row struct offsets in the area-data
  loader (GlobalAreas / AreaProperties reader) to INT/FLOAT_PARAM columns.
- [?] Exact stop offsets at the docks (model sizes of 1192 / 1193 / 1421 / 1422).

---

## 2. L141 — when live made loot pinatas ownerless (TASKS E.4)

### Status
**Client mechanism fully proven; the live trigger rule is not in the client (server-side) and no design source states
it.** The server's current claim answer is wrong for the client (see change 1). A live-faithful lifetime constant was
found (DecayAge 120 s, unread by the client).

### Evidence
1. **The client only shows pinatas that are yours or ownerless** [P]: LootPinataManager::HandlePacket FUN_143125240,
   sub 1 SpawnLoot: the pinata is created only if `owner == local player guid || owner == DAT_145453608` (the "none"
   guid). DAT_145453608 is in .bss and has **no writer** in .text (ripscan: 21 references, all `mov rax,[rip+…]`
   reads; no store, no LEA) → it is 0 at runtime [P with that method]; the server's owner 0 is right.
   Consequence: a pinata owned by someone else is never shown to you; **shared loot can only be ownerless loot**.
2. **Per-pinata update FUN_143127a10** [P] (constants from Resources/StringHashToValue.txt, hashed with oaat):
   - owner set and ≠ me and the owner entity not known to this client → state 4 (decay) and destroy flag (+0x1E4);
   - owner = me or none and the local player is busy (vfunc +0xB0 of player +0x250) → wait;
   - owner none and within **LootPinata.DistanceToPickupOwnerlessLoot = 5.0 m** (hash 0x1d59bdcd) → state 2 and
     **RequestClaimOwnerlessLoot 0xFB/7 {u32 loot id, u64 none}** — sent on every update while in range and unclaimed;
   - otherwise (owner set, or out of claim range): FUN_143124200 (PickupRadiusAuto 20 / Manual 30, hashes 0xa6687917 /
     0xb07a1b2e) → state 3, scoop towards **the owner entity** at ScoopStartSpeed 0.05 (0x17ccb9b2) + ScoopAcceleration;
     each step checks the **local** player's bag / resource room (FUN_143124180: containers 0 and 5), else back to
     state 1; on arrival: if owner = me → **PickupLoot 0xFB/5 {u32 id, u64 me}** and state 5; if owner ≠ me → just
     the destroy flag (no packet).
3. **NotifyOwnerlessLootClaimed 0xFB/4 {u32 id, u64 new owner}** (reader FUN_143121c60) [P]: sets pinata +0x200 and
   +0x70 to the new owner and state 3 → every viewer's copy flies to the claimer; the claimer's client then sends
   PickupLoot. **PickupLootResponse 0xFB/3 {u32 id, u64, bool}** (FUN_143121d20) and **RemoveLoot 0xFB/2**
   (FUN_143121e00) both just remove it (FUN_143126f70) — the bool is not read [P].
4. States (FUN_143126d90) [P]: 0 dropping, 1 on the ground, 2 claim requested, 3 scooping, 4 decaying, 5 collected;
   each plays an effect from the definition (+0x0C / +0x10 / +0x14 / +0x18).
5. **LootPinata.DecayAge = 120** (StringHashToValue.txt line 374): its oaat hash 0x65741807 appears nowhere in the 0.695
   decompile → read only by the server [P unread; I that it is the seconds a pinata lies before the server removes it].
   Server today: BagLinger 60 s, ChunkLinger 10 s.
6. Registry also names **LootPinataPacketIDRequestByItem 0xFB/6** (part_0107 ~10970), no client class [P] — a
   server-internal or unused request (bags from items, e.g. the sifter bags of P0043) [I].
7. Design sources (searched: patch notes "loot", forum all/ and wiki dump for loot bag / pinata / shared / group /
   party / kill credit / free for all): chests give **each** nearby player their own loot (P1089, P1647, wiki Chests
   "anyone within 50m"); reward chests give the whole group (P6503); "You should not receive loot if an ally or neutral
   creature kills an enemy creature" (P4719, 26 Apr 2016 — a player reply in that thread reads it as damage-based
   credit; not a dev statement); "Fiery Fungus now drops tree loot on death … (it continues to also drop destroyed
   terrain loot if it explodes)" (P4533). **No source states when loot becomes ownerless.**

### Server change (exact) — `Game/LootPinatas.cs`
1. Claim flow as the client expects (replaces the current `ClaimOwnerless`):
   - make `Pending.Owner` replaceable (`pending[id] = loot with { Owner = session }`);
   - first sub-7 request for an ownerless pinata from one of its viewers (optionally checking the claimer is within
     5 m + slack of the spawn point): set Owner = claimer and send **sub 4 `{u8 4, u32 id, u64 claimer guid}`** to every
     viewer (claimer included). Do not give the contents yet;
   - later sub-7 requests for that id: **no reply** (any sub 3 / sub 2 would remove it on that client before the
     sub 4 arrives — harmless, but the client will animate correctly only without it);
   - the claimer's PickupLoot (sub 5) then goes through the existing owner branch of `Handle` (contents + sub 3);
   - viewers whose copy flew to the claimer destroy it themselves; send RemoveLoot to non-claimers only as a cleanup
     after a few seconds [I].
2. `BagLinger = 120 s` (DecayAge) [I]; keep chunks short.
3. The trigger (call `SpawnOwnerless`) — proposed rule, every part [I], to confirm with the user:
   a. loot with no entitled player: drops caused by the world, not a player — the Fiery Fungus explosion's destroyed
      terrain loot (P4533; Creatures.cs:1727 today gives it to the trigger);
   b. a personal bag whose owner leaves (disconnect / zone change) before collecting it: re-spawn it ownerless to the
      players still in range (no client path forbids it; the owner's own client is gone) — keeps loot from vanishing;
   c. never for chest / reward-chest / creature loot that has a credited player (those stay personal, P1647 / P6503).

### Remaining unknowns
- [?] The live rule itself. Next paths: dev posts by name (Domino, Dexella, Terry) with "loot" in design/forum/all
  (full-text, not only update threads); EQNext "loot pinata" design talks in the archived EQN forum, if scraped; a
  live packet capture (none known). Keep L141 open with the proposal above as the interim rule.

---

## 3. L4 — replies for the admin-monitor requests 0x4A9, 0x4FC, 0x4CC

### Status
**Answered for the retail client: none of the three replies has a reader, and none of the three requests can be
sent by the retail client.** Partial live layouts recovered from the consumers that do exist.

### Evidence
- **Requests (wire)** [P]:
  - 0x4CC AdminCommandPacketZonePlayerCount: `u16 0x1E, u16 0x4CC, u32 0` (builder in FUN_1432262a0, sender
    FUN_14321ccd0 writes the header then the u32 at +0x20; "ZoneCommand" is the in-object handler-group string, not on
    the wire).
  - 0x4A9 PathingDataMonitorUpdateRequest: header only (FUN_14321c770 → FUN_143204600), every 0.2 s.
  - 0x4FC StorybricksRequestDiagnostics: `u16 0x1E, u16 0x4FC, u64 admin target guid, u8 flag` (FUN_1432040a0:
    FUN_142c02460 +0x20, byte +0x28); flag 0 from page 30/31 (FUN_14321c900, admin client only), flag 1 from
    ClientStorybricksDebugManager update FUN_14352d160 every 5 s (0x1389 ms) while its +0x40 is on.
- **Reachability** [P]: FUN_1432262a0 is called only from the monitor draw FUN_14321e590 (part_0092 lines 2353 /
  2386) after `if (+0x342A8 == 0) goto end` — the paged view that nothing in the retail exe turns on (L3,
  FUN_142d56520 unreferenced). The Storybricks manager's +0x40 is set only through the sb_* command bodies
  FUN_14352d7c0 / FUN_14352dad0 / FUN_14352dd70, which refscan finds **unreferenced**.
- **Consumers / fields** [P]:
  - ZonePlayerCount reply → would fill monitor **+0x34288** ("Fps: %i, Zone Pop: %i" on page 1, "Pop:%4d" before
    "PC:%3d NPC:%3d AINPC:%3d" on page ≥ 2). Nothing in the exe writes +0x34288 except the monitor constructor (= 0);
    PC / NPC / AINPC (+0x3428C / +0x34298 / +0x3429C, +0x34290 / +0x34294) are counted locally by FUN_1432262a0. The
    Command dispatcher passes unknown admin subs to the module registry (+0x70, all `return 0`) and unknown opcodes to
    +0x78 (all `return 0` but ClientContentManager, opcode 0x118 only). → **no reader**; implied body `u32 count`
    [I: the field is read as an int].
  - PathingDataMonitorUpdateResponse 0x4AA: no class, no reader (architecture-leads.md L4); the page-32 fields are
    known (admin-pages.md, pathing.md) and give the implied content [I].
  - Storybricks diagnostics reply: its consumer exists but is unreferenced: **FUN_14352d770** (no reference at all)
    → FUN_14352f060 copies a reply struct into the "DebugData_AI_SelectedCharacterInfo" data source and fires
    "UpdateAICharacterInfo": reply +0x80 u64 character guid, +0x88 string (name), +0xA0/+0xA4 health cur/max (i32),
    +0xA8/+0xAC energy, +0xB0/+0xB4 armor, +0xB8/+0xBC f32 speed cur/max, +0xC0 intelligence, +0xC4 perception,
    +0xC8 alert range, +0xCC attack range, +0xD0 leash range (f32), +0xD8..+0xE0 position, +0x100 bool interruptible,
    +0x110 string mode, +0x128 string status, +0x140 string pathing [P for the struct offsets; I that the wire order
    follows them].

### Server change
None needed for the retail client (no request can arrive, no reply would be read). For completeness of the verdicts:
log-only handlers for 0x1E/0x4CC, 0x4A9 and 0x4FC in AdminCommands (gated by Admin.IsAdmin), each answered with
nothing. The /admin/monitor web page already shows the zone population and pathing data from the server side.

### Remaining unknowns
- [?] Live wire layouts of 0x4AA / 0x4AC / the ZonePlayerCount and Storybricks replies: only another client build
  (internal / admin) or a live capture can show them.

---

## 4. L145 — the pointer table at 0x1447e9698 (DebugPathQueryRequest 0x1E/0x4A7)

### Status
**Answered: it is a vtable slot of `VoxelPathFindVisualizeCursor`, the cursor of the build tool "PathFindVisualize";
the tool is registered in retail but no shipped UI or script can select it.**

### Evidence
- 0x1447e9698 = slot 83 (+0x298) of the vtable at **0x1447e9400 = VoxelPathFindVisualizeCursor** (re/out/rtti_695.tsv;
  the next RTTI vtable, 0x1447e96f0, is the packet class itself) [P].
- FUN_14379b600 is that cursor's click handler [P]: state +0x220: first click stores the start (+0x230 ← cursor point
  +0x1B0), second click stores the end (+0x240) and sends **0x1E/0x4A7 {vec4 start, vec4 end, u8 flag}** (writer
  FUN_14379adb0: 4 × f32 +0x20, 4 × f32 +0x30, byte +0x40); flag = cursor +0x268, toggled by FUN_14379b790.
- Constructor FUN_14379b0c0 is called only by the brush-cursor factory **FUN_1436e3a70 case 9** [P, refscan], which the
  tool-manager init **FUN_143462fa0** calls as `register("PathFindVisualize", FUN_1436e3a70(9), 9)` next to Selection,
  Smoothing, HealingBrush … [P]. deadcode.py: reachable ("live pointer").
- ToolTypes.txt row **15 "PathFindVisualize" — "Path Find Tool"**: NAME_ID 0, IMAGE_SET 0, SHOW_MODAL 0,
  **SHOW_IN_PALETTE 0**, **KEYBINDING_ID 0**; no ModalToolBrushDefinitions row with BRUSH_TYPE 9 [P].
- Tools are activated by name through FUN_143464780. Its static callers pass only "Props", "TriggerVolume",
  "StampBrush", "PasteVoxel", "ClaimPlacement", "PropTint", "Location" and "" (DAT_144c40a60) [P]; the two dynamic
  callers are the Lua natives Tool.ActivateTool (FUN_143606470) and Voxel.ActivateTool (FUN_1435fd940) [P]. Their
  callers: hudstate.lua ("Selection" / "None") and the AS3 ToolsWindow / BuildModeWidget with `itemData.ToolName` from
  the palette entries (FUN_1437ae3b0 writes "ToolName") [P]; admin.lua does not call them [P].
- So retail reaches the tool only if a palette entry named it; the palette is built from ToolTypes rows with
  SHOW_IN_PALETTE (read into +0x61 by the ToolTypes row loader FUN_143cbcf70, part_0147 ~17600) [P for the read; I that the palette builder
  filters on it — the filtering function was not located].

### Server change
Handle 0x1E/0x4A7 in AdminCommands (Admin.IsAdmin only): `{4 × f32 start, 4 × f32 end, u8 flag}` → run
`Pathing` between the two points and report the result as a chat line (path length, node count, failure reason) [I].
Do not send 0x4A8 DebugPathQueryResponse (registry name only, no reader) [P]. Verdict: reachable only with a modified
palette / Lua; mark "unreachable on retail (no palette entry, no keybinding, no script)".

### Remaining unknowns
- [?] Confirm the palette filter: find the function that builds the tool palette list (callers of FUN_1437ae3b0's
  owner class constructor, or readers of tool-type +0x61), then the verdict becomes [P].

---

## 5. L146 — did a Landmark claim hold items?

### Status
**Yes — a claim had an item inventory: the items of the props placed on it.** Proven by the client's message
classes and handlers plus four patch notes; the emulator already has the data (PlacedProp.PaidByItemGuid).

### Evidence
- **Client side (0.695)** [P]:
  - Claims 0xF9 registry (part_0107 ~8740-8840): **0x0D SendInventoryDetails**, **0x23 ClaimRequestMoveItemToPlayer**,
    **0x47 ClaimMoveItemToPlayerOnZone**, **0x0E ClaimConfirmMoveItemToPlayer**, **0x36 / 0x37
    InventoryListDebugRequest / Response**, **0x60 DebugAddItemClaim** (0.589 used 0x5E). 0x47 and 0x0E have no client
    class → claim-server ↔ zone messages of the item move [I from the names].
  - The Claims dispatcher FUN_142dbecb0 (switch on sub − 1) handles **sub 0x0D** (case 0xC): reader FUN_142da0d70
    `{u16, u8 sub, u64 claim guid (+0x18), u64 (+0x20), list}` with List<ClaimsPacketIdSendInventoryDetails::
    ClaimItemDetails> (FUN_142da3c70 → element FUN_142d9fe40: `u64 item guid, u32, u32, u32, u32, u32, bool, u32,
    u64, u32, u32`) and prints "Claims Inventory for claim: %llu" + "\tItemGuid: %llu" per item to the console.
  - It handles **sub 0x37** (case 0x36): list of full client items, printed as `/claim inventory <%llu>` and
    `[%d] <itemGuid:%llu> <itemDefId:%d> <propGuid:%llu> <ownerGuid:%llu> <stackCount:%d> <slot:%d> <invIndex:%d>
    <ownerType:%d>` — claim-inventory items carry the **prop guid** they are placed as and an **owner type** (claim vs
    player) [P for the text].
  - /item usage text (part_0101 33799): "/item listClaimInventory <claim guid> Show the items listed in a claims
    inventory", "/item claimItemToInventory <item guid> <claim guid> Requests a transfer of an item in a claim's
    inventory to the player's inventory", "/item claimAddItem <item definition id> <quantity> <tint id> <claim guid>
    [targetGuid]" [P]. (listClaimInventory has no branch left in 0.695 — usage text only [P].)
  - **0x23 is client → server** (the 0.589 family doc lists it as S2C): claimItemToInventory builds it in FUN_1433acb90
    and sends with FUN_14338c930: `u16 0xF9, u8 0x23, u64 claim guid (+0x18, arg 2), u64 target player (+0x20: arg 3,
    else admin target, else self), u64 item guid (+0x28, arg 1)` [P]. Its chat echo "Move and item with guid %llu to
    player inventory from claim Guid %llu" prints the claim and the player — the client's own mislabel [P].
- **Design** [P, patch notes]: P0037 (4 Feb 2014) props that disappeared from claims are refunded from "any items the
  game has data about"; P0079 (7 Feb 2014) "You can no longer delete a claim unless you have room in your inventory for
  all the items on it and the claim flag"; P0862 (1 May 2014) "If you had lost items from your claim previously … they
  will also be restored to your Overflow Inventory"; P0165 chests store items on a claim.

### Server change (exact)
- **Model**: a claim's inventory = every PlacedProp on it with `PaidByItemGuid ≠ 0` (item guid, prop def item, count 1,
  prop guid = Spawn.Guid, owner = the placer) plus a small per-claim list of **unplaced** items added by
  `claimAddItem` (prop guid 0), persisted with the claim. No new table for placed props.
- **0xF9/0x60 DebugAddItemClaim** `{u32 item, u32 tint, u32 count, u64 target, u64 claim, u32, u32, u8}`
  (Admin.IsAdmin): add `count` of `item` to that claim's unplaced list; chat confirmation; reply with
  SendInventoryDetails (below).
- **0xF9/0x23 ClaimRequestMoveItemToPlayer** `{u64 claim, u64 player, u64 item guid}` (Admin.IsAdmin): if the item
  is an unplaced claim item → give it to the player (inventory, else overflow as P0862) and drop it from the list; if it
  is a placed prop's PaidByItemGuid → pick the prop up for that player exactly as a normal pick-up with
  `Refund.AsPlaced` (pack movers take the whole pack). Reply with SendInventoryDetails.
- **0xF9/0x0D SendInventoryDetails** (S→C, admin only): `u8 0x0D, u64 claim guid, u64 0, u32 n, n ×
  {u64 item guid, u32 item def, u32 tint, u32 count, u32 slot, u32 0, bool placed, u32 0, u64 prop guid, u32 0, u32 0}`
  — the element field meanings past the guid are [I] (the client prints only the guid), so keep the unknown fields 0.
- Claim deletion (DeleteClaimItem 0x2F) should return the claim inventory's items (placed props refund as today, plus
  the unplaced list) — P0079.

### Remaining unknowns
- [?] Element field meanings of ClaimItemDetails beyond the guid; the 0x37 response's request 0x36 has no client
  sender (live sent it from a server command) — the 0x37 path is reachable if the server sends it (handler is live),
  so the full-item form `/claim inventory` could be used instead: next, decode FUN_143404230 / FUN_142c208e0 (the client
  item reader used per element) to send a proper 0x37.

---

## Summary

| Lead | Result | Server work |
|---|---|---|
| L135 movers | Server moved the platform (types 27/28 data-only [P]); the client accepts 0x7F UpdatePosition for a placed prop and marks it moved, refreshing its collision at ≤ 1 Hz [P]; 0xB2 PositionOnPlatform absent [P]; speed = setting × 0.01 (160 → 1.6 m/s), pause int s (2) [P]; keys are oaat hashes — **L134 solved** (0xa632764b = oaat("enable_teleportation")) | Props.Platforms.cs: time-based dock-to-dock motion, 0x7F with quaternion movement bodies; runtime rider test with `propslide` first |
| L141 ownerless loot | Client flow proven: only own or ownerless pinatas are shown; claim at 5 m → server must answer NotifyOwnerlessLootClaimed 0xFB/4 to all viewers, then the claimer's PickupLoot; DecayAge 120 s is server-only. Live trigger rule not found | Fix the claim answer (sub 4, no reply to losers); BagLinger 120 s; interim rule (world-caused drops, abandoned bags) for the user to confirm |
| L4 monitor replies | No reader for ZonePlayerCount / Pathing / Storybricks replies and no retail sender of the requests [P]; request bodies decoded; Storybricks reply struct recovered from an unreferenced consumer | Log-only handlers; nothing to send |
| L145 0x1447e9698 | Slot 83 of VoxelPathFindVisualizeCursor; tool "PathFindVisualize" (ToolTypes 15) registered but not in the palette, no keybinding, no script [P/I] | Admin-only handler that runs Pathing and reports in chat; no 0x4A8 |
| L146 claim items | Claims had an inventory of their placed props' items (+ admin-added items) [P client classes + patch notes] | Claim inventory from PaidByItemGuid; handle 0x60 / 0x23; send 0x0D; return items on claim deletion |
