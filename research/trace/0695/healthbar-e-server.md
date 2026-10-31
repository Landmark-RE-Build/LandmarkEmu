# Health bars on trees / plants — task E: server-side audit (read-only)

Bug report (relayed): trees had a green health bar while being chopped, larger trees took more hits, better axes did
more per hit; NPC bars also missing. This note audits the server (LandmarkEmu, branch client-0.695) and names the
change points. Nothing was edited. `[P]` = proven (file:line or FUN_ + what it does), `[I]` = inferred.

Paths below are relative to `LandmarkEmu/LandmarkEmulator.WorldServer/` unless they start with another folder.

## 0. Existing research (checked first)

- MODLOG.md:1165 (2026-10-11): the NPC bar works when the creature has a Life resource: requirement 21's
  TargetMaxHealth reads resource type 1 from the entity's resource container +0x4268. It was verified on a Kobold. [P]
- trace/0695/gear-stats-695.md:15: the target-frame / nameplate bar = ClientRequirementTargetMaxHealth FUN_143711bb0 →
  FUN_143b8c310(entity+17000 = +0x4268, type 1). [P]
- trace/0695/audit-wire-a.md:94: the SetCharacterResource record is FUN_142c18870. audit-wire-a.md:95: the
  StartHarvest / StopHarvest packets (PlayerUpdate 0x37 / 0x38) are never sent. [P]
- trace/0695/full-npc-695.md §4: LightweightToFull fields. +0x90 → disposition +0x147c. The +0x2c8 list →
  FUN_143b8aef0(+0x4268), i.e. the entity's resources. [P]
- design/forum/all/2014-03-21-25683-how-stats-work.md:443-449: a player says large trees have about 2,250 hit points.
  A Gold Axe with speed 93.15 and damage 81.87 fells one in 28 swings of 0.54 s. [P source, player claim]
- design/game-rules.md:16: harvest damage is taken off the resource's hit points on each swing; the resource dies at 0. [P]
- No earlier note covers tree health bars. MODLOG.md:469 decoded PlayerUpdate 0x2D Destroyed (the dev command
  `treefall`); its visual effect is still untested.

## 1. Current behaviour per axe hit (who is told what)

Path: AbilityCastAttempt → `Game/Abilities.cs` HandleCast 123-358.

1. Pacing: `IsHarvestAbility && TooSoon` (318-322, 1015-1024). The interval is 0.85 × 50 / speed. Speed is stat 13
   after buffs, from the `toolStats` table (964-988; axes at 983-987). A cast that comes too soon gets only Answer
   (CastSuccess, then AbilityEnd 0.75 s later) and does nothing else. [P]
2. Swing animation for the others: `Emotes.ShowSwing(session, 254, swingAt)` (332-333). This sends PlayAnim 254, and
   255 after 350 ms, to the other players. [P]
3. `Gathering.Chop(session, TargetGuid(attempt, session), RingPoint ?? target, axe item id)` (342-343).
   - TargetGuid (865-873) takes the guid in the cast tail +36 when it is above 0xFFFF. Otherwise it uses
     `session.CurrentTarget`. [P]
   - Extractor swings (1036 / 1037) also call Chop / Reap (346-355). [P]
4. `Answer(session, attempt, swung: true)` (357, 404-449): CastSuccess with animation 254, a second CastSuccess with 255
   350 ms later, and AbilityEnd. These go to the swinger only. [P]

Inside `Game/Gathering.cs` Chop (315-347):

- `Target()` (436-456):
  - Finds the node by guid (`guid >> 62 == 1`, GuidBase 0x4000…). Without a guid it takes the nearest standing tree
    within 3 m of the aim point (AimTolerance).
  - The player must be within 8 m horizontally (Reach).
  - With no tree found → `WrongTool`. When the guid is a plant, the swinger gets HarvestFail reason 3, at most once
    every 5 s (373-390, 403-427). [P]
- `Hit(tree, …)` (473-491): `Hits++`. The tree is felled when `Hits >= MaxHits`. The counter resets to 0 and the spot
  is freed:
  - min/max respawn model: `Felled`, Gathering.Respawn.cs 358-372;
  - in-place respawn model: `RegrowsAt` and the `gathered` list. [P]
