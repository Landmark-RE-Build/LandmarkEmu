# Claims 0xE3 and moving-platform follow-ups (agent "e3-platform", 2026-10-18)

Read-only static trace: `re/decomp_all_695`, the 0.695 test exe `<0.695 test copy>\Landmark64.exe` (capstone / pefile byte
scans from the scratchpad), the server source, `server-data/worlds/dev/opcode_stats.tsv` and MODLOG. Nothing was run.
- [P] = read in code or data (function + offset / line given).
- [I] = inferred.

---

## 1. Claims 0xF9 sub 0xE3

### 1.1 Verdict [P]

**0xE3 is PlayerEnteredClaim (0x63) as the old ClientWire695 translator numbered it.** It is a stale row in a
cumulative stats file, not a live client packet. **No server change is needed** for the protocol.

The chain:
1. **The client never writes 0xE3.**
   - The Claims header writer FUN_142d9e650 writes `u16` from packet +0x08 (the base) and then **one byte**: the low
     byte of the `u32` at +0x10 (the sub).
   - Every Claims packet object in the 0.695 decompile was listed: all constructors and stack builds that store
     0xF9 at +0x08. Their subs are 1-0x1E, 0x22-0x2F, 0x30, 0x35, 0x37-0x3E, 0x40-0x46, 0x60, 0x61 and 0x63. None is
     0xE3, and none takes its sub from a variable.
   - PlayerEnteredClaim is built in FUN_142dc8ec0 (entering a claim). The exe has `mov dword [rbp-9], 0x63` at
     0x142DC930A, followed by the send FUN_142da8230 at 0x142DC933A.
   - So the wire is `F9 00 63 <u64 claim>`.
2. **The server reads the sub correctly.**
   - The width is right: ClaimsRequest is `SubRequest8` (a u8 sub).
   - The opcode is right: `WorldSession.GetOpcode` matches the 2 bytes `F9 00` = ClaimsBase.
   - Nothing else can turn `F9 00` into another family.
3. **The stats row is PlayerEnteredClaim with the high bit set.**
   - The row is `in F900E34A 6`. The `4A` is the first byte of a claim id: the same server's outgoing ClaimInfoUpdate
     rows are `F900054A` / `F900054C`.
   - There is no `in F90063…` row at all, although 0x63 is sent on every site entry.
   - So `E3 4A…` is the 0x63 packet with bit 7 set.
4. **The translator explains bit 7.**
   - The 2026-10-15..17 ClientWire695 translator gave 0.695-only Claims subs the internal number **0x80 + sub**.
     Sources: TASKS C.2; MODLOG ~2021 ("Claims 0x80 + sub"); MODLOG 2153 (StartClaimPlacement as "internal 0xC6").
   - 0x80 + 0x63 = **0xE3**.
   - MODLOG ~2031 already records "the expected unhandled 0.695-only Claims 0x63 (internal 0xE3)" from the first
     0.695 login. That was before L33 (MODLOG 2212) added the 0x63 handler.
5. **The row is history, not new traffic.**
   - The translator was removed on 2026-10-17 (003dc1f / aaa3d8e).
   - `OpcodeStats` merges its counts into `opcode_stats.tsv` on every run (OpcodeStats.cs ~91-101: ReadAllLines +
     merge). So the 6 counts are left over from the translator days.
   - Today the same packet reaches `PlayerEnteredClaimSub = 0x63` → `Claims.Entered`.
   - The client reads no reply (packets-695.md §8).

### 1.2 Server change

- **Protocol: none.**
- **Housekeeping (optional):**
  - Delete the row `unanswered	Claims 0xF9	0xE3	6` and the row `in	F900E34A	6` from
    `server-data/worlds/dev/opcode_stats.tsv` while the server is stopped. Otherwise the merge keeps them for good.
  - Change the comment at Claims.cs:400 ("the dev world saw 0xE3, which no client builder sends") to "0xE3 was 0x63
    PlayerEnteredClaim renumbered by the removed ClientWire695 translator (0x80 + sub)".
  - Close TASKS E.3's runtime note the same way.
