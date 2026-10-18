# 0.695 items: what is new / changed, how definitions reach the client, what the server must do (traced 2026-10-15)

Agent #7 (trace/0695/AGENTS.md). Sources: the two installs (read-only), `re/tools/tablediff.py`, the decompiles
(`re/decomp_all` 0.589, `re/decomp_all_695` 0.695), `trace/0695/functions.tsv` (status = decompiled text, code =
machine code compared by re/tools/codecompare.py), re/out/text_589.tsv / text_695.tsv, trace/0695/text-new.tsv,
re/out/fileindex_589/695.tsv, the server (LandmarkEmu/) and its packet dumps
(LandmarkEmu/LandmarkEmulator.WorldServer/Resources/PacketDumps, decoded copy gameData/items.json via re/items_decode.py).
Scratch scripts and raw outputs: `re/out/agent_items/` (items695.py → items695.txt, dump_names.txt, mdtables.py →
table63.md, modelcheck.py, diff_<Table>.txt, reader_589.c / reader_695.c, inv_disp_695.c, cu_695.c).
Addresses are 0.695 unless marked 0.589.

## Summary

1. **The item-definition protocol did not change.** The record reader (0.695 FUN_142c176d0 = 0.589 FUN_142b7df20),
   its LZ4 wrapper (FUN_142c19850 = 0.589 FUN_142b800a0), every helper it calls, the ItemDefinitions (0x4B) and
   ItemDefinitionReply (0x4A) readers, the login-list handler, the request sender (0x49) and the cache add are
   machine-code identical (§2.1). Opcodes are unchanged (CommandBase 0x1E sub 0x49 / 0x4A / 0x4B). The server's
   definitions work for 0.695 byte for byte.
2. **The live item dump the server sends is not a 0.589-era dump: it is a 0.695-era one** (correcting TASKS §4's
   premise). 63 of its 2,885 items have name texts that exist only in the 0.695 text table: Roll Wraps, the Combat
   Node / Scoreboard / IF Gate / new Respawn Point, 7 fireworks packs, 22 fireworks emitters + their 22 recipe items,
   6 reward chests and "Title: Champion of the People" (§1.1). The companion dumps are 0.695-era too: PropBase
   (145-PropBase, props up to 3013 with 0.695-only model ids 1727-1745) and the item categories
   (39-ReferenceDataItemCategoryDefinitions: has 289 Parlor Option, 290 Fireworks, 291 Pet, 292 Reward Chest). The
   capture postdates 28 Sep 2016 (it holds the Superb / Legendary chests of that update, P6501) and lacks the
   6 Dec 2016 items (flight suits, P6566).
3. **The login list is a subset.** Items the 0.695 client itself names by id are missing from it: 4419 (Fantasy
   Parlor Pack unlocker), 4521 / 4522 / 4523 (Cowboy Hat / Ninja Mask / Samurai Helmet unlockers). So are every pet
   figurine, the 22 fireworks consumables, the Thank You Token, the post-September titles and all Marketplace /
   event cosmetics (even 0.589-era ones such as the Skeletal Bodysuits, Holiday Sweaters, Year Zero outfits). Category
   291 "Pet" exists in the category dump with **no** item in the list. The client fetches definitions it lacks with
   0x49, and also accepts a definition embedded in ClientUpdate ItemAdd and in tooltip replies (§2.2). Our server
   answers 0x49 only from the dump and stays silent otherwise (ItemDefinitionManager.cs:184-189), so these items
   cannot be given to anyone yet.
4. **0.695 table changes that touch items** (§1.2-1.4): 33 new recipes (22 emitter-recipe items, 7 fireworks packs,
   3 reward chests at the Lumen Station 55; Roll Wraps with no station), 13 changed (Lumicite 500 → 750 per craft,
   Build Site Flag no longer known by default), 1 removed; 82 new props (the item-placed ones are above; 27 Ebonguard,
   10 ruin creatures / hazards, game props); parlor unlocks by item id (CharacterCreateOptions.UNLOCKING_ITEM_ID,
   new ArtTintGroupUnlock tables); 113 new icons including the pets, hats, parlor options and fireworks
   consumables; 7 pet models (Models 1729-1735).
5. **The server cannot use the 0.695 items yet**: it reads 0.589 Recipes / RecipeComponents / RecipeRecipeComponentMaps
   / CraftingStationRecipeMappings / PropDefs and the 0.589 text file (gameData = byte-identical copies of the 0.589
   install), so the 63 items above have no names on the server and none of the new recipes exist; nothing handles
   fireworks, pets, reward chests, unlockers or Roll Wraps (no match for any of them in WorldServer). Gaps 4.7-4.22.
