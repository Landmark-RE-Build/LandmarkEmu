# Why the Translocation Pad and the Flinger never fire (round 91) — 0.695 trace

2026-10-18, read-only. Decompile `re/decomp_all_695`, exe `<0.695 test copy>\Landmark64.exe`, data `gameData695`.
Proof marks: [P] read in the client / data, [I] inferred, [?] open.

## 0. Verdict

**Every prop the server spawns is spawned *disabled*, and both movers refuse to fire for a disabled prop.** [P]

- The last byte of the AddLightweightProp packet (reader FUN_142c273d0, object +0x490) is the prop's **enabled** flag.
  The handler FUN_142cde1c0 copies it with `FUN_1430abea0(prop, packet[+0x490], 0)` into **ProxiedProp +0x4e9c**.
- The client defaults are 1 in both places: the packet ctor FUN_142c66de0 sets `+0x490 = 1`, and the ProxiedProp ctor
  FUN_1430a7760 sets the u16 at `+0x4e9c = 1`.
- `PlayerUpdateAddLightweightProp.Write` ends with `writer.Write(false);`, so that byte is 0 for every prop.
- The Teleporter enter FUN_1436db8b0 returns before it does anything when the pad's `+0x4e9c == 0`. It creates no
  callback and sends neither 0xFC/9 nor 0xFC/10. The ChainThrustPad FUN_1436d9c90 also returns when the source part's
  (the flinger's) `+0x4e9c == 0`. That matches round 91 exactly: total silence, no throw.
- The moving platform works because SlidingFriction never reads +0x4e9c. Its rider carry is armed at spawn instead
  (FUN_142fe1c80 sets prop +0x4e58), not by an area enter. So the platform does **not** show that area enter events work.

**Server change (one field):** send the enabled byte as 1 (`!prop.Disabled`) instead of `false`. Details in §6.

## 1. The packet tail (who reads what) [P]

`pkt.py 142c273d0` (prop part after the lightweight base FUN_142c26790):

| wire order | object offset | server writes today | meaning |
|---|---|---|---|
| u64 | +0x2a0 | OwnerGuid | owner; if == local player, FUN_142c98c00 marks it locally managed (also done for every prop by FUN_1430aafd0 in SpawnProxiedProp) |
| u8, u32, u8 | +0x2b0, +0x2b4, +0x318 | 1, 0, 0 | collision block FUN_142c40140 (type, …) |
| u32 (type 1) | +0x2b8 | 0 | collision flags (FUN_142c02770); bit 28 would add 3 u32 + a 4-byte block |
| i32 count + lists | +0x340 | 0 | settings lists FUN_142c3b8a0 → FUN_1430ab830 (actor +0x4c00) |
| u32 | +0x480 | 0 | → ProxiedProp +0x4e5c [?] (ctor 0) |
| u64 | +0x488 | 0 | → ProxiedProp +0x4e60 [?] (ctor default DAT_14544faa0, packet ctor default DAT_145430bf0, i.e. "none") |
| **bool** | **+0x490** | **false** | **enabled → ProxiedProp +0x4e9c (FUN_1430abea0)** |

Who reads ProxiedProp +0x4e9c (every hit of `0x4e9c` in the decompile): the ctor (writes 1), FUN_1430abea0 (setter),
and five mover reads: FUN_1436db8b0 (Teleporter), FUN_1436d9c90 (ChainThrustPad), FUN_1436d9680 (shared launch
helper), and FUN_1436dbf00 ×2 (Thrower). (part_0081 FUN_143028c20 writes a u32 at +0x4e9c of a different class.)
So making props enabled changes **only** the movers [P]. The ProxiedProp vfuncs +0x100 / +0x108 that the movers call
both return `this` (FUN_1430a8da0 / FUN_1430a8d90) [P].

Runtime switch: PlayerUpdate **0x26 / 0x73 SetEnabled** {u64 guid, bool} (reader FUN_142c2b660, dispatch
FUN_142d54c10 → FUN_142f85dc0 → FUN_1430abea0(prop, bool, 0)) [P]. The server has the opcode (`PlayerUpdateSetEnabled =
0x26007300` / `PlayerUpdateSetEnabledWire = 0x260073`) but only logs it as C2S. The client never sends it here: both
callers of FUN_1430abea0 pass send = 0.

## 2. Teleporter (type 23 / 38): every condition for the local player [P]

**Enter chain:**

1. The physics trigger reports an actor to ClientAreaData vtable +0x100, FUN_143cc93b0. It needs:
   - area **+0x175** active. AreaDefinition ctor FUN_143db7d80 sets its +0x35 to 1, and FUN_143d67420 copies it into
     the area's +0x174 / +0x175. GlobalAreas.txt has no column for it.
   - the manager filter FUN_1431abb00, which keeps the local player's controller (the entity sub +0x10 vfunc 0xf8 test,
     or a vehicle carrying the local player).