- **Side note [I], not part of 0xE3:**
  - `Claims.Entered` drops the report unless `ClaimAt(session.Position) == claim`.
  - The client sends 0x63 from its own site-entry logic as it crosses the border, and the server's position lags by
    up to one movement update. Some legitimate entries can therefore be logged as "stands elsewhere" and skip
    DailyGoals / RewardChests.
  - If that is seen in logs, allow a margin of a few metres, or re-check after the next position.

---

## 2. Moving platform

### 2.0 Verdict for round 79: why the pad does not move even with OwnerGuid 0 [P]

**The platform's AddLightweightProp sends control mode 0.** The client only plays a proxied entity's server
positions when the entity's control mode (+0x41A0) is non-zero. With 0 it leaves the entity where it spawned. This is
the same rule the emulator already applies to creatures (ControlMode 2).

The chain:

1. **The spawn sets +0x41A0.** SetupLightweightNpc **FUN_142d54040** is shared by the NPC and prop spawns. At decompile
   line ~121 it does:
   ```
   mode = u32 at pkt + 0x19c;  FUN_142ecf190(entity, old mode = entity[0x834], mode);  entity[0x834] (+0x41A0) = mode
   ```
   - `PlayerUpdateAddLightweightProp.Write` writes `(byte)0` in that slot: the byte after the three "spawn flags"
     u32s, the same slot as `PlayerUpdateAddLightweightNpc.ControlMode`.
   - So every prop, the pad included, gets +0x41A0 = 0.
2. **The gate on +0x41A0.** **FUN_142e93340** returns true only if:
   - `+0x41A0 != 0`, and
   - `!(entity+0xA20 & DAT_1455fedb8)` (the state mask, FUN_143c260c0), and
   - `+0x1470 == 0`.
3. **Where the gate is used.** The base update **FUN_142eb1950** is called first by ProxiedProp::Update FUN_1430ab000.
   It uses the gate in both paths that turn the buffered 0x7F records into the entity's transform:
   - **Throttled path** (line ~661-756, taken when vfunc +0x4C0 = FUN_142e94050 says "skip this frame"): it needs
     `FUN_142e93340(e)` before vfunc +0xE0.
   - **Normal path** (line ~947-1463):
     - `ms+0xF0` GetLatest fills `local_308`.
     - Then `if (FUN_142e93340(e) || (latest.flags >> 17 & 1))` → vfunc **+0xE0** = FUN_142ea8bb0 →
       FUN_143501430 → motion state **+0xD8 GetStateAtTime** (FUN_1435a53e0) writes the interpolated state into the
       entity +0x1B0 (position +0x1C0, rotation +0x1D0, velocity +0x1F0).
     - Then (line ~1399-1420, `vfunc +0x130` = FUN_142e9dd50 returns 0 for ProxiedProp) the motion state +0x180 gets:
       SetVelocity +0x40 ← entity +0x1F0, SetAngular +0x50 ← +0x200, SetTransform +0x28 ← position / rotation.
   - With +0x41A0 = 0 and our state words (0x401 / 0x441: bit 17 clear), **neither path runs**. The 0x7F records pile
     up in the ProxiedObjectMotionState history (FUN_1435a4430) and are never played.
4. **What does still run:**
   - The 0x7F handler FUN_142ce6340 still finds the prop.
   - Since e8cbad8 its guid is no longer in the +0x32F40 set.
   - It queues the record (thunk FUN_143500500 → +0xD8 FUN_142e9c760, and ms +0x70) and sets +0x4F00. So
     FUN_1430ac390 rebuilds collision once a second at the **unchanged** spot.
   - That matches round 79 exactly: the pad stays put and so does the rider.
5. **Why creatures move with the same body.** `Creatures.ControlMode = 2` (Creatures.cs ~2042) is written into the
   same packet slot. PlayerUpdateAddLightweightNpc.cs:26-30 already documents "0 leaves the NPC where it spawned"
   (spec-pets.md §3.3).

