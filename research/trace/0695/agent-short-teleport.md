# Short teleports without a loading screen: client trace (L116, 2026-10-18)

Read-only trace of the 0.695 client (`re/decomp_all_695`, exe `<0.695 test copy>\Landmark64.exe` read with pefile/capstone
for constants and disassembly). [P] = proven from code (function + offset), [I] = inference.
Inputs: OPEN-LEADS L116 + "ROOT CAUSE FOUND 2026-10-16", `Game/Teleport.cs`, `re/test/teleport_trace.js/.py` and
`teleport_trace.log`, docs/terrain.md "Long teleports", movers-695.md, agent-l140.md (Evac / `/location`).

## Verdict

- **No server-only sequence is proven safe for an arbitrary close-range destination.**
- **The cause is not a missing unlink in the teleport path.** The TeleportToLocation path has no visibility code of its
  own. The player's visibility object is moved by the same per-frame step as walking: SetTransform → the room's dirty
  list → flush → Octree::Update, which is Remove + Insert.
- **The octree is consistent on one thread.** I read it instruction by instruction (Remove, Insert, Update, split,
  merge and root growth): on one thread it cannot leave an item in two lists.
- **So the cycle needs one of two things.**
  - Another thread changes the octree, the dirty list or the per-item query link (+0x48) while the main thread
    flushes or queries. None of these are locked. The DPVS calls right next to them are locked (critical section
    DAT_145651050). [P for the lock placement, I for the race.]
  - Or the corruption comes from somewhere I have not found.
- **One real client bug amplifies the damage once the octree is inconsistent [P].** When Remove's primary lookup misses,
  it unlinks the item from the slot where it found it, but decrements the count of the *other* slot. When it finds
  nothing at all, it silently does nothing. The following Insert then links the item a second time, and the next
  query loops in QuerySimpleVisibility.
- **TeleportToLocation (ClientUpdate 0xC) is the only server packet that moves the local player without zoning.**
  - Live used it for short moves. The packet carries a "teleporter guid + cooldown" pair that the Translocation Pad
    state machine reads.
  - Evac to Safety, `/location`, teleport-to-camera and map teleport all expect it as their answer.
  - The 0x7F Position packet is ignored for the local player.
- **Interim server policy (data-backed, [I]):**
  - Use a plain TeleportToLocation only for moves of 10 m or less, and re-zone anything longer.
  - Evidence: in both test runs, every hop of 3–10 m was fine. Every hang came on the hop *after* a plain hop of 20 m
    or more. If long moves always re-zone, that precondition never happens.
  - Check this with the Frida plan below (§5) before raising the limit.

## 1. How the visibility object moves between cells

### Structure [P]

- **The "visibility cells" are a loose octree** `SoeUtil::Octree<Deep::VisibilityObject*, Box_t<float>, 1, 0>`.
  It lives at `Deep::VisibilityRoom` +0x10; the room's constructor is at part_0167 ~23981, and the octree constructor
  is FUN_1440c62f0.
- **There is no fixed cell size.**
  - Node half-sizes are powers of two. The root half-size starts at **1.0** (DAT_144c410f0) and doubles
    (DAT_144c411a0 = 2.0, FUN_1440ca8f0) until it holds every item. Object translations are clamped to ±8000
    (DAT_14468e2b4/b8, FUN_1440c9eb0), so at y ≈ 1340 the root half-size is 2048.
  - A child node's half-size is half its parent's (DAT_144c41098 = 0.5).
  - A bucket becomes a child node when it holds **more than 16 items** (0x10) and the node's half-size is more than the
    minimum of **1.0** (octree +0x80) (FUN_1440cb6e0).
  - Queries widen each node by min(half-size × 0.5, largest item size). The largest item size is kept at octree +0x84.
  - So cells are cubes with half-sizes of 1, 2, 4, 8, 16 … m, depending on how many objects are around.
- **Octree object** (room +0x10). The root node is embedded at +0x10:

  | Offset | Field |
  |---|---|
  | +0x78 | root half-size |
  | +0x80 | minimum half-size |
  | +0x84 | largest item size |
  | +0x88 | item count |
  | +0x98 | octant offset table: 8 ±1 vectors; index bit 4 = x<c, 2 = y<c, 1 = z<c |

  Its vtable allocates and frees: +0x08 / +0x10 an item, +0x18 / +0x20 a node.
