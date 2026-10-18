# Spec: Daybreak-granted Design categories (TASKS 3.7, 30 Jun 2016) and the Starter Castle set

Status: research spec, 2026-10-15. Read-only trace. No code was changed and no client was run. This spec closes
lead **L40** (OPEN-LEADS.md) and answers audit **F5** (audit-equipment-goals-695.md).
Sources: re/decomp_all_695 (part_0120.c, part_0086.c, part_0153.c), gameData695/ExportedTemplate*.txt,
gameData/items.json (live item dump, `grantId`), re/as3_695/DesignsWindow, <0.695 test copy>\UI\Default (Lua, read only),
re/out/text_695.tsv, design/forum notes (21 Mar, 26 Apr, 11 May, 7 Jun, 30 Jun 2016).

Tags: **[P]** proven from code or data. **[I]** inferred. **[U]** unknown (see §7).

---

## 0. Answer in short

1. **The client never shows Daybreak designs from its own tables.** The category tables only decide which tab a
   design goes in. The design itself must still come from the server in the template list (TemplateBase 0xF0 sub 2
   / 3 / 0xC). The palette walks the client's template manager, which those packets fill. **[P]**
2. The map's `TEMPLATE_ID` is compared with the **TemplateBasicData u32 at +0xec**. On the wire that is the **third
   u32 after the "counts against storage" bool** (after the name text id at +0xe4 and an unknown u32 at +0xe8). The
   client reads it only when `IsDesignerTemplate != 0`. **[P]**
3. The id space is the live template database id. It is the same number a design item carries as its `grantId`:
   22 of the 28 Starter Castle ids are the grantIds of castle design items, and every Collected id with a design
   item matches too. **[P]** (the data matches; the shared id space is **[I]**, but very strong)
4. **The server writes 0 there today** (Templates.cs:308). Every designer design (the 27 virtual Starter Castle
   designs and the design-item copies) therefore falls back to **"Collected"** on 0.695. The "Starter Castle" tab
   never appears. **[P]** for the code path. **[I]** for what the client shows, because it has not been checked
   in the client.
5. **ExportedTemplates / ExportedTemplateSide / SideLoader** is the definition manager for .otd files the game
   stamps itself (ruins, tests, the old starter shapes). Its constructor has no caller, no side table ships, and no
   UI reads it. It is shared server/tool code built into the client, and the Designs window does not use it. **[P]**
   (no caller found) / **[I]** (that it is server-side)

---

## 1. Client trace

### 1.1 Data

| Table (gameData695 = 0.695 install Resources) | Columns | Content |
|---|---|---|
| ExportedTemplateCategory.txt | ID, NAME_ID, SORT_ORDER | 1 576947 "Starter Castle" (1), 2 577064 "Personal" (2), 3 577066 "Collected" (3), 4 577065 "Player Studio" (4) |
| ExportedTemplateCategoryMap.txt | ID, CATEGORY_ID, TEMPLATE_ID | rows 1-28 → category 1 (28 ids), rows 29-176 → category 3 (148 ids); categories 2 and 4 have no rows |
| ExportedTemplates.txt | ID, TEMPLATE_NAME_ID, AUTHOR_NAME_ID, CREATION_DATE, W/L/H, TEMPLATE_VERSION, TEMPLATE_FILE_NAME | ids 2-41 only (poi_test, cavern_ruin_template, starter_arcs/fonts/ovals/triangles 7-30, holiday 31-32, evacpod, test_ruin_*, tomb_of_the_unknown_king …). **No row for any id in category 1 (1119-1324)** |
| (side table) | EXPORTED_TEMPLATE_ID, ID, FACE_ID, ATTACHMENT_TYPE_ID, BUFFER_IN_VOXELS, ALLOWED_EMBED_DEPTH, ALL_AIR_VOXEL_PADDING | **not shipped** in either install (only VoxelCaveRoomGap*/VoxelGapSizeToSide*, which are something else) |

Because no category-1 id has an ExportedTemplates row or .otd name, the client could not load those designs' voxels
from local data even if it tried. The voxels have to come from the server (VoxelDataResponse 0xF0/5). **[P]**

