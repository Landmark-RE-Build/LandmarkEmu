# Highlight task A: the 0.695 interaction highlight, its colours and the "in reach" (gold / yellow) switch

2026-10-31. Read-only trace. No code was changed, nothing was built, the game / Frida / Ghidra were not used.
Sources: re/decomp_all_695 (fn.py, pkt.py, grep); re/out/rtti_695.tsv; a capstone + pefile read of
`<0.695 install>\Landmark64.exe` (scratch scripts: the colour table, the timing configs and three call
sites); server-data/worlds/dev/console.log (all 185 InteractionInfoReply TRACE lines decoded); MODLOG.md:760,
1172-1176 (0.589 work of 10-10 / 10-11); trace/0695/axe-a-client.md; craft-a-client.md.

**[P]** = proven (FUN_ address and what it does, or file:line). **[I]** = inferred.

## 0. Answer in short

- **The highlight is one shared outline effect** (ProxiedObjectOutlineEffect, InteractionProcessor +0x280) that the
  InteractionProcessor puts on one entity at a time, in **outline slot 3**, every frame [P].
  - Per-frame update FUN_1433d2a40 (called from the client frame FUN_142cd6830 while client +0x31f88 is set).
  - The highlighter FUN_1433d19b0(processor, entity, on) picks the colour.
- **Two colours** [P] (FUN_14362e890, table in .rdata 0x1447bf0a0, RGBA, HDR):
  - mode **1 = (2, 1, 0, 1) gold / "yellow"** = in reach;
  - mode **2 = (2, 0, 0, 1) red** = not in reach.
  - Others not used by this path: 3 = (0.5, 2, 0.5) light green, 4 = (0, 2, 0) green, 5 = (2, 2, 0) yellow.
  - The effect's own default (FUN_14362d530) is (2, 2, 2, 1) white.
  - Hard-coded: no Outline* / Highlight* config value, nothing in StringHashToValue or the client .ini files.
- **Gold needs all of these in the same frame** [P] (FUN_1433d19b0 at 1433d1a4a-1433d1a7c):
  1. the local player exists (entity manager +0x282c8);
  2. **FUN_142ea9b60(entity, player)** is true: the gap between the two axis-aligned boxes is at most
     `max(entity +0x4118, player +0x4118)`, or the ClientGameSettings default range if both are 0;
  3. the interaction type FUN_142ea07e0 (+0x4a58) is **3**.
  - Anything else gives red, or nothing (§3).
- **Our server's data meets this on paper** [P].
  - Every Spire-prop reply in this session is: type 3, cursors 10 / 0 / 10 / 0, prompt 0, sphere -1, range 6.0,
    outline 1.
  - So the client should turn gold within **6 m of the prop's model box** and stay red from 6 m out to the edge of
    the prop's 10 m interaction sphere.
  - The 0.589 client (same code, FUN_143324bd0) was seen going gold on a crafting station (MODLOG.md:760) and red at
    14 m (MODLOG.md:1173).