- **Node** (0x70 bytes):

  | Offset | Field |
  |---|---|
  | +0x00 | centre (vec4) |
  | +0x10..+0x48 | 8 slots |
  | +0x50..+0x57 | slot flags |
  | +0x58 | parent |
  | +0x60 | own list: items larger than the node's half-size |
  | +0x68 | half-size |

  A slot flag of 0 means the slot is a child node (or empty). A non-zero flag means the slot is a bucket (an item
  list), and the flag is its item count; 0xFF means "count unknown, recount".
- **Item**: a VisibilityObject's handle is `item+0x38`, and the VisibilityObject keeps that handle at +0x98.

  | Offset | Field |
  |---|---|
  | +0x00 / +0x10 | box min / max |
  | +0x20 | centre (FUN_1440c9b10) |
  | +0x30 | size (largest extent) |
  | +0x38 | VisibilityObject* |
  | +0x40 | next item in its node list |
  | +0x48 | next item in the current query result (one field shared by every query) |
  | +0x50 | owning node: the "cached cell" |

- **VisibilityObject** (vtable 0x1449b5fe8):

  | Offset / method | Meaning |
  |---|---|
  | +0x60 | DPVS object |
  | +0x90 | room |
  | +0x98 | item handle |
  | +0xa0 | visible flag |
  | +0xa4 bit 0 | transform dirty |
  | +0xb0..+0xec | transform |
  | +0xf0 / +0xf8 | prev / next in the room's dirty list |
  | vtable +0x40 | SetRoom (FUN_1440cf060) |
  | vtable +0x58 | SetTransform (FUN_1440ccf60) |
  | vtable +0x60 | Release (FUN_1440caa50) |
  | vtable +0x70 | SetVisible (FUN_1440ca7d0) |

- **Room**: dirty list head at +0x190, tail at +0x198, count at +0x1a0. The visible-output list is at +0x1a8 / +0x1b0.

### Normal movement (per frame) [P]

1. **The actor's transform changes.** The entity's SetPosition FUN_142ec5410 → FUN_143501b10 → the transform
   component's (entity +0x180) vtable +0x28. The render model then calls `VisibilityObject::SetTransform`
   (FUN_1440ccf60), which stores the matrix, sets the +0xa4 dirty bit and appends the object to the room's dirty list.
   It does not append an object that is already queued (the check is `+0xf0 == 0 && room.head != obj`).
   [P for each function; I for the link from the transform component to the render model.]
2. **Each camera flushes the list before its query.** `VisibilityCamera` vtable +0x28 is FUN_1440cd510: when
   camera +0x210 (the room) is set, it calls FUN_1440cf300(room) and then QuerySimpleVisibility (vtable +0xC0,
   FUN_1440cd6d0). VisibilityCameraLite (FUN_1440cd540) and the SoftwareOcclusion camera (FUN_1440d0bd0) run the same
   flush on camera +0x420. For each queued object, the flush:
   - recomputes the world transform into the DPVS object (FUN_1440c9eb0);
   - reads the new box (FUN_1440caee0);
   - calls **Octree::Update** FUN_1440cc4e0(octree, handle, box).
3. **Octree::Update** (disassembly 0x1440cc4e0–0x1440cc688) takes one of two paths:
   - **In place:** when the size is unchanged, the new centre is inside the current node
     (`max|new − node.c| < node.half`) and it falls in the same octant as the old centre, only the box is replaced.
   - **Otherwise:** `Remove` (FUN_1440cba30), then the new box, then `Insert` (FUN_1440cb8a0 → FUN_1440cb6e0).
4. **Remove** (FUN_1440cba30) uses the cached cell:
   - It reads node = item+0x50 and clears it (0x1440cba4c).
   - If size > node half-size, it unlinks the item from the own list. Otherwise it works out the slot from the item's
     *stored* centre and unlinks it from that bucket.
   - If the node still has a parent and no own items, and holds at most one bucket of 16 items or fewer (and no child
     nodes), it frees the node (and any parents left empty) and reinserts that bucket's items into the surviving
     ancestor.
5. **Insert** grows the root if needed, then walks down. The item goes into a bucket, or into a child node's own list
   when it is larger than that child's half-size, and +0x50 is set to the node that receives it. A bucket that passes
   16 items is split.

### TeleportToLocation path [P]

1. The handler FUN_142d59f30 first calls FUN_142d5ce80. That function ends the wait state if one is pending, then calls
   the camera object (client +0x31ec0) through FUN_142d69b20, whose actor hook FUN_142ec0e00 is an empty stub.