2. The actor is added to the contained arrays (+0x1a8 / +0x1d0) and +0x176 is set. Then:
   - if the area has a parent group (+0x188 ≠ none), the event goes to the parent's +0x100;
   - otherwise the manager queues an AreaEvent (vfunc 0x48 FUN_1431a64f0, array +0x968).
3. The manager's FUN_1431b1160 (vtable +0x58) drains the queue: area **+0x110** on enter, +0x120 on exit.
4. The static area of a prop is created by the manager's vtable +0x28 (FUN_143d67420, from FUN_142d54040) as a
   **ClientAreaDataGroup**. Its +0x110 is FUN_143d924b0, which calls each property's vfunc **+0x80** with the guid.
5. The Teleporter's +0x80 (FUN_142fe1cc0) forwards to +0x88, **FUN_1436db8b0**.

**Not required** [P]:
- There is no leave-then-enter test of position and no ground contact test. Entering the overlap is enough [I: a
  trigger overlap after a teleport counts as an enter].
- UPDATE_INTERVAL 500 only paces the update (+0x148 → property +0xa0 FUN_1436dbb40, which advances the callback).
- Rows 6 / 7 / 19 / 26 of GlobalAreas.txt have TRACKABLE_REQUIREMENT_ID 0 and REQUIREMENT_SOURCE 0 / 0, so there are no
  requirements.
- FUN_1436db8b0 and FUN_1436db650 have no Play Mode, build mode, site permission or owner test.

**FUN_1436db8b0 gates, in order:**

1. The local player (`*(DAT_1454319c0)+0x282c8`) exists. Its vfunc 0x118 returns an actor, and its physics controller
   +0x148 is set.
2. No `"*Bamf!*"` callback is pending on the controller's container (+0x148 − 0x18).
3. The pad entity is found by **area data +0x10**. That value is set by vfunc 0xd0 = FUN_1431b6070 from **spawn +0x18,
   the prop's own Guid** (FUN_142d54040, type 0x17), and looked up by entity +0x2b0 (FUN_142f85390).
4. **Pad ProxiedProp +0x4e9c ≠ 0. ← fails today.**
5. `enable_teleportation`: key 0xa632764b in settings list 2 (actor +0x4c00 → +0x60 list, count +0x68, buckets +0x88).
   **When it is missing the default is 1**. The list is always allocated (FUN_1430a7590 in the ctor), so with an empty
   settings list nothing is dereferenced through a null pointer.
6. The ignore set FUN_1433286b0 (actor +0x4dc0) **does not block** the enter. It only picks the callback's start state:
   - not ignored: state 1 (delay > 0) or state 3;
   - ignored: state 6, which still sends **0xFC/9** with effect data +0x20.

   So an ignore window can never explain *total* silence.

**Callback** (FUN_1436db150, +0x20 = group key from data +8, +0x28 = pad Guid from data +0x10, +0x50 = property data):

- State 1: send 0xFC/9 if effect +0x18 is valid (FUN_1431736e0).
- State 2: wait for the delay (+0x2c × DAT_144c41034).
- State 3: local effect (+0x28 sound, +0x1c composite on the player).
- State 4: wait while the composite plays.
- State 5: send **0xFC/10 {u64 = the source pad's prop Guid, u8 0, u32 1, u32 0, u32 −1, u64 DAT_145454bd8, u64
  DAT_145454dc8}** (FUN_143173480).
- State 7: done.
- Leaving the sphere while the callback is pending cancels it (exit FUN_1436dbac0 → FUN_1436db3e0).

**Pairing:** the client never looks up the other pad. The group key (+0x270) goes only into the callback. The server
picks the other pad from the source pad's Guid. `Props.ManagedTeleport` already does this, and it already sends the
arrival ignore keyed by the destination pad's **prop Guid**, which is right per gates 3 and 6.

**Arrival ignore from our test move:** TeleportToLocation +0x48 / +0x50 calls FUN_143325060 only when the guid is
valid and the ms value is above 0 (FUN_142d59f30). A dev TeleportToLocation with 0 / 0 sets nothing. Even if it had
set something, the result would have been an 0xFC/9 (state 6), not silence. FUN_142cd4e60 is a second inserter [?, not
traced; the hook logs its return address].

