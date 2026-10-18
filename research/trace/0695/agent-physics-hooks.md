# Agent "physics-hooks": when does a prop get a physics body (0.695 client)

Question: why do props sent after a cross-landscape re-zone never become collidable (players fall through the Spire
deck)? This note lists the client functions that create / add / remove prop physics, the ready-to-run read-only Frida
hooks in `re/hooks/physics/`, and the test procedure. Nothing here has been run yet. [P] = read in the 0.695
decompile (`re/decomp_all_695`), [I] = inferred.

Files:
- `<workspace>\re\hooks\physics\hook_prop_physics.js`: 26 read-only `Interceptor.attach` hooks plus a 2 s state
  audit of Spire props.
- `<workspace>\re\hooks\physics\run_prop_physics.py`: the runner. It writes JSONL and stops cleanly.
- `<workspace>\re\hooks\physics\summarize_prop_physics.py`: reads the log and answers "body or no body, per
  zone visit".

## 1. What the code shows (read before testing)

**The collidable bit [P].** The client syncs every entity's model collision with the entity's "collidable" bit on
every frame:
- The bit is at entity +0x1459, bit 2.
- The base entity update `FUN_142eb1950` calls `FUN_143d0c060(actor, bit)`. It runs only once the actor has loaded
  (`FUN_143d05520`), and is called from ProxiedProp::Update `FUN_1430ab000`, vtable +0x4c8.
- If the bit is 1, the actor's CollisionObject is added to the world (group 1) when it is not in a scene.
- If the bit is 0, it is removed (`FUN_142b7f2d0`).

Who sets the bit (`FUN_142ec2c20`, SetCollidable):
- **AddLightweightProp**: packet flag byte **+0x299, bit 3 (0x08)**, via `FUN_142d54040`
  "BaseClient::SetupLightweightNpc".
- **PlayerUpdate SetCollidable (0x26 / 0x27)**: body u16, u32, **u64 guid, bool** (reader `FUN_142c2b330`, dispatcher
  `FUN_142cdfa60` case 0x26).
- AddFeatherweightObject: always 1.
- LightweightToFullProp (`FUN_142cdf400` → `FUN_142d532f0`) does **not** touch the bit.

What we send:
- Our `PlayerUpdateAddLightweightProp.cs:101` writes the three flag bytes as `00 00 00`. The 0.695 reader
  `FUN_142c26790` stores them in order at +0x298 / +0x299 / +0x29a [P]. So the second wire byte's 0x08 is the
  collidable bit, and **we send it as 0 for every prop**.
- `ZoneMessageOpcode.PlayerUpdateSetCollidable` exists, but nothing sends it.
- Top hypothesis for the hooks to confirm or refute: the model body is added by the build path, then removed by the
  per-frame sync because col = 0. If so, the log shows `wRemove ... ra=FUN_142eb1950/FUN_143d0c060` and states
  `col0 ... aosp`.

**A body can be "in the scene" without a PhysX actor [P].**
- `PhysXScene::Add` (`FUN_142ba2490`) calls PhysX only when the object's scene pointer (+0xb0) is still 0.
- `PhysX::addStaticActor` (`FUN_142be07c0`) returns at once while the byte at `*(0x14541d3f8)+0x58` is non-zero.
- `addStaticActor` writes failures to the client log "PHYSX_log": "Failed to create rigid STATIC / DYNAMIC / collision
  shape for actor: %s".
- The hooks show this case as `Sp` (scene set, no PhysX actor). It is the second candidate for "after a re-zone".

**The physics world is not rebuilt per zone [P].** The world `DAT_14541e9b8` is created once (`FUN_142cacd40`) and
cleared only at shutdown (`FUN_142cbc8a0`). A zone change destroys the entities (and their bodies with them) and
builds new ones from the new zone's packets.

## 2. Function list (Ghidra addresses; rebase `mod.base + (a - 0x140000000)`)

### (a) Create a physics body for an entity / prop model