Other suspects, checked:

| Suspect | Result |
|---|---|
| **Entity +0xF4** (the ProxiedProp early return, FUN_142eb1950 line ~625) | Initialised to 0 by the NetworkObject constructor FUN_1434ff670: the `mov byte [rdx+0xe4], r8b` at 0x1434FF7BA, with r8 = 0. Entity +0xF4 is that object's +0xE4 at entity +0x10. A byte scan of the whole exe for stores to `[reg+0xF4]` / `[reg+0xE4]` (mov, setcc, or, xchg) found no entity-class writer [P for the scan; I that nothing else sets it]. So that early return is not taken. |
| **Transient id** | Allocated once per PlacedProp (Props.cs 880 / 2156) and reused for every spawn, re-zone respawn and LightweightToFullProp (Props.cs 2292). Platforms send `platform.Spawn.TransientId` (Props.Platforms.cs 240). r71 showed FUN_142f85340 finding it. Fine. |
| **Body format** | Same `Creatures.MovementBody` bytes that move kobolds. Type 0 skips the "state +0x68 must match" merge. Fine. |
| **+0x4E9D / +0x4F00** | Collision only (FUN_1430ac390); they never move the model. |
| **PropDefs, model type, spawn flag bytes** | Nothing in the 0x7F path or the gate reads them. Only +0x19c decides. |

### 2.1 Exact server change [P for the field; I that 2 is live's value]

1. `Network/Message/Model/PlayerUpdateAddLightweightProp.cs`:
   - Add `public sbyte ControlMode { get; set; }` (default 0), documented as in PlayerUpdateAddLightweightNpc
     (reader +0x19c → entity +0x41A0 via FUN_142d54040).
   - In `Write`, replace the `writer.Write((byte)0);` that follows the three `writer.Write(0u);` after
     `// spawn flags passed to SpawnProxiedProp` with `writer.Write((byte)ControlMode);`.
2. `Game/Props.Platforms.cs` `BindPlatform`: next to `part.Spawn.OwnerGuid = 0;` add
   `part.Spawn.ControlMode = 2;`. That is `Creatures.ControlMode`, the value remote players are forced to.
   - Only the moving part (1193 / 1421). Docks and all other props stay 0.
   - The spawn object is reused for re-zone respawns and LightweightToFullProp, so one assignment covers them.
3. Everything else stays as it is:
   - OwnerGuid 0 is still required, or the owner's client drops the 0x7F (§7.1 of agent-platform-collision.md).
   - Static area 14 is still required.
   - Keep the 0x7F with the real velocity every 500 ms, velocity 0 at stops, and the double snapshot.
4. **Side effect [P]:** FUN_142ecf190 → FUN_143d002d0 clears actor +0x3B8 bit 2 when the mode is non-zero. That bit
   only registers the model with TextureLodManager (FUN_143d5e8d0 / FUN_143d5f360, refcount by model-name hash).
   Creatures run that way too [I: harmless].
5. **Verify by the client's reaction:**
   - The pad moves on the owner's screen and on a second account's screen.
   - Physics hook: `buildEntColl ra=FUN_1430ab000+0x411` every frame during a leg (the motion state velocity is now
     non-zero).
   - Dev shortcut, if a quick check is wanted: an existing platform needs a fresh spawn (re-zone or relog) after the
     change, because +0x41A0 is only set at spawn.

**Possible conflict to watch [I]:** the owner's gizmo move.
- With control mode 2, the base update rewrites the entity transform from the newest buffered record every frame.
- If the prop tool (FUN_142fb9ba0 → FUN_142fc0790 attaches the real ProxiedProp) moves that same entity, a drag
  could snap back to the last 0x7F spot. The server already stops sending while `MovedAt` / `!Placed` /
  `ReplacingBy`.
