# Prop collision on the 0.695 client (agent "prop-collision", 2026-10-17)

Question: how the 0.695 client gives a prop (the Spire deck props 489, 575, 884, 1871, 2894, 2895, 2942) its
collision, why players arriving from another landscape fall through the deck, and the smallest server change that
fixes it. Read-only pass over `re/decomp_all_695`, `re/out/rtti_695.tsv` and the server code. The vtable slots were
read from `<0.695 test copy>\Landmark64.exe` with `re/tools/vtslots.py`; the file was only read.
**[P]** means proven by a function address or a decompile line. **[I]** means inferred, with the reason given.

Existing research checked first:
- agent-e-leads.md §7-8: collision refresh at ≤ 1 Hz on 0x7F.
- agent-props2.md: riders.
- agent-live-zonein.md: there are no live prop or zone-in captures on disk.
- OPEN-LEADS L100.
- Spire.cs, PlayerManager.Reintroduce, MiscHandler.FinishZoneIn, Teleport.RezoneNow.

Nothing earlier traced the collision build itself.

---

## 0. Summary

1. **Every ClientBeginZoning destroys every entity except the local player, whether the re-zone is in place or a
   change of landscape [P].**
   - Dispatcher case 8 (FUN_142d1ccf0, part_0066 ~14113-14183, logs "RECEIVED=Begin Zoning") always calls
     FUN_142d60340 ("GameEvents:OnPlayerBeginZoning").
   - FUN_142d60340 calls `FUN_142f83590(manager, manager+0x282c8 = local player)` unconditionally. That function takes
     the local player out of the list, clears all the entity hash tables, and runs
     `while (count +0x48) FUN_143b66eb0(entity+0x10)`.
   - FUN_143b66eb0 is the deleting destructor: vfunc +8 → FUN_1430a7fc8 → FUN_1430a8150 → dtor FUN_1430a7c40. The
     dtor removes the prop's physics object (FUN_142b7f2d0).
   - So "an in-place re-zone keeps the client's entities" is **false**, both in `Teleport.RezoneNow` and in the
     comment at `PlayerManager.Reintroduce` ("sending a Spire prop again then cost it its collision").
     `PlayerManager.cs:145` already said it correctly. After an in-place re-zone with no re-send, the Spire props are
     not there at all.
   - That explains coordinator result 1: the in-place re-zones at +10 / +30 / +60 s destroyed the props. That test
     therefore says nothing about whether props created on a cross-landscape arrival gain collision later.
2. **A prop's collision is built once, in the first complete update of its model (Actor), and only if the model's
   current camera-distance LOD is a real, loaded LOD [P].** (§1)
   - When the build is skipped, the one-time latch is still used up. Nothing retries the build except:
     - PlayerUpdatePosition 0x7F (the "moved" flag +0x4F00)
     - the prop placement tools
     - the Area tool
   - No packet field turns a prop's collision on or off (§2).
   - Nothing in the zone-ready logic waits for collision (§3).
3. **The prop part we write is read correctly, and none of its fields is a collision type [P].** (§2)
   - The "collision type 1" byte is the **movement-body type**. Type 1 means "flags only", so our comment is wrong.
   - The last bool (+0x490) is the prop's **Enabled** flag: entity +0x4E9C, the same field as PlayerUpdate 0x73
     SetEnabled. Its packet default is **1**. We send **0**, which disables the prop's mover areas (§2.3). It does not
     affect collision.
4. **Why arrivals fall [P for the mechanism, I for which gate fails]:** the one-time build (FUN_1430aabc0 →
   FUN_1430ac390) runs while the arrival is still loading. Either the actor's current LOD index +0x2B4 equals the LOD
   count (the actor is "beyond the last LOD", i.e. culled by camera distance), or that LOD's mesh is not loaded yet.
   The build then does nothing and is never retried.
   - On an in-place re-zone, props created after the flags find the camera already at the arrival point and the Spire
     model cached, so the build succeeds. This matches coordinator results 2 and 3.
   - The Frida plan in §5 says which of the two gates fails.
