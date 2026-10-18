# Spec: Fireworks (TASKS 3.9, 4.12, 4.13) — 0.695

Research 2026-10-15 (read-only). Sources: 0.695 tables in gameData695, `<0.695 install>\Resources\
ActorCompositeEffectDefinitions.xml` + `Images\ImageSets.txt`, the server's dumps (Resources/PacketDumps/44-CommandBase,
145-PropBase), the 0.695 decompile (re/decomp_all_695), text_695, patch notes P5913-P5915 / P6038 / P6041 / P6499,
forum_raw 57596 / 57616 / 57648. Builds on items-695.md §1.1 / §3, storytools-695.md §3 / §4.15, OPEN-LEADS L24 / L25 / L43.

**Proven** = read from a table, dump, client file or decompiled code. **Inferred** = reasoning from names, counts or
patch notes, not seen working.

---

## 1. Summary

Fireworks came on 30 Jun 2016 (P5913-P5915), in two forms with 22 kinds each:

1. **Consumables.** You use one from the bag. It is used up and a firework goes off over your head. In 0.695 the Lumen
   Station "Fireworks" section crafts only **packs** of them (7 recipes, 65-130 Lumens). The 22 consumable item
   definitions are **not** in the live item list, so their ids and abilities are unknown (L24 / L25). The client has
   no composite effect of its own for them. Its only firework visuals are the 22 emitter composites 12836-12857, so
   the server plays those over the player. That part is inferred.
2. **Emitter props 2949-2970.** They come as items 4455-4476 (random drops), or as "Recipe:" items 4477-4498 crafted at
   the Lumen Station ("Props - Special Effects", 130 / 260 Lumens). A recipe item teaches palette placement, and each
   placement costs **20 Cut Sunstone** (PropDefs PROP_RECIPE_ID 38 → component 5008). The only Link sentence is the
   action 577506 "...Launch this firework.". Each trigger in Play Mode plays the emitter's one-shot composite once
   through UpdatePropInteractionState on InteractableCompositeEffectSet 129-150.

What the server gets wrong today:
- StoryTools treats every "Fireworks Emitter (…)" as a **"fire" on/off light**: "Fire" comes before "Emitter" in
  `namedKinds`, StoryTools.cs:92 vs 94, and the name contains "Fire". As a result the editor offers "When this fire is
  lit…" and similar sentences, and never offers 577506.
- PropEffects' name matcher links only 6 of the 22 emitters to a set: twinkle ×4 and whirly ×2. The other 16 get no
  effect, because "standard", "streamers" and "triple fan" never appear in the props' descriptions. I simulated
  PropEffects.cs:114-124 against the 0.695 tables to check this. The 6 that do match then burst **every time a
  client spawns them**, because SendSpawns sends the "on" state (Props.cs:1343-1344), and they show an on/off button
  (RightClickMenus.cs:101).
- Nothing handles the packs: 4444-4449 have no use and 4443's ability 1237 is unknown to Consumables. The consumables
  themselves don't exist on the server.

---

## 2. Items & recipes

### 2.1 Packs (proven: dump 44-CommandBase, Recipes, RecipeRecipeComponentMaps, RecipeComponents, CraftingStationRecipeMappings)

All are category 290 "Fireworks", flags [8,0,0], stack 100, quality 2. Every recipe below is RECIPE_TYPE 1,
DEFAULT_LEARNED 1, CAN_CRAFT_WITHOUT_STATION 1, cast 0.25 s, mapped to station 55 (Lumen Station, mappings 5988-5994).
The cost is Lumens (item 1579).