- `Durability.ToolUsed` on every hit (324). [P]
- Wood: random 2..4 × 1.5 for the Goliath axe (3261) × ToolYieldBonus (1.25 for the Harvestron 3703) × (1 + stat 17 / 100)
  × Durability.ToolStrength (0.5 for a broken tool). At least 1. (327-329) [P]
  - The wood goes to the harvester and to party members within 200 m (`GiveHarvest`, 117-126).
  - Then `Achievements.Harvested`, a loot chunk to the harvester (`LootPinatas.SpawnChunk`), and a 30 % chance of Tree
    Fiber. [P]
- Not felled: a debug log line only (335-339). **Nobody is told the tree's state.** No resource, no UpdateHitpoints,
  no hit count. [P]
- Felled: 15 % chance of Sap, `RollDrops` (Lumens / essence / recipe / emitter / beacon) and `AfterUse` (the speed
  burst). Then `Despawn` → `PlayerUpdateRemovePlayer {guid}` to every session that has the node's 64 m region in `sent`
  (496-503). [P]
- MaxHits rules:
  - old tree lists (Generate 682-688): `IsLargeTree(model)` (902: models 344-347, 226, 1370) gives 8 hits, every other
    tree 5;
  - placement-set trees (AddSetNodes 735-741): `Footprint >= LargeTreeFootprint` (4.5 m, line 805) gives 8, else 5;
  - plants: 1; decoration: int.MaxValue. [P]
- The tree's size, scale and the axe have no effect on how many hits it takes. Only the Goliath axe, the Harvestron,
  stat 17 and durability change the wood per hit. [P]

Reap (350-365): a single hit always gathers the plant. The reaper gets 1 plant item, `RollDrops`, a loot chunk, then
`Despawn`. [P] There are no plant nodes at all, because `PlantsHarvestable = false` is a const at 212 (P4128,
21 Mar 2016). [P]

## 2. Every place a tree's state is sent or removed

| Where | What is sent / changed | To whom |
|---|---|---|
| Gathering.cs Stream 568-615 (from Update 263-292, once a second per session) | AddLightweightProp `node.Spawn` for the standing nodes of each newly entered region (within 160 m). RemovePlayer for the standing nodes of regions dropped (beyond 224 m, another landscape, or more than 60 m underground). | that session [P] |
| Despawn 496-503 (from Chop, Reap and DevFell in Respawn.cs 559-560) | RemovePlayer | every session with the region in `sent` [P] |
| RegrowDue 620-640 (in-place model) | AddLightweightProp `node.Spawn` again: same guid, same TransientId | sessions with the region [P] |
| Respawn.cs RespawnUpdate 378-430 → SpawnElsewhere → Stand 463-480 (min/max model) | Stand sets `Hits = 0` and `RegrowsAt = default`; then AddLightweightProp | sessions with the region (422-429) [P] |
| SendFull 878-891 (Network/Handlers/PlayerHandler.cs:85, FullCharacterDataRequest) | PlayerUpdateLightweightToFullProp {TransientId}, with every list empty | the requester [P] |
| Forget 300-309 (PlayerManager.cs:569 Reintroduce after a re-zone, :650 leave, :709 never entered) | clears `sent`, so the next Stream sends everything again | — [P] |
| Install 312-355 / SavedState 194-199 | sets RegrowsAt on regions generated before the census; no packets | — [P] |
| Hit 473-491, Stand 476, DevFell 553 | `Node.Hits`: in memory only, never saved, never sent | — [P] |

- The partial-hit state lives only in `Node.Hits`. The `regions` cache never forgets a node, so a half-chopped tree
  stays half-chopped until a restart. [P]
- The client deletes entities on a re-zone. After the re-stream, the tree is a fresh entity on the client, while the
  server still holds its damage. [P, Reintroduce + project rule "re-zone deletes entities"]

## 3. Client facts the fix rests on (newly proven here, 0.695 decompile)

