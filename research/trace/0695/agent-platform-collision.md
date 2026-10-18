# Magical Platform collision on the 0.695 client (agent "platform-collision", 2026-10-18)

The question: the server's Magical Platform (pack 5: platform prop 1193 = model 1019, docks 1192 = model 1018) floats
4 m up on a build site. A player teleported (`tp … nosnap`) 1.5 m above its centre falls through. In physics hook r71
the platform ends with `eoSP aoS?` and the docks with `aoSP`.

This is a read-only static trace of `re/decomp_all_695`, the 0.695 exe (`<0.695 test copy>\Landmark64.exe`, through
vtslots/capstone), the asset files, and the r71 log. Nothing was run in the game.
- [P] = read in the code or data (function + offset given).
- [I] = inferred.

---

## 0. Verdict

1. **The entity CollisionObject at ProxiedProp +0x4BE8 can never hold a player. This is by design.** It is a box with
   AppPhysics collision group `0x1000004` and mask `7` (terrain | structure | static_object). The player's character
   controller filters both ways: the shape's mask must contain the player's group 8, and it does not. So the box is
   invisible to the player [P]. It exists only so that queries can hit a prop whose model body is dynamic (see §2.3).
2. **The body meant to hold players is the model's own physics body (`aoS?`).** The platform's model
   `Theme_Movers_MovingPlatforms_Fantasy01_Platform.adr` names `CollisionData = …Platform.apx` (a PhysX
   DynamicSystemAsset: one 10 kg convex body, 4 × 0.34 × 4 m) and `CollisionType = dynamic_object` [P]. So the client
   builds an AppPhysics **dynamic** actor, not a static `.cdt` CollisionObject. The dock's `.adr` names a `.cdt` and
   `static_object` [P].
3. **ProxiedProp +0x4E58 turns that dynamic body into a rider platform.** The client then makes the body massless
   (no gravity), sets group 4 / mask 0x301F8 (blocks players), and turns on transform tracking on its motion state
   (FUN_1430ac390, tail at +0x2CE…+0x334) [P]. +0x4E58 is set by exactly one thing: the **SlidingFriction** area
   property (type 26). It comes from **static area 14**, which the server already sends in the platform's
   AddLightweightProp (+0x260) [P code, P area_trace.log for the spawn field].
4. **Why the r71 player fell [I, strong; one control test settles it].** The test arrived by `tp`, and every tp is a
   re-zone. The re-zone deletes all props, and the platform's bodies were rebuilt only **2.6–2.9 s after
   ClientBeginZoning** (r71: z=1 2861 ms, z=2 2623 ms). The docks took the same 2.5–2.7 s. Outside the Spire, nothing
   holds the arrival (`MiscHandler.ClientIsReady` → `FinishZoneIn` at once), so a player placed in the air falls before
   any prop under them has a body. This is the Spire fall-through again (prop-collision-rules memory), on every floating
   prop. A tp 1.5 m above a **dock** should fall too; that is the control test.
5. **Riding while it moves** needs nothing new on the wire:
   - While the prop's motion state has a non-zero linear velocity, the client rebuilds the prop's collision **every
     frame** (FUN_1430ab000 +0x3F0…+0x411) [P]. The ≤ 1 Hz figure in agent-e-leads.md §1 point 7 / agent-props2.md §2
     is only the +0x4F00 path, which runs after a 0x7F with zero velocity.
   - The kinematic body is tracked by its motion state [I], and PhysX 3.x's character controller carries a controller
     standing on a moving kinematic actor [I, engine behaviour].

**Exact server change** (details in §5):
- (a) Hold re-zone arrivals that land in the air over placed props, as the Spire hold does: send the props first, then
  the zone-done flags after about 4 s.
- (b) Keep the platform's spawn fields exactly as now: static area 14, a non-sentinel area guid, the real prop guid,
  and the collidable bit.
- (c) Keep the 0x7F motion with the real velocity while moving and zero when stopped.
- No new packet, flag or message type is needed. 0x72 / 0xC7 / UpdateLocation do not touch these fields [P].

---

## 1. Collision groups, masks and the two-way filter [P]

**CollisionTypeManager** (ctor FUN_143cd8980, global DAT_145430ff8):
- Each type is registered as (name, group bit, mask) and looked up by `oaat(name)`
  (FUN_143cd9060: map at +0x10, node +0x38 key, +0x30 group, +0x34 mask).

| type | oaat | group | mask |
|---|---|---|---|
| terrain | 0x0F012DBF | 0x1 | 0x301DE |
| structure | 0xEDC05F26 | 0x2 | 0x301DD |
| **static_object** (default when the model names none) | 0xF58BE1CC | **0x4** | **0x301FF** |
| **player** | 0x6643B8AE | **0x8** | **0x34FDF** |
| character | 0x170E4746 | 0x10 | 0x34E5F |
| **dynamic_object** | 0x6A1F33AA | **0x80** | **0x30FFB** |
| invisible_wall / ghost | 0xF8288386 / 0x3A3AA5CA | 0x400 / 0x800 | 0x301F8 |
| client_area / zone_area | | 0x4000 | 0x78 / 0x48 |

(Full list in FUN_143cd8980.)

**The local player's controller.** FUN_142e69e50 (part_0072 ~35208) uses `player` (8 / 0x34FDF) for the local player,
or `character` with group 8 for others.

FUN_142bb99c0 (PhysXCharacter) writes the controller's query filter data at char +0x308:
- word0 = group (+0x258)
- word2 = mask (+0x25C) & ~0x10

The move call passes `{u32 +0x25C, PxFilterData* +0x308, filter callback +0x168, flags 7}` to `PxController` vfunc +0x18
(MyCharacter move FUN_142bbb7e0, part_0057 ~16942; also FUN_142bb9090). This is the PhysX 3.2 `PxControllerFilters` layout [I for the SDK version].

**The controller's preFilter, MyCharacter sub-object +0x168 (vtable 0x144692C40) slot 2 = FUN_142bbb700:**
```
getQueryFilterData(shape) -> {g = word0, m = word2}         // +0x1A call [rax+0x118], result at rsp+0x20 / rsp+0x28
if (g & 0x10) return BLOCK(2);                                // characters always block
if ((m & cct.word0) && (cct.word2 & g)) return BLOCK(2);      // two-way group/mask test
return NONE(0);
```
Shapes get `word0 = group`, `word2 = mask` from their owner (FUN_142be04b0 via pep vfunc +0x78/+0x80 FUN_142be1770 /
FUN_142be1710, group = owner vfunc +0x90, mask = owner vfunc +0x80). They get zeros when pep +0x50 bit 1 (the
"collides" bit, FUN_142be1820) is clear.

Applied to the player (g = 8, m = 0x34FCF):

| body | group / mask | blocks player? |
|---|---|---|
| dock model body (`.cdt`, static_object) | 4 / 0x301FF | yes |
| platform entity box +0x4BE8 | 0x1000004 / 7 | **no** (7 & 8 = 0) |
| entity box when the area is a trigger | 0x2000 / 0x2000 | no |
| platform model body, default (`dynamic_object`) | 0x80 / 0x30FFB | yes, but it is a 10 kg dynamic body under gravity (§2.2) |
| platform model body after the +0x4E58 conversion | 4 / 0x301F8, mass 0 | **yes**, and it stays where the actor is |

---

## 2. What each body is, and what decides it

### 2.1 Model collision: static vs dynamic [P]

FUN_143d08400 is Actor vtable +0x188, "create collision":
- Gates: actor +0x42F bit 0x10 and def +0xD40.
- `lVar5 = asset->vfunc 0x60()` gets the static (`.cdt`) data. If it is present, FUN_143d0c110 builds a static
  `AppPhysics::CollisionObject` (vtable 0x144916E88) with the collision type of the model (def +0xB88; 0 → static_object).
  **The docks take this path:** r71 shows `actorSetCollData group 1` → `aoSP`.
