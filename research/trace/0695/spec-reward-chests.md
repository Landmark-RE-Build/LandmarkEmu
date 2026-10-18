# Spec: Reward Chests (TASKS 3.11 / 4.14)

Read-only research, 2026-10-15. Nothing in LandmarkEmu, the installs or Docker was changed. Labels used below:
**[P]** = proven (client decompile, client data table, server dump, or a developer post quoted with its id);
**[I]** = an inference from that evidence; **[U]** = unknown, listed in §6.

Existing research used, not redone: items-695.md §1.1 / §3 (item rows, recipes, texts), storytools-695.md §1.4, §4.15,
§6.9 (settings window, Link sentences), CLIENT-DIFF.md, text-new.tsv, patchnotes.tsv P6399-P6517, the forum threads in
design/forum/all (57902 update notes, 57918 feedback thread, 57987 29 Sep update, 57990, 58108), wiki page
"Common Reward Chest", trace/code-strings.md, trace/architecture/social-service.md (mail kinds). New traces below are
marked with the function address.

---

## 1. Summary

- Six chest items (category 292 "Reward Chest"), each placing its own prop 3003-3008. Only three can be crafted in
  0.695: **Common 4510 (130 Lumens), Superb 4514 (260), Legendary 4515 (780)** at the Lumen Station (55). The three
  middle tiers (Stonedelver's / Orefinder's / Gemseeker's) are retired ("[OLD]" props, no recipe).
- You place the item from your bag like any prop item (P6412). In 0.695 the settings window has **one slider,
  "Player looting cooldown"** (EditableProperties 104: 15-120 minutes, default 30). It also has **one read-only text
  row for the count remaining** (row 106 `loot_chest_remaining_count`, BLOCKS_USER_MODIFICATION = 1). The client
  draws that row as a `PropertyTextLine`, which is new in 0.695.
- A visitor clicks the chest, or a Link fires **"...Loot this chest"** (577598). The rules are:
  - the site must be in Play Mode, and the chest's owner never gets loot (P6416, 577633);
  - each player has their own lockout per chest (P6413);
  - each loot raises **"When this chest is looted..."** (577597);
  - since 29 Sep the whole group gets loot (P6503).
- Uses: Common 10, Superb 20, Legendary 40 (the item descriptions 577607 / 577632 / 577635). At 0 the chest is
  consumed and the owner gets the "Reward Chest Expiration!" mail (577638 / 577639) with a reward (P6502). The reward
  is very likely a **Thank You Token** (577640). It counts toward goal 1490 "No Good Deed..." (objective 2209, reward
  15 Lumens, in the server's own achievement dump).
- Once a chest has paid out, its owner cannot pick it up: PropBase PickUpReply error **0xE** →
  `Prop.PropPickupErrorPartialLootChest` 577600 (0.695 FUN_142fba9f0 case 6).
- The server has **none of this yet**. Placement already works through the generic prop-item path. Reuse the
  storage-chest / treasure-chest interaction, LootPinatas bags + LootNotification, PropSettings, StoryTools Links,
  Groups, Claims play mode, Mail.SendSystemMail and SystemMessages.ShowKey. It depends on the 0.695 data layer
  (PropDefs / EditableProperties / Recipes / text for 0.695 sessions) and on TASKS 2.18 (f64 settings records).

---

## 2. Items, props and recipes

### 2.1 Items (server dump 44-CommandBase, 0.695-era; items-695.md §1.1) [P]

| item | name (text) | description (text) — uses | places prop | recipe @ Lumen Station 55 |
|---|---|---|---|---|
| 4510 | Common Reward Chest (577596) | 577607 — "common resources, such as stone, ice, sand, and wood, in addition to exceptional surface resources … good for **10 uses**" | 3003 | 5362: 1 × item 1579 Lumen ×**130** |
| 4511 | Stonedelver's Reward Chest (577602) | 577607 (same text in the dump) | 3004 [OLD] | — |
| 4512 | Orefinder's Reward Chest (577603) | 577607 | 3005 [OLD] | — |
| 4513 | Gemseeker's Reward Chest (577604) | 577607 | 3006 [OLD] | — |
| 4514 | Superb Reward Chest (577605) | 577632 — "an abundance of exceptional resources, such as rare stone, metal, and gems in addition to common resources … **20 uses**" | 3007 | 5363: Lumen ×**260** |
| 4515 | Legendary Reward Chest (577606) | 577635 — "outpost loot, such as lumens, essence, and the occasional recipe in addition to a ton of common and exceptional resources … **40 uses**" | 3008 | 5364: Lumen ×**780** |

- All are category 292, icon 1334, flags [8,0,0], stack 1. Description ids were read from the item records in
  44-CommandBase: name at record +8, description at +12.
- Unused draft descriptions with "X uses": 577612 stone, 577613 metal, 577616 gem, 577617 "mix of uncommon and rare",
  577618 "rare resources". These are the retired tiers and earlier wordings.
- The higher-tier names match P6501 ("two higher-tier reward chests … at the lumen station, or also available in the
  marketplace for DBC"). The Common price of 130 Lumens is confirmed by players in 57902 #19 / #22 and 57918 #6 / #16.
- Note the gap: 4516-4518 are unused ids between the chests and the title 4519. They are candidates for the Thank You
  Token (OPEN-LEADS L24, unproven).

### 2.2 Recipes (gameData695) [P]

- Recipes.txt 5362 / 5363 / 5364: ITEM 4510 / 4514 / 4515, count 1, RECIPE_TYPE 1, CASTING_TIME 0.25,
  DEFAULT_LEARNED 1, STACK 1, CAN_CRAFT_WITHOUT_STATION 1. These are the same values as the fireworks-pack rows
  5333 / 5334.
- RecipeRecipeComponentMaps 6824-6826 → RecipeComponents 5861 / 5862 / 5863: COMPONENT_TYPE 1, COMPONENT_ID 1579
  (Lumen), COUNT 130 / 260 / 780.
- CraftingStationRecipeMappings 6018-6020: station 55 (CraftingStations 55 "landmark_v2: crafting station: ether shard
  shop", name 573395).
- The Lumen Station heading "Reward Chests" is text 577610 = item category 292 (P6411 "a new category in the Lumen
  Station called 'Reward Chests'"). CraftingCategoryGroupMapDefinitions has no 292 row.

### 2.3 Props

- **PropDefs (0.695) rows 3003-3008** [P]: `3003^0^577596^0^0^0^1^1^0^0^0^` … `3008^0^577606^…`. That is TRIVIAL 0,
  PLACE_MODE 1, DRAW_IN_PLAYER_TEMPLATES 1, **PROP_RECIPE_ID 0** (not in any palette; item-placed only),
  ALLOW_USER_RENAME 0. These rows are not in PropCategoryPropMap.
- **PropBase dump** (Resources/PacketDumps/145-PropBase, parsed with the Props.LoadDefinitions layout) [P]:

| prop | model file | model id | internal name |
|---|---|---|---|
| 3003 | Theme_Elf_Furniture_Chests_Ornate01.adr | 574 | landmark_v2: site reward chest: level 1: common |
| 3004-3006 | same | 574 | [OLD] … level 2: stone / level 3: metal / level 4: gem |
| 3007 | Theme_FoundersPack_Chests_Founder01.adr | 724 | … level 5: superb |
| 3008 | same | 724 | … level 6: legendary |

  Both models predate 0.589, so 0.589 clients can draw the chests. 0.589 has no PropDefs row or name text for them [I].
- **Not storage chests.** Chests.Slots (Chests.cs:61-80) only counts props with a PROP_RECIPE_ID whose name contains
  "Chest". Reward chests have recipe 0, so they open no inventory window and do not count against the 50-chest site
  cap [P for the server code; I that live did the same — Zaxzia asked in 57902 #50, unanswered].

---

## 3. Settings (settings window, 0.695)

### 3.1 Table rows [P] (gameData695/EditableProperties.txt; loader 0.695 FUN_143dc1440)

| id | key | DISPLAY_NAME_ID | default | max | min | step | BLOCKS_USER_MODIFICATION |
|---|---|---|---|---|---|---|---|
| 104 | `loot_chest_player_cooldown` | 577599 "Player looting cooldown" | 30 | 120 | 15 | 1 | 0 |
| 106 | `loot_chest_remaining_count` | 0 (none) | 0 | 0 | 0 | 5 | **1** |

- The loader stores BLOCKS_USER_MODIFICATION at row +0x64 and IF_NODE_CAPABLE at +0x65.
- Row 106 is not IF-capable.
- Unit is minutes (P6413 "time shown is in minutes").
- History [P, forum]: the August build allowed 1 minute to 1 hour (57918 #6: "maximum lockout of 1 hour",
  "minimum of 5 minutes, not 1 minute"). The 0.695 table, 15-120 with default 30, is the final rule.

### 3.2 How the 0.695 window draws them [P, new trace]

- The EditSettingsReply record is read by 0.695 FUN_142c15ee0 (layout already in storytools-695 §1.4):
  - string label @+0, u32 label text id @+0x18, string key @+0x20;
  - **f64** value @+0x38, default @+0x40, max @+0x48, min @+0x50, step @+0x58;
  - u32 help text id @+0x60.
- The window builder is 0.695 **FUN_142fb71f0** (0.589 FUN_142f18330). For each record:
  - max == 1, min == 0, step == 1 → checkbox (FUN_1435be1e0);
  - **max == 0 and min == 0 → `LuaPropertyEditor::PropertyTextLine`** (FUN_1435becc0 → FUN_1435bacf0 →
    FUN_1435ba6b0 → ctor FUN_1435bc890, property type 7; this class is new in 0.695, rtti_new_695.txt:114);
  - otherwise → slider (FUN_1435be680) with value / min / max / step and the label text id.
- What a TextLine shows: the UI row export FUN_1435c06f0 / FUN_1435c1b60 sets OptionType "Text" and
  **Value = the record's label string**. The row's **Name = the record's key string**: the TextLine ctor passes the
  key as the property name, and FUN_1435c1b60 exports property +0x28 as "Name".
- In InstancePropertyEditor (as3_695 RulesetObjectSettingListItemRenderer.as:199-214 and OptionsListView.as:62),
  "Text" rows show `valueText = Value`, and the list label field is "Name".
- So a read-only text row needs no table text. The server writes the visible caption and value itself.
  [I] Live sent row 106 as max 0 / min 0 with a caption key and the remaining count as text. The exact wording is
  unknown (§6 U3). P6414 calls it "the 'count remaining'".
- BLOCKS_USER_MODIFICATION is not read by the window builder (FUN_142fb71f0 has no +0x64 access). Read-only-ness in
  the client comes from the TextLine type. The server must still refuse 0xFD changes to row 106 (storytools-695 §6.9).
- Row 104 (min 15, max 120) → slider labelled 577599.
- 0.589 clients: no rows 104 / 106 and no TextLine. Reward chests are a 0.695 feature (build gate, §5.9).

### 3.3 Server today

- PropSettings.cs writes **f32** records (WriteRecord, PropSettings.cs:224-235) and loads EditableProperties from the
  0.589 gameData, which lacks rows 104 / 106. So the reward chest window **depends on TASKS 2.18**: f64 records for
  0.695 sessions, the 0.695 table, and read-only rows.

---

## 4. Client behaviour (what the client does and expects)

1. **Crafting**: the generic recipe path at station 55. Nothing chest-specific [P: tables].
2. **Placing**: right-click the item → Inventory ActivateItem → placement session (PropBase sub 2) → Save. This is the
   same as every prop item (P6412 "right-click to place it on your build site").
   - The server path exists: InventoryHandler.cs:153-168 HandleActivate → Props.BeginPlacement(session, propId,
     itemDefinitionId, itemGuid) (Props.cs:348).
   - The item is consumed on the first Save (Props.cs:806).
   - The prop id comes from ItemDefinition.PropId (4510 → 3003), and the model from the PropBase dump [P server code].
3. **Clicking**: the client sends Command 0x1E sub 9 InteractRequest {u64 guid} for an entity the server marked
   interactable (InteractionInfoReply). It is the same flow as treasure chests / Lost Chests
   (InteractionHandler.cs:38-85; Props.SendInteractionInfo Props.cs:1377-1410).
   - No reward-chest-specific client code was found. Searched the 0.695 decompile for "PlayerLoot", "LootChest",
     "loot_chest", the reward text ids 577597-577651 as decimal and hex, and prop ids 0xbbb-0xbc0. The only hit was
     an unrelated low-address switch in part_0028.
   - So looting is entirely server-driven [P absence; I design].
4. **Messages**. CodeStringMappings 0.695 rows [P]:
   - `PlayerLoot.OwnerCantLoot` 577633 "Sorry! Owners cannot loot their own chests!"
   - `PlayerLoot.ChestLocked` 577651 "This chest is locked!"
   - `ClaimPlayerLoot.ChestsYouCantLoot` 577634 "There are chests on this build site that you have a lockout timer
     for!"
   - **None of these is referenced by 0.695 client code** (no literal in decomp_all_695), so they are server-sent.
   - The client route that takes a CodeStringMappings key from the server is PacketShowSystemMessage 0x69 {u32 text id,
     string key, params…}, reader FUN_142b8b220, used by SystemMessages.ShowKey (SystemMessages.cs:22) [P route;
     I that live used it for these keys].
5. **Pick-up refusal** [P]: 0.695 PropBase handler **FUN_142fba9f0** case 6 (PropPickUpReply).
   - If success is false, the u32 error picks the message: 3 inventory full, 6 PropLocInvalid, 7 no-trade, 9 vendor
     not empty, 10 no permission, 0xB soulbound, 0xD not in edit mode, **0xE →
     `Prop_PropPickupErrorPartialLootChest`** (577600 "You cannot pickup a loot chest that has already given out
     items!").
   - The server writes this reply in Props.PickUp (Props.cs:1148-1176: u8 6, u64 player, bool ok, u32 error, u64 prop).
   - Delete (case 8, PropDeleteReply) has no matching error string.
6. **Links**:
   - The client has no prop → Link table. The server answers ObjectScripting 0x0D-0x0F with each prop's events and
     actions (storytools-695 §1.1).
   - The 0.695 sentences for these props are event 577597 "When this chest is looted..." and action 577598 "...Loot
     this chest" (storytools-695 §4.15).
   - Players locked reward chests and drove them from Links (57918 #7: "Locked chest in open areas connected to a
     couple of logic props so they deliver loot as player completes encounters … Unlocked click to loot reward chests
     located in secured areas").
   - Together with `PlayerLoot.ChestLocked` this shows that reward chests also had the chest Lock / Unlock Links
     (573142 / 573143, events 573144 / 573145) [I, strong].
   - One player reported on 27 Oct that the Link loot did not work (57918 #20), while the 29 Sep dev reply expected it
     to (P6517). The emulator should make it work.
7. **Loot display**: loot reaches the player through the loot toast. Inventory 0x2D sub 0x1A LootNotification
   (FUN_142e78620 → DisplayLootNotification FUN_142e7f050) is already sent by LootPinatas.Notify (LootPinatas.cs:137).
   57918 #12 calls these "loot messages" [P that players saw loot messages; I that live used bags vs direct adds — U5].
8. **Mail**: the client has the texts 577638 "Reward Chest Expiration!" (subject) and 577639 "Greetings! We're writing
   to let you know that a Reward Chest on your build site has been fully consumed. Thanks for giving back to the
   community!" (body).
   - Mail kinds 0x2000 / 0x4000 / 0x8000 mean the sender / subject / body are text ids (FUN_143585ac0;
     social-service.md §3.1).
   - Text **577637 "Terrogaunt"** sits right before the subject: the developer who presented the feature on the
     livestream (P6458) [I: the mail's sender].
   - The token item is 577640 "Thank You Token" / 577641 "Thanks for being awesome!".
9. **Goal** [P, server dump 71-AchievementBase decoded with re/tools/decode_achievements.py]:
   - Achievement **1490** "No Good Deed..." (577643 / 577644), one objective **2209** "Thank You Tokens received from
     emptied Reward Chests" (577642), reward item 1579 Lumen ×15.
   - "To Serve Luminaries" (577645 / 577646) and "They Like You…" (577647 / 577648) with title 4519 "Champion of the
     People" (grant 60) are in the text table but not in the dump. They are later tiers (L24).

---

## 5. Server design

### 5.1 New module `Game/RewardChests.cs` (one place for the rules)

- Constants:
  - props 3003-3008;
  - uses per prop: 3003 → 10, 3007 → 20, 3008 → 40, old 3004-3006 → 10 [I: their dump description is 577607, "10 uses"];
  - setting keys `loot_chest_player_cooldown` and `loot_chest_remaining_count`;
  - texts 577597 / 577598 / 577633 / 577634 / 577651 / 577637-577641;
  - error 0xE.
- `IsRewardChest(ulong guid)`: Props.Find(guid) with PropId in 3003-3008.
- `Placed(PlacedProp)`: called from Props.Save on the first successful placement (Props.cs:~815, beside
  Achievements.PropPlaced). It sets `loot_chest_remaining_count` = uses through PropSettings.Set, and only when no saved
  value exists, so moves and re-saves keep the count.
- `Remaining(guid)` = PropSettings.Get(...) and `Cooldown(guid)` = PropSettings.Get(guid, cooldown key), clamped
  15-120.

### 5.2 Looting (click and Link)

`Loot(WorldSession trigger, PlacedProp chest, bool fromLink)`:

1. Find the site with Claims.ClaimAt(chest.Zone, pos) (Claims.cs:1330) and check Claims.IsPlaying(site)
   (Claims.cs:1534). Not playing → nothing; a click in Edit Mode gets no loot (P6416) [U4: which message, if any].
2. Locked (StoryTools state, §5.5) and not fromLink → SystemMessages.ShowKey(trigger, "PlayerLoot.ChestLocked"), stop.
3. Recipients:
   - the trigger, plus the trigger's group members (Groups.MembersOf / NearbyMembers, Groups.cs:82-91) — P6503 "give
     loot to the whole group rather than needing to be individually clicked by each person";
   - range: same landscape and within the site, or 50 m like treasure chests (LootChests ShareRange) [U2: live range];
   - remove the chest owner: if the trigger is the owner → ShowKey(trigger, "PlayerLoot.OwnerCantLoot"), and owners in
     the group are skipped silently [P: P6416 "the owner of a chest can never loot it"; I: silent for group members];
   - remove dead players;
   - remove players under lockout. If the trigger is locked out →
     ShowKey(trigger, "ClaimPlayerLoot.ChestsYouCantLoot") [I: the only "lockout" text; U1: when live showed it].
4. Charges:
   - Take one charge per recipient, stopping at Remaining; recipients past the remaining charges get nothing
     [I: P6414 "the number of remaining times this can be looted"; per-group charging is U2].
   - Make it one constant so it can be switched to one charge per loot event.
5. For each recipient:
   - roll the tier's loot (§5.3);
   - give it as LootPinatas.SpawnBag(p, chestPos + 0.6 up, loot) (LootPinatas.cs:157), the same as LootChests.cs:168
     and Outposts.OpenChest; Notify runs on pickup;
   - set the lockout (chest, character) = now + Cooldown minutes.
6. Raise "When this chest is looted..." (577597) once per loot event, with Context.Player = trigger (StoryTools, §5.5).
7. Optional open effect / animation: none is known for models 574 / 724 [U6]. Don't invent one.
8. When Remaining hits 0 → `Consume(chest)` (§5.4).

Entry points:

- InteractionHandler.cs:38-85: add `else if (Game.RewardChests.IsRewardChest(guid)) Game.RewardChests.Click(session,
  guid);` **before** the Props.MayUse "No Touching" refusal. Looting is the visitor's purpose and the chest only works
  in Play Mode [I; live access rule U7]. It must also come before StoryTools.Click.
- Props.SendInteractionInfo (Props.cs:1382): add `RewardChests.IsRewardChest(guid)` to `usable`, so visitors get the
  highlight and interact cursor (Cursors 10). Optionally only while the site is in Play Mode [I].
- StoryTools action 577598 (§5.5) calls `Loot(ctx.Player, chest, fromLink: true)`. If no player is in the context,
  nothing happens [I].

### 5.3 Loot tables

**Common** [P: measured by a player over 100 loots, 57918 #19]. Each loot gives all of:
- Tree Fiber 164 ± 23;
- Tree Sap 124 ± 32;
- one wood ≈197 ± 28 (Striped 30% / Burled 33% / Plain 37%);
- Stone 812 ± 116;
- one of Sand 28% / Dirt 32% / Ice 40%, ≈1650 ± 220;
- one uncommon: Copper Ore 23% 784, Tin Ore 13% 787, Iron Ore 10% 818, Amaranthine 17% 783, Marble 14% 795,
  Agate 14% 585, Sapphire 9% 577 (± ~100).

The same player got 2 recipes, 17 essences and 45 Lumens over the 100 loots and judged those general loot chances.
Item ids come from the server's resource ids (the LootChests / ItemUses tier lists: 34 Copper, 35 Tin, 36 Iron,
47 Marble, 48 Amaranthine, 51 Agate, 57 Sapphire, 2000-2002 woods, …). Map Tree Fiber / Tree Sap / Stone / Sand /
Dirt / Ice by name from the dump.

**Superb / Legendary**: no measurements exist [U8]. Build them from the descriptions [P text, I amounts]:
- Superb (577632): "an abundance of exceptional resources, such as rare stone, metal, and gems in addition to common
  resources". That is the Common roll plus rare stone / metal / gem rolls (ItemUses tiers 3-4: Silver, Obsidian,
  Emerald, Ruby, Gold, Diamond).
- Legendary (577635): "outpost loot, such as lumens, essence, and the occasional recipe in addition to a ton of common
  and exceptional resources". That is the Superb roll plus Outposts.OpenChest-style extras: Lumens, Adventure
  Essence, RecipeBook.RandomUnknownRecipeItem, chance 0.3.
- Seasonal extras on Superb / Legendary only [P]:
  - Halloween hats (P6505: "clicking on the higher level player reward chests … chance of unlocking two new hats";
    unlocker items 4521-4523, not in the dump, L24);
  - Colonist's Flight Suits from 6 Dec 2016 (P6569 "from outposts and higher-end reward chests").
  - Gate both by the server's holiday calendar, and add them only once the item definitions exist.
- Old tiers 3004-3006 (if owned): stone / metal / gem uncommon rolls per their draft descriptions 577612 / 577613 /
  577616 [I].
- Keep the tables in one data file (JSON or a C# table) so they can be retuned. Patch notes say prices and charges were
  tuned during the trial (P6445).

### 5.4 Consumption and the owner's mail

`Consume(chest)`:

1. Remove it the way Props.PickUp does with no refund: Props.Remove(prop, null) (Props.cs:1283). This already
   despawns the chest for the zone, clears its Links (StoryTools.PropRemoved) and settings (PropSettings.PropRemoved),
   and deletes the world_prop row. Also delete its lockout rows (§5.6).
2. Mail the owner (prop.OwnerGuid) through Social.SocialServices.Service.SendSystemMail (Mail.cs:627; the pattern is
   SiteUpkeep.cs:70-80):
   - sender text 577637 ("Terrogaunt") [I];
   - subject 577638, body 577639 [P texts]. Resolve them server-side with a fallback string, like SiteUpkeep's
     Text(id, fallback), or send a text-id mail kind (0x2000 | 0x4000 | 0x8000) for 0.695 recipients once that kind
     is built;
   - attachment: Thank You Token ×1 [I: P6502 "send you a reward via in-game mail"; texts 577640-577642] plus the
     Common-roll "bonus resources" of P6415 [U9].
   - Token item id / definition: L24. Until it exists, send the resources only, and say so in a dev log.
3. Goal 1490 / objective 2209 is credited with one "Thank You Token received":
   - when the owner takes the token from the mail, or at once if the objective counts mails [U10];
   - use Achievements.Progress(session, 2209) directly. ProgressByText matches 0.589 text, which lacks 577642;
   - for an offline owner, credit at next login (or from the mail-attachment take).

### 5.5 StoryTools (Links)

- Add a `kindsById` entry for 3003-3008 (StoryTools.cs:170). KindOf matches names with the word "Chest"
  (StoryTools.cs:223-243). Once 0.695 names load, "… Reward Chest" would get the storage-chest kind (Open / Lock)
  and never the loot sentences.
  - `Name = "reward chest"`, `Events = { 577597, 573144, 573145 }`, `Actions = { 577598, 573142, 573143 }`,
    `LockEvents = { 573144, 573145 }`, `LockActions = { 573142, 573143 }`.
  - Lock / Unlock as I-strong (§4.6).
- Execute (StoryTools.cs:455): `case 577598: RewardChests.Loot(ctx.Player, placed, fromLink: true)`.
  - Links run only for sites in Play Mode (StoryTools.Start / sites).
  - Expose `IsLocked(guid)` for the click path. A locked chest refuses clicks (577651), but "...Loot this chest" from a
    Link still works: WhiskyJack's locked chests "deliver loot as player completes encounters" [I].
- `FireLooted(chest, player)` raises 577597 with `new Context(player, 0)`.
- Build-gate the two new sentences to 0.695 sessions (TASKS 2.17).

### 5.6 Persistence

- Remaining count: world_prop_setting key `loot_chest_remaining_count` (existing table, PropSettings).
- Cooldown: world_prop_setting key `loot_chest_player_cooldown` (existing).
- **New table `world_loot_lockout` (prop_guid u64, character_guid u64, until UTC)**, PK (prop_guid, character_guid).
  - Load at start, drop expired rows, write in a 1 s batch like PropSettings.Flush.
  - Survives restarts [I: live restarts were nightly, and a lockout reset would be an exploit].
- "Has given out items": Remaining < uses-for-tier, so no extra column is needed.

### 5.7 Pick-up, delete, move, stow

- Props.PickUp (Props.cs:1148-1176): if IsRewardChest and Remaining < initial uses → refuse with error **0xE (14)**.
  An untouched chest picks up normally and refunds item 4510 / 4514 / 4515 (Remove refunds PaidByItem).
- Delete: allowed for the owner (no client error exists for delete) [I].
- Move (PropAccess / gizmo): unchanged, edit mode only (error 13 in Play Mode, as now).
- Site stowing (StowInBox) keeps the prop guid, so settings and lockouts stay with it [P code].
- Designs / templates: a reward chest in a design is paid with its item (Props.cs:148-169) and starts fresh at full
  uses. Copied prop state is not carried over [I].

### 5.8 Lockout notice on arrival (optional)

ClaimPlayerLoot.ChestsYouCantLoot ("There are chests on this build site that you have a lockout timer for!") reads
like a site-level notice. It may have been shown when a locked-out player entered a site, or when they tried to loot.
Do the click case first (§5.2). The on-arrival case needs a site-entry hook, which does not exist yet (U1).

### 5.9 Build gating and data

- 0.695 sessions get:
  - the 0.695 PropDefs / text (names 577596…);
  - EditableProperties rows 104 / 106 with f64 records (TASKS 2.18);
  - recipes 5362-5364 at station 55 (0.695 Recipes / RecipeComponents / CraftingStationRecipeMappings — items-695 §1
    point 5);
  - the new Link sentences.
- 0.589 sessions: the item definitions are in the shared login dump. Do not offer the recipes. Chests placed by 0.695
  players render with models 574 / 724 [I]. Looting from a 0.589 client can follow the same server rules (no
  0.695-only packet is used), but the settings window must not send rows 104 / 106 as f32 records the 0.589 table
  doesn't know. Simplest: no settings window for reward chests on 0.589.

### 5.10 Bandwidth

- Per loot: one bag per recipient (LootPinata SpawnLoot), one effect at most, plus the 0xE / system-message packets
  only to the person concerned.
- No per-tick traffic. Lockouts are server-only state.
- On consumption, one PlayerUpdateRemove to the zone viewers (existing SendRemove).

### 5.11 Tests (by the client's reaction)

1. Craft each tier at the Lumen Station: 130 / 260 / 780 Lumens are taken and the item arrives.
2. Place it on your site. The settings show "Player looting cooldown" (15-120 slider) and a read-only count row.
3. Owner click in Play Mode → "Sorry! Owners cannot loot their own chests!".
4. Second character click (Play Mode) → a loot bag and toasts, and the count drops. A second click within the cooldown
   → lockout text, no loot.
5. Group of two (non-owners): one click → both get bags, and both are locked out.
6. Owner tries to pick it up after one loot → "You cannot pickup a loot chest that has already given out items!".
7. Exhaust it → the prop disappears for all viewers. The owner's inbox gets "Reward Chest Expiration!" with the
   attachments.
8. Link: a creature spawner "When … dies" → "...Loot this chest": the killer gets loot. Lock the chest → clicks show
   "This chest is locked!", the Link still loots.

---

## 6. Unknowns / leads (to add to OPEN-LEADS.md)

| # | Unknown | Searched | Try next |
|---|---|---|---|
| U1 | When `ClaimPlayerLoot.ChestsYouCantLoot` (577634) was shown: on site entry, on click, or both | 0.695 decompile (no key literal, no text id), as3_695, CodeStringMappings | Forum / livestream (YouTube replay linked in P6458), WhiskyJack's thread 57930 (not in design/forum/all — scrape it) |
| U2 | Group loot details: range, whether a group loot used 1 charge or 1 per member, whether every member is locked out | P6503, 57987 / 57990 threads, 57918 | Forum after 29 Sep (thread 57987 pages 2+, Dec 2016 threads), wiki |
| U3 | Exact caption / value text of the count-remaining row (row 106 has no DISPLAY_NAME_ID) | EditableProperties, text_695 ("count remaining" only in P6414), window builder FUN_142fb71f0 | The patch-note screenshot in 57902 (image not archived here); archive.org copy of the forum image |
| U4 | What a click in Edit Mode showed (a message, or no interaction at all) | CodeStringMappings (only Claim.NoCameraPlayMode), decompile | Forum; runtime |
| U5 | Loot delivery: LootPinata bags vs direct add + toast | 57918 #12 ("loot messages"), LootPinata packet ids | Forum videos (57930), loot of Lost Chests for comparison |
| U6 | An open animation / effect for the chest models 574 / 724 | PropBase dump (no effect name), PropAnimations | ArtSourceActors / animation sets for Theme_Elf_Furniture_Chests_Ornate01 and Founder01; InteractableCompositeEffectSets |
| U7 | Whether "No Touching" site access blocked looting | P6416 (Play Mode is the only stated rule) | Forum |
| U8 | Superb / Legendary loot amounts | descriptions 577632 / 577635, P6501, 57987 | Forum posts after 29 Sep, Reddit / YouTube archives |
| U9 | The "bonus resources" of the August mail (P6415) and whether they stayed alongside the September reward | 57918 #12 (no attachment seen in Aug) | Forum after 29 Sep |
| U10 | Thank You Token item id / definition / stack, and how objective 2209 counts (mail sent vs token taken / owned) | dump (no item named 577640), L24, goal 1490 objective 2209 | 0.695 item tooltips at runtime (ItemDefinitionRequest for 4516-4518 on a live-like dump), the missing goal tiers |
| U11 | Whether Lock / Unlock Links existed on reward chests (inferred from 577651 + 57918 #7) | link_texts_695.tsv (no reward-specific lock sentence) | Forum videos |
| U12 | Mail sender (577637 "Terrogaunt" inferred) and whether live used text-id mail kinds for it | social-service.md mail kinds, texts | Runtime only (no capture exists) |

## 7. Files

| What | Where |
|---|---|
| Item / prop / recipe data | gameData695/PropDefs.txt:1979-1984, EditableProperties.txt rows 104 / 106, Recipes.txt:2362-2364, RecipeComponents.txt:2725-2727, RecipeRecipeComponentMaps.txt:3674-3676, CraftingStationRecipeMappings.txt:1400-1402, CodeStringMappings.txt:120 / 472 / 473 / 507 |
| Client functions | FUN_142fba9f0 (PropBase handler; case 6 error 0xE), FUN_142c15ee0 (f64 setting record), FUN_142fb71f0 (window builder), FUN_1435becc0 / FUN_1435ba6b0 / FUN_1435bc890 (PropertyTextLine), FUN_1435c06f0 / FUN_1435c1b60 (row export), FUN_143dc1440 (EditableProperties loader) |
| Server to change | new Game/RewardChests.cs; Network/Handlers/InteractionHandler.cs:38-85; Game/Props.cs (SendInteractionInfo :1377, Save :725-818, PickUp :1148); Game/StoryTools.cs (:170 kindsById, :455 Execute); Game/PropSettings.cs (:79 settingsByKind, f64 → 2.18); Database: world_loot_lockout |
| Server to reuse | LootPinatas.SpawnBag / Notify, Groups.MembersOf / NearbyMembers, Claims.ClaimAt / IsPlaying, Mail.SendSystemMail via SocialServices, SystemMessages.ShowKey, Achievements.Progress, PropSettings.Get / Set |
