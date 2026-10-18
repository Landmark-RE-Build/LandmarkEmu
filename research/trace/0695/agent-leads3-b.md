# Open leads, third pass — group B (content, data tables, items, goals, placement, event islands, rule numbers), 2026-10-17

Read-only pass over L1 L2 L10 L11 L13 L14 L16 L17 L18 L19 L24 L25 L42 L43 L45 L46 L47 L86 L87 L89 L90 L92 L93 L95
L100 L101 L109 L110 L111 L112 L113 L119 L120 L131. Each lead started from its OPEN-LEADS.md entry and the pass-2 /
sweep files. This pass tried only paths those passes had not recorded:

- the **live final CDN manifest** (re/out/final-digest-2017: landmark-final-2017-file-list.csv, 34,138 files, and
  common-shared-cdn.sha.soe.txt, Feb 2017);
- the **live PropBase dump's internal prop names** (LandmarkEmulator.WorldServer/Resources/PacketDumps/145-PropBase:
  1,981 records {model .adr, prop id, name, internal "landmark_v2: builder: …" name}; scratch parser
  proprecs.json);
- **voxel decoding of both Spire templates** (scratch otdcells.py / spire2.py: every cell blob unpacked, SerializeBlockData
  format 2) and the **DME bounding boxes** of the Spire models (pack.py get, header AABB);
- **Resources/Images/Images.txt + ImageSetMappings.txt** (image ids, as opposed to image-set ids) and the AS3 image loader;
- full-text greps of the wiki dump and forum scrape for wording not searched before (IF node "If / Is", Combat Node
  settings, durability on dirt, attached expansions, wiki list pages);
- 0.695 Recipes.txt column cross-tabulation; 0.695 decompile for the tooltip-flag writer and the Travel zone list.

Marks: **[P]** proven (address / file:line / table row / archive post), **[I]** inferred (reason given).

## 1. Summary