## 3. ChainThrustPad (type 24): every condition [P]

The enter path is the same up to the group (§2 steps 1-4). The property's +0x80 is **FUN_1436da050** itself, and its
+0x88 is empty. FUN_1436da050 calls FUN_1436d9c90(property, local controller − 0x18, 1).

FUN_1436d9c90 gates:

1. The container's +0x90 (an active physics callback) is not "busy": vfunc 0x130 on (+0x90 − 0x10) returns 0.
2. The local player and its +0x148 exist.
3. The props of the **group key** (property +0x80 = spawn +0x270) are walked through the manager hash +0x180d8 (prop
   +0x340 key, +0x348 next; FUN_142f85170 / FUN_142f85230). The key is registered at spawn from packet +0x270, through
   the SpawnProxiedProp arg +0x590 → vfunc 0x598 → prop +0x4a50 → FUN_142f86270 (the key "none" is skipped).
   - The **source** is the part whose entity +0x240 (= spawn +0x268 AreaGuid) equals the property's area guid
     (`*(prop+0x40)+0x150`) [I-strong that the group's +0x150 is the +0x268 guid it was created with].
   - Any other part is the **target**.
4. There must be a source, and the **source's +0x4e9c ≠ 0. ← fails today.**
5. With a target: FUN_1436d9720 computes the arc to it. Without one: FUN_1436d9ab0 launches by the settings
   (defaults, because the list is empty).
6. The player's velocity is set on the client. No packet is sent.

No requirement, permission, Play Mode or owner test applies. GlobalAreas row 6 has ACTION_ID_ENTER 3, which is
server-side data with no client gate [I].

## 4. Client-managed state [P]

Every new prop goes into the local player's managed set: SpawnProxiedProp FUN_142f8a800 → FUN_1430aafd0 →
FUN_142c98c00 (client +0x32f68). It happens again when OwnerGuid == the local player (FUN_142cde1c0).

Neither mover reads the managed set or the entity's managed flag. The managed state only matters for 0x7F position
playback, which is the platform's case (Props.Platforms releases it with 0x4D). Pads and flingers need no release.

[I, low] FUN_1431abb00 accepts any actor whose entity reports "locally controlled" (+0x10 vfunc 0xf8). If a static
prop had a trigger actor, its own part could raise an enter, and the Teleporter enter acts on the local player whatever
actor triggered it. The hook logs every actor that enters a mover area, so this can be checked.

## 5. Ranked causes

1. **Packet +0x490 enabled = 0 (we write `false`).** [P] It blocks both movers at a gate that sits before every packet
   and every effect. This alone explains round 91.
2. The area enter never reaches the group (trigger not built, +0x175 off, or the actor filter rejects the player).
   [I, unlikely: the AreaDefinition default is active and the rows have no requirements. The platform cannot vouch for
   it (§0).] The Frida run below settles it.
3. The source part of the flinger is not found: +0x268 versus the group's +0x150 [I-strong it matches, since BindMover
   gives every part a unique AreaGuid and the same group].
4. Ignore window, settings list, permission, owner: **ruled out** by §2 gates 5 and 6 and by §3 [P].

## 6. Exact server change

`LandmarkEmulator.WorldServer/Network/Message/Model/PlayerUpdateAddLightweightProp.cs`:

```csharp
/// Packet +0x490 (last byte; reader FUN_142c273d0) → ProxiedProp +0x4e9c via FUN_1430abea0 (handler FUN_142cde1c0).
/// Client defaults 1 (packet ctor FUN_142c66de0, prop ctor FUN_1430a7760). Read only by the movers: Teleporter
/// FUN_1436db8b0, ChainThrustPad FUN_1436d9c90, launch helper FUN_1436d9680, Thrower FUN_1436dbf00.
public bool Enabled { get; set; } = true;
...
writer.Write(0u);      // +0x480
writer.Write(0ul);     // +0x488
writer.Write(Enabled); // +0x490 enabled (was false: every prop spawned disabled)
```

- Spawn: set `Spawn.Enabled = !prop.Disabled`, both where PlacedProp spawns are built (Props.cs ~932 / ~2302, or in
  `SendSpawns`) and in BindMover. That way a pad switched off with PropBase 0x1A stays off after a relog (live default
  on; the toggle stays).