- If `lVar5 == 0`:
  1. `asset->vfunc 0x68()` gives the dynamic-system data. The engine's factory `*(DAT_14541e9b8+0x88)` vfunc +0x28
     builds an AppPhysics dynamic actor (call at +0x37D, 0x143D0877D).
  2. SetGroup (vfunc +0x98) and SetMask (vfunc +0x88) apply the model's collision type (0x143D08890 / 0x143D088A1). If
     actor +0x42E bit 0 is set, both are first `& 0xFFFFFE07`.
  3. The actor is stored at actor +0x380 / +0x388.
  4. If actor +0x370 ≠ 0 and the body is dynamic (+0x555, 0x143D08955), it also gets mask & ~7, SetMass(0),
     `FUN_142b8aba0(obj,5,1)` and actor +0x42F |= 4.
- **The platform takes this path.** r71 shows `actorCreateColl` with no `actorSetCollData` → `aoS?`. The hook prints `?`
  because the object is not a CollisionObject, so it cannot see its PhysX actor (that is at body +0xE0, not pep +0x58).

The asset files [P]:
- Platform `.adr` (Assets_248.pack): `<CollisionType type="1780429738"/>` (= 0x6A1F33AA dynamic_object),
  `<CollisionData fileName="Theme_Movers_MovingPlatforms_Fantasy01_Platform.apx"/>`.
- The `.apx` (<0.695 test copy>\CommonData\Collision): `DynamicSystemAssetParam`, `ModelRagdoll`, one bone
  "Fantasy01_Platform", `Mass 10`, convex hull ±2 × ±0.17 × ±2 m.
- Dock `.adr`: `<CollisionType type="4119585228"/>` (static_object), `<CollisionData …Dock.cdt/>`.
- The Metal01 and Translucent01 (Glass) platforms also have an `.apx` [P, file list]. So every moving platform model is
  built this way.

The two kinds are told apart by the vfunc +0x170 "IsDynamic" [P]:
- `CollisionObject` = FUN_143B03300 → 0.
- `AppPhysicsActor` and every dynamic-system class (vtables 0x14468D4F0, 0x144693CD0, 0x144694AA0, 0x1446950C8,
  0x144694040) = FUN_142B881B0 → 1.

### 2.2 The ProxiedProp build: FUN_1430ac390 [P]

Gates: entity +0x4E50 (collision enabled), the actor and its LOD loaded. Then:
- **If** +0x4E9D == 0 (before the first actor tick), **or** the model body's IsDynamic == 0 → FUN_1430a82a0 re-adds the
  model body (the docks and every static prop).
- **Else** (a dynamic model after the first tick, i.e. the platform):
  1. Delete the old box (+0x4BE0 shape, +0x4BE8 object via FUN_142e67330).
  2. Build a box from the actor's bounds (actor vfunc +0x128 → FUN_143b054e0 / FUN_143b055b0).
  3. `CollisionObject(name, shape, group 0x1000004, mask 7, …, owner type 0x10, owner id actor +0x334)` (FUN_143b02960
     at +0x296). It becomes 0x2000 / 0x2000 if FUN_142f86d40 says the prop's area (entity +0x240) is a trigger
     (+0x1E4…+0x1ED).
  4. World add (FUN_142b7d510, +0x2C9) with scene group **2 → static actor**, or **0xC → kinematic dynamic actor**
     while the interpolator +0x4D98 is active. The bit math is in addStaticActor FUN_142be07c0: param_6 bit 1 → static,
     bit 2 → kinematic.
  5. **Tail, only if `byte +0x4E58 != 0`** (0x1430AC65E) and the model body has a motion state (body +0x60):
     - `body->SetMass(0)`: vfunc +0xB8 → FUN_142bc9260 → PhysX actor (+0xE0) vfunc +0xB0(2, 1) = disable gravity
       [P call; I enum].
     - `body->SetGroup(4)`: vfunc +0x98 → +0xC0 (static_object bit) [P].
     - `body->SetMask(0x301F8)`: vfunc +0x88 → +0xC4. That is the player, character, hi-res character, vehicle, debris,
       projectile and the 0x10000 / 0x20000 bits, but not terrain / structure / static [P].
     - `motionState->vfunc 0xB0(1)` (DefaultMotionState FUN_142b8d880 creates a 0xC0 tracker FUN_142b8c790 at +0x30 that
       keeps previous and current transforms) [P; I that this drives the kinematic body from the actor transform].
     - `FUN_142b8aba0(body, 0xB, 1)`: actor flag bit 11 [P; meaning ?].
  6. In all cases +0x4F00 = 0 and +0x4F08 = now.

r71 matches the else branch exactly:
- `actorLoaded` → `reAddEntPhys` (+0x343, before the tick).
- First tick FUN_143d0e060+0xE2E → `wAdd kind=entity group 2` + `eoSP`.
- No later builds while stopped.

The log cannot show whether the tail ran: +0x4E58 and the body's +0xC0 / +0xC4 are not logged. See the Frida plan in §6.

### 2.3 Why the box exists at all [I]

Its mask 7 meets only terrain, structure and static objects. Its extra group bit 0x1000000 is the bit asked for by
queries such as the ledge-grab probe FUN_142e65a10 (raycast flags 0x1008000). That probe also explicitly skips
+0x4E58 props: owner type 0x10 → entity → AsProxiedProp → +0x4E58 → skip.

So the box is a query proxy for placement, picking and ledge tests on props whose real body is dynamic. It is not
player collision.

### 2.4 Where +0x4E58 comes from [P]

The only writer is **FUN_142fe1c80** (`mov byte [rax+0x4E58],1` at 0x142FE1CB4). Its only caller is
**FUN_142d54040** (SetupLightweightNpc), area loop, case type **0x1A (26 SlidingFriction)**:
- `prop[0xD] = pkt+0x270` (group key)
- `prop[0xE] = pkt+0x18` (prop guid)
- then FUN_142fe1c80 looks the guid up in the ProxiedCharacterManager (DAT_1454319c0, FUN_142f85390 hash at +0x50) →
  AsProxiedProp (vfunc +0x108) → +0x4E58 = 1.

The ctor clears it (FUN_1430a7760: `param_1[0x9cb] = 0` as a u16, which also clears +0x4E59).

The loop runs only when:
- pkt **+0x260** (static area id) is found in the client's static area table (client +0x37910; GlobalAreas id **14** =
  SlidingFriction 1/1/1 + type 28 for 1193 / 1421), and
- ClientAreaDataManagerClient (DAT_1454319d0, vtable 0x14471CB48) vfunc +0x28 = FUN_143d67420 returns an area for the
  guid at pkt **+0x268**. It returns none when its property list (vfunc +0xB0) is empty.

Our spawn has these fields (Props.Movers.cs `BindMover`: StaticAreaId 14, AreaGuid `0x4D41…|guid`, GroupGuid
`0x4D47…|guid`). They arrived in the client: re/test/area_trace.log has `spawn-area guid 20001ab area 14 areaGuid
4d410000020001ab` [P]. Whether FUN_142fe1c80 then ran is not logged anywhere yet (§6 H1).

Nothing else feeds this path. +0x4E58 has no other writer, and +0x4F00 is written only by the 0x7F handler
(part_0065 ~21271) and cleared by FUN_1430ac390 [P, grep]. So PlayerUpdate 0x72, ManagedPosition 0xC7,
UpdateLocation and the spawn flag bytes play no part in platform collision.

### 2.5 Moving: what re-registers collision [P]

ProxiedProp::Update **FUN_1430ab000** (vtable +0x4C8). After the base update FUN_142eb1950, once the actor is ready:
- **Motion state present** (entity +0x180 = `param_1[0x30]`, a DefaultMotionState; vfunc +0x38 = FUN_142b8da00
  returns the linear velocity at +0x130; vfunc +0x40 sets it), with the interpolator +0x4D70 idle:
  - `|v|² ≥ 1e-5` (DAT_144c4102c) → FUN_1430ac390 **every frame** (call at +0x40C, return +0x411).
  - r71 z=0 shows exactly this: `buildEntColl ra=FUN_1430ab000+0x411` every ~10 ms while the platform was moving.
