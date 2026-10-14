# System: crafting, achievements, meta (portraits, reports, sharing, ratings), admin

Status: [~] map level.

## Crafting (0x42; Game/Crafting*.cs)
✔ RecipeStart, Cancel, salvaging start / add item, component image sets, repair, open crafting window, enable window.
✗ C→S RefiningRemoveProcess 0x11, RefiningAddFuel 0x12, RefiningCommand 0x14, RefiningApplyAdditive 0x16,
RefiningRemoveAdditive 0x17, RecipeUpgrade 0xA, RemoveUpgrade 0xC, UpgradeStat 0xE.
✗ S→C RefiningProcessResponse 0x10, RefiningStateResponse 0x13, PlayerRecipeDataUpdate 0x1E, Hide / UnhideRecipe
0x1F / 0x20, ZoneCraftingWindow 0x24, CraftWindowEnableResponse 0x7, AddRecipeResponse 0x8, UpgradeResponse 0xB,
RemoveUpgradeResponse 0xD; no-opcode classes Recipe*ResponsePacket (Cancel / Complete / RefiningOutput / RemoveRecipe /
Start / UpgradeStat) — find their family. Data: Recipes (17 cols), RecipeComponents, CraftingStations (10),
CraftingStationRecipeMappings, Catalyst mappings, CraftingCategoryGroup*, MaterialProgression*; refining station
GameData.Crafting.Refining.State (FuelCap, FuelPreview..., FuelRequired, FuelTime).

## Achievements (0x92) ✔ initialize, complete, objective activated / complete / update; ✗ AchievementAdd 0x2,
ObjectiveAdded 0x5, SecretComplete 0xE, GlobalAnnounce 0xF; AchievementTrackerInitialize (no opcode yet). Tracker
update ✔. Rewards (0x35) ✗ RewardBasePacket; RewardBundle sent inside encounters ✔. First-time events (0x6D) ✔.
Data: Achievement* tables (captured achievements.tsv).

## Portraits (0x98, 0x75) ✔ PlayerImageData, AddClientPortraitCrc; ✗ GeneratePortraitRequest 0x1, PortraitDataRequest
0x2 (received 206×!), SnapshotRequest 0x4 (S→C). Social share (0x104) ✗ provider status / publish. Report player (0x47)
✗, bug submission (0x55) ✗, claim report (0x56) ✗, LiveGamer post (0x48) ✗, WallOfData telemetry (0xCE) ✗,
client metrics (0x6B ✔ / 0x8B ✗), client log 0x6F (S→C ✗; C→S logs ✔).

## Ratings / contests (0xA7; Game/Ratings.cs, Contests) ✔ (check: stats mark CanVote / RequestContests / AddVote ✗
— sub encoding); CreateContestReply 0x1B ✗.

## Commerce (0x45) → docs/commerce.md; 0x45 open: preview order, country / state codes, bundle content, loyalty,
web auth URL, follow / unfollow, recommendations, search, screenshots; store bundles / gift / schedules / price
updates / nudge offers. Item review opt-in (0x10B) ✗. Commerce session (0x77 / 0x78) ✗.

## Admin / dev (0x1E ≥ 0x3EA, 0x1F, 0x49A, 0x4DA grief, 0x4E7 items, 0x4E8 currency, 0x100 debug draw, 0x109 / 0x10A
procedural monitor, 0x119 debug animation, 0xBB PunkBuster, 0xD6 security): not needed for players; GM tools later.

## Client constants (trace/client-constants.md)
Crafting: window closes at 10 m from the station, MaxCatalysts 3, MaxTrackedRecipes 4, player anims 265 / 266.
Recipe picker 4 slots. Templates (designs): MaxTotalStoredVoxelCount 6,000,000, MaxVoxelExpandable 48,000,000 (wiki:
+6M per 1040 Lumens). PlayerStudio.MaxListingValue 29600. Achievements: completion stagger 6 s, global announce
colour 40, delay 3000 ms.

## Live behaviour (wiki, launch 2016: Crafting, Crafting station, Replicator, Refining, Recipes, Lumens, Lumen Station, Composite Resources, Essence)
- **One crafting station: the Replicator** (refines raw resources, makes composites, consumables, all gear,
  tools and outfits). At every Spire, and placeable on any site you can build on for zero materials (two styles:
  table with claw arm, futuristic smelter). Anyone may craft; no professions. **Repair-o-Matic** repairs gear for
  Lumens (WIDGET_REPAIR); **Crimson Parlor** changes appearance for Lumens (WIDGET_BARBERSHOP,
  CharacterCustomization.ResourceId 1579). The old station series (forge, loom …) was retired before launch;
  0.589 data still has them (CraftingStations table) — check which ones the 0.589 Replicator lists.