6. **0.589 clients**: the same login list goes to them. Every model it names exists in both builds, but 57 of the
   items use icons that exist only in 0.695 (ImageSets 21666-21778) and the 63 have no 0.589 name text; the props
   2940, 2949-2970, 2976, 3000, 3003-3008 the new items place are not in 0.589 PropDefs. Harmless while nobody owns
   them; gate them by build when they become obtainable (gap 4.21). No id the server sends was removed or renumbered.

## 1. Items new or changed in 0.695

### 1.1 New items already in the server's dump (definitions in hand)

Name = the item's NAME_ID in the dump, found only in 0.695 text (re/out/agent_items/dump_names.txt). "places prop" =
definition u35_39[3] (ItemDefinition.PropId); "grant" = the record's grantId field; recipe = 0.695 Recipes row
whose ITEM_DEFINITION_ID is the item (@st55 = CraftingStationRecipeMappings to station 55 "Lumen Station", text
573395). Category ids resolved from the category dump (25 Boots, 176 Prop - Special Effects, 229 Title, 288 Special
Effects, 290 Fireworks, 292 Reward Chest). Flags byte 0 bit 3 (8) = stackable, bit 7 (128) = NO_TRADE; byte 1 bit 4
(16) = see trace/systems/inventory.md.

| id | name (text id) | cat | icon (ImageSets) | prop / grant / ability | 0.695 recipe | flags, stack |
|---|---|---|---|---|---|---|
| 4406 | Roll Wraps (577205) | 25 | 2202 wearable_HeroicMovement_Boot_MessengersSandal | u59 [90] | 5331 | [0, 0, 0] 1 |
| 4408 | Combat Node (577196) | 288 | 21666 props_Theme_Widgets_RuleSetObjects01_CombatNode01 | places prop 2940 | - | [8, 0, 0] 100 |
| 4443 | Pack of Unstable Fireworks (577410) | 290 | 21687 marketplace_chest_fireworks_unstable | ability 1237 type 4 | 5332 @st55 | [8, 0, 0] 100 |
| 4444 | Pack of Fireworks (577413) | 290 | 21681 marketplace_chest_fireworks01 | grant 1124 | 5333 @st55 | [8, 0, 0] 100 |
| 4445 | Pack of Fancy Fireworks (577417) | 290 | 21682 marketplace_chest_fireworks02 | grant 1125 | 5334 @st55 | [8, 0, 0] 100 |
| 4446 | Pack of Streamers (577419) | 290 | 21683 marketplace_chest_fireworks_curved | grant 1126 | 5335 @st55 | [8, 0, 0] 100 |
| 4447 | Pack of Triple Fan Fireworks (577421) | 290 | 21685 marketplace_chest_fireworks_triplefan | grant 1127 | 5337 @st55 | [8, 0, 0] 100 |
| 4448 | Pack of Twinkle Fireworks (577423) | 290 | 21686 marketplace_chest_fireworks_twinkle | grant 1128 | 5336 @st55 | [8, 0, 0] 100 |
| 4449 | Pack of Whirly Fireworks (577425) | 290 | 21688 marketplace_chest_fireworks_whirly | grant 1129 | 5338 @st55 | [8, 0, 0] 100 |
| 4455-4476 | Fireworks Emitter (Gold … Purple Whirly) (577439-577460) | 176 | 21712-21733 Emitter_Fireworks_* | places prop 2949-2970 (in order) | - (random drops, P5915) | [8, 0, 0] 20 |
| 4477-4498 | Recipe: Fireworks Emitter (…) (577462-577504, even ids) | 176 | same icon as its emitter | grant 2949-2970 = the emitter prop | 5339-5360 @st55 | [8, 16, 0] 1 |
| 4508 | Scoreboard (577536) | 288 | 21676 props_Theme_Widgets_DMWidgets_CountingNode01 | places prop 3000 | - | [8, 0, 0] 100 |
| 4509 | Respawn Point (577587) | 288 | 21772 props_Theme_Widgets_RuleSetObjects01_RespawnMarker02 | places prop 2701 | - | [136, 0, 0] 100 |
| 4510 | Common Reward Chest (577596) | 292 | 1334 | places prop 3003 | 5362 @st55 | [8, 0, 0] 1 |
| 4511 | Stonedelver's Reward Chest (577602) | 292 | 1334 | places prop 3004 ("[OLD] … level 2: stone") | - | [8, 0, 0] 1 |
| 4512 | Orefinder's Reward Chest (577603) | 292 | 1334 | places prop 3005 ("[OLD] … level 3: metal") | - | [8, 0, 0] 1 |
| 4513 | Gemseeker's Reward Chest (577604) | 292 | 1334 | places prop 3006 ("[OLD] … level 4: gem") | - | [8, 0, 0] 1 |
| 4514 | Superb Reward Chest (577605) | 292 | 1334 | places prop 3007 | 5363 @st55 | [8, 0, 0] 1 |
| 4515 | Legendary Reward Chest (577606) | 292 | 1334 | places prop 3008 | 5364 @st55 | [8, 0, 0] 1 |
| 4519 | Title: Champion of the People (577650) | 229 | 1759 | grant 60 (title id) | - | [136, 16, 0] 1 |
| 4524 | IF Gate (577524) | 288 | 21778 props_Theme_Widgets_DMWidgets_IfNode | places prop 2976 | - | [8, 0, 0] 100 |