| item | name / desc text | icon | use | recipe → component → Lumens | contents (desc text) |
|---|---|---|---|---|---|
| 4443 Pack of Unstable Fireworks | 577410 / 577412 | 21687 | **ability 1237, type 4** (record offset 215), "Combat" | 5332 → 5858 → 130 | "Leaves behind 50 'safe' fireworks … of a somewhat random variety" |
| 4444 Pack of Fireworks | 577413 / 577414 | 21681 | no ability; grantId **1124** | 5333 → 5859 → 65 | 5 each Gold, Green, Purple, Red Fireworks |
| 4445 Pack of Fancy Fireworks | 577417 / 577418 | 21682 | grant 1125 | 5334 → 5860 → 90 | 5 each Gold and White, Green and Blue, Purple and Pink, Red and Orange |
| 4446 Pack of Streamers | 577419 / 577420 | 21683 | grant 1126 | 5335 → 5859 → 65 | 5 each Gold / Green / Red Curved Streamers, Gold Straight Streamers |
| 4447 Pack of Triple Fan Fireworks | 577421 / 577422 | 21685 | grant 1127 | 5337 → 5860 → 90 | 5 each Blue / Gold / Purple / Red Triple Fan |
| 4448 Pack of Twinkle Fireworks | 577423 / 577424 | 21686 | grant 1128 | 5336 → 5859 → 65 | 5 each Gold / Green / Purple / Red Twinkle |
| 4449 Pack of Whirly Fireworks | 577425 / 577426 | 21688 | grant 1129 | 5338 → 5860 → 90 | 10 Gold Whirly, 10 Purple Whirly |

