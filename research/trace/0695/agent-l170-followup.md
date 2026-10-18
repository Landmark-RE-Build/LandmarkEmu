# L170 follow-up: what the Sweep can hit, entity state bit 45 + the Block effect, ShapeDefinition (2026-10-18)

Read-only research. Sources: the 0.695 decompile (re/decomp_all_695 via re/tools/fn.py and pkt.py), the
0.695 exe at <0.695 test copy> (vtable slots and constants read with re/tools/vtslots.py and capstone), our
server code (WeaponCombat.Targeters.cs, Creatures.cs, Buffs.cs, PlayerUpdateLightweightToFullNpc.cs,
PlayerUpdateAddLightweightNpc.cs), and design/forum. No game, Frida, Docker or web was used.

Marks: **[P]** = proven (address, offset or constant). **[I]** = inferred (reason given).

## Summary

| Q | Verdict | Server change |
|---|---|---|
| 1. What the Sweep considers | The Sweep does **no faction, disposition or hostility test** and **no alive test** [P]. It reports every character whose physics body overlaps the sweep sphere and lies inside the swept angle, except the local player. An NPC has such a body only once it is **full** (LightweightToFullNpc applied), has **control mode 2**, is not attached to anything, and either has a **Health-type (1) resource > 0** or was given an explicit capsule (LightweightToFullNpc +0x26c/+0x270 > 0) [P]. Our creatures meet all of these: full data on request, `ControlMode = 2`, Life resource at spawn. Allies, neutrals, story creatures, outpost elites and bosses all pass the same way. Pets never get a body (they never become full), so they are never reported. | **None is required.** Recommended: send **MaxTargets = 0** in the Sweep element and keep the limit on the server (§1.5). The client counts every reported character (corpses, allies, other players) against the element's limit. Optional: the explicit capsule (§1.4). |
| 2. Bit 45 and the Block effect | Entity state bit 45 (+0xa20) is written only by **PlayerUpdate UpdateCharacterState 0x26/0x0F** (whole word) and **UpdateCharacterStateDelta 0x26/0x51** (set / clear masks) [P]. "Block" (0xC5838838) is an **ability effect type**, put on a character by **Abilities EffectStart 0x27/0x0C** (+0x38 = type, +0x110 = arc in degrees, +0x114 = height) [P]. TargeterStart 0x27/9 does **not** set bit 45: its vtable +0x250 call is an animation-slot list [P]. The Sweep stops on any character with bit 45 inside its arc, with no facing or hostility test. The Block targeter also needs the Block effect, a hostile target, and the blocker facing the attacker within the arc [P]. The 2016 text "prevents all damage from the next attack that hits you head on" (Corrupting Revenge, 25 May 2016 notes) matches the facing test [P text, I live use]. | For a blocking stance seen by **other** clients: Delta 0x26/0x51 `{guid, 0, set = 1<<45, clear = 0, time}` plus EffectStart(type 0xC5838838, arc, height, duration) to the other viewers; on stance end, Delta with `clear = 1<<45` plus EffectEnd. Today this only matters for PvP sweeps, and for creatures if a creature ever blocks (§2.6). The PvE stance stays server-side, as built. |
| 3. ShapeDefinition | The **Sweep never reads shapes** (it is a sphere of radius elem +0x28 plus an angle) [P]. Only the **Block** targeter uses them: kind +0x10 (1 sphere, 2 box, 3 cooked wedge/convex, 4 capsule), the centred flag +0x1c, offsets +0x20/+0x24/+0x28, yaw +0x2c, sizes +0x30/+0x34/+0x38/+0x3c, and a second angle +0x40 [P]. Fields +0x8, +0xc, +0x14 and +0x18 are read by neither [P]. | None for the Sweep. Write shapes only when Block targeters are built. |

---

## 1. Which entities the Sweep considers

### 1.1 The Sweep update, FUN_14354d190 [P]

The update runs once per frame while the targeter is active (vtable slot FUN_14354da40, state +0x1a8 == 1).