| Address | What | Args / how to read ids |
|---|---|---|
| `FUN_142cde1c0` | BaseClient::HandlePlayerUpdateAddLightweightProp | (client, pkt). pkt +0x18 guid, +0x20 transient id, +0x48/+0x50 model name (char*, len), +0x58 model id, +0x64 def id, +0x228 pos (3 doubles) + 0xd0 offset (3 floats), **+0x298/+0x299/+0x29a flag bytes**, +0x2a0 owner, +0x490 enabled. Returns 1 = entity made. Sets entity +0xcb4 = 1 (lightweight). |
| `FUN_142f8a800` | ProxiedCharacterManager::SpawnProxiedProp | (mgr = client+0x31e60, &{u64 guid, u32 tid}, u32) → entity. ctor `FUN_1430a7760` (ProxiedProp, 0x4f20 bytes, vftable 0x144703fc8) sets +0x4e50 = 1. |
| `FUN_142d54040` | BaseClient::SetupLightweightNpc | (client, entity, pkt): applies the flag bytes; +0x299 bit 3 → `FUN_142ec2c20`. |
| `FUN_142ec2c20` | entity SetCollidable | (entity, bool): +0x1459 bit 2, then `FUN_143d0c060(actor, bool)`. |
| `FUN_1430a8a00` | ProxiedProp SetCollisionEnabled | (entity, bool): +0x4e50. 1 → `FUN_1430ac390`; 0 → remove the +0x4be8 body, `FUN_143cff670(actor,1)`. |
| `FUN_1430ac390` | ProxiedProp build collision | (entity). Gate +0x4e50 and actor def +0xd40 (collision asset). Calls `FUN_143cff670(actor,0)` (enable). If +0x4e9d is 0, or the physics actor is static → `FUN_1430a82a0` (re-add). Otherwise it builds the entity CollisionObject at **+0x4be8** and world-adds it (group 2, or 0xc). Sets +0x4f00 = 0 and +0x4f08 = time. Called on actor load (listener slot 14 `FUN_1430aab50`), on the first actor tick (slot 17 `FUN_1430aabc0`, sets +0x4e9d), and by Update when +0x4f00 is set and more than 1000 ms have passed. A movement packet (`FUN_142ce6340`) sets +0x4f00. |
| `FUN_1430a82a0` | ProxiedProp re-add physics | (entity): removes and re-adds the model body (`FUN_142ccb9e0(actor)` = actor+0x388−0x18) and the +0x4be8 body, with transform. |
| `FUN_1430aab50` | ProxiedProp actor-listener slot 14 (actor loaded) | (entity+0x250, actor). The actor calls it from `FUN_143d08080` / `FUN_143d08100`. |
| `FUN_143d08400` (Actor), `FUN_143d08a30` (BaseActor) | Actor vtable +0x188, create collision | (actor). Skipped if actor +0x42f bit 4 (disabled) is set, or the model def (actor +0x348) +0xd40 (collision asset) is null. The asset's data → `FUN_143d0c110`. |
| `FUN_143d0c110` | Actor set collision data | (actor, collisionData, a, b, group): new AppPhysics::CollisionObject (`FUN_143b02960`), kept at **actor +0x380/+0x388** (+0x388 = obj+0x18), then world add with the group. |
| `FUN_143cff670` | Actor set collision disabled | (actor, bool): +0x42f bit 4. 1 → destroy (`FUN_143d08d90`); 0 → vtable +0x188. |
| `FUN_143d0c060` | Actor sync collision | (actor, bool): add (group 1) if not in a scene / remove if in one. Called every frame from `FUN_142eb1950`. Not hooked; seen through the world add / remove return addresses. |
| `FUN_142e6cbe0` | character capsule | built from full data when pkt +0x26c / +0x270 > 0 (group 4). It shows up as `wAdd kind=other`. |

`FUN_142cdf400` LightweightToFullProp: (client, pkt +0x18 = u32 transient id) → `FUN_142f85340` lookup
(entity +0x2f0) → `FUN_142d532f0` (client, entity, pkt, 0), then entity +0xcb4 = 0. The full request is sent by
`FUN_142ebdc90` (entity), only when +0xcb6 == 0 and +0xcb4 != 0.

### (b) Add / remove bodies to / from the physics scene

| Address | What | Args |
|---|---|---|
| `FUN_142b7d510` | World add | (world = DAT_14541e9b8, obj, group, scene or 0 = default). Sets obj +0xb0 = scene through vtable +0x1a8. |
| `FUN_142b7f2d0` | World remove | (world, obj): obj +0xb0 → scene remove, then +0xb0 = 0. |
| `FUN_142ba2490` / `FUN_142ba8e50` | PhysXScene add / remove (vtable 0x144690810, slots +0x10 / +0x18) | Calls PhysX only on a scene-pointer transition. |
| `FUN_142be07c0` | PhysX::addStaticActor | (pep = obj+0xe8, scene, f1, f2, appActor = obj, group). On success **pep +0x58 = PxRigidActor**. |
| `FUN_142be1410` | pepCollisionObject release | (pep): releases pep +0x58. |

