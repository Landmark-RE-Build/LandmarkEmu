# Full NPC data and creature disposition (0.695) — OPEN-LEADS L98

2026-10-16. Marks: **[P]** proven from the 0.695 decompile (re/decomp_all_695), **[I]** inferred.
Server code: `LandmarkEmulator.WorldServer/Network/Message/Model/PlayerUpdateLightweightToFullNpc.cs`,
`.../Model/PlayerUpdateSetDisposition.cs`, `Game/Creatures.cs` (`SendFull`, `DispositionOf`, `StoryFaction`).

## 1. The problem

Ally / Neutral story creatures showed red (enemy) nameplates. The client decides hostility from entity
**+0x147c** ("disposition") for every NPC whose FactionId (+0x1448, the signed byte after the spawn id in the
lightweight spawn; we send 0) is outside 1-3. The cause was ours, not the missing packet: since 2026-10-15 the server
answers the client's FullCharacterDataRequest with LightweightToFullNpc carrying **+0x90 = 0**, which the client copies
into +0x147c → hostile. Before the full data arrives every entity is friendly (constructor default 2).

## 2. Who writes entity +0x147c [P]

| Writer | Value | Path |
|---|---|---|
| FUN_142e88840 (ProxiedCharacter constructor, from FUN_142f8a0b0 create-entity) | **2** (u64 write: +0x147c = 2, +0x1480 = 0) | every character, incl. lightweight NPCs (FUN_142cddaa0) and pets |
| FUN_142cdfa60 case 0x5a (= sub 0x5B LightweightToFullPc) | 2 | full players |
| FUN_142cdd710 `BaseClient::HandlePlayerUpdateAddFeatherweightObject` (case 0x73 = sub 0x74) | 1 | featherweight objects (not characters; no combat state) — not a route for creatures |
| FUN_142d532f0 (full-data apply) ← FUN_142cdf400 (LightweightToFullNpc, case 0x5b = sub 0x5C) and FUN_142cdf560 (LightweightToFullProp/Vehicle path, param_4 = 1) | packet **+0x90** | once per entity |
| FUN_142d50410 (setter) ← FUN_142cdfa60 **case 0x13 = sub 0x14 PlayerUpdatePacketSetDisposition** | packet +0x20 | any time |

(The PlayerUpdate dispatcher switches on sub − 1: case 0x10 builds ExpectedSpeed with sub 0x11, case 0x5b reads
LightweightToFullNpc FUN_142c4c3d0, so case 0x13 is wire sub 0x14 [P].)

## 3. What the value means [P]

- **FUN_142ea9630 (is hostile a↔b)**: if either side is "neutral" (NPC: +0x147c == 1; PC: FactionId == DAT_1455fe51c)
  → not hostile. NPC vs PC: FactionId 1-3 → compare factions; else **hostile ⇔ +0x147c == 0**. NPC vs NPC: both
  FactionId 1-3 → compare; else hostile when neither is 1 and they differ (so 2 vs 0 = ally vs enemy creature fight).
- **FUN_142ea8f90 (is ally)**: NPC vs PC with FactionId outside 1-3 → **+0x147c == 2**; NPC vs NPC → equal and ≠ 1.
- **FUN_142eaa260 (is neutral)**: NPC → +0x147c == 1.
- **Nameplate, FUN_143201830**: `HighlightMode` = 0 when the entity is neutral (eaa260) or another early condition,
  else **1 when hostile, 2 when not** (`iVar8 = 2 - hostile`), written to the widget only when it changed → enemy
  (0) = HighlightMode 1 (red), neutral (1) = 0 (plain), ally (2) = 2 (friendly colour). [I] the colours themselves are
  in the nameplate SWF; the three-way split is [P].