### 1.2 Category build — FUN_1437546f0 (PaletteCollectionTemplates, part_0120.c:8725)

- Runs once (returns early if the category array count at +0x348 is not 0). **[P]**
- Looks up the localized strings `Template.Category.Personal / PlayerStudio / Collected`
  (DAT_145431178 vtable +0x20). **[P]**
- Loads ExportedTemplateCategory (FUN_143752bc0 / FUN_143755800) and ExportedTemplateCategoryMap
  (FUN_143752c80 / FUN_143755950). **[P]**
- For each category row it builds an entry (0x88 bytes) with FUN_143752e00: +0x00 ID, +0x08 name (NAME_ID text),
  +0x48 hash of the name (the palette folder key), +0x4c SORT_ORDER, +0x50 `HashListSet<int>` of template ids,
  +0x80 entry count. **[P]**
- Index of the category whose name equals Personal → +0x328, PlayerStudio → +0x32c, Collected → +0x330. Starter
  Castle has no special index. It is only a category with an id set. **[P]**
- For each map row it finds the category with that CATEGORY_ID and adds TEMPLATE_ID to its set (FUN_142ea6f90). **[P]**
- Categories are stored in table load order (the HashList's linked list). **[I]** This is 1, 2, 3, 4 in file order.

### 1.3 Which category a design goes in — FUN_143754540 (count) / FUN_143755d60 (entries) / FUN_143755020

Both walk the client template manager `FUN_142ccb9a0(DAT_1454311c8)` (hash by guid, record key at +0x190). That is
the store the template list packets fill. FUN_14310bbf0 case 2 is TemplatePacketEnvironmentListResponse with
`Array<TemplateCommon::TemplateBasicData>`. The record fields are at the offsets the 0.695 reader FUN_1430ff530
writes. **[P]**

```
if  rec+0x48 (IsOnMarketplace)    != 0 → Player Studio (+0x32c)
elif rec+0x50 (IsDesignerTemplate) != 0 → first category whose id set contains rec+0xec, else Collected (+0x330)
elif rec+0xf4 (own design bool)    != 0 → Personal (+0x328)
else                                    → Collected (+0x330)
```
(part_0120.c:8670-8716 and 9827-9841. FUN_143755020 is the "first set containing id, else Collected" lookup at
part_0120.c:9171.) **[P]**

- FUN_143755ca0 (vtable slot 3, part_0120.c:9700) runs FUN_1437546f0 + FUN_143754540 and then **creates a folder
  only for categories with count > 0** (FUN_143753e00 with name and sort order). An empty category does not show. **[P]**
- FUN_143755d60 (vtable slot 2) makes one TemplatePaletteEntry per template (FUN_1437ad110) and files it under the
  category hash (FUN_1437ad710). **[P]**
- Vtable: `PaletteCollectionTemplates` 1447e2c78 = {143753dd0, 143748730, **143755d60**, **143755ca0**, …}
  (re/out/rtti_695.tsv:6865). Constructor FUN_143752f60 binds `Character.MegaPalette.Template.{Collection, Categories,
  Entries, SortOptions}`. **[P]**
- Ids 1133-1141 (the 9 Modular Castle pieces) are in **both** category 1 and category 3. The first match wins, so
  they show under Starter Castle if categories are searched in file order. **[I]** (order of the HashList)

### 1.4 The record fields the categories use (0.695 reader FUN_1430ff530)

| Wire order (after AllTags) | Record | Meaning | Server today (Templates.WriteBasicData) |
|---|---|---|---|
| bool | +0xe0 | counts against storage (the client computes its own flag) | `!IsDesigner` (:303) |
| u32 | **+0xe4** | designer name text id. FUN_1437ad710 and the tooltip (part_0086.c:12124) use it, or "Daybreak Template (Invalid Name)" | name id (:306) |
| u32 | +0xe8 | unknown **[U]** | 0 (:307) |
| u32 | **+0xec** | **exported template id → category set** | **0 (:308) ← must change** |
| bool | +0xf4 | own design (Personal vs Collected) | IsOwnWork (:309) |
| string | +0xf8 | author (designer templates show "Daybreak" no matter what is sent) | AuthorName |
| u32 | +0x110 | TemplateValue | 0 |
| bool | +0x4c | ContestWinner | false |
| u32 | +0xf0 | unknown **[U]** | 0 |

The 0.589 docs give the name id at "+0xfc" (LandmarkEmu/docs/protocol.md:432, MODLOG 2026-10-10). That is the 0.589
layout, which still had the 0x18-byte PropData list. In 0.695 the same field is at +0xe4. **[P]**

### 1.5 Designs window (SWF) and Lua

- re/as3_695/DesignsWindow/.../DesignsWindow.as:26-27 reads only `Character.MegaPalette.Template.Entries` /
  `.Categories` (shown as "Name (NumChildEntries)", ModularWindow.as:262). Selecting a tab calls Lua
  `Templates.SelectCategory(<category GUID>)` (DesignsWindow.as:116-120). Nothing else is used. **[P]**
- Lua (<0.695 test copy>\UI\Default) has no category logic. Data.xml has only static placeholder rows. Template.lua:29
  forwards `SendToInventory` to `Templates.ExportToItem`. **[P]**

### 1.6 ExportedTemplates / SideLoader (part_0153.c:21708ff)

- FUN_143de1770 builds `ExportedTemplateDefinitionManager` (table "ExportedTemplates") with a
  `SideLoader` (vtable 144970ac0). The SideLoader's row readers FUN_143de28f0 / FUN_143de47b0 take
  EXPORTED_TEMPLATE_ID + ID and add an `ExportedTemplateSideDefinition` (FUN_143de3e00: FACE_ID, ATTACHMENT_TYPE_ID,
  BUFFER_IN_VOXELS, ALLOWED_EMBED_DEPTH, ALL_AIR_VOXEL_PADDING) into the definition's `Array<…,6>` (one per face).
  This is the attachment data for modular pieces. **[P]**
- FUN_143de1770 has **no caller** (grep of decomp_all_695), and FUN_143de3af0 / 143de3e00 are reached only from the
  manager vtables (L40 "found so far"). No side table ships. **[P]**
- Reading: this is live's server-side exported-template catalogue (the Env server's SpawnExportedTemplateRequest
  0xF4/0x18 stamps by id: ruins, the tomb, evacpod), compiled into the client library. The Designs window does not
  depend on it. **[I]**

