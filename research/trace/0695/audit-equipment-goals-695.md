# Audit: TASKS 3.15 Equipment changes and 3.16 Goals against the 0.695 data and the server (2026-10-15)

Read-only research. Sources: trace/0695/patchnotes.tsv (post-0.589 notes, June-Dec 2016), gameData695 vs gameData
(`python re/tools/tablediff.py <Table>`), gameData695/items.json (live item dump, 0.695 era), the live achievement
capture LandmarkEmu/LandmarkEmulator.WorldServer/Resources/PacketDumps/71-AchievementBase (decoded with
re/tools/decode_achievements.py; re/out/achievements.tsv), re/out/text_589.tsv / text_695.tsv,
<0.695 test copy>\Resources\ActorCompositeEffectDefinitions.xml (0.695) vs trace/composite_effects.tsv (0.589),
re/as3/Journal, design/wiki, and the server code (Gear.cs, WeaponCombat.cs, Statuses.cs, Achievements.cs,
Creatures.cs, Templates.cs, Props.cs, Claims.cs, MiscHandler.cs). Existing research used: launch-rules-audit.md,
items-695.md, tables-diff.md, wiki-content-audit.md, OPEN-LEADS.md (L26, L40, L42), TASKS 4.20, 5.5, 5.6, 5.11, 6.5.

Classes: **IN DATA** = the 0.695 tables / live dumps the server reads already carry it; **CODE** = server code must
change; **CLIENT** = client-side only; **UNKNOWN** = a number or source is missing (say what).

## Key facts found

1. **The achievement capture is final-era.** Its start / completion timestamps are 18-19 Feb 2017 (goal 471 started
   2017-02-18 20:12:39 UTC, 477 completed 2017-02-19 21:37:42). So every goal definition, required count and reward the
   server sends is the post-June-2016 version. This settles the "date unknown" caveat in launch-rules-audit.md for
   these goals.
2. **The armour passive texts did not change between 0.589 and 0.695.** All 28 chest-armour descriptions
   (575654-575681, "While wielding a <Affinity> weapon ...") are identical in text_589 and text_695, and the live item
   dump (items.json) links every armour to that same description id (e.g. 1541 → 575668, 1942 → 575669, 2255 → 575676).
   The 7 June "new passives" are therefore the texts both clients already show. The wiki pages still describe the
   *old* passives, for example Frostlord's Guard "Surge deals double damage" (now Raiment of Frozen Sorrow's text) and
   Electrified Assault Exo-Suit "periodically Electrocute an enemy ...". Do not implement from those wiki pages.
3. **Passive numbers are server-side only.** The client has no Abilities table (gameData695/AbilityEx.txt holds one
   test row), and the passive ability ids in the dump (e.g. 1131, 1133, 1143) appear in no client table. Chances,
   radii and damage shares were live server data and are not available anywhere. The server's values (Gear.cs:72
   `Chance = 0.3f, Nearby = 5f`, plus per-effect shares) are ours.
4. **0.695 adds no combat effects.** ActorCompositeEffectDefinitions.xml has 1,679 effects in 0.695 against 1,611
   in 0.589. All 68 new ones are props: light bulbs, counting nodes, fireworks, noticeboard, respawn markers. The
   7 June "ice explosion and hit flashes" for the Coat of Crashing Waves used effects that already exist in 0.589.
5. **The server reads the 0.695 recipe tables** (gameData695, TASKS 4.20), so table-level recipe changes are live.

## 3.15 Equipment changes

