# Visibility octree log analysis: round 80 short-teleport freeze (L116, 2026-10-18)

Read-only analysis of `re/hooks/visibility/logs/vis_a.jsonl`. The run: the test copy logged in, a tpclaim, the hooks
attached, then plain TeleportToLocation hops of +3, +20 and +45 m in x. The client froze on the 45 m hop.

Sources:
- The client decompile (`re/decomp_all_695`, `fn.py`).
- The exe `<0.695 test copy>\Landmark64.exe`, disassembled read-only with the new `re/hooks/visibility/disasm.py`.
- The server's `Game/Teleport.cs`, read-only.

New helper scripts in `re/hooks/visibility/`:

| Script | What it does |
|---|---|
| `disasm.py` | Disassembly, RTTI name of a vtable, and which vtables hold a function |
| `ripwrites.py` | Finds the static initialisers of runtime-filled constants |
| `nan_chain.py` | Per-hop chain for each item: non-finite box → Remove miss → double link → cycle |

[P] = proven from code or the log, [I] = inference.

## Verdict

- **Cause.** For about one frame after every plain TeleportToLocation, the local player's four visibility boxes get a
  **non-finite (NaN/Inf) bounding box**. The octree's handling of NaN is inconsistent between its functions [P]:
  - **Update** (FUN_1440cc4e0) stores the NaN box. Depending on the item's octant, it either keeps the item where it is
    or reinserts it into a fixed far bucket.
  - **On the next frame, Remove** (FUN_1440cba30) reads the NaN stored size and searches the wrong list (the own list).
    It misses the item and returns without unlinking it.
  - **Insert** then links the item a second time.
  - **Result:** two node lists share a tail. When both lists are inside the camera's query, QuerySimpleVisibility pushes
    the item twice, its +0x48 result list becomes a cycle, and QSV spins forever (the freeze).
- **No concurrency [P].** All 180 062 Update, 15 210 Remove, 15 000 Insert, 286 RoomAdd and 496 RoomRemove calls, and all
  queries, ran on thread 31620. There were 0 CONCURRENT events and 0 foreign-thread operations. The race theory in
  agent-short-teleport.md §1 is refuted for this freeze.
- **Objects involved [P].** Only four items, all `Deep::VisibilityBoxObject` (vtable 0x1449b6098, RTTI read from the
  exe). All four follow the local player: the player's actor box and three attached actor boxes.
  - Every REMOVE-MISS, DOUBLE-INSERT, DOUBLE-LINK, QUERY-CYCLE and BREACH involves these four.
  - No prop, NPC, creature, terrain cell or effect is involved, even during the 286 adds and 496 removes of entity churn
    after the 20 m hop.
- **Distance is not the cause [P mechanism, I statistics].** The NaN frame happens on every plain hop (hops 1, 2 and 3
  all show it). Whether it hangs depends on two things:
  - the octant that holds the player's items when the NaN frame lands;
  - whether the next real box leaves that node, with both lists in view.
  A stale link left by an earlier hop can also turn a later, harmless move into a cycle. This explains the earlier "hang
  on the hop after a ≥20 m hop" pattern. A 10 m limit is no protection.
- **What starts the NaN frame [I, strong; one Frida run to confirm].**
  - The server sends **(0,0,0,1)** at TeleportToLocation +0x30 (`Quaternion.Identity`, Teleport.cs line 127).
  - The client does **not** read +0x30 as a quaternion. It hands the field to FUN_142ec1650, which treats it as a
    **forward direction vector** [P]: it masks w with (1,1,1,0), normalises xyz, and builds right = (d.z,0,−d.x),
    up = (0,1,0) and forward = d.
  - With xyz = 0, both rotation axes are zero, so the actor matrix is singular.
  - The NaN appears in the same frame the client applies the pending move (+56..70 ms after the packet).
  - The re-zone path reads its rotation as a real quaternion with a near-zero guard (FUN_142ec4d70). Re-zones never
    hung.
- **Server avoidance:**
  1. Send a non-zero horizontal unit facing vector `(x, 0, z, 0)` at +0x30: the player's current facing applied to +Z,
     flattened, with (0,0,1,0) as the fallback. Never send (0,0,0,1).
  2. Until the confirmation run is clean, keep `ShortTeleportDistance = 0`. Every move then re-zones. A re-zone uses the
     quaternion path, and it deletes and re-adds every visibility item, which also clears stale links left by earlier
     plain hops.