- **Interpolator active** (SimpleAnimation): the transform comes from the interpolator → motion state vfunc +0x18 →
  FUN_1430a82a0 every frame.
- **+0x4F00 set by a 0x7F** and the last build is ≥ 1000 ms old (+0x4F08) → FUN_1430ac390 once.

So a 0x7F stream with a real velocity (Props.Platforms.cs already sends leg direction × speed) re-registers the body
every frame. The `≤ 1 Hz` limit applies only to zero-velocity updates (stops, a newcomer's two snapshots). The base
update copies the entity velocity (entity +0x1F0) into the motion state (FUN_142eb1950 ~line 689, motion state vfunc
+0x40) [P for the call; I that the 0x7F velocity is what lands in +0x1F0 for a prop].

---

## 3. Answers to the three questions

**Q1. What the entity object is, and why it is used.**
- What it is: an AppPhysics CollisionObject box from the model bounds. Static actor (scene group 2), or kinematic (0xC)
  while the SimpleAnimation interpolator runs. Group 0x1000004, mask 7 (or 0x2000 / 0x2000 as a trigger).
- It is not a trigger by default, but the controller's two-way filter excludes it. The player can never stand on it.
- Why the platform has one: its model collision is the `.apx` dynamic system (`dynamic_object`), so the model body is
  an AppPhysicsActor with IsDynamic = 1. FUN_1430ac390's else branch then adds the box as a query proxy.
- What decides it: the model's `.adr` (`CollisionData` .apx vs .cdt, `CollisionType`). Neither PropDefs, nor a packet
  flag, nor a "moving prop" class decides it.
- What makes the real body walkable: +0x4E58, from area 14's SlidingFriction property.

**Q2. What the client needs from the server.**
- AddLightweightProp for the platform, as now:
  - flag byte +0x299 bit 0x08 (collidable)
  - **+0x260 = 14**
  - +0x268 = a non-sentinel area guid
  - +0x270 = group key
  - +0x18 = the prop guid, which FUN_142fe1c80 looks up
- Collision bodies exist only once the model has loaded and ticked: about 1.0–1.3 s after AddLightweightProp and
  2.5–2.9 s after a re-zone (r71). Arrivals in the air must therefore be held.
- Motion: 0x7F with a non-zero velocity while moving, zero when stopped. Sending nothing while stopped is fine either
  way: it does not change whether the platform blocks.
- No 0x72 / 0xC7 / UpdateLocation, and no different spawn packet or area type.

**Q3.** The Frida plan is in §6. It settles two things statically open: (i) that +0x4E58 is really 1 on our platform,
and (ii) the fall timing.

---

## 4. Evidence from r71 (re-read)

| z | prop | addLW | model built | body in PhysX | state |
|---|---|---|---|---|---|
| 1 | platform 20001cf | 1612 ms | actorCreateColl 2830 (dynamic, no SetCollData) | wAdd entity group 2 at **2861 ms** | `eoSP aoS?` |
| 1 | dock 20001d0 | 1612 ms | actorSetCollData group 1 at 2683 | model re-added group 2 at 2689 / 2696 | `aoSP` |
| 2 (the tp) | platform | 1656 ms | 2615 | entity box at **2623 ms** | `eoSP aoS?` |
| 2 | dock | 1656 ms | 2552 | 2557 / 2565 | `aoSP` |

The pxDis / pxAddFail counters are 0. The bodies do exist; they come too late for an arrival placed in the air.

---

## 5. Server change (exact)

1. **Hold airborne re-zone arrivals over props**, as already done for the Spire. Change
   `LandmarkEmulator.WorldServer/Network/Handlers/MiscHandler.cs`, `ClientIsReady`, re-zone branch, next to the
   `Game.Spire.Covers` block:
   - **Condition:** the arrival point is more than ~0.5 m above the terrain height there, **and** a placed prop of the
     session's zone lies within ~4 m horizontally with its position 0–6 m below the arrival.
     Equivalent: any arrival marked `nosnap` / not snapped.
   - **Action:**
     - send that landscape's props now, as `Spire.Enter` does for the Spire. Duplicates sent again later by
       `Reintroduce` are ignored by the client (FUN_142f8a800) [P];
     - then `Game.Teleport.After(session, PropBodyMs, () => FinishZoneIn(session))` with `PropBodyMs ≈ 4000`.
       r71 measured 2.55–2.90 s after ClientBeginZoning; the Spire measured 2.7–4.4 s.
   - This fixes every floating prop, not only platforms. The hold-the-flags mechanism is proven for the Spire
     (agent-prop-collision.md).
2. **Platform spawn: no change, but never drop these fields.** `BindMover` must keep giving 1193 / 1421 static area 14,
   the `MoverAreaTag | guid` area guid (≠ 0) and the group key. Collidable stays 1.
   - Without area 14 there is no +0x4E58 [P].
   - The platform body would then stay a 10 kg gravity-driven `dynamic_object` [P: no SetMass(0) unless actor +0x370].
     Expected result: it drops away from the visual platform [I].
3. **Motion: no change.** 0x7F with the leg velocity (non-zero) every 500 ms while moving, velocity 0 at stops, and the
   newcomer's double snapshot. Do not add 0x72 / 0xC7 / UpdateLocation for props.
4. **Test procedure:** a `tp` onto anything floating is a re-zone. Until (1) is in, test the rider without a re-zone:
   - place the pack at ground level, walk onto a dock, step onto the stopped platform, and ride one leg;
   - or tp beside it on the ground, wait 5 s, and jump on.
5. **Notes to correct** (coordinator; this agent wrote only this file):
   - movers-695 / agent-e-leads.md §1 pt 7 and agent-props2.md §2 say "collision follows at most once per second".
     It is every frame while the velocity ≠ 0 (§2.5).
   - physics-hooks "aoS?" means "AppPhysics dynamic actor"; its PhysX actor is at body +0xE0.

---

## 6. Frida plan (read-only; test copy <0.695 test copy> only)

The hooks are additions to `re/hooks/physics/hook_prop_physics.js` (use its `hook(name, ga, sig, h)` helper and
`G()` rebase). Every address is a Ghidra VA (base 0x140000000). The signature bytes below are read from the test copy
exe. Mid-function hooks are placed only on instructions of 6 bytes or more, and none of them is a branch target.

| id | address | first 8 bytes (sig) | read |
|---|---|---|---|
| H1 | 0x142FE1CB4 (FUN_142fe1c80+0x34, `mov byte [rax+0x4e58],1`) | `c6 80 58 4e 00 00 01 48` | `rax` = entity: guid `rax+0x70` (u64), tid `rax+0x1464`. Fires only when the SlidingFriction property found the prop. Also hook FUN_142fe1c80 entry 0x142FE1C80 `48 83 ec 28 48 8b 41 70`: `args[0]+0x70` guid, `+0x68` group key (so a lookup miss is visible). |
| H2 | 0x1430AC65E (FUN_1430ac390+0x2CE, `cmp byte [r15+0x4e58],0`) | `41 80 bf 58 4e 00 00 00` | `r15` entity: guid +0x70, u8 +0x4E58, u32 +0x4D98, box = ptr +0x4BE8 → u32 +0xC8 group, +0xCC mask, pep = ptr +0xE8 → u8 +0x50 (bit 1 = filter on), ptr +0x58. `r12` = model body: ptr, vtable (u64 at +0), u32 +0xC0 group, +0xC4 mask, f32 +0x160 mass, ptr +0xE0 PhysX actor, motion state ptr +0x60 → ptr +0x30 (tracker). Reached only on the dynamic (else) branch. |
| H3 | FUN_142b8aba0 entry 0x142B8ABA0 | `8b c2 4c 8b c9 45 0f b6` | onEnter only when `this.returnAddress == G('0x1430ac6c9')` (the tail's last call). Log `args[0]` body: +0xC0, +0xC4, +0x160, +0xE0, motion state +0x60 → +0x30. Expect 4 / 0x301F8 / 0.0 / non-null / non-null. Return at once for any other caller: this function is generic. |
| H4 | 0x143D08955 (FUN_143d08400+0x555, `cmp dword [r15+0x370],0`) | `41 83 bf 70 03 00 00 00` | `r15` actor: u32 +0x370, u8 +0x42E, u8 +0x42F, model body = ptr +0x388 − 0x18 → vtable, +0xC0, +0xC4, +0xE0. Entity = ptr +0x340 − 0x250 → guid +0x70. Expect group 0x80 / mask 0x30FFB before the tail. |
| H5 | 0x142BBB720 (character controller preFilter FUN_142bbb700+0x20, `mov eax,[rsp+0x20]`) | `8b 44 24 20 a8 10 74 0e` | shape g = `[rsp+0x20]`, m = `[rsp+0x28]`; controller cg = `[rdi]`, cm = `[rdi+8]`. Verdict `= (g&0x10) \|\| ((m&cg) && (cm&g)) ? 2 : 0` (the function's own logic, §1). **Log only** if `(g & 0x1000000) \|\| m == 0x301f8 \|\| g == 0x80`. Deduplicate on (g, m, cg, cm, verdict) and send a count every 250 ms. This is a hot path, so keep the callback to reads plus a counter. |
| state | the existing 2 s audit (`AUDIT_ALL`) | | add `plat` = u8 entity +0x4E58 and, for the model body when its vtable ≠ CollisionObject: `dg`/`dm` = +0xC0/+0xC4, `ms` = f32 +0x160, `px` = +0xE0 non-null. |

**Run order** (coordinator; one landmark64.exe; never kill or time out the runner while hooks are attached):
1. Start the dev world and the 0.695 test client; log in with re/dlogin.sh (<0.695 test copy>). Close any other client.
2. `python "<workspace>\re\hooks\physics\run_prop_physics.py" "<workspace>\re\hooks\physics\logs\r73.jsonl" --config "{\"AUDIT_ALL\":true,\"PER_ENTITY_CAP\":300}"`
   in the background. Wait for `ready: N hooks, 0 skipped`. If H1–H5 are skipped, stop: the build differs.
3. Visits, about 20 s each. Note the wall-clock time of each command (the server log has the tp time and the client's
   0x441 → 0x41 states):
   - **A (control):** `tp <1.5 m above dock 0x20001D0> nosnap`. Does the player fall?
   - **B:** `tp <1.5 m above the platform centre> nosnap` (the r71 case).
   - **C:** a fresh Magical Platform pack placed at ground level, no re-zone. Walk onto a dock, step onto the stopped
     platform, then stay on through one moving leg.
   - **D (optional):** the platform at 4 m. tp beside a dock on the ground, wait 10 s, use the Spire-style hold if it
     is built by then, or skip.
4. **Close the game first** (Escape menu → Exit, or end the client normally). The runner detaches by itself when the
   process exits. Do not create the `.stop` file and do not kill the runner while the game still runs with hooks
   attached (memory frida-detach-crash). Then delete leftover `frida-*` temp folders (up.sh does this).
5. `python summarize_prop_physics.py logs/r73.jsonl --all --guid 20001cf --timeline` (with PYTHONIOENCODING=utf-8),
   plus `grep` for the new event names.

**Reading the result:**

| Observation | Meaning | Server action |
|---|---|---|
| H1 fires for the platform in every z; H2 `plat=1`; H3 shows 4 / 0x301F8 / mass 0 | conversion works [expected] | (1) arrival hold only |
| A and B both fall; the H5 platform / dock shapes first appear after the fall time | timing (§0.4) | (1) |
| C: the player stands; H5 shows `g=4 m=0x301f8 verdict 2` and box `g=0x1000004 m=7 verdict 0` | proves §1 / §2 | none more |
| H1 never fires, but `spawn-area 14` arrives | area creation or lookup failed (FUN_143d67420 returned none, or the guid lookup missed) | trace FUN_143d67420 with the area guid; check that the area guid is unique per spawn after a re-zone |
| H2 `plat=0`, H4 `+0x370=0`, model body mass 10 | the body is a free-falling dynamic body | make sure static area 14 is on the spawn |
| C: the player falls through a stopped platform, but H5 never sees `0x301f8` near the player | the kinematic body is not at the visual spot | read the body's motion-state transform (motion state +0x60 → its transform fields / tracker +0x30) instead of calling PhysX, compare with the entity position, and open a new lead |

---

## 6a. Open leads to add (first pass; coordinator decides)

- **L135a:** confirm +0x4E58 = 1 and the conversion at runtime (H1–H3).
- **L135b:** the generic airborne-arrival hold (§5.1). This applies to every floating prop, not only platforms.
- **L135c:** whether the rider is carried by PhysX's controller on the tracked kinematic body (C run). If not, trace
  FUN_142b8c790 (the 0xC0 tracker) and the DynamicSystemActor motion-state update.
- **Bit meanings still open (?):**
  - FUN_142b8aba0 bit 11 (tail) and bit 5 (the actor +0x370 path);
  - actor +0x370 and actor +0x42E bit 0 (the `& 0xFFFFFE07` group/mask clear);
  - FUN_143d6ba10 in the trigger check FUN_142f86d40.

---

## 7. Follow-up 2026-10-18: why the owner's client ignores the platform's 0x7F updates

Runtime (coordinator, rounds 75 / 76):
- With the arrival hold (97b5728), the player stands on a stopped platform. So §0.4 is confirmed.
- Platform 0x20001D3 was switched on. The server logged legs 202.3 ↔ 207.7 at 1.6 m/s to "1 viewer(s)", but the pad
  never moved on the client.

### 7.1 Verdict [P]

**The viewer is the platform's owner, and the client puts every prop it owns into its "locally managed" set. The 0x7F
handler drops position updates for anything in that set.**

1. `BaseClient::HandlePlayerUpdateAddLightweightProp` **FUN_142cde1c0** (part_0065, decompile ~line 252), right after
   SetupLightweightNpc:
   ```
   localPlayerGuid = (mgr+0x282c8)->+0x278 handle ->vfunc 0x28()
   if (localPlayerGuid == pkt+0x2A0 /* prop part: OwnerGuid */)
       FUN_142c98c00(client, entity guid);
   ```
   +0x2A0 is the first field of the prop part, `u64 OwnerGuid`. That compare is the only reader of +0x2A0 in the
   handler and in SetupLightweightNpc (grep of both decompiles).
2. **FUN_142c98c00** = "take local management":
   - inserts the guid into the hash set at client **+0x32F40** (buckets at +0x32F68, key node +0x18, next +0x20) via
     FUN_142c0e220;
   - calls entity +0x10 vfunc +0xF0(1);
   - calls FUN_142ec58a0(entity, 1), which sets **entity +0x1457 bit 0x04** (locally simulated);
   - adjusts the mass of the model body if it is not 1.0.
   PlayerUpdate ManagedObject 0x4D (handler FUN_142cdd450) does the same when "new manager" is the local player.
3. **PlayerUpdatePosition 0x7F** = **FUN_142ce6340**:
   ```
   e = FUN_142f85340(mgr, pkt+0xA8 /* transient id */)       // table mgr+0x20100, key entity +0x2F0
   if (e) {
       guid = e+0x278 ->vfunc 0x28()
       for (n = client[0x32F68 + ((hi^lo)&3)*8]; n; n = n+0x20)
           if (n+0x18 == guid) return 0;                         // <<< locally managed: update dropped
       ... apply body (vfunc +0xD0 = thunk FUN_143500500, shared with ProxiedCharacter), FUN_142ec5900(e, pkt+0xB0),
       ProxiedProp +0x4F00 = 1
   }
   ```
   The other position handler **FUN_142cdd5c0** has the same set test (part_0065 ~15537) [P].
   The base update FUN_142eb1950 (~line 626) also takes a different, client-simulated path when +0x1457 & 4 is set.
4. The server sends OwnerGuid = the placing player's guid for every placed prop:
   - `Props.Create` sets `Spawn.OwnerGuid = owner.PlayerGuid` (Game/Props.cs ~875–884);
   - `PlayerUpdateAddLightweightProp.Write` writes it as the first u64 of the prop part.
   So on the owner's client the platform (and every one of the owner's props) is locally managed, and the 0x7F stream
   is discarded at the first test.
   - Nothing on the client moves a type-28 moving object (agent-e-leads §1), so the pad stays at its spawn spot.
   - The client sent no movement either: it has no mover for it.

The other suspects were ruled out:
- **Transient-id lookup:** passes. The prop is in the +0x20100 / +0x2F0 table: in r71 LightweightToFullProp found
  ptid 132234 through the same FUN_142f85340.
- **Lightweight flag +0xCB4:** not read by the handler.
- **Flag bytes +0x298..+0x29A and the spawn-flags u32:** none of them is read by FUN_142ce6340, and none reaches the
  set.
- **Body format:** passes. A type-0 body skips the "state +0x68 must match" merge branch (it runs only when the body
  type byte ≠ 0). The bytes are the creatures' `Creatures.MovementBody`, which move on the same client.
- **The 4 trailing floats:** they are just FUN_142ec5900(e, pkt+0xB0).

### 7.2 Server change (exact)

Make sure no viewer's client holds a server-moved platform in its locally managed set. There are two ways:

**(A) Preferred, one field.**
- In the AddLightweightProp of each platform part that the server moves (1193 / 1421: the `MovingPlatform.Platform`
  prop), write prop part **+0x2A0 `u64 OwnerGuid` = 0**, or any guid that is not a player.
- Keep `PlacedProp.OwnerGuid` (ownership, permissions, DB) unchanged; change only the spawn copy. For example, in
  `BindPlatform` set `platform.Spawn.OwnerGuid = 0`, or zero it in the writer when the prop is a moving platform.
- Layout: prop part = `u64 OwnerGuid` (+0x2A0), then `u8 1` (+0x2B0 body type), `u32` (+0x2B4), `u8` (+0x318),
  `u32 flags` (+0x2B8), `u32` list (+0x340), `u32` (+0x480), `u64` (+0x488), `bool` (+0x490). This is
  PlayerUpdateAddLightweightProp.cs's last block, unchanged except the first value.
- The client reads +0x2A0 only for this compare, so docks and other props can keep the real owner.

**(B) Equivalent, if the spawn must keep the owner.**
- After the AddLightweightProp, send to the owner **PlayerUpdate ManagedObject 0x4D** `{u64 object = platform guid,
  u64 oldManager = owner's player guid, u64 newManager = 0}`.
- Handler FUN_142cdd450: oldManager == the local player → **FUN_142d3b580** removes the guid from the set and calls
  entity +0x10 vfunc +0xF0(0).
- This must be re-sent after every spawn, including each re-zone.
- The reader is FUN_142c29910. The field order is from the handler's locals: +0x38 object, +0x30 old, +0x28 new [P].
  The wire order is not verified here [?].

Unchanged: the 0x7F stream (type-0 body with the leg velocity, 500 ms) and the double snapshot for newcomers.

**Verify by the client's reaction** (rule verify-client-results):
- The pad moves on the owner's screen.
- r7x physics hook: `buildEntColl ra=FUN_1430ab000+0x411` every frame while the platform moves (§2.5).
- A second account (TestPlayer2) should already see it move before the fix [I: only the owner's client has the guid in
  the set]. This is a cheap confirmation of the cause.

### 7.3 Note for other props [I]

Every placed prop is locally managed on its owner's client. This sets entity +0x1457 bit 4, and the base update
treats such entities as client-simulated. So any other server-driven motion of a player's own props is also dropped
on the owner's client: `propslide`, and probably the SimpleAnimation position path if it goes through 0x7F (it uses
0xFD, so it should not). Whether live sent OwnerGuid in prop spawns at all is unknown: there are no live
AddLightweightProp captures (agent-live-zonein.md). Lead: decide per feature, or test whether sending 0 for all props
changes anything visible (prop menus read ownership from server data, not +0x2A0 [P for the handler; other readers of
entity +0x1457 bit 4 not traced]).

---

## 8. Follow-up 2026-10-18 (round 81): every gate between a 0x7F record and a moving pad

Runtime (coordinator, round 81, test copy):
- The build has spawn OwnerGuid 0 and control mode 2 (packet +0x19C → entity +0x41A0).
- The server sends 0x7F type-0 records every 500 ms while the pad moves (Props.Platforms.cs → Creatures.MovementBody →
  MovementFanout).
- The pad (guid 0x20001D7) still looks static in the screenshots.
- After the mode change, the player standing on the pad started sending about 33 movement updates per second. Before
  it sent none.

This section re-reads the whole path statically in `re/decomp_all_695` and the test exe (vtable slots, constants and
call-site return addresses were read with capstone/pefile from `<0.695 test copy>\Landmark64.exe`). Nothing was run.
§8.4 adds a read-only Frida hook that settles the runtime values.

### 8.1 Verdict [P for the gates, I where marked]

**No static gate blocks a pad that was spawned with mode 2 and owner 0.** Every condition between the 0x7F handler
and the motion-state SetTransform passes on its static values:
- the ProxiedProp vtable slots return what is needed;
- the record format matches what creatures receive;
- the history window is 1 s;
- the throttle constant (DAT_145208b31) is 1.

What is left are **five runtime values**. They cannot be read statically, and the hook in §8.4 logs each of them:

1. **Did the entity really get +0x41A0 = 2?**
   - Only the spawn sets it: FUN_142d54040, and later only the vehicle code and a featherweight spawn (FUN_142d50250).
   - An entity spawned before the build keeps mode 0 until it respawns (re-zone or relog).
2. **The per-entity flags read before the apply:** +0xF4, +0x80 (parent guid), +0x1640 (controller), the state mask
   at +0xA20, and +0x1470. All of them should be 0 for a prop [I], but none is verified.
3. **The model actor's motion state.**
   - The apply writes the entity's ProxiedObjectMotionState (entity +0x180).
   - The visible model follows only if the actor's own motion state (actor +0x2D8, set by actor vfunc +0x100 =
     FUN_143d0cf70) is that object, or if the handler FUN_1430aac30 → FUN_143501530 finds the actor through entity
     +0x240.
   - It was not found statically where a ProxiedProp's actor gets its motion state (the one direct +0x100 call,
     FUN_142ec0000, is projectile/ragdoll code).