| Note | Date | Change | Class | Evidence / server state |
|---|---|---|---|---|
| P5736-P5746 | 7 Jun 2016 | Ten armours got new passives: Frost Lord's Guard, Electrified Assault Exo-Suit, Blight-Warden's Suffering, Crusader's Calling, Night Herald's Promise, Dragonfire Ritual Robes, Tectonic Destroyer's Platemail, Desperado's Deadly Poncho, Lightbringer's Oath, Silent Shadow's Cloak | **IN DATA** (texts) + server effects already follow the new texts; one partial divergence (Night Herald's) | Per-item check below |
| P5747 | 7 Jun 2016 | Frenzied Slayer's Chainmail: lower trigger chance | **UNKNOWN** (number) | Gear.cs:93 `Roll(0.25f, s.Strength)` when Frenzied and target life < 0.2. No source gives the old or new chance. Ours (25 %) is already well below the other execute passives, which always fire: Poison Drenched Leathers (Gear.cs:112) and Silent Shadow's Cloak (Gear.cs:129). Leave it, and record it as a lead. |
| P5748 | 7 Jun 2016 | Reinforced Coat of Crashing Waves: less damage and range, damage reduction removed, ice explosion + hit flashes added | Partly in place; **CODE** for the explosion; **UNKNOWN** numbers | Gear.cs:161-162: 30 % chance, `Burst(p, p.Position, 5 m, 0.4 share, Freeze, 10114)`. Damage reduction: none (f unchanged), so that part is already right. Hit flashes: 10114 PFX_Combat_Misc_Impact_Ice_NonDirectional is played on each target, so that part is in place. Ice explosion: nothing is played at the wearer. The share (0.4) is already lower than the other bursts (0.5). The radius (5 m) equals the shared `Nearby`, and the old and new radii are unknown. |
| P5749 | 7 Jun 2016 | Dash Soles no longer drain energy without activating | **CLIENT** / nothing to do on the server | The server never debits Energy for movement: Shift casts ability 326, and MiscHandler.cs:451-462 answers its caster info with no cost, cast or recovery. CharacterResources.cs:8-14 says the client runs the Energy pool itself. Boots carry the movement ability in the item record's u59 (Dash Soles 52, items-695.md / L26). Verify in a 0.695 client test: double-tap Dash Soles with low Energy. |
| P5735 | 7 Jun 2016 | Some missing weapon recipes now at the Lumen Station | **IN DATA** (in both builds); **UNKNOWN** for 4 | Station 55 (Lumen Station) sells the recipe items of 15 weapons plus the 4 NovaTech tools at component 5831 in both 0.589 and 0.695, including the 25 May weapons Frenzied Slayer's Battle-Axe, Electrified Assault Axe, Blight-Warden's Pain, Frost Lord's Fury, Night Herald's Mercy, Staff of the Lightning Caller and Crusader's Courage. Four recipe items have recipes (2450, 2453, 2454, 2455; VISIBILITY_REQUIREMENT 1303 / 1306-1308) but no CraftingStationRecipeMappings row in either build: 3996 Recipe: Katana of Crashing Waves, 3999 Desperado's Deadly Arsenal, 4000 Lightbringer's Justice, 4001 Silent Shadow's Condemnation. No goal in the Feb 2017 capture rewards them and the wiki has no pages for them. Source unknown (probably the weapon-kill goal tiers the capture lacks, L42). |
| P5930 | 30 Jun 2016 | Lumen Station cost of some combat equipment recipes adjusted | **IN DATA** | RecipeComponents (tablediff), currency 1579 Lumens: **5829 520 → 260** (14 accessory recipes 4009-4022, e.g. Recipe: Volcanic Bracer, Band of Flame, Spite Etched Bracer); **5830 780 → 520** (14 chest-armour recipes: 3988 Flame Bound Cuirass, 3989, 3991-3994, 4002 Frenzied Slayer's Chainmail, 4004 Electrified Assault Exo-Suit, 4005, 4024 Frost Lord's Guard, 4025, 4027 Blight-Warden's Suffering, 4028 Crusader's Calling, 4029 Night Herald's Promise); **5831 1040 → 780** (23 recipes: the 19 weapon recipes 3981-3987, 3995-4001, 4118-4122 and the NovaTech tools 4347-4350). The server reads gameData695, so the prices apply. |
| P6439 (related) | 29 Aug 2016 | Colonist's Patrol Armor (Orange) / (Purple) no longer duplicated at the Lumen Station | **IN DATA** | CraftingStationRecipeMappings rows 5752 / 5753 (station 55 → recipes 5111 / 5112, items 4153 / 4154) removed in 0.695. |

### The ten new passives: server effect against the 0.695 description

| Armour (item) | 0.695 description (id) | Server (Gear.cs) | Verdict |
|---|---|---|---|
| Frost Lord's Guard (2255) | Opponents that attack you take revenge damage; if Flow is at max stacks they are also Frozen (575676) | :163-168 Revenge + Freeze at max Flow | matches |
| Electrified Assault Exo-Suit (2249) | Every stack of Energize reduces incoming damage and deals revenge damage (575670) | :178-183 −5 % per stack, revenge 6 % per stack | matches |
| Blight-Warden's Suffering (2258) | Hitting a Rotting opponent can infect all nearby with Rot, plus bonus damage (575679) | :114-115 30 % → Burst Rot 0.5 | matches |
| Crusader's Calling (2259) | When Illuminated, all opponents in front can be struck by radiant beams when you hit (575680) | :124-126 30 % → cone 15 m / 45°, half damage | matches |
| Night Herald's Promise (2260) | **Your first Power Attack against a Condemned opponent** triggers a shadow burst near the target (575681) | :131-132 every Power Attack whose first hit lands on a Condemned target (`s.Hits.Count == 1`) | **diverges**: fires on every Power Attack, not only the first one against that Condemned opponent. See fix F2. |
| Dragonfire Ritual Robes (2254) | While Frenzy is active, hitting a Burning opponent makes all your attacks Erupt for a short time (575675) | :95-96 Frenzy + target Burn → Status.Eruption | matches |
| Tectonic Destroyer's Platemail (2257) | Hitting a Crushed opponent can create a shockwave that Crushes all nearby (575678) | :107-109 30 % → Burst Crush 0.5 | matches |
| Desperado's Deadly Poncho (2251) | Opponents that attack you take revenge damage; if Infect is at max stacks all nearby also suffer Rot (575672) | :192-197 Revenge + Rot burst at max Infect | matches |
| Lightbringer's Oath (2252) | Your first Power Attack while Exalted triggers a radiant burst near your target (575673) | :122-123 FirstPowerWhileExalted → Burst 0.6 | matches |
| Silent Shadow's Cloak (2253) | You can instantly kill Tormented opponents that are low on life (575674) | :129-130 Torment + life < 0.2 → Execute (not elites) | matches |
| *Frenzied Slayer's Chainmail (1541)* | When Frenzied your attacks can instantly kill opponents low on life (575668) | :93 25 % chance | matches; chance unknown (P5747) |
| *Reinforced Coat of Crashing Waves (1942)* | When an opponent hits you all nearby opponents can suffer damage and be Frozen (575669) | :161-162 | matches; explosion missing (F1) |

No server-coded passive contradicts its 0.695 description text, apart from Night Herald's "first Power Attack"
condition. Other findings:
- Statuses.cs:47: the doc comment on `Overloaded` names Night Herald's Promise, but the effect belongs to Vestment of
  the Lightning Caller (575677; Gear.cs:144-145 applies it). Only the comment is wrong.
- Outside the ten, the server has no effect for Wanderer's Blazing Guise (575654), Raiment of Frozen Sorrow (575655),
  Plate of Unrelenting Vengeance's Siphon part (575660), Unflinching Embodiment of Stone's "prevents all damage from
  the next attack" (575671; :188-189 only does revenge) or Hauberk of Piercing Judgment (575667). These are not
  7 June changes and belong to TASKS 6.9.

## 3.16 Goals

| Note | Date | Change | Class | Evidence / server state |
|---|---|---|---|---|
| P5750 | 7 Jun 2016 | Tutorial goals final pass; modular Castle designs taken from all players at the wipe and unlocked through the tutorial | Goal definitions **IN DATA**; design grants **CODE** + **UNKNOWN** | The Feb 2017 capture's Getting Started chain is 471 → 480 → 478 → 477 → 685 (Achievements.cs:42 follows it). Getting Crafty (576237), Building Block Basics (576265) and Modular Thinking (576271) exist as client texts but are not in the capture (see U4). Rewards in the capture: 471 *and* 477 both carry reward bundle 1051, which is 30 Lumens plus 13 castle design items: 3706 Wall - Long, 3707 Wall - Short, 4361 Hallway with Side Doors, 4080 Tower - Corner, 4079 Tower, 4078 Room - Corner, 4077 Room - Four Way, 4076 Room, 4085 Long Room, 4075 Hallway - T-Junction, 4072 Hallway - Short, 4360 Hallway - Medium, 4074 Hallway - Corner. Their grantIds are template ids in client category 1 "Starter Castle" (ExportedTemplateCategoryMap). Of the 28 ids in that category, only 6 have no item: 1120, 1121, 1124, 1128, 1143, 1144. By elimination these are Castle Courtyard, Castle Hallway - Long, Castle Tower - Inline, Castle Wall - Corner, Castle Wall - Medium and Castle Wall Entry, which include the 4 designs that 471's objectives ask for. The 9 modular designs are items 4098-4106 (grants 1133-1141), and no captured goal rewards them. **Server**: Templates.cs:119-129 / SendList :207-212 gives *all 27* Starter Castle templates to every character from the start, and Achievements.Complete (:893-894) also gives the 13 reward items, twice (471 and 477). |
| P5751 | 7 Jun 2016 | Journal search box | **CLIENT** | re/as3/Journal AchievementViewClass.as:45/96/460-469: the SearchField filters locally through Lua `ObjectivesController.ApplyFilterString`, with no server message. It is already in the 0.589 Journal SWF, and the Journal is not among the changed 0.695 SWFs (re/as3_695). |
| P5752 | 7 Jun 2016 | Starting Location prop sometimes had to be placed twice for "Props to You" | **Mostly OK**; one gap to verify | Achievements.cs:649-655 maps prop 2655 to objective 1313, and PropPlaced (:687-698) counts it on the first placement from both placement paths (Props.cs:168, :815, `!wasPlaced`). Gap: Claims.EnsureStartingLocation (Claims.cs:1153-1172) auto-places a Starting Location on every site through Props.PlaceAt → PlaceNear (no PropPlaced). A tutorial player who *moves* that one (Props.cs:772-815, `wasPlaced = true`) gets no count, and has to pick it up and place it again, which is the live bug. See fix F4. |
| P5753 | 7 Jun 2016 | "You Can Kill Those Things?" counts a Fiery Fungus dying anywhere near you, and needs more kills | Count **IN DATA**; trigger **CODE** | Capture: goal 596, objective 1058 "Dispatch explode-y mushrooms", required **100**, reward 15 Lumens. This is the final-era value (wiki also "?/100"); the pre-June count is not known and not needed. **Server**: the only call is Achievements.Killed (:774), reached from Creatures.Kill (:829). A Fiery Fungus never goes through Kill: Hit (:762-776) lights the fuse, and ThinkFungus (:923-956) explodes it without calling any goal counter. TASKS 6.5 notes this is on purpose for P4801, "Fiery Fungi should no longer update any kill goals". So objective 1058 can never advance in normal play (only DevSlayNearest, :1336). |
| P5835 | 16 Jun 2016 | Objective count sometimes showed a lower number than required | **IN DATA** / client | The server sends the capture's `Required` (Achievements.cs Progress, `Math.Max(o.Required, 1)`). |
| P5838 | 16 Jun 2016 | Lumen count not updating at once when gaining Lumens from goals | OK | Complete → session.GiveItem(1579) → SendItem right away (WorldSession.cs:221-225). |
| P5760 | 7 Jun 2016 | Designs from goals show an icon in the Build Mode UI when granted | **CLIENT** | UI only. |
| P6436 | 29 Aug 2016 | Goal "Recipe: Floor Switch" removed (recipe known by everyone) | **IN DATA** | The Feb 2017 capture has 547 "Recipe Discoveries" goals and none is "Recipe: Floor Switch". Text 573587 "Recipe: Floor Switch" is only the recipe item 3435 name. Floor Switch has been default-learned since P4577 (13 Apr 2016). Nothing to remove on the server. |

## Fix list

| # | File / function | Change (old → new) | Source |
|---|---|---|---|
| F1 | LandmarkEmulator.WorldServer/Game/Combat/Gear.cs `OnHurt`, Water case (:161-162) | When the Crashing Waves passive fires, also play an ice explosion at the wearer for all viewers (`HitEffects.PlayOn(p.PlayerGuid, 11533, Statuses.Viewers(p.PlayerGuid))`, 11533 = PFX_Combat_Ice_Staff_SpecialAttack_Explosion). The per-target 10114 hit flash and the share 0.4 stay as they are. The effect id is our choice: neither build has an armour-specific ice effect. Leave damage and radius until a number is found (lead U1). | P5748 |
| F2 | Gear.cs `OnHit`, Shadow case (:131-132) + WeaponCombat.cs (state like `FirstPowerWhileExalted`, :1031) | Night Herald's Promise: `s.Power && Condemned && s.Hits.Count == 1` → fire only on the player's **first Power Attack against that opponent since it was Condemned**. Keep a per-player set of target guids already burst during their current Condemn, and clear the entry when Condemn ends or is applied again. | P5741, text 575681 |
| F3 | Creatures.cs `ThinkFungus` (:938-956) + Achievements.cs (new public `FungusDied(WorldSession)` wrapping `ProgressByText(..., "Dispatch explode-y mushrooms")`; `ProgressByText` is private at :492) | When a Fiery Fungus explodes, call FungusDied for **every live player near it**, not only the trigger. "Near" is not given by any source: use the players who can see it (`ViewersOf(c)`, already computed at :939), and note it as our rule. Do **not** count "Monsters killed", the family or "Kills with a ... weapon" texts (P4801 stays in force). Then remove the now-unreachable fungus branch from `Achievements.Killed` (:773-774), or keep it only for DevSlay. Verify with the client's own tracker: 1058 rises after a nearby fungus pops. | P5753, P4801 |
| F4 | Props.cs placement save (:772-815) or Achievements.PropPlaced | Count objective 1313 (Props To You: "Place the Starting Location prop") also when the player moves (re-saves) a Starting Location already on their site (`wasPlaced && changed && prop.PropId == StoryTools.StartingLocationProp`). Every site already has an auto-placed one (Claims.cs:1153), so one placement action must be enough. This is an inference from the patch note; confirm in a 0.695 client test that the palette refuses a second Starting Location. | P5752 |
| F5 | Templates.cs `StarterCastle` (:119-129) / `SendList` (:207-212) | Default Starter Castle designs: **27 → 6** (Castle Courtyard 574336, Castle Hallway - Long 574284, Castle Tower - Inline 574296, Castle Wall - Corner 574251, Castle Wall - Medium 574302, Castle Wall Entry 574257). The 13 castle pieces in reward bundle 1051 come as items from goal 471 (ItemUses design-item path). The 8 modular designs in the array (574303-574311): keep them for now, because their tutorial source is unknown (lead U3) and removing them would make Modular Castle * unobtainable. For existing characters, keep any starter designs they placed (designs are by guid `ST`+index; check Templates.Find callers before shrinking the array, or filter in SendList only). | P5750, capture 471 rewards |
| F6 | Achievements.cs `Complete` (:893-894) | Decide whether 477 (Getting Around) really gives the 13 castle design items again. The capture shows bundle 1051 on both 471 and 477, but the wiki lists only 30 Lumens + 5 score for each. If it is a duplicate, skip items the player already owns. Otherwise leave it. | capture |
| F7 | Combat/Statuses.cs:47 | Comment only: "Night Herald's Promise" → "Vestment of the Lightning Caller" (575677). | — |

Nothing to change for P5930 / P6439 / P6436 / P5835 (the 0.695 tables and the capture carry them) or for
P5749 / P5751 / P5760 (client).

## Unknowns to add to OPEN-LEADS.md (not written there by this audit)

- U1. Passive numbers: Frenzied Slayer's trigger chance before and after 7 Jun (P5747); Coat of Crashing Waves damage
  and radius (P5748). These are server-side, with no client table. Try forum threads after 7 Jun 2016
  (design/forum/all, "Frenzied", "Crashing Waves"), archived combat-feedback posts, and videos.
- U2. Where the recipe items 3996 / 3999 / 4000 / 4001 (Katana of Crashing Waves, Desperado's Deadly Arsenal,
  Lightbringer's Justice, Silent Shadow's Condemnation) came from. Recipes 2450 / 2453-2455 have no station mapping in
  either build, and VISIBILITY_REQUIREMENT is 1303 / 1306-1308. These are probably weapon-kill tier goal rewards (L42).
- U3. Which tutorial step unlocks the 9 modular castle design items 4098-4106 after the 7 Jun final pass. No goal in
  the Feb 2017 capture rewards them.
- U4. Whether Getting Crafty, Building Block Basics and Modular Thinking were retired by the 7 Jun final pass, or come
  after 685 Becoming a Luminary through AchievementAdd. The captured player had 685 in progress, so later chain goals
  would not show yet. This affects TASKS 5.6, which plans to build them.
