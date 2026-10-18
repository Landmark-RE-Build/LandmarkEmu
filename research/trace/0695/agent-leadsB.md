# Leads L173, L178, L179, L186, L187 (agent "leadsB", 2026-10-18)

Read-only pass over `re/decomp_all_695` (fn.py / pkt.py, `DECOMP=decomp_all_695`), the test exe
`<0.695 test copy>\Landmark64.exe` (pefile + capstone: constants, RIP-relative writers, vtable slots, call sites) and the
test copy's data files. Nothing was run. [P] = read in code or data, [I] = inferred.

Context: moving platforms work since round 88 (ManagedObject 0x4D release after the spawn, control mode 2, 0x7F
every 500 ms with the leg velocity).

| Lead | Verdict | Server change |
|---|---|---|
| L186 | **K = +infinity**: the client extrapolates along the newest record's velocity with no time limit [P] | none needed; R7(d) is now safe (§1.4) |
| L187 | delay = max(age at the newest record, 0.15 s); pad model, pad collision and the rider's carry all use the same delayed state [P] | none |
| L178 | the model is pushed each frame by the base update, right after the motion-state SetTransform (actor +0x6E0 graphics object, vfunc +0x40). Actor +0x2D8 plays no part [P] | none (round 88 confirms it) |
| L179 | the ~33/s bodies are type 2 (position + velocity only, no platform block) [P layout / I that they are type 2]. The block (flag 0x10000000, offset, pad tid) travels only in type 0. Our relay forwards it unchanged, and the tid is the same on every client [P] | none required. Optional: send each mover's bodies in time order (§4.5) |
| L173 | WeaponHitFeedback 0x32/0x10 has **no effect on 0.695**: ReticleHandler is missing, and the HUD sound global `GLOBAL_HUD` is not in the sound data, so every sound id is 0 [P] | none. Close the lead |

---

## 1. L186: how far GetStateAtTime extrapolates (FUN_1435a53e0)

### 1.1 How the two records are picked [P]
- History index 0 is the newest record. The loop finds the first record i whose time is older than the render time t.
  - i = 0 (t is newer than every record) is moved to i = 1: older = rec[1], newer = rec[0].
  - i = count (t is older than every record) is moved to count − 1.
- With only 1 record, it returns that record unchanged: no extrapolation (a newcomer holds still until its 2nd record).
- `dt = (newer.time − older.time) · 0.001` (DAT_144c41034 = 0.001).
- If |dt| ≥ 1e-4 (DAT_14468e2b0):
  - `alpha = (t − older.time) · 0.001 / dt`;
  - `K` = the StringHashToValue double with hash **0xCC862F8A = oaat("Network.ExtrapolationLimit")** (list at
    `DAT_145431198 + 0x3C80`, node: double +0, key +0x18, next +0x20); if that value is 0.0, `K = DAT_145471a98`;
  - alpha is clamped to `[−K/|dt|, 1 + K/|dt|]`, i.e. at most K seconds past the newest record (or before the
    oldest).
- If |dt| < 1e-4, alpha = 1.0.
- `motion_state_force_lerp` (DAT_145471920, a console FloatCommand, FUN_141ad9c50) replaces alpha when it is non-zero.
  It is 0 by default.

### 1.2 The value of K [P]
- `<0.695 test copy>\Resources\StringHashToValue.txt` line 415: `Network.ExtrapolationLimit^0^`. The value 0 sends the
  code to the fallback.
- DAT_145471a98 is .bss. Its only writer is the static initializer at **0x141ad9c30**:
  `movss xmm0,[0x1452B7990]; movss [0x145471A98],xmm0`.
- **0x1452B7990 holds 0x7F800000 = +infinity.** It is in initialized .data, and a scan of every RIP-relative reference
  found 1,709 `movss` reads and no store. It is the engine's global float infinity.
- The scan found only two references to 0x145471a98: that one store and the read in FUN_1435a53e0. No console
  command points at it.
- **So K = +inf:** `1 + K/|dt| = inf` and `−K/|dt| = −inf`, and alpha is never clamped.