- If the snap is seen, during an edit session send **PlayerUpdate ManagedObject 0x4D** `{object = pad, old = 0,
  new = editor}`. Handler FUN_142cdd450 → FUN_142c98c00 sets bit 4, which takes the client-simulated path.
- Release it after the save settles with `{pad, old = editor, new = 0}` → FUN_142d3b580.
- The 0x4D wire order is still [?] (agent-platform-collision.md §7.2 B).

### 2.2 Does OwnerGuid 0 break anything the owner does? [P]

**Nothing in the client reads the spawn owner except the "locally managed" set.**
- +0x2A0 has exactly one reader: the compare in FUN_142cde1c0 (§7.1 of the platform file).
- Every reader of the set at client **+0x32F40** (hash buckets +0x32F68):

| function | what |
|---|---|
| FUN_142c98c00 | add (spawn owner match, or 0x4D new manager = me) |
| FUN_142d3b580 | remove (0x4D old manager = me) |
| FUN_142ce6340 / FUN_142cdd5c0 | 0x7F / position handlers: drop updates for members |
| FUN_142d05710 `IsLocallyManaged` | called only by FUN_14303ca60 and FUN_143040f30: **vehicle** code (VehicleSetAutoDrive / VehicleExitPoints) |
| FUN_142c5cdf0, Unwind@144544ebe / 144546c16 | constructor / destructor |

- Every reader of **entity +0x1457 bit 4**:

| function | what |
|---|---|
| FUN_142ec58a0 | setter (clearing it calls ms +0xE8) |
| FUN_142ea8bb0 | clears it when a server state is applied |
| FUN_142eb1950 (lines ~626, ~669, ~950) | picks client-simulated vs interpolated update |

- No prop UI function is in either list: pick up (PropBase 5), delete (7), PropAccess / gizmo (1 → 2), the prop
  radial menu, tint, or the settings window.
  - Those ask the server.
  - The server decides from `PlacedProp.OwnerGuid` (`MayRearrange`, `MayDecorateAt`, PickUp / Delete owner checks in
    Props.cs), and that field is unchanged.
- **So OwnerGuid 0 on the pad's spawn costs nothing [P for the client side].** The only behavioural difference is the
  one wanted: the owner's client now treats the pad like everyone else's (interpolated, not client-simulated).

**Did live send the owner in prop spawns? [I, not settleable statically].**
- No captured live AddLightweightProp exists (agent-live-zonein.md).
- What the client shows: a non-zero owner makes the owner's client simulate the prop locally (bit 4: local physics,
  mass reset in FUN_142c98c00, 0x7F ignored). That fits props the owner manipulates physically, such as dropped or
  thrown dynamic objects, and vehicles (the only other set users).
- A server-moved prop has to go out with no player owner, or with a 0x4D handing management away.
- Keep the real owner on ordinary props (nothing changes for static ones). Keep 0 on server-moved parts.

### 2.3 Rider carry: what the client does and needs [P unless marked]

**The carry is the client's own PhysXCharacter code, not PhysX's controller** (this corrects L135c's assumption).

- **Ground object.** MyCharacter move FUN_142bbb7e0 (PhysXCharacter.cpp) sweeps down (FUN_142b94630, hits
  FUN_142b94f60). It stores the first non-water hit's AppPhysics object as the ground object: char **+0x1E8** (via
  FUN_142b80770, decompile line ~377; getter FUN_142bbae90 = MyCharacter vfunc +0x268). Last frame's ground object is
  kept at char +0x1F8.
- **Carry rule** (decompile lines ~654-730):
  ```
  if (onGround && ground == previousGround)
      if (ground->byte[+0x51] & 0x10)   // AppPhysics flag bit 11
          displacement += ground world matrix (vfunc +0x30) applied to the stored previous local position
                          - the previous world position (carry by transform delta, rotation included)
      else
          displacement += dt * char(+0x240, +0x244, +0x248)   // a stored ground velocity
  ```