- **Bar condition for trees.** Requirement 13 (the Prop indicator) holds for a tree when TargetInjured && TargetAlive
  && IsHarvestTarget. [P, data]
  - TargetInjured / TargetAlive = ClientRequirementHealthPercent (vtable 144740468, slot 9 = **FUN_143710070**). It
    calls FUN_143b8c370(entity+0x4268, type 1), which finds the resource record of type 1 (+0xe8) and returns
    current (+0x70) × 100 / max (FUN_143217a00, which honours the MAX_VALUE variable). So **a Life resource of type 1
    for the tree's guid is all the health part needs**. UpdateHitpoints (0x03) is not read. [P]
  - IsHarvestTarget = ClientRequirementIsTargetOfPlayer **FUN_143710fe0**: `FUN_1431e6210(ClientTargetManager at
    DAT_1454311c8+0x32b20, entity guid) == 9`. [P]
  - Who puts a guid in slot 9:
    - BaseClient::SetCurrentTarget **FUN_142d4e890** puts the entity in slot 9 when disposition +0x147c ≠ 0 or entity
      +0x1460 bit 2 is set (any non-hostile entity). Otherwise it uses slot 3 (FUN_143c74930 → +0x10 with 3). [P]
    - With the "send" flag, FUN_1431e7220 sends TargetPacketRequestSetTarget (0xB5, sub 2) {guid, slot}. Our
      InteractionHandler.cs:331-340 echoes SetTarget {guid, slot, true}, and the client adopts the target only on that
      reply (comment at 318-321). [P server side]
    - The harvesting action-state family calls SetCurrentTarget(…, 1): FUN_143459e30 → FUN_142d4e890. [P call]
    - That this is the axe path when it targets a tree is [I]. Trees get disposition 2 in our LightweightToFullProp
      (PlayerUpdateLightweightToFullProp.cs:30 → +0x90). Lightweight props also read 2 (MODLOG.md:1172). So trees
      land in slot 9. [I, strong]
- **Initial value is MAX − 1.** Resources.txt has tree and plant Life rows (type 1). Names are from
  re/out/text_695.tsv. [P, data]

  | Row | Name | Initial / max |
  |---|---|---|
  | 18 | Tree - Very Small | 350 / 351 |
  | 16 | Tree - Small | 750 / 751 |
  | 14 | Tree - Medium | 1500 / 1501 |
  | 15 | Tree - Large | 2250 / 2251 |
  | 21 | Tree - Very Large | 3000 / 3001 |
  | 17 | Plant | 10 / 11 |
  | 20 | Plant | 20 / 21 |

  - All of these rows have REGEN 0 and PACKET_BROADCAST_RANGE 30. [P, data]
  - Because initial = max − 1, a full tree reads 99 %. TargetInjured therefore holds from the start, and the bar
    appears as soon as the tree is the harvest target. [P arithmetic; I that this was the purpose]
  - The Large row matches the forum's "about 2,250 hit points". [P]
- **Tree and plant profiles.** ProfileDefinitions has 200-214 "Tree: Very Small/Small/Medium/Large/Very Large: Plain/
  Burled/Striped Wood", 90/91/296 "Plant - One/Two/Three Hit" and 39 "Prop". The tree profile rows have no model,
  camera or abilities. [P, data]
  - The old TargetIsTree* requirements (8 / 19) name profiles 56-58, 87 and 100-105. In 0.695, 57 and 58 are "COMBAT"
    profiles, so those requirements look stale. Requirement 13 does not use them. [P, data]
- **The spawn's profile slot.** ClientRequirementIsProfile **FUN_143710dd0** compares entity +0x1474.
  - +0x1474 is set by FUN_142ec55e0, called from SetupLightweightNpc **FUN_142d54040** with the packet's +0x1a0. [P]
  - Base reader FUN_142c26790 (shared by the NPC and prop spawns) reads: … u32 +0x190 / +0x194 / +0x198, u8 +0x19c,
    **u32 +0x1a0**, bool +0x1a4 … (re/tools/pkt.py). [P]
  - So the unnamed `writer.Write(0u)` right after ControlMode, at Network/Message/Model/PlayerUpdateAddLightweightProp.cs:126,
    **is the profile id**. Plants need it for the DistToTarget ≤ 7 branch; trees do not. [P]
- **The full-data resource list.** LightweightToFull +0x2c8 is read by FUN_142c3cdd0: an i32 count, then n records of
  0xd8 bytes, each read by **FUN_142c18870**, the same reader as SetCharacterResource. That is the
  `writer.Write(0u); // list (0xd8-byte entries)` at PlayerUpdateLightweightToFullProp.cs:58. [P]
  - A tree's Life can therefore ride inside the full-prop answer, with no extra packet. [P format; I that live did so]
- **ProxyNameHidden** FUN_1437119c0 needs entity +0x13c8 ≠ 0 and tests +0x1454 bit 6. Trees spawn with no name or
  name id, so requirement 13's first branch (TargetIsProp && ShowProxyName) stays off. [I]