CollisionObject (vftable 0x144916e88):
- +0x40 owner type (0x10 = model actor) and +0x48 owner id (= actor +0x334).
- +0xb0 scene; +0xe8 pepCollisionObject (+8 = name char*, +0x58 = PhysX actor).

Actor → entity: actor +0x340 = entity + 0x250 (listener). The listener vftable is 0x1447049b0 for ProxiedProp.
Entity → actor goes through a handle (entity +0x1eb0, vtable +0x58), so the hooks learn the actor from its own
callbacks instead. Entity guid = **+0x70** (base ctor `FUN_142e88840`: `param_1[0xe] = guid`). Transient id = +0x1464
(copy at +0x2f0).

### (c) Tile / terrain collision

- `FUN_1437b6c30` "Tile Create Collision": LodTile CollisionObject, world add group 1.
- `FUN_143fabf50` "CreateCollisionMeshes".
- "TileStatic" is only a zone-type name. `FUN_142d52e90` / `FUN_142d531c0` set client +0x31fa8 and Lua "ZoneType"
  (TileStatic 0 / TileSeamless 1 / RuntimeSeamless 2 / Mesh 3 / HeightfieldLod 4 / BlockTerrain 5). It has nothing to
  do with props.

### (d) Zone change teardown

`FUN_142d60340` is the ClientBeginZoning handler: (client, zoneName char*, …, p9 = same-zone flag).
- If p9 == 0, it also tears down zone objects (`FUN_1431589a0`, `FUN_142dc9ee0`, `FUN_1431ffae0`).
- It always calls `FUN_142f83590(mgr, localPlayer)`: every entity except the local player is released (list
  mgr +0x30, count +0x48; maps cleared).
- Then `FUN_1431c3c40`, `FUN_142fb45b0`, `FUN_142d4f410(client,0)`, and finally `FUN_142d58cd0` (zone load:
  "Waiting for Zone to Load...", state cClientRunStateWaitForZoneLoad).
- A single entity is removed by `FUN_142f839d0` ProxiedCharacterManager::DestroyProxiedCharacter (mgr, entity).

## 3. The hooks

Every hook has these properties:
- It is read-only: `Interceptor.attach`, reads only, no calls into game code.
- It checks the function's first 8 bytes against the test copy and skips on a mismatch (listed in the `ready`
  message).
- Detailed events are written only for ProxiedProp entities, capped at 60 per entity per hook; everything else is
  counted.
- Events are batched every 250 ms.

Every event has `fn`, `t` (ms), `z` (zone sequence, +1 per ClientBeginZoning), `dz` (ms since that zone change), and
for props `e`, `guid`, `tid`, and `spire:1` when the guid starts with `535000018b` / `5350000187`.

| fn | from |
|---|---|
| `addLW` | packet guid / tid, flags `f298 f299 f29a`, `colPkt` (+0x299 bit 3), `en490`, model / def ids, name, pos; after: `colEnt`, `snap` |
| `l2f`, `applyFull`, `fullReq` | full-data path, with `before` / `after` state |
| `setCollidable` (`old`, `v`, `ra`), `setCollEnabled`, `setEnabled` | entity flags |
| `actorLoaded`, `actorCreateColl`, `baseActorCreateColl`, `actorSetCollData` (`group`), `actorSetCollDisabled`, `actorDestroyColl` | model actor |
| `buildEntColl`, `reAddEntPhys` | entity collision builders |
| `wAdd` (`kind` model / entity / other, `group`, `before` → `after`, `pxCalled`, `pxDis`, `pxOk`, `ra`), `wRemove` | world |
| `pxAddFail` (any owner, first 50; `name`, `dis`), `pxRemove` | PhysX |
| `beginZoning` (`name`, `same`), `beginZoningDone`, `entClear` (`count`), `zoneLoad`, `destroy` | zone |
| `tileColl`, `collMeshes` | terrain (first 10 per zone, then counted) |
| `state` | every 2 s for Spire props, only when the state changes; also `tag:preZone` right before each zone change |
| `stats` | counters every 5 s when they change (`wAdd.g<group>` for non-prop adds, `pxAdd.disabled`, `pxAdd.fail`, `capped.*`, `err.*`) |

State string (`snap`):
- `lw` lightweight; `col` collidable bit; `en` +0x4e50; `dyn` +0x4e9c; `tick` +0x4e9d; `pend` +0x4f00.
- `eo..` = the entity body at +0x4be8; `ld` actor loaded; `dis` actor collision disabled; `cdt` the model def has a
  collision asset; `ao..` = the model body at actor +0x388.