| Lead | Verdict | One line |
|---|---|---|
| L1 | CLOSE-BY-DESIGN | TilingTestZZ.bmp is not in the **live Feb 2017 CDN manifest** either (33 masks listed, not it); row 170 is a test row (group 17, altitude 0-4000, angle 0-90). Live clients never had it; the server keeps groups 17/18 out. |
| L2 | ADVANCED (proposal) | Not in the live manifest (server-only, proven). Best-evidence default for the entity-list model from Domino's 55352 post: min / max per island-layer-biome-entity, respawn elsewhere when below min. |
| L10 | UNCHANGED | No new text near 574500; keep 24 h. |
| L11 | ADVANCED (small) | Player, 19 Apr 2016: picks / extractors wear even when digging dirt; wear is not scaled by material. Our flat 1 point per swing matches. No change. |
| L13 | FOUND (no change) | Bake check of 2026-10-16 stands. |
| L14 | **FOUND** [P] | TELEPORT_IMAGE_ID is an **Images.txt image id**: 10228 = back_depthSelector_tier1.dds, 10229 = **back_depthSelector_tier2.dds**. Layer 496 is a tier-2 depth layer. The Travel window shows it as a zone row's ImageId (FUN_143174ca0). |
| L16 | ADVANCED (bookkeeping) | Material 65532 is already FOUND (= interior air, ruin-stamping.md §1, VoxelTemplate.InteriorAir). Only the .ocm grids and "client without the files" stay open. |
| L17 | CLOSE-BY-DESIGN | Already closed 2026-10-16 (client-only palette). |
| L18 | CLOSE-BY-DESIGN [P] | EditablePropertyValues.txt is also absent from the live manifest. The live client never had it, so drop-down options always came from the server's EditSettingsReply. Our option values are legitimately the server's own. |
| L19 | **ADVANCED** [P] | Dev post: the IF Gate rows are labelled **"If"** and **"Is"**. Patch notes + wiki: the **Combat Node's settings show Combat Tactics, then the linked creature's ability list**. Our server sends the Combat Node **no rows** (gap). |
| L24 | UNCHANGED | Built with our own ids; no capture on disk. |
| L25 | UNCHANGED | Built (emitter composite at the player). |
| L42 | UNCHANGED | Record ids exist only in captures; built from the wiki. |
| L43 | **ADVANCED** [P data] | VISIBILITY_REQUIREMENT_ID is set on exactly the 581 Lumen Station "unlock" purchases (CAN_CRAFT_WITHOUT_STATION = 1, MAX_QUEUE 1): 555 "Recipe:" items, 22 firework emitter recipes, the Dungeon Builder's Starter Set, the 3 store expansions. It is one id per recipe, numbered in recipe order. |
| L45 | **FOUND** (4 of 5) | Dark Desert Succulent (+ With Butterflies) = props **275 / 392 "Drylily Plant"** (live internal name "desert succulent (dark with butterflies)"; wiki gallery caption). Glowing Jungle (Triple) Pendant Plant = flora OrbSingleGlow01 / OrbThreeGlow01, never a prop. Bowl of Apples: no model exists. Large Square Knotted Rope: model only, no prop. |
| L46 | UNCHANGED | — |
| L47 | UNCHANGED (+neg) | Template ids 61 / 75 / 76 are in neither VoxelSystemTemplates (2-30) nor ExportedTemplates (2-41). |
| L86 | ADVANCED (small) | The **Attached** Build Site Expansion (texts 571177 / 571251, image set 10465) needs no item: attached sites were removed for the 2016 relaunch. The plain expansion is built. |
| L87 | UNCHANGED | — |
| L89 | CLOSE-BY-DESIGN | Already matches (5 s grace, dropped on attack). |
| L90 | UNCHANGED | — |
| L92 | UNCHANGED (+confirm) | The wiki's Modular Thinking steps require Window / Long Room / Entry designs before the goal. This fits the built Building Block Basics reward. |
| L93 | UNCHANGED | — |
| L95 | CLOSE-BY-DESIGN | Built as a drop-down; option values are the server's (see L18). |
| L100 | **ADVANCED (major)** [P] | Both Spire templates hold **no solid voxel**: an air box 124 × 68 × 124 m with a 0.4 m vertex-only floor. The Hub model's AABB is y −20.94 … +43.74 m, so its base sits on the template floor. The server stamps the floor ~21 m (±8 m from cell rounding) **below** the ground. |
| L101 | CLOSE-BY-DESIGN [P] | The client shows "Unique Equipped" only from **tooltip flag 0x02** (0.695 FUN_1435176a0, caller FUN_143768e20 reads tooltip +0x1c8) and refuses only on TransferItemResponse 0x22. No code reads definition +0xe. The rule is the server's, as built. |
| L109 | UNCHANGED | Teleport.Worlds columns {Name, GUID, Claims, ClaimCapacity, CurrentWorld}, sorted by Name string (TeleportSpireWidget.as:32, :120). No id. |
| L110 | UNCHANGED | Expiry is the user's decision. |
| L111 | UNCHANGED | Depends on L100: landmarks are placed beside the Spire. |
| L112 | UNCHANGED | — |
| L113 | ADVANCED (proposal) | As L2: concrete min / max / respawn-elsewhere default. |
| L119 | UNCHANGED | — |
| L120 | UNCHANGED | Only the Barren Lightbringer has a heal (wiki "Ability 1: Rejuvenate"). It is already built. |
| L131 | UNCHANGED | No post-7 Jun report of a trip landing on another world. |

---

## 2. Per lead (new this pass)

### L1 — TilingTestZZ.bmp: CLOSE-BY-DESIGN