1. **Origin.** If elem +0x0c == 2 ("follow"), FUN_14354d880 re-places the origin at targeter +0x150 on every update.
   It depends on elem +0x08:
   - 1 = the local player's position;
   - 3 = the point on the camera ray (DAT_145431090+0x50) nearest the player;
   - anything else = the aim position stored at init (+0x70).
2. **Angle window.**
   - Progress = (now − cast start +0x138) × 0.001 / elem +0x18 (the duration), clamped to 0..1.
   - The window this frame runs from the previous progress (+0x140) to the current one.
   - It is mapped to elem +0x1c (start angle) … +0x20 (end angle) and stored as min/max at +0x1a4 / +0x1a0.
3. **Physics query.** FUN_142b7e300 creates an `AppPhysics::PhysXCollisionQuery`:
   - FUN_142b946b0(query, origin, **elem +0x28**) makes it a sphere of that radius;
   - flags |= 0x41;
   - **query +0x5c = 0x18**.
4. **Per hit** (result userdata +0x40 must be **0x10**; +0x48 = actor id):
   - entity manager DAT_145431080 vtable +0x38 (the actor id), then vtable +0x118, gives the owning character;
   - `(+0x278)->vtable+0x30 || (+0x1452 & 2)`. Slot +0x30 is FUN_142eaa2a0, which returns `!(+0x1452 bit 1)`, so the
     test reads "is NPC or is PC". **It is always true for a character; it is not an attackable test** [P];
   - the character is not the local player;
   - **FUN_14354e3a0**: the angle test. It takes the character's bounds (position ± f32 +0x1848) relative to the local
     player's current position and facing (FUN_142ea0060 / FUN_142ea1e50) and needs the angle, in degrees
     (× 57.2958), to fall within [+0x1a4, +0x1a0];
   - **state bit 45**: if `+0xa20 >> 45 & 1`, the client plays composite effect **508** on the local player (or the
     StringHashToValue override 0xE64C2335, which is not in the 0.695 table) and **ends the sweep** through vtable
     +0x28 = FUN_14354d640 → FUN_14354d570, which sends ClientTargeterEnd 0x27/0x23;
   - **re-hit**: the HitTracker (+0x80) stores the last hit time. A character hit within elem +0x24 × 1000 ms is
     skipped;
   - otherwise vtable +0x38 = **FUN_14354e800** is called (§1.5).

**What is not called anywhere in the Sweep:**
- **FUN_142ea9630** (hostility). The Block targeter does call it (§2.3).
- Any disposition (+0x147c), FactionId (+0x1448), health or death test.

### 1.2 What the query mask 0x18 matches [P]

- PhysX preFilter (`AppPhysics::PhysXCollisionQuery` filter callback at offset 352, FUN_142bb3530): hit when
  `(shape.word0 & query.word2) != 0 && (shape.word2 & query.word0) != 0`.
- Query word0 = +0x58 (constructor default 0xFFFFFFFF), word2 = +0x5c (here 0x18).
- **CollisionTypeManager** FUN_143cd8980 lists the collision type names with (category, mask). Among them:
  - player 8 / 0x34fdf;
  - **character 0x10 / 0x34e5f**;
  - hires_character 0x20;
  - vehicle 0x40;
  - … and 18 more.
- So 0x18 = **Player | Character**: character capsules only (not terrain, structures, props or projectiles).

### 1.3 Which characters have such a body [P]

There are two body paths. Both live in the character's controller component at +0x16e0.

**PCs.** `AppPhysics::MyCharacter` (the PhysX CCT), built in FUN_142e69e50:
- only when **+0x1452 bit 1** (the "PC" bit; the lightweight-NPC spawn FUN_142cddaa0 always clears it);
- group "player" (hash 0x6643b8ae) for the local player, "character" (0x170e4746) for other players;
- userdata +0x40 = 0x10, +0x48 = actor id.
- So **other players are reported too**, whatever their side. The server already ignores them.