5. **Minimal server changes** (§4):
   - **(a) [P]** Send the Spire props after **every** re-zone, not only after a change of landscape.
   - **(b) [P mechanism]** Re-trigger the build: send PlayerUpdatePosition 0x7F with the prop's unchanged transform
     once the arrival has settled, then once more ~1.5 s later.
   - **(c) [P mechanism, I that it is enough]** Send the Spire props *before* DoneSendingPreloadCharacters with
     `WaitForLoad = true`. The loading screen then holds until those models are loaded, and that is the same actor
     update that runs the build.
   - **(d) [P]** Send +0x490 = 1 (Enabled).

   Which mix is needed is decided by the §5 run.

---

## 1. Packet → collidable prop, with addresses (0.695)

### 1.1 Spawn
| Step | Function | What it does |
|---|---|---|
| Reader | FUN_142c273d0 | Base reader FUN_142c26790 + prop part (§2) [P] |
| Handler | FUN_142cde1c0 (BaseClient::HandlePlayerUpdateAddLightweightProp, BaseClient.cpp:0x4f9c) | Builds the spawn struct (flag bytes +0x298 / +0x29a → struct +0x598 / +0x599) → SpawnProxiedProp. Returns 0 without initialising if no local player exists (`manager+0x282c8 == 0`) [P] |
| SpawnProxiedProp | FUN_142f8a800 (ProxiedCharacterManager.cpp:0x247) | A new guid creates a ProxiedProp (size 0x4F20, ctor FUN_1430a7760). The ctor sets **+0x4E50 = 1 (collision enabled)**, +0x4E9C = 1 (enabled), +0x4E9D = 0 (build latch) and +0x4E5A = 1 [P: part_0083 31088-31101] |
| Duplicate guid (live entity) | same function | Lookup by guid (entity +0x2B0) in the hash at manager+0x50:<br>• featherweight (+0x1461 bit 3, set by AddFeatherweightObject FUN_142cdd710): the entity is reused and upgraded.<br>• pending destroy (+0x1457 bit 7): the stale scan FUN_142d20da0 ("Scan for stale Proxied Chars") runs and a new entity is created.<br>• **Otherwise it returns NULL and the handler returns 0: the duplicate is ignored and nothing is re-registered** [P] |
| Handler, after spawn | FUN_142cde1c0 | Steps:<br>• FUN_142d54040 (base data)<br>• vfunc +0xD0 movement body (ProxiedProp FUN_143500500 thunk; this does not set +0x4F00)<br>• +0xCB4 = 1 (lightweight)<br>• FUN_1430ab830 (editable properties, +0x340)<br>• +0x4E5C ← +0x480<br>• +0x4E60 ← +0x488<br>• FUN_1430abea0 (Enabled ← +0x490)<br>• vfunc +0x328(0): clears the featherweight bit (FUN_142ec18a0)<br>[P] |

### 1.2 The collision build (one time)
The model is an `Actor` (RTTI 14495b668). Its listener is the ProxiedProp sub-object at +0x250 (RTTI row
"ProxiedProp 592"), stored at actor +0x340.

1. The actor update, Actor vfunc +0x28 = **FUN_143d0e060**, runs each frame. Its tail (decompile lines ~730-736) does:
   ```
   if (!(actor+0x42d & 0x20)) { actor+0x42d |= 0x20; actor->vfunc0x180(); }   // FUN_143d08080 "fully loaded"
   if (actor+0x340) listener->vfunc0x88(actor);                                  // every update
   ```
   [P]. The update reaches this tail only after its early returns are passed: at least one LOD loaded
   (FUN_143d01fc0 ≠ 0) and the scene node is present.
2. Listener 0x88 = **FUN_1430aabc0**: `if (entity+0x4E9D == 0) { entity+0x4E9D = 1; FUN_1430ac390(entity); }`. This is
   the **one-time latch**. It is set *before* the build, so a failed build still uses it up [P].
3. FUN_143d08080 (vfunc 0x180) → listener 0x70 = **FUN_1430aab50**. It calls FUN_1430ac390 only if the actor has the
   ready bits 0x20|4|2 and +0x368, and entity +0x4BE8 == 0. This path also runs only once, because bit 0x20 is set
   only once [P].