---

## 2. Template id ↔ design (category 1 "Starter Castle")

Built by joining ExportedTemplateCategoryMap with the live item dump's `grantId` (gameData/items.json). The map rows for
category 1 are in **alphabetical name order**. All 22 ids that have a design item sit exactly where their name falls
in that order. This pins the 6 ids that have no item. **[P]** for the 22, **[I]** (strong) for the 6.

| Map row | Template id | Design | Name text | Server file (StarterCastle index) | Source on live |
|---|---|---|---|---|---|
| 1 | 1144 | Castle Courtyard | 574336 | castle_courtyard (0) | **default** (no item) |
| 2 | 1125 | Castle Hallway - Corner | 574256 | castle_hallway_(corner) (1) | item 4074 (bundle 1051) |
| 3 | 1124 | Castle Hallway - Long | 574284 | castle_hallway_(long) (2) | **default** |
| 4 | 1324 | Castle Hallway - Medium | 576962 | castle_hallway_-_medium (3) | item 4360 (1051) |
| 5 | 1123 | Castle Hallway - Short | 574281 | castle_hallway_(short) (4) | item 4072 (1051) |
| 6 | 1126 | Castle Hallway - T-Junction | 574255 | castle_hallway_(t-junction) (5) | item 4075 (1051) |
| 7 | 1323 | Castle Hallway with Side Doors | 576961 | castle_hallway_with_entries (6) | item 4361 (1051) |
| 8 | 1310 | Castle Long Room | 576199 | castle_long_room (7) | item 4085 (1051) |
| 9 | 1127 | Castle Room | 574226 | castle_room_v2 (8) | item 4076 (1051) |
| 10 | 1129 | Castle Room - Corner | 574294 | castle_tower_(corner) (9) | item 4078 (1051) |
| 11 | 1130 | Castle Room - Four Way | 574295 | castle_tower_(4_way) (10) | item 4077 (1051) |
| 12 | 1131 | Castle Tower | 574297 | castle_tall_tower (11) | item 4079 (1051) |
| 13 | 1132 | Castle Tower - Corner | 574298 | castle_tall_tower_(corner) (12) | item 4080 (1051) |
| 14 | 1128 | Castle Tower - Inline | 574296 | castle_tall_tower_(inline) (13) | **default** |
| 15 | 1120 | Castle Wall - Corner | 574251 | ramparts_wall_(corner) (14) | **default** |
| 16 | 1122 | Castle Wall - Long | 574253 | castle_wall_(long) (15) | item 3706 (1051) |
| 17 | 1143 | Castle Wall - Medium | 574302 | castle_wall_(medium) (16) | **default** |
| 18 | 1119 | Castle Wall - Short | 574252 | ramparts_wall_(short) (17) | item 3707 (1051) |
| 19 | 1121 | Castle Wall Entry | 574257 | ramparts_wall_(entry) (18) | **default** |
| 20 | 1140 | Modular Castle Entry | 574310 | modular_castle_entry (19) | item 4098 (source **[U]**) |
| 21 | 1135 | Modular Castle Entry with Wall | 574305 | modular_castle_entry_with_wall (20) | item 4099 |
| 22 | 1139 | Modular Castle Hall Entry | 574309 | modular_castle_hall_entry (21) | item 4100 |
| 23 | 1134 | Modular Castle Long Room | 574304 | modular_castle_room_(long) (22) | item 4101 |
| 24 | 1137 | Modular Castle Room Hallway | 574307 | modular_castle_room_hallway (23) | item 4102 |
| 25 | 1138 | Modular Castle Tower Hallway | 574308 | modular_castle_tall_tower_hallway (25) | item 4103 |
| 26 | 1133 | Modular Castle Tower | 574303 | modular_castle_tower (24) | item 4104 |
| 27 | 1141 | Modular Castle Window with Wall | 574306 | **no .otd** (not in the array) | item 4106 |
| 28 | 1136 | Modular Castle Window | 574311 | modular_castle_window (26) | item 4105 |