- **[P]** The live CDN file list of Feb 2017 (re/out/final-digest-2017/landmark-final-2017-file-list.csv) lists 33
  files under `CommonData\ProceduralPlacementTool\Masks\`: the 31 PPP_Mask_* files plus TestMask01 / TestMask02. It has
  **no TilingTestZZ.bmp**, and no file name containing "tiling" anywhere in the 34,138 files. So no live client ever
  received it. It was a tool-side file.
- **[P]** Row 170 differs from the real sets: group 17, biome 32, MIN_SITE_ALTITUDE **0** (every real set uses 3000),
  slope 0-90°, scale 1-1, spacing 50/50, priority 0. It is a tiling test row [I strong].
- **Server:** none. ProceduralPlacement already leaves groups 17 / 18 out.

### L2 / L113 — entity lists and live's spawn model: ADVANCED (proposal)

- **[P]** ProceduralPlacementEntityLists / …EntityListEntities are absent from the live manifest too. They were
  server-only data.
- **[P] The model** (Domino, forum 55352 #1, 19 Jun 2015):
  - Each spawnable type (each tree, plant or NPC) has a minimum and a maximum per island, per layer and per biome.
  - At server start, a random count between the two is placed on random allowed locations. The example is 1000-1500
    curved palms on 5000 possible locations.
  - When a type falls **below its minimum**, one respawns "within about 20 minutes" at **any random allowed location
    of that biome on that island**, not where it was cut.
  - The 3-nights-a-week restarts reset every spawn.
- **Best-evidence default [I]:** the numbers in the post are illustrative ("making up some nice round numbers"). The
  ratios are min = ⅔ max and max = 30 % of the nodes. Two options:
  - Option A (keeps today's density): candidate nodes = today's jittered AVERAGE_DISTANCE grid. Per (zone, set,
    entity) pool, max = the node count and min = ⌈⅔ max⌉. Start at a random count in [min, max]. Below min, respawn one
    after MIN..MAX_RESPAWN_MINUTES (12-18 on most sets, which fits "within about 20 minutes") at a random **free**
    node of the same set on that island.
  - Option B (the post's ratios literally: 1000-1500 of 5000) would cut forest density to 20-30 %. Not recommended
    without screenshots.
- No data proves which. Mark it [I]; it is the user's choice (TESTING-NEEDED).

### L11 — tool wear: ADVANCED (small)

- **[I, 1 report]** hammerman32 (forum 56901 #93, 19 Apr 2016): "we have to repair our Picks and Extractors when they
  get damaged from digging DIRT". He then suggested "Maybe the damage should be modified by the Materials collected".
  So live wore tools on every dig, dirt included, by the same amount whatever the material.
- That matches ours: Gathering.RollDrops runs on every collecting mining swing, dirt included, through
  Abilities.Dig:851, and Durability.ToolLossPerHarvest is a flat 1.
- **[P] DurabilitySet rows (gameData695/DurabilitySet.txt):**
  - set 1 = 9751 / 0 / 0;
  - set 2 = 9834 / 0.009 per hp / **984 per death** (exactly 10 % of 9834, rounded up);
  - set 3 = 4917 / 0.0045 / 492 (10 %);
  - set 5 = 14751 / 0 / 0.
  The tool sets' "+1" (9751 = 9750 + 1, 14751 = 14750 + 1) suggests a designed count of 9,750 / 14,750 uses, so
  1 point per use [I weak]. No change.

### L14 — layer 496 and TELEPORT_IMAGE_ID: FOUND [P]

- **The image id space.** The AS3 ImageUtils.SetImageById loads `img://ii?id=<n>`, an **image** id. Image sets go
  through `img://si?setid=` (BuildModeWidget/scripts/soe/utils/ImageUtils.as:30-38, :71). Pass 2 looked the ids up in
  ImageSets (outfit icons), which is the wrong table.
- **[P] <0.695 test copy>\Resources\Images\Images.txt** (the same ids are in the 0.589 install):
  - 10228 = back_depthSelector_tier1.dds;
  - 10229 = back_depthSelector_tier2.dds;
  - 10230-10232 = tiers 3-5.
- **[P] Where the client shows it.** The Travel window's zone rows, `Teleport.Zones` {Name, GUID, Claims,
  ClaimCapacity, ImageId, SurfaceTier, Total Claims}, are filled by **FUN_143174ca0** (part_0088 ~9716-9718) from the
  server's Locations record ints [3] total claims, [5] surface tier and [6] image id. The default "Icon" view
  (TeleportSpireContinentListItemRenderer → TeleportImageListItemRenderer, `_iconField = "ImageId"`) draws it.
- **So:**
  - Layer 496 "391_Layer2" is a **tier-2** depth layer. That fits its tier-2 cave groups 234 / 228 / 258
    (VoxelLayerBmpToPropGroups 1610-1612).
  - Every other layer is tier 1 (10228).
- **Server:** no change needed. Landscapes.WriteWorld already sends SurfaceTier 1 and ImageId 10228, which is
  consistent. Fix the comment on Landscapes.ImageId: it is the Images.txt id of the tier-1 depth-selector background,
  i.e. ImageId = 10227 + SurfaceTier [P names, I rule].

### L16 — material 65532: ADVANCED (bookkeeping)

- 0xFFFC is already proven to be interior empty space (trace/0695/ruin-stamping.md §1; client FUN_143f9a150 /
  FUN_143f9c330, paste FUN_142a7eeb0; VoxelTemplate.InteriorAir). The OPEN-LEADS entry still lists it as open.