- **StartHarvest is not the tree bar.** PlayerUpdate 0x37 StartHarvest (dispatcher case 0x36, part_0065:19355) acts
  only when the guid is the local player's. It calls FUN_1431d7ea0 → FUN_1431d7f00(…, "CastingBar:SetupBar"): a
  casting / progress bar for the player. [P]

## 4. Change points for trees (cleanest)

**A. Node state.** Gathering.cs Node, 149-168:
- Add `uint LifeRow` (the Resources.txt id), `uint MaxHealth` and `uint Health`.
- Fill them in Create (849-871) and in AddSetNodes (745-763), next to MaxHits (keep MaxHits for the old mode).
- Size class:
  - first from the model file name (Models.txt col 2): VerySmall/Sapling, Small/Short, Medium, Large/Tall,
    VeryLarge/Huge/Giant;
  - then from the footprint (ModelFootprints / Models.txt PATH_BLOCK_RADIUS), e.g. <1 / <2 / <4.5 / <6.5 / ≥6.5 m. [I]
- Size → row: VerySmall 18, Small 16, Medium 14, Large 15, VeryLarge 21. [P names; I mapping]
- Max = the row's MAX_VALUE, read with `GameDataFiles.Rows("Resources.txt")` as Creatures.LifeRow does
  (Creatures.cs 2392-2399).
- Full = MAX − 1 (INITIAL_VALUE).
- Optional: scale by node scale, sending the MAX_VALUE variable through `SendFor(..., maximum: (rowMax, nodeMax))`
  (CharacterResources.cs 60-97, +16 bytes). Off by default [I].
- The old-list examples:

  | Size | Models (old lists) |
  |---|---|
  | Medium | JaggedPine_Medium 338 / 342 / 343, Birch_Medium 351 / 352, Oaks_Medium 1467-1469 |
  | Large | DiamondPine / GnarledPine_Large 344-347, Lavastorm Tall 226 / 1370 |
  | Small | GnarledPine_Small 348 / 349, Lavastorm Short 225 / 1371 |

  The JaggedPine HugeFace models 336 / 337 have a footprint of 11.6 m → VeryLarge.
- Optional: the spawn's profile, 200 + 3 × size + wood (wood: 0 Plain, 1 Burled, 2 Striped; matches SetWood 811-816),
  written into the new `ProfileId` field at AddLightweightProp.cs:126. Not needed for the bar. Low risk: the rows
  are empty. [I]

**B. Damage instead of a count.** Hit 473-491 becomes `Hit(node, damage, regrow)`:
- health mode: `Health -= damage`, felled at ≤ 0, then reset `Health = MaxHealth - 1`;
- hits mode: keep today's `Hits` logic.
- Also reset health in Stand (Respawn.cs:476, beside `Hits = 0`) and DevFell (553). RegrowDue needs nothing (the
  reset happens at the fell).

**C. Axe strength.**
- Add a damage column to Abilities.cs `toolStats` (960-988):
  - Adventurer's 80 (the live display value already noted at 994-999);
  - the crafted tiers higher [I]; Goliath / Hotfoot / Charmed per their perks [I];
  - the Whirlwind Axe has no row (`toolStats` has the Whirlwind *pick* 3256 only): check its item id (open question).
- Damage per hit = `Buffs.Apply(session, 12, toolDamage)` (stat 12 Harvesting Damage; Potion of Furious Harvesting
  +25 %, Consumables.cs:147) × `Durability.ToolStrength(session)`.
- Make `ToolDisplayStats` return the per-tool damage instead of the flat 80, so tooltips match.
- Optionally send stat 12 for a held axe in CalculatedStats (936 sends it only with a pick).
- Yield per hit should follow the damage (the report: "axes doing more progress / yielding more per hit"; P0474:
  rewards at a consistent rate regardless of size):
  - wood = random 2..4 × damage / 80 × (Goliath, Harvestron, stat 17, strength), as today;
  - total wood ∝ tree health; wood per second for a given axe is unchanged. [I]
- Example, Adventurer's Axe at 80, one swing a second: Very Small 5 hits (today's 5), Small 10, Medium 19, Large 29,
  Very Large 38. [arithmetic]
- RollDrops stays on the felling hit. Live rolled Discovery on every swing (design/game-rules.md:21); that is a
  separate decision.