Notes:
- The other grantIds 1124 / 1125-1129 / 1134 / 1137 / 1141 / 1143 / 1144 on fireworks packs (4444-4449) and
  "Recipe:" items are other id spaces (loot / recipe / prop). Only design items (no category, or cat 101 "Design")
  use template ids. **[P]** (the data shows it)
- The 6 defaults include all 4 designs that goal 471 "Your Home Is Your Castle" asks you to place (Courtyard, Wall
  Entry, Tower - Inline, Wall - Medium; Achievements.cs:838). This matches 21 Mar 2016: "You'll start with a
  handful of these and earn more through achievements". **[P]** (notes) / **[I]** (that it is exactly these 6)
- Category 3 "Collected" (148 ids): 6-32 (old starter shapes, holiday reindeer / snow globe; design items 1727-1767
  carry grantIds 7-30), 599-608 (Starter Homes, items 2641-2646), 740-838, 1087-1098, 1133-1141 (again), 1318-1319
  (Architecture Tools arcs, items 4358/4359). These are Daybreak-made designs that came as items or gifts. 11 May
  2016: "Collected (which includes … gift designs such as the holiday snow globe)". A designer template with an
  unknown id goes to Collected anyway (§1.3), so this list changes nothing except for the dual 1133-1141. **[P]**

---

## 3. What the server must send (to look like live)

1. **Every design the player has, in the list**: TemplateBase 0xF0 sub 2 on zone-in (PlayerManager.cs:82), sub 3 when
   one is added, sub 0xC on change. This includes Starter Castle designs. The client cannot invent them. **[P]**