4. **FUN_1430ac390**, the register / rebuild step. It does something only if **all** of these hold [P]:
   - entity +0x4E50 ≠ 0 (collision enabled; only FUN_1430a8a00 changes it, §2.4)
   - actor (entity vfunc +0x58) ≠ 0 and actor +0x348 ≠ 0
   - **actor +0x2B4 (current LOD) < actor +0x190 (LOD count)**
   - **LOD entry `*(actor+0x188)[+0x2B4]` ≠ 0 and its +0x10 ≠ 0** (that LOD's mesh is loaded)

   Then:
   - FUN_1430ac0d0 stores the bounds.
   - If +0x4E9D == 0, or the actor's collision object (entity vfunc 0x758 → actor +0x388 − 0x18) says vfunc 0x170 ==
     0: **FUN_1430a82a0** registers the actor's own collision object.
   - Otherwise it builds a new physics object:
     - shape from actor vfunc +0x128, via FUN_143b054e0 / FUN_143b055b0
     - FUN_143b02960(obj, actor +0x358 name, shape, flags 0x1000004; or 0x2000 when the prop's area guid +0x240
       names a trigger area, FUN_142f86d40)
     - stored at entity +0x4BE8 … +0x4BE8 is param_1[0x97d]
   - Either way it is added to the scene with **FUN_142b7d510(world DAT_14541e9b8, obj, group, 0)**. The group is 2,
     or 0xC while the prop's interpolator (+0x4D98) is active.
   - FUN_142b7d510 takes the default scene: PhysicsEnginePhysX vfunc 0x1D8 = FUN_142ba6a70 → engine +0xA48. That
     scene is created once at start-up in FUN_142b9e820; there is no per-zone scene [P].
   - At the end it clears +0x4F00 and stamps +0x4F08, even if no object was made.
5. The LOD index +0x2B4 comes from camera distance [P]:
   - FUN_143cffe20 → FUN_143d0b3d0(actor, metric). The metric is ‖actor position − render view position‖ × the view
     factor.
   - FUN_143d0b3d0 can set **+0x2B4 = LOD count without loading anything** (`uVar10 == uVar1` branch: past the last
     LOD = culled).
   - For a real LOD it sets the index once FUN_143d06d90 succeeds. Whether that waits for the mesh, or only starts the
     load, is not known yet; §5 measures it.
   - The LOD-change callback (Actor vfunc 0x1A0 = FUN_143d06810 → FUN_143d08140 / FUN_143d0df30) handles materials
     and effects. **It does not call the listener, so no collision retry happens on a LOD change** [P for the code
     read].
6. **What re-runs FUN_1430ac390 later** (callers, `fn.py -x 1430ac390`) [P]:
   - **ProxiedProp update FUN_1430ab000** (vfunc 0x4C8), when +0x4F00 is set and the last stamp is absent or more than
     1000 ms old. +0x4F00 is set **only** by the PlayerUpdatePosition 0x7F handler FUN_142ce6340 (line ~128). The
     update returns early, keeping the flag, while `+0x145C & 0x10` or the model is not ready (FUN_143d05520).
   - Prop placement / move tools: FUN_142fb1660, FUN_142fb40b0, FUN_142fbb860, FUN_142fbb930, and SetCollisionEnabled
     FUN_1430a8a00 (callers FUN_142fb3b80 / 3e40 / bd9d0 / c0790 / 9ba0, FUN_143688e30). All are placement UI.
   - Area tool: FUN_143739860, FUN_143739ad0.
   - None of these is reached by a zone change or by a full conversion.
7. LightweightToFullProp (FUN_142cdf4b0 → FUN_142cdf400 → FUN_142d532f0) does not touch collision: no FUN_1430a* call
   and no +0x4E50 / 0x9CA in FUN_142d532f0 [P]. A full conversion therefore cannot give a prop collision. The
   ".lightweight props have no collision until full" premise in Spire.cs / OPEN-LEADS L100 is not supported by the
   client [P].
8. The `.cdt` loader FUN_142f84f60 (ProxiedCharacterManager::CdtCollisionData) is not on this path. The prop's shape
   comes from the actor (vfunc 0x128 / actor +0x388), which matches the coordinator's trace [P].
9. "Tile Create Collision" (FUN_1437b6c30, GameRuntimeLodTerrain\LodTile.cpp) builds **terrain heightfield** objects
   named "LodTile" from the tile's own height data. It never touches entities [P]. "TileStatic" (FUN_142d531c0 /
   part_0067 ~5966) is only a ZoneType name (0 = TileStatic … 5 = BlockTerrain) [P]. Neither has anything to do with
   props.