- Crafting window: categories → recipe → description, output count, components "have / need" (need in red when
  short); quantity arrows (down = maximum makeable); "Watch" adds the recipe to the Goal / Watch list (max 4).
  Missing components may be bought (Recipe.SelectRecipe → PointOfNeed purchase, uimap GAPS). Window closes at 10 m.
- **Refining** multiplies: e.g. 50 gathered wood → 750 cut wood; refining needs a minimum batch. Wood → Cut wood,
  ore → Smelted metal, gems → Cut gems, uncommon stone → Cut stone; Essences, Tree Sap and Tree Fiber are used raw.
- **Composites:** metals (Smelted Solucite, Druidium, Indicite, Rubicite, Mithril, Etherium = two smelted metals),
  gems (Cut Skystone, Amethyn, Tormeral, Sunstone, Omenstone, Moonstone), textiles (9 cloths = tree fiber + cut gem),
  plastics (9 = tree sap + 1-2 cut gems), Lumicite (9 = uncommon stone + cut gem).
- **Essences** (for weapons, armour, accessories): basic Monster / Adventure Essence drop from monsters, Elite
  monsters (Outposts), chests, Lost Crystals, mining, logging; converted at the Replicator into affinity essences
  (Fire, Earth, Water, Lightning, Radiance, Affliction, Shadow).
- **Recipes:** innate set at start; others discovered (purple satchel + recipe card) from Journal goals, gathering,
  monsters, loot chests, Lost Crystals — added to the Replicator (gear) or Prop Palette (props) automatically; or
  bought with Lumens.
- **Lumens** (item 1579, not tradeable): earned from Journal goals, gathering, monsters, chests; bought with Daybreak
  Cash (inventory "Get more Lumens" → OpenMarketplaceTagSearch "Lumen"). Spent at the Lumen Station on recipes,
  resource bundles, outfits, gear, design storage; also revive in place (15), Crimson Parlor, repairs.
  The Lumen Station has no window of its own in 0.589's UI list — find it (MerchantWindow / CoinStore 0x9E?).
- Trading: right-click a player (or friend list) → trade window, target must accept; mailboxes at Spires or
  craftable; Shift-drag splits stacks.

## Live behaviour — Journal / goals, Showcase, Gallery, Player Studio (wiki, launch 2016)
- **Journal** (J; formerly Achievements): Goals tab + Recipes tab; goals are cards with steps; completion pops the
  card on screen; rewards items, stats and **Lumens**. Watch list (eye icon) tracks goals and recipes (component
  "have" counts); recipe count shows 0 until makeable. Categories (July 2016): Daily, Getting Started, Social,
  Building, Decorating, Combat, Exploration, Crafting, Recipe Discoveries; first goal "Your Home is Your Castle"
  (place the starter castle designs on the Pioneer site).
- **Showcase** (B or cart icon): Marketplace tab (Daybreak Cash: Lumens, extra build site slots, player designs;
  Player Studio inside, "My Store" for sellers / following creators), Gallery tab, Competition tab, My Store tab.
  $1 = 100 DC.
- **Gallery**: publishing = adding tags (the only required step) in Site Management; name (default "<name>'s Build
  site #1"), welcome message ("Welcome!" default; shown and searchable in the Gallery), up to 4 screenshots (taken
  only inside the site; Gallery image may take an hour to update). Visitors search by preset tags / message text,
  sort, and travel there.
- **Player Studio**: designs are sold through the Marketplace; seller registration (W2); submission: up to 4
  screenshots + description; **review by randomly chosen players who have spent ≥ 1000 DC**, creator name hidden,
  thumbs up / down on two rules only (no copyright violation, not offensive); a rejection goes to Daybreak staff,
  reviewers who reject on taste stop getting reviews. Price ≥ 25 SC, $1 submission fee (store is free on our
  server). Purchased designs: reusable, editable, not resellable (derived submissions split royalties by block
  ownership), not copyable by visitors, not mailable.