- **Bit 11 is set by the +0x4E58 tail.**
  - FUN_142b8aba0(obj, n, on) sets bit `1 << (7 - n&7)` of byte `+0x50 + n/8`. So n = 0xB → **+0x51 bit 0x10**.
  - The SlidingFriction (+0x4E58) tail of FUN_1430ac390 calls `FUN_142b8aba0(body, 0xB, 1)` on the model body.
  - So bit 11 means "carries characters standing on it". It is the "meaning ?" left open in agent-platform-collision.md
    §6a.
- **Who else sees the rider.** The ground entity's id also goes into the local player's movement state:
  - FUN_142e6dcc0 stores owner id (owner type 0x10) at controller +0x22C.
  - FUN_142e69e50 copies it to entity +0x16B4.
  - FUN_142ecc7f0 writes it into the outgoing state: flag 0x10000000, +0x80 = platform transient id (+0x1464), +0x70 =
    position relative to the platform.
  - Observers place remote riders at platform position + offset (FUN_142eb1950 lines ~956 and ~1451-1461).
  - So other players see riders ride without server help, **provided the server relays the rider's movement body
    unchanged**. `PositionUpdate.ToRemoteFrame` must keep that flag and block [?]. Check it if remote riders jitter.

**What the carry needs:**
1. The pad must move. That is §2.1: control mode ≠ 0, plus OwnerGuid 0.
2. The ground object must be the converted model body:
   - group 4 / mask 0x301F8 (blocks the player's sweep) and bit 11.
   - Both come from the +0x4E58 tail, which needs static area 14 on the spawn. That is already sent.
   - The entity box (0x1000004 / 7) is never the ground: mask 7 has no player bit, and it never gets bit 11.
3. The body's world transform must change frame by frame:
   - The tail enables the motion-state transform tracker on the body (`motionState->vfunc 0xB0(1)`, FUN_142b8c790).
   - The interpolated entity transform reaches the actor through the entity motion state's SetTransform each frame
     (§2.0 point 3) [I that the tracker then moves the body; one physics-hook look settles it].
4. **0x7F velocity: not used by the carry itself** (the transform-delta branch). It is still needed:
   - it lands in entity +0x1F0 through the interpolation and then in the motion state velocity;
   - with |v|² ≥ 1e-5 (DAT_144c4102c), ProxiedProp::Update calls FUN_1430ac390 every frame, which keeps the box and the
     tail in step;
   - with v = 0 those rebuilds come only from +0x4F00, at most once a second.

   So keep the leg velocity.
5. Nothing else is needed from the server: no 0xB2 (not built), no attach packets.
   - The "+0x4E58 SlidingFriction conversion" is the prerequisite: it supplies both the walkable body and the carry
     flag.

### 2.4 The proposed leads (§6a of agent-platform-collision.md)

- **L135a (+0x4E58 = 1, conversion runs): settled enough statically and by round 75 [I strong].**
  - Round 75 has the player standing on the stopped pad with the arrival hold.
  - The only bodies there are the entity box (cannot hold a player: mask 7) and the model body. Unconverted, the model
    body is a 10 kg gravity body that would have dropped away.
  - So the tail ran. H1-H3 Frida remain optional confirmation.
- **L135b (airborne-arrival hold): built** (MiscHandler.cs `PropBodyMs = 4000`, line ~287; 97b5728). Close it.
- **L135c (rider carry): answered, see §2.3.**
  - The carry is client code keyed on the AppPhysics flag bit 11 that the +0x4E58 tail sets.
  - It needs only the pad to move: §2.1.
  - The runtime check is the old test C (stand on the pad through one leg) once the control-mode fix is in.
- **Bit meanings:**
  - FUN_142b8aba0 **bit 11 = "carry characters on this body"** (+0x51 & 0x10, read by FUN_142bbb7e0) [P].
  - Bit 5 (+0x50 & 0x04, the actor +0x370 path) is not traced here.
- **New lead (open, not written off):** the gizmo/interpolation conflict in §2.1, and its 0x4D remedy, if it shows.