- **What can still keep it one colour.** These are candidates, not proven (see root_cause / open questions):
  - **(a) A held tool hides slot 3** [P mechanism]:
    - The axe (ActionStateLogging) and sickle (ActionStateReaping) outline the prop under the cursor in **slot 1**,
      always gold. They also outline usable props, because type 3 sets +0x1457 bit 2 and FUN_142ea9e60 then passes.
    - Slot 1 outranks slot 3 (FUN_142ec5ce0 applies only the lowest filled slot).
    - The user held crafted axe 1926 at the Spire in this session (console.log 08:39:08 "build mode, put away
      1926").
  - **(b) The 6 m reach is shorter than what the server lets the player do** [P values, I effect]:
    - A click opens a station from any distance.
    - The window stays open up to max(8 m, reach + 3 m).
    - The live ClientGameSettings default is 10 m.
    - Between 6 m and 10 m the highlight stays red, although the station still opens.
  - **(c) A runtime failure of the reach test** (bounds or positions) cannot be ruled out without a probe.

## 1. Who draws it and which entity gets it [P]

### 1a. Per frame: FUN_1433d2a40 (InteractionProcessor, client +0x31f88)

1. It runs ProcessAutomaticInteractions FUN_1433d4070(processor, local player). That sets **+0x20 = the proximity
   pick** (§1c).
2. It chooses the entity to highlight, `lVar5` (the same choice as FUN_1433d2690 and the F key FUN_1433d5520):
   - **+0x28**, the entity of an open interaction menu. It is set by FUN_1433d3ad0 / FUN_1433d3b70 and cleared by
     FUN_1433d14e0 / FUN_1433d15c0.
   - Otherwise the rule is `(state(+0x18) == 2 || state(+0x20) != 2) && state(+0x18) != 0`:
     - true → **+0x18**, the hovered entity;
     - false → **+0x20**, the proximity pick.
   - state() is FUN_1433d2940:
     - 0 = not flagged or not found;
     - 1 = flagged but out of reach, no local player, or type 2;
     - **2 = in reach and type 3**;
     - 3 = in reach and type 1 / 4.
3. If the choice changed, the previous entity (+0x30) is un-highlighted: FUN_1433d19b0(…, 0).
4. The new one is highlighted with FUN_1433d19b0(…, 1) **every frame**, so the colour follows the distance live.

### 1b. Hover (+0x18): FUN_1433d5620(processor, entity, on)

Callers:
- the mouse events in FUN_142d0f610: 0x400 enter, 0x80 / 0x200 move, 0x800 leave;
- every frame from FUN_142cd4e60 for the entity under the cursor, client +0x32b80.

It sets +0x18 = the entity's guid only when all of these hold:
- the entity is flagged (+0x1461 bit 0x40, or +0xa20 bit 40);
- the player's +0x1459 bit 0x10 is clear;
- the PropProcessor exists and its vf+0x20 returns 0 (not editing a prop);
- DAT_145431ab8 +0xb4 == 0;
- FUN_142ccbad0(client) != 1.

Otherwise +0x18 = none.

It also sets the **hover cursor** (client +0x322c8 → +0x70), using the same reach test:
- type 3: in reach → entity +0x2188, out of reach → +0x218c;
- other types: +0x2190 / +0x2194;
- values below 1 → 0.

A call with entity 0 returns at once, so +0x18 can keep a stale guid. That is harmless: the lookup then fails and
state = 0.

### 1c. Proximity pick (+0x20): FUN_1433d4070 "BaseClient::ProcessAutomaticInteractions"

- Every flagged entity is registered as a `PlayerSphereTrackable` in the grid at client +0x32030. FUN_142e93f50 does
  this from the 0x6B handler FUN_142f85770 when FUN_142ec7ce0 is true.
- Each frame the grid is queried at the player's position (FUN_1433ce560, radius 0). That returns the entities whose
  **interaction sphere** (+0x4120 centre, +0x4130 radius) contains the player.
- The candidates are sorted by distance, weighted by facing ("interactionDirectionWeight").
- +0x20 = the first candidate that is in reach and type 3, else the first flagged one (out of reach → red).
- A candidate with +0x4140 > 0 that is within that distance is auto-interacted: CommandPacketInteractRequest 0x1E / 9.
- Where the sphere radius comes from:
  - reply +0x20 when ≥ 0 (FUN_142ec2b40(entity, xmm1));
  - otherwise LightweightToFull +0x148 if > 0, else the ClientGameSettings default (FUN_142d532f0 at 142d533a9-142d533c8);
  - when +0x411c = 0, FUN_142ea4230 falls back to the default only if +0x1452 bit 2 is set.
- Our props send -1 / 0, so the sphere is **10 m around the pivot**.

### 1d. The highlighter: FUN_1433d19b0(processor, entity, on)

Disassembly checked at 1433d19b0-1433d1b2c.

1. `on` is forced to 0 when:
   - the type is 1 (unknown) or 4 (request pending); or
   - **entity +0x4a60 == 0** (the reply's outline byte; this is its only reader).
2. When on:
   - `fx` = processor +0x280.
   - If the local player exists **and FUN_142ea9b60(entity, player)** (rcx = entity, rdx = player) **and type == 3**:
     - FUN_14362e890(fx, **1**) → gold;
     - FUN_1433d5310(processor +0x288), the InteractionHintHandler: the HUD hint "[key] interact with <name>"
       (verb = entity +0x4a5c, or the handler's default).
   - Else, if !(+0x1452 & 2) && (**+0xa20 bit 44**): no outline at all (fx = 0, on = 0).
   - Else FUN_14362e890(fx, **2**) → red, and the HUD hint is cleared (FUN_142e7fd50 on client +0x32080).
3. Actor flag: FUN_143d041c0(actor, on) sets actor +0x42c bit 0x40, only when the actor allows outlines (+0x42c bit
   0x80).
4. Attach: FUN_142ec5ce0(entity, **slot 3**, fx), when FUN_1433d3760 passes or fx = 0.
   - FUN_1433d3760 is false for the prop the PropProcessor is editing (FUN_142fb97a0).
   - It is also false for characters with +0x1452 bit 2 whose +0x250 vf+0xb0 returns false.
5. Group: every entity in the same group (vf+0x590, FUN_142f85170) gets the same effect. fx +0xf0 / +0x100 = the
   union of their model boxes.

### 1e. The colour really is per frame

- FUN_14362e890 writes fx +0x60..+0x6c and fx +0x110 = mode.
- fx +0x30 is the "PropOutlineBox" sub-object (FUN_14362d530 / FUN_14362d970). Its material has the
  "OutlineScreenPasses" technique.
- Every draw, FUN_14362e1a0 copies sub +0x30 (= fx +0x60, the RGB) into that material's constant.
- So a mode change shows on the next frame. The actor itself only gets technique 0x7975665e (FUN_14362d920).

The 0.589 client has the same function: FUN_143324bd0, with +0x946 × 8 = +0x4a30 and FUN_14357b090 as the colour
setter [P].

## 2. The in-reach test, exactly: FUN_142ea9b60(a = entity, b = local player) [P]

```
boxA = a.vf1e8()->vf120()   // Actor bounds = model bounds (+0xc0..) x actor scale (+0x3a0), local, FUN_143d01e50
boxB = b.vf1e8()->vf120()   // (-1..1) when the model is not loaded
A = a.vf40() + boxA          // pivot (+0x180 vf10 +0x30) + box, not rotated
B = b.vf40() + boxB
range = (a+0x4118 > b+0x4118) ? a+0x4118 : b+0x4118     // comiss / ja at 142ea9ca4
if (range == 0) range = (float)client+0x326d0           // ClientGameSettings default interaction range
return gap(A, B)^2 <= range^2                            // per-axis gap, x / y / z only
```

Where entity +0x4118 is written [P]:
- AddLightweightProp handler FUN_142cde1c0: `packet +0x240 > 0 ? it : default`;
- lightweight setup FUN_142d54040 (NPCs / PCs / props): a raw copy of +0x240;
- the reply: `+0x24 >= 0` overwrites it (FUN_142f85860);
- the constructors zero it.

The local player keeps 0 [I: no other writer].

**Packet +0x240** is the u32 after the three f64 zone origin in the lightweight base (pkt.py FUN_142c26790). Our
writer calls it "draw distance (0 = default)" (PlayerUpdateAddLightweightProp.cs, after `ZoneOrigin.Z`). **It is
the interaction range (f32)** [P].

## 3. Every outcome of the highlighter [P]

| entity state | highlight |
|---|---|
| not flagged (no 0x6B, no +0xa20 bit 40) | none: state 0, never picked |
| flagged, reply never arrived (type 1) or pending (type 4) | none |
| type 0 | none: state 0 |
| outline byte (+0x4a60) 0 | none |
| type 2, any distance | **red** (gold needs type 3) |
| type 3, out of reach | **red**; none if +0xa20 bit 44 is set and +0x1452 bit 2 clear |
| type 3, in reach | **gold** + HUD hint; F sends InteractRequest (FUN_1433d5520 needs FUN_1433d2890 == 2) |
| any of the above, but a lower outline slot (0-2) is filled on that entity | the lower slot's effect is shown instead |

### Outline slots: entity +0x1ef8 + 8 × slot, 7 slots; FUN_142ec5ce0 applies only the lowest filled slot [P]

| slot | owner | colour |
|---|---|---|
| 0 | ReticleManager FUN_143201830, mouse 0x400 / 0x800, only entities with +0x1452 bit 2 (characters); LocalProxiedCharacter FUN_14332b710 | mode 1, set once (FUN_1431ffd70) |
| 1 | the axe: ActionStateLogging FUN_1437327b0 (effect +0xa0 made by FUN_143734a80); the sickle: ActionStateReaping FUN_143735c60 / FUN_143735d20 (effect +0xc0) | **mode 1, fixed** |
| 2 | UIData-side tool, FUN_143650cf0 (+0xb0, FUN_14364e600) | mode 1 |
| 3 | **InteractionProcessor** (this trace) | 1 / 2 by reach |
| 4 | PropProcessor (FUN_142fc0790 copies the mode), ControllerObjectSelector FUN_143736f30 and others | varies |

- **Usable props count as axe targets.** FUN_142ec5740 sets +0x1457 bit 2 for type 2 / 3, so FUN_142ea9e60 is true
  for every usable prop.
  - The axe's "valid" test (axe-a-client §1a: `Requirement(+0xb0) || FUN_142ea9e60(prop)`) therefore passes on every
    station, chest and Spire prop, even with requirement 20.
  - With an axe or sickle in hand, the hovered usable prop gets the tool's **fixed gold** slot-1 outline at any
    distance. That hides the slot-3 red / gold.
- **A detach does not re-apply the next slot.** When slot 1 is cleared, slot 3 stays set but is not re-applied.
  FUN_1433d19b0 passes the same pointer every frame, and FUN_142ec5ce0 ignores an unchanged pointer. Slot 3 shows
  again only after the highlight target changes [P from FUN_142ec5ce0; I visible effect].

## 4. InteractionInfoReply (PlayerUpdate 0x26 / 0x6A), as the 0.695 client reads it [P]

- Reader: FUN_142c285e0 (u16 opcode, u32 sub, i32 count, then count × FUN_142c202b0, 0x30-byte entries).
- Dispatcher: FUN_142d54c10 case 0x6a.
  - It applies the packet **only if the read raised no error and consumed every byte**:
    `(int)end - (int)cursor < 1`.
  - Then it calls FUN_142f85860(entity manager client +0x31e60, packet).

| wire | bytes | struct | handler FUN_142f85860 → entity | meaning |
|---|---|---|---|---|
| u64 | 8 | +0x00 | lookup in the +0x50 table (0x1003 buckets, key +0x2b0) | guid |
| u32 | 4 | +0x08 | FUN_142ec2b00 → **+0x4a58** (a 4 is written only over a 1); +0x4a68 = now | type: 0 none, 1 unknown (re-ask), 2 interactable without F / gold, **3 interactable**, 4 pending (client-side) |
| 4 × u32 | 16 | +0x0c..+0x18 (FUN_142c22c10) | FUN_142ec5740: types 2 / 3 → +0x2188, +0x218c, +0x2190, +0x2194 and +0x1457 bit 2; other types → -1 × 4 and bit 2 cleared | **cursor ids** (Cursors.txt): type 3 in reach, type 3 out of reach, other in reach, other out of reach. Not colours (docs/protocol.md:366 says "colour": wrong) |
| u32 | 4 | +0x1c | **+0x4a5c** | text id of the HUD hint verb (FUN_1433d1c50); 0 = the hint handler's default |
| f32 | 4 | +0x20 | if ≥ 0: FUN_142ec2b40 → +0x411c, sphere centre +0x4120 = pivot, radius +0x4130 | proximity sphere radius; < 0 = keep |
| f32 | 4 | +0x24 | if ≥ 0: **+0x4118** | **interaction range** for FUN_142ea9b60; < 0 = keep (spawn +0x240 or the default) |
| u8 | 1 | +0x28 | **+0x4a60** | outline on (read only by FUN_1433d19b0) |

So an entry is 41 bytes on the wire. Our writer (InteractionMessages.cs `InteractionInfoReply.Write`) writes exactly
this: guid, type, CursorInReach, CursorOutOfReach, CursorInReach, CursorOutOfReach, 0, SphereRadius, Range,
Outline.

### Refresh, client side [P]

The entity update FUN_142eb1950 refreshes the info once the timestamp at +0x4a68 is old. The thresholds are client
config values registered in part_0031 (.data values read from the exe):

| config | value | effect |
|---|---|---|
| `interaction_freshen_ms` (DAT_145208b58) | 5000 | after 5 s: re-ask when the player is within the apathy radius, otherwise type 1 |
| `interaction_apathy_radius` (DAT_145208b54) | 50 m | the distance for the re-ask |
| `interaction_giveup_ms` (DAT_145208b5c) | 10000 | after 10 s: type 1 |

A refresh leaves type 3 in place (FUN_142ec2b00), so the outline does not flicker. That is the stream of
InteractionInfoRequests in console.log, about one per prop every 5 s.

## 5. What this session's server sent [P, console.log]

Decoded all 185 replies (every one 45 bytes for 1 entry, which matches the exact-length rule):

| guid(s) | type | cursors | +0x1c | sphere | range | outline | replies |
|---|---|---|---|---|---|---|---|
| 0x53500001CC000002..7 | 3 | 10 0 10 0 | 0 | -1.0 | **6.0** | 1 | 164 |
| 0x5350000187000002 / 4 / 5 / 6 / 7 | 3 | 10 0 10 0 | 0 | -1.0 | 6.0 | 1 | 19 |
| 0x20001D3 (a placed prop, not usable) | 0 | 0 0 0 0 | 0 | -1.0 | -1.0 | 0 | 2 |

Source: Props.SendInteractionInfo (Props.cs:2204-2236): `Range = usable || editable ? StationRange (6f) : -1f`,
`Outline = usable`, `CursorInReach = 10`.

What the client therefore has for each usable Spire prop:
- +0x4a58 = 3, +0x4a60 = 1, +0x4118 = 6.0 (the spawn had set 10, the default, because +0x240 = 0);
- sphere = 10 m (LightweightToFull +0x148 = 0 → default), cursor 10 in reach and arrow out of reach.

The flagging is there too: the client keeps re-asking every 5 s.

Other evidence from the log:
- There was no F-key InteractRequest in this session. Every interaction is the click triple: Command SelectPlayer,
  TargetBase, Command InteractRequest (e.g. 08:39:31 Resupply Chest).
- The station-56 window was closed by the server at 14.1 m, with limit 8.3 m (08:41:26). So it had been opened about
  5.3 m from the station's pivot (Crafting.cs `openReach` + 3 m).
- The user held crafted axe 1926 near the Spire ("build mode, put away 1926", 08:39:08).

## 6. What the client needs for the gold "in reach" highlight (checklist)

1. **Flag** the entity: PlayerUpdate 0x6B DynamicInteractionFlagging (+0x1461 bit 0x40), or state bit 40 in +0xa20.
   We do this.
2. Reply **type 3**: we do this. Type 2 is red only; 0 / 1 / 4 show nothing.
3. Reply **outline byte 1**: we do this for usable props.
4. **Range**, reply +0x24 ≥ 0 → +0x4118: the box-to-box gap must be within it. Ours is 6 m.
   - The default path is reply -1 plus spawn +0x240 = 0 → ClientGameSettings (10). That would give 10 m.
5. The local player exists; it is not the prop being edited.
6. It is the processor's chosen entity: hovered (+0x18), or inside its interaction sphere (+0x20), or the open
   interaction menu (+0x28).
7. **No lower outline slot filled on it.** Not with an axe / sickle in hand while hovering it (slot 1, fixed gold),
   and not a reticle character (slot 0).

## 7. Server change points (not edited; for task B)

1. **Range.** Either send `Range = -1` and put the per-prop range in AddLightweightProp +0x240, or send 10 to match
   the ClientGameSettings default.
   - Then make the server's "can use" distance agree with it: InteractRequest has no distance check today, and the
     walk-away close is max(8 m, reach + 3 m).
   - [I that live used the default 10 m: the live ClientGameSettings capture sends 10; no live 0x6A capture exists.]
2. **Fix the misnamed AddLightweightProp field** "draw distance" → interaction range f32 (packet +0x240).
3. **Optional "gold only" look** [P mechanism, I live use]: set state bit 44 on a prop's +0xa20 (0x51
   UpdateCharacterStateDelta `set = 1 << 44`). The red out-of-reach outline is then dropped, and only the in-reach
   gold shows.
4. **The tool outline hiding the interaction colour** is client behaviour with live data (type 3 → +0x1457 bit 2).
   [I] it was the same on live; nothing to send differently.
5. **Docs and comment corrections:**
   - docs/protocol.md:366 says "4 × u32 colour": they are cursor ids.
   - InteractionMessages.cs comments name 0.589 offsets and functions. The 0.695 equivalents: Outline +0x4a60 (not
     +0x4a30), type +0x4a58, highlighter FUN_1433d19b0 (not FUN_143324bd0), hover FUN_1433d5620 (not
     FUN_143328810), cursor store FUN_142ec5740 (not FUN_142e26a10).
   - FUN_142ea4230 falls back to the default only for +0x1452 bit 2 entities.
6. The HUD hint verb, reply +0x1c, is free to use. 0 keeps the default "interact with".

## 8. Related, not the prop highlight

- **Nameplate / reticle HighlightMode** (full-npc-695.md) is written by ReticleManager FUN_143201830 into the
  Reticle data source:
  - 0 by default;
  - inside the slot-5 ability range: 2 - (FUN_142ea9630(entity, player) != 0), i.e. 1 or 2.
  - Characters only: entity vf+0x108 must be 0 and FUN_142eaa260 false.
- The axe cursor / outline: axe-a-client.md §1a.