2. `LocalProxiedCharacter` vtable +0x418 is FUN_14332b920 (vtable 0x1447533f8; FirstPerson… shares it).
   - It does **not** move the actor. It stores a pending position and rotation at actor +0x4ca0 / +0x4cb0, and the
     physics controller's generation counter (+0x50) at +0x4c94.
   - The base ProxiedCharacter version FUN_142ec54f0 moves the actor directly.
3. The per-frame LocalProxiedCharacter update FUN_143329ed0 (part_0099 ~2681) applies the pending move.
   - Once the generation changes, it calls SetPosition (vtable +0x48 = FUN_142ec5410) and SetRotation (FUN_142ec1650).
   - Once the terrain engine is ready (DAT_145431d68 checks), it also runs vtable +0x618 and clears +0x4c94.
4. From then on, it is the normal per-frame path above: SetTransform → dirty list → flush → Octree::Update.
   **There is no teleport-specific unlink or relink, and no other cell is cached.** item+0x50 is the only one, and
   Remove clears it before every Insert.
5. The rest of the handler:
   - It zeroes the velocity (FUN_142ec6d20, which goes to the physics character at +0x148 and the controller at +0x180).
   - It calls actor vtable +0x2c0(0, 0) and +0x2b0(0), FUN_142ea59e0, and the camera's +0x140.
   - It clears the "teleport pending" bit 8 of actor +0x1457. Evac sets that bit (agent-l140.md).
   - The engine re-centre (FUN_1431589a0) runs only when the destination is not covered (FUN_143155b40), is farther
     than VoxelFarm setting 0x2c55a58 (80 m), or +0x41 is set. That is unchanged from L116.

### Re-zone path [P, from existing traces]

ClientBeginZoning deletes every entity (FUN_142ee4930). Each VisibilityObject is released (vtable +0x60
FUN_1440caa50 → SetRoom(0) → FUN_1440ce050), which unlinks it from the dirty list and removes and frees its item.
Re-added entities are inserted fresh (FUN_1440cb3f0). All of this happens under the loading screen.

### Why a cross-cell teleport can leave an item linked twice

- **Single-threaded, it cannot.** I checked Remove, Insert, Update, split, merge and root growth:
  - every path sets or clears +0x50 consistently;
  - Update always runs Remove with the old box still stored;
  - the in-place path keeps the item in the same node and octant.
- **What the QSV spin shows.** The cycle seen in QSV (`next = node[2]`, i.e. item +0x48) means one item was pushed
  twice in one query. That happens only if it sits in two node lists, or if two lists share a tail (one item's +0x40
  points into another list).
- **The client bug that turns a stale +0x50 into a double link [P]** (FUN_1440cba30, disassembly):
  - When the slot computed from the stored centre (r9) does not hold the item, the fallback scans the other bucket
    slots (r8). When it finds the item there, it unlinks it but reads and writes the count of slot **r9**
    (`movzx eax, byte [r9+rdi+0x50]` at 0x1440cbbac, `mov [r9+rdi+0x50], al` at 0x1440cbbde). The real slot keeps a
    count that is now too high.
  - If slot r9 was a child-node slot (flag 0), the decrement turns it into a "bucket" with count 0xFF, and the child
    node gets walked as an item list.
  - When the item is in none of the buckets, Remove returns without unlinking it. The Insert that follows links it a
    second time, and the result is the QSV cycle.
- **What can start that inconsistency [I]:**
  - The octree, the dirty list and the +0x48 query link have no lock.
  - SetRoom (FUN_1440cf060), SetVisible and Release take the critical section DAT_145651050 only around the DPVS calls.
    Their octree Insert (FUN_1440cb3f0) and Remove (FUN_1440ce050) run **outside** it.
  - A critical section around these suggests the objects are touched from more than one thread.
  - So, likely: another thread (model or terrain-chunk loads finishing after a burst of new content) inserts or removes
    items while the main thread flushes or queries.
  - This fits all three observations:
    - a hop of 20 m or more brings in new terrain cells and props, while walking brings them in slowly;
    - 0.589 re-zones hung only when other packets were in the same burst, which means more objects created at once;
    - a re-zone with nothing else in its burst is safe, because the scene is rebuilt behind the loading screen.
  - The Frida plan in §5 is built to confirm or rule this out.

## 2. Client paths that move the local player without zoning