## 1. Is the integrity walker right? Are the hop-0 BREACHes real?

### What the client's octree does (re-read for this analysis) [P]

- **Insert walk** FUN_1440cb6e0, disassembly 0x1440cb6f2:
  - `comiss size,[node+0x68]; ja own-list`. An item goes into a bucket only when size ≤ the half-size of the node that
    receives it. Otherwise it goes into that node's own list (+0x60).
  - The octant bits are set with `comiss c,nodeC; setb`: 4 = x<c, 2 = y<c, 1 = z<c.
  - A bucket split (more than 16 items, half-size above the minimum at +0x80) reinserts the bucket's items into the new
    child, which sets their +0x50.
- **Root growth** FUN_1440ca8f0: puts a new intermediate node in each root child slot (child moved to slot k^7,
  parent fixed). Bucket slots and their items' +0x50 stay valid, because the root centre is fixed (DAT_1455f0730). It
  reinserts the old own list.
- **Remove's merge** (after 0x1440cbbf6) frees nodes upward and reinserts the surviving bucket into the surviving
  ancestor, which sets +0x50.
- **Loose octree.** The looseness is only in the query widening, min(half × 0.5, largest item size at +0x84). An item is
  never stored in a parent's bucket. The only parent-level storage is a node's own list, for items larger than that
  node's half-size.

### The walker matches these rules for finite boxes

| Walker check | Client rule it matches |
|---|---|
| `cell` | item +0x50 is the node that holds it |
| `octant` | item centre vs node centre, setb semantics |
| `size` / `ownSize` | bucket items have size ≤ half; own-list items have size > half |
| `count` | flag = list length, or 0xFF for "unknown" |
| `parent` | child +0x58 points back at the node |
| `dup` | each item is seen once |

So the BREACHes on finite items are **real** [P].

### Hop-0 BREACHes: real, not false positives [P]

- **Where.** Node 0xe8c05e50 is the root's slot-7 child: centre (−8192,−8192,−8192), half-size 8192, parent the root
  0x4cac860. Its slot-7 bucket holds items whose +0x50 is 0xe8c062b0 (centre (200,1240,392), half-size 8). Relative to
  0xe8c05e50 their octant is 0. Two of them are player boxes: a41bdfe0 and a41bdf80.
- **Why that node.** It is exactly where a NaN item lands. A NaN box "fits" every node (`ja` is not taken when the
  comparison is unordered) and takes octant 7 at every level (`setb` gives 1). So it descends root → slot 7 → 0xe8c05e50
  → bucket 7.
- **So** the hop-0 entries are stale links left by a NaN round trip **before the hooks were attached**. The likely source
  is a plain hop during the earlier steps of this login. If the tpclaim target was within ShortTeleportDistance = 60 m,
  tpclaim itself went out as a plain TeleportToLocation [I].
- **Neighbours, not anomalies.** e7b212a0 and e7b20820 are other items in 0xe8c062b0 slot 0. The walker reached them
  through the shared tail, because a stale entry's +0x40 points into its real list.

### Walker and hook limitations (fix them before the next run)

- **JS NaN semantics differ from the client's `comiss` [P].**
  - JS `<` with NaN is false; the client's `setb` gives 1. So `octant()` returns 0 for a NaN item where the client uses 7.
  - JS `size <= half` is false for NaN. That raises the walker's `size` breach and makes `cellOf` print `s:-1`. The client
    puts a NaN item in a bucket (Insert) but looks in the own list (Remove).
  - REMOVE-MISS `kind:"own"` was right only because Remove also takes the own-list branch for NaN.
  - **Fix:** use NaN-aware helpers:
    - `lt(a,b) = !(a >= b)` (setb);
    - insert-fits = `!(size > half)`;
    - remove-bucket = `half >= size`;
    - update-same-size = `a === b || isNaN(a) || isNaN(b)` (ucomiss/jne with no parity check);
    - update-inside = `!(maxd >= half)`.
- **The walk stops at 8 breaches.** "nodes 2, items 4" is where the walk stopped, not the size of the tree. The full walks
  covered 271–274 nodes and 5 501–5 682 items.
- **`boxCentre` only tests x.** A box with only y or z non-finite would pass. Test all six floats.

## 2. Every REMOVE-MISS, DOUBLE-INSERT and DOUBLE-LINK: item and preceding operations

### The four items [P from the log; class from RTTI]