**D. Telling the client (the bar).** A new `SendTreeLife(viewer, node)` =
`CharacterResources.SendFor(viewer, node.Guid, node.LifeRow, LifeType 1, node.Health)`. No maximum variable is needed
when the row's own max is used. This is about 102 bytes per record (the sequence and guid included).

Recipients (bandwidth rule):
- (i) The hitter, after every hit that does not fell the tree. On a fell, RemovePlayer follows as today.
- (ii) Every session whose harvest target (slot 9) is this node: two players on one tree.
  - Track it in InteractionHandler.HandleTarget (323-353): sub 2 {guid, slot 9} with `Gathering.IsNode(guid)` →
    `Gathering.Targeted(session, guid)`; sub 3 {slot 9} / sub 4 → `Untargeted`.
  - Send the node's current Life **before** the SetTarget confirm, so the bar appears exactly when the client adopts
    the target.
  - Clear the tracking in Gathering.Forget and in Despawn.
- Note: `WorldSession.CurrentTarget` keeps one value whatever the slot (331-347). Slot 3 and slot 9 overwrite each
  other. Keep the harvest target separately. [P]
- Viewers who are not harvesting need nothing. P0123 says only harvesters see the bars, and requirement 13 hides them
  anyway. [P]
- Alternative, closer to live [I]: put the Life record in LightweightToFullProp's +0x2c8 list (SendFull 878-891;
  PlayerUpdateLightweightToFullProp.cs:58).
  - The client promotes the nearest 50 entities (full-npc-695.md §4), so this costs about 95 bytes × 50 once.
  - Suggestion: do it only for damaged trees. A re-streamed damaged tree then shows its damage again.
  - Factor a `CharacterResources.WriteRecord(...)` out of SendFor (73-95) for both paths.

**E. Plain removal is unchanged.** On a fell, RemovePlayer still clears the entity and its resources. Optional:
PlayerUpdate 0x2D Destroyed (MODLOG.md:469) for the falling look; separate work.

## 5. Plants (sickle; profiles 90 / 91 / 296; DistToTarget ≤ 7)

- Today plants are not generated at all (`PlantsHarvestable` const false, Gathering.cs:211-212, P4128). So everything
  in this section is inert unless that const becomes a setting. Its default stays off (live at 0.695 had no plant
  harvesting). [P]
- Requirement 13's plant branch is TargetInjured && TargetAlive && DistToTarget ≤ 7 && (profile 90 | 91 | 296), with
  no harvest target. [P, data]
  - So a plant needs **both** a Life record and the profile id at AddLightweightProp +0x1a0 (AddLightweightProp.cs:126,
    see §3). [P]
  - Its bar then shows to anyone within 7 m once the record is there. Initial = max − 1 means even an untouched plant
    with a record reads injured. [P arithmetic]
- Rows: 17 Plant 10/11 and 20 Plant 20/21. There is no 30/31 row for "Three Hit". [P]
  - Mapping [I]: common → profile 90 / row 17, Large (uncommon) → 91 / row 20, Perfect (rare) → 296 / row 20 with a
    MAX variable of 30.
  - Sickle damage 10 per swing [I] (one / two / three hits).
- Change points:
  - Generate's plant branch (698-707) and AddSetNodes' Plant case (739): size row and profile;
  - Reap (350-365): damage, gather on 0, Life to the reaper after each non-final hit;
  - viewers: send Life only to sessions within about 7-10 m of an injured plant, on the hit; not on spawn;
  - sickle damage in toolStats, which has no sickles today (DefaultTool).

## 6. Setting (live default, togglable)

Proposed `TreeHealth` = `health` (default, live) | `hits` (today's 5 / 8 counts, no bar, or a bar from
(MaxHits − Hits) / MaxHits if wanted). Optional `TreeHealthScale` (default 1.0) for owners who find big trees slow.

Plumbing, following ProceduralRespawn / CreatureWeapons:
- WorldServerConfiguration.cs: a property with a doc comment (e.g. CreatureWeapons at 260-269);
- Game/ServerSettings.Table.cs: an `Edit(...)` row (e.g. 207: `Edit("ProceduralRespawn", SettingApply.Restart, "minmax") with { Choices = … }`);
- docker/world/docker-compose.yml: `TreeHealth: ${TREE_HEALTH:-health}` (next to 144-149);
- .env.example: a commented `TREE_HEALTH=health` (next to 213-216);
- LandmarkEmulator.WorldServer/WorldServer.example.json: the key with the code default;
- docs/gameplay.md:211: the "5 hits (8 for large trees)" line;
- docs/docker.md: the env section (~452);
- wiki/Settings-Reference.md, and tools/wiki_reference.py for the live value;
- docs/protocol.md: ResourcesBase on non-character entities, and AddLightweightProp +0x1a0 = profile.

