# Health bar task A: how the 0.695 client reads a target's health

Traced 2026-10-31 from the 0.695 decompile (`re/decomp_all_695`, `DECOMP=decomp_all_695 python re/tools/fn.py`),
`re/out/rtti_695.tsv`, `gameData695` and the server source. Nothing was run. No game, Frida or Ghidra was used.
[P] = proven from client code, client data or our source (FUN_ address or file:line). [I] = inferred.

## 0. What already existed

- `trace/0695/gear-stats-695.md:12-40`: the resource container at entity +17000 (+0x4268). Max = FUN_143b8c310 →
  FUN_143217a00 (variable 0x2C7CD6E6 base + modifier, otherwise Resources.txt MAX_VALUE). It also has the
  SetCharacterResource (0xC4 sub 2) record layout. Line 15 already says the target bar uses ClientRequirementTargetMaxHealth.
- `LandmarkEmu/docs/gameplay.md:759-762` and `Game/Creatures.cs:2368-2387`: requirement 21 "NPC Combatant" and the
  creature Life resource (SendLife).
- MODLOG 2026-10-11 (line 1165): "NPC NAME/HEALTH BARS DONE … Verified: 'Kobold' nameplate + green bar". The addresses
  in that entry are 0.589 ones (FUN_143656820 / FUN_143ad0540). MODLOG 2026-10-15 (line 2135) added "CREATURE FULL
  DATA": LightweightToFullNpc answers for creatures. See §5 for why that change matters.
- `trace/0695/full-npc-695.md:77`: the LightweightToFullNpc list at +0x2c8 goes to FUN_143b8aef0(+0x4268).
- `trace/0695/agent-collision-history.md` B.1/B.4: the ProxiedProp ctor, SpawnProxiedProp, and the LightweightToFullProp
  handler, which calls the same full apply as the NPC one.
- Nothing yet on how the factory keys map to the requirement names, how the percent is computed, the fact that ProxiedProp
  is a ProxiedCharacter, or the wipe in §5.

## 1. Requirement factories: name → class → evaluate function

The factory key is the **one-at-a-time hash of CODE_FACTORY_NAME** (Requirements.txt column 3). This is the same hash as
`re/tools/strhash.py oaat()`: `oaat("TargetMaxHealth") = 0xfb2fb598` and `oaat("TargetHealthPercent") = 0xbdab6175`. The
TYPE column (35, 4036) is not the key. The registrations are in FUN_1432b1460 (part_0095):
- `FUN_14329ea70(f, 0xfb2fb598)` registers `ClientRequirementTargetMaxHealth` (part_0095:2972).
- `FUN_1432995b0(f, 0xbdab6175)` registers `RequirementAutoInitializeParam<ClientRequirementHealthPercent,1>` (part_0095:3290).

FUN_1432eec90 stores `key ^ 0xb1432a7c`. The lookup in FUN_1432e59a0 (part_0097:10313) uses the same xor. [P]

| Requirement (Requirements.txt) | Class (vtable) | Evaluate (vtable slot 8) | Reads |
|---|---|---|---|
| 5 TargetInjured, 18 TargetAlive, 65 TargetNotInjured: TargetHealthPercent | `RequirementAutoInitializeParam<ClientRequirementHealthPercent,1>` 144746878. Its ctor FUN_1432ce110 sets byte +0x88 = 1 (the `,0` variant FUN_1432ce160 sets 0). | **FUN_143710070**: +0x88 = 1 → entity = context[1] (target), 0 → context[0] (self). Then `FUN_14370e4b0(this, FUN_143b8c370(entity+17000, type 1), PARAM_1 +0x30)` | percent of resource **type 1** |
| 46 TargetCanBeHarmed: TargetMaxHealth | `ClientRequirementTargetMaxHealth` 144741af8 | **FUN_143711bb0**: `FUN_14370e4b0(this, FUN_143b8c310(context[1]+17000, type 1), PARAM_1)` | maximum of resource **type 1** |

