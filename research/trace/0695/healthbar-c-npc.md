# Health bars C — NPC name / health indicator (client 0.695)

2026-10-31, read-only trace (no game, no Frida, no Ghidra). Sources: `re/decomp_all_695` (fn.py), `re/out/rtti_695.tsv`,
`gameData695`, server `LandmarkEmu/LandmarkEmulator.WorldServer`. **[P]** = proven (FUN_ address + what the code does,
or file:line), **[I]** = inferred.

Bug report part: "health bars did show up for NPCs that were attackable but only on NPCs I had aggro'd then ran away
from."

## 0. Existing research (read first)

- `LandmarkEmu/docs/gameplay.md:760-762` and `Game/Creatures.cs:2373-2387` (SendLife comment): requirement 21 "NPC
  Combatant", `TargetCanBeHarmed` = TargetMaxHealth > 0, Life resource sent at spawn and on every hit.
- MODLOG 2026-10-11 (line 1165): Frida walk of requirement 21 on live 0.589 client: IsPc, IsProp, IsProfile 39 pass,
  TargetMaxHealth failed until a Life resource was sent; "Verified: 'Kobold' nameplate + green bar that drops on
  hitnear". Addresses in that entry are 0.589.
- MODLOG 2026-10-15 (line 2135) "CREATURE FULL DATA": FullCharacterDataRequest for creatures answered with
  LightweightToFullNpc (all client defaults, every list empty).
- `trace/0695/full-npc-695.md`: disposition +0x147c (constructor 2 = friendly, full data +0x90, SetDisposition),
  hostility FUN_142ea9630, LightweightToFullNpc field table (row "+0x2c8 list → FUN_143b8aef0(+0x4268)").
- `trace/0695/gear-stats-695.md` §1: target bar = FUN_143711bb0 → FUN_143b8c310(target+17000, type 1).
- Sister notes of this run: `healthbar-a-health-read.md` §4-5 (independently found the full-data resource wipe),
  `healthbar-d-sources.md` §5 (no live capture with per-entity resource traffic on disk), `healthbar-e-server.md`.
- `trace/client-constants.md:551-553`: NpcNameplate.CloseProximityRange 25 / MaxPromximityRange 100 (0.589
  FUN_14366b590 = 0.695 **FUN_143726eb0**, address-map-589-695.md:1983).

This note adds: the whole indicator pipeline for code 22, the exact health-bar visibility rule (hostile vs not), and
why "aggro then run away" (not only "hit it") brings the bar back.

## 1. HUDIndicatorSettings: what each TYPE_ID does [P]

Loader FUN_142fe8980 (part_0080): rows with TYPE_ID 1..13 go into a 32-bucket HashListMap (manager at game +0x341a0,
ctor part_0063 ~27352). Record: +0x8 TYPE_ID, +0xc PARAM1 (= indicator CODE_ENUM_TYPE), +0x10 CLIENT_REQ_ID; bucket key
`type & 0x1f`, chain next +0x30, **new rows pushed at the bucket head**, so a type's rows are walked in **reverse
file order**.

Three lookups (part_0104 / part_0105), all evaluate the requirement with FUN_1432f5870(reqMgr, reqId, localPlayer
(EntityMgr +0x282c8), entity, &guid); req 0 = always true:

| Function | Meaning |
|---|---|
| FUN_14343b0b0(…, type) | PARAM1 of the **first** row of that type whose requirement passes (reverse file order) |
| FUN_14343b1c0(…, type) | largest PARAM1 among passing rows |
| FUN_14344a890(…, type, code) | does a row of that type with PARAM1 == code exist and pass |

Per-entity driver **FUN_143444620** (called from the entity tick FUN_142eb6440 when entity +0x1457 bit 8 is clear):

1. Indicator kind: cached at **entity +0x14b8**. While it is 0 / -1: `code = FUN_14343b0b0(type 13)`; code 0 or ≥ 255
   → cache -1 and stop this frame (so it is retried every frame); else cache the code (sticky).
   Type-13 rows in walk order: 27 (req 27 IsInstancedGameProp), **22 (req 21 NPC Combatant)**, 19 (req 11 TargetIsHUB),
   18 (req 13 prop indicator), 1 (req 3 TargetIsPlayer).
2. Per frame: `FUN_14344a890(type 1, code)` must pass, else no indicator update this frame. Type-1 row for code 22 is
   **1^22^21** → requirement 21 again, every frame.