Full per-row list (all 63 rows, icon names): re/out/agent_items/table63.md.

Where they came from: fireworks packs, emitters and emitter recipes — 29 Jun 2016 (P5913-P5915: consumables crafted
at the Lumen Station "Fireworks" section, emitters from random drops, emitter recipes in the Lumen Station's "Props -
Special Effects" section); Combat Node 27 Jul 2016 (P6149); Scoreboard 27 Jul 2016 (P6182); IF Gate, Respawn Point,
Superb / Legendary chests 28 Sep 2016 (P6494, P6501); Common Reward Chest 29 Aug 2016 (P6411). The prop internal names
(145-PropBase) mark 3004-3006 as "[OLD]": the three middle tiers were retired; 0.695 has recipes only for Common,
Superb and Legendary. Roll Wraps, the Respawn Point item 4509 and the Champion title: no patch note found yet
(searched patchnotes.tsv for "roll", "respawn", "champion").

The emitter-recipe items' grantId is the PropDefs id they teach. Checked over the whole dump: of 584 "Recipe:" items
with a grant, 526 grant a prop (PropDefs id; 496 of them have exactly the taught prop's name), 58 grant a recipe id
(Recipes row making the same-named item) — re/out/agent_items (inline check, 2026-10-15). The server links recipe
items by name instead (RecipeBook.cs:37-75) using 0.589 text, which cannot name these 22.

### 1.2 New items the 0.695 client names by id, absent from the dump (definitions to build)

| id | what (proof) | icon | needs |
|---|---|---|---|
| 4419 | Fantasy Parlor Pack unlocker: UNLOCKING_ITEM_ID of every "[Unlock] Fantasy …" CharacterCreateOptions row (24 rows: Long Ear / Short Ear / Horns / Tusks heads 2xx, Black Cat / White Cat eyes 329-336) and of all 6 ArtTintGroupUnlock rows (fantasy skintones black / blue / green / red / white / yellow → 45 ArtTintGroupUnlockTint rows). Name text by elimination: 577355 "Fantasy Parlor Pack", description 577356 / 577361 (released 29 Jun 2016, P5910-P5912, Marketplace) | not identified (21667-21680 are the option icons) | definition; use = unlock all its options; refusal 577416 / 577364 |
| 4521 | Cowboy Hat unlocker: CharacterCreateOptions 295 "[Unlock] Hat - Cowboy - Human FEMALE Hair 18" | 21775 wearable_Hats_Cowboy | definition; name 577699, description 577700; refusal 577705 |
| 4522 | Ninja Mask unlocker: row 305 "[Unlock] Hat - Ninja …" | 21776 wearable_Hats_Ninja | name 577701 / 577702; refusal 577706 |
| 4523 | Samurai Helmet unlocker: row 301 "[Unlock] Hat - Samurai …" | 21777 wearable_Hats_Samurai | name 577703 / 577704; refusal 577707 |

The hats are the Halloween drops of 28 Sep 2016 (P6505: "an unlocker, like the Crimson Parlor Pack, … can be
traded"; outposts and the higher reward chests). The patch note says two hats; the client has three.

The CharacterCreateOptions header gained DESCRIPTION and UNLOCKING_ITEM_ID (0.695 reader FUN_143c6a010 stores the
item id at option +0x2c; ArtTintGroupUnlock reader FUN_143c6c5b0 at +0xc). All 129 new option rows: 92 default-unlocked
slot-4 rows, 24 rows → 4419 (slots 2 and 5), 3 hat rows → 4521-4523 (slot 3), 10 locked rows with no item (slots 2 / 3).

### 1.3 New items known from 0.695 text / icons / models whose item ids are not found yet

Ids searched: every 0.695 table column named *ITEM* (list in re/out/agent_items/items695.py), the dumps, the 0.695
UI scripts (re/as3_695, no item ids), the wiki dump (design/wiki: names only), StringHashToValue (no new item ids).
Candidates by gaps in the dump's id sequence (ids never in the login list, ordered like their texts) are noted but
**not proven** (OPEN-LEADS L24).

| Item(s) | Texts | Icon (ImageSets) | Model | Source | Candidate ids (unproven) |
|---|---|---|---|---|---|
| Shadow Wolf Figurine → Shadow Wolf Pup | 577507 / 577508 / 577510 | 21740 Pets_Wolf_Pup01 | Models 1732 Char_Wolf_SpiritWolf_Entities_WolfPup_00_Chest.adr | Marketplace (P6144) | 4499-4507 |
| Stag Figurine → Young Stag | 577512 / 577513 / 577514 | 21739 Pets_Deer_Fawn01 | 1733 Char_Deer_Deer_Entities_Fawn_00_Chest.adr | Marketplace | 〃 |
| Cockatrice Figurine → Baby Cockatrice | 577515 (+ second name 577547) / 577516 / 577517 | 21738 Pets_Cockatrice01 | 1729 Char_FlightlessBird_Cockatrice_Body_00.adr | Marketplace | 〃 |
| Bunny Figurine → Bunny | 577518 / 577519 / 577520 | 21735 Pets_Bunny_Bunny01 | 1730 Char_Bunny_Bunny_00_bunny.adr | Marketplace | 〃 |
| Beetle Figurine → Beetle | 577521 / 577522 / 577523 | 21734 Pets_Beetle_Klicnik01 | 1731 Char_Klicnik_Klicnik_000.adr | Marketplace | 〃 |
| Jackalope Figurine → Jackalope | 577532 / 577533 / 577534 | 21736 Pets_Bunny_Jackalope01 | 1734 Char_Bunny_Bunny_00_Jackalope.adr | Marketplace | 〃 |
| Lop Ear Bunny Figurine → Lop Ear Bunny | 577538 / 577539 / 577540 | 21737 Pets_Bunny_Lopear01 | 1735 Char_Bunny_Bunny_00_Lopear.adr | rare Outpost elite drop (P6143) | 〃 |
| 22 fireworks consumables (Gold … Purple Whirly Fireworks) | names / "Use to launch …" 577366-577408, 577438 | 21689-21711 props_Fireworks_* (23 rows, one duplicate Redorange) | effects: composite 12836-12857 (the emitters' InteractableCompositeEffects 278-321 — the consumables' own effect not found yet) | Lumen Station (P5914), packs 4443-4449 | 4410-4442, 4450-4454 |
| Fantasy Parlor sub-options (4 faces, 2 cat eyes, 6 skintones) | 577339-577353, 577362-577363, 577427-577436 | 21667-21674, 21677-21680 | — | Fantasy Parlor Pack | maybe options only (the client maps all of them to 4419) |
| Thank You Token | 577640 / 577641 ("Thank You Tokens received from emptied Reward Chests" 577642 is a goal objective) | not identified | — | mail when a Reward Chest is used up (577638 / 577639, P6502) | 4516-4518 |
| Titles: The Lumerian Lens, Hero of Landmark, Landmark Explorer, Holiday Hero, Creator of Winter Wonderlands, Icy Architect | 577697, 577727-577734 | (titles use 1759) | — | events / giveaways (577743-577744 developer notes) | after 4524 |
| Colonist's Flight Suit (Merry Red / Forest Green / Festive Blue) | 577737-577740 | not identified | existing Colony light-suit model + tint (no new .adr) | Outpost drops from 6 Dec 2016 (P6566, P6569) | after 4524 |
| Holiday Sweater (Green), (Blue) | 577741 / 577742 (577745-577746 notes) | 9648-9650 are the 0.589 sweaters | ArtTints Holiday_Light_00/02/03 | 6 Dec 2016 drops / giveaway (P6567, P6570) | after 4524 |

Pet support rows also new in 0.695: category 291 "Pet" (577509), KeywordDefinitions 86 "Pet" (577509), TagDefinitions
623 "Cosmetic Pets" (577544) and 622 "Parlor" (577359), ProfileDefinitions 379 renamed "Adventuring Gear" (575475) →
"Non-Combat Entity" (577511, between the pet texts: probably the pets' profile, not proven).

Pre-0.695 cosmetics also absent from the dump (not 0.695 changes, same gap): Skeletal Bodysuits (565425, 572805-572809),
Holiday Sweaters (566189-566208), Year Zero Survivor's Outfits / Gift (568788-568793), Tech Sergeant's / Commander's
Gear (560076-564995). And 23 older item ids referenced by recipe components in **both** builds are not in the dump
(63, 64, 65, 67, 77, 167, 169, 239, 434, 558, 585, 588, 589, 756, 757, 947, 949, 1115, 1117, 1718, 1720, 1762, 2605 —
e.g. plants used by the Infused flowers and cloth recipes at station 30; items695.txt).

### 1.4 Changed items and recipes (both builds have them)

- **Renamed** (same item id and text id, text changed in 0.695): 2521 Colonist's Elegant Robe (Red → Russet); Gray →
  Grey on 1260, 1265, 2485, 4034, 4041; Lillies → Lilies on 1854-1858, 3273; Sargeant → Sergeant on 3160, 3564;
  Drowners → Drowner's on 1627, 4012; 4212 Spellweaver's Raiments → Raiment; 3078 Adventure Game Table → Game Table;
  3132 Dice Roll Filter → Probability Filter; 3770 → Random Link Filter; 3772 → Server Time Node; 4409 Counter Node →
  Counting Node; 3795 / 3796 and 3880 / 3884 Ice Sculptures swap names (Beaver → Badger, Squirrel → Beaver). The
  0.589 client keeps showing the old names (its own text); nothing to send.
- **Recipes changed** (tablediff Recipes): 644-650, 652, 672 Lumicites (920-929, 981) ITEM_COUNT 500 → 750; 640 Build
  Site Flag (105) DEFAULT_LEARNED 1 → 0 (sites placed from the site window, 14 Jul 2016, TASKS 3.5); 5031 / 5036
  VISIBILITY_REQUIREMENT_ID swapped (1424 ↔ 1429); 5084 Recipe: Painting: The Withered Land 1394 → 1467; recipe 5086
  (also 4126, achievement 1467) removed. Station mapping removed: 5111 / 5112 Colonist's Patrol Armor (Purple / Orange)
  no longer at station 55. No component changed on an existing recipe.
- **New recipe details**: 5332-5338 packs cost 130 / 65 / 90 / 65 / 65 / 90 / 90 Lumens (item 1579); 5339-5360 emitter
  recipe items 130 or 260 Lumens, each with VISIBILITY_REQUIREMENT_ID 1563-1584 (by analogy with 1394 / 1424, which are
  "Recipe Discoveries" achievements in re/out/achievements.tsv, these are achievement ids — the rows are not in the
  achievement dump; hand-off #9); 5362 / 5363 / 5364 chests 130 / 260 / 780 Lumens; 5331 Roll Wraps: 1500 Smelted
  Indicite (114), 3000 Cut Tormeral (1756), 2 Adventure Essence (3683), 6 Affliction Essence (1588), RECIPE_TYPE 3,
  DEFAULT_LEARNED 0, TRACKER_ACHIEVEMENT 1467, **no station mapping** and CAN_CRAFT_WITHOUT_STATION 0 (other boots:
  station 53, default learned) — where it was craftable is not found yet (L26). All 0.695 recipes:
  re/out/agent_items/items695.txt.
- **Props** (PropDefs, tablediff): 82 new — item-placed ones in §1.1; 2902-2928, 2996 Ebonguard and 2978-2995 ruin
  creatures / hazards / Highfall Catapult / Target Dummy (spawner props, no items); 2945 Hour Node, 3010 Victory Node,
  3011 Score Node, 3012 / 3013 Team Spawners (no item in the dump; game props, hand-off #6). 15 changed (Magical
  Platforms scale, Game Table / Random Link Filter / Server Time Node / Respawn Point become trivial with recipe 52 and
  renamable, Team Spawner / Control Point renamed).

## 2. How the client gets item definitions (0.589 vs 0.695)

### 2.1 Paths and functions (all unchanged)

There is no item table in either client: definitions are server-sent records (u32 id, 3 flag bytes, name / description
text ids, image set, …, model strings, stack sizes, tint alias, category list, ability list, …, grant, stats — field
list in re/items_decode.py; field meanings in trace/systems/items.md and inventory.md). Each record travels as
{u16 compressed size, u16 size, LZ4 block}.

| Path | 0.695 | 0.589 | functions.tsv (status / code) |
|---|---|---|---|
| Record reader (fields → ClientItemDefinition) | FUN_142c176d0 | FUN_142b7df20 | changed / **same** |
| LZ4 wrapper, called by every path below | FUN_142c19850 | FUN_142b800a0 | same / same |
| Reader helpers (arrays, strings) | FUN_142c34a10, 142c34b60, 142c3df50, 142d4b5d0 | 142b9ae90, 142b9afe0, 142ba43d0, 142cb22d0 | same / same |
| CommandBase dispatcher (cases 0x4A, 0x4B) | FUN_1433baf00 | FUN_14330e110 | changed / changed (other cases) |
| 0x4A ItemDefinitionReply reader | FUN_143382bd0 | FUN_1432d5de0 | same / same |
| 0x4B ItemDefinitions (login list) reader | FUN_143382c90 | FUN_1432d5ea0 | same / same |
| Login-list handler (per record → cache) | FUN_1434042d0 | FUN_143358210 | same / same |
| Cache add ("Received item definition data", "Overwriting existing …", drops the pending request) | FUN_143402db0 | FUN_143356cf0 | changed / **same** |
| Lookup + 0x49 ItemDefinitionRequest {u32 id} ("not found or pending, will issue new request" / "pending, will drop request") | FUN_1434063a0 | FUN_14335a2e0 | same / same |
| ClientUpdate 0x29 sub 1 ItemAdd: bool "definition follows", then FUN_142c19850 and the cache add, then the item | FUN_142cd9980 case 1 | FUN_142c3fec0 | changed / changed (other cases) |
| Tooltip / item-definition tooltip reply ("OBSOLETE ITEM DEFINITION") | FUN_143515710 | FUN_143463610 | changed / same |
| Other definition readers (callers of FUN_142c19850) | 142e47a50, 142e49840, 142e4c1c0, 142f77eb0, 143089c10, 1431d3260 (requisition), 143404230, 14350c990, 143637bd0 | partners in functions.tsv | all same except 143089c10 (changed) |

So in both builds the client learns a definition from (a) the login list 0x4B, (b) a 0x4A reply to its own 0x49,
(c) a definition embedded in ItemAdd, (d) one embedded in a tooltip reply. No field was added or removed in 0.695.

### 2.2 What is new around items in 0.695 (packet layouts belong to #3)

- **Inventory 0x2D sub 0x1E InventoryPacketShowActivationFailure** (new class and new case; the 0.589 dispatcher
  FUN_142e81590 stops at 0x1D): dispatcher FUN_142f202e0 case 0x1e reads one u32 after the sub-opcode, looks it up as a
  text id (DAT_145431178 vtable +0x10) and opens a Lua CreateConfirmationDialog titled "Activation Failure". The
  matching server texts are new in 0.695 and have no client CodeStringMappings row: 577364 / 577416 (parlor pack
  already unlocked), 577705-577707 (hat already unlocked).
- **Parlor unlocks are a per-player option set** at player +0x7ae28: read in the 0.695 SendSelfToClient reader
  FUN_142c19bd0 (vtable +0x18 on +0x7ae28) and replaced by CharacterCustomization 0x11C sub 2 (FUN_1430a29d0 →
  FUN_14368a830, then the parlor UI refreshes). The Crimson Parlor data source FUN_1430a37a0 sets "PurchaseRequired"
  for an option that is not in that set and whose unlocking item (option record +0x3c) is a Marketplace product, and
  then fills "MarketName" / "MarketCost" from the store entry (store manager +0x460; name text at entry +0x17c,
  price FUN_143c2c370) — the UI (re/as3_695/BarberShopWidget, BarberShopWidget.as:293-298) shows "requires <pack>…
  purchase from the Marketplace". Hand-off #3 (layouts) / TASKS 3.3.

### 2.3 What the server sends now

| What | Where | Status for 0.695 |
|---|---|---|
| Login list 0x4B = the 44-CommandBase dump verbatim | ItemDefinitionManager.cs:127-148 (called WorldSession.cs:428) | valid (same layout; 0.695-era content) |
| 0x4A reply to 0x49 | InteractionHandler.cs:112-116 → ItemDefinitionManager.cs:182-195 | valid for dump items; **unknown ids get no answer** (184-189), the client then keeps the id pending |
| ItemAdd | ClientUpdateItemAdd.cs:17-28 | always writes "no definition" (line 23) |
| Tooltip replies | InventoryTooltipReply.cs:45-47 (RawData) | embeds the dump record |
| Server-side parse | ItemDefinitionManager.cs:202-251 | reads id … abilities; skips grantId, u59, stats |
| Item categories / profiles | WorldSession.cs:422-426 (dumps 39 / 41) | 0.695-era already (has 289-292) |
| Recipes, stations, props, text | GameTableManager.cs:33-69 + TextManager (gameData) | **0.589 copies** (md5 of Recipes / RecipeRecipeComponentMaps / CraftingStationRecipeMappings / PropDefs = the 0.589 install's; en_us_data.dat 1,358,552 B = 0.589) |

## 3. Items whose behaviour needs server code

| Item(s) | What the client / sources show | What to build |
|---|---|---|
| Fireworks packs 4444-4449 | grant 1124-1129 (server-side reward sets, not in any client table); contents in their descriptions 577414-577426 (e.g. 5 each of Gold / Green / Purple / Red Fireworks) | use from bag → remove pack, add the consumables (ids L24 / L25) |
| Pack of Unstable Fireworks 4443 | ability 1237, type 4 (use); 577412 "Leaves behind 50 'safe' fireworks when used, of a somewhat random variety…" | ability 1237: random 50 consumables (+ its own effect, not found yet) |
| 22 fireworks consumables | "Use to launch … into the sky" (P5914: single-use, a firework over your head) | definitions (L24), use ability → client effect over the player, consume one (effect ids L25) |
| 22 emitters 4455-4476 | props 2949-2970, model Theme_Widgets_Motes_Emitter01 (145-PropBase), InteractableCompositeEffectSets 129-150 / effects 278-321 (composite 12836-12857, "On"), action text 577506 "...Launch this firework."; "fire once when triggered" (TASKS 3.9) | placeable effect prop, link action plays the set; random drops (P5915); story-tool side → #6 |
| 22 emitter recipe items 4477-4498 | grant = emitter prop id | teach the prop on use (use grantId, not the name match in RecipeBook.cs:37-75) |
| Reward chests 4510, 4514, 4515 (+ old 4511-4513) | props 3003-3008; texts 577597-577635 (loot event / action, "Player looting cooldown", owner can't loot 577633, lockout 577634, locked 577651, "can't pick up a chest that has given out items" 577600 = Prop.PropPickupErrorPartialLootChest); mail 577638 / 577639 on use-up; per-tier loot descriptions 577607-577635; group loot (P6503) | TASKS 3.11 in full; loot tables per tier from the descriptions; Thank You Token mail (L24) |
| Pets (7 figurines) | P6142-P6147: right-click to summon, one at a time, follows, cancel the buff icon to dismiss; models 1729-1735; profile 379 "Non-Combat Entity" (likely); no new client class (rtti_new_695.txt has none for pets) | definitions (L24); use ability → buff with icon + spawn a non-combat follower NPC with the model; dismiss on buff cancel; Lop Ear from Outpost elites (L27) |
| Fantasy Parlor Pack 4419, hats 4521-4523 | §1.2 / §2.2 | definitions; use → add the options (and 4419's six tint groups) to the player's unlocked set, send 0x11C sub 2, consume; already-unlocked → ShowActivationFailure with 577416 / 577705-577707; Marketplace products (store) and hat drops (Outposts, Superb / Legendary chests, P6505) |
| Roll Wraps 4406 | boots slot 4, u59 [90] (other boots carry 51-69 there, e.g. Dash Soles 52); texts 577207 "Roll", 577211 "Execute an acrobatic roll in any direction." | the boot ability (roll); what u59 / def +0x1d0 is: L26 |
| Titles 4519 (+ new ones) | grant = title id (4519 → 60) | Titles.cs grants by id — check title 60 exists server-side; goals "They Like You…" (577647-577649) → #9 |
| Lumicite recipes | 750 per craft in 0.695 | build-gated recipe data (gap 4.20) |

## 4. Are the server's items still valid in 0.695?

- **Ids**: the dump is 0.695-era; every one of its 2,885 name ids exists in the 0.695 text (2,885 / 2,885), every model
  (with [RACE][GENDER] expanded to the four body types) exists in the 0.695 files (modelcheck.py: 0 missing) and every
  icon in the 0.695 ImageSets. No item id used by 0.589 tables disappeared in 0.695; the 23 component ids missing from
  the dump are missing for both builds (§1.3).
- **Renames** only (§1.4); no renumbering. Recipe 5086 removed; the server reads 0.589 recipes, so it still offers it
  (harmless; gate when moving to 0.695 tables).
- **For 0.589 clients**: 63 dump items have no 0.589 name text; 57 use 0.695-only icons (21666, 21676, 21681-21683,
  21685-21688, 21712-21733, 21771, 21772, 21778); their props (2940, 2949-2970, 2976, 3000, 3003-3008) are not in 0.589 PropDefs
  (3010-3013 neither). All models exist in 0.589 too.

## Gaps (merge into TASKS §4; numbering continues after the six existing lines)

- [ ] **4.7 Answer every 0x49**: when an id has no definition, the server stays silent (ItemDefinitionManager.cs:184-189)
      and the client keeps it pending (FUN_1434063a0). Build definitions for the items below and send them by 0x4A,
      and embed them in ItemAdd (ClientUpdateItemAdd.cs:23) for items not in the login list, as the client allows
      (FUN_142cd9980 case 1).
- [ ] **4.8 Definition builder**: write records from data (same layout as re/items_decode.py, LZ4 + sizes) instead of
      only replaying the dump, so new items (4419, 4521-4523, pets, fireworks consumables, Thank You Token, titles,
      flight suits, sweaters) can exist. Template each on the closest dump item (title → 4519, consumable → a type-4
      ability item, unlocker → a NO_TRADE-free stackable).
- [ ] **4.9 Fantasy Parlor Pack 4419**: definition (name 577355, description 577356 / 577361, category 289 Parlor Option),
      use → unlock CharacterCreateOptions rows with UNLOCKING_ITEM_ID 4419 + ArtTintGroupUnlock 2, 5-9 (their 45
      tints); persist per character; send 0x11C sub 2 and the set in SendSelf (+0x7ae28); refusal 577416 / 577364 via
      0x2D sub 0x1E. Marketplace product so the parlor shows MarketName / MarketCost.
- [ ] **4.10 Hat unlockers 4521 / 4522 / 4523** (Cowboy Hat / Ninja Mask / Samurai Helmet, icons 21775-21777, texts
      577699-577707): definitions, use → unlock option 295 / 305 / 301, refusals 577705-577707; drops from Outposts and
      the Superb / Legendary chests (P6505), tradeable.
- [ ] **4.11 Pets**: ids (L24), definitions with icons 21734-21740 and texts §1.3, summon / dismiss (L27), Marketplace
      for six, Lop Ear Bunny as a rare Outpost-elite drop (P6143).
- [ ] **4.12 Fireworks consumables (22)**: ids (L24), definitions with icons 21689-21711, the launch effect (L25), Lumen
      Station "Fireworks" section; pack contents for 4444-4449 (descriptions) and Unstable pack ability 1237 (50 random).
- [ ] **4.13 Fireworks emitters**: place props 2949-2970, the "...Launch this firework." action (577506) plays
      InteractableCompositeEffectSets 129-150; random-drop source (P5915); emitter recipe items teach via grantId.
- [ ] **4.14 Reward chests** (with TASKS 3.11): Common / Superb / Legendary craftable at station 55 (recipes 5362-5364),
      settings, loot per tier, group loot, owner can't loot, lockout, use-up mail + Thank You Token, pickup refusal
      (Prop.PropPickupErrorPartialLootChest 577600). Old tiers 4511-4513 / props 3004-3006: keep working if owned.
- [ ] **4.15 Roll Wraps 4406**: the roll boot ability (u59 90) and where it was obtained (L26).
- [ ] **4.16 New titles**: Champion of the People (4519, grant 60) is in the dump; The Lumerian Lens, Hero of Landmark,
      Landmark Explorer, Holiday Hero, Creator of Winter Wonderlands, Icy Architect need ids / definitions (L24);
      check Titles.cs knows title 60.
- [ ] **4.17 Holiday cosmetics 2016**: Colonist's Flight Suit ×3 (577737-577740) and Holiday Sweater Green / Blue
      (577741 / 577742) as Outpost drops from 6 Dec 2016 (P6566-P6570); Skeletal Bodysuits and the three older sweaters
      as seasonal drops (P6506, P6567). Ids / tint aliases: L24.
- [ ] **4.18 Recipe items by grantId**: RecipeBook links "Recipe:" items by name (RecipeBook.cs:37-75) with 0.589 text,
      which cannot name the 22 emitter recipes; use the record's grantId (prop id for 526 recipe items, recipe id for
      58) and parse it in ItemDefinitionManager.Parse.
- [ ] **4.19 0.695 text on the server**: TextManager reads gameData/en_us_data.dat = the 0.589 file; the 63 0.695-era
      items have no server-side names (mail, chat links, name matching). Load the 0.695 text (it is a superset for
      these ids) or gate by build.
- [ ] **4.20 0.695 recipe / prop tables per build**: Recipes (+33 / -1 / ~13), RecipeComponents (+10 / ~3),
      RecipeRecipeComponentMaps (+36 / -1), CraftingStationRecipeMappings (+32 / -2), PropDefs (+82 / ~15),
      PropCategoryPropMap (+58): the server reads the 0.589 copies; serve 0.695 crafting (fireworks, chests, Lumicite
      750) to 0.695 clients and keep 0.589 lists for 0.589 clients.
- [ ] **4.21 Gate 0.695-only items for 0.589 clients**: the 63 items (no 0.589 name, 57 without 0.589 icons) and props
      2940-3013 must not be granted / placed for a 0.589 client (or must degrade cleanly); the login list itself is
      fine as it is.
- [ ] **4.22 0x2D sub 0x1E ShowActivationFailure {u32 text id}** (0.695 only): use it for every refused item use on 0.695
      clients (already-unlocked parlor items, pets where summoning is refused…); layout check by #3.

## Hand-offs

- #3 (packets): Inventory 0x2D sub 0x1E ShowActivationFailure (FUN_142f202e0 case 0x1e, one u32 text id);
  CharacterCustomization 0x11C sub 2 (unlocked-option set, FUN_1430a29d0 → FUN_14368a830) and the +0x7ae28 set in
  the 0.695 SendSelfToClient reader FUN_142c19bd0; ClientUpdate ItemAdd embedded definition flag (FUN_142cd9980
  case 1).
- #6 (story tools): Combat Node 4408 / prop 2940, Scoreboard 4508 / 3000, IF Gate 4524 / 2976, Respawn Point 4509 /
  2701, Hour Node 2945, Victory / Score Node 3010 / 3011, Team Spawners 3012 / 3013 (no items), emitter link action
  577506.
- #9 (goals): VISIBILITY_REQUIREMENT_ID 1563-1584 on the emitter recipe items (Recipe Discoveries achievements by
  analogy), TRACKER_ACHIEVEMENT 1467 (Roll Wraps, Withered Land painting), the reward-chest goals 577643-577650
  ("No Good Deed…", "To Serve Luminaries", "They Like You…" → Champion of the People).
- #8 / ruins: props 2902-2928, 2978-2996 (Ebonguard, Slaug, Abomination, Chomper, hazards, Highfall Catapult, Target
  Dummy) are ruin spawner creatures, not items.
- TASKS 3.3 (Crimson Parlor): 0.695 parlor costs StringHashToValue CharacterCustomization.RaceChangeCost 30 /
  GenderChangeCost 60.

## Open leads added

L24 (login-list selection and the missing items' ids / definitions), L25 (fireworks consumable and pack effects /
contents), L26 (Roll Wraps source and the u59 field), L27 (pet summon mechanics) — trace/0695/OPEN-LEADS.md.