[P]

- **Context:** FUN_1432f5870(reqMgr, reqId, self, target) builds `{self, target}` and calls the expression. The HUD
  indicator code passes (local player = EntityManager +0x282c8, the indicator's entity), so "Target" means **the entity
  whose indicator is being drawn**, not the selected target. [P]
- **Operator** FUN_14370e4b0 switches on (OPERATOR at +0x2c) − 1: 1 `==`, 2 `!=`, 3 `<`, 4 `<=`, 5 `>`, 6 `>=` (value vs PARAM_1).
  - TargetInjured = percent < 100.
  - TargetAlive = percent > 0.
  - TargetCanBeHarmed = max > 0. [P]
- **Null target:** both evaluate functions return false. [P]

## 2. The fields read (entity +0x4260 / +0x4268)

- **Container:** entity +0x4260 is `Resources::ProxiedCharacterResourceManager` and entity +0x4268 (= 17000) is
  `HashListMap<int, Resources::CharacterResource, 8>` (ctor FUN_142e8a0a0). It has 8 buckets at +0x28 + (type & 7)*8, a
  record list head at +0x10, a listener at +0x68 and a listener flag at +0x70. The owner is set by FUN_143550680(entity+0x4260, entity),
  which writes +0xe8 = owner. [P]
- **Record (CharacterResource, 0xd8 bytes + map links):** +0 resource id, +4 resource type, +0x60 variable list,
  +0x70 current value, +0xe8 map key = resource **type**, +0xf0 next. [P]
- **Current:** FUN_143b8c2b0 returns record +0x70, or 0 when there is no record. [P]
- **Maximum:** FUN_143b8c310 → FUN_143217a00(record). It walks the record's variables for id 0x2C7CD6E6:
  - Found → `(int)(base + modifier)`. A value of 0 or near 0 gives 0, and a negative value gives 0.
  - Not found → `FUN_143c77750(record)` (Resources.txt row looked up by **record +0 resource id**, through manager
    DAT_1455fd138) → **+0x4c MAX_VALUE**. This dereference has **no null check**, so a Set with no MAX variable and a
    resource id that is not in Resources.txt crashes the client. No record of the type at all → 0. [P]
- **Percent:** FUN_143b8c370 = `(current * 100) / max` (integer division), or 0 when there is no record or max < 1. [P]
  So:
  - TargetInjured ⇔ current < max.
  - TargetAlive ⇔ current*100 ≥ max, i.e. current ≥ ⌈max/100⌉.
  - TargetCanBeHarmed ⇔ the max above is > 0. [P arithmetic]

## 3. Does a lightweight prop (tree) have the container?

**Yes. A ProxiedProp *is* a ProxiedCharacter:**
- SpawnProxiedProp FUN_142f8a800 ("ProxiedCharacterManager::SpawnProxiedProp") allocates 0x4f20 bytes and calls the
  ProxiedProp ctor FUN_1430a7760. [P]
- That ctor first calls **FUN_142e88840**, the ProxiedCharacter ctor. FUN_142e88840 writes the ProxiedCharacter vtables,
  builds the resource manager at +0x4260 (FUN_142e8a0a0) and links it (FUN_143550680(param_1+0x84c, param_1)). Only then
  does FUN_1430a7760 overwrite the vtables with ProxiedProp's and initialise its own fields from +0x4b50. [P]
- AddLightweightNpc (handler FUN_142cddaa0, "BaseClient::HandlePlayerUpdateAddLightweightNpc", "Failed to spawn
  ProxiedCharacter …") makes a ProxiedCharacter through the same ctor. [P for the string, I that it is the same ctor path]
- **The prop is reachable by guid** in the table every resource packet uses:
  - SpawnProxiedProp inserts the new prop with `FUN_142f861f0(mgr + 0x30, guid, entity)`.
  - FUN_142f85390(mgr, guid) walks the same table (buckets mgr +0x50, % 0x1003, guid at entity +0x2b0, next at +0x2b8).
  - Both run on BaseClient +0x31e60. [P]
- **Neither spawn packet carries health:**
  - The AddLightweightProp handler FUN_142cde1c0 and the shared lightweight apply FUN_142d54040 never touch +0x4260/+0x4268.
  - The only definition lookup there is FUN_143b88ba0(+0x64 model def) → model name and scale.
  - So **a fresh tree or NPC has no type-1 record: max 0, percent 0. All three requirements are false.** [P]

## 4. Every server→client path that writes (or does not write) those fields

| Packet | Client path | Effect on +0x4268 | Tag |
|---|---|---|---|
| **ResourcesBase 0xC4 sub 2 SetCharacterResource** `{u32 seq, u8 2, u64 guid, record}` (server `Game/CharacterResources.cs:60-97`, record reader FUN_142c18870) | sub switch FUN_14361c300 → FUN_14361c410 (also FUN_14361ccd0 for queued ones): entity = FUN_142f85390(BaseClient+0x31e60, guid) → **FUN_143550610(entity+0x4260, record)** finds the record by type. Found → copy over it (FUN_14354f240 clears the variables first). Missing → create (FUN_143b8ac50). Then FUN_14354f770 notifies: for type 1 it calls FUN_142d11210 (health event) and FUN_142ccbc00 / FUN_142ccbda0 (target refresh). The sequence check is FUN_14361cd30 (one rising counter per receiving client). | **creates/sets the record; works on any entity in the guid table, lightweight or full, NPC or prop** | [P] |
| ResourcesBase 0xC4 sub 1 SetCharacterResources (list) | FUN_14361c300 → FUN_142f85390 → FUN_14354fa00(entity+0x4260): FUN_14354ef20 **clears all** (FUN_142eb9780), then reads u32 count × record (FUN_142c18870) | replaces the whole set (not used by the server) | [P] |
| ResourcesBase 0xC4 sub 3 UpdateCharacterResource | FUN_14361c640 → FUN_143550820 (gear-stats-695.md) | updates an existing record (not used) | [P per gear-stats; not re-read] |
| **PlayerUpdate 0x26/0x5C LightweightToFullNpc** and **0x26/0x63 LightweightToFullProp** | FUN_142cdf400 (the Prop handler FUN_142cdf4b0 calls it): needs +0xcb4 ≠ 0 → **FUN_142d532f0** whose first line is `FUN_143b8aef0(entity + 0x84d*8 = +0x4268, packet + 0x2c8)`. FUN_143b8aef0 **first removes every record** (`while (head) FUN_142eb9780`), then adds one per list entry (u32 count × FUN_142c18870 record, reader FUN_142c3cdd0, key = entry +4 type). Then +0xcb4 = 0, so it happens once per spawn. | **wipes all resources, then installs the list** | [P] |
| **PlayerUpdate 0x26/0x03 UpdateHitpoints** `{u64 guid, u32 a(+0x20), u32 b(+0x24), u32 c(+0x28)}` (reader FUN_142c4dc60 → FUN_142c2c9a0) | dispatcher part_0065:18359 case 2: skips the local player. If entity +0xa78 exists it queues a PlayerHitpointEvent (FUN_1432707e0). When that fires, FUN_142d5c780 → FUN_1433fd710 calls Lua `CombatGroupHandler:SetHealth(guid, a clamped 0..100, 100, falling)`, the group frame. It also refreshes the current target (FUN_142ccbc00, BaseClient +0x6564*8 vtable +0x40). For a non-local entity it **reads** FUN_143b8c370 to decide "falling". | **does not write the container**: no effect on TargetHealthPercent / TargetMaxHealth | [P] |
| ClientUpdate 0x29/1 Hitpoints | combat text only (gear-stats-695.md §1) | none | [P per gear-stats] |
| UpdateStat 0x26/0x53 | stats at entity +0x7e8 | none (MODLOG 10-10 test agrees) | [P per gear-stats] |
| AddLightweightProp / AddLightweightNpc | §3 | none | [P] |

## 5. Why NPC bars appear only after a hit (current server)

1. `Creatures.SendSpawn` (Creatures.cs:2272-2275) sends AddLightweightNpc and then **SendLife**, a SetCharacterResource
   of type 1. [P]
2. The client then asks for full data for the nearest entities (FUN_142ee8540 / FUN_142e1f220, full-npc-695.md). The
   server answers with `Creatures.SendFull` (Creatures.cs:1486-1504): LightweightToFullNpc with **`writer.Write(0u); // list
   +0x2c8`** (PlayerUpdateLightweightToFullNpc.cs, the line before `+0x258`). SendFull does not send Life again. [P]
3. The client's FUN_142d532f0 → FUN_143b8aef0 wipes the Life record (§4). TargetMaxHealth is now 0, so requirement 21
   "NPC Combatant" (`… && TargetCanBeHarmed && …`) fails and there is no Npc indicator (HUDIndicatorSettings 1^22^21, 13^22^21). [P]
4. A hit runs `SendHitpoints` (Creatures.cs:2446-2453): UpdateHitpoints, then SendLife. The record is back, current <
   max, so TargetCanBeHarmed and TargetInjured both pass and the name and bar show from then on. This matches "only NPCs I had
   aggro'd then ran away from". [P chain, I that this is what the player saw]
5. Timeline: bars were verified on 10-11, before creatures had full data. The full data came on 10-15 → regression. [P
   MODLOG 1165 / 2135, I cause]

**Trees today** (`Gathering.cs:878-889` SendFull → LightweightToFullProp with an empty list at
PlayerUpdateLightweightToFullProp.cs:58, `// list (0xd8-byte entries)`): no resource is ever sent for a node
(Node.Hits / MaxHits only), so all three requirements are false for every tree. [P]

## 6. The data: Resources.txt already has the tree and plant health pools

Name texts are from re/out/text_695.tsv. All rows are RESOURCE_TYPE 1. [P]

| ID | Name | INITIAL | MAX |
|---|---|---|---|
| 18 | Tree - Very Small | 350 | 351 |
| 16 | Tree - Small | 750 | 751 |
| 14 | Tree - Medium | 1500 | 1501 |
| 15 | Tree - Large | 2250 | 2251 |
| 21 | Tree - Very Large | 3000 | 3001 |
| 17 | Plant | 10 | 11 |
| 20 | Plant | 20 | 21 |
| 22 | "Life" | 500 | 501 |

The other type-1 rows (2-12, 23, 25-28, 40) have INITIAL = MAX.

- **INITIAL = MAX − 1** on every tree and plant row. An untouched tree at INITIAL has percent `1500*100/1501 = 99`, so
  TargetInjured holds at once. The bar then follows only requirement 13's other terms: IsHarvestTarget for trees (P0123
  2014-02-12 "only see the health bars of trees you're currently harvesting"), or ≤ 7 m for plant profiles 90/91/296.
  [P data, I design intent]
- P0473 "Tree health has been rebalanced to better match tree size" fits the five size rows. Forum 2014-03-21 "how stats
  work" (design/forum/all/2014-03-21-25683-how-stats-work.md:2222): "each % unit of health from the tree will grant you a
  reward set", with heartwood at thresholds. That points to percent-based yield (task C). [P quote source, I mapping]
- No client table maps a prop or tree to these rows (no RESOURCE/HEALTH column besides Resources/ResourceInfo; ResourceInfo
  is empty). The tree→row choice was live server data. [P search, I conclusion]

## 7. Answer: what the server must send for a tree

For TargetHealthPercent < 100, TargetHealthPercent > 0 and TargetMaxHealth > 0 to hold, the tree entity's container
needs a record of **type 1** with current < max and current·100 ≥ max:

```
record (FUN_142c18870; the same bytes in SetCharacterResource and in the +0x2c8 list):
  u32 <computed>
  u32 resource type = 1
  u32 <computed>
     (or 1 × {u32 0x2C7CD6E6, u32 0x2C7CD6E6, f32 base, f32 modifier} for a per-tree max = base + modifier)
  10 × u32  (first = current value, e.g. 2250 untouched, falling per hit; 0 at felled)
  u32, 4 × u64, bool, bool, bool, u32   (all 0, as CharacterResources.SendFor writes them)
```

It can be delivered in one of two ways [P mechanics]:
- **(a)** ResourcesBase 0xC4 sub 2 `{u32 per-client seq, u8 2, u64 tree guid, record}` (CharacterResources.SendFor
  already writes exactly this, with resourceType 1). It must reach the client **after** that client's LightweightToFullProp
  for the tree, or the full packet wipes it.
- **(b)** The record as entry #1 of LightweightToFullProp's +0x2c8 list (`u32 1` + record instead of `u32 0`). It is
  installed together with the wipe.

The same goes for NPCs (LightweightToFullNpc +0x2c8, or SendLife again after SendFull).

UpdateHitpoints 0x03 cannot do this: it never writes the container. [P]

Bandwidth notes [I, our design]:
- The tree bar needs IsHarvestTarget, so only the harvester has to know a tree's health. A SetCharacterResource to the
  harvester on each hit (after the full packet; the tree is near, so it is full already) is enough.
- Untouched trees need nothing sent: they show no bar until harvested.
- Plants (shown within 7 m when injured) would need the record sent to viewers within that range once injured.
- For an injured tree or creature that a newcomer gets full data for, put the record in the full packet's list (b) instead
  of an extra packet.

## 8. Open leads (not written off)

- **IsHarvestTarget** (Requirements 19, TargetOfPlayer type 4030, param 9) is `RequirementAutoInitializeParam<
  ClientRequirementIsTargetOfPlayer,1>` (oaat key 0xb9d80e7f, FUN_14329bfb0), evaluate **FUN_143710fe0**. It calls
  FUN_1431e6210(PlayClient +0x32b20 ClientTargetManager, target guid), which returns the slot (+0x20) of that guid in the
  manager's list, and compares it with 9. [P]
  - Who puts a tree into slot 9 is not traced: TargetPacket S→C AddTarget 0xB5/5 (not sent today, coverage-695.md:1746),
    SetTarget 0xB5/6, or the client itself on a harvest swing?
  - Next: read FUN_1431e3d90 / FUN_1431e3fd0 and the list writers of +0x32b20 +0x18.
- **Indicator binding:**
  - FlashProxiedCharacterIndicator vtable slot 82 = FUN_143449560 evaluates HUDIndicatorSettings elements through
    FUN_14344a890(…, element type 7/8/2/9 …).
  - It ends with a loop over entries whose +0x28 == 3 (requirement at +0x10), reading FUN_143b8c310 / FUN_143b8c370 (type 1).
  - The decompile drops where the percent goes (likely a vtable call). TYPE_ID 3 = health bar element (rows 3^18^2, 3^22^2
    = TargetInjured) is [I].
  - Next: task B, or the assembly of FUN_143449560 around the FUN_143b8c370 call.
- **FUN_14354f770 type-1 side effects** (FUN_142d11210 health event; owner vtable +0x170) on a ProxiedProp owner: has
  ProxiedProp overridden slot +0x170? It is inherited unless overridden [I]. A live check with the real client proves
  it (verify-client-results rule).
- **Sequence check** FUN_14361cd30: the first lookup keys a pending table by its param_4. Confirm in the part_0067 case
  0x9E caller that this is the guid and not the sequence, so per-client counters for many guids never stall.
