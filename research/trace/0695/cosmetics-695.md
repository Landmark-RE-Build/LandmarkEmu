# 0.695 cosmetics, late titles and Roll Wraps (TASKS 4.15 / 4.16 / 4.17, §4 "Outfits"; traced 2026-10-16)

[P] = proven from the client (0.695 test copy <0.695 test copy>, read-only), its data, the live dump or a patch note.
[I] = inferred; the evidence is given. Scratch scripts: the session scratchpad (getadr.py = extract a file from the
0.695 packs by re/out/fileindex_695.tsv offsets; tints.py = mesh → tint type → ArtTints aliases → dump users;
sheet.py = icon contact sheets; verify.py = mirror of the builder checked with re/items_decode.py).

## 0. Summary

- **What a worn outfit needs** (§1): an item record with chest slot 1, a model path
  `Char_Biped_[RACE][GENDER]_Entities_<mesh>_Chest.adr` and a tint alias from the mesh's tint type. Nothing else.
  All four meshes the missing outfits need ship in 0.695 for all four body types; no new outfit art came with 0.695
  (fileindex_diff: no new chest .adr, no new ArtTints rows for these families, no new outfit icons).
- **Built** (ItemDefinitionBuilder from 2521 / 4519, own ids): 22 outfits (6 Skeletal Bodysuits, 5 Holiday Sweaters,
  4 Colonist's Flight Suits, 4 Year Zero Survivor's Outfits, 3 Tech gear) and 6 title items (§2, §4).
- **Titles**: Titles.cs could not use 4519 Champion of the People (grant 60): its title text is ", Champion of the
  People" and only " X" / "X" were matched. Fixed (comma suffix). All six late titles now resolve (§4).
- **Roll Wraps** (§3): the record field u59 is a **DynamicInputActionSystems** id [P]. Roll Wraps' 90 is the one row
  0.695 added; its chain ends in InputActionGroupEntries group 153 = abilities **1207-1214** (Shift + direction), the
  same shape as Dash Soles (432, 807-813). The server must answer those casts like 326 (hand-off). Where Roll Wraps
  came from is still not found (L26).
- **Drops** (§5): seasonal switch LANDMARK_SEASON (off by default): Halloween → Skeletal Bodysuits from Outposts;
  Winter → festive Flight Suits + the 3 old sweaters from Outposts (wired), festive Flight Suits from Superb /
  Legendary chests (helper ready, wiring is a hand-off). Correction to TASKS 4.17: the 2016 Green / Blue sweaters were
  **giveaways, not drops** (P6570, notes 577745 / 577746).
- **Elegant Robe (Russet)**: 2521's own name text 569050 changed Red → Russet in 0.695 [P]; the server reads the 0.695
  text (4.19), so nothing to build.

## 1. How the client renders an outfit

| Field (re/items_decode.py) | Example 2521 Colonist's Elegant Robe (Russet) | Use |
|---|---|---|
| equipSlotId | 1 | chest |
| modelFile | `Char_Biped_[RACE][GENDER]_Entities_Colony_Light_000_Chest.adr` | mesh; [RACE][GENDER] → HumanMale / HumanFemale / HumanLargeMale / HumanLargeFemale (items-695.md §4) |
| tintAlias | `Colony_Light_10` | colour |
| u47 (categories) | [29] | every dump outfit |
| flags | [0,0,0] | tradeable |

Server side the same two strings go to every viewer in EquipmentSetCharacterEquipmentSlot (WorldSession.cs:300-310).

The alias must be listed for the mesh [P]: ArtSourceActors (mesh .adr) → ArtSourceActorTintTypes (tint type) →
ArtTintTypeTints → ArtTints.ALIAS_NAME (ArtTints.txt is new as a loose file in 0.695). Each .adr names a default alias
(`<TintAliases><AliasPerMaterial … defaultTintAliasPerMaterial="…">`).

**Colours per alias: not found yet** (L103). Searched: all 8.2 GB of 0.695 packs and every loose file for the alias
strings (only ArtTints.txt has them); the .adr / .dma of the Holiday mesh (DMAT holds hashed float4 material params, no
alias hash for Jenkins one-at-a-time, lookup2 or CRC-32 of the names); AlexTestTintSemanticTables.txt shows the format
of an alias → RGB table but the real one is not shipped loose; "TintSemantic" / "ArtTint" strings in the 0.695
decompile (none; only "BadTintAliasHashes.txt"). So which alias gives which colour is [I] below.

## 2. The outfits