---

## 2. The prop part we write: meaning of each field

The 0.695 reader is FUN_142c273d0. It is the base part FUN_142c26790, then the fields below. The packet ctor
FUN_142c66de0 sets the defaults [P].

| We write (PlayerUpdateAddLightweightProp.cs) | Packet offset | Reader | Meaning in the client | Right value |
|---|---|---|---|---|
| u64 OwnerGuid | +0x2A0 | direct | The owner. If it equals the local player's guid, the handler calls FUN_142c98c00 (the player owns this entity) [P] | as now |
| **u8 1** "collision type 1" | +0x2B0 | FUN_142c40140 | **Movement-body type**: the same block as the 0x7F body.<br>0 = full (pos/rot/vel…), 1 = **flags only** (u32 +0x2B8), 2, 3, 4, 5 = other bodies [P switch] | 1 is right. It is **not** collision |
| u32 0 | +0x2B4 | same | Body header u32 (body +4) [P] | 0 |
| u8 0 | +0x318 | same | Body byte +0x68 [P] | 0 |
| u32 0 "flags" | +0x2B8 | same, case 1 | Body flags. Bit 28 adds 3 × u32 + a packed id (FUN_142c0bd80) [P] | 0 |
| u32 0 "list" | +0x340 | FUN_142c3b8a0 | Editable-property list (oaat-keyed settings), applied by FUN_1430ab830 [P] | 0 or the settings |
| u32 0 | +0x480 | direct | → entity +0x4E5C. No reader found yet | 0 |
| u64 0 | +0x488 | direct | → entity +0x4E60. Ctor default DAT_14544faa0. No reader found yet | 0 |
| **bool false** | **+0x490** | direct | → FUN_1430abea0 → **entity +0x4E9C = Enabled**, the same field as PlayerUpdate 0x73 SetEnabled. Packet default **1** (ctor `param_1[0x92] = 1`) | **1** (0 only for a disabled prop) |

### 2.1 The flag bytes +0x298 / +0x29A (written as `WriteBytes(new byte[3])`) [P: FUN_142cde1c0 + FUN_142f8a800]

The bits are mapped: packet byte, bit → spawn struct +0x599, bit → effect.

| Packet bit | Spawn struct +0x599 | Effect |
|---|---|---|
| +0x298 bit 6 | +0x598 bit 6 | no reader seen in SpawnProxiedProp |
| +0x29A bit 5 | bit 7 | (not read in SpawnProxiedProp) |
| +0x29A bit 4 | bit 6 | entity vfunc 0x268(0x28, v) → FUN_142ec5f80 (sets / clears entity flag 0x28) |
| +0x29A bit 3 | bit 5 | +0x4E59, read by the radial-menu code (FUN_143448150 via vfunc 0x5A0) |
| +0x29A bit 2 | bit 3 | entity +0x1461 bit 5 |
| +0x29A bit 1 | bit 4 | +0x4E5A (ctor default 1), read by ScreenshotManager::DoCapture (FUN_1431eddc0 via vfunc 0x5A8) |

**None of them reaches +0x4E50 or the collision build.**

### 2.2 Is collision typed or switched by a packet field?
No [P]. The only switch is +0x4E50, set by the ctor (1) and by FUN_1430a8a00. Every caller of FUN_1430a8a00 is a
placement tool (§1.2.6). The collision filter (0x1000004 vs 0x2000) depends only on whether the prop's area guid
(base +0x268 → entity +0x240) names an AreaDefinition with +0x218 ≠ 0 (a trigger) (FUN_142f86d40). For the Spire
(AreaGuid 0) it is the solid filter.