3. Path: HUD manager +0x580 set **and** code ∈ {1, 13, 18, 21, 22} (mask 0x642002) or code 27 → **native** indicator
   (FUN_14343d330; code 1 → 0x1c0-byte object FUN_143436f40, others 0x1a0-byte FUN_143436fd0), update vtable +0x278
   (throttled) and +0x270 (each frame). Otherwise the Flash (PS2 Hud.swf) indicator, vtable +0x290 / +0x288.
   HUD indicator manager ctor **FUN_1434369a0** ends `*(byte*)(this+0x580) = 1` → **native path is the default**
   (toggles FUN_14343a5e0 / FUN_143439b70 / FUN_143447510 have no code callers in the decompile).

Inside the native update for characters (ProxiedCharacterIndicator vtable 144772de0 slot 79 = **FUN_143449e00**;
PlayerIndicator slot 79 FUN_143449b40 calls it too), with `hostile = FUN_142ea9630(localPlayer, entity)`:

| Indicator field (setter) | Rule [P FUN_143449e00] | Row(s) for code 22 |
|---|---|---|
| +0x38 (vt +0xf8) | vt +0xb8 or type 7 passes | none for 22 (7^6^0, 7^18^2) |
| +0xd8 (vt +0x120) | type 8 | none |
| **+0xd0 name shown** (vt +0x110, FUN_143446ac0) | (entity +0x1454 bit 0x40 clear **and** (ind +0xd9 or type 2 passes)) or HUD +0x518 (Story Tools link editing) | **2^22^39** "NotPlayer && ShowProxyName" |
| +0x3b (vt +0x150) | type 9 | none |
| +0xf6 (vt +0x180) | type 11 | none |
| **+0xfc health bar shown** (vt +0x190, FUN_1434471e0) | entity +0x1454 bit 0x80 clear **and** Life max (FUN_143b8c310(+0x4268, 1)) ≥ 1 **and** (**hostile** or type 3 passes) | **3^22^2** TargetInjured |
| +0x100 health % (vt +0x198), +0x104 (vt +0x1a0), +0x108 type 2 %, +0x10c type 11 (armour) % | FUN_143b8c370(+0x4268, type) = current·100 / max | — |
| +0xf8 relation (vt +0x1e8 colour) | 1 not hostile; 2 / 3 hostile (3 when local player state bit 41 / instanced game) | — |

So for an NPC (code 22) [P]:
- **No indicator at all** unless requirement 21 passes: NotPlayer && NotProp && **TargetMaxHealth > 0** &&
  TargetNotPropProfile. TargetMaxHealth = ClientRequirementTargetMaxHealth slot 9 **FUN_143711bb0** →
  FUN_143b8c310(target + 17000 = +0x4268, type 1): resource of type 1 in the entity's container, max = MAX_VALUE
  variable 0x2C7CD6E6 or Resources.txt MAX_VALUE (FUN_143217a00); **no record → 0**.
- **Hostile NPC: health bar always shown** (full health too). **Neutral / friendly NPC: bar only while injured**
  (TargetHealthPercent < 100; ClientRequirementHealthPercent slot 9 FUN_143710070 → FUN_143b8c370 = cur·100/max, integer).
- Name shown when the spawn's name-hidden bit is clear (requirement 39). Not tied to targeting, combat or aggro.
- Spawn flags [P FUN_142f8a0b0 create-entity]: spawn struct +0x599 bit 2 → entity +0x1454 bit 0x40 (name hidden,
  FUN_142ec28c0), bit 1 → +0x1454 bit 0x80 (health bar hidden). We send the flag bytes as 0 (both visible).

## 2. What the Npc indicator draws [P]

HudIndicators row `Npc^15^22`: PARAMn sits at definition +0x50 + (n-1)·4 (FUN_143727210 reads +0x64/+0x6c/+0x70 =
PARAM6/8/9 = UiColors 33/34/35).

- Definition ctor **FUN_143726eb0**: +0x328 = NpcNameplate.MaxPromximityRange (hash 0x9366C32D, default 100.0),
  +0x32c = CloseProximityRange (hash 0xCA30CF00, default 25.0).
- Colours FUN_143727210: +0x330 = UiColors 33 **FFFFFF** (not hostile), +0x334 = 34 00FF00, +0x338 = 35 **FF0000**
  (hostile) (gameData695/UiColors.txt).