4. **The record clock** (§8.3). It cannot stop the pad by itself, but it decides how smooth the motion is.
5. **The rider's 33 updates/s [I].**
   - A rider sends this way when its ground object is a moving entity: FUN_142ecc7f0 adds flag 0x10000000 and the
     platform transient id at +0x80 (§2.3 of agent-e3-platform.md).
   - That hints that the pad's **physics body** now follows the motion state: FUN_1430aac30 → FUN_1430ac0d0 /
     FUN_1430ac390 rebuild the collision from it.
   - If so, the part that is still missing is the **visual actor**.
   - Cheap server-side check: log those 33/s bodies. If their positions travel along the pad's leg, or carry flag
     0x10000000 with the pad's transient id, the client-side motion state moves, and the gap is actor-only.

Most likely blocker, in order:
- (a) The pad entity was not respawned after the mode change, or its spawn did not carry mode 2. Check `spawn.entMode`
  in the hook log.
- (b) The actor does not follow the entity motion state (point 3).
- (c) A non-zero +0x1640, +0x80 or +0xF4.

### 8.2 The path, gate by gate [P]

**A. 0x7F handler FUN_142ce6340** (client, packet). Packet +0xA8 = transient id, +0xA0 → body (MotionRecord layout:
+0 type), +0xB0 = 4 floats.