grantId sits at record length − 20 (the 6th u32 of the record's last 40 bytes) in 4444-4449 / 4477-4498. The server's
parser skips it (ItemDefinitionManager.cs:202-251). ImageSet 21684 `marketplace_chest_fireworks_fancy` is not used by
any item (4445 uses 21682).

### 2.2 The 22 consumables (definitions missing: L24 / L25)

The names, descriptions and icons are proven from text_695 and ImageSets. The composite is the matching emitter
composite (by name, inferred). The item id is a **candidate only**. The 22-id gap 4421-4442 lies between 4419 (the
proven Fantasy Parlor Pack, text 577355) / 4420 (probably White Cat Eye, 577362) and 4443 (Unstable pack, 577410).
The texts run in that same order, and 4450-4454 likewise fits the 5 Fantasy Skintones 577427-577436 that come before
the emitters at 4455. Text order is not strictly id order, though (Combat Node 4408 = 577196 but Roll Wraps 4406 =
577205), so this stays unproven.

| # | name / desc | icon (ImageSets) | composite (emitter set) | candidate id |
|---|---|---|---|---|
| 1 | Gold Fireworks 577366 / 577367 | 21692 props_Fireworks_Standard01_Gold | 12836 (set 129) | 4421 |
| 2 | Green Fireworks 577368 / 577369 | 21693 | 12837 (130) | 4422 |
| 3 | Purple Fireworks 577371 / **577370** | 21694 | 12838 (131) | 4423 |
| 4 | Red Fireworks 577372 / 577373 | 21695 | 12839 (132) | 4424 |
| 5 | Gold and White Fireworks 577374 / **577438** | 21698 Standard02_Goldwhite | 12840 (133) | 4425 |
| 6 | Green and Blue Fireworks 577376 / **577375** | 21699 | 12841 (134) | 4426 |
| 7 | Purple and Pink Fireworks 577378 / **577377** | 21700 | 12842 (135) | 4427 |
| 8 | Red and Orange Fireworks 577379 / 577380 | 21696 or 21697 (`Standard01_Redorange`, two rows) | 12843 (136) | 4428 |
| 9 | Gold Curved Streamers 577381 / 577382 | 21689 Curved01_Gold | 12855 (137) | 4429 |
| 10 | Green Curved Streamers 577383 / 577384 | 21690 | 12856 (138) | 4430 |
| 11 | Red Curved Streamers 577385 / 577386 | 21691 | 12857 (139) | 4431 |
| 12 | Gold Straight Streamers 577387 / 577388 | 21701 StreamersStraight_Gold | 12854 (140) | 4432 |
| 13 | Blue Triple Fan Fireworks 577389 / 577390 | 21703 (`Triplefan_Green`; the Blue emitter 2961 also uses its Green icon 21725) | 12844 (141) | 4433 |
| 14 | Gold Triple Fan 577391 / 577392 | 21702 | 12845 (142) | 4434 |
| 15 | Purple Triple Fan 577393 / 577394 | 21704 | 12846 (143) | 4435 |
| 16 | Red Triple Fan 577395 / 577396 | 21705 | 12847 (144) | 4436 |
| 17 | Gold Twinkle 577397 / 577398 | 21706 | 12848 (145) | 4437 |
| 18 | Green Twinkle 577399 / 577400 | 21707 | 12849 (146) | 4438 |
| 19 | Purple Twinkle 577401 / 577402 | 21708 | 12850 (147) | 4439 |
| 20 | Red Twinkle 577403 / 577404 | 21709 | 12851 (148) | 4440 |
| 21 | Gold Whirly 577405 / 577406 | 21710 | 12852 (149) | 4441 |
| 22 | Purple Whirly 577407 / 577408 | 21711 | 12853 (150) | 4442 |

Text quirks (proven): the descriptions in rows 3, 6 and 7 come **before** their names. 577375 ("…Green and Blue…")
directly follows the Gold and White name, and that item's real description is the later string 577438.

The 0.695 client has no recipe for a single consumable: Recipes 5331-5364 hold only the packs, the emitter recipe items
and the chests. P5914 ("crafted at the Lumen Station in the section called 'Fireworks'") matches the packs (category
290). 577365 "Fireworks" is the category name.

### 2.3 Emitter items, recipe items, recipes (proven)

- Emitter items **4455-4476**: category 176 "Prop - Special Effects", flags [8,0,0], stack 20, quality 0, places prop
  2949-2970 in order (u35[3]). Description 577461 "This emitter prop can be placed on your Build Site." No recipe. They
  come from random drops (P5915; a player saw one drop on day one, forum 57596 p.7 #134).
- Recipe items **4477-4498**: category 176, flags [8,16,0], stack 1, quality 4. grantId = prop 2949-2970 (4477 → 2949 …
  4498 → 2970). Names 577462-577504 (even), descriptions odd. Made by recipes **5339-5360** (in order). Those recipes
  have SORT_ORDER 110000, DEFAULT_LEARNED 1, CAN_CRAFT_WITHOUT_STATION 1, **MAX_QUEUE 1**, **VISIBILITY_REQUIREMENT_ID
  1563-1584** (meaning unknown, L43) and station 55 (mappings 5995-6016).
- Recipe cost (Lumens, component 5836 = 130, 5837 = 260) matches the rarity in each prop's 145-PropBase description
  ("uncommon" = 130, "rare" = 260).
- Recipe-discovery achievements 1468-1489 (22 rows, subcategory 242 "Recipe Discoveries", objectives 2181-2202) are in
  the capture. wiki-content-audit says they belong to these recipe items. That they map in id order is inferred.

---

## 3. Emitter props

### 3.1 Per-prop table (proven, except where marked)

All 22 share these properties:
- PropDefs: TRIVIAL 0, MIN/MAX_SCALE 0 (the dump says 0.5-10), DRAW_IN_PLAYER_TEMPLATES 1, **PROP_RECIPE_ID 38**,
  ALLOW_USER_RENAME 0.
- Model Theme_Widgets_Motes_Emitter01.adr (model id 1400).
- Palette category 17 "Emitters" (PropCategoryPropMap 1889-1910).
- No row in any other client table: grepped every gameData695 table, including EditableProperties.

Prop recipe 38: PropRecipeComponentMaps 87 → RecipeComponents 5008 = item 1754 **Cut Sunstone × 20**.

| k | prop | name text | icon | 145-PropBase desc | item | recipe item | recipe | Lumens | vis.req | ICE set (rows) | composite |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 2949 | 577439 (Gold) | 21715 | uncommon: gold | 4455 | 4477 | 5339 | 130 | 1563 | 129 (278, 300) | 12836 Standard01_Gold |
| 1 | 2950 | 577440 (Green) | 21716 | uncommon: green | 4456 | 4478 | 5340 | 130 | 1564 | 130 (279, 301) | 12837 |
| 2 | 2951 | 577441 (Purple) | 21717 | uncommon: purple | 4457 | 4479 | 5341 | 130 | 1565 | 131 | 12838 |
| 3 | 2952 | 577442 (Red) | 21718 | uncommon: red | 4458 | 4480 | 5342 | 130 | 1566 | 132 | 12839 |
| 4 | 2953 | 577443 (Gold and White) | 21719 | rare: gold/white | 4459 | 4481 | 5343 | 260 | 1567 | 133 | 12840 Standard02_GoldWhite |
| 5 | 2954 | 577444 (Green and Blue) | 21720 | rare: green/blue | 4460 | 4482 | 5344 | 260 | 1568 | 134 | 12841 |
| 6 | 2955 | 577445 (Purple and Pink) | 21721 | rare: purple/pink | 4461 | 4483 | 5345 | 260 | 1569 | 135 | 12842 |
| 7 | 2956 | 577446 (Red and Orange) | 21722 | rare: red/orange | 4462 | 4484 | 5346 | 260 | 1570 | 136 | 12843 |
| 8 | 2957 | 577447 (Gold Curved Streamer) | 21712 | uncommon: gold curved | 4463 | 4485 | 5347 | 130 | 1571 | 137 | 12855 StreamersCurved_Gold |
| 9 | 2958 | 577448 (Green Curved Streamer) | 21713 | uncommon: green curved | 4464 | 4486 | 5348 | 130 | 1572 | 138 | 12856 |
| 10 | 2959 | 577449 (Red Curved Streamer) | 21714 | uncommon: red curved | 4465 | 4487 | 5349 | 130 | 1573 | 139 | 12857 |
| 11 | 2960 | 577450 (Gold Straight Streamer) | 21723 | uncommon: gold straight | 4466 | 4488 | 5350 | 130 | 1574 | 140 | 12854 StreamersStraight_Gold |
| 12 | 2961 | 577451 (Blue Triple Fan) | 21725 | rare: blue triplefan | 4467 | 4489 | 5351 | 260 | 1575 | 141 | 12844 TripleFan_Blue |
| 13 | 2962 | 577452 (Gold Triple Fan) | 21724 | rare: gold triplefan | 4468 | 4490 | 5352 | 260 | 1576 | 142 | 12845 |
| 14 | 2963 | 577453 (Purple Triple Fan) | 21726 | rare: purple triplefan | 4469 | 4491 | 5353 | 260 | 1577 | 143 | 12846 |
| 15 | 2964 | 577454 (Red Triple Fan) | 21727 | rare: red triplefan | 4470 | 4492 | 5354 | 260 | 1578 | 144 | 12847 |
| 16 | 2965 | 577455 (Gold Twinkle) | 21728 | uncommon: gold twinkle | 4471 | 4493 | 5355 | 130 | 1579 | 145 | 12848 Twinkle_Gold |
| 17 | 2966 | 577456 (Green Twinkle) | 21729 | uncommon: green twinkle | 4472 | 4494 | 5356 | 130 | 1580 | 146 | 12849 |
| 18 | 2967 | 577457 (Purple Twinkle) | 21730 | uncommon: purple twinkle | 4473 | 4495 | 5357 | 130 | 1581 | 147 | 12850 |
| 19 | 2968 | 577458 (Red Twinkle) | 21731 | uncommon: red twinkle | 4474 | 4496 | 5358 | 130 | 1582 | 148 | 12851 |
| 20 | 2969 | 577459 (Gold Whirly) | 21732 | rare: gold whirly | 4475 | 4497 | 5359 | 260 | 1583 | 149 | 12852 Whirly_Gold |
| 21 | 2970 | 577460 (Purple Whirly) | 21733 | rare: purple whirly | 4476 | 4498 | 5360 | 260 | 1584 | 150 | 12853 |

**The prop → set link is inferred** from matching names (set = prop − 2820 for all 22), because no client table links a
prop to an ICE set (PropEffects.cs:15-19). The match is exact for every row, including the curved / straight / triple
fan order, which differs between the props' order and the composites' order.

InteractableCompositeEffects (proven): each set 129-150 has **two rows, index 0 and index 1, both "… On"**, with the
same composite on socket FX_Attach and SCRIPT_EVENT_ID_GROUP 3. No set has an "Off" row.

Composite contents (ActorCompositeEffectDefinitions.xml lines 14183-14412, proven):
- Burst sets (12836-12853): an FX launch effect at t = 0 (one of 2282-2285, likely the four
  Firework_Rockets_Launcher_50/60/70/80m.fxd), the burst FX 2264-2281 at **t = 1.0 s**, and a SOUND (11049 / 11050
  Standard, 11054 TripleFan, 11055 Twinkle, 11056 Whirly).
- Streamers (12854-12857): one FX (2286-2289) plus a SOUND (11052 / 11053) at t = 0.
- None of the 22 composites existed in 0.589.
- The FX id → .fxd mapping is not in a loose file (the 26 Firework_*.fxd are in Assets packs). Mapping them is not
  needed for the server.

### 3.2 Story-tool behaviour (proven text, inferred rules)

- Link vocabulary: action **577506 "...Launch this firework."** No firework-specific event exists in text_695. From 29
  Aug 2016 every prop also has the generic "When a Luminary clicks this object..." 576311 (P6432; storytools-695 §4.15).
- P5915: "fire off once when triggered on your claim, and are intended to be used with the Storytelling Props". P6499
  gives an IF Gate example ("set off some fireworks"). Links run only in Play Mode (StoryTools.cs header, `Run`).
- Inferred: nothing plays on spawn or when Play Mode starts, there is no on/off state, and no toggle buttons are shown.
- No settings (no EditableProperties row; storytools-695 lists none for 2949-2970).

---

## 4. Client behaviour (0.695 addresses)

| What | Address (0.695) | 0.589 | Notes |
|---|---|---|---|
| UpdatePropInteractionState reader | FUN_142c2e170 (same) | FUN_142b94680 | layout as PropEffects.cs:21-25: u64 guid, u32 behaviour (1 = ClientPropInteractableCompEffectStateBehavior), u32 key, state {u32 value, u32 previous, u64, u32 n × [i32 set, 0, start-offset ms, apply-flag], u32 n × string}, bool |
| state apply | **FUN_1430ac970** (changed) | FUN_1430065e0 | Looks up the stored value for (entity, key). value −1 removes it, otherwise stores it. Then: if the stored old value == state.previous **and** the trailing bool == 0 → behaviour vtable +8, otherwise vtable +0x10 |
| CompEffect behaviour vtable | 144779ee8 {143476480, **143476bb0**, **143476ad0**} (rtti_695.tsv:5751) | 1446b0770 | +8 = FUN_143476ad0: plays only if the first list entry's 4th i32 (apply flag) ≠ 0. +0x10 = FUN_143476bb0: always plays |
| play | **FUN_1430aaeb0** | — | If value ≥ 0, `FUN_143d22f30(mgr, set, value)` returns the set's row **at index = value**. If found, it calls the composite player (`this+0x4b50` vtable[0]) with key, composite id, socket, the u64 and start offset = ms × 0.001. Otherwise it stops |
| set row lookup | FUN_143d22f30 | — | returns 0 for index < 0 or ≥ row count (so values 0 and 1 are both valid for sets 129-150) |
| stop | FUN_1430abfe0 | — | removes the effect for the key |
| PlayCompositeEffect (PlayerUpdate sub 0x0B) | dispatcher FUN_142cdfa60 case 10/0xb, reader **FUN_142c2a0c0** (changed: u16 + **u32 sub**) | FUN_142b906d0 | Body as HitEffects.cs:19-21 after the 0.695 header. Target readers FUN_142c03170 (character, same) and FUN_142c03a10 (location, same) |
| item use with ability (type 4) | cast → server AbilityBase attempt → Abilities.cs:208-216 → Consumables | same | the path Cozy Campfire (ability 1098) uses |
| item use without ability | Inventory 0x15 ActivateItem → InventoryHandler.cs:153-166 | same | the path the packs 4444-4449 and the recipe items take |

Consequences, inferred from the code above:
- Re-sending value 1 with previous = −1 and apply flag 1 (exactly what PropEffects.Send does) always replays the
  composite. If the client holds no state, old −1 == previous −1 and bool 0 go through +8, which plays because the flag
  is 1. Otherwise the call goes through +0x10, which plays.
- The duplicate index 0 / 1 rows suggest live **toggled the value 0 ↔ 1** on each trigger so that every launch is a
  state change. Either way works with this client.
- After a burst ends, the stored client state stays. A viewer who spawns the prop later must **not** be sent the state,
  or they see a launch.

---

## 5. Server design

### 5.1 Reuse as is
- **Placement and cost**: Props.RecipeCost (Props.cs:1433-1446) already charges PROP_RECIPE_ID 38 = 20 Cut Sunstone for
  palette placement. Placing from an emitter item 4455-4476 already works through ActivateItem → `PropId` →
  `Props.BeginPlacement` (InventoryHandler.cs:163-165).
- **Learning a recipe item**: RecipeBook name-match (RecipeBook.cs:37-75) now reads 0.695 text (MODLOG 2026-10-15
  GAMEDATA695). "Recipe: Fireworks Emitter (Gold)" finds item "Fireworks Emitter (Gold)" 4455, which has PropId 2949
  and no crafting recipe, so `propsByItem` gets it. LearnProp (RecipeBook.cs:185-200) then unlocks
  `Unlocks.PropTaught`, and KnowsProp (RecipeBook.cs:85-96) gates the palette. Inferred: verify the startup log
  "… teach a prop" count rises by 22.
- **Crafting the packs and recipe items**: Recipes / RecipeRecipeComponentMaps / CraftingStationRecipeMappings are 0.695
  copies in gameData695 and DEFAULT_LEARNED 1, so they show at the Lumen Station.
- **Effects**: PropEffects.Send (PropEffects.cs:164-184) for the emitter state, and HitEffects.PlayOn / PlayAt
  (HitEffects.cs:99-132) for the consumables. The 0.695 PlayerUpdate u32-sub framing is handled by the session's
  0.695 wire translator (WorldSession.cs:1177 `Speaks695`).

### 5.2 Add / change

**A. PropEffects — explicit fireworks map and one-shot flag** (PropEffects.cs)
1. After `Build` (end of the try, before `byProp = map`), map props 2949-2970 → set `prop − 2820`. Put the "on" index
   in a separate `oneShot` set, and drop or overwrite whatever the word matcher picked: it gets 6 right and 16 wrong
   or missing. Better still, check each pair by name: the set name `EFX_Prop_Emitter_Fireworks_<Style>_<Colour>`
   against the prop's PropBase description. Log a warning on any mismatch.
2. Add `IsOneShot(propId)`, and `Launch(IEnumerable<WorldSession> viewers, uint propId, ulong guid)`. Launch flips a
   per-guid value 0 ↔ 1, writes previous = −1, `u64 0`, the list `{set, 0, 0, 1}` and bool 0, and sends to the viewers.
   One body is built once for all viewers (bandwidth).
3. Keep the one-shot props out of `HasEffect`, or have the callers check `IsOneShot`, so that:
   - `Props.SendSpawns` (Props.cs:1343-1344) sends **nothing** for them, so there is no burst on spawn;
   - `Props.SetEnabled` (Props.cs:973-984) and `RightClickMenus.PropMenu` (RightClickMenus.cs:101) offer no on/off;
   - `StoryTools.ShowOn` (StoryTools.cs:657-675) and `Stop` never toggle them;
   - `Props.SendRemove` (Props.cs:1692-1696) may still send −1. That stops a burst in flight when the prop goes away,
     which is acceptable.

**B. StoryTools — emitter kind and action** (StoryTools.cs)
1. Add `public const uint LaunchFirework = 577506;` and entries in `kindsById` (StoryTools.cs:170+) for 2949-2970:
   `new Kind { Name = "fireworks emitter", Actions = new[] { LaunchFirework } }`. Optionally also add Events
   `{ ClickedEvent }` if the P6432 "every prop" click event is implemented generally. Being in `kindsById` stops the
   name match from choosing "Fire" (StoryTools.cs:92).
2. In `Execute` (StoryTools.cs:458+), add `case LaunchFirework:` →
   `PropEffects.Launch(Viewers(site, r.Position), r.PropId, r.Guid)`.
   - Viewers are players in `site.Zone` within ~300 m who are not re-zoning. The bursts reach 50-80 m up and are seen
     from far away. Reuse the `p.RezoneTarget == null` guard from ShowOn; see project rule "cave teleport hang".
   - Add a per-emitter minimum interval (ours, e.g. 1 s), so that a looping Timer chain cannot flood the clients.
   - No event follows.
3. `PlayModeChanged` (StoryTools.cs:384-414) needs no change: a Running entry with a non-toggle kind is created and does
   nothing at start.

**C. Recipe items via grantId** (optional, more robust than name matching)
- Parse grantId in `ItemDefinitionManager.Parse` (ItemDefinitionManager.cs:202-251): the u32 at record length − 20 in
  these records. Confirm the field position against the client reader (items-695 §2 traced FUN_142c176d0 /
  FUN_142c34a10) before relying on it for all items.
- In `RecipeBook.RecipesByItem`, prefer grantId: a PropDefs id means teach a prop, a Recipes id means teach a recipe.
  This matches the dump: 526 of the 584 "Recipe:" items grant a prop and 58 grant a recipe (items-695 §1.1).
- Hook `Achievements.RecipeLearned` (or a prop variant) into `LearnProp` (RecipeBook.cs:185-200), so the Recipe
  Discoveries 1468-1489 can progress. Their objective text has to be checked against 0.695 text first.

**D. Packs** (ItemUses.cs)
- Add a `fireworksPacks` table: item → [(consumable, count)], taken from the descriptions in §2.1 (4444-4449). Check it
  in `Handles` (ItemUses.cs:74-80) and `TryActivate` (ItemUses.cs:83-119).
- Unpack them like `Unpack` (ItemUses.cs:274-290): a space check (`SpaceFor` per kind), `TakeItem(pack, 1)`, then
  `GiveItem` for each kind, plus a chat line.
- The packs carry grantId 1124-1129, which point to server-side reward sets with no client table. The descriptions are
  the only source for their contents.

**E. Consumables** (Consumables.cs + item definitions)
1. **Definitions.** Synthesize the 22 item records at load: clone pack 4443's decompressed record (282 bytes, ability
   type 4, "Combat", the same shape as Cozy Campfire 3894) and patch these little-endian u32 fields:
   - id @0;
   - name @7, description @11, icon @15 (from §2.2);
   - category @183 = 290 (keep it; the in-game category "Fireworks");
   - ability id @215 = a server ability id (below).

   Re-wrap each record as {u16 compressed, u16 size, LZ4 block}. A literal-only LZ4 block is valid. Add the records to
   `definitions` and the ability index. Either append them to the 0x4B login list (keep the count right) or serve them
   through 0x4A `SendDefinition` (ItemDefinitionManager.cs:182-195) and the ItemAdd embedded-definition path
   (items-695 §2.1).
   - Ids: use the candidates 4421-4442 (§2.2), flagged as unproven in code comments and in L24.
   - Ability ids: live's are unknown. The 45 ids 1192-1236 are unused in the dump, just below the Unstable pack's 1237;
     pick 1215-1236 and say they are ours.
2. **Use effect.** Add 22 entries to `effects` (Consumables.cs:117-148): `new("Gold Fireworks", Launch(12836))`, where
   Launch:
   - plays the composite with `HitEffects.PlayAt(session.Position, composite, viewers)` (TargetLocation, so the rocket
     stays where it was lit). The alternative is `PlayOn(session.PlayerGuid, …)`, which attaches it to the character
     and moves with them. Which one live used is unknown; P5914 says only "over your head";
   - viewers are players in the zone within ~300 m, the player included;
   - returns `Use.Consume`. `TryUse` takes one (Consumables.cs:163-172), and CastSuccess / CastFail already follow
     (Abilities.cs:208-216);
   - adds a short cooldown (ours, ~2 s), so held-key spam can't flood viewers.
3. **Unstable pack** (ability 1237 → Consumables): `Use.Consume` the pack and give 50 consumables, each kind chosen at
   random from the 22 (description 577412). It should first check that there is room for up to 22 stacks, and refuse
   (`Use.Refuse`) if there isn't. Optional: play one random composite at the player as its "unstable" flourish (L25).

**F. Drops**
- Add the emitter items 4455-4476 to a rare prop drop. P5915 says "random drops, like other props". The server has no
  prop-item drop yet: Gathering.RollDrops (Gathering.cs:32-52) and the Outposts loot (Outposts.cs:421, 482) drop
  "Recipe:" items only. Weight "rare" (260-Lumen) kinds below "uncommon". The rates are ours.
- Keep the recipe **items** 4477-4498 **out** of `RecipeBook.RandomUnknownRecipeItem` (RecipeBook.cs:127-134), because
  they are bought for Lumens. After change C they would become candidates.

### 5.3 Docs / bookkeeping
- In LandmarkEmu/docs, document the ICE one-shot state (value toggle, previous −1, apply flag) and the fireworks item
  record template.
- Update PropEffects' header comment with the 0.695 addresses (§4).
- MODLOG entry; TESTING-NEEDED.md.

### 5.4 Verification (client reaction, on <0.695 test copy> per memory)
1. Place an emitter, then link a Switch's "When a Luminary clicks this object..." to 577506.
   - In Play Mode, the burst plays once per click.
   - A second viewer who arrives later sees no burst at spawn.
   - The link editor offers only "...Launch this firework." on the emitter.
2. Use one consumable: the burst shows over the player, one item is gone, and the hotbar cooldown is fine.
3. Open each pack 4444-4449 and the Unstable pack: counts as in §2.1 / 50.
4. Craft Recipe 5339 at the Lumen Station, use it, then place prop 2949 from the palette: 20 Cut Sunstone is charged.
5. Check whether the burst composite ends on its own. If it loops (unexpected for a one-shot .fxd), add a timed −1
   clear, about 6 s after the launch.

---

## 6. Unknowns / leads

- **L24 (open)**: the 22 consumables' real item ids and definitions. Candidates 4421-4442 come from gap / text order
  only. **Try next**: any 0.695-era capture with ItemAdd / 0x4A for these names; Marketplace / CoinStore product lists;
  forum screenshots.
- **L25 (narrowed)**: the client has **no** separate consumable or pack composite. All 22 firework composites are the
  emitter ones (XML ids 12836-12857; nothing else matches "Firework" in ActorCompositeEffectDefinitions). Still
  unknown:
  - the consumables' ability ids;
  - whether live played the composite on the character or at a location;
  - ability 1237's own flourish;
  - reward sets 1124-1129 beyond the descriptions.
- **L43**: VISIBILITY_REQUIREMENT_ID 1563-1584 on recipes 5339-5360, together with MAX_QUEUE 1. A plausible reading,
  inferred: "hide the recipe once the prop is known", or "show it only after an emitter drop / discovery". No
  Requirements rows exist (searched). Note that 1563-1584 and achievements 1468-1489 are both 22 consecutive ids, 95
  apart, but that proves no link. **Try next**: the client's recipe-list visibility check in the 0.695 crafting UI
  (Recipes reader FUN_143bf6ed0 field users).
- **CAN_CRAFT_WITHOUT_STATION 1** on all 0.695 Lumen Station recipes (5332-5364) while they are also mapped to station
  55. It needs checking how the 0.695 crafting UI treats it, whether it is craftable from the bag.
- Whether live emitters fired on **click outside Play Mode** or for the owner in Build Mode (a test button). Nothing
  in the sources says so; the spec fires them only through Links in Play Mode.
- FX ids 2282-2285 → launcher heights 50/60/70/80 m is inferred from counts only. It is cosmetic and not needed.
- Consumable icon for "Red and Orange": 21696 vs 21697 (two `Standard01_Redorange` rows). "Blue Triple Fan" uses the
  `Triplefan_Green` art (21703 / 21725) in both forms.
- grantId field position: confirmed only empirically (record length − 20 in the fireworks / chest records). Confirm it
  against the 0.695 definition reader before the general RecipeBook change C.