- **Goal rewards** (trace/wiki-goals.md, 184 goals from the wiki): Lumens + "Luminary Score" points (+ titles, e.g.
  "Becoming a Luminary": 30 Lumens, 5 score, title Luminary; building goals 15 / 35 / 80 Lumens with 5 / 10 / 15
  score by size). **Daily goals**: reset every 24 h (progress carries over until done), 30-45 Lumens each; kinds:
  Cavern Crawler (5 Lost Crystals), Shrewd Miner (16000 ore / gems / stone), Lucky Logger (4000 wood), On the
  Warpath (50 kills), faction hunts (15 Scalewind / Stoneskin / Highfall / Ebonguard / Barren), affinity kills (25
  with a fire / radiance / shadow / affliction / earth / lightning / water weapon).

## Deep trace — Progression (rebuild module 11, 2026-10-14; archive progression-v1)
Packets: all reachable ones handled (REACHABLE-GAPS; PlayerTitleRequestSelect handled). The work list was the
objectives the server never counted. Every objective text in the live capture (re/out/achievements.tsv, 898
achievements: 569 Recipe Discoveries, Replicator lists, Decorating, Social 29, Combat 21, Building 16, Crafting 15,
Exploration 14, Getting Started 5, Daily 2) was checked against the server's triggers. ADDED (all by the client's own
objective text, so every tier with that text counts):
- Monsters killed (all tiers, not only 1016), "<Family> killed" (Barren / Scalewind / Highfall / NovaTech /
  Ebonguard / Stoneskin), "Dispatch explode-y mushrooms" (Fiery Fungus), "Kills with a(n) <Element> weapon" (held
  weapon affinity; Physical = Earth, as the daily wielders).
- Guild joined (join or found), Give or receive a Design in trade, Treasure Chests opened (cave chests), Design item
  created (design → Player Design item), Build Site "Like" given or received (voter and online owner), Build Sites
  visited / 2 unique Build Sites visited (first visit per site per day, as Build Site Tourist), Scan completed
  (Holoseeker overlay shown), Extractor acquired (an Extractor crafted), plural crafted goals ("Lightstones crafted").
Still not counted (no 0.589 trigger known): "Like" a <category> Build Site (sites carry no category in 0.589 — the
management record's CategoryName is never set by any UI), the emote challenges 492-503 ("Get loopy with the cacti"
… — which emote and where is not in the client), Boom knowledge obtained, Adventure landscape Build Site claimed,
Venison / Revenge / bad match / Clothing malfunction (event-specific), Breeze Boots.
Titles, first-time events, Pioneer start sites and character resources: already traced against the client (headers
of Titles.cs / FirstTimeEvents.cs / Pioneer.cs / CharacterResources.cs); no divergence found.

## Deep trace — Commerce & store (rebuild module 12, 2026-10-14; archive commerce-v1)
- Packets: every reachable InGamePurchase / ClaimVendor / Rating request handled (REACHABLE-GAPS 0; claim-vendor
  results 1/2/3 match FUN_142d…; contest CanVote reply layout fixed 10-12 by the s2cprobe).
- Template **instances** (the open item from building.md): placing a design "as an instance" is a VoxelConstruction
  option (S→C 0xB AdminForceInstancesSetting "Place all templates as instances", FUN_1430b7bf0); an instance's voxels
  are marked in the terrain cell's ownership grid (v4: optional i64 instance list + u16 list before the voxel
  indices, FUN_14307f3a0 / FUN_14307f2b0 — written empty by the server), VoxelTemplateLookup::AddTemplateInstance
  (FUN_143fa49c0) indexes them, ClientTemplateManager::PopulateClaimInstanceDataToOwnershipDS (FUN_1434892a0 /
  FUN_14348add0) lists a site's instances for Voxel Vision (Lua VoxelOwnership.PurchasedTemplate*) and asks
  GetInstanceDetailsRequest (0xF0/0x12) for unknown ones. AddOwnershipResponse (0xF0/0x11, FUN_143061f90) only prints
  "Learned.Template". Live used instances for PURCHASED (Player Studio) designs so Voxel Vision can credit their
  creator. NOT BUILT: needs instance ids in the terrain ownership grid (module 1 format change) and a Player Studio
  purchase flow (the server's store hands out client design items; player-made products are submitted and reviewed
  but nobody can buy them). Logged in TESTING-NEEDED as a known gap.