| # | gate | needs for the pad |
|---|---|---|
| A1 | `FUN_142f85340(client+0x31E60, tid)`: buckets mgr +0x20100 (tid % 0x1003), key entity +0x2F0, next +0x2F8; tid ≠ DAT_145446e2c | the server's `Spawn.TransientId` is the 0x7F id. No entity → returns 1 silently. |
| A2 | guid = entity +0x70 (sub +0x278 vfunc +0x28 = FUN_142ea0620 reads +0x70 via -0x208); not in client +0x32F68 (4 buckets, (hi^lo)&3, key +0x18, next +0x20) | spawn owner ≠ local player (§7), and no 0x4D taking management |
| A3 | only if body type ≠ 0: vfunc +0xF8 GetLatest and record +0x68 equal, else return 0 | type 0 skips this |
| A4 | `+0x1684 = clock(client+0x32BE0 +0x3C + timeGetTime) − record +4` (record age, ms) | no gate. It sets the render delay (§8.3). |
| A5 | type 0 or 2: FUN_142ec8550 → FUN_142d567c0: pos = (pos·10 + entity origin +0x48B0 (3 doubles, set only by PlayerUpdate 0x63)) − client origin +0x31FC0) · 0.1 | the same frame that creatures use. Props and creatures both keep the default origin. |
| A6 | vfunc +0xD0 = **thunk FUN_142e91020 → FUN_143500500**: `FUN_142b8e070(record)` must hold, then +0xD8 FUN_142e9c760 and **ms +0x70 = FUN_1435a4430 push** | record +8 bit 0 set (server 0x401 / 0x441: yes); pos, rot, +0x30, vel, angular and +0x70 finite; \|+0x60\|, \|+0x64\| below DAT_14541d224 (runtime; the server sends 0) |
| A7 | FUN_142ec5900(pkt +0xB0): scale | only rescales the actor if it differs |
| A8 | vfunc +0x108 (FUN_1430a8d90 returns the entity) → **+0x4F00 = 1** | collision rebuild pending (≤ 1 per second) |