- `Props.SetEnabled` (PropBase 0x1A): also send **PlayerUpdate 0x26/0x73 {u64 guid, bool enabled}** (opcode
  `PlayerUpdateSetEnabled`) to `ViewersOf(prop)` when the prop is a mover part. That switches a live pad or flinger
  without a respawn (FUN_142f85dc0) [P for the client side, I that live drove the mover toggle this way]. The cost is
  about 13 bytes per viewer, only on a toggle.
- Nothing else is needed for the pads: 0xFC/10 handling and the arrival ignore (prop Guid key) are already right. The
  0xFC/9 effect relay is optional.
- Retest: the same placement; stand on pad A, or walk onto it. Expect 0xFC/9 (effect id 10625 or similar), then about
  0.5 s later 0xFC/10 with pad A's Guid, then our TeleportToLocation to pad B with the ignore. Walking onto the flinger
  should throw toward 1191.

## 7. Frida plan (one round) — `re/hooks/movers/hook_movers.js`

Read-only. Signatures are checked against the test copy (all 8-byte prologues read from `<0.695 test copy>\Landmark64.exe`).
`recv('stop')` detaches. Run it with `re/hooks/recipes/run_hook.py` and attach **before** the movers spawn (re-zone or
relog onto the site):

```
cd "<workspace>\re\hooks\movers"
python ../recipes/run_hook.py ../movers/hook_movers.js logs/r92.jsonl --pid N
```

| hook | address | logs |
|---|---|---|
| spawn | 0x142cde1c0 | for areas 6/7/8/14/15/19/20/26: guid, +0x260, +0x268, +0x270, owner +0x2a0, settings count, **+0x490** |
| setEnabled | 0x1430abea0 | binds guid → ProxiedProp; the value written to +0x4e9c |
| s2cSetEnabled | 0x142f85dc0 | 0x26/0x73 for mover guids |
| areaActorEnter / Leave | 0x143cc93b0 / 0x143cc95b0 | area guid +0x150, +0x175, +0x176, parent +0x188, prop guid +0x1f8, static id +0x218, actor |
| groupEnter | 0x143d924b0 | group fields + property vtables (Teleporter 0x14471d2f0, ChainThrustPad 0x1447d0ba0) |
| teleporterEnter | 0x1436db8b0 | data +8 / +0x10, the pad's +0x4e9c, settings counts, local player +0x148 |
| bamfCtor / bamfState | 0x1436db150 / 0x1436db650 | notIgnored flag, effect ids, delay; state transitions |
| teleporterExit | 0x1436dbac0 | cancel |
| send_FC9 / send_FC10 | 0x1431736e0 / 0x143173480 | the two requests (guid) |
| thrustEnter / thrustFire | 0x1436da050 / 0x1436d9c90 | group key, area guid, busy +0x90, every part's +0x240 / +0x4a50 / +0x4e9c |
| thrustArc / thrustPlain | 0x1436d9720 / 0x1436d9ab0 | the result |
| ignoreInsert | 0x143325060 | ms and guid, caller (0x142d59f30 or 0x142cd4e60) |
| teleportApply | 0x142d59f30 | TeleportToLocation pos, +0x40..+0x42, +0x48 / +0x50 |

How to read the result:

- **Before the fix:** expect `spawn.enabled490 = 0` and `moverBound.enabledArg = 0`. If cause 1 is the only one, the
  log also shows `groupEnter` (props include 0x14471d2f0) and `teleporterEnter` with `pad.enabled4e9c = 0`, and no
  `bamfCtor`.
- No `areaActorEnter` / `groupEnter` when standing on the pad means cause 2. Then compare `active175` and the actor list.
- **After the fix:** `teleporterEnter` → `bamfCtor` (notIgnored 1) → states 1→2→3→4→5 → `send_FC10`, then
  `ignoreInsert` from 0x142d59f30 with the destination pad's Guid. For the flinger, `thrustFire` (source enabled 1) →
  `thrustArc ok 1`.

## 8. Open leads (add to OPEN-LEADS.md)

- Prop +0x4e5c (packet +0x480) and +0x4e60 (packet +0x488): no direct readers found yet (written only by the ctor and
  the spawn). Next: vfunc getters of ProxiedProp, and the 0.589 decompile.
- The second ignore inserter FUN_142cd4e60 (which packet sets a teleporter ignore with a computed ms) [?].
- [I] Does a trigger overlap fire when the player is teleported into the sphere, or only when the player walks in? The
  hook answers it.