**NPCs.** `AppPhysics::CollisionObject` at +0x7a8, userdata type 0x10, filter 0x301f8 (which includes 0x18). It is
built by FUN_142e69e50 when all of these hold:
- the character is an NPC;
- character vtable +0x108 returns 0 (not attached);
- no body exists yet;
- the model has a collision entry (FUN_142e6d830);
- **+0xcb4 == 0**, i.e. the entity is **full**. The LightweightToFullNpc handler clears this flag;
- **FUN_142ccbb60(character) > 0**: it reads the character's resource container +0x4268 at key 1 (the resource
  **type**, record +4) and returns the entry's current value (+0x70). In other words, **a Health-type resource above
  0**;
- bounds radius FUN_142e9e460 > 0.1.

The second NPC path is **FUN_142e6cbe0**, called by the full-data apply FUN_142d532f0 when LightweightToFullNpc
**+0x270 > 0 and +0x26c > 0**:
- capsule radius = +0x270, the centre raised by +0x26c along "up" [I names];
- no health test.

**Kept or destroyed every frame** (FUN_142eb1950):
- if **+0x41a0 == 2** (control mode) and FUN_142e93340 passes (state word & 0x4000a000 == 0, +0x1470 == 0), the
  controller update runs: vtable +0x360 = FUN_142e98e90 → FUN_142e69e50, which needs +0x4050 = null guid
  (LightweightToFullNpc +0x140);
- **otherwise FUN_142e72be0 destroys the NPC body.**
- The mask 0x4000a000 (DAT_1455fedb8) is set by the static initialiser at 0x143c25f90. It does **not** include bit 45,
  so blocking never removes the body.

**Other entities:**
- Pets are lightweight NPCs that never get full data (spec-pets), so +0xcb4 stays set: **never reported**.
- Props have no Health resource and get +0x26c/+0x270 = −1, so: **never reported** [I: no other body path writes
  userdata 0x10; only two writers exist, 0x142e69e50 and FUN_142e70ba0].
- A dead creature keeps its body: only the control-mode path destroys it. So **corpses are reported** until they
  despawn.

### 1.4 Do our creatures pass? Yes [P for each field]

| Needed | Ours |
|---|---|
| Full (+0xcb4 cleared) | `Creatures.SendFull` answers FullCharacterDataRequest with LightweightToFullNpc. The client promotes the nearest ≤ 50 within 300 m, 10 per frame (full-npc-695.md §4), so melee-range creatures are full. |
| Health-type resource > 0 | `Creatures.SendSpawn` → `SendLife` (ResourcesBase 0xC4/2, type `LifeType` = 1, value scaled to a Resources.txt Life row) at every spawn, and again on every hit. |
| +0x41a0 == 2 | `Creatures.ControlMode = 2` (AddLightweightNpc +0x19c). **The dev command `npcmode` with any other value destroys the bodies, so sweeps then hit nothing.** |
| +0x4050 null | LightweightToFullNpc +0x140 = 0. |
| FactionId / disposition | **Not used by the Sweep.** We send FactionId 0 (AddLightweightNpc byte after the name id) and disposition 0/1/2 (+0x90). Allies (2) and neutrals (1) are reported like enemies. |

Fields that would matter only in an edge case:
- A creature whose `Kind.MaxHealth` has no Resources.txt Life row (LifeRow falls back to the last row, so not really).
- A model with no collision LOD (FUN_142e6d830 = 0).
- For those, LightweightToFullNpc **+0x26c = half-height, +0x270 = radius** (> 0) makes the body anyway [P call;
  I meaning]. Optional; nothing found that needs it.

### 1.5 The target limit is spent on anything reported [P]

FUN_14354e800 sends FindTarget only while `elem+0x10 == 0 || +0xa0 < elem+0x10`. +0xa0 is the HitTracker map's count
(FUN_14354da80 increments `map[4]`). Every reported character counts: an ally, a neutral, a corpse, another player.

So with `MaxTargets = 1` (a single-target cone), a corpse or ally nearer in the query order uses the slot. The real
target is then never reported. The server's swing gets one refused FindTarget, closes as reported, and does not fall
back.