**B. History push FUN_1435a4430** (ProxiedObjectMotionState, vtable 0x1446D78E0; created for every proxied entity
by FUN_1434ffa70 → FUN_142e8a1b0 with window **ms +0x178 = 1.0 s**):
- No push at all while ms +0x178 ≤ 0. It is 1.0.
- The history is reset to the new record when the new time < the newest time, or when +0x68 differs.
- Gaps > 0.15 s (DAT_14520886C) get a bridging record.
- Records older than the window are dropped, but at least 2 are kept.
- Deque: ms +0x190 blocks, +0x1C8 count, +0x1CC start; index 0 = newest.

**C. Per frame.** The entity list loop FUN_142f87230 calls vfunc +0x4C8 for every entity every frame:
`(dt, mgr+0x281B8, mgr+0x281BC)`. For a ProxiedProp that is FUN_1430ab000, and its first call is
**FUN_142eb1950(e, dt, 1, lod)**.

| # | line (decompile) | gate | needs |
|---|---|---|---|
| C1 | ~357 | +0x145C & 0x10 → FUN_142e97c80, return | clear |
| C2 | ~545-567 | +0x48AC = max(+0x1684·0.001, 0.15). Render time **t = clock − delay·1000**. If +0x145F & 0x40 is clear: return while t − +0xCB8 < 0, and vfunc +0x158 FUN_142ec6e20 (which sets 0x40) == 0 → DestroyProxiedCharacter | runs each frame until FUN_142ec6e20 sets 0x40 [I: once]; +0xCB8 has no entity writer [P for the grep] |
| C3 | ~569 | vfunc +0x58 FUN_142e9d9f0: actor by +0x1EB0 in DAT_145431080, else return | the actor loaded |
| C4 | ~619-651 | **+0xF4 ≠ 0** && vfunc +0x108 ≠ 0 (always for props) && !(+0x1457 & 4) && +0x1688 == 0 → vfunc +0xF8, then **return** unless +0x1688 got set | +0xF4 == 0 (the NetworkObject constructor writes 0; agent-e3-platform.md §2.0) |
| C5 | ~661 | `allowSkip` && vfunc **+0x4C0 FUN_142e94050** == 1 → **throttled path**. Lightweight (+0xCB4) or beyond-detail (+0xCB5) entities skip frames until accumulated dt ≥ lod ms (mgr +0x281BC); others are throttled by actor LOD | either path can apply (C6/C7) |
| C6 | ~664-753 throttled | DAT_145208B31 ≠ 0 (exe: 1) && **FUN_142e93340** (ra 0x142EB2171) && sub(+0x10) vfunc +0xF8 (FUN_142ea9fd0 = 0) == 0 && (+0xF4 == 0 \|\| …) && sub vfunc +0x1F8 (FUN_142e9de00 = **entity +0x80**) == DAT_145441368 (runtime "no guid", 0 in the image) && **vfunc +0xE0** (ra 0x142EB21E6) && FUN_142b8e070(+0x1B0); then vfunc +0x130 (FUN_142e9dd50 = 0) == 0 && sub vfunc +0xD0 (FUN_1430a8e90 = 0) == 0 → ms SetVelocity / SetAngular / **SetTransform** | same values as C8 |
| C7 | ~812 | normal path skipped entirely when +0x1457 & 8 | only the local player sets bit 8 (FUN_1433c80a0) |
| C8 | ~825-836 | **mode == 2** && FUN_142e93340 (ra 0x142EB2794) → vfunc +0x360 FUN_142e98e90: client NPC movement controller (+0x16E0, FUN_142e69e50 / FUN_142e6d420). Mode ≠ 2 → FUN_142e72be0 resets it | runs before the apply, so the apply overrides it in the same frame |
| C9 | ~947-958 | sub vfunc +0xF8 == 0; (+0xF4 == 0 \|\| C4 passed \|\| +0x1457 & 4); **entity +0x80 == DAT_145441368**; ms +0xF0 GetLatest (FUN_1435a4f70: false when the history is empty) | parent guid 0 |
| C10 | ~959-963 | **FUN_142e93340** (ra 0x142EB2C28) **or latest record flags bit 17** | +0x41A0 ≠ 0 (any non-zero value; 2 also enables C8), `!(+0xA20 & DAT_1455FEDB8)`, +0x1470 == 0. Server flags 0x401/0x441 have bit 17 clear. |
| C11 | ~963 | **vfunc +0xE0 FUN_142ea8bb0** (ra 0x142EB2C5A) → ms +0xD8 **GetStateAtTime FUN_1435a53e0**(t, out = entity +0x1B0): false only when the history is empty; otherwise it interpolates between the bracketing records (it extrapolates a little past the newest and then holds). Then it applies flag bits 4/5/7/9-0x16/0x19 to the entity. | history count ≥ 1 |
| C12 | ~965 | DAT_145441276 == 0 && FUN_142b8e070(entity +0x1B0) | the debug byte is 0 in the image |
| C13 | ~1006 | vfunc +0x130 (0 for props) == 0 → (actor/animation block skipped for props: local_8c5 set) | — |
| C14 | ~1381-1412 | **+0x1640 == 0** (else FUN_143b049f0 controller path) && sub vfunc +0xD0 == 0 → ms **SetVelocity +0x40**, **SetAngular +0x50**, **SetTransform +0x28** (FUN_142b8efc0: ms +0x100 pos, +0x110 rot, +0x120 scale; it notifies the handlers via FUN_142b8f040) | +0x1640 == 0 |
| C15 | — | ms handlers: ProxiedProp's handler at entity +8 → **FUN_1430aac30** → FUN_143501530 (object by entity +0x240 in DAT_1454319D0, vfunc +0x178 with the new transform) and FUN_1430ac0d0 (collision box from the actor's transform). The actor registers itself as a handler when its +0x2D8 is the motion state (FUN_143d0cf70) | §8.1 point 3 |
| else | ~1463 | any of C9-C12 fails → FUN_142ec58a0(e, 0) | — |

**D. What does not gate** [P]:

| suspect | why it does not gate |
|---|---|
| "Is NPC" / entity type | ProxiedProp runs the same FUN_142eb1950. Its prop-only differences are vtable slots that return constants: +0x130 = 0, +0x108 = self, sub +0xF8 = 0, sub +0xD0 = 0. |
| Interpolation component | Every proxied entity gets a ProxiedObjectMotionState with a 1 s window (FUN_1434ffa70). |
| +0x41A0 values | Any non-zero value passes FUN_142e93340. Only 2 also runs the C8 controller. 0 queues the records without playing them (round 79). |
| Lightweight / full (+0xCB4) | Only the C5 throttle; the throttled path also applies. LightweightToFull does not touch +0x41A0 (its writers are listed in C2/§8.1). |
| "Dynamic" flag (+0x4E9C, from packet +0x490) and +0x4E9D | Collision only (FUN_1430ac390); not read on this path. |
| Spawn flag bytes +0x298..+0x29A | Not read on this path. |

### 8.3 Record time vs the client clock [P]

What the server writes:
- `Creatures.MovementBody` writes `u32 ServerClock.Milliseconds` (milliseconds since the server started) right after
  the type byte. That becomes record +4.
- Creatures, pets, PlayerManager and the platform all use the same builder, so the pad's time is exactly what
  creatures send.

What the client does with it:
- The client's clock is `client+0x32BE0 → +0x3C` (the offset learned from Synchronization 0xC3; the server answers
  with the same ServerClock, MiscHandler.cs ~406) plus `timeGetTime()` (FUN_142cd2690).
- Age at arrival = entity +0x1684.
- Delay = max(age, 0.15 s).
- Render time = clock − delay. GetStateAtTime then works within the last ≥ 1 s of records.

What a wrong clock does:
- A wrong offset of either sign only shifts or flattens the playback. Late records play back late; early records hold
  the oldest record ± the extrapolation clamp.
- It cannot make the pad perfectly static while records keep arriving 500 ms apart with new positions. A pad with a
  bad clock would still step every 500 ms.
- So the time is **not** the blocker. The hook still logs `age1684`, the render times and the history times.

### 8.4 Runtime check: hook_platform.js (read-only)

Files:
- `re/hooks/platform/hook_platform.js`: 17 targets, each prologue checked against the test exe (all 17 match).
- `re/hooks/platform/summarize_platform.py`
- `re/hooks/platform/README.md`

Hooks:

| hook | what it logs |
|---|---|
| AddLightweightProp FUN_142cde1c0 (+ SpawnProxiedProp FUN_142f8a800) | the pad's spawn: pkt +0x19C mode, +0x2A0 owner, transient id, entity +0x41A0 |
| 0x7F FUN_142ce6340 | the transient id lookup and the locally-managed test, recomputed. Branch: noEntity / localSet / typeMismatch / noThunk / notPushed / pushed. Also the wire record and its validity (FUN_142b8e070 recomputed), the converted position (FUN_143500500 args), the history before and after, and +0x1684. |
| ms push FUN_1435a4430 | pushes into the pad history, from the 0x7F or from elsewhere |
| FUN_142eb1950 and FUN_142e93340 | **hot**: attached only while the pad is bound. The gate vector of C1-C14 with the first failing gate (`why`), and FUN_142e93340 per call site (thr / mode2 / norm). |
| vfunc +0xE0 FUN_142ea8bb0 | path, result, render time, and the entity +0x1B0 record written |
| FUN_142e98e90 (mode-2 controller), FUN_1430aac30 (ms handler: transform changed), FUN_1430ac390 (collision build) | per-second counts |
| setLocal / unsetLocal / destroy / entClear / beginZoning / actor loaded (FUN_1430aab50) | lifetime events; the actor's motion state compared with the entity's |

How to run it:
- Attach before the pad spawns (re-zone or relog after attaching), so that `spawn` and `actorLoaded` are seen.
- Run from `re/hooks/platform`:
  ```
  python ../recipes/run_hook.py ../platform/hook_platform.js logs/r82.jsonl --pid N --config '{"PAD":"0x20001D7"}'
  python summarize_platform.py logs/r82.jsonl
  ```
  PowerShell 5.1 needs the inner quotes escaped: `'{\"PAD\":\"0x20001D7\"}'`.
- **Close the game before stopping a runner with hooks attached.** For an early stop use the `.stop` file or
  `--stop-after`; never kill the runner.

How to read the result. `summarize_platform.py` prints one line per pad 0x7F record (branch, validity, push, history,
age, played), the gate changes, the per-second frames (applies, handler calls, metres the motion state moved), and a
verdict:
- **`moved` > 0 with the model static on screen:** the actor does not follow the motion state (§8.1 point 3).
  Check `audit.actorMsIsEntityMs` and `actorHandle`.
- **`why` ≠ pass:** the named gate is the blocker.
- **`spawn.entMode` = 0:** the server's mode-2 spawn did not reach this entity.

### 8.5 Leads (open, not written off)

- **L-pad-actor:** how a ProxiedProp's model actor gets its motion state (actor +0x2D8). The writers seen so far are
  actor vfunc +0x100 FUN_143d0cf70, called directly only by FUN_142ec0000 (projectile physics), and the entity +0x240
  handle path FUN_143501530. If the round-82 hook shows `moved` > 0 with a static model, trace the actor creation of
  ProxiedProp (vfunc +0x58 by +0x1EB0, DAT_145431080) next.
- **L-pad-rider-bodies:** server-side, log the rider's 33/s movement bodies (flag 0x10000000, +0x80 platform transient
  id, +0x70 offset). This tells whether the client already treats the pad as moving.

---

## 9. Follow-up 2026-10-18 (round 87): the client manages every prop itself; release it with ManagedObject 0x4D

Runtime (coordinator, r87, `re/hooks/platform/logs/r87.jsonl`):
- Pad 0x20001A5 (tid 39529, def 1019) spawned with mode 2 and owner 0.
- `setLocal` came from FUN_1430aafd0+0x2A at each spawn; `unsetLocal` from FUN_142e8c2b0+0x15F at each re-zone.
- All 68 0x7F records took the `localSet` branch and were dropped. State: `f4=1 localSet=1 hist=0`.

### 9.1 What FUN_1430aafd0 is: every new prop is locally managed, unconditionally [P]

FUN_1430aafd0 does one thing: it reads the entity's guid (the object at entity+0x278, its vfunc 0x28) and passes it
to FUN_142c98c00 on the client object (DAT_1454311c8), which adds the guid to the locally-managed set.

- Its only caller is SpawnProxiedProp **FUN_142f8a800**, on the new-entity branch.
- That branch runs when no live entity has the guid, after the init `thunk_FUN_142ec6e20(e, spawn, 1)` succeeds and
  after the entity is linked into the guid / tid / list tables (decompile lines ~150-198).
- There is no test before the call: no model, no collision type, no flag byte, no owner, no mode, no def id.
- **So no spawn field and no model choice avoids it.** Every ProxiedProp, static or not, starts in the client's
  locally-managed set on every viewer. This is not related to the `.apx` / dynamic_object body.
- The "spawn owner 0" change (§7) only removed a second, redundant add: the owner compare in FUN_142cde1c0, and its
  twin in FUN_142cde8b0.
- [I] EQN-engine props are managed objects that each client simulates by default. A server that wants authority over
  one takes it with ManagedObject 0x4D, as the vehicle code does (`+0x4D20` manager in the handler).

**What "managed" changes on the entity** (FUN_142c98c00) [P]:
- The guid goes into the set at client +0x32F40 (buckets +0x32F68).
- `entity+0x10 ->vfunc 0xF0(1)` = **FUN_143b67730**. It writes sub +0xE4, which is **entity +0xF4**, the `f4`
  in the hook (1 = managed). It also clears bit 13 in the flags of the physics object at sub +0x138 (FUN_142b8aba0(obj,
  0xD, !v)).
- FUN_142ec58a0(e,1) sets **entity +0x1457 bit 0x04**.
- The entity physics object (entity +0x148 − 0x18) gets vfunc +0xB8 when its vfunc +0xC0 ≠ 1.0. That is a reset of
  the gravity factor (§9.3).

**Effects that stop the pad** [P]:
- **0x7F handler FUN_142ce6340:** a guid in the set means `return 0` before the record is pushed. That is the r87
  `localSet` branch, and why the history stays 0.