| Item | VisibilityObject | Size | Behaviour |
|---|---|---|---|
| 2c62c0e0 | d3cece00 | 3.2–3.3 | Always at the player. Finder hit at +0.37 m |
| cee95160 | ebc78040 | 1.94 | Always at the player |
| a41bdf80 | acd2e840 | 1.42–1.53 | Bogus finite positions for about 300 ms after each NaN frame (e.g. (−211.8, 2485.9, 231.7), (−326.8, 2440.9, 154.3)), then jumps back to the player (pMove, jump of about 1 320 m) |
| a41bdfe0 | acd2ee80 | 1.37 | Same as a41bdf80: (3.05, 1.65, 0.86), (24.2, 13.6, −1.8), (244.9, −17.5, 79.4), then back |

What these objects are:
- **[P]** All four use vtable `.?AVVisibilityBoxObject@Deep@@`.
- **[P]** VisibilityBoxObjects are created by:
  - Actor vtable +0x148 (FUN_143cfdd20 → FUN_143cfdd30);
  - AppFxEntity (FUN_143df3fc0);
  - DeepParticle::ParticleEntity (FUN_144166890);
  - VoxelVisibility "VoxelCellNoOcclude" (FUN_1437879d0).
- **[P]** An Actor pushes its world matrix to its visibility object through FUN_1440fb080, from actor+0x18 to vtable
  +0x58 SetTransform. The callers are the transform listener FUN_143dbce30 and Actor vtable +0x50 / +0xc0.
- **[I]** These four are the player's actor boxes: the character body (size 3.3) plus three attached actors. a41bdf80 and
  a41bdfe0 behave like bone-attached parts, whose transform lags the skeleton for a few frames.
- The exact actor classes come from the run in §4.

### The chain, the same for every event (`nan_chain.py`)

1. **Frame N (+56..70 ms after the packet):** an Update with a **non-finite** box (pUpdate `newC: null`). The client then
   takes one of two paths:
   - The item's stored centre is in octant 7 of its node (hop 3: all four in d26c2e60 slot 7). The NaN "same size",
     "inside" and "octant 7" tests all pass, so it is an **in-place** update. The item stays in a visible bucket with a
     NaN box.
   - Any other octant (hops 1 and 2: 062b0 slot 0 and 135c0 slot 4). A correct Remove, then a NaN Insert into
     **0xe8c05e50 slot 7**. That bucket already held stale links of the same four items, so it became a 4-item ring
     (`everywhere` lists the item at i = 4, 8, 12, … 0xe8c05e50 slot 7).
2. **Frame N+2..3:** a finite box arrives. The stored size is NaN, so the in-place test runs against the stored node. It
   fails because the new centre has left that node, so Update calls Remove.
   - **REMOVE-MISS:** Remove executes `comiss half,NaN; jae bucket`, which is not taken, so it looks in the own list.
     The item is in bucket 7, so nothing is unlinked. The own-list branch has no fallback scan.
   - The ops for each miss are `[update n=X → remove n=X]`, preceded by the NaN step one or two frames earlier: either
     `[update → remove (old cell) → insert n=0]` or a lone in-place `update`.
3. **DOUBLE-INSERT (stillLinked) → DOUBLE-LINK:** Insert links the item into its real new cell. Its +0x40 now continues
   the new list, so the old list shares that tail.

| Hop | Item | NaN step | Stale entry left in | New cell |
|---|---|---|---|---|
| 1 | 2c0e0, 95160 | (before hop-1 logging) | e8c05e50 slot 7 | 062b0 slot 0 |
| 1 | bdf80 | | e8c05e50 slot 7 | ebe650f0 slot 1 (bogus position) |
| 1 | bdfe0 | | e8c05e50 slot 7 | e8c079e0 slot 7 (bogus position) |
| 2 | all 4 | reinsert → e8c05e50 slot 7 | e8c05e50 slot 7 | 2c0e0 and 95160 → d26c2e60 slot 7; bdf80 → ebe650f0 slot 1; bdfe0 → e8c06240 slot 6 |
| 3 | 2c0e0 | **in place in d26c2e60 slot 7** | **d26c2e60 slot 7 (visible)** | **d26c2920 slot 1 (visible)** |
| 3 | 95160 | in place | none: orphaned, because the d26c2e60 slot-7 list now runs through 2c0e0 into d26c2920, so `found: nowhere` and no DOUBLE-INSERT | d26c2920 slot 5 |
| 3 | bdf80, bdfe0 | in place | the old e8c05e50 slot-7 ring | far nodes (bogus positions) |