### 1.3 What alpha > 1 does: the blend FUN_143b043f0(out, older, newer, alpha, dt) [P, call sites read in the exe]
| case | result |
|---|---|
| newer flags bit 3 (0x8) set, or \|dt\| < 1e-4 | alpha ≥ 1: copy of the newer record (hold). Otherwise the older record extrapolated. Server flags 0x401/0x441 have bit 3 clear. |
| 0 ≤ alpha ≤ 1 | cubic Hermite on position (velocities +0x40 of both ends, scaled by dt), slerp of rotation (FUN_142b8f2e0), lerp of velocity, angular velocity and +0x70 |
| **alpha > 1** | at 0x143b044ed: **FUN_143b04100(newer, out, (alpha − 1)·dt, pos = 1, rot = 1)**. That sets `out.pos = newer.pos + newer.vel(+0x40) · s` and `out.time = newer.time + s·1000`, and rotates by the angular velocity +0x50 (axis-angle, `|ω|·s`) when it is non-zero. |
| alpha < 0 | at 0x143b047c4: FUN_143b04100(older, out, alpha·dt, 1, 1), the same backwards |

**Result:** past the newest record the pad keeps moving along the newest record's velocity for as long as it takes the
next record to arrive. Rotation keeps turning by the record's angular velocity. There is no hold.

### 1.4 The history push FUN_1435a4430 (bridging) [P]
- A new record older than the newest, or with a different +0x68 byte, resets the history to that record alone.
- If `gap = (new.time − newest.time)/1000 − 0.15` (DAT_14520886c) is > 0, a bridge record is inserted first:
  FUN_143b04100(newest, …, gap, 1, 1), i.e. the newest record extrapolated along its velocity to `new.time − 150 ms`
  (call at 0x1435a44ff).
- Then the new record is pushed. Records older than 1 s (ms +0x178) are trimmed, but at least 2 are kept.
- On a straight, constant-speed leg the bridge lies exactly on the line, so a 1000 ms (or 2000 ms) gap plays back as
  smooth motion.

### 1.5 Lowest safe 0x7F rate for straight legs (R7 d)
**Statically, no mid-leg record is needed.** The client needs a leg-start record with v and the stop record with v = 0.
Smaller things that remain:
1. **Newcomers** hold their first record until a second one arrives (count < 2 → no extrapolation). The newcomer
   repeat (`PlatformRepeat` = 1 s) or the next regular send covers this. The two records need different timestamps:
   equal ms → |dt| < 1e-4 → hold [P].
2. **Leg end.** The stop record is sent at the first 100 ms tick after arrival. Render time at its arrival is
   `≤ T_send − 0.15 s ≤ T_end + 0.1 − 0.15`, so the extrapolated pad never passes the dock [P from the formulas].
   - Same at any rate.
   - A server hitch of more than 150 ms between the true leg end and the send overshoots by `v · (lag − 0.15)`; the
     Hermite blend then pulls it back.
3. **Rotating legs.** The server's `Creatures.MovementBody` writes angular velocity (+0x50, the first 4 × p2 values
   after velocity) as 0, so a turning leg holds its rotation between records and then slerps. That is why R7(d) keeps
   500 ms there.
4. **Drift between the server's `mp.Clock` and `ServerClock`.** Each tick adds at most 1 s to mp.Clock. After a stall,
   extrapolation runs ahead of the server's pose until the next record corrects it. Rarer records correct later.
5. The 0x7F goes Reliable through the gateway (`ExternalDestination.Reliable`), so lost records are not a concern.

**Recommendation:** 1000 ms on legs whose two stops have the same Turn: 50 %, ≈126 → 63 B/s per viewer while moving
[P that it is smooth]. 2000 ms is also safe statically (≈75 %). Keep 500 ms on turning legs, or fill in angular
velocity (below).

**Exact change (R7 d), `LandmarkEmulator.WorldServer/Game/Props.Platforms.cs` `StepPlatform`, the line
`bool all = mp.Segment != pose.Segment || (pose.Velocity != Vector3.Zero && now - mp.LastSend >= PlatformSendInterval);`:**
```csharp
private static readonly TimeSpan PlatformStraightSendInterval = TimeSpan.FromMilliseconds(1000);
...
TimeSpan interval = PlatformSendInterval;
if (pose.Segment % 2 == 1)                                   // travelling from stop k
{
    PlatformStop from = stops[pose.Segment / 2], to = stops[(pose.Segment / 2 + 1) % stops.Count];
    if (MathF.Abs(Quaternion.Dot(from.Turn, to.Turn)) > 0.99999f)   // no turn on this leg (float-safe equality)
        interval = PlatformStraightSendInterval;
}
bool <computed>
```
- Also correct the `<para>` at Props.Platforms.cs ~61-67: K is +inf (Network.ExtrapolationLimit 0 → DAT_145471a98 =
  +inf from 0x1452B7990) [P], not "unknown".