### 2.3 What +0x4E9C (Enabled) does [P]
- Read by ClientAreaChainThrustPad (FUN_1436d9680), ClientAreaTeleporterBase (FUN_1436d9c90, FUN_1436db8b0 "*Bamf!*")
  and ClientAreaThrower (FUN_1436dbf00). In each, a prop-backed mover area acts only while its prop is enabled.
- Sending 0 makes every static-area prop (teleporters, flingers, launchers) inert until a SetEnabled 0x73 arrives.
- It does not affect collision.
- Live's value is unknown: there are no captures (agent-live-zonein.md). The packet default is 1.

### 2.4 Live values
There are no live AddLightweightProp bytes on disk (agent-live-zonein.md §2). From the client:
- type 1, flags 0 is a valid "no movement" body [P].
- Enabled should be 1 [P default].
- The flag bytes have no collision role [P].

---

## 3. Zone change, zone-ready, and why the preload bool did not hold

- **ClientBeginZoning** (case 8 of FUN_142d1ccf0):
  - It computes `same = (new zone type == current && name == current && world == +0x378cc)`.
  - It always calls **FUN_142d60340**, which does:
    1. FUN_1434b9400
    2. the Lua event OnPlayerBeginZoning
    3. FUN_142d22840 / FUN_143d5fce0
    4. if not `same`: voxel teardown FUN_1431589a0(voxel, 1, 0) and FUN_1431ffae0
    5. **FUN_142f83590: all non-local entities destroyed**
    6. FUN_1431c3c40, FUN_142fb45b0, FUN_142d4f410
    7. FUN_142d58cd0 (zone load)

  If not `same`, FUN_142ca8cc0 (voxel unload) also runs before it. [P]
- The physics scene is **not** reset by a zone change: it is one PhysX scene from start-up [P]. FUN_142d3b730 /
  FUN_142ca6660 only replace the zone-boundary object, from ZoneCoordinateInfo bounds, when the origin is applied
  (FUN_142ce7e80) [P].
- **ZoneCoordinateInfo** (0x12, reader FUN_142c23b80: u16, 3 × f64 origin, 3 × f32 size, 3 × f32 **shift**, bounds):
  - With a non-zero shift, FUN_142ce7e80 moves every entity (vfunc +0x48). It does not set +0x4F00, so physics would
    stay behind [P].
  - Our captured body has shift (0, 0, 0) and one origin for all landscapes, so this does not apply [P].
  - We do not re-send ZoneCoordinateInfo on re-zone. That is harmless, because the origin is the same.
- **WaitForWorldReady FUN_142d5e590** returns ready only if all of these hold [P]:
  - InitialZoneDataComplete (+0x31E0C, ZoneDone case 3)
  - ReceivedPreloadDonePacket (+0x31E0E, ClientUpdate 0x1A)
  - **RequiredCharactersLoaded FUN_142f86ee0**
  - WaitForZoneReadyPacket (+0x31E0F, from BeginZoning; cleared by FUN_142d10020) == 0
  - +0x31560
  - ShouldDrop FUN_142d54a00
  - +0x31E10
  - bAreaDefinitionsLoaded +0x31E0D
- **RequiredCharactersLoaded** counts only entities that pass FUN_142eaa5f0: +0x2450 == 0 and (+0x1458 bit 4 **or**
  +0x4158). The test is vfunc **0x4F8** (ProxiedProp FUN_142eaa440 → FUN_143d05520 = actor bits 0x20|4|2 &&
  +0x368: **model loaded, not collision**) [P].
- +0x1458 bit 4 is set **only** by FUN_142f86f80, when DoneSendingPreloadCharacters has `WaitForLoad = true`
  (FUN_142cd9980 case 0x1A). It marks the entities that exist **at that moment** [P].
  - **That is why setting the bool did not hold the client: the props were sent after the flag, so none was marked**
    [P].
  - Props sent before it with the bool set would hold the loading screen until their models are loaded. The actor
    update that sets bit 0x20 is the one that runs the collision latch (§1.2.1), so the build attempt has been made by
    then [P]. Whether it *succeeds* depends on the LOD gate (§1.2.4).