| Family | Mesh (all 4 bodies in 0.695 packs) [P] | Tint type → aliases [P] | Dump users | Icons (ImageSets) [P] |
|---|---|---|---|---|
| Skeletal Bodysuit | Skeleton_Light_000 (actors 742/743/900/901) | 574 → Skeleton_Light_00..06 (default 00) | none | 2295 _White, 10735 _Cyan, 10736 _Green, 10737 _Orange, 10738 _Pink, 10739 _Red (wearable_Light_Costume_Skeleton_000_*) |
| Holiday Sweater | Holiday_Light_001 (853-856) | 585 → Holiday_Light_00 / 02 / 03 (default 00; id 711 "_01" absent) | none | 9648 _BrownGreen, 9649 _GreenTan, 9650 _RedBrown (wearable_Light_Holiday_000_*) |
| Colonist's Flight Suit | Colony_Heavy_001 (640/641/674/726) | 276 → Colony_Heavy_00..14 (default **05**), shared with Battle Armor / Patrol Armor | Colony_Heavy_001: none | 1905 wearable_Heavy_Colony_000_ProxySilver, 10415 wearable_Heavy_Colony_001_Pink |
| Year Zero Survivor's Outfit | Anniversary_Light_000 (911-914) | 601 → Anniversary_00..03 (default 00) | none | 10118 _Black, 10119 _Blue, 10120 _Red, 10121 _White (wearable_Light_Costume_Anniversary_000_*) |
| Tech gear (Founder's Packs) | Founder_000 / Founder_001 | 47 → Founder_00..09 (defaults 00 / 01) | 1385-1387 PlanetSide Commander's Gear use Founder_000 + Founder_05..07 | 749 wearable_Medium_Founder_000, 750 _001, 2162 _001_OrangeBlack |

Mesh per family [I, strong]: the icons were rendered (sheet.py) — ProxySilver and Colony_001_Pink show the same
one-piece suit, unlike Battle Armor (RedPlum) and Skirmish (OrangeBlack); the sweater, skeleton and anniversary icons
show those meshes; the five meshes are used by no dump item; Colony_Heavy_001's default alias Colony_Heavy_05 is the
Battle Armor "Grey" = the Flight Suit "(Silver)". "The Colonist's Flight Suit and other shiny outfits are shiny once
again" (forum 53766, 5 Mar 2015) matches a metallic suit. Tech Sergeant's Gear (Orange) text 564996 "black pants and
shirt with orange accents" matches icon Founder_001_OrangeBlack.

Built items (Game/Cosmetics.cs `Outfits`; template 2521; ids ours = 900000 + n):

| Id | Name (text) | Desc | Icon | Tint | Tint evidence |
|---|---|---|---|---|---|
| 900301 | Skeletal Bodysuit (White) 565425 | 565426 | 2295 | Skeleton_Light_00 | mesh default, oldest suit [I] |
| 900302-900306 | … (Pink / Orange / Cyan / Green / Red) 572805-572809 | 565426 | 10738 / 10737 / 10735 / 10736 / 10739 | Skeleton_Light_01..05 | text order [I]; _06 unused |
| 900311 | Holiday Sweater (Green & Tan) 566189 | 566191 | 9649 | Holiday_Light_00 | mesh default, first sweater [I] |
| 900312 | Holiday Sweater (Red & Brown) 566207 | 566191 | 9650 | Holiday_Light_02 | text order [I] |
| 900313 | Holiday Sweater (Brown & Green) 566208 | 566191 | 9648 | Holiday_Light_03 | text order [I] |
| 900314 | Holiday Sweater (Green) 577741 | 566191 | 9649 | Holiday_Light_00 | **placeholder** [I] |
| 900315 | Holiday Sweater (Blue) 577742 | 566191 | 9648 | Holiday_Light_03 | **placeholder** [I] |
| 900321 | Colonist's Flight Suit (Silver) 564149 | 564150 | 1905 | Colony_Heavy_05 | mesh default = "Grey" [I] |
| 900322 | Colonist's Flight Suit (Merry Red) 577737 | 577738 | 1905 | Colony_Heavy_13 | worn 2026-10-17: deep red on this mesh (03 = navy) [P runtime] |
| 900323 | Colonist's Flight Suit (Forest Green) 577739 | 577738 | 1905 | Colony_Heavy_02 | "Green" on 2514 / 4148 [I] |
| 900324 | Colonist's Flight Suit (Festive Blue) 577740 | 577738 | 1905 | Colony_Heavy_12 | worn 2026-10-17: blue on this mesh (04 = black / white) [P runtime] |
| 900331-900334 | Year Zero Survivor's Outfit (Blue / White / Red / Black) 568788, 568790-568792 | 568789 | 10119 / 10121 / 10120 / 10118 | Anniversary_00..03 | text order [I] |
| 900341 | Tech Sergeant's Gear 560076 | 560090 | 750 | Founder_01 | mesh default [I] |
| 900342 | Tech Sergeant's Gear (Orange) 564995 | 564996 | 2162 | Founder_08 | **placeholder** [I] |
| 900343 | Tech Commander's Gear 560078 | 560092 | 749 | Founder_00 | mesh default [I] |

The 2016 Green / Blue sweaters: the final client has no 4th / 5th Holiday tint and no new sweater icon, so live
reused existing ones; which ones is unknown (L103). Not built (no request in the notes): Year Zero Gift 568793 (a gift
box: wiki "received this outfit in one of three random colors"), Courtier's / Noble's Regalia, the Rancher's Outfits.

Ids [I]: no capture holds any of these items (L24). The gaps after 4524 are unproven, so the ids are ours
(OwnIdBase 900000 + 301..343), like the pets'.

## 3. Roll Wraps 4406 and the field u59 (L26)

u59 = DynamicInputActionSystems.ID [P]: the boots' values 51, 52, 64-69 and Roll Wraps' 90 are all rows of that table,
and **90 → state machine 84 is the only row 0.695 added** (diff vs the 0.589 install). Chain (all 0.695 Resources):

| Boot (item) | u59 | state machine → initial state → properties | InputActionGroupEntries (trigger, modifiers → ability) |
|---|---|---|---|
| Nimbus Boots 1120 | 51 | 48 → 74 → 80 (group 81) | 354: trigger 3 → 320 |
| Dash Soles 235 | 52 | 49 → 75 → 81 (group 82) | 355 → 432; 363-369 modifiers 1/7, 1/8, 0, 2/7, 2/8, 7, 8 → 807-813 |
| Breeze Boots 2851 | 64 | 58 → 83 → 87 (84) | 357: trigger 3 → 542 |
| Boots of Bounding 243 | 65 | 59 → 84 → 88 (85) | 358 → 321 |
| Leaper's Launchers 244 | 66 | 60 → 85 → 89 (86) | 359 → 322 |
| Neon Blitzing Boots 233 | 67 | 61 → 86 → 90 (87) | 360 → 327 |
| Adventurer's Sprint Boots 2178 | 68 | 62 → 87 → 91 (88) | 361 → **326** (the server's HeroicMovementAbility) |
| Messenger's Sandals 234 | 69 | 63 → 88 → 92 (89) | 362 → 328 |
| **Roll Wraps 4406** | **90** | **84 → 149 → 153 (153)** — all three rows new in 0.695 | **445-452: trigger 17; none → 1208; 2/7 → 1209; 2/8 → 1210; 1 → 1211; 1/7 → 1212; 1/8 → 1213; 7 → 1214; 8 → 1207** |