**Server change (recommended):** in `WeaponCombat.SweepOf`, write **`MaxTargets = 0`** (no client limit). Keep the
server's own limit, which `SweepFoundTarget` already enforces (`a.MaxTargets <= 0 || s.Claimed.Count < a.MaxTargets`,
counting claimed targets only).

The rest of the server check is unchanged. `Creatures.InSweep` already refuses dead, out-of-zone, out-of-range and
out-of-arc creatures and non-creatures. It strikes allies and neutrals, the same as the server-cone path `Resolve`
(allies are not provoked: `Creatures.Provoked`). That is parity with the old behaviour. Live's rule for hitting allies
is not known [I].

---

## 2. Entity state bit 45 and the Block effect

### 2.1 Who writes entity +0xa20 [P]

ProxiedCharacter vtable (1446d5a88) slots:
- **+0x260** FUN_142ec5e40: set the whole word;
- +0x268: set or clear one bit;
- +0x270 FUN_142ec5ef0: set a bit;
- +0x278 FUN_142e94f40: clear a bit.

The only network path into +0x260 is the queued state records at entity +0xad8 (list +0xae0):
- FUN_142ecdd20 → FUN_142e832b0 queues {time, set, clear, source};
- FUN_142eb6a10 applies each record when `(now − interpolation delay) − time ≥ 1`;
- applied as `word = (word | set) & ~clear` → vtable +0x260.

Two PlayerUpdate subs feed the queue (dispatcher FUN_142cdfa60):

| Sub | Name (packet_ids.tsv) | Reader / wire (pkt.py) | Effect |
|---|---|---|---|
| **0x0F** (case 0xe) | UpdateCharacterState | FUN_142c2c6c0: `u16 0x26, u32 0x0F, u64 guid, u64 state, u32 time` | set = state, clear = ~state → **word = state exactly** |
| **0x51** (case 0x50) | UpdateCharacterStateDelta | FUN_142c2c7a0: `u16 0x26, u32 0x51, u64 guid, u64 source, u64 set (+0x28), u64 clear (+0x30), u32 time (+0x38)` | word = (word \| set) & ~clear |

Details for both subs:
- **time** is the server clock in ms (client "now" = FUN_142cd2690). 0 applies at once.
- **source** (+0x20, Delta only): if it equals client +0x32508 and FUN_142d22200() holds, the time is replaced with
  "now". Send 0.
- No other client code writes bit 45. The local client never sets it on anyone. FUN_142ec5ef0's callers are local
  game logic only.

**Use the Delta (0x51) for bit 45.** The full word (0x0F) would also overwrite the bits the client keeps itself:
- bit 0, set in the ProxiedCharacter constructor FUN_142e88840;
- bit 7, from spawn flag +0x29a & 0x80;
- others.

Our opcode enum already has `PlayerUpdateUpdateCharacterStateDelta = 0x26005100`. A wire constant (`…Wire = 0x260051`)
like the other PlayerUpdate subs is needed.

### 2.2 Who reads bit 45 [P]

| Reader | Use |
|---|---|
| FUN_14354d190 (Sweep) | Character in the arc with bit 45 → blocked effect 508 on the attacker, sweep ends (End sent), **no FindTarget for anyone after**. No facing, hostility or Block-effect test. |
| FUN_14354dc10 (Block targeter, below) | Bit 45 **and** a Block effect facing the attacker. |
| FUN_14354ffd0 / FUN_143550090 | Ability usability: ability record (FUN_143c77750) +0x80 flags 1 / 2 accept "caster is blocking". For the local player this is "a Block effect in the local effect list" (DAT_145431a20 +0x75648, FUN_142fd09c0, filled by EffectInitialized 0x27/0x1C → FUN_142e194c0 → FUN_142fca720). For other characters it is bit 45. |
| FUN_142e19660 (EffectStart handler) | Records bit 45 as condition 4 in the effect's parameter list (composite-effect variant [I]). |

### 2.3 The Block targeter, FUN_14354cda0 [P]