- **No client mechanism waits for prop collision**: none of the WaitForWorldReady terms reads +0x4E9D, +0x4BE8 or the
  physics scene [P for the terms listed]. After the drop, the character controller just falls.

---

## 4. Why our arrivals fall, and the server changes

### 4.1 Diagnosis
- **[P]** The build is one-time and can silently fail (§1.2).
- **[P]** A full conversion does not help (§1.2.7).
- **[P]** A duplicate AddLightweightProp for a live prop is ignored (§1.1).
- **[P]** Every re-zone destroys the props (§0.1).

**[I, consistent with all three coordinator results]** The latch fires while the arrival is still loading, and the
build fails one of two gates:
- (G1) `actor+0x2B4 == LOD count`: culled, because the render view is not yet near the Spire. During a
  cross-landscape load the view stays at the old or unset position until the drop. When props are sent during
  WaitForConfirmationPacket on an in-place re-zone, the view is likewise not placed yet.
- (G2) That LOD's mesh is not loaded yet: the model is not cached after a landscape change.

When the props are created after the flags on an in-place re-zone, the view is already at the arrival point and the
model is cached, so both gates pass. Result 3 (props before the flags, in place, fell) fits G1 better than G2,
because the model would be cached in an in-place re-zone. §5 decides between them.

### 4.2 Changes (in order)
1. **[P, do regardless]** In `PlayerManager.Reintroduce`, call `Spire.Enter` after **every** re-zone, and fix the
   comment and `Teleport.RezoneNow`'s "in place keeps entities" text. The client wiped them (FUN_142f83590).
2. **[P mechanism] Collision re-trigger with no visual change:**
   - What to send: PlayerUpdatePosition 0x7F for each of the 7 Spire props. Use the prop's transient id and its
     *unchanged* position and rotation, as a type-0 movement body with zero velocity, the same as
     `Props.Platforms` / `Creatures.MovementBody`.
   - The client sets +0x4F00 (FUN_142ce6340). The next ProxiedProp update then runs FUN_1430ac390 with the current
     LOD, at most once per 1000 ms.
   - When: once the arrival has settled (`Teleport.FinishRezone`, and again on the client's first movement update
     after the re-zone), then once more ~1.5 s later.
   - Cost: ~7 × ~50 B per arrival.
   - Limit: if the player stands on the deck at the drop, the re-trigger may come a frame or two after the controller
     starts. The fall is ~20 m, so it should still land on the deck once the collision exists [I]; §5 checks this.
3. **[P mechanism, I that it is enough] Hold the drop for the Spire models.** In `MiscHandler.FinishZoneIn` (re-zone
   ClientIsReady) and in the first-login path, send in this order:
   1. `Spire.Enter` (AddLightweightProp ×7)
   2. `DoneSendingPreloadCharacters { WaitForLoad = true }`
   3. ZoneDoneSendingInitialData
   4. then the rest (Reintroduce minus the Spire)

   The loading screen then stays up until the 7 actors report loaded (RequiredCharactersLoaded). With G2 this alone
   should fix it. With G1 it does not, because the view is still not placed while loading, and change 2 is needed.
   Do not send the props before the client's ClientIsReady for the re-zone: they would be created during
   WaitForConfirmationPacket (result 3) [I].
4. **[P]** Write +0x490 = 1 (Enabled) for normal props. Keep `Props.SetEnabled` (0x73) for the disabled ones. This
   is separate from collision.
5. Optional: `SendFullAtOnce` (unrequested LightweightToFullProp) is not needed for collision [P]. Keep answering
   FullCharacterDataRequest as live did (agent-live-zonein.md §3.2).

What live did is unknown: there are no captures. The 2016-06-29 forum report that NPCs fell through after
re-entering range (agent-live-zonein.md §3.4) suggests live had the same one-time behaviour [I].

---

## 5. Frida verification plan (test copy `<0.695 test copy>\Landmark64.exe` only)

Rules: the test copy only. Detach cleanly and never kill the runner with hooks attached (memory: frida-detach-crash).
Addresses are for an image base of 0x140000000; rebase with `Module.findBaseAddress('Landmark64.exe')`.