Enforced by LandmarkEmulator.Tests/Config/ConfigConsistencyTests.cs (EverySetting_IsInTheExampleJson,
WorldCompose_DefaultsAreTheCodeDefaults, WorldCompose_VariablesAreInEnvExample_WithTheSameDefault, …) and
ServerSettingsTests.cs (EveryCodeSetting_HasARule, EveryConfigurationProperty_IsOnThePage_WithARule,
RawRules_HaveTheCodeDefaults, GroupsHelpAndEnvNames_ComeFromEnvExampleAndCompose). [P]

## 7. Tests covering gathering today

- Only `LandmarkEmulator.Tests/Rules/TreeFellingTerrainTests.cs`. It:
  - puts flat ground at 1010 m for its own landscape (TerrainHeights.TestGround);
  - marks the landscape rolled for today's generator (the private `rolled` field);
  - finds the first standing tree through reflection on the private Node class (`Type`, `Standing`, `Spawn`, `Guid`,
    `MaxHits`);
  - chops it **`MaxHits` times** (lines 99-101), then asserts the tree is felled, the terrain manifests are
    unchanged, no TerrainCell or VoxelConstruction packets went out, and the region's list no longer has the guid. [P]
- **It breaks under health mode** (MaxHits swings no longer fell the tree). Change it to chop until `!Standing` with a
  bound, or set `hits` mode in the test.
- Useful patterns: Rules/ObserverSyncRuleTests.cs:228-256, `LifeOf` (filters ResourcesBase bodies longer than 21
  bytes; guid at body offset 5). Also FakeWorld.Session / FakeWorld.Outgoing.
- No test covers Reap, WrongTool, HarvestFail, TooSoon pacing, ToolDisplayStats or the respawn model.
- Suggested new tests:
  - model → size → Resources row and max;
  - a hit sends exactly one type-1 record for the tree guid to the hitter, value = max − 1 − damage, and none to a
    viewer who is not targeting it;
  - damage scales with the axe, buff 12 and a broken tool;
  - a tree is felled at ≤ 0 (RemovePlayer to region holders) and its health resets on Stand / regrow;
  - `hits` mode keeps 5 / 8;
  - TargetBase sub 2 slot 9 on a node guid sends its Life before the SetTarget echo;
  - re-zone (Forget) then re-target sends the damaged value again;
  - the settings consistency tests pass with TREE_HEALTH.

## 8. Open questions (what to try next)

1. Confirm that the axe's target request for a tree uses slot 9. Log the slot in InteractionHandler.HandleTarget
   (no client change needed) and chop once with the test client. If it is 3, IsHarvestTarget fails: check disposition
   +0x147c on lightweight props.
2. Confirm that SetCharacterResource for a prop guid lands in the prop's +0x4268 (FUN_14361b900 queues records by guid).
   One dev `res` send to a tree guid, then target it. The NPC case is already proven (MODLOG.md:1165).
3. Live's model → size → profile / Life row mapping was server data and is not in the client tables (no
   PropDefs / Models column). The name + footprint rule is ours. Check the wiki's tree pages / resource guide (P1103)
   for per-tree sizes.
4. Live axe damage per tool in 2016: only the Adventurer's 80 (live display) and a 2014 Gold Axe 81.87 are known. Look
   in design/wiki "Category:Axes" and forum tool posts for damage / speed per 2016 axe. Also the Whirlwind Axe item id
   (it has no toolStats row).
5. The Prop indicator (HudIndicators id 12, code 18, params 0.75 / 0.75): does it draw the bar from Life alone, or does
   it also need a name? Client-side (tasks A-D).
6. NPC part of the report ("bars only on NPCs I aggro'd then ran away from"):
   - Creatures.SendLife (2380-2387) sends the scaled Life at spawn (SendSpawn 2275) and on every hit
     (SendHitpoints 2446-2454 → ViewersOf).
   - Requirement 21 has no Injured term, so every combatant should show a bar.
   - Compare with HUDIndicatorSettings types 1 / 2 / 3 / 13 and requirement 39 / 2 for the live rule. Client-side
     tasks.