- Same window maths as the Sweep.
- For each ShapeDefinition in elem +0x38 (next +0x50):
  - FUN_143c22990 builds the shape's transform from the origin and the local player's facing;
  - FUN_143c24d40 runs a query with mask **0x34ffe**.
- Per character hit, every test below must pass:
  - **FUN_142ea9630(local player, target)**, which **is** a hostility test here (creature: FactionId outside 1-3 and
    disposition 0; players: only in the arena mode or with target bit 41, full-npc-695.md §3);
  - FUN_14354de50: angle;
  - **FUN_14354dc10**, which needs all of:
    - target **bit 45**;
    - a target effect (list from vtable +0x220 / +0x228 = entity +0x4400, next +0xc0) with type +0x20 ==
      **0xC5838838** ("Block": the effect-type name table FUN_142e7ee80);
    - the direction from the target to the targeter origin, flattened, within **cos(effect +0xa0 × 0.5°)** of the
      target's facing (+0xa0 = the **full arc in degrees**; constants 0.5, π, 1/180);
    - **|origin.y − target.y| ≤ effect +0xa4 × 0.5** (+0xa4 = the **height**).
- On a match the client:
  - plays effect 508 on the attacker;
  - stores the cast {guid, hash} in ClientAbilityManager +0x3f8 / +0x400 (FUN_142e1fa00). CastSuccess FUN_142e18340
    then **skips that cast's success composite effect**;
  - ends the targeter.
- It never sends FindTarget.
- **So the Block targeter is the attacker-side "my hit was blocked" feedback.** The blocker's state comes from the
  server (§2.4).

### 2.4 How a character gets the Block effect [P]

- Entity +0x4400 is filled only by vtable +0x240 = FUN_142e907e0, called from **FUN_142e19660, the handler of
  Abilities EffectStart 0x27/0x0C** (dispatcher FUN_142e1aab0 case 0xc; reader FUN_142e028b0).
- The handler copies:
  - packet +0x38 → effect +0x20 (**type**);
  - packet +0x110 → effect **+0xa0**;
  - +0x114 → **+0xa4**;
  - +0x118 → +0xa8.
- So the Block effect is our existing `Buffs.SendEffectStart` layout with:
  - type (+0x38) = **0xC5838838**;
  - **+0x110 = f32 arc in degrees** (Buffs writes `0u` there today; it needs a parameter);
  - **+0x114 = f32 height in metres**;
  - +0x70/+0x78 = the blocker's guid;
  - +0xdc = duration in seconds, +0xe0 = start ms.
- EffectEnd 0x27/0x0D with the same key removes it.
- Data tables do not help: AbilityEffectTypeSettings.txt and ClientEffects.txt are header-only in 0.695.

### 2.5 Did live use it for the blocking stance? [P text, I use]

- 25 May 2016 notes (design/forum/all/2016-05-24-57170, line 312), Corrupting Revenge: "prevents all damage from the
  next attack that hits you head on". That is exactly the Block effect's facing arc.
- 11 May 2016 hotfix: Molten Rebuke "continues to block".
- 26 Apr 2016: "the block effect wasn't always correctly blocking damage" (agent-leads5.md).
- "Block effect" is the effect-type name. The client has an effect type "Block" plus the state bit that the Sweep and
  the Block targeter both honour. That is the live mechanism [I strong].