| # | Hook | Where | Log |
|---|---|---|---|
| H1 | **FUN_1430ac390** | onEnter, rcx = entity | • guid `u64 [e+0x2B0]`, +0x4E50, +0x4E9D, +0x4F00 (bytes)<br>• actor: call entity vfunc +0x58 (`[ [e]+0x58 ]`, `pointer(pointer)`; it is a getter)<br>• for the actor: `i32 [a+0x2B4]`, `i32 [a+0x190]`, `p [a+0x188]`, LOD entry = `[[a+0x188] + idx*8]` and `[entry+0x10]`, `p [a+0x348]`, `u8 [a+0x42D]`, `p [a+0x388]`<br>• `this.returnAddress`: 1430aabc0 is a tail call, so expect an address inside FUN_143d0e060; FUN_1430aab50 for the 0x70 path; FUN_1430ab000 for the 0x7F path |
| H2 | **FUN_142b7d510** | onEnter | rdx (obj), r8d (group), return address. The FUN_1430ac390 / FUN_1430a82a0 ranges mean a prop was registered. Match obj to H1 by time and thread |
| H3 | FUN_143b02960 | onEnter | rdx = name `char*` (the actor's model name): Spire props show their model names. Return address |
| H4 | FUN_142cde1c0 | onEnter / onLeave | `u64 [rdx+0x18]` guid, `u32 [rdx+0x64]` model id, time. Return value (0 = ignored or not initialised) |
| H5 | FUN_142d60340 | onEnter | time and the entity count `i32 [[client+0x31E60]+0x48]` (client = rcx) before the wipe; onLeave count after (expect 1) |
| H6 | FUN_142d5e590 | onLeave | log only when the return value changes to 1 (world ready, drop) |
| H7 | FUN_143d0b3d0 | for actors seen in H1 / H3 only | xmm1 (the distance metric) on entry, `[a+0x2B4]` on leave |
| H8 | FUN_142ce6340 | onEnter | 0x7F arrivals (for change 2) |

**Runs** (391 → 395 Spire, then an in-place re-zone onto the deck):
1. **Baseline cross-landscape arrival.** Expect H5 count → 1, then H4 ×7.
   - The decisive line is H1 for each Spire guid, called once, from the 0x88 path:
     - `idx == count` → G1
     - `entry+0x10 == 0` → G2
     - `[a+0x348] == 0` → model data
   - Expect no H2 for those guids.
   - Note the H6 time relative to the H1 lines.
2. **In-place re-zone with the props re-sent after the flags** (the build that stood). Expect H1 to pass the gates and
   H2 with group 2. This confirms the wiping and the working path.
3. **Change 2 only.** Send the 0x7F nudge after FinishRezone. Expect H8, then H1 from FUN_1430ab000 passing the
   gates, then H2.
   - Client reaction: the player's own position updates stay at deck height (y ≈ 1287.7 on 395), not at 1266.8.
   - Proof is the client's own reaction (memory: verify-client-results).
4. **Change 3 only** (props → preload(true) → ZoneDone). Expect H6 to go ready only after the 7 H1 lines. Check
   whether those H1 lines pass the gates.
5. If run 1 shows G1, log the render-view position once in H7. That is `[[ [a+8] or FUN_142d784e0 ]+0x60]+0x20` →
   +0x40..+0x4C, per FUN_143cffe20. It shows where the camera sat when the latch fired.

---

## 6. Open leads to add (trace/0695/OPEN-LEADS.md; not written by this agent)
- Readers of entity +0x4E5C / +0x4E60 (packet +0x480 / +0x488): none found in decomp_all_695 by offset grep. Next:
  a vfunc getter in the ProxiedProp vtable (slots read: 0x150, 0x410, 0x5A0, 0x5A8 are others), or Frida on writes.
- Whether FUN_143d06d90 waits for the LOD mesh before FUN_143d0b3d0 sets +0x2B4 (it decides G2): read FUN_143d06d90,
  or settle it with run 1.
- The meaning of entity flag 0x28 (vfunc 0x268 / FUN_142ec5f80, packet +0x29A bit 4) and of +0x298 bit 6
  (spawn +0x598).
- Where the render view sits during the loading screen after ClientBeginZoning (decides G1): §5 run 5.