| Path | How the player moves | Server-triggerable for any close point? |
|---|---|---|
| **TeleportToLocation (ClientUpdate 0xC)**: reader FUN_142b83d70, apply FUN_142d59f30 | Pending move on the actor (§1), then the normal per-frame relink | **Yes, the only one** |
| Translocation Pad (ClientAreaTeleporter FUN_1436db8b0 / state machine FUN_1436db650) | Client sends 0xFC/9 (effect) and 0xFC/10 ManagedRequest; the server answers with TeleportToLocation (`Props.ManagedTeleport` → `Teleport.To`) | Answered by 0xC |
| Evac to Safety (FUN_1433c80a0) / `/location` / teleport-to-camera / map teleport | Client sends CommandPacketTeleportToLocation 0x1E/0x51 (0x52 with heading, 0x3F6 TeleportManaged on a mover). The client freezes itself (actor +0x1457 \|= 8) until a TeleportToLocation clears it (agent-l140.md) [P] | Answered by 0xC |
| Thrower / ChainThrustPad / Flinger (FUN_1436dc4a0, FUN_1436da050) | Client sets the velocity; physics moves the player over many frames | No: needs a prop and gives no exact target |
| Grapple / glide / rails (PlayerUpdate 0x2A-0x2C) | Client physics, rails from a fixed table | No |
| Position 0x7F for the player's own entity | Ignored: FUN_142ce6340 returns for locally controlled entities (client +0x32f68) [P, L116] | No |
| ManagedPosition 0xC7 | Client to server only | No |
| Respawn | Server-driven: either 0xC or ClientBeginZoning | Same as 0xC |

### TeleportToLocation fields

The packet object (reader FUN_142b83d70) is: +0x20 position, +0x30 rotation, +0x40 u8, +0x41 wait screen, +0x42 snap,
+0x48 u64, +0x50 u32.

- **+0x40 u8:** stored at client +0x32560 [P].
  - ClientBeginZoning stores the same field (FUN_142d60340).
  - BroadcastLocalPlayerMovement (FUN_142ca4ce0) copies it into every movement body it sends (`local_1d0` → the low
    byte of `local_260`).
  - [I, strong] It is the movement body's **version byte** (docs/protocol.md 4.3: non-full updates are dropped unless
    the version matches). The server picks a new epoch per teleport, so it can tell pre-teleport movement apart.
    Sending 0 is harmless.
- **+0x48 u64 + +0x50 u32:** when the guid is valid (≠ DAT_145430c58) and the count is above 0, the handler calls
  FUN_143325060(actor, ms, &guid) [P].
  - That function puts guid → (now + u32) into the actor's timed set at +0x4dc0.
  - The Translocation Pad state machine, case 1 (FUN_1436db650), asks FUN_1433286b0(actor, pad guid at callback +0x28)
    and does **not** fire while the pad is in that window.
  - So the pair means **"ignore teleporter <guid> for <u32> ms"**: the destination pad's guid, so the player does not
    bounce straight back [P mechanism; I that the unit is ms].
- **+0x41:** forces the engine re-centre and the wait state (L116). Not useful here: it froze on hop 1.

## 3. Can a server sequence make the plain packet safe?

Not proven, and for the reason above (no teleport-specific relink) there is no field value that "forces a relink".
What each lever does:

- **Field values.** None selects a different visibility path. +0x41 adds a forced re-centre, which is worse. +0x40,
  +0x48 and +0x50 are unrelated to visibility.
- **A following 0x7F or another packet.** It is ignored for the local player, and no other server packet touches the
  room.
- **Letting the client run a frame at the old spot first.** That is already the case: the move is deferred to the
  actor's update.
- **"Only within one cell."** Cells are density-dependent octree nodes (§1). The server cannot know them, and the item
  moves between nodes every few metres when walking anyway.
- **Policy the test data supports [I].** Plain TeleportToLocation only for 10 m or less, with nothing else in the
  burst (already done: `ShortMoveSettle`). Re-zone anything longer.
  - All hops of 3–10 m, in both runs, were fine.
  - All three hangs came on the hop after a plain hop of 20 m or more (20/45 m then 3 m back; 25 m then 25 m back).
  - With longer moves re-zoned, that precondition never happens.
- **Also worth sending, whatever the outcome (live behaviour) [I]:**
  - For pad teleports, the destination pad's area guid (spawn +0x268) at +0x48 and a cooldown of about 2000–3000 ms at
    +0x50.
  - Optionally, a per-player epoch byte at +0x40.

Exact layout, unchanged: `vec4 pos, vec4 rot, u8 version(+0x40), bool waitScreen(+0x41)=0, bool snap(+0x42), u64 guid,
u32 ms`.

## 4. Server change (proposed)

1. `Teleport.LoadingScreenDistance`: test with **10**, and keep the default at 0 until the Frida run below is clean.
   `To()` already re-zones farther moves, holds other packets for 1 s, and allows one re-zone at a time.