- No capture shows the packet. Players in 2016 could not block at will (57065 line 1612: no way to "actively dodge or
  block"), so only these stance abilities used it [I].

### 2.6 Exact server change (when wanted)

Today players' stances are a server state (`Status.Block`, 2 s; `WeaponCombat.Incoming` uses dot > 0.2 ≈ a 157° arc)
against creature attacks, which are the server's. That is correct and needs no client state. Send the client state
only so that **other players' clients** treat the blocker as blocking:
- PvP sweeps stop on him;
- Block targeters suppress the hit effect.
Do the same if a creature ever blocks. No creature block is on record.

On stance start, to every viewer **except the blocker**:
1. `PlayerUpdate 0x26/0x51 UpdateCharacterStateDelta`: `26 00 | 51 00 00 00 | u64 blocker guid | u64 0 |
   u64 set = 0x0000200000000000 | u64 clear = 0 | u32 time` (server ms, or 0).
2. `Abilities 0x27/0x0C EffectStart` (`Buffs.SendEffectStart` layout): key = blocker guid + effect id;
   +0x38 = 0xC5838838; +0x70 = +0x78 = blocker guid; **+0x110 = 157f** (2 × acos 0.2, to match the server's test [I])
   or live's value (unknown); **+0x114 = 4f** [I]; +0xdc = 2f (Status.Block); +0xe0 = start ms.

On stance end (blocked hit, timeout, cancel):
- Delta with `set = 0, clear = 0x0000200000000000`;
- EffectEnd 0x27/0x0D with the same key.

The blocker's own client:
- only for "usable while blocking" abilities (§2.2), EffectInitialized with the Block effect;
- nothing else needed.

Effect on FindTarget: a sweep blocked by bit 45 sends End without a FindTarget for the blocker. The server then does no
damage, which matches. Do not add a fallback that strikes when an End arrives with no hits.

---

## 3. ShapeDefinition fields

| Field (element) | Wire order (agent-leads5) | Used by | Meaning [P use; I name] |
|---|---|---|---|
| +0x08 | 1st u32 | none | — |
| +0x10 | 2nd u32 | Block (FUN_143c22990, FUN_143c24d40) | **kind**: 1 sphere, 2 box, 3 cooked convex wedge, 4 capsule |
| +0x0c | 3rd u32 | none | — |
| +0x18 | 4th u32 | none | — |
| +0x1c | bool | Block | false → shape pushed forward so it starts at the origin: kind 1 +r, 2 +len/2, 3 −0.5 × (+0x34 + +0x30), 4 +len/2 |
| +0x14 | 6th u32 | none | — |
| +0x20 / +0x24 / +0x28 | u32 → f32 | Block | local offset (x, y, z; z = forward) |
| +0x2c | f32 | Block | yaw offset, degrees (× 0.0174533) |
| +0x30 | f32 | Block | kind 1 radius; kind 2 length; kind 3 radius/length; kind 4 radius |
| +0x34 | f32 | Block | kind 2 width; kind 4 length (segment = +0x34 − +0x30) |
| +0x38 | f32 | Block | kind 2 height; kind 3 arc angle (FUN_142ba3360 halves it) |
| +0x3c | f32 | Block | kind 3 only (third cooking parameter: height [I]); the cooked shape is cached at +0x48 |
| +0x40 | f32 | Block | second rotation, degrees (FUN_142cb31a0; pitch/roll [I]) |

- **The Sweep reads none of this**: FUN_14354d190, FUN_14354d880, FUN_14354eca0 and FUN_14354ebb0 never touch elem
  +0x38. Our Sweep can keep sending an empty shape list.
- +0x0c / +0x14 / +0x18 / +0x08 matter for neither targeter. They may matter for server-run shapes (ShapeStart 0x21),
  which were not part of this task.

## 4. Still open

- Live's Block arc and height values (no capture, no table). The +0x110 / +0x114 values above are matched to our
  server test [I].
- Whether live creatures ever blocked (bit 45 on NPCs).
- Bit 0 / other client-kept bits of +0xa20: these are not needed if the Delta (0x51) is used.

## Files

- This report: `<workspace>\trace\0695\agent-l170-followup.md`.
- Code read (not edited):
  - `LandmarkEmu\LandmarkEmulator.WorldServer\Game\Combat\WeaponCombat.Targeters.cs`;
  - `…\Game\Combat\WeaponCombat.cs` (Incoming, Block);
  - `…\Game\Creatures.cs` (SendSpawn / SendLife / InSweep / ControlMode / Provoked);
  - `…\Game\Buffs.cs` (SendEffectStart);
  - `…\Network\Message\Model\PlayerUpdateLightweightToFullNpc.cs`, `PlayerUpdateAddLightweightNpc.cs`;
  - `…\Network\Message\ZoneMessageOpcode.cs`.