The DOUBLE-INSERT count is 11 rather than 12 because of the orphaned 95160 at hop 3.

**QUERY-CYCLE (hop 3, +250 ms).** The entry is 2c62c0e0, linked in **d26c2e60 slot 7 and d26c2920 slot 1**:
neighbouring 16 m cells (centres x = 240 and x = 272), both in view. The query pushed the item twice, giving a result
list of 859 entries with a loop of 143. Then STUCK: QSV ran for 1 677 ms and never returned.

### Is there a common cause? [P]

Yes: one cause for all of them. A non-finite box entered Octree::Update, and Remove's NaN branch silently misses on the
next update.

The other suspects are ruled out:
- **Not a stale centre from setTransform without an Update:** SetTransform always queues, and the flush always runs
  Update.
- **Not a freed cell.**
- **Not the Remove fallback count bug:** no miss took the fallback branch.
- **Not an update that bypasses Remove.**

The client code path that moves the objects is the normal one:

TeleportToLocation FUN_142d59f30 → actor vtable +0x418 FUN_14332b920, which stores pos/dir at +0x4ca0/+0x4cb0 → per-frame
FUN_143329ed0 → at 0x143329ff3 SetPosition (vtable +0x48), and at **0x14332a000 `call FUN_142ec1650(actor, +0x4cb0)`**
→ transform component (+0x180) vtable +0x18 → transform listener FUN_143dbce30 / FUN_1440fb080 → SetTransform
FUN_1440ccf60 → flush FUN_1440cf300 → Update FUN_1440cc4e0.

## 3. Server or client?

### Server-triggered [I, strong], through TeleportToLocation +0x30

- **[P]** The handler passes packet+0x30 to FUN_142d5ce80 and to actor vtable +0x418 (call sites 0x142d5a1f7 and
  0x142d5a319). FUN_14332b920 copies it to actor+0x4cb0 (`param_1[0x996..0x997]`). FUN_143329ed0 passes it to
  **FUN_142ec1650**.
- **[P] What FUN_142ec1650 does, from the disassembly at 0x142ec167c–0x142ec1759:**
  - `mulps` by DAT_1455f0830 = **(1,1,1,0)** (filled at startup by 0x1428a2080 from 0x144692200);
  - `rsqrtps` normalisation;
  - a finite mask (`x−x == 0` against DAT_1455f0870 = 0, set by 0x1428a1f60);
  - a cross product with up DAT_1455f07a0 = **(0,1,0,0)** (set by 0x1428a1fb0 from 0x144c41340).
  - It is a **direction**, not a quaternion. Zero xyz gives right = 0 and forward = 0: a singular rotation, with the
    translation kept.
  - The zoning setter FUN_142ec4d70 is a real quaternion normaliser: it uses all four components, and when the length is
    at most DAT_144c41034 it substitutes a fixed default. That is why identity is fine for ClientBeginZoning.
- **[P]** The server writes `Quaternion.Identity` = (0,0,0,1) at +0x30 for every plain hop: Teleport.To, "Identity
  unless a facing is given". Its xyz is zero.
- **[I] Where the NaN comes from.** FUN_142ec1650 itself masks NaN to 0, and the Umbra matrix setter (FUN_14424b080) does
  not divide. So the NaN must come from the transform component or the actor and skeleton stage that consumes the
  singular matrix (for example, a scale or quaternion decomposition dividing by a zero axis length). Evidence for this:
  - the NaN shows up in all of x, y and z of all four boxes in the same frame;
  - that frame is the one where the pending move is applied;
  - the boxes are finite again 2–3 frames later, once the controller re-orients the actor.
  The run in §4 proves or refutes it.

### Not caused by anything else the server sent in this run [P]

- No prop, creature or NPC position update, no LightweightToFull, no respawn, scale change or entity delete touched an
  anomalous item.
- The 286 room adds and 496 room removes after hop 2 produced no misses.
- 0x7F Position is ignored for the local player (L116).

### Server change to avoid it [I until the run below confirms]