Also new in 0.695: CLIENT_REQUIRMENT_ID 42 on every boot row (RequirementExpressions 42 "CanUseBootAbilities" →
Requirements 74 CanUseBootAbility, text 577415 "Boot abilities are disabled on this Build Site.": Play Mode Rules bit 2,
already TASKS 2.1).

So equipping Roll Wraps (`give 4406 1`, it is in the dump) makes the client cast 1207-1214 on Shift. The roll's
name / description are server texts 577207 "Roll" / 577211 "Execute an acrobatic roll in any direction." (no client
table names them) [P text].

Server today (read only, not my files): MiscHandler.AbilityCasterInfoRequest answers only 326 as an instant boot
ability; other boot ids get the generic 1098 layout (60 m range, name 0). Abilities.HandleCast excludes only 326 from
the "weapon held → melee swing" path. **Hand-off** (owner of Abilities.cs / MiscHandler.cs): use
`BootAbilities.IsBootAbility(id)` (Game/BootAbilities.cs, data above) in both places, as for 326; name 1207-1214
with 577207 / 577211. This also fixes Dash Soles and the other boots.

Where Roll Wraps came from: still not found (L26). Recipe 5331 (1500 Smelted Indicite, 3000 Cut Tormeral,
2 Adventure Essence, 6 Affliction Essence; DEFAULT_LEARNED 0, no station mapping, TRACKER_ACHIEVEMENT 1467).

## 4. Titles (4.16)

The dump's 30 title items (3900-3929, 4363, 4519; category 229, flags [136,16,0], icon 1759) carry **grantIds 31-60 in
id order** [P]: the grant is the live title id (4519 → 60). Titles.cs keys titles by item id instead (the client
reads {id, type, text} from the packet, so either works; switching would break saved Unlocks — not changed).

Title texts in 0.695 [P]:

| Item | Name text | Title text | Type |
|---|---|---|---|
| 4519 Champion of the People (dump) | 577650 | 577649 ", Champion of the People" | suffix |
| 900201 The Lumerian Lens | 577697 | 577698 ", The Lumerian Lens" | suffix |
| 900202 Hero of Landmark | 577727 | 577731 ", Hero of Landmark" | suffix |
| 900203 Landmark Explorer | 577728 | 577059 "Landmark Explorer" (was "Curator" in 0.589; Curator moved to 577736) | prefix |
| 900204 Holiday Hero | 577729 | 577730 ", Holiday Hero" | suffix |
| 900205 Creator of Winter Wonderlands | 577733 | 577732 ", Creator of Winter Wonderlands" | suffix |
| 900206 Icy Architect | 577734 | 577735 "Icy Architect" | prefix |

Formats (client FUN_142d8b8a0, text 9516 / 9517): type 1 "[*title*] [*fullName*]", 2 "[*fullName*] [*title*]". The
comma texts are suffixes [I, by the comma]. Fix: Titles.SuffixText + the ", X" match (Titles.cs). Before it, 4519
logged "no title text found" and could not be used.

Built: template 4519, own ids 900201-900206, grants 61-66 [I] (Cosmetics.Titles). Sources [I]: 577743 "giveaway title
for sunset players", 577744 "Title for builders on winter island 2016" (developer notes in the text table); "Landmark
Explorer" is the Twitch channel of the Dec 2016 giveaways (P6570). No drop; dev give only. Champion of the People is
the goal "They Like You. They Really Like You!" (577647-577649; another agent's goal work).

## 5. Drops (4.17)

| When (2016) | Source | Items | Proof |
|---|---|---|---|
| 29 Sep - 5 Dec | Outposts (Lost Crystal loot) | Skeletal Bodysuits (all six [I]) | P6506 "you may also come across some skeletal body suits", P6565 "Halloween themed items no longer drop from outposts" |
| from 6 Dec | Outposts | festive Flight Suits ×3, the three older Holiday Sweaters | P6566, P6567 |
| from 6 Dec | higher-end Reward Chests | festive Flight Suits ×3 | P6569 |
| Dec | giveaways (Twitch, Mistletoe Madness) | Holiday Sweater (Blue) / (Green) | P6570; 577745 / 577746 — **not drops** |

Built: RareDrops.Season / Setting / Current / OutpostSeasonal / ChestSeasonal. Environment variable
**LANDMARK_SEASON** = off (default) | auto | halloween | winter; "auto" = Halloween 28 Sep - 5 Dec, Winter 6 Dec - 10 Jan
(end ours; "till early January", P6560). Chance 5 % per Lost Crystal loot / chest (ours). Wired in Outposts.cs (crystal
loot). Hand-off: RewardChests.Roll → `Add(RareDrops.ChestSeasonal(random), 1)` for Superb / Legendary (comment at
RewardChests.cs:313 already waits for this). The hats (RareDrops.Hat) are unchanged; live stopped them on 6 Dec (P6565)
— gating them by the season is a decision for the hat / chest owner.

## 6. Test steps (dev world restart needed for the new definitions)

1. `give 900301 1` … `wield 900321` (DevCommands give / wield): each item shows its name, description and icon; worn,
   the outfit shows on all four body types. **Compare the worn colour with the icon** for every [I] tint (§2) and
   correct Cosmetics.Outfits where they differ (the alias list of each mesh is in §2).
2. `give 4519 1`, use it: "New title: , Champion of the People" chat line, title list entry, shown after the name.
   Same for 900201-900206.
3. `give 4406 1`, equip Roll Wraps, press Shift with / without direction keys: the client casts 1207-1214
   (server log "Cast 1208 …"); until the hand-off in §3 the reply is the generic one.
4. Set LANDMARK_SEASON=winter for the world container, use Lost Crystals: occasional flight suit / sweater in the bag.

**Runtime check 2026-10-17** (test copy, dev `wield` + `weartint`, screenshots): Skeleton 00..05 = White, Pink, Orange,
Cyan, Green, Red; Holiday 00 / 02 / 03 = Green & Tan, Red & Brown, Brown & Green; Founder_01 red / grey, Founder_00 blue,
Founder_08 orange / black (= the Orange icon); Colony_Heavy on the flight suit: 00 black, 01 dark teal, 02 green, 03 navy,
04 black / white, 05 silver, 06 black, 07 dark grey, 08 steel blue, 09 black / dark red, 10 orange, 11 pink (= icon
10415), 12 blue, 13 deep red, 14 bronze; Anniversary 00..03 = navy, brown with a light top, red, black (fits the text
order; seen from behind only).

## 7. Open leads

- **L103** colours of the tint aliases (which alias = which colour; the 2016 sweaters' and the Orange Tech gear's
  aliases). Try next: render-check in game; the client's tint-loading path (callers of the ArtTints reader, the
  "BadTintAliasHashes.txt" writer) to find the colour source.
- **L24** (unchanged): live ids of all items above.
- **L26** (narrowed): u59 is decoded (§3); the source of Roll Wraps is not.