- Close that part. The .ocm second grid / int-pair lists and the client's behaviour without the asset stay open.

### L18 — EditablePropertyValues.txt: CLOSE-BY-DESIGN [P]

- Not in the live Feb 2017 manifest. With the pass-1 file indexes this means **no client build ever shipped the
  option table**.
- Both builds' EditSettingsReply carries each drop-down's options {label text id, value, is-default bool (+ 0.695
  string label)} (PropSettings.cs:536-547). So on live the options always came from the server.
- EditableProperties VALUE_DEFAULT on drop-down rows (89 / 81 / 51 / 109 / 106) are ids in the server's option table.
  The client does not need them.
- **Server:** none. PropSettings.DropDowns (AnimationSelection ids) are a valid server-side choice. Close L18.

### L19 — server-only setting rows: ADVANCED [P]

- **IF Gate labels [P].** Domino's 29 Sep 2016 notes (forum 57987 #1, line 66): "we select 'Current Value' as the
  property to check in the **If** section, 'Equal To' in the **Is** section, and then we set the **slider** for the
  value to be checked."
  - The rows are labelled **If** and **Is**, and the value row is a slider.
  - Ours are labelled "Property" / "Comparison" / "Value" (PropSettings.cs:160-165).
  - [I] Comparison options: manopoliy (57064 #206, 7 Oct 2016) proposes timer checks with "equal … greater, less then
    or not equal". That matches our four options (Equal To, Not Equal To, Less Than, Greater Than) but does not prove
    live's list.
  - 11 Oct 2016 (58022 #1): the triggering-character checks are "game team, NPC disposition, and whether or not it's a
    real player". Built.
- **Combat Node settings [P].**
  - P6150: "Combat Tactics, now available on both creature spawners and the Combat Node".
  - P6157: "Open the Combat Node's settings, and set the desired Combat Tactics (e.g. 'Stationary'). Below this you
    will see a list of the creature's abilities."
  - P6152: the list appears once the node is linked to a creature.
  - Wiki "Combat Node": "Settings: Name, Combat Tactics … After being linked to a creature, the Combat Node Settings
    will also show a list of the creature's abilities and designate them Ability 1, Ability 2, etc".
  - **Gap in our server:** PropSettings.settingsByKind (PropSettings.cs:345-396) has no entry for
    StoryTools.CombatNodeProp 2940, and ServerRowsOf returns rows only for the IF Gate. So the Combat Node's window is
    empty. Yet StoryTools.cs:1228 / :1235 read the node's own row 31 (`PropSettings.Choice(r.Guid,
    PropSettings.SpawnerAi)`), which a player can never set, so it is always Standard.
- **Desired Time rows:** the wiki "Server Time Node" page confirms "Desired Time 1-6 … New Desired Time settings will
  appear dynamically as the Amount of Listeners setting is updated and saved". Built as is.

### L43 — VISIBILITY_REQUIREMENT_ID: ADVANCED [P data]

- **[P] Cross-tab of 0.695 Recipes.txt** (columns RECIPE_TYPE × TRACKER × VISIBILITY × CAN_CRAFT_WITHOUT_STATION ×
  output item name):

  | group | rows | VISIBILITY | CAN_CRAFT_WITHOUT_STATION | outputs |
  |---|---|---|---|---|
  | type 1 | 555 | set | 1 | every "Recipe: X" item (e.g. 1939 → 3266 Recipe: Arched Highfall Door (Left), 723) |
  | type 1 | 23 | set | 1 | 3760 Dungeon Builder's Starter Set (2389); 4477-4498 "Recipe: Fireworks Emitter (…)" (5339-5360, 1563-1584); 4403 Recipe: Highfall Catapult (5330, 1522) |
  | type 3 | 3 | set | 1 | 3710 Vault / 3711 Inventory / 3712 Design Expansion (2386-2388) |
  | type 1 | 307 | none | 1 | outfits |
  | type 3 | 69 | none | 1 | cut stone |
  | — | — | — | — | VISIBILITY is never set together with a TRACKER_ACHIEVEMENT_ID |

- The ids are 579 distinct values (723-1584), allocated in recipe order (e.g. 1939 → 723, 1944 → 728). They are
  **not** objective ids. They overlap the capture's objective id range by chance: recipe 2003 = 3330 Recipe: Hanging
  Lantern has 787, which is objective "Regal Master Bed recipe unlocked".
- **[I strong]** Every one of these is a one-time Lumen Station **unlock purchase** that becomes pointless once owned
  (prop known, set crafted, expansion at its cap), while repeatable crafts (outfits, cut stone) have none. So
  VISIBILITY_REQUIREMENT_ID is a server Requirements row "player does not have this yet", which hides the purchase.
  The client's Requirements.txt has only 75 client rows (3-76), so the client cannot evaluate these ids.
- **Reader still not found.** Loader FUN_143cb6e00 stores it at +0x40. A function-body scan for +0x34 / +0x40 / +0x44
  readers found only unrelated hits (e.g. FUN_142e76890 is a transform). **Next:** see §4 (a hardware-watchpoint test
  finds it in one run).

### L45 — wiki pages under other names: FOUND (4 of 5) [P dump names, wiki captions]

From the live 0.695 PropBase dump's internal names (145-PropBase; record = model .adr, prop id, name, internal name):

| Wiki page | Answer | Evidence |
|---|---|---|
| Dark Desert Succulent With Butterflies | **prop 392 "Drylily Plant (with Butterflies)"**, model Biome_Ashfang_Plants_Plants_Dark01_FX_ButterfliesWhite | [P] internal name `landmark_v2: builder: default: desert succulent (dark with butterflies)`; [P] wiki "Desert Biome Props (List)" gallery: `Dark Desert Succulent with Butterflies prop placed.png\|Drylily Plant (with Butterflies)` |
| Dark Desert Succulent | **prop 275 "Drylily Plant"**, model Biome_Ashfang_Plants_Plants_Dark01 | [I strong] same model without the FX (internal name now "drylily plant"); the wiki's old prop list (Wiki projects_Prop Articles) pairs it with the butterflies variant |
| Glowing Jungle Pendant Plant / Triple Pendant Plant | **no prop**. They are surface-jungle flora models Biome_Feerrott_Plants_Plants_OrbSingleGlow01 / OrbThreeGlow01 (auto names 557664 / 557665, both builds) | [I strong] the wiki lists them among the Feerrott surface props (ferns, shrubs, vines); one glow orb / three glow orbs; no PropBase record uses either model |
| Bowl of Apples | **no prop**. No BowlApples model exists in either build's packs; the Erudite food set has PileApples01 (2620 "Apples"), PlateApples01 (2627) and BowlCantalope01 (2632) only | [P neg] fileindex_589 / _695; [I] the wiki's Food list made it up by analogy with "Bowl of Cantaloupes" |
| Large Square Knotted Rope | **no prop**. Ropes_KnotLarge02.adr is in the 0.695 packs, but no PropBase record uses it. By analogy CoilLarge02 = 2251 "Large Square Coiled Rope" | [P] dump / fileindex; [I] naming analogy |

- Previous pass: Rusty Sifter and Coiled Hanging Chain. The dump also confirms 754 Rusty Sifter (Small) = internal
  "roughwork sifter".
- **Change:** add the aliases to re/out/agent_wiki/audit.py: 275 / 392, plus "no prop" for the other three. No server
  change. The wiki-content audit can drop these five from its gap list.

### L47 — .cpa manual templates: UNCHANGED (+neg)

- Template ids 61 / 75 / 76 match no row of VoxelSystemTemplates.txt (ids 2-30: poi tests, cavern_ruin_template,
  starter arcs / fonts / ovals) or ExportedTemplates.txt (2-41). They are server-side template ids.

### L86 — Build Site Expansion: ADVANCED (small)

- **[P]** The client also has an **Attached** Build Site Expansion:
  - names 571177 / 571251;
  - refusal 571175 "maximum number of expansions for attached Build Sites";
  - ImageSets 10463 icon_flag_claim_expansion_attached and 10465 flag_claim_expansion_attached (Images/icons_flags).
- **[P]** Attached sites were removed for the 2016 relaunch: forum 56105 #1 (2 Mar 2016) says the new site size is
  "the size of over 16 old claims … the concept of attach[ed claims goes away]". The wiki "Build Sites" says
  "Attached claims were removed because the overall size of a single build site is now as large as the prior maximum".
  So no attached-expansion item is needed.
- Pre-wipe maximum, one player: 8 base + 24 attached, 13,993 DBC (56105 #146).
- The plain expansion is built (ItemDefinitionBuilder.BuildSiteExpansion, icon 10464).

### L92 — castle designs: UNCHANGED (+consistency)

- The wiki "Modular Thinking" steps use the Modular Castle **Window** (three paste modes), **Long Room** and **Entry**
  designs, so the player must own them **before** that goal starts.
- The wiki's reward sections for Building Block Basics / Modular Thinking list only 30 Lumens + 5 score. That is the
  same omission as 471, whose capture gives 13 designs.
- This is consistent with the built choice (Achievements.Tutorial.cs:45-47, Building Block Basics gives 4098-4105).

### L100 — Spire raise: ADVANCED (major) [P data, I placement intent]

**The template's voxels [P].** Every cell blob of base_portal_spire.otd and portalspirebase.otd was decoded (320
cells, 310 × 170 × 310 voxels). Both are **identical in voxel content**:
- 16,337,000 voxels flagged 0x63 (material bit + **material 0** + centred vertex) = **air**;
- 202,291 vertex-only voxels (0x22): the whole y = 0 layer (96,721 = 311²) plus 621 per layer on the low x / z faces;
- **no solid voxel** (the material map u32 n = 0 in both).

By ruin-stamping.md §1 ("material 0 carves anything; a vertex-only voxel keeps what is underneath") the Spire
template is a **124 × 68 × 124 m clearing**. It carves everything above a flat floor at the box bottom.

**The models [P].**
- Theme_Widgets_Teleporters_PortalSpires01.dme (Assets_157.pack; Models 596 = prop 1871 Portal Spire (Hub)): header
  AABB x / z ±47.19 m, y **−20.94 … +43.74 m**.
- PortalSpiresBase01.dme (Models 612): y −20.94 … +0.45 m.

**Fit [P numbers].**
- The newer template puts the Hub at (60.1, **21.2**, 63.9) m from the corner, so the model's bottom is at
  21.2 − 20.94 = **0.26 m**, i.e. on the floor (the vertex border's surface ≈ 0.2 m).
- Its top is at 64.9 m, inside the 68 m box; its 94.4 m footprint is inside the 124 m box.
- The props stand on the deck 18.3-20.1 m up (Replicator, Repair Station, Parlor 18.3; Mailbox / Chest 20.1).
- So the box was cut as the Spire's own clearance volume, and **the Spire stands on the template floor** [I strong].
- Older template: Hub at 22.0, so its base is 1.06 m above the floor. Its props are 0.8 m higher.

**What the server does now [P code].** Spire.cs:233-237 puts the **Hub prop (21.2 m up) at the ground point**.
- corner.Y = round((ground − 21.2 m) / 16 m cell), i.e. the carve floor at ground − 21.2 m ± 8 m.
- The stamp then removes up to ~21-29 m of terrain inside the 124 m box. The Spire's 21 m plinth stands in a pit,
  with its deck at about the old ground level.
- The vertical error between deck and ground is up to ±8 m, because the cell-aligned Stamp is used, not the
  voxel-aligned StampAt.
- This likely explains the 10-11 report "travel landed under the platform … natural terrain below the stamp"
  (MODLOG 1171).

**P5647 [I].** "Raised slightly higher out of the ground" because grass overgrew the spires. That fits a Spire
standing *on* the ground with its base at grass level. A small upward offset is the raise. The post-7 Jun template
already sits 0.8 m lower on its floor than the pre-11 May one, so the raise must have been in live's placement.

Proposed change: §3 S2.

### L101 — UniqueEquip: CLOSE-BY-DESIGN [P]

- The 0.695 tooltip writer **FUN_1435176a0** (= 0.589 FUN_1434655a0; part_0108 28839-29210) adds one line per bit of
  a flag byte: 0x40 NoSale, 0x20 NoTrade, 0x10 RemovedOnUse, 0x08 NoDelete, 0x04 NoUpgrade, **0x02 UniqueEquip**,
  0x01 NoSalvage.
- Its caller **FUN_143768e20** (part_0120 ~24849) passes the tooltip object's bytes at +0x1c8, i.e. the tooltip
  reply's flags, not the item definition.
- With the refusal dialog opened only from TransferItemResponse 0x22, the client never decides uniqueness itself. The
  definition bit is data for the server.
- Already built that way: InventoryTooltipReply.cs:88-89, Inventory.DuplicatesUnique.

### L109 — Events world: UNCHANGED

- **[P]** AS3 TeleportSpireWidget:
  - worlds {Name, GUID, Claims, ClaimCapacity, CurrentWorld} (line 32), sorted by the Name string (line 120);
  - the current world is pre-selected by CurrentWorld == "1" (line 151).
- No id or text in any new source. The server's 3 / 567016 stays [I].

### L111 — Landmarks of Landmark: UNCHANGED

- LandmarksOfLandmark.cs places beside the Spire box. If S2 changes the Spire's height or box, re-check that
  placement (same terrain).

### L120 — cavern healers: UNCHANGED

- Of the 33 wiki creature pages with ability lists, only "Barren Lightbringer" has a heal ("Ability 1: Rejuvenate").
  It is already built (CreatureAbilities Role.Heal).
- The Scalewind / Ebonguard / Stoneskin healer pages are empty templates.

### Leads with nothing new

- **L10:** no duration near 574500 (text 574494-574506, StringHashToValue).
- **L13:** found, no change.
- **L24 / L25:** built with our ids and the emitter composites; no capture on disk.
- **L42:** capture-only.
- **L46:** nothing new.
- **L87:** no mail wording anywhere.
- **L89:** matches.
- **L90:** no numbers.
- **L93:** nothing new.
- **L95:** built.
- **L110:** user decision.
- **L112:** nothing new.
- **L119:** .mrn event tracks not decoded; no new numbers.
- **L131:** no post-7 Jun trip report naming a world (forum 2016-06…12 "crystal" + world names; wiki Lost Crystals /
  Chaos Caverns).

---

## 3. Server work

**S1 — IF Gate row labels (L19) [P].**
- Where: Game/PropSettings.cs `ifGateRows` (lines 160-165).
- Labels: 20101 "Property" → **"If"**; 20102 "Comparison" → **"Is"**.
- Keep 20103 as the slider. Its label "Value" stays ours [I].
- Evidence: forum 57987 #1 line 66.
- Test: the IF Gate window shows "If / Is / slider".

**S2 — Spire placement (L100, TASKS 6.49) [P geometry, I raise].** Where: Game/Spire.cs Ensure / Stamp path
(lines 233-246).
- Put the template **floor on the ground**: corner Y = ground − *raise* + 0. The Hub pivot then lands at
  ground + 21.2 m (+ raise), the deck at ground + ~18.3 m, and the plinth on the ground.
- Stamp voxel-aligned with `EnvironmentServer.Instance.StampTemplateAt` (IEnvironmentServer.cs:202 →
  VoxelTemplate.StampAt, the 0.4 m grid LandmarksOfLandmark.cs:252 uses), not the 16 m cell-aligned `StampTemplate`.
  That keeps the vertical error under 0.4 m.
- *raise* [I] = 0.8 m (two voxels: the older template's extra base clearance, "slightly higher out of the ground",
  P5647). Make it a constant with this note.
- Keep `MarkPermanent` / `Covers` / `PlatformBox` on the stamped cell set.
- Arrival and the Local Travel hub stay relative to `site.Position` (the Hub pivot), so they follow.
- `SpotFor`'s level test (corner heights within MaxStep) still applies. With the floor on the ground, hills inside the
  box are cut flat, as the template intends.
- **Migration:**
  - Landscapes already stamped have `spire_base*.txt` markers and permanent carved cells (the pit).
  - Accounts and terrain may be reset (memory: account-reset-planned). Otherwise delete those Spire cells' terrain
    edits and the marker before re-stamping, because the pit cannot be filled back from the template, which has no
    solids.
- Runtime check first (§4 T1).
- Also re-check LandmarksOfLandmark placement (L111) and graveyards that key off the Spire.

**S3 — Combat Node settings (L19) [P].** Where: Game/PropSettings.cs.
- Add `settingsByKind[StoryTools.CombatNodeProp] = { 31 }`. That is Combat Tactics, the existing SpawnerAi drop-down
  577230-577232 Standard / Stationary / Do Nothing, which StoryTools.cs:1228 / :1235 already read from the node.
- Once `StoryTools.CombatCreatureOf(node)` is set, append read-only rows "Ability N: <name>", N = 1 …
  `Creatures.StoryAbilityCount(creature)`.
  - Use 0.695 free string labels and the ReadOnly / readOnlyCaptions path used for row 106.
  - Names come from `Combat.CreatureAbilities.Of(kind)`; e.g. Barren Grunt "Ability 1: Slashing Blade 1".
  - The wiki creature pages use exactly that "Ability N: name" form [I format].
- 0.695 clients only (the Combat Node is 0.695).
- Evidence: P6150, P6152, P6157; wiki "Combat Node".

**S4 (optional, user decision) — procedural respawn model (L2 / L113) [P model, I numbers].**
- Where: Game/ProceduralPlacement.cs.
- Per (zone, set, entity): max = candidate nodes, min = ⌈⅔ max⌉. Start at a random count in [min, max].
- On harvest, respawn only while below min, after MIN..MAX_RESPAWN_MINUTES, at a random free node of that set on the
  same island (not in place). Restarts reset.
- Evidence: forum 55352 #1.
- Keep today's regrow-in-place as the default until the user picks.

**S5 (optional) — Lumen Station unlock purchases (L43) [I strong].**
- Where: Game/Crafting.TryStart.
- For a recipe with `VisibilityRequirementId != 0`, refuse (or warn) when the purchase would add nothing:
  - the output "Recipe:" item's grant is a prop / recipe the player already knows;
  - the Starter Set has already been crafted (only if Cry Havoc! needs it once [I]);
  - an expansion is at its cap.
- First run §4 T2: if the client already hides these rows, only the server-side refusal is needed for safety.

**S6 — comments / bookkeeping (no behaviour change):**
- Landscapes.cs:104-105: ImageId 10228 is the Images.txt id of back_depthSelector_tier1.dds (tier-N image =
  10227 + N) (L14).
- OPEN-LEADS:
  - L16: the 65532 part is FOUND (ruin-stamping.md §1);
  - L18: close (never shipped, options server-sent);
  - L101: close (client reads tooltip flag 0x02 only);
  - L45: four names resolved;
  - L14: TELEPORT_IMAGE_ID resolved;
  - L86: attached expansion not needed.
- re/out/agent_wiki/audit.py ALIASES: Dark Desert Succulent → 275, … With Butterflies → 392; Glowing Jungle (Triple)
  Pendant Plant, Bowl of Apples, Large Square Knotted Rope → "no prop in the live list".

---

## 4. Runtime tests wanted

- **T1 (S2, before changing Spire.cs):**
  - On <0.695 test copy>, travel to a non-home landscape's Spire. Stand on the deck and note the player Y.
  - Then walk or fly to just outside the 124 m box and note the ground Y.
  - Expected with the current code: deck ≈ outside ground ± 8 m, and a carved pit ~21 m deep between the plinth and
    the box wall.
  - After S2: the plinth base on the outside ground, the deck ~18-19 m above it, and no pit.
  - Screenshot both.
- **T2 (L43 reader):**
  - Hook FUN_143cb6e00 (RecipeDefinition loader) to log `param_1` for recipe id 2003 (Recipe: Hanging Lantern,
    VISIBILITY 787).
  - Set a Frida `MemoryAccessMonitor` (read) on `param_1 + 0x40` (4 bytes) of that object, then open the Lumen Station
    (Lua Recipe.RecipeListSelectStation 55).
  - The first reader's return address is the visibility check. Detach the monitor before closing (memory:
    frida-detach-crash; use the normal runner, never kill it).
  - Also check by eye whether "Recipe: Hanging Lantern" is listed for a character who knows prop "Hanging Lantern".
- **T3 (S1 / S3):**
  - Open the IF Gate settings and check the "If" / "Is" labels.
  - Open the Combat Node settings: first unlinked (Combat Tactics only), then linked to a Barren Grunt spawner. The
    ability rows should appear, and "Stationary" should be honoured on "…Set the combat tactics on this node's linked
    creature."

## 5. Hand-offs

- **Graveyard leads (L150, other group):** if S2 changes the Spire's vertical placement, any graveyard / respawn spot
  computed from the Spire box or ground under it moves too.
- **Group A / architecture (admin page 28 / L82):** procedural "queued" counts (AdminMonitor.cs:218 "nodes regrowing")
  would change meaning under S4 (pending respawns below minimum).