- Other readers of `+0x147c == 0` [P]: FUN_142d11210 (clicking an entity with no target selects it only when hostile),
  FUN_142d4e890 `BaseClient::SetCurrentTarget` (combat targeting branch for hostile, non-ally targets),
  FUN_142ea9e60 / FUN_142ec5740 / FUN_142e96ef0 / FUN_142ec7ce0 ("PC, or highlighted (+0x1457 & 2), or hostile and not
  +0x1460 & 4" → a render-object flag bit 0 and creation of a per-entity component in FUN_142e93f50), FUN_14331f7c0
  (filter mode 3 accepts dispositions 1-2). [I] these are targeting / attackable-marker paths: a friendly or neutral
  creature is not auto-targeted on click and may lose the hostile marker; our combat hit tests are server-side
  (Creatures.Swing / Strike / Shoot by aim) so players can still provoke neutrals. To verify in game (open question).
- Values match the story spawner's Reaction (Creatures.Faction 0 enemy / 1 neutral / 2 ally) one to one.

## 4. LightweightToFullNpc (PlayerUpdate 0x26 sub 0x5C) — 0.695 reader [P]

Reader FUN_142c4c3d0 → FUN_142c28ba0 (exact-length check). Header u16 base +0x8, **u32 sub +0x10** (0.695). Then,
identical to 0.589 FUN_142b8f230:

| Field (packet offset) | Type | Apply FUN_142d532f0 → entity | Safe default (what we send) |
|---|---|---|---|
| +0x18 transient id | uint, 2-bit length (FUN_142c0bd80) | lookup (FUN_142f85340); handler needs lightweight flag +0xcb4 ≠ 0 | creature's transient id |
| +0x1c, +0x20, +0x24 | u32 ×3 | +0x219c, +0x21a0, +0x21a4 (unconditional) | 0 |
| +0x28 attachments | i32 n + n × CharacterAttachmentData (FUN_142c185c0) | n < 1 → FUN_142ebdae0(e, 1) (keep look); else apply + FUN_142e9a050(+0x48) | 0 |
| +0x48, +0x60, +0x78 | strings (FUN_142d4b5d0) | — | "" |
| **+0x90** | u32 | **+0x147c disposition** (unconditional) | **0 / 1 / 2 by faction (new)** |
| +0x94, +0x98 | u32 | +0x94 → +0x158c | 0 |
| +0x9c, +0xa8 | 3 × u32 each | FUN_142ec4960 / FUN_142ec2ae0 | 0 |
| +0xb4 | u32 | +0x740 (the ExpectedSpeed field, case 0x10) | 0 |
| +0xb8 | u32 | actor +0x6b8 | 100 |
| +0xbc | u32 text id | name (FUN_142ec6110) when the text exists | 0 (keep spawn name) |
| +0xc0 | u32 | +0x24a0 when ≠ 0 | 0 |
| +0xe8 | {u32, string, string, u32, string} FUN_142c1e6f0 | actor FUN_143cfc410 | 0 / "" |
| +0xd0 | 4 × u32 | FUN_142ec4e60 (only with +0x1458 bit 0x80 and +0x2450 == 0) | 0 |
| +0xe0, +0xe4 | u32 | +0xe4 → +0x1498 | 0 |
| +0x140 | u64 | +0x4050 | 0 |
| +0x148 | u32 | — | 0 |
| +0x150 | Target (vtable +8 reader; u8 kind, 0 = none) | — | 0 |
| +0x168 | list FUN_142c3d010 | — | 0 |
| +0x208, +0x20c, +0x210 (4 × u32 FUN_142c02830) | u32, u32, list | resource setup FUN_142ec45d0 when +0x208 > 0 | 0 |
| +0x220 .. +0x240 | 9 × u32 | +0x22c → +0x4140, +0x230 → +0x2198, +0x234 FUN_142ec19d0, +0x238 → +0x149c, +0x23c → +0x14a0, +0x240 → +0x14b4 | 0 |
| +0x244, +0x248 | i8, i8 | +0x244 → +0x14b0 | 0, 2 |
| +0x24c, +0x250, +0x254 | u32 | +0x250 → +0x4144 when ≠ "unset" DAT_1451f31c8; +0x254 → +0x4148 | 0, 0xFFFFFFFF, 0xFFFFFFFF |
| +0x2c8 | list FUN_142c3cdd0 | FUN_143b8aef0(+0x4268) | 0 |
| +0x258, +0x260 | u64, u32 | +0x7d8 / +0x7e0; with a guid and count > 0 → FUN_142ed1540 | 0, 0 |
| +0x298 | list FUN_142c3fa30 | vtable +0x238 | 0 |
| +0x2e0 | byte array FUN_142d4b2b0 | parsed only for the prop path (param_4 = 1) | 0 |
| +0x264, +0x268 | u32 | FUN_142ec0310; +0x4aa8 | 0 |
| +0x26c, +0x270 | f32 | both > 0 → FUN_142e6cbe0 | −1, −1 |
| +0x278 | list FUN_142c34c30 | FUN_142ec4990 | 0 |

Handler FUN_142cdf400 [P]: entity found and +0xcb4 ≠ 0 → apply → **+0xcb4 = 0**. A second LightweightToFullNpc for
the same entity is ignored, so it cannot carry a later change of side.

Client request side [P]: FUN_142e1f220 sends FullCharacterDataRequest once per entity (+0xcb6 latch); FUN_142ee8540
promotes the nearest lightweight entities, limits from the manager constructor (part_0078: +0x282d8 = 50 full
entities, +0x282dc = 10 requests per frame, +0x282e0 = 300.0 [I] metres). So nearby creatures turn full within a
frame or two of spawning; until then they read 2 (friendly).

## 5. PlayerUpdatePacketSetDisposition (0x26 sub 0x14) [P]

- Ids: cPlayerUpdatePacketIdSetDisposition = 0x14 (re/out/packet_ids.tsv); RTTI vftable 1446a9830 (rtti_695.tsv);
  0.589 reader FUN_142bb6160.
- Reader FUN_142c4f960 → FUN_142c2b5a0: u16 base, u32 sub, **u64 guid (+0x18), u32 disposition (+0x20)**; nothing else.
- Handler (FUN_142cdfa60 case 0x13): entity by guid (FUN_142f85390) → FUN_142d50410 (+0x147c = value); if the entity
  is the current target (GetCurrentTarget FUN_142ccbc00) and value ≠ 0 → SetCurrentTarget(empty) (target cleared).
- Wire (0.695): `26 00 | 14 00 00 00 | guid u64 | disposition u32` = 18 bytes.

## 6. What the server does now

- `Creatures.SendFull` (answer to FullCharacterDataRequest) puts `DispositionOf(c)` in +0x90: non-story creatures
  Hostile (0); story creatures by faction (0 enemy → 0, 1 neutral → 1, 2 ally → 2). No extra packet.
- `Creatures.StoryFaction` sends SetDisposition to the creature's viewers only when the faction really changes (the
  Spawn → Reaction call in StoryTools.Spawn included, so a just-spawned neutral is right before its full data comes).
- Pets (Game/Pets.cs): unchanged. They are lightweight NPCs that never get full data (PlayerHandler's chain has no pet
  answer), so they keep the constructor's 2 = friendly (HighlightMode 2), which is right for a pet. [P] for the value;
  [I] that they show non-red (not yet seen in game).
- Props: LightweightToFullProp also runs FUN_142d532f0 (FUN_142cdf560), so placed props get +0x147c = its +0x90 (we
  send 0 → "hostile"). No visible effect known; props have no nameplate. [I]

## 7. Open

- Whether neutral creatures (1) can still be attacked through every client path (ability target validation
  FUN_1437xxxx group uses FUN_142ea9e60) — check in game: hit a neutral story creature with a melee weapon and a
  ranged tool.
- The actual colours behind HighlightMode 0 / 1 / 2 (nameplate SWF) — read from re/as3_695 when needed.
- Wild animals with no damage (Deer) are sent Hostile like every non-story creature; live's value is unknown (L85).