2. For every **Daybreak (designer) design**: `IsDesignerTemplate = 1`, name text id at +0xe4, **exported template
   id at +0xec**:
   - Starter Castle pieces: the id in §2. They then appear under "Starter Castle". **[P]** (code path)
   - Design-item designs (old starter shapes, Starter Homes, Architecture Tools …): the item's grantId (Collected ids).
     They appear under "Collected", as they do now through the fallback. Sending the real id keeps them right if
     tables change. **[P]**
3. Player designs: IsDesignerTemplate 0 and `own = true` go to Personal, `own = false` goes to Collected, and
   IsOnMarketplace goes to Player Studio. No change. **[P]**
4. **Which Starter Castle pieces** a character has (the 6-vs-27 rule; P5750 7 Jun 2016 "The modular Castle designs
   will be removed from all players during the wipe so they will be unlocked as intended throughout the tutorial"):
   - New character: the **6 defaults** (1144, 1124, 1128, 1120, 1143, 1121). **[I]**
   - Goal 471 (and again 477, both reward bundle 1051 in the Feb 2017 capture): 13 design items. Using one unlocks its
     Starter Castle piece. **[P]** (capture) / how live granted it **[U]** (as an item, or applied straight away; the
     7 Jun note "Designs acquired through Goals should now have an icon … when initially granted" points to designs
     arriving through goals)
   - The 9 Modular Castle pieces: items 4098-4106 exist, but no captured goal rewards them. U4's lost tutorial goals
     (Building Block Basics 576265, Modular Thinking 576271) are the likely source. **[U]** Until that is settled,
     keep the 8 that have files as defaults (audit F5), so they stay obtainable.
5. Bandwidth: one list on zone-in, as now. Designer entries carry no voxels until asked for (0xF0/4, bursts of 25). **[P]**

---

## 4. Server changes (LandmarkEmu/LandmarkEmulator.WorldServer)

| # | File / function | Change |
|---|---|---|
| S1 | Game/Templates.cs `StarterCastle` (:119-130) | Add `TemplateId`, `ItemId` and `Default` to each tuple (§2 table). **Keep the order**: the guid is `"ST" + index` (:133, :159) and must not shift. If a file for Modular Castle Window with Wall (1141) turns up later, add it as index 27, at the end. |
| S2 | Templates.cs `WriteBasicData` (:276-314) | Third u32 after the storage bool (:308): write `ExportedTemplateId(design)` instead of 0 when `IsDesigner(design)`. For a starter design it comes from the table. For a design-item copy it is the grantId of the design item with the same name (new helper next to `ItemUses.DesignNameId`, :233. Static table from §2 + Collected ids, or parse grantId as TASKS 4.18 / spec-fireworks C plan, at record length − 20). The 0.589 path writes the same u32 (it was always a u32 slot), so it is harmless there. 0.589 is frozen anyway. |
| S3 | Templates.cs `SendList` (:207-221) | Add starter `i` only if `Default` **or** `Unlocks.Has(session, Unlocks.StarterDesign, TemplateId)`. |
| S4 | Game/Unlocks.cs (:31) | New kind `StarterDesign = 20` (value = template id). Document it in the summary at :18-29. |
| S5 | Templates.cs `Find` (:198-204) | For `IsStarter(guid)`: return the model only if the owner has that piece (default or unlocked). Otherwise someone could paste a design they don't own by guid (BuildingClipboard.cs:385, DesignItems.Export, Commerce.cs:134). |
| S6 | Game/ItemUses.cs `LearnDesign` (:253-277) | If the item is a Starter Castle piece (by ItemId / name → §2), call `Unlocks.Add(StarterDesign, id)`, then `Templates.SendAdded(session, Starter(index, …))` and `SendLearned` (0x11). **Do not** store a DB copy (no storage use, no duplicates). If the piece is already known: what live did is **[U]**. Proposal: keep the item and send a system message, so a second bundle-1051 grant (goal 477) stays tradeable. |
| S7 | Templates.cs comments (:286, :379-386) and docs/protocol.md (:426-435), docs/gameplay.md (:314-315) | Correct the category rule for 0.695: designer → category by +0xec, else Collected. Name id at +0xe4 (0.589: +0xfc). |
| S8 (optional) | DesignItems.Export (:25) | Consider refusing designer templates (`IsDesigner`): otherwise Daybreak designs become tradeable Player Design items. Whether live refused this is **[U]**. Check the native right-click menu that calls Lua `Template:SendToInventory` first. |

Nothing changes in the 0.695 wire layout. Only the value of one u32 and which designs are in the list change.

## 5. Migration (existing characters, dev world)

- Placed builds are not affected. A paste copies the voxels into the terrain, and no placed record keeps the
  starter guid (BuildingClipboard paste reads `VoxelsOf` once). **[P]** (code read)
- Today every character sees all 27, and some may also have **DB copies** of castle pieces from using goal-reward items
  (`template` rows with AuthorName "Landmark Team"/"Landmark" and a Starter Castle name; ItemUses.LearnDesign →
  AddFromTemplate).
- Proposal (one-time, back up the character DB first with dbq.sh; the dev world restarts):
  1. For every existing character: `Unlocks.Add(StarterDesign, id)` for **all 27 ids that have files**. Nobody loses
     a design they had. Only characters created after the change start with the 6 (+8 modular, §3.4).
     Use a marker (for example an Unlocks row kind 20 value 0) so the step runs once.
  2. Delete the DB rows that duplicate a Starter Castle piece (designer author + starter name) after adding their
     unlock. Their guids are not referenced anywhere else (no images or props files for designer copies).
  3. Alternative if a launch-faithful reset is wanted (the 7 Jun wipe): unlock the 6 defaults, the 13 from
     bundle 1051 only for characters with goal 471 or 477 complete, and any piece they had as a DB copy. Choose
     before running. Option 1 is the safe default for a dev world.
- Say in MODLOG / TESTING-NEEDED that the dev world restarts for this.

## 6. Verification (client's own reaction; <0.695 test copy> only)

1. Before the change, with an existing character: open Designs (L). Prediction from the code: no "Starter Castle" tab,
   and the castle pieces are counted under "Collected (n)". This confirms that +0xec = 0 sends them to the fallback.
2. After the change: "Starter Castle (27)" for a migrated character. A new character gets "Starter Castle (14)"
   (6 + 8 modular) and **no Collected tab** until it owns a gifted or other-author design (empty categories are hidden,
   FUN_143755ca0).
3. Use a bundle-1051 item (for example 3706 Wall - Long): the count goes up by 1, the "Learned.Template" notice shows,
   no DB row is added, and design storage (Character.Template.Limits) is unchanged.
4. Use an old-shape item (1727): the design appears under Collected, as before.
5. Read the client state from the palette data source or the window label, not only the server log.

## 7. Unknowns (also go to OPEN-LEADS / TASKS)

- U-DC1: meaning of TemplateBasicData +0xe8 and +0xf0 (u32s; server sends 0). Try the 0.589 readers of +0x100 / +0x108
  and the Commerce product records.
- U-DC2: category search order for the dual-listed 1133-1141. It depends on the HashList order, inferred to be file
  order (category 1 first). Check FUN_143755800 insertion, or check in the client (Modular pieces under Starter
  Castle?).
- U-DC3: source of the 9 Modular Castle items 4098-4106 (= audit U3/U4). 2026-10-16 (TASKS 6.57): built as the reward of
  the tutorial goal Building Block Basics (the 8 with a file; Modular Thinking, the goal after it, makes the player place
  them; P5750) [I], so they are no longer defaults; proof still open in OPEN-LEADS L112.
- U-DC4: live behaviour when a design item's design is already known; whether goal rewards arrived as items or as
  designs directly (the 7 Jun note about the icon).
- U-DC5: the guid live used for designer templates (any unique u64 works for the client).
- U-DC6: whether live allowed ExportToItem on designer templates.
- U-DC7: the ExportedTemplateSide table (never shipped) and who builds ExportedTemplateDefinitionManager. Server-side
  stamping (Env server, terrain-service.md 0xF4/0x18) is the likely user. Not needed for the Designs window.