- Each body reads `-` (none), `S`/`s` (in the AppPhysics scene or not), then `P`/`p` (PhysX actor exists or not), or
  `?` (not a CollisionObject).
- **A prop has a body when `eoSP` or `aoSP` is present.**

## 4. Test procedure (coordinator)

1. Bring the dev world and the 0.695 test client up as usual, and log in (re/dlogin.sh → <0.695 test copy>). Close other
   clients: the runner needs exactly one landmark64.exe, or pass `--pid`.
2. Start the runner in the background and do not kill it:
   `python "<workspace>\re\hooks\physics\run_prop_physics.py" "<workspace>\re\hooks\physics\logs\spire1.jsonl"`
   Wait for `ready: 26 hooks, 0 skipped`. If anything is skipped, the build differs, so stop and re-check the
   addresses.
   Options:
   - `--config "{\"AUDIT_ALL\":true}"` audits every prop, not only the Spire set.
   - `--config "{\"PER_ENTITY_CAP\":300}"` raises the per-entity cap for a long flip-flop.
3. Visits, about 30 s each, standing on or next to the Spire deck. The log has a `z` value per ClientBeginZoning, so
   note the order:
   - A. Cross-landscape arrival on 395: from another landscape, `@TestPlayer landscape 395`. Stay where you arrive
     (the Spire) for about 30 s without a tp: every tp is itself a re-zone (a new `z`, `same=1`), and that would mix
     A with B.
   - B. In-place re-zone on 395: `@TestPlayer tp <x y z> nosnap` away and back onto the deck.
   - C. Cross-landscape to 391: `@TestPlayer landscape 391`, then the deck tp. Then an in-place re-zone on 391 as in B.
   - D. (baseline) Log out and log in directly on 395 at the Spire, if the server allows it.
4. Stop: create the file `<workspace>\re\hooks\physics\logs\spire1.jsonl.stop` (clean detach inside the script),
   or leave the runner until the game exits.
5. `python "<workspace>\re\hooks\physics\summarize_prop_physics.py" "...\spire1.jsonl"` (`--timeline` for
   per-prop event lists, `--all` for every prop). Also look in the client's PHYSX_log for "Failed to create ..."
   lines.

## 5. Reading the result

- `== zones` lists each zone change: `z`, the zone name, and `same` (1 = in-place re-zone on the same zone, 0 =
  different zone).
- `== per zone` counts the Spire props seen and how many have a body, with the tokens that explain the rest.
- What the outcomes mean:

| Outcome | Meaning |
|---|---|
| `colPkt=0` on every `addLW`, `col0`, and `wRemove kind=model` with `ra` in FUN_142eb1950 / FUN_143d0c060 / FUN_142ec2c20 | The collidable bit removes the model body (§1). Server fix candidate: second flag byte \|= 0x08 in PlayerUpdateAddLightweightProp.cs:101, or send SetCollidable 0x26/0x27 (guid, 1) after the prop. Verify by the client's reaction (`col1 … aoSP`, player stands on the deck). |
| `Sp` in `ao` / `eo`, `wAdd … pxCalled:0` with `before:"S…"`, or `pxAddFail` / `pxDis>0` | AppPhysics thinks the body is in a scene but PhysX has none (stale +0xb0, or PhysX add disabled during the load). |
| `ld0` / `a?` | The model actor never loaded (no `actorLoaded`). |
| `cdt0` | The model definition has no collision asset. |
| `dis1` | Actor collision disabled (`actorSetCollDisabled v=1` / `setCollEnabled v=0`; see `ra`). |
| `eo-` with `tick0` | The first actor tick never ran. |

- "When": compare the `dz` of `addLW`, `actorLoaded`, `wAdd`, and the first `state` with `SP` for z-visits A (cross)
  vs B (in-place). If the body appears in B but not in A (or the other way), compare the two timelines event by event.

## 6. Not covered / open

- The client setting `disable_physics_when_voxels_loading` (`FUN_1413e5c70`) and the per-frame Actor update that
  calls listener slot 17 (`FUN_143d0e060`) were not traced further. The hooks see their effects (`pxDis`, `tick`).
- `FUN_143d066d0` (Actor vtable +0x88: adds or removes the body with actor +0x20 bit 1 & arg, group 1) is another
  add / remove source. It is seen only through `ra`.
- Added to `OPEN-LEADS.md`: no. The coordinator decides after the run.