- Optional, so turning legs can also use 1000 ms: a `MovementBody` overload with the angular velocity in the first
  4 × p2 slot after velocity (record +0x50, rad/s, w component 0). For a constant-rate slerp: `ω = axis · angle / legTime`
  of `q_to · conj(q_from)`. Whether FUN_143b04100 applies dq in the world or the body frame is [I]: the multiply order
  was not resolved. Check with the 'extrap' hook (outPos/rotation) before turning it on.

## 2. L187: the mode-2 render delay [P]

- **Age** (0x7F handler FUN_142ce6340, part_0065 ~21255): `entity +0x1684 = (client +0x32BE0 → +0x3C offset) +
  local now − record.time`, i.e. the age at arrival in the client's server clock. It is recomputed for every record.
- **Delay** (FUN_142eb1950, fn.py lines 544-556): `+0x48AC = max(age · 0.001, min(1.0, 0.15)) = max(age, 0.15 s)`.
  **Render time** `t = now − delay·1000`. The same t feeds the throttled path (C6) and the normal path (C11).
- **What follows that t:**
  - The pad's motion state (SetTransform), and so its collision box: FUN_1430aac30 → FUN_1430ac0d0, and every-frame
    rebuilds while |v| > 0.
  - The visible model (§3).
  - The local carry of a rider standing on that collision (agent-e3-platform.md §2.3).
  - So **on any one client the visible pad, its collision and the rider on it are in step.** There is no relative lag.
- **The only lag is against the server's true pose:** `delay + one-way latency`, e.g. 0.15 s × 1.6 m/s ≈ 0.24 m (up to
  1.5 m at the 10 m/s maximum). The rider's reported absolute positions trail the server's pad by about that much. No
  server code compares a rider with the pad today (grep: no rider logic), so nothing needs a tolerance.
- **Live's clock sync:** not readable statically. With a working sync, age ≈ one-way latency plus server queueing, so
  the 0.15 s floor rules below about 150 ms of age.
  - Our records carry `ServerClock.Milliseconds` and Synchronization 0xC3 answers with the same clock (MiscHandler.cs
    ~406). The hook in §6 logs nothing new here; hook_platform.js already logs `age1684` / `delay48ac`.
  - A wrong offset only shifts the playback. With K = inf it can no longer make it hold.

## 3. L178: how the ProxiedProp model follows its motion state [P]

**The per-frame base update pushes it, in the same block that writes the motion state.** FUN_142eb1950, normal path
C14 (fn.py lines ~1404-1427):
```
if (entity+0x1640 == 0 && sub->vfunc+0xD0 == 0) {
    ms->SetVelocity(+0x40, entity+0x1F0); ms->SetAngular(+0x50, entity+0x200);
    ms->SetTransform(+0x28, pos/rot from entity+0x1C0/+0x1D0 + scale);
    when the model pointer (below) is set, its vfunc +0x40 gets the position (entity+0x1C0) and rotation (entity+0x1D0)
}
```
- `(local_8c0, local_8b8)` is the shared pointer returned by entity vfunc **+0x468**. For ProxiedProp (vtable
  0x144703FC8) that is **FUN_142e9db90**. Through FUN_142dfbaa0 it returns the actor's **+0x6D8 / +0x6E0** object.
- That object is the actor's graphics / animation instance [I for the name]. Its +0x90 takes animation-variable
  hashes and +0x98 takes "State_*" names in the same function.
- Fetched at fn.py line 660; the same call appears in ProxiedProp::Update FUN_1430ab000.
- The motion-state handler (entity +8 → FUN_1430aac30) adds three things:
  - thunk FUN_143501530 moves the entity's **area** (entity +0x240 = area guid in DAT_1454319d0 =
    ClientAreaDataManagerClient, vfunc +0x178);
  - FUN_1430ac0d0 rebuilds the collision box;
  - FUN_142e64f00(local player +0x1A20, guid) notifies the local player.