1. **Teleport.To: fill +0x30 with a facing direction, not a quaternion.**
   - Compute `fwd = Vector3.Transform(Vector3.UnitZ, facing ?? session.Facing)`, set y = 0 and normalise. If the length
     is below about 1e-3, use (0,0,1).
   - Write `Vector4(fwd.X, 0, fwd.Z, 0)`.
   - The client's forward is the matrix's third row (FUN_142ec1650 builds rows right, up, forward, position), so +Z is
     the right axis to rotate.
   - This also applies to every caller of Teleport.To: Translocation Pad (`Props.ManagedTeleport`), Evac, `/location`,
     map teleport and respawn by 0xC.
2. **Keep `ShortTeleportDistance = 0` (every move re-zones) until the confirmation run is clean.** A plain hop of any
   length can create a stale link. A re-zone clears all stale links, because every item is released and re-inserted.
3. **No distance, ordering or burst rule fixes this.** The trigger is the per-hop NaN frame, and the hang depends on the
   client's octant geometry, which the server cannot know. The ≤10 m policy in agent-short-teleport.md §3 rested on
   chance. It should not be relied on.
4. **Related lead [I].** FUN_142ec1650 has 13 other callers. Any other server field that ends up there as a direction
   (NPC or creature facing, mover rotation) must also be a non-zero horizontal vector. Audit server packets for
   "identity quaternion sent where the client expects a direction". Add this to OPEN-LEADS.

## 4. Next Frida run (narrow and decisive)

Use the same runner rules as the README (close the game first, never kill the runner). Only `Interceptor.attach`.
The prologue bytes below were checked against the 0.695 exe.

| Address | Prologue | Hook |
|---|---|---|
| 0x142ec1650 FUN_142ec1650(actor, dir*) | `48895c2410574883` | Log `dir` xyz/w and \|xyz\|, plus the return address. **ZERO-DIR** when \|xyz\| < 1e-3 or not finite. Mark calls whose return address is 0x14332a005 (the teleport apply in FUN_143329ed0) |
| 0x1440fb080 FUN_1440fb080(actor, matrix*) | `40534883ec20488b` | Read 16 floats. **NONFINITE-XFORM** or **DEGENERATE-XFORM** (row 0 or row 2 length < 1e-4) with the actor vtable (resolve offline with `disasm.py --rtti`), the actor's visibility object (actor+0x18), and a FUZZY backtrace. This names the four actor classes and shows whether the matrix is already NaN or only singular |
| 0x1440ccf60 SetTransform(visObj, matrix*) | `40534881ec900000` | Same finite and degenerate test, for visibility objects not owned by an Actor |
| 0x1440cc4e0 Update | (existing) | **NAN-BOX** when any of the six box floats is non-finite. Log the item, node, stored-centre octant (NaN-aware) and the predicted path: in place when that octant is 7, otherwise reinsert into the root's slot-7 chain |
| 0x1440cba30 Remove | (existing) | Add `nanStored` when item +0x30 is NaN. The client then takes the own-list branch |
| 0x142d59f30 TeleportToLocation | (existing) | Also log packet +0x30..+0x3c (the direction) |

The other hooks, the walker and the cycle detection stay as they are, with the NaN-aware fixes from §1 and the walker's
breach cap raised to about 64.

**A/B procedure:**
1. **A — server unchanged.** Hops of 3, 20, 45 and 3 m back.
   - Expect a ZERO-DIR (return address 0x14332a005) on each hop.
   - Expect a NONFINITE-XFORM or DEGENERATE-XFORM, then a NAN-BOX on the four player boxes within one frame.
   - Then a REMOVE-MISS with `nanStored`, and a DOUBLE-LINK.
2. **B — server change 1 only** (facing vector at +0x30, ShortTeleportDistance = 60).
   - Run the same hops, plus the 30-hop policy run (3–45 m, alternating, including hops right after a tpclaim).
   - Expect **0** ZERO-DIR from 0x14332a005, 0 NAN-BOX, 0 REMOVE-MISS, 0 DOUBLE-LINK, 0 BREACH (after the initial
     re-zone), and no STUCK.

**Reading the result:**
- **B is clean:** the server change is proven. Raise ShortTeleportDistance to the 80 m engine re-centre limit, then test
  that limit.
- **B still shows NAN-BOX:** the NONFINITE-XFORM backtrace names the producer. For example, the ground snap (+0x42) or the
  attachment update. Follow that path. Re-zone-only stays in place in the meantime.
- **A shows DEGENERATE-XFORM without NONFINITE-XFORM** before the NAN-BOX: the NaN is made inside Umbra's AABB code
  (FUN_144234100 / FUN_14424b620). The server fix is the same.