- Draw = HudIndicatorDefinitionNpc vtable 1447dc270 slot 1 **FUN_143727270** (needs DAT_1452a2a19, HUD +0x57d / +0x57e
  clear):
  - name alpha: 0xFF up to 25 m (indicator +0xd4 distance), linear fade 25 → 100 m, 0 beyond 100 m; colour white /
    red by FUN_142ea9630; second line from vt +0x108 (guild / title) [I meaning].
  - **health bar**: when PARAM19 (+0x98 = 1848) ≠ 0 and indicator +0xfc: copies +0x100 / +0x104 / +0x10c into the
    definition and calls **FUN_143725870**, a Deep::ProgressMultiBarTextured with frame image vt +0x48 (PARAM18 1851),
    bars vt +0x50 (PARAM19 1848, health; swapped for vt +0x68 = PARAM22 1894 when **not** hostile), vt +0x58 (PARAM15
    1850) and vt +0x60 (PARAM17 1849). The bar has no distance fade in this function.
- Per-frame visibility (vt +0x270 = FUN_143449190 → vt +0x290 = FUN_14344a820): shown while the actor is rendered
  (FUN_142ed0620 state < 3 and FUN_142e9e5a0 ≠ 2), or always with HUD +0x57d / +0x57e.

So live's look for a wild (hostile) creature in range: red name over a health bar, always, the name fading out between
25 and 100 m. Neutral / ally creatures: white name, bar only when hurt. [P for the code; I that live's creatures were
hostile in the full data — consistent with our DispositionOf and with a Dec 2014 player post (forum 51046 #263:
"some of the mobs seem to have Dev level invincibility atm ie. no health bar") implying mobs normally had a bar.]

## 3. The full-data apply wipes every resource [P]

- LightweightToFullNpc handler FUN_142cdf400 → **FUN_142d532f0**, first line:
  `FUN_143b8aef0(entity + 0x84d*8 = +0x4268, packet + 0x2c8)`, unconditional.
- **FUN_143b8aef0** first removes every record (`while (head) FUN_142eb9780(...)`), then creates one record per list
  entry (key = record type at +0xe8).
- The list reader **FUN_142c3cdd0**: `u32 count`, then count × **FUN_142c18870** — the same record reader as
  ResourcesBase 0xC4 sub 2 SetCharacterResource (CharacterResources.SendFor body after the guid).
- The handler runs once per entity (+0xcb4 cleared), so it wipes once per spawn of the entity on that client.

## 4. The server today and the player's observation [P unless marked]

Order for one player and one wild creature:
1. `Creatures.SendSpawn` (Creatures.cs:2272-2275): AddLightweightNpc, then **SendLife** (Creatures.cs:2380-2387,
   SetCharacterResource type 1, value scaled to a Resources.txt Life row). Entity +0x147c = 2 (friendly).
2. The client asks FullCharacterDataRequest for the nearest ≤ 50 entities (full-npc-695.md §4), a frame or two later.
3. `Creatures.SendFull` (Creatures.cs:1486-1504) answers LightweightToFullNpc: disposition 0 (hostile, DispositionOf
   :1571) and **`writer.Write(0u); // list +0x2c8`** (PlayerUpdateLightweightToFullNpc.cs:91). SendFull does not send
   Life again.
4. The client's FUN_143b8aef0 wipes the Life record → TargetMaxHealth 0 → requirement 21 fails → type-1 gate fails →
   **no name and no bar** (and the type-13 kind may stay -1 and be retried each frame).
5. Anything that later calls **SendHitpoints** (Creatures.cs:2446-2453: UpdateHitpoints **and SendLife** to every
   viewer) puts the record back on a full entity; requirement 21 passes and, the creature being hostile now, **the bar
   stays visible even at full health**:
   - a hit (Hit :1762, no-impact Wound :1645, creature-on-creature HurtCreature :3208);
   - **the leash heal**: State.Return at home → `c.Health = c.Kind.MaxHealth; SendHitpoints(c, 0);`
     (Creatures.cs:877-884). This is the "aggro'd then ran away" case exactly — the creature chased, the player ran past
     LeashRange 40 m, it walked home, healed and re-sent Life. No hit is needed;
   - healer heals (TryHeal :2808 → SendHitpoints :2832), training dummy mend (:3230).
6. Re-introduction does not keep it: leaving ViewRange 120 m removes the entity; coming back sends spawn + Life, the
   client makes a new entity, asks for full data again, and the empty list wipes Life again. A respawn (Respawn :1902,
   new transient id) also starts over.