- **Base update FUN_142eb1950 ~line 619-651 (§8 C4):** for a ProxiedProp with **+0xF4 ≠ 0** && !(+0x1457 & 4) &&
  +0x1688 == 0, it calls vfunc +0xF8 and **returns** (via the +0xAF0 counter / FUN_142eb6a10 spawn-pending work).
  The interpolated apply C9-C14 is never reached.
- The `+0xF4` in §8.1 point 2 and the `+0xF4 == 0` row in §8.2 C4 are this same flag. +0xF4 is the managed flag, not a
  network-object constant.

**Other adders of the set** (`fn.py -x 142c98c00`) [P]:
- 0x4D with new manager = local player.
- AddLightweightProp / FUN_142cde8b0 when the owner is the local player.
- The vehicle seat path inside thunk_FUN_142ec6e20 (spawn +0x398 ≠ 0).
- The character state changes FUN_142eabe30 / FUN_142eade40.
- The tool FUN_143688e30 (~line 273, a multi-prop editing tool).
- **Nothing on actor load or per frame.**

**Removers** [P]:
- **FUN_142d3b580** from the 0x4D handler, entity destroy FUN_142e8c2b0 (r87's `unsetLocal`), and the character state
  changes. It removes the guid (FUN_142d2d710) and calls `entity+0x10 ->vfunc 0xF0(0)`, which sets +0xF4 = 0 and sets
  the physics bit 13.
- It does not clear +0x1457 bit 4. With +0xF4 = 0 that bit no longer gates the apply: C9 is `+0xF4 == 0 || …`.
  If C9-C12 ever fail, FUN_142eb1950 clears it itself at ~line 1466 (FUN_142ec58a0(e,0)).

### 9.2 PlayerUpdate ManagedObject 0x4D: wire layout and handler [P]

- Dispatcher FUN_142cdfa60 case 0x4D → **FUN_142cdd450**(client, data, len). It builds `PlayerUpdatePacketManagedObject`
  (base 0x26, sub 0x4D) and reads it with **FUN_142c29910** from the start of the message.
- The handler acts only if the reader set no error **and consumed every byte** (`end − cursor < 1`). It must be
  exactly 30 bytes with no tail.

| wire offset | type | packet field | meaning in FUN_142cdd450 |
|---|---|---|---|
| 0 | u16 | +0x08 | 0x0026 (PlayerUpdate base) |
| 2 | u32 | +0x10 | 0x0000004D (sub, as u32: `26 00 4D 00 00 00`, what `WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PlayerUpdateManagedObject = 0x26004D00)` already emits) |
| 6 | u64 | +0x18 | **object guid**. Ignored if it equals the "no guid" sentinel DAT_145430c58. |
| 14 | u64 | +0x20 | **old manager**. If it equals the local player's guid (player +0x278 vfunc +0x28 = the player's character guid), FUN_142d3b580(object) runs: removed from the set, +0xF4 = 0. |
| 22 | u64 | +0x28 | **new manager**. If it equals the local player's guid, FUN_142c98c00(object) runs: re-added. Then, if the entity has vfunc +0x130 ≠ 0 (vehicles; 0 for props), it is stored at +0x4D20. |

**Values that release the pad on one viewer:**
- `object = pad guid`
- `old = that viewer's own PlayerGuid`
- `new = 0` (anything that is not the viewer)

Each viewer needs its own copy, because "old" is compared with the receiving client's player. FUN_142d3b580 works on
the guid even without an entity, but it must run after the add, so the order matters:
- **When:** in the same send burst, directly after the pad's AddLightweightProp. The add happens synchronously inside
  that packet's handler (SpawnProxiedProp → FUN_1430aafd0), and packets are handled in order.
- Nothing re-adds on actor load or model load (adders list above). Waiting for the actor is unnecessary.
- **Re-send after every new spawn of the pad on that client:**
  - each re-zone (entities destroyed → re-created → re-added), the ClientIsReady prop send, and Reintroduce;
  - a relog;
  - a pick-up / move that despawns and respawns the pad;
  - after FUN_143688e30's multi-prop tool (only an editing player).
- A duplicate AddLightweightProp for a guid that is still alive is ignored before FUN_1430aafd0 (SpawnProxiedProp
  returns NULL), so it needs no 0x4D. An extra 0x4D is harmless: the remove is a no-op when the guid is absent.

### 9.3 After the release: does the body follow the 0x7F positions? [P path, I where marked]

1. **0x7F records are pushed.** FUN_142ce6340 passes the set test → vfunc +0xD0 → FUN_143500500 → history push
   (§8 B), and +0x4F00 = 1.
2. **Per frame with +0xF4 = 0,** C4 is skipped. C9-C14 interpolate the history at render time and call the motion
   state SetVelocity / SetAngular / SetTransform. This needs +0x41A0 ≠ 0 (the mode 2 already sent) and history ≥ 1.
   The motion-state handlers then fire: FUN_1430aac30 → FUN_143501530 / FUN_1430ac0d0.
3. **Collision:**
   - While the pad's velocity is non-zero, FUN_1430ab000 rebuilds the collision every frame (§2.5).
   - Each rebuild re-applies the +0x4E58 tail (SlidingFriction from static area 14).
4. **Gravity: it will not take the slab** [P with one naming correction]:
   - vfunc +0xB8 (FUN_142bc9260 / FUN_142bc91f0) is a **gravity factor**, not a mass. 0 → PhysX actor (+0xE0)
     `vfunc +0xB0(2, 1)` (disable gravity); 1.0 (DAT_144c410f0) → `+0xB0(2)` (gravity on); other values → a
     custom-gravity list (FUN_142b839c0).
   - The +0x4E58 tail sets 0 on the model's `.apx` body every rebuild, so the 10 kg body has no gravity whether or not
     it is managed.
   - The managed-set add touches only the entity physics object (entity +0x148), not this body.
   - Read "SetMass(0)" in §2.2 / §0.3 as "gravity factor 0".
   - **If static area 14 were missing**, the tail would not run and the body would keep gravity [I: it would fall away
     from the visual].
5. **The model body following the actor** is the tail's motion tracker (§2.2 [I]). Whether the **visual actor**
   follows the entity motion state is still lead L-pad-actor (§8.5). The release removes the gate in front of it, so
   the next run answers it directly: `moved` > 0 with or without a moving model.
6. **Physics bit 13:** the release also sets it in the entity physics object (FUN_143b67730). Meaning [?]. With 0 =
   managed and 1 = server-driven, it reads like "remote / don't simulate" [I].

### 9.4 Exact server change

1. **New message** `PlayerUpdateManagedObject` (`ZoneMessageOpcode.PlayerUpdateManagedObject` 0x26004D00 already
   exists): `u64 ObjectGuid, u64 OldManager, u64 NewManager`, with nothing after. The opcode writer adds the
   `26 00 4D 00 00 00` header, so the full message is 30 bytes.
2. **Platforms only** (Props.Platforms: the moving part, 1193 / 1421; also any other prop the server moves, e.g.
   `propslide`):
   - immediately after each AddLightweightProp of that prop to a session (every path: placement broadcast, pack
     expansion, re-zone prop send at ClientIsReady / Spire-style hold, Reintroduce, relog, move settle),
   - enqueue `ManagedObject { ObjectGuid = prop.Spawn.Guid, OldManager = session.PlayerGuid, NewManager = 0 }` to
     that same session.
   - Static props do not need it: being client-managed changes nothing visible for a prop that never moves.
3. **Keep** spawn mode 2 (+0x19C), spawn owner 0 (harmless; it avoids the owner add), static area 14, the collidable
   bit, and the 0x7F stream with the real velocity.

**Verify by the client's reaction** with re/hooks/platform:
- `unsetLocal` with ra in **FUN_142cdd450** (→ FUN_142d3b580) right after each `spawn`;
- state `f4=0 localSet=0`;
- 0x7F branch `pushed`, and `hist` > 0;
- the gate vector passes C9-C14, and `moved` > 0;
- then the screenshot: the pad travels 202.3 ↔ 207.7.
- If `moved` > 0 but the model stays still, the remaining gap is L-pad-actor (§8.5).