- **Actor +0x2D8 / vfunc +0x100 (FUN_143d0cf70) plays no part for props.**
  - The only direct caller is FUN_142ec0000, whose only caller is FUN_1436dfec0 =
    `ProjectileCallbackPhysics::OnActorReady` (string in the function).
  - FUN_142ec0000 is in no vtable (a .rdata scan found only FUN_143d0cf70's 4 slots).
- The actor-loaded path (FUN_1430aab50 → FUN_142eac160) only gives the actor its first transform (FUN_143d0d8f0) and
  its owner entity (actor +0x5F0, FUN_143d0d500).
- **Why it was static before round 88:** this push sits in the interpolated apply C9-C14. That block is skipped while
  the entity is locally managed (C4: +0xF4 ≠ 0), which is §9 of agent-platform-collision.md. Round 88's 0x4D release
  is exactly what was needed; no further change. **Close L178.**

## 4. L179: the rider's bodies and our relay

### 4.1 What the client builds (BroadcastLocalPlayerMovement FUN_142ca4ce0) [P]
- It builds a full record of the local player. FUN_142ecc7f0 adds the platform part when the ground actor (+0x16B4)
  has an owner entity:
  - flags |= **0x10000000**;
  - **+0x80 = pad entity +0x1464**: the transient id. BaseEntity ctor FUN_142e88840 stores the spawn tid there;
    FUN_142f88e30 resets it to the lookup sentinel DAT_145446e2c;
  - **+0x70 = rider position − pad position** (world difference, not rotated; 4 floats).
- The send is rate-limited by `DAT_145430b2c` ms, which is 20 unless StringHashToValue key 0x28787A29 (not in the
  0.695 table) gives another value. That allows 50/s at most.
- **Heartbeat:** `Network.PositionUpdateHeartbeat` (0x307A8282) is 0 in the table, so the fallback DAT_145430dbc is
  used. It is set by the static initializer at 0x1406da330 from the same **+inf** at 0x1452B7990.
  - **So no periodic full body is sent.** A full body is sent only when it is forced (entity +0xFC bit 7) or when
    several categories change at once.
- **Body type** from the change mask FUN_143b03830 (last sent, extrapolated, vs now):

  | bit | set by |
  |---|---|
  | 1 | flags changed (except 0x100) |
  | 2 | position or velocity error over its threshold; also a platform-offset change ≥ 1.0 m |
  | 4 | rotation |
  | 8 | +0x30 / +0x60 / +0x64 |
  | 0x40 | +0x84 |

  Flags in 0x201F810 count as 2|4. A single bit 1/2/4/8/0x40 → type 1/2/3/4/5. Any combination → **type 0** (full).

### 4.2 Wire layout (serializer FUN_142c141c0) [P]
`u8 type, u32 time, u8 (+0x68)`, then per type:

| type | fields |
|---|---|
| 0 | pos 3×p2, rot 4×p5, vel 3×p5, angular +0x50 4×p2, +0x30 4×p2, +0x60 p1, +0x64 p5, flags (2-bit uint), then +0x84 (signed int; case 0 falls through into case 5) |
| 1 | flags |
| 2 | pos 3×p2, vel 3×p5 |
| 3 | rot 4×p5, angular 4×p2 |
| 4 | +0x30 4×p2, +0x60 p1, +0x64 p5 |
| 5 | +0x84 |

**Then, if the body's flags (+8) have bit 28: offset 3 × int(value · 100) and the platform tid (+0x80), all signed
2-bit ints.**
- Types 2-5 are built with flags = 1 (`local_2c4 = CONCAT44(1, time)`), so **they never carry the platform block**.
- Type 1 carries the real flags, so it gets the block, but with offset (0,0,0) and tid 0.
- **Only type 0 carries the real offset and tid.**

### 4.3 What the ~33/s are [I strong]
- A rider standing on a moving pad changes only position (the carry moves it; its own velocity +0x2090 is that of its
  walking).
- So each body is a **type 2 (position + velocity)**: about 0.05 m of error at 1.6 m/s, every ~30 ms, under the 20 ms
  floor.
- **They carry no platform flag, no offset and no pad id.** Getting on or off the pad changes several categories at once
  (flags + position / velocity), so that body is a type 0 with the block. The §6 hook `sendSum` settles the split.

### 4.4 What an observer does [P]
- The 0x7F handler merges a partial body into the latest record (FUN_142ca3a60: type 2 sets only pos/vel; type 1 only
  flags; type 0 replaces everything).
- So the **flag, offset and tid from the last type 0 persist** across the type-2 stream.
- Each frame of the interpolated apply (FUN_142eb1950 ~1451-1461): if the latest record has 0x10000000 and +0x80 ≠ 0,
  the rider is placed at **(observer's own rendered pad, found by tid through FUN_142f85340) + offset**.
- So an observer sees a rider who stands still carried exactly with its own view of the pad, whatever the type-2
  positions say.
- [I, client quirk, live had it too] A rider who *walks* on the pad sends type-2 bodies with no new offset. Observers
  keep the walk-start offset until the next type 0 (stop, jump or leave), so the walking rider looks pinned and then
  jumps. The server cannot fix that without inventing bodies; live did not.

### 4.5 Our relay [P]
- `WorldSession.FromClient` (PositionUpdate channel) → `PositionUpdate.ToRemoteFrame` → `PlayerManager.RelayMovement`
  → `MovementFanout.Queue` (or the per-session `QueueMovement`).
- ToRemoteFrame rewrites only the first 3 values of types 0 and 2 (the zone-origin shift) and copies the remaining
  bytes verbatim. The trailing platform block (offset, tid) passes unchanged.
  - The offset is a difference, so it needs no frame shift.
  - The tid is the server's `Spawn.TransientId`. The same value goes to every viewer (the pad's own 0x7F stream uses it
    for all of them), so it resolves on the observer's client too.
- Coalescing is per (transient id, body type) per tick, so a type 0 is never overwritten by a later type 2.
- **Verdict: forwarded correctly. No change needed for riders to be carried on other screens.**
- **Optional (minor, all player movement):** in one tick, `MovementFanout.Flush` / `WorldSession` pending movement
  send a mover's kinds in first-queued order.
  - So a type 2 of time c can go out before a type 0 of time b < c.
  - The observer's push then sees an older record and resets its history to b (FUN_1435a4430). The result is a
    backward snap of one tick of motion.
  - Fix: in both flushes, order each mover's pending bodies by their u32 time (`BitConverter.ToUInt32(movement, 1)`),
    or drop a pending partial (types 1-5) that is older than a type 0 queued for the same mover in that tick.

## 5. L173: WeaponHitFeedbackPacket 0x32/0x10

**Layout** (reader FUN_1433e8a50, dispatcher FUN_1433f2b90 case 0x10) [P]: 12 bytes.

| bytes | field | meaning |
|---|---|---|
| u16 | 0x0032 | base |
| u8 | 0x10 | sub |
| u32 | +0x18 | the Lua parameter |
| bool | +0x1C | enemy (else ally) |
| bool | +0x1D | vehicle |
| bool | +0x1E | headshot |
| bool | +0x1F | melee |
| bool | +0x20 | kill |

The handler FUN_1433ff920 does two things:
1. **Lua** `ReticleHandler:OnEnemyHit` / `OnAllyHit` with the u32. ReticleHandler does not exist in 0.695
   (agent-s2c-sweep.md §1), so the call does nothing [P].
2. **Sound** FUN_1431f8000(SoundPlayer DAT_145431358, enemy, vehicle, headshot, melee, kill). It picks a sound id from
   the **psAudio::HudGlobal** at SoundPlayer +0x3D0 (+0x10 ShotEnemyHuman, +0x14 HeadShotEnemyHuman, +0x18
   MeleeEnemyHuman, +0x1C KillEnemyHuman, +0x20 ShotEnemyVehicle, … +0x28.. Friendly, from the class reader
   FUN_143dc9ba0) and plays it via FUN_143db04a0. This is the PlanetSide 2 HUD hit sound.
   - That global is looked up by hash **0xCFD6CC9B = oaat("GLOBAL_HUD")** (FUN_1431f76a0) in the psAudio objects
     loaded from `Resources\Sound\GameObjectInstances.xml` (path in part_0063 ~27459).
   - The 0.695 test copy's file (the same file as 0.589, 30,524 B; there is no packed copy in fileindex_695) defines
     **only `GLOBAL_MUSIC` (MusicGlobal)**. `HudGlobal` exists only as a class in GameObjectDefinitions.xml:216.
   - So FUN_1431f76a0 allocates a **default HudGlobal with every id 0**. The lookup in DAT_145431350 then needs a sound
     whose name hash is 0: none in practice [I strong], so nothing plays.

**Verdict:** on the 0.695 client the packet has no visible and no audible effect. Whether live sent it cannot change
what a player saw or heard; Landmark never shipped a GLOBAL_HUD. **No WeaponCombat change. Close L173** (re/tools/
verdicts.tsv "parked" → "no effect on 0.695"). The §6 hook's `consts.hud` confirms the zero ids at runtime.

## 6. Frida plan (read-only; test copy <0.695 test copy> only)

Script: `re/hooks/leadsB/hook_leadsB.js`. Same protocol as hook_platform.js: Interceptor.attach with reads only,
prologues checked, batched events, clean stop. Prologues were taken from the test exe:

| hook | function | prologue |
|---|---|---|
| extrap | FUN_143b04100 | `48895c2408574883` |
| sendBody | FUN_142c141c0 | `415641574883ec48` |
| recv7f | FUN_142ce6340 | `48895c2418555657` |

`node --check` passes.

**What it does:**
- **consts** (read at load, no hook):
  - K: the list entry and the fallback;
  - the +inf source at 0x1452B7990, force_lerp, the heartbeat and its fallback, the 20 ms send minimum, the bridge gap;
  - the HudGlobal object at SoundPlayer +0x3D0: vtable, type and the 13 ids.
- **extrap:** FUN_143b04100 split by return address (0x143b044f2 'past' = beyond the newest record, 0x143b047c9
  'before', 0x1435a4504 'bridge'). It logs extrapolated ms (`out.time − rec.time`), rec / out position and velocity,
  and per-second counts with a histogram. This function runs only when something is extrapolated, so the cost is low.
  `SPEED: 1.6` limits the detail to the pad.
- **sendBody** (rider's client): body type, flags, and the platform block when it is present. Per-second counts per
  type (`t2`, `t0+plat`, `t1+plat` …).
- **recv7f** (observer's client): the bodies of `RIDER_TID`, or of anyone carrying 0x10000000.

**Run** (attach before the test, from `re/hooks/recipes`; PowerShell 5.1 needs `\"` inside the JSON):
```
python run_hook.py ../leadsB/hook_leadsB.js ../leadsB/logs/b1.jsonl --pid N --config '{"SPEED":1.6}'
python run_hook.py ../leadsB/hook_leadsB.js ../leadsB/logs/b1obs.jsonl --pid M --config '{"RIDER_TID":<rider tid>,"EXTRAP":false,"SEND":false}'
```
Close the game first or use the `.stop` file / `--stop-after`. Never kill the runner while it is attached.

**Expected (from the static answer):**
- **consts:**
  - `extrapolationLimit {found:1, value:0}`, `K_fallback_145471a98 = "Infinity"`, `inf_1452b7990 = "Infinity"`,
    `forceLerp 0`;
  - `heartbeat {found:1, value:0}`, `heartbeatFallback = "Infinity"`, `minIntervalMs 20`;
  - `hud.vt = 0x144725848`, `hud.type = 16`, and every id `0x0` (L173).
- **extrap 'past' while the pad moves:**
  - at 500 ms: dtMs up to ≈350 (500 − 150 delay floor);
  - after R7(d) at 1000 ms: up to ≈850;
  - in both cases `outPos − recPos = vel · dtMs/1000`. **dtMs above 850 with the positions still advancing proves no
    limit (L186).**
  - 'bridge' entries appear only for gaps > 150 ms.
- **sendBody while standing on the moving pad:** mostly `t2` at ~30/s. `t0+plat` when stepping on or off, with
  `platTid` = the pad's `Spawn.TransientId` and `offset` = rider − pad. `t1+plat` with a zero block when only the flag
  changes (L179 §4.3).
- **recv7f on a second account (TestPlayer2):**
  - the rider's `t0+plat` arrives with the same offset and tid (relay intact, §4.5), then `t2` with no block;
  - on screen the rider rides the pad.
  - If the rider walks on the pad, the rider stays pinned at the walk-start offset until the next `t0` (§4.4,
    client behaviour).

## 7. Updates for OPEN-LEADS (for the coordinator)

| Lead | New state |
|---|---|
| L186 | **FOUND [P]**. K = Network.ExtrapolationLimit (0 in the table) → DAT_145471a98 = +inf (static init 0x141ad9c30 from 0x1452B7990). No limit; the records' velocity carries the pad. R7(d) is unblocked (§1.5 change). |
| L187 | **FOUND [P]**. delay = max(age, 0.15 s); the pad model, its collision and the rider's carry share it, so there is no relative lag. Lag vs the server ≈ delay × speed. |
| L178 | **FOUND [P]**. FUN_142eb1950 C14 pushes the interpolated pos/rot into actor +0x6E0's graphics object (vfunc +0x40) after ms SetTransform. It is gated only by the local-management release (round 88). Close. |
| L179 | **ANSWERED [P layout, I for the 33/s type]**. Type-2 bodies with no platform block; the block (0x10000000, offset ×100, pad tid) is only in type 0. The relay forwards it unchanged; no change needed. Runtime split: the hook_leadsB.js sendSum/recv log. Optional time-ordered flush (§4.5). |
| L173 | **CLOSED [P]**. 12-byte layout; no Lua handler and no GLOBAL_HUD in 0.695's GameObjectInstances.xml, so the packet does nothing on this client. No server change. |