2. `Props.ManagedTeleport`: pass the destination pad's area guid and a cooldown to `Teleport.To`, written at
   +0x48 / +0x50 instead of `0ul, 0u`.
3. If §5 shows the race (outcome O1), mark L116 as "client race, re-zone beyond 10 m". If it shows a single-thread
   path instead (O2 or O3), follow the logged operation.

## 5. Frida verification plan

The test copy is `<0.695 test copy>`, image base 0x140000000, and the stamp check is the one in `teleport_trace.js`
(0x586d8017). All hooks are read-only (`Interceptor.attach`).

**Runner rules.**
- Use `teleport_trace.py`'s runner: it never detaches and exits after the game closes.
- **Close the game before stopping a runner that has hooks attached.**
- Server: ShortTeleportDistance 60 and ShortTeleportWaitScreen false. The dev world restarts for the config change.

**Hooks**

| Address | What | Log |
|---|---|---|
| 0x142d59f30 | TeleportToLocation apply | tid; pos; +0x40/41/42; +0x48 u64; +0x50 u32. Mark "hop N" and arm the player-item finder |
| 0x1440cd510 / 0x1440cd6d0 | camera flush + QSV | tid; room = camera+0x210; QSV duration (keep the QSV-STUCK probe) |
| 0x1440cf300, 0x1440cd540, 0x1440d0bd0 | dirty-list flush | tid; room; count (+0x1a0) |
| 0x1440cc4e0 | Octree::Update(octree, handle, box*) | tid; item = handle−0x38; node before (item+0x50); in place or Remove+Insert (a cba30 call inside); node after (item+0x50 on leave) |
| 0x1440cba30 | Octree::Remove(octree, item) | On enter, in JS: node = item+0x50; the slot from item centre +0x20 vs node centre (bits x<c:4, y<c:2, z<c:1) or the own list when size +0x30 > node half +0x68. Walk that list (step cap 10 000). **REMOVE-MISS** if absent: then scan all 8 slots and the own list and report where the item really is, or "nowhere" |
| 0x1440cb8a0 | Octree::Insert(octree, item) | When called from cc4e0, item+0x50 must be 0: **DOUBLE-INSERT** if not |
| 0x1440cb3f0, 0x1440ce050 | room add / remove object (SetRoom) | tid, object, room |
| 0x1440ccf60, 0x1440cdc90 | SetTransform / SetVisible queue | tid histogram only (counts per tid every 5 s) |
| 0x1440c5720, 0x1440c5670, 0x1440c57a0 | octree queries (they use item +0x48) | tid; nesting depth per tid |

**Concurrency detector.** Keep `active[tid] = fn` for every octree-touching function above. On enter, if a *different*
tid is active on the same room, log **CONCURRENT** with both functions and tids, plus
`Thread.backtrace(this.context, Backtracer.FUZZY)` as module offsets for the foreign thread.

**Player item.** After a hop, the first Update whose box centre is within 3 m (x/z) of the destination is the player's
item. Record it and log every Update, Remove and Insert on it: old node centre and half-size, new node, octant.

**Integrity walker.** Run it on the main thread inside the QSV onEnter: on every QSV for 30 frames after each hop,
otherwise every 60th. It walks the room's octree from the root node at room+0x20:
- for each node, children must have parent +0x58 == node;
- bucket flag must equal the list length (or be 0xFF);
- every item's +0x50 must equal its node;
- each item must be seen once (a duplicate means a double link);
- step cap 300 000.

Report the first breach with the hop number and the last 20 operations on the items involved.

**Repro.**
1. Log in with `re/dlogin.sh` on the test copy.
2. Hops of 3, 20, 45 m, then 3 m back, 6 s apart.
3. A second run of 3, 3, 10, 10, 25, 25 m back.
4. Control run: walk and use a flinger for 2 minutes.
5. Policy run: 30 alternating hops of 10 m or less, plus hops right after a re-zone.

**Outcomes.**
- **O1:** CONCURRENT entries before the breach or hang (a non-main thread in cb3f0, ce050, cb8a0, cba30 or cc4e0
  while the main thread is in a flush or QSV). The race is confirmed, and it is not fixable by any packet. Keep the
  ≤10 m policy only if the policy run shows 0 breaches.
- **O2:** no concurrency, but REMOVE-MISS or DOUBLE-INSERT first. A single-thread path left +0x50 stale; the logged
  operation history names it.
- **O3:** the walker is clean, but QSV still spins. Then look for a nested query on the same tid (depth > 1) reusing
  +0x48.