Timeline: bars verified 2026-10-11 (creatures were never promoted to full then, so the spawn's Life stayed); full NPC
data added 2026-10-15 → regression. [P MODLOG lines 1165, 2135]

The failing condition for fresh NPCs is therefore **TargetCanBeHarmed (TargetMaxHealth > 0) inside requirement 21**,
because the Life record sent at spawn is erased by our own empty LightweightToFullNpc resource list. Not ProfileId 39,
not the name-hidden flag, not "injured". (Injured only matters for neutral / ally creatures, see §1.)

Side findings [P]:
- `Creatures.DamageStory` (:3599) and `HealStory` (:3616) (Story Tools Damage Node on a creature,
  StoryTools.cs:994-1001) change `c.Health` without SendHitpoints, so viewers' bars and hit points go stale.
- Still-lightweight creatures (beyond the 50 nearest full ones) keep the spawn's Life and +0x147c = 2: name in white,
  bar only when hurt. Rare in practice (ViewRange 120 m < full range 300 m) [I].

## 5. What live sent [P absent / I]

- No live capture with per-entity resource, hit-point or LightweightToFullNpc traffic is on disk
  (healthbar-d-sources.md §5: PacketDumps and NetworkCaptures/_research/combat-loot-probe hold only loot rows of the
  Courage session 5IewoKPQE2BqKg6Q; the raw bins are not in the workspace). [P absent]
- [I] Live's LightweightToFullNpc must have carried the NPC's resources in +0x2c8: the client clears the container and
  rebuilds it from that list on promotion, and live creatures (promoted like ours, nearest 50) had health bars.

## 6. Server changes

1. **Creature full data carries its Life** (fixes the report): in `Creatures.SendFull`, write the list +0x2c8 as
   `u32 1` + one FUN_142c18870 record: `u32 resource id (LifeRow row), u32 type 1, u32 variable count (0, or 1 +
   {u32 key 0x2C7CD6E6, u32 id 0x2C7CD6E6, f32 base, f32 max-base}), u32 current, 9 × u32 0, u32 0, 4 × u64 0,
   3 × bool false, u32 0` — byte-for-byte the SetCharacterResource body after the guid (CharacterResources.cs:60-97).
   About 95 bytes in a packet that is sent anyway, no extra packet or sequence number. Needs
   PlayerUpdateLightweightToFullNpc to take an optional resource record (share the record writer with
   CharacterResources). Lower-risk alternative already proven by the player's observation (the leash heal works on a
   full entity): call `SendLife(session, c)` right after enqueueing LightweightToFullNpc (~110 bytes, one more packet
   and sequence).
2. Keep `SendLife` in `SendSpawn` (entities that stay lightweight need it; the Spawn → Full order is fixed by the
   client asking only after the spawn).
3. `DamageStory` / `HealStory`: call `SendHitpoints(c, damage or 0)` after the change (only when the value changed).
4. Nothing to add for hostility: wild creatures are already 0 in the full data, so after (1) their bar shows at full
   health as live's client draws it; neutral / ally story creatures show the bar only when hurt (client rule).
5. Bandwidth: no periodic sends; Life stays event-driven (spawn, full data, hits, heals). Optionally skip the spawn
   SendLife for clients that will promote the creature at once — not recommended (the client decides who is promoted).
6. Verify by the client's reaction (verify-client-results): a fresh wild creature shows red name + bar with no hit; a
   neutral story creature shows a white name and no bar until hit; leash home keeps the bar.

## 7. Open questions (what to try next)

- Image ids 1848 / 1849 / 1850 / 1851 / 1894 (bar textures): Images table is not in gameData695 — read the client
  pack's Images / ImageSets, or screenshot after fix (1848 hostile vs 1894 non-hostile colour).
- Which AddLightweightNpc wire byte is spawn struct +0x599 (bits 1 / 2 = bar / name hidden): read the spawn reader
  chain (FUN_142c43fe0 / FUN_142b8cfc0 0.695 partners) to map "flag bytes"; useful for pets (profile 379 non-combat).
- Whether the full-data record path (FUN_143b8aef0 add) fires the same type-1 notifications as SetCharacterResource
  (FUN_14354f770 → health event / target refresh): HUD indicators poll every frame so it should not matter; confirm in
  game with the target frame on a freshly promoted creature.
- Live's exact LightweightToFullNpc resource list for creatures (Life only, or Life + armour type 11 / Energy): look for
  the Courage session raw bins (`5IewoKPQE2BqKg6Q_session/…/bins`) if they turn up; add to OPEN-LEADS L85.
- Flash fallback FUN_143449560 (used only with HUD +0x580 = 0): its type-3 loop takes the first type-3 row without the
  code compare in the decompile — irrelevant on the default native path; re-read only if someone toggles it.
