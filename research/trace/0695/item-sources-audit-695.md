# Item sources: can every wiki item be obtained the way the wiki says? (agent, 2026-10-16)

TASKS §5 "Content pages: confirm every matched item can be obtained the way the wiki says (innate, discovered, Lumen
Station, drop, Marketplace)". One row per wiki content page that has a client item (trace/0695/wiki-xref.tsv,
client_item ≠ "-"): the wiki's route, the route in the client's own data, the route the server gives, and a verdict.
Builds on trace/0695/wiki-content-audit.md (agent #9, 2026-10-15, 0.589 tables and the server as it was then: "is it
in the data and obtainable at all") — this pass is the 0.695 data and today's server, and it checks the **route**.

Evidence marks: **[P]** client table / live capture (gameData695 = the 0.695 client tables the server reads, the item
list 44-CommandBase = gameData695/items.json, the goal capture 71-AchievementBase), **[W]** wiki page
(design/wiki/pages/<title>.wiki) or patch note (trace/0695/patchnotes.tsv), **[I]** inferred. Where the wiki and the
client data disagree, the client data wins (BUILD-PATH rule: developer data over the wiki, which mixes beta,
launch and later values) and the row says "different route".

Scripts (re-run after server changes): re/out/agent_item_sources/ — wikiroutes.py (wiki claims), audit695.py (client
routes + a model of every server item source with its code citation), verdict695.py (→ item-sources.tsv), gendoc.py
(→ the §4 tables).

## 1. How each route was checked

- **Wiki [W]**: the infobox "Innate" Yes / No (the "Innate Props / Creature Props / Special Effects Props (List)" pages
  override a page's own No), "This prop is (not) innate", the [[Crafting Station]] cell, "Purchased at the [[Lumen
  Station]] for N [[Lumens]]", "It must be discovered … or added using the Lumen Station", "recipe can be looted while
  gathering / purchased for N Lumens", "This recipe is a reward for the Combat Goal …", Marketplace / Showcase,
  drop / chest / Lost Crystal / mined / logged / seasonal mentions. Every sentence in the 1,410 pages that names a way
  to get the item was listed and read (wikiroutes.py + the phrase sweep in this session).
- **Client data [P]**: Recipes rows whose output is the item (station by CraftingStationRecipeMappings, DEFAULT_LEARNED
  = innate, the Lumen cost = RecipeComponents item 1579), the "Recipe:" items that teach it by their record's grantId
  (type 6 → a Recipes row, type 45 → a PropDefs row; RecipeBook.cs class notes), whether that recipe item is itself sold
  at the Lumen Station (station 55: 948 recipes, all default-learned, all priced in Lumens — 292 outfits, 520 prop
  recipe items, 47 gear recipe items, 69 refined materials, packs / chests / expansions), goal reward items in the
  capture and the server's built tiers (Achievements.Tiers.cs).
- **Server**: every way the code puts an item in a bag (audit695.py ROUTES, each cited): starter kit
  (Inventory.cs:58-66), ore veins (EnvServer OreVeins.itemByOre; Abilities.cs:782-787), digging (TerrainMaterials 44 / 45 /
  46 / 76), trees (Gathering.cs:132,176-182,281-292), gathering finds (Gathering.RollDrops: Lumens, Adventure Essence,
  a random unknown "Recipe:" item, fireworks emitters, Repair Bot Beacon), creature loot (Creatures.cs Kind.Loot),
  cave chests (LootChests.cs:176-183), Lost Chest / Lost Crystal (Outposts.cs:604-613, 674-688, RareDrops hats /
  seasonal outfits), Reward Chests (RewardChests.cs loot tables), goal rewards (Achievements.Complete), salvaging,
  bag unpacks; crafting where the recipe is known (default-learned, or taught by a recipe item the server hands out)
  at a station the player can reach (Replicator at the Spires, the Lumen Station from the UI, and the innate station
  props: Rusty Sifter (Small) 28, Rusty Workshop 30, Rusty Salvager (Small) 41, Stone Forge 48, Resource Reclamation
  Chest 54 — CraftingStations.Build) and only for station-mapped recipes (the client lists only records
  CraftingStations.BuildRecipeRecords sends); prop knowledge by RecipeBook.KnowsProp; the free Showcase store
  (Store.Catalogue) is listed but never counted as the wiki's route. A fixed-point closure checks that the crafting
  components are themselves obtainable.
- **Same-name items**: where wiki-xref matched an old item and another item of the same name has the wiki's route,
  the second is used and the row says so (Colonist's Elegant Robe (Green) 2551 → 4162, (Teal) 2631 → 4165,
  Falchion of the Inner Spark 1503 → 3763, Unstable Laboratory 156 → 2456).

## 2. Summary

| Kind | Pages | as wiki | as client (wiki silent) | different route | not obtainable | n/a |
|---|---|---|---|---|---|---|
| resource | 84 | 83 | 0 | 0 | 0 | 1 |
| weapon | 62 | 61 | 0 | 1 | 0 | 0 |
| tool | 22 | 11 | 11 | 0 | 0 | 0 |
| consumable | 8 | 4 | 2 | 1 | 1 | 0 |
| outfit | 319 | 315 | 0 | 2 | 2 | 0 |
| creature | 68 | 57 | 0 | 11 | 0 | 0 |
| prop | 847 | 825 | 4 | 18 | 0 | 0 |
| **all** | **1410** | **1356** | **17** | **33** | **3** | **1** |

Verdicts: **as wiki** — the server gives the item by the route(s) the wiki names, and the client data agrees;
**as client (wiki silent)** — the page names no route (tools and a few props), the server follows the client data;
**different route** — the wiki names a route the 0.695 client data does not have; the server follows the client data
(§3.2); **not obtainable** — neither the 0.695 client data nor the server has a route (§3.3); **n/a** — an overview page.

Content pages **with no client item** (wiki-xref client_item "-", the separate TASKS §4 list; only counted here):
system 282, meta 226, goal 184, prop 94, list 73, resource 26, outfit 17, weapon 8, consumable 7, pet 6, tool 4, creature 3 = 930. Two of the "system" pages are really items: Dragonfire Ritual Robes and Tectonic Destroyer's Platemail
(the 500-kill tier recipe rewards, recipe items 4023 / 4026) — their pages have no category, so wikixref filed them as
systems; both are given by the server's tiers (Achievements.Tiers.cs).

## 3. Findings

### 3.1 Fixed in the server (2026-10-16)

1. **Goal-only recipe items dropped at random.** RecipeBook.RandomUnknownRecipeItem (gathering finds, Lost Chests,
   Lost Crystals, Reward Chests) offered every "Recipe:" item, including the eight a goal gives: 3906 "Recipe: The
   Outcast" (goal 561 The Bigger They Are, capture [P]) and the 500-kill tier rewards 3990 Overcharged Recon Gear,
   4003 Reinforced Coat of Crashing Waves, 4006 Desperado's Deadly Poncho, 4007 Lightbringer's Oath, 4008 Silent
   Shadow's Cloak, 4023 Dragonfire Ritual Robes, 4026 Tectonic Destroyer's Platemail (Achievements.Tiers.cs). The wiki
   gives them as goal rewards only ("This recipe is a reward for the Combat Goal Seismically Snuffed Out"; "Recipe is
   an achievement reward") [W]; the 11 May 2016 notes moved the recipes these goals used to give back to "loot drops
   and via the lumen station" (P5327-P5332) [W]; and the 0.695 Lumen Station sells none of the eight while it sells the
   other 47 gear recipe items and 520 prop recipe items [P]. Now: Achievements.IsRewardItem (every goal's reward items,
   capture + dailies + tiers, filled at load) and RecipeBook.DropsAsRecipe keep them out of random finds.
2. **Retired wisp recipes dropped at random.** "Recipe: Black Wisp / Elite Wisp Monarch / Red Wisp / Elite Volatile
   Wisp / Elite Burster Wisp" (3931, 3956-3958, 3977) are the only creature recipe items the 0.695 Lumen Station does
   not sell — their recipes 2394-2397 / 2416 lost the station mapping [P] — and the wisps were removed on 26 Apr 2016
   ("Wisps have been removed as they continued to refuse to fly", P4704) [W]. Now never dropped (owned copies still
   teach). No wiki page (they are not content pages); found while checking which recipe items have no route.

Files: LandmarkEmulator.WorldServer/Game/RecipeBook.cs (DropsAsRecipe, RetiredRecipeItems), Game/Achievements.cs
(rewardItems, IsRewardItem). Build: 0 errors.

### 3.2 Different route: the client data over the wiki (no change)

| Pages | Wiki [W] | Client data [P] / server | Why the client wins |
|---|---|---|---|
| 12 Barren creature props (Bowman, Coldmage, Fireshot, Flameblade, Flamemage, Grunt, Icebrute, Lightbringer, Protector, Shadowshot, Stormmancer, Stormshot) | Innate: No | no "Recipe:" item in the live item list grants props 2445-2459 (only the four Elite Barren recipes 3561-3564 exist and are sold); innate on the server | The client text does hold "Recipe: Barren Bowman" … "Recipe: Barren Stormshot" names and "This recipe will teach you how to place …" descriptions (574376-574399, in 0.589 and 0.695) [P], so the recipe items were authored, but no item record of the Feb 2017-era list uses them, and Getting Started goal 478 Cry Havoc! asks to "Place the Barren Flameblade character from your Props tab" [P]. Lead L44 updated. |
| 15 props: Bouquet of Lillies, Floating Handle, Intricate Wooden Door (Left / Right), Stemmed Goblet (Large), Rusty Sifter (Medium), Metal Switch (on an Innate list), Fire Plume Type 2 / Lava Fire / Magic Fire (Red) / Magic Fire (Small Yellow) / Shadow Lamp / Torch Fire (Pink / Purple / Yellow) Mote Emitters | Innate | a "Recipe:" item grants the prop (grantId) and is sold at the Lumen Station (65-130 Lumens); server: discovered (drop + Lumen Station) | grantId is the live record [P]; the wiki's innate flags mix beta values (wiki-content-audit.md §2.9) |
| Iron Portcullis, Void Vault | Innate: No | no recipe item, no non-default recipe: innate on the server | Iron Portcullis was a 2014 giveaway-only item (P1138-P1147) and the Void Vault a Trailblazer Founder's Pack item (wiki Founder's Packs); the client data makes them ordinary palette props. Entitlements: TASKS 6.59. (Unstable Laboratory: the wiki's xref item 156 is innate, the same-name prop item 2456 is discovered as the wiki says.) |
| Artisan's Outfit (Emerald), Traveler's Attire (Emerald) | Replicator | only recipe 516 / 812 at the Workshop (station 30, default-learned); server: craftable at a placed Rusty Workshop | the Emerald colour has no Replicator recipe in 0.695 [P] |
| Potion of Minor Yield, Plate of Unrelenting Vengeance | Not innate | default-learned Replicator recipes 742 / 1341 | client tables [P] |
| (also counted "as wiki" with a note) Overcharged Recon Gear | Innate | recipe 1101 taught by 3990, a goal reward (1.21 ...Something, tier) | the page's own text says "This recipe is a reward for the Combat Goal" [W] |
| (as client) Bottled Moonlight, Spirit of the Wolf | Alchemy Station | Replicator recipes 746 / 747 | the Alchemy Station is 2014 (P1110-P1118) |
| (as wiki, price note) NovaTech Extractor / Harvestron / Holoseeker / Velocitizer | recipe looted or bought for 1040 Lumens | recipe items 4347-4350 at the Lumen Station for 780 Lumens | client price [P] |
| (as wiki, note) Rancher's Outfit (Black / Blue / Orange / Red) | Lumen Station 520; beta Marketplace crate bonus | Lumen Station 520 [P]; also in the free Showcase | the Marketplace mention is a beta bonus |
| (as client) Delver's Grappling Hook | "loot-only from cave chests during closed beta" | default-learned Replicator / Forge recipe 307 | client tables [P] |
| (as client) Explorer / Settler / Trailblazer Flag, Year Zero Trophy | Founder's Pack / Year Zero mail (wiki Founder's Packs, Year Zero Trophy) | innate props for everyone (their Workshop recipes 271-273 are never taught) | entitlements: TASKS 6.59 |

All 290 Lumen Station outfit prices on the wiki (520 Lumens) match the client's recipes; all 448 discovered props /
creatures are both random finds and Lumen Station purchases on the server, as the wiki's "discovered … or added using
the Lumen Station" says.

### 3.3 Not obtainable (the 0.695 client data has no route either)

- **Sharp-Eyed Elixir** (1100): a 2014 Alchemy Station potion (P1111); recipe 741 has no station and is not
  default-learned in 0.695 [P].
- **Colonist's Patrol Armor (Purple / Orange)** (4153 / 4154): 0.695 removed the Lumen Station mapping of recipes 5111 /
  5112 (items-695.md, tables diff) [P]; the wiki's 520 Lumens is the 0.589-era route. Only the free Showcase lists them.

### 3.4 Gaps that need a missing feature or a decision (TASKS §6)

- **6.59 Account entitlements** (Founder's Packs, Year Zero, giveaways): the wiki's only route for the Trailblazer /
  Explorer / Settler flags, Void Vault, Mastercraft Bracer, Ring of Bounty, Founder's Pickaxe, Mega Pocket, Noble's /
  Courtier's Regalia, Tech Commander's / Sergeant's Gear, the Founder title and the Year Zero Trophy / outfits.
- **6.60 Showcase catalogue vs the live Marketplace**: the free Showcase (Store.Catalogue) hands out every outfit (the
  290 Lumen Station outfits) and every categorised prop (incl. the 448 discovered ones), a route the wiki names for none
  of them; decide what the store lists and what it costs.
- Open lead **L108**: where the four unmapped weapon recipe items came from (3996 Katana of Crashing Waves, 3999
  Desperado's Deadly Arsenal, 4000 Lightbringer's Justice, 4001 Silent Shadow's Condemnation; recipes 2450 /
  2453-2455 lost their Lumen Station mapping like the wisp and Patrol Armor ones). Kept as random finds meanwhile.

## 4. Per page

### 4.1 Resources (84)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| Adventure Essence | 3683 | crystal | r2356@Lumen Station (innate, 130 Lumens) | gathering drops; creature loot; outpost Lost Chest; Lost Crystal; craft r2356@Lumen Station | as wiki |  |
| Affliction Essence | 1588 | innate; Replicator | r1740@Replicator (innate); r2357@Lumen Station (innate, 130 Lumens) | creature loot; Lost Crystal; reward chest loot; craft r1740@Replicator; craft r2357@Lumen Station | as wiki |  |
| Agate | 51 | mined |  | mining ore veins; creature loot; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Alabaster | 50 | mined |  | mining ore veins; reward chest loot | as wiki |  |
| Amaranthine | 48 | mined |  | mining ore veins; reward chest loot | as wiki |  |
| Black Cloth | 3700 | mined; logged | r2373@Replicator (innate); r2376@Lumen Station (innate, 130 Lumens) | craft r2373@Replicator; craft r2376@Lumen Station | as wiki |  |
| Black Plastic | 2492 | mined; logged | r1660@Replicator (innate); r1669@no station mapping (never taught); r1678@no station mapping (never taught); r1687@Workshop (innate); r1936@Lumen Station (innate, 130 Lumens) | craft r1660@Replicator; craft r1687@Workshop; craft r1936@Lumen Station | as wiki |  |
| Blue Cloth | 788 | mined; logged | r497@Workshop (innate); r1471@Replicator (innate); r1476@no station mapping (never taught); r1482@no station mapping (never taught); r1909@Lumen Station (innate, 130 Lumens) | craft r497@Workshop; craft r1471@Replicator; craft r1909@Lumen Station | as wiki |  |
| Blue Lumicite | 926 |  | r648@Replicator (innate); r657@no station mapping (never taught); r665@no station mapping (never taught); r1692@Forge (innate); r1922@Lumen Station (innate, 130 Lumens) | craft r648@Replicator; craft r1692@Forge; craft r1922@Lumen Station | as wiki |  |
| Blue Plastic | 2488 | mined; logged | r1656@Replicator (innate); r1665@no station mapping (never taught); r1674@no station mapping (never taught); r1683@Workshop (innate); r1935@Lumen Station (innate, 130 Lumens) | craft r1656@Replicator; craft r1683@Workshop; craft r1935@Lumen Station | as wiki |  |
| Burled Wood | 72 | logged |  | trees (axe); reward chest loot | as wiki |  |
| Copper Ore | 34 | mined |  | mining ore veins; creature loot; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Cut Agate | 1747 | mined | r1309@Forge (innate); r1558@Replicator (innate); r1585@no station mapping (never taught); r1612@no station mapping (never taught); r1895@Lumen Station (innate, 130 Lumens) | craft r1309@Forge; craft r1558@Replicator; craft r1895@Lumen Station | as wiki |  |
| Cut Alabaster | 124 | mined | r51@Forge (innate); r1553@Replicator (innate); r1580@no station mapping (never taught); r1607@no station mapping (never taught); r1883@Lumen Station (innate, 130 Lumens) | craft r51@Forge; craft r1553@Replicator; craft r1883@Lumen Station | as wiki |  |
| Cut Amaranthine | 122 | mined | r49@Forge (innate); r1551@Replicator (innate); r1578@no station mapping (never taught); r1605@no station mapping (never taught); r1881@Lumen Station (innate, 130 Lumens) | craft r49@Forge; craft r1551@Replicator; craft r1881@Lumen Station | as wiki |  |
| Cut Amethyn | 1755 | mined | r1315@Forge (innate); r1564@Replicator (innate); r1591@no station mapping (never taught); r1618@no station mapping (never taught); r1901@Lumen Station (innate, 130 Lumens) | craft r1315@Forge; craft r1564@Replicator; craft r1901@Lumen Station | as wiki |  |
| Cut Burled Wood | 2001 | logged | r1326@Workshop (innate); r1575@Replicator (innate); r1602@no station mapping (never taught); r1629@no station mapping (never taught); r1907@Lumen Station (innate, 130 Lumens) | cave treasure chest; outpost Lost Chest; craft r1326@Workshop; craft r1575@Replicator; craft r1907@Lumen Station | as wiki |  |
| Cut Diamond | 1751 | mined | r1313@Forge (innate); r1562@Replicator (innate); r1589@no station mapping (never taught); r1616@no station mapping (never taught); r1899@Lumen Station (innate, 130 Lumens) | craft r1313@Forge; craft r1562@Replicator; craft r1899@Lumen Station | as wiki |  |
| Cut Emerald | 1749 | mined | r1312@Forge (innate); r1561@Replicator (innate); r1588@no station mapping (never taught); r1615@no station mapping (never taught); r1898@Lumen Station (innate, 130 Lumens) | craft r1312@Forge; craft r1561@Replicator; craft r1898@Lumen Station | as wiki |  |
| Cut Marble | 121 | mined | r48@Forge (innate); r1550@Replicator (innate); r1577@no station mapping (never taught); r1604@no station mapping (never taught); r1880@Lumen Station (innate, 130 Lumens) | craft r48@Forge; craft r1550@Replicator; craft r1880@Lumen Station | as wiki |  |
| Cut Moonstone | 1757 | mined | r1319@Forge (innate); r1568@Replicator (innate); r1595@no station mapping (never taught); r1622@no station mapping (never taught); r1905@Lumen Station (innate, 130 Lumens) | craft r1319@Forge; craft r1568@Replicator; craft r1905@Lumen Station | as wiki |  |
| Cut Obsidian | 123 | mined | r50@Forge (innate); r1552@Replicator (innate); r1579@no station mapping (never taught); r1606@no station mapping (never taught); r1882@Lumen Station (innate, 130 Lumens) | craft r50@Forge; craft r1552@Replicator; craft r1882@Lumen Station | as wiki |  |
| Cut Omenstone | 1753 | mined | r1318@Forge (innate); r1567@Replicator (innate); r1594@no station mapping (never taught); r1621@no station mapping (never taught); r1904@Lumen Station (innate, 130 Lumens) | craft r1318@Forge; craft r1567@Replicator; craft r1904@Lumen Station | as wiki |  |
| Cut Plain Wood | 2000 | logged | r1325@Workshop (innate); r1574@Replicator (innate); r1601@no station mapping (never taught); r1628@no station mapping (never taught); r1906@Lumen Station (innate, 130 Lumens) | cave treasure chest; outpost Lost Chest; craft r1325@Workshop; craft r1574@Replicator; craft r1906@Lumen Station | as wiki |  |
| Cut Ruby | 1750 | mined | r1311@Forge (innate); r1560@Replicator (innate); r1587@no station mapping (never taught); r1614@no station mapping (never taught); r1897@Lumen Station (innate, 130 Lumens) | craft r1311@Forge; craft r1560@Replicator; craft r1897@Lumen Station | as wiki |  |
| Cut Sapphire | 1748 | mined | r1310@Forge (innate); r1559@Replicator (innate); r1586@no station mapping (never taught); r1613@no station mapping (never taught); r1896@Lumen Station (innate, 130 Lumens) | craft r1310@Forge; craft r1559@Replicator; craft r1896@Lumen Station | as wiki |  |
| Cut Skystone | 1752 | mined | r1314@Forge (innate); r1563@Replicator (innate); r1590@no station mapping (never taught); r1617@no station mapping (never taught); r1900@Lumen Station (innate, 130 Lumens) | craft r1314@Forge; craft r1563@Replicator; craft r1900@Lumen Station | as wiki |  |
| Cut Stone | 911 | mined | r634@no station mapping (innate); r1557@no station mapping (innate); r1584@no station mapping (never taught); r1611@no station mapping (never taught) |  | n/a (overview page) | class page for Cut Marble / Alabaster / Obsidian / Amaranthine (each as wiki); item 911 is the pre-launch "Cut Stone" whose recipes have no station mapping |
| Cut Striped Wood | 2002 | logged | r1327@Workshop (innate); r1576@Replicator (innate); r1603@no station mapping (never taught); r1630@no station mapping (never taught); r1908@Lumen Station (innate, 130 Lumens) | cave treasure chest; outpost Lost Chest; craft r1327@Workshop; craft r1576@Replicator; craft r1908@Lumen Station | as wiki |  |
| Cut Sunstone | 1754 | mined | r1317@Forge (innate); r1566@Replicator (innate); r1593@no station mapping (never taught); r1620@no station mapping (never taught); r1903@Lumen Station (innate, 130 Lumens) | craft r1317@Forge; craft r1566@Replicator; craft r1903@Lumen Station | as wiki |  |
| Cut Tormeral | 1756 | mined | r1316@Forge (innate); r1565@Replicator (innate); r1592@no station mapping (never taught); r1619@no station mapping (never taught); r1902@Lumen Station (innate, 130 Lumens) | craft r1316@Forge; craft r1565@Replicator; craft r1902@Lumen Station | as wiki |  |
| Diamond | 59 | mined |  | mining ore veins; reward chest loot | as wiki |  |
| Dirt | 44 | mined | r2378@Lumen Station (innate, 130 Lumens) | digging ground; creature loot; reward chest loot; craft r2378@Lumen Station | as wiki |  |
| Earth Essence | 1587 | innate; Replicator | r1739@Replicator (innate); r2361@Lumen Station (innate, 130 Lumens) | Lost Crystal; reward chest loot; craft r1739@Replicator; craft r2361@Lumen Station | as wiki |  |
| Emerald | 56 | mined |  | mining ore veins; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Fire Essence | 1589 | innate; Replicator | r1741@Replicator (innate); r2358@Lumen Station (innate, 130 Lumens) | creature loot; Lost Crystal; reward chest loot; craft r1741@Replicator; craft r2358@Lumen Station | as wiki |  |
| Gold Ore | 39 | mined |  | mining ore veins; creature loot; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Gray Plastic | 2485 | mined; logged | r1653@Replicator (innate); r1662@no station mapping (never taught); r1671@no station mapping (never taught); r1680@Workshop (innate); r1930@Lumen Station (innate, 130 Lumens) | craft r1653@Replicator; craft r1680@Workshop; craft r1930@Lumen Station | as wiki |  |
| Green Cloth | 1118 | mined; logged | r501@Workshop (innate); r1478@no station mapping (never taught); r1484@no station mapping (never taught); r1830@Replicator (innate); r1912@Lumen Station (innate, 130 Lumens) | craft r501@Workshop; craft r1830@Replicator; craft r1912@Lumen Station | as wiki |  |
| Green Lumicite | 925 |  | r647@Replicator (innate); r656@no station mapping (never taught); r664@no station mapping (never taught); r1691@Forge (innate); r1925@Lumen Station (innate, 130 Lumens) | craft r647@Replicator; craft r1691@Forge; craft r1925@Lumen Station | as wiki |  |
| Green Plastic | 2489 | mined; logged | r1657@Replicator (innate); r1666@no station mapping (never taught); r1675@no station mapping (never taught); r1684@Workshop (innate); r1931@Lumen Station (innate, 130 Lumens) | craft r1657@Replicator; craft r1684@Workshop; craft r1931@Lumen Station | as wiki |  |
| Indigo Lumicite | 927 |  | r649@Replicator (innate); r658@no station mapping (never taught); r666@no station mapping (never taught); r1693@Forge (innate); r1923@Lumen Station (innate, 130 Lumens) | craft r649@Replicator; craft r1693@Forge; craft r1923@Lumen Station | as wiki |  |
| Iron Ore | 36 | mined |  | mining ore veins; creature loot; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Lightning Essence | 1593 | innate; Replicator | r1745@Replicator (innate); r2360@Lumen Station (innate, 130 Lumens) | Lost Crystal; reward chest loot; craft r1745@Replicator; craft r2360@Lumen Station | as wiki |  |
| Marble | 47 | mined |  | mining ore veins; reward chest loot | as wiki |  |
| Obsidian | 49 | mined |  | mining ore veins; reward chest loot | as wiki |  |
| Orange Cloth | 786 | mined; logged | r495@Workshop (innate); r1470@Replicator (innate); r1475@no station mapping (never taught); r1481@no station mapping (never taught); r1911@Lumen Station (innate, 130 Lumens) | craft r495@Workshop; craft r1470@Replicator; craft r1911@Lumen Station | as wiki |  |
| Orange Lumicite | 923 |  | r645@Replicator (innate); r654@no station mapping (never taught); r662@no station mapping (never taught); r1689@Forge (innate); r1921@Lumen Station (innate, 130 Lumens) | craft r645@Replicator; craft r1689@Forge; craft r1921@Lumen Station | as wiki |  |
| Orange Plastic | 2490 | mined; logged | r1658@Replicator (innate); r1667@no station mapping (never taught); r1676@no station mapping (never taught); r1685@Workshop (innate); r1934@Lumen Station (innate, 130 Lumens) | craft r1658@Replicator; craft r1685@Workshop; craft r1934@Lumen Station | as wiki |  |
| Pink Cloth | 3699 | mined; logged | r2372@Replicator (innate); r2375@Lumen Station (innate, 130 Lumens) | craft r2372@Replicator; craft r2375@Lumen Station | as wiki |  |
| Pink Lumicite | 981 |  | r672@Replicator (innate); r673@no station mapping (never taught); r674@no station mapping (never taught); r1696@Forge (innate); r1927@Lumen Station (innate, 130 Lumens) | craft r672@Replicator; craft r1696@Forge; craft r1927@Lumen Station | as wiki |  |
| Plain Wood | 71 | logged |  | trees (axe); reward chest loot | as wiki |  |
| Purple Cloth | 790 | mined; logged | r499@Workshop (innate); r1469@Replicator (innate); r1477@no station mapping (never taught); r1483@no station mapping (never taught); r1910@Lumen Station (innate, 130 Lumens) | craft r499@Workshop; craft r1469@Replicator; craft r1910@Lumen Station | as wiki |  |
| Purple Plastic | 2491 | mined; logged | r1659@Replicator (innate); r1668@no station mapping (never taught); r1677@no station mapping (never taught); r1686@Workshop (innate); r1937@Lumen Station (innate, 130 Lumens) | craft r1659@Replicator; craft r1686@Workshop; craft r1937@Lumen Station | as wiki |  |
| Radiance Essence | 1591 | innate; Replicator | r1743@Replicator (innate); r2362@Lumen Station (innate, 130 Lumens) | Lost Crystal; reward chest loot; craft r1743@Replicator; craft r2362@Lumen Station | as wiki |  |
| Red Cloth | 793 | mined; logged | r502@no station mapping (never taught); r1472@Replicator (innate); r1473@no station mapping (never taught); r1479@no station mapping (never taught); r1485@no station mapping (never taught); r1826@Workshop (innate); r1913@Lumen Station (innate, 130 Lumens) | craft r1472@Replicator; craft r1826@Workshop; craft r1913@Lumen Station | as wiki |  |
| Red Lumicite | 920 |  | r644@Replicator (innate); r653@no station mapping (never taught); r661@no station mapping (never taught); r1688@Forge (innate); r1924@Lumen Station (innate, 130 Lumens) | craft r644@Replicator; craft r1688@Forge; craft r1924@Lumen Station | as wiki |  |
| Red Plastic | 2487 | mined; logged | r1655@Replicator (innate); r1664@no station mapping (never taught); r1673@no station mapping (never taught); r1682@Workshop (innate); r1938@Lumen Station (innate, 130 Lumens) | craft r1655@Replicator; craft r1682@Workshop; craft r1938@Lumen Station | as wiki |  |
| Ruby | 58 | mined |  | mining ore veins; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Sand | 45 | mined | r2379@Lumen Station (innate, 130 Lumens) | digging ground; reward chest loot; craft r2379@Lumen Station | as wiki |  |
| Sapphire | 57 | mined |  | mining ore veins; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Shadow Essence | 1592 | innate; Replicator | r1744@Replicator (innate); r2363@Lumen Station (innate, 130 Lumens) | Lost Crystal; reward chest loot; craft r1744@Replicator; craft r2363@Lumen Station | as wiki |  |
| Silver Ore | 37 | mined |  | mining ore veins; creature loot; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Smelted Copper | 108 | mined | r38@Forge (innate); r1440@Replicator (innate); r1488@no station mapping (never taught); r1500@no station mapping (never taught); r1884@Lumen Station (innate, 130 Lumens) | craft r38@Forge; craft r1440@Replicator; craft r1884@Lumen Station | as wiki |  |
| Smelted Druidium | 112 | mined | r42@Forge (innate); r1446@Replicator (innate); r1494@no station mapping (never taught); r1506@no station mapping (never taught); r1890@Lumen Station (innate, 130 Lumens) | craft r42@Forge; craft r1446@Replicator; craft r1890@Lumen Station | as wiki |  |
| Smelted Etherium | 117 | mined | r47@Forge (innate); r1450@Replicator (innate); r1498@no station mapping (never taught); r1510@no station mapping (never taught); r1894@Lumen Station (innate, 130 Lumens) | craft r47@Forge; craft r1450@Replicator; craft r1894@Lumen Station | as wiki |  |
| Smelted Gold | 113 | mined | r43@Forge (innate); r1444@Replicator (innate); r1492@no station mapping (never taught); r1504@no station mapping (never taught); r1888@Lumen Station (innate, 130 Lumens) | craft r43@Forge; craft r1444@Replicator; craft r1888@Lumen Station | as wiki |  |
| Smelted Indicite | 114 | mined | r44@Forge (innate); r1447@Replicator (innate); r1495@no station mapping (never taught); r1507@no station mapping (never taught); r1891@Lumen Station (innate, 130 Lumens) | craft r44@Forge; craft r1447@Replicator; craft r1891@Lumen Station | as wiki |  |
| Smelted Iron | 110 | mined | r40@Forge (innate); r1442@Replicator (innate); r1490@no station mapping (never taught); r1502@no station mapping (never taught); r1886@Lumen Station (innate, 130 Lumens) | craft r40@Forge; craft r1442@Replicator; craft r1886@Lumen Station | as wiki |  |
| Smelted Mithril | 116 | mined | r46@Forge (innate); r1449@Replicator (innate); r1497@no station mapping (never taught); r1509@no station mapping (never taught); r1893@Lumen Station (innate, 130 Lumens) | craft r46@Forge; craft r1449@Replicator; craft r1893@Lumen Station | as wiki |  |
| Smelted Rubicite | 115 | mined | r45@Forge (innate); r1448@Replicator (innate); r1496@no station mapping (never taught); r1508@no station mapping (never taught); r1892@Lumen Station (innate, 130 Lumens) | craft r45@Forge; craft r1448@Replicator; craft r1892@Lumen Station | as wiki |  |
| Smelted Silver | 111 | mined | r41@Forge (innate); r1443@Replicator (innate); r1491@no station mapping (never taught); r1503@no station mapping (never taught); r1887@Lumen Station (innate, 130 Lumens) | craft r41@Forge; craft r1443@Replicator; craft r1887@Lumen Station | as wiki |  |
| Smelted Solucite | 1746 |  | r1303@Forge (innate); r1445@Replicator (innate); r1493@no station mapping (never taught); r1505@no station mapping (never taught); r1889@Lumen Station (innate, 130 Lumens) | craft r1303@Forge; craft r1445@Replicator; craft r1889@Lumen Station | as wiki |  |
| Smelted Tin | 109 | mined | r39@Forge (innate); r1441@Replicator (innate); r1489@no station mapping (never taught); r1501@no station mapping (never taught); r1885@Lumen Station (innate, 130 Lumens) | craft r39@Forge; craft r1441@Replicator; craft r1885@Lumen Station | as wiki |  |
| Striped Wood | 73 | logged |  | trees (axe); reward chest loot | as wiki |  |
| Tin Ore | 35 | mined |  | mining ore veins; creature loot; cave treasure chest; outpost Lost Chest; reward chest loot | as wiki |  |
| Violet Lumicite | 928 |  | r650@Replicator (innate); r659@no station mapping (never taught); r667@no station mapping (never taught); r1694@Forge (innate); r1928@Lumen Station (innate, 130 Lumens) | craft r650@Replicator; craft r1694@Forge; craft r1928@Lumen Station | as wiki |  |
| Water Essence | 1590 | innate; Replicator | r1742@Replicator (innate); r2359@Lumen Station (innate, 130 Lumens) | Lost Crystal; reward chest loot; craft r1742@Replicator; craft r2359@Lumen Station | as wiki |  |
| White Cloth | 3701 | mined; logged | r2374@Replicator (innate); r2377@Lumen Station (innate, 130 Lumens) | craft r2374@Replicator; craft r2377@Lumen Station | as wiki |  |
| White Lumicite | 929 |  | r652@Replicator (innate); r660@no station mapping (never taught); r668@no station mapping (never taught); r1695@Forge (innate); r1929@Lumen Station (innate, 130 Lumens) | craft r652@Replicator; craft r1695@Forge; craft r1929@Lumen Station | as wiki |  |
| White Plastic | 2484 | mined; logged | r1652@Replicator (innate); r1661@no station mapping (never taught); r1670@no station mapping (never taught); r1679@Workshop (innate); r1932@Lumen Station (innate, 130 Lumens) | craft r1652@Replicator; craft r1679@Workshop; craft r1932@Lumen Station | as wiki |  |
| Yellow Cloth | 1726 | mined; logged | r1122@no station mapping (never taught); r1474@no station mapping (never taught); r1480@no station mapping (never taught); r1486@no station mapping (never taught); r1827@Workshop (innate); r1831@Replicator (innate); r1914@Lumen Station (innate, 130 Lumens) | craft r1827@Workshop; craft r1831@Replicator; craft r1914@Lumen Station | as wiki |  |
| Yellow Lumicite | 924 |  | r646@Replicator (innate); r655@no station mapping (never taught); r663@no station mapping (never taught); r1690@Forge (innate); r1926@Lumen Station (innate, 130 Lumens) | craft r646@Replicator; craft r1690@Forge; craft r1926@Lumen Station | as wiki |  |
| Yellow Plastic | 2486 | mined; logged | r1654@Replicator (innate); r1663@no station mapping (never taught); r1672@no station mapping (never taught); r1681@Workshop (innate); r1933@Lumen Station (innate, 130 Lumens) | craft r1654@Replicator; craft r1681@Workshop; craft r1933@Lumen Station | as wiki |  |

### 4.2 Weapons, armour and accessories (62)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| Armament of Unrelenting Vengeance | 3766 | innate; Replicator | r1721@Replicator (innate) | craft r1721@Replicator | as wiki |  |
| Armored Brawler's Blade | 3764 | not innate; Replicator | r1716@Replicator (taught by 3984) | craft r1716@Replicator after learning | as wiki | recipe item 3984 via recipe drop + Lumen Station |
| Armored Brawler's Doublet | 1530 | not innate; Replicator | r984@Replicator (taught by 3991) | craft r984@Replicator after learning | as wiki | recipe item 3991 via recipe drop + Lumen Station |
| Avenging Champion's Aegis | 1539 | not innate; Replicator | r1134@Replicator (taught by 3993) | craft r1134@Replicator after learning | as wiki | recipe item 3993 via recipe drop + Lumen Station |
| Avenging Champion's Exaltation | 3037 | not innate; Replicator | r2423@Replicator (taught by 3986) | craft r2423@Replicator after learning | as wiki | recipe item 3986 via recipe drop + Lumen Station |
| Band of Flame | 1620 | not innate; Replicator | r1065@Replicator (taught by 4010) | craft r1065@Replicator after learning | as wiki | recipe item 4010 via recipe drop + Lumen Station |
| Belt of Virulence | 1629 | not innate; Replicator | r1144@Replicator (taught by 4018) | craft r1144@Replicator after learning | as wiki | recipe item 4018 via recipe drop + Lumen Station |
| Blessed Brooch | 1621 | not innate; Replicator | r1145@Replicator (taught by 4019) | craft r1145@Replicator after learning | as wiki | recipe item 4019 via recipe drop + Lumen Station |
| Blight Warden's Pain | 4091 | not innate; Replicator | r5075@Replicator (taught by 4120) | craft r5075@Replicator after learning | as wiki | recipe item 4120 via recipe drop + Lumen Station |
| Blight Warden's Suffering | 2258 | not innate; Replicator | r1131@Replicator (taught by 4027) | craft r1131@Replicator after learning | as wiki | recipe item 4027 via recipe drop + Lumen Station |
| Bracer of Survival | 1607 | innate; Replicator | r1050@Replicator (innate) | craft r1050@Replicator | as wiki |  |
| Brooch of the Boundless Eye | 1610 | not innate; Replicator | r1142@Replicator (taught by 4013) | craft r1142@Replicator after learning | as wiki | recipe item 4013 via recipe drop + Lumen Station |
| Crusader's Calling | 2259 | not innate; Replicator | r1137@Replicator (taught by 4028) | craft r1137@Replicator after learning | as wiki | recipe item 4028 via recipe drop + Lumen Station |
| Crusader's Courage | 4096 | not innate; Replicator | r5076@Replicator (taught by 4121) | craft r5076@Replicator after learning | as wiki | recipe item 4121 via recipe drop + Lumen Station |
| Defender's Wristguard | 1608 | not innate; Replicator | r1052@Replicator (taught by 4015) | craft r1052@Replicator after learning | as wiki | recipe item 4015 via recipe drop + Lumen Station |
| Desperado's Deadly Poncho | 2251 | not innate; Replicator; goal | r1103@Replicator (taught by 4006) | craft r1103@Replicator after learning | as wiki | wiki: recipe is a goal reward; recipe item 4006 via goal reward 611 |
| Diabolical Scientist's Coat | 1538 | not innate; Replicator | r1342@Replicator (taught by 3992) | craft r1342@Replicator after learning | as wiki | recipe item 3992 via recipe drop + Lumen Station |
| Diabolical Scientist's Raygun | 3032 | not innate; Replicator | r2422@Replicator (taught by 3985) | craft r2422@Replicator after learning | as wiki | recipe item 3985 via recipe drop + Lumen Station |
| Earrings of Enduring Energy | 1609 | innate; Replicator | r1140@Replicator (innate) | craft r1140@Replicator | as wiki |  |
| Electrified Assault Axe | 3027 | not innate; Replicator | r2430@Replicator (taught by 3997) | craft r2430@Replicator after learning | as wiki | recipe item 3997 via recipe drop + Lumen Station |
| Electrified Assault Exo-Suit | 2249 | not innate; Replicator | r1339@Replicator (taught by 4004) | craft r1339@Replicator after learning | as wiki | recipe item 4004 via recipe drop + Lumen Station |
| Falchion of the Inner Spark | 3763 | innate; Replicator | r1717@Replicator (innate) | craft r1717@Replicator | as wiki | xref matched 1503, same-name item 3763 used |
| Flame Bound Bastion | 3030 | not innate; Replicator | r2391@Replicator (taught by 3981) | craft r2391@Replicator after learning | as wiki | recipe item 3981 via recipe drop + Lumen Station |
| Flame Bound Cuirass | 1534 | not innate; Replicator | r1099@Replicator (taught by 3988) | craft r1099@Replicator after learning | as wiki | recipe item 3988 via recipe drop + Lumen Station |
| Forsaken Band of Power | 1618 | not innate; Replicator | r1059@no station mapping (never taught); r1143@Replicator (taught by 4014) | craft r1143@Replicator after learning | as wiki | recipe item 4014 via recipe drop + Lumen Station |
| Frenzied Slayer's Battle Axe | 3031 | not innate; Replicator | r2428@Replicator (taught by 3995) | craft r2428@Replicator after learning | as wiki | recipe item 3995 via recipe drop + Lumen Station |
| Frenzied Slayer's Chainmail | 1541 | not innate; Replicator | r1340@Replicator (taught by 4002) | craft r1340@Replicator after learning | as wiki | recipe item 4002 via recipe drop + Lumen Station |
| Frost Lord's Fury | 4097 | not innate; Replicator | r5073@Replicator (taught by 4118) | craft r5073@Replicator after learning | as wiki | recipe item 4118 via recipe drop + Lumen Station |
| Gunslinger's Last Resort | 1537 | innate; Replicator | r1100@no station mapping (never taught); r1338@Replicator (innate) | craft r1338@Replicator | as wiki |  |
| Gunslinger's Six-Shooters | 3040 | innate; Replicator | r2421@Replicator (innate) | craft r2421@Replicator | as wiki |  |
| High Herald's Ring | 1622 | not innate; Replicator | r1146@Replicator (taught by 4020) | craft r1146@Replicator after learning | as wiki | recipe item 4020 via recipe drop + Lumen Station |
| Lightbringer's Oath | 2252 | not innate; Replicator; goal | r2425@Replicator (taught by 4007) | craft r2425@Replicator after learning | as wiki | wiki: recipe is a goal reward; recipe item 4007 via goal reward 608 |
| Manifestation of the Inner Spark | 1495 | innate; Replicator | r987@Replicator (innate) | craft r987@Replicator | as wiki |  |
| Mantle of Boundless Light | 1532 | innate; Replicator | r1353@Replicator (innate) | craft r1353@Replicator | as wiki |  |
| Night Herald's Mercy | 4089 | not innate; Replicator | r5077@Replicator (taught by 4122) | craft r5077@Replicator after learning | as wiki | recipe item 4122 via recipe drop + Lumen Station |
| Night Herald's Promise | 2260 | not innate; Replicator | r2427@Replicator (taught by 4029) | craft r2427@Replicator after learning | as wiki | recipe item 4029 via recipe drop + Lumen Station |
| Overcharged Recon Blaster | 3033 | not innate; Replicator | r2420@Replicator (taught by 3983) | craft r2420@Replicator after learning | as wiki | recipe item 3983 via recipe drop + Lumen Station |
| Overcharged Recon Gear | 1536 | innate; Replicator; goal | r1101@Replicator (taught by 3990) | craft r1101@Replicator after learning | as wiki | wiki innate, client recipe taught by a Recipe item; wiki: recipe is a goal reward; recipe item 3990 via goal reward 617 |
| Plate of Unrelenting Vengeance | 1533 | not innate; Replicator | r1341@Replicator (innate) | craft r1341@Replicator | different route | wiki not innate, client recipe default-learned |
| Poison Drenched Daggers | 3765 | innate; Replicator | r1719@Replicator (innate) | craft r1719@Replicator | as wiki |  |
| Poison Drenched Leathers | 1531 | innate; Replicator | r990@Replicator (innate) | craft r990@Replicator | as wiki |  |
| Poison-Etched Amulet | 1628 | not innate; Replicator | r1141@Replicator (taught by 4017) | craft r1141@Replicator after learning | as wiki | recipe item 4017 via recipe drop + Lumen Station |
| Radiant Bulwark | 1614 | innate; Replicator | r1057@Replicator (innate) | craft r1057@Replicator | as wiki |  |
| Raiment of Frozen Sorrow | 1491 | not innate; Replicator | r1102@Replicator (taught by 3989) | craft r1102@Replicator after learning | as wiki | recipe item 3989 via recipe drop + Lumen Station |
| Reinforced Coat of Crashing Waves | 1942 | not innate; Replicator; goal | r1344@Replicator (taught by 4003) | craft r1344@Replicator after learning | as wiki | wiki: recipe is a goal reward; recipe item 4003 via goal reward 602 |
| Ring of Indifference | 1613 | not innate; Replicator | r1061@Replicator (taught by 4016) | craft r1061@Replicator after learning | as wiki | recipe item 4016 via recipe drop + Lumen Station |
| Ring of the Rising Tide | 1625 | innate; Replicator | r1067@Replicator (innate) | craft r1067@Replicator | as wiki |  |
| Shadowforged Band | 1612 | not innate; Replicator | r1147@Replicator (taught by 4021) | craft r1147@Replicator after learning | as wiki | recipe item 4021 via recipe drop + Lumen Station |
| Sigil of Piercing Floods | 1626 | not innate; Replicator | r1053@Replicator (taught by 4011) | craft r1053@Replicator after learning | as wiki | recipe item 4011 via recipe drop + Lumen Station |
| Silent Shadow's Cloak | 2253 | not innate; Replicator; goal | r1348@Replicator (taught by 4008) | craft r1348@Replicator after learning | as wiki | wiki: recipe is a goal reward; recipe item 4008 via goal reward 605 |
| Spite Etched Bracer | 3819 | not innate; Replicator | r2390@Replicator (taught by 4022) | craft r2390@Replicator after learning | as wiki | recipe item 4022 via recipe drop + Lumen Station |
| Staff of Frozen Sorrow | 3762 | not innate; Replicator | r1720@Replicator (taught by 3982) | craft r1720@Replicator after learning | as wiki | recipe item 3982 via recipe drop + Lumen Station |
| Staff of the Lightning Caller | 4107 | not innate; Replicator | r5074@Replicator (taught by 4119) | craft r5074@Replicator after learning | as wiki | recipe item 4119 via recipe drop + Lumen Station |
| Supercooled Power Armor | 1535 | innate; Replicator | r1337@Replicator (innate) | craft r1337@Replicator | as wiki |  |
| Supercooled Power Cannon | 3036 | innate; Replicator | r2419@Replicator (innate) | craft r2419@Replicator | as wiki |  |
| Unflinching Embodiment of Might | 3035 | not innate; Replicator | r2431@Replicator (taught by 3998) | craft r2431@Replicator after learning | as wiki | recipe item 3998 via recipe drop + Lumen Station |
| Unflinching Embodiment of Stone | 2250 | not innate; Replicator | r1096@Replicator (taught by 4005) | craft r1096@Replicator after learning | as wiki | recipe item 4005 via recipe drop + Lumen Station |
| Vestment of the Lightning Caller | 2256 | not innate; Replicator | r1128@Replicator (taught by 4025) | craft r1128@Replicator after learning | as wiki | recipe item 4025 via recipe drop + Lumen Station |
| Volcanic Bracer | 1616 | not innate; Replicator | r1056@Replicator (taught by 4009) | craft r1056@Replicator after learning | as wiki | recipe item 4009 via recipe drop + Lumen Station |
| Wand of Boundless Light | 3767 | innate; Replicator | r1722@Replicator (innate) | craft r1722@Replicator | as wiki |  |
| Wanderer's Blazing Bow | 3761 | innate; Replicator | r1718@Replicator (innate) | craft r1718@Replicator | as wiki |  |
| Wanderer's Blazing Guise | 1466 | innate; Replicator | r1098@Replicator (innate) | craft r1098@Replicator | as wiki |  |

### 4.3 Tools (22)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| Adventurer's Grappling Hook | 1178 | innate; Replicator | r765@Replicator (innate) | starter kit; craft r765@Replicator | as wiki |  |
| Adventurer's Logging Axe | 3259 | logged | r331@Replicator (innate) | starter kit; craft r331@Replicator | as client (wiki silent) |  |
| Adventurer's Mining Pick | 3253 | Replicator; mined | r330@Replicator (innate) | starter kit; craft r330@Replicator | as client (wiki silent) |  |
| Cave Seeker | 2455 | innate; Replicator; mined | r1651@Forge/Replicator (innate) | craft r1651@Forge/Replicator | as wiki |  |
| Charmed Axe | 3260 | logged | r2370@Replicator (innate) | craft r2370@Replicator | as client (wiki silent) |  |
| Charmed Pick | 3254 | mined | r2366@Replicator (innate) | craft r2366@Replicator | as client (wiki silent) |  |
| Delver's Grappling Hook | 505 | None | r307@Forge/Replicator (innate) | craft r307@Forge/Replicator | as client (wiki silent) |  |
| Farseer Pick | 3257 | mined | r2364@Replicator (innate) | craft r2364@Replicator | as client (wiki silent) |  |
| Goliath Axe | 3261 |  | r2369@Replicator (innate) | craft r2369@Replicator | as client (wiki silent) |  |
| Goliath Pick | 3255 | mined | r2365@Replicator (innate) | craft r2365@Replicator | as client (wiki silent) |  |
| Greater Lightstone | 232 | innate; Replicator | r1120@Forge/Replicator (innate) | craft r1120@Forge/Replicator | as wiki |  |
| Ground Sounder | 1104 | innate; Replicator; mined | r749@Forge/Replicator (innate) | craft r749@Forge/Replicator | as wiki |  |
| Hotfoot Axe | 3263 |  | r2368@Replicator (innate) | craft r2368@Replicator | as client (wiki silent) |  |
| Lightstone | 231 | innate; Replicator | r785@Forge/Replicator (innate) | starter kit; craft r785@Forge/Replicator | as wiki |  |
| NovaTech Extractor | 3702 | mined; logged | r2382@Replicator (taught by 4347) | craft r2382@Replicator after learning | as wiki | price wiki 1040 / client 780 Lumens; recipe item 4347 via recipe drop + Lumen Station |
| NovaTech Harvestron | 3703 | mined; logged | r2385@Replicator (taught by 4350) | craft r2385@Replicator after learning | as wiki | price wiki 1040 / client 780 Lumens; recipe item 4350 via recipe drop + Lumen Station |
| NovaTech Holoseeker | 3704 | mined; logged | r2383@Replicator (taught by 4348) | craft r2383@Replicator after learning | as wiki | price wiki 1040 / client 780 Lumens; recipe item 4348 via recipe drop + Lumen Station |
| NovaTech Velocitizer | 3705 | mined; logged | r2384@Replicator (taught by 4349) | craft r2384@Replicator after learning | as wiki | price wiki 1040 / client 780 Lumens; recipe item 4349 via recipe drop + Lumen Station |
| Ore Prospector | 1103 | innate; Replicator; mined | r748@Forge/Replicator (innate) | craft r748@Forge/Replicator | as wiki |  |
| Repair Bot Beacon | 4129 | innate; Replicator | r5305@Replicator (innate) | gathering drops; craft r5305@Replicator | as wiki |  |
| Whirlwind Axe | 3262 | logged | r2371@Replicator (innate) | craft r2371@Replicator | as client (wiki silent) |  |
| Whirlwind Pick | 3256 | mined | r2367@Replicator (innate) | craft r2367@Replicator | as client (wiki silent) |  |

### 4.4 Consumables (8)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| Bottled Moonlight | 1110 | Alchemy Station | r746@Replicator (innate) | craft r746@Replicator | as client (wiki silent) | wiki station Alchemy Station, client Replicator |
| Highstep Brew | 1109 | innate; Replicator | r756@Replicator (innate) | craft r756@Replicator | as wiki |  |
| Potion of Furious Harvesting | 1102 | innate; Replicator | r743@Replicator (innate) | craft r743@Replicator | as wiki |  |
| Potion of Greedy Harvesting | 1107 | innate; Replicator | r744@Replicator (innate) | craft r744@Replicator | as wiki |  |
| Potion of Minor Yield | 1101 | not innate; Replicator | r742@Replicator (innate) | craft r742@Replicator | different route | wiki not innate, client recipe default-learned |
| Potion of Nimble Harvesting | 1108 | innate; Replicator | r745@Replicator (innate) | craft r745@Replicator | as wiki |  |
| Sharp-Eyed Elixir | 1100 | Alchemy Station | r741@no station mapping (never taught) |  | not obtainable | 2014 Alchemy Station potion (P1111); in the 0.695 data recipe 741 has no station and is not default-learned, so live had no route either (Bottled Moonlight / Spirit of the Wolf moved to the Replicator) |
| Spirit of the Wolf | 1111 | Alchemy Station | r747@Replicator (innate) | craft r747@Replicator | as client (wiki silent) | wiki station Alchemy Station, client Replicator |

### 4.5 Outfits (319)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| Adventurer's Hiking Gear (Black) | 773 | not innate; Lumen Station 520 Lumens | r2500@Lumen Station (innate, 520 Lumens) | craft r2500@Lumen Station; Showcase (free) | as wiki |  |
| Adventurer's Hiking Gear (Green) | 774 | not innate; Lumen Station 520 Lumens | r2501@Lumen Station (innate, 520 Lumens) | craft r2501@Lumen Station; Showcase (free) | as wiki |  |
| Adventurer's Hiking Gear (Red) | 772 | not innate; Lumen Station 520 Lumens | r2499@Lumen Station (innate, 520 Lumens) | craft r2499@Lumen Station; Showcase (free) | as wiki |  |
| Adventurer's Hiking Gear (Royal Blue) | 959 | not innate; Lumen Station 520 Lumens | r2502@Lumen Station (innate, 520 Lumens) | craft r2502@Lumen Station; Showcase (free) | as wiki |  |
| Adventurer's Hiking Gear (Teal) | 771 | not innate; Lumen Station 520 Lumens | r2498@Lumen Station (innate, 520 Lumens) | craft r2498@Lumen Station; Showcase (free) | as wiki |  |
| Adventurer's Sprint Boots | 2178 | innate; Replicator | r1329@Replicator (innate) | starter kit; craft r1329@Replicator | as wiki |  |
| Artisan's Outfit (Black) | 886 | innate; Replicator | r517@Workshop/Replicator (innate) | craft r517@Workshop/Replicator; Showcase (free) | as wiki |  |
| Artisan's Outfit (Blue) | 1257 | innate; Replicator | r815@Workshop/Replicator (innate) | craft r815@Workshop/Replicator; Showcase (free) | as wiki |  |
| Artisan's Outfit (Emerald) | 884 | innate; Replicator | r516@Workshop (innate) | craft r516@Workshop; Showcase (free) | different route |  |
| Artisan's Outfit (Green) | 548 | innate | r512@Workshop/Replicator (innate) | craft r512@Workshop/Replicator; Showcase (free) | as wiki |  |
| Artisan's Outfit (Orange) | 885 | innate | r519@Workshop/Replicator (innate) | craft r519@Workshop/Replicator; Showcase (free) | as wiki |  |
| Artisan's Outfit (Red) | 883 | innate | r515@Workshop/Replicator (innate) | craft r515@Workshop/Replicator; Showcase (free) | as wiki |  |
| Artisan's Outfit (White) | 1256 | innate | r814@Workshop/Replicator (innate) | craft r814@Workshop/Replicator; Showcase (free) | as wiki |  |
| Artisan's Outfit (Yellow) | 882 | innate | r514@Workshop/Replicator (innate) | craft r514@Workshop/Replicator; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Black & Red) | 4232 | not innate; Lumen Station 520 Lumens | r5190@Lumen Station (innate, 520 Lumens) | craft r5190@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Blue & Gold) | 4226 | not innate; Lumen Station 520 Lumens | r5184@Lumen Station (innate, 520 Lumens) | craft r5184@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Blue & Violet) | 4228 | not innate; Lumen Station 520 Lumens | r5186@Lumen Station (innate, 520 Lumens) | craft r5186@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Blue) | 4224 | not innate; Lumen Station 520 Lumens | r5182@Lumen Station (innate, 520 Lumens) | craft r5182@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Bronze) | 4230 | not innate; Lumen Station 520 Lumens | r5188@Lumen Station (innate, 520 Lumens) | craft r5188@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Maroon & Black) | 4225 | not innate; Lumen Station 520 Lumens | r5183@Lumen Station (innate, 520 Lumens) | craft r5183@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Purple & Black) | 4229 | not innate; Lumen Station 520 Lumens | r5187@Lumen Station (innate, 520 Lumens) | craft r5187@Lumen Station; Showcase (free) | as wiki |  |
| Assassin's Stealth Leathers (Seafoam) | 4231 | not innate; Lumen Station 520 Lumens | r5189@Lumen Station (innate, 520 Lumens) | craft r5189@Lumen Station; Showcase (free) | as wiki |  |
| Boots of Bounding | 243 | innate; Replicator | r233@Replicator (innate) | craft r233@Replicator | as wiki |  |
| Brawler's Gilded Doublet (Black & Red) | 4311 | not innate; Lumen Station 520 Lumens | r5269@Lumen Station (innate, 520 Lumens) | craft r5269@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (Black) | 4314 | not innate; Lumen Station 520 Lumens | r5272@Lumen Station (innate, 520 Lumens) | craft r5272@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (Bronze & Green) | 4309 | not innate; Lumen Station 520 Lumens | r5267@Lumen Station (innate, 520 Lumens) | craft r5267@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (Gold & Purple) | 4310 | not innate; Lumen Station 520 Lumens | r5268@Lumen Station (innate, 520 Lumens) | craft r5268@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (Grey) | 4308 | not innate; Lumen Station 520 Lumens | r5266@Lumen Station (innate, 520 Lumens) | craft r5266@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (Silver & Brown) | 4307 | not innate; Lumen Station 520 Lumens | r5265@Lumen Station (innate, 520 Lumens) | craft r5265@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (Teal) | 4312 | not innate; Lumen Station 520 Lumens | r5270@Lumen Station (innate, 520 Lumens) | craft r5270@Lumen Station; Showcase (free) | as wiki |  |
| Brawler's Gilded Doublet (White) | 4313 | not innate; Lumen Station 520 Lumens | r5271@Lumen Station (innate, 520 Lumens) | craft r5271@Lumen Station; Showcase (free) | as wiki |  |
| Breeze Boots | 2851 | innate; Replicator | r1153@Replicator (innate) | craft r1153@Replicator | as wiki |  |
| Casual Victorian Apparel (Black) | 946 | not innate; Lumen Station 520 Lumens | r2522@Lumen Station (innate, 520 Lumens) | craft r2522@Lumen Station; Showcase (free) | as wiki |  |
| Casual Victorian Apparel (Blue) | 777 | not innate; Lumen Station 520 Lumens | r2526@Lumen Station (innate, 520 Lumens) | craft r2526@Lumen Station; Showcase (free) | as wiki |  |
| Casual Victorian Apparel (Gold) | 778 | not innate; Lumen Station 520 Lumens | r2527@Lumen Station (innate, 520 Lumens) | craft r2527@Lumen Station; Showcase (free) | as wiki |  |
| Casual Victorian Apparel (Green) | 1277 | not innate; Lumen Station 520 Lumens | r2523@Lumen Station (innate, 520 Lumens) | craft r2523@Lumen Station; Showcase (free) | as wiki |  |
| Casual Victorian Apparel (Pink) | 1091 | not innate; Lumen Station 520 Lumens | r2528@Lumen Station (innate, 520 Lumens) | craft r2528@Lumen Station; Showcase (free) | as wiki |  |
| Casual Victorian Apparel (Purple) | 775 | not innate; Lumen Station 520 Lumens | r2524@Lumen Station (innate, 520 Lumens) | craft r2524@Lumen Station; Showcase (free) | as wiki |  |
| Casual Victorian Apparel (Red) | 776 | not innate; Lumen Station 520 Lumens | r2525@Lumen Station (innate, 520 Lumens) | craft r2525@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Black) | 4262 | not innate; Lumen Station 520 Lumens | r5220@Lumen Station (innate, 520 Lumens) | craft r5220@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Gold) | 4260 | not innate; Lumen Station 520 Lumens | r5218@Lumen Station (innate, 520 Lumens) | craft r5218@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Green) | 4265 | not innate; Lumen Station 520 Lumens | r5223@Lumen Station (innate, 520 Lumens) | craft r5223@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Grey) | 4257 | not innate; Lumen Station 520 Lumens | r5215@Lumen Station (innate, 520 Lumens) | craft r5215@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Platinum) | 4264 | not innate; Lumen Station 520 Lumens | r5222@Lumen Station (innate, 520 Lumens) | craft r5222@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Purple) | 4259 | not innate; Lumen Station 520 Lumens | r5217@Lumen Station (innate, 520 Lumens) | craft r5217@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Red) | 4263 | not innate; Lumen Station 520 Lumens | r5221@Lumen Station (innate, 520 Lumens) | craft r5221@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (Teal) | 4258 | not innate; Lumen Station 520 Lumens | r5216@Lumen Station (innate, 520 Lumens) | craft r5216@Lumen Station; Showcase (free) | as wiki |  |
| Champion's Sturdy Plate (White) | 4261 | not innate; Lumen Station 520 Lumens | r5219@Lumen Station (innate, 520 Lumens) | craft r5219@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Black) | 4139 | not innate; Lumen Station 520 Lumens | r5097@Lumen Station (innate, 520 Lumens) | craft r5097@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Bronze) | 4135 | not innate; Lumen Station 520 Lumens | r5093@Lumen Station (innate, 520 Lumens) | craft r5093@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Brown) | 4145 | not innate; Lumen Station 520 Lumens | r5103@Lumen Station (innate, 520 Lumens) | craft r5103@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Gold) | 4138 | not innate; Lumen Station 520 Lumens | r5096@Lumen Station (innate, 520 Lumens) | craft r5096@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Green & Blue) | 4143 | not innate; Lumen Station 520 Lumens | r5101@Lumen Station (innate, 520 Lumens) | craft r5101@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Green) | 2514 | not innate; Lumen Station 520 Lumens | r2536@Lumen Station (innate, 520 Lumens) | craft r2536@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Grey) | 4137 | not innate; Lumen Station 520 Lumens | r5095@Lumen Station (innate, 520 Lumens) | craft r5095@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Orange) | 4141 | not innate; Lumen Station 520 Lumens | r5099@Lumen Station (innate, 520 Lumens) | craft r5099@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Purple) | 4140 | not innate; Lumen Station 520 Lumens | r5098@Lumen Station (innate, 520 Lumens) | craft r5098@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Red) | 2515 | not innate; Lumen Station 520 Lumens | r2537@Lumen Station (innate, 520 Lumens) | craft r2537@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Seafoam) | 4144 | not innate; Lumen Station 520 Lumens | r5102@Lumen Station (innate, 520 Lumens) | craft r5102@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (Teal & Purple) | 4142 | not innate; Lumen Station 520 Lumens | r5100@Lumen Station (innate, 520 Lumens) | craft r5100@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Battle Armor (White) | 4136 | not innate; Lumen Station 520 Lumens | r5094@Lumen Station (innate, 520 Lumens) | craft r5094@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Black) | 2518 | not innate; Lumen Station 520 Lumens | r2535@Lumen Station (innate, 520 Lumens) | craft r2535@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Blue) | 2520 | not innate; Lumen Station 520 Lumens | r2543@Lumen Station (innate, 520 Lumens) | craft r2543@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Brown) | 4163 | not innate; Lumen Station 520 Lumens | r5121@Lumen Station (innate, 520 Lumens) | craft r5121@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Gold) | 2522 | not innate; Lumen Station 520 Lumens | r2546@Lumen Station (innate, 520 Lumens) | craft r2546@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Green) | 4162 | not innate; Lumen Station 520 Lumens | r5120@Lumen Station (innate, 520 Lumens) | craft r5120@Lumen Station; Showcase (free) | as wiki | xref matched 2551, same-name item 4162 used |
| Colonist's Elegant Robe (Midnight) | 4166 | not innate; Lumen Station 520 Lumens | r5124@Lumen Station (innate, 520 Lumens) | craft r5124@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Orange) | 4161 | not innate; Lumen Station 520 Lumens | r5119@Lumen Station (innate, 520 Lumens) | craft r5119@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Purple) | 4164 | not innate; Lumen Station 520 Lumens | r5122@Lumen Station (innate, 520 Lumens) | craft r5122@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Red) | 2521 | not innate; Lumen Station 520 Lumens | r2544@Lumen Station (innate, 520 Lumens) | craft r2544@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Sky) | 2523 | not innate; Lumen Station 520 Lumens | r2547@Lumen Station (innate, 520 Lumens) | craft r2547@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Elegant Robe (Teal) | 4165 | not innate; Lumen Station 520 Lumens | r5123@Lumen Station (innate, 520 Lumens) | craft r5123@Lumen Station; Showcase (free) | as wiki | xref matched 2631, same-name item 4165 used |
| Colonist's Elegant Robe (White) | 2519 | not innate; Lumen Station 520 Lumens | r2542@Lumen Station (innate, 520 Lumens) | craft r2542@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Black) | 760 | not innate; Lumen Station 520 Lumens | r2504@Lumen Station (innate, 520 Lumens) | craft r2504@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Blue & Grey) | 1192 | not innate; Lumen Station 520 Lumens | r2511@Lumen Station (innate, 520 Lumens) | craft r2511@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Blue & Ivory) | 4133 | not innate; Lumen Station 520 Lumens | r5091@Lumen Station (innate, 520 Lumens) | craft r5091@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Blue) | 761 | not innate; Lumen Station 520 Lumens | r2505@Lumen Station (innate, 520 Lumens) | craft r2505@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Bronze & Grey) | 4131 | not innate; Lumen Station 520 Lumens | r5089@Lumen Station (innate, 520 Lumens) | craft r5089@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Gold) | 763 | not innate; Lumen Station 520 Lumens | r2506@Lumen Station (innate, 520 Lumens) | craft r2506@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Green & Grey) | 4132 | not innate; Lumen Station 520 Lumens | r5090@Lumen Station (innate, 520 Lumens) | craft r5090@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Lime) | 1189 | not innate; Lumen Station 520 Lumens | r2508@Lumen Station (innate, 520 Lumens) | craft r2508@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Orange & Grey) | 1190 | not innate; Lumen Station 520 Lumens | r2509@Lumen Station (innate, 520 Lumens) | craft r2509@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Purple & Teal) | 4134 | not innate; Lumen Station 520 Lumens | r5092@Lumen Station (innate, 520 Lumens) | craft r5092@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Red & White) | 1191 | not innate; Lumen Station 520 Lumens | r2510@Lumen Station (innate, 520 Lumens) | craft r2510@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Red) | 960 | not innate; Lumen Station 520 Lumens | r2507@Lumen Station (innate, 520 Lumens) | craft r2507@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (Rose) | 4130 | not innate; Lumen Station 520 Lumens | r5088@Lumen Station (innate, 520 Lumens) | craft r5088@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Encounter Suit (White) | 545 | not innate; Lumen Station 520 Lumens | r2503@Lumen Station (innate, 520 Lumens) | craft r2503@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Black) | 4152 | not innate; Lumen Station 520 Lumens | r5110@Lumen Station (innate, 520 Lumens) | craft r5110@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Blue & Orange) | 4155 | not innate; Lumen Station 520 Lumens | r5113@Lumen Station (innate, 520 Lumens) | craft r5113@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Bronze) | 4146 | not innate; Lumen Station 520 Lumens | r5104@Lumen Station (innate, 520 Lumens) | craft r5104@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Brown & Gold) | 4159 | not innate; Lumen Station 520 Lumens | r5117@Lumen Station (innate, 520 Lumens) | craft r5117@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Gold) | 4151 | not innate; Lumen Station 520 Lumens | r5109@Lumen Station (innate, 520 Lumens) | craft r5109@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Green & Blue) | 4157 | not innate; Lumen Station 520 Lumens | r5115@Lumen Station (innate, 520 Lumens) | craft r5115@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Green) | 4148 | not innate; Lumen Station 520 Lumens | r5106@Lumen Station (innate, 520 Lumens) | craft r5106@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Grey) | 4150 | not innate; Lumen Station 520 Lumens | r5108@Lumen Station (innate, 520 Lumens) | craft r5108@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Orange) | 4154 | not innate; Lumen Station 520 Lumens | r5112@no station (innate, 520 Lumens) | Showcase (free) | not obtainable | 0.695 dropped the Lumen Station mapping of recipes 5111 / 5112 (items-695.md, tables diff): the wiki price is the 0.589-era one; only the free Showcase lists them |
| Colonist's Patrol Armor (Purple) | 4153 | not innate; Lumen Station 520 Lumens | r5111@no station (innate, 520 Lumens) | Showcase (free) | not obtainable | 0.695 dropped the Lumen Station mapping of recipes 5111 / 5112 (items-695.md, tables diff): the wiki price is the 0.589-era one; only the free Showcase lists them |
| Colonist's Patrol Armor (Red) | 4149 | not innate; Lumen Station 520 Lumens | r5107@Lumen Station (innate, 520 Lumens) | craft r5107@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Seafoam & Red) | 4158 | not innate; Lumen Station 520 Lumens | r5116@Lumen Station (innate, 520 Lumens) | craft r5116@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (Teal & Purple) | 4156 | not innate; Lumen Station 520 Lumens | r5114@Lumen Station (innate, 520 Lumens) | craft r5114@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Patrol Armor (White) | 4147 | not innate; Lumen Station 520 Lumens | r5105@Lumen Station (innate, 520 Lumens) | craft r5105@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Skirmish Suit (Copper) | 2550 | not innate; Lumen Station 520 Lumens | r2539@Lumen Station (innate, 520 Lumens) | craft r2539@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Skirmish Suit (Orange) | 2517 | not innate; Lumen Station 520 Lumens | r2541@Lumen Station (innate, 520 Lumens) | craft r2541@Lumen Station; Showcase (free) | as wiki |  |
| Colonist's Skirmish Suit (Purple) | 2516 | not innate; Lumen Station 520 Lumens | r2540@Lumen Station (innate, 520 Lumens) | craft r2540@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Black) | 4187 | not innate; Lumen Station 520 Lumens | r5145@Lumen Station (innate, 520 Lumens) | craft r5145@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Blue & Ivory) | 4189 | not innate; Lumen Station 520 Lumens | r5147@Lumen Station (innate, 520 Lumens) | craft r5147@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Blue & Orange) | 4194 | not innate; Lumen Station 520 Lumens | r5152@Lumen Station (innate, 520 Lumens) | craft r5152@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Blue) | 4184 | not innate; Lumen Station 520 Lumens | r5142@Lumen Station (innate, 520 Lumens) | craft r5142@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Bronze) | 4191 | not innate; Lumen Station 520 Lumens | r5149@Lumen Station (innate, 520 Lumens) | craft r5149@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Emerald) | 4195 | not innate; Lumen Station 520 Lumens | r5153@Lumen Station (innate, 520 Lumens) | craft r5153@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Gold & Blue) | 4193 | not innate; Lumen Station 520 Lumens | r5151@Lumen Station (innate, 520 Lumens) | craft r5151@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Green & Purple) | 4192 | not innate; Lumen Station 520 Lumens | r5150@Lumen Station (innate, 520 Lumens) | craft r5150@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Green) | 4183 | not innate; Lumen Station 520 Lumens | r5141@Lumen Station (innate, 520 Lumens) | craft r5141@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Grey) | 4188 | not innate; Lumen Station 520 Lumens | r5146@Lumen Station (innate, 520 Lumens) | craft r5146@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Purple & Blue) | 4196 | not innate; Lumen Station 520 Lumens | r5154@Lumen Station (innate, 520 Lumens) | craft r5154@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Purple & Teal) | 4190 | not innate; Lumen Station 520 Lumens | r5148@Lumen Station (innate, 520 Lumens) | craft r5148@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Purple) | 4185 | not innate; Lumen Station 520 Lumens | r5143@Lumen Station (innate, 520 Lumens) | craft r5143@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Rose & Blue) | 4182 | not innate; Lumen Station 520 Lumens | r5140@Lumen Station (innate, 520 Lumens) | craft r5140@Lumen Station; Showcase (free) | as wiki |  |
| Conqueror's Platemail (Rose & Gold) | 4186 | not innate; Lumen Station 520 Lumens | r5144@Lumen Station (innate, 520 Lumens) | craft r5144@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Blue & Orange) | 4254 | not innate; Lumen Station 520 Lumens | r5212@Lumen Station (innate, 520 Lumens) | craft r5212@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Gold) | 4256 | not innate; Lumen Station 520 Lumens | r5214@Lumen Station (innate, 520 Lumens) | craft r5214@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Green) | 4249 | not innate; Lumen Station 520 Lumens | r5207@Lumen Station (innate, 520 Lumens) | craft r5207@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Ivory) | 4250 | not innate; Lumen Station 520 Lumens | r5208@Lumen Station (innate, 520 Lumens) | craft r5208@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Midnight) | 4255 | not innate; Lumen Station 520 Lumens | r5213@Lumen Station (innate, 520 Lumens) | craft r5213@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Pink) | 4251 | not innate; Lumen Station 520 Lumens | r5209@Lumen Station (innate, 520 Lumens) | craft r5209@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Purple) | 4252 | not innate; Lumen Station 520 Lumens | r5210@Lumen Station (innate, 520 Lumens) | craft r5210@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Red) | 4247 | not innate; Lumen Station 520 Lumens | r5205@Lumen Station (innate, 520 Lumens) | craft r5205@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (Rose) | 4248 | not innate; Lumen Station 520 Lumens | r5206@Lumen Station (innate, 520 Lumens) | craft r5206@Lumen Station; Showcase (free) | as wiki |  |
| Covert Watcher's Cloak (White) | 4253 | not innate; Lumen Station 520 Lumens | r5211@Lumen Station (innate, 520 Lumens) | craft r5211@Lumen Station; Showcase (free) | as wiki |  |
| Dash Soles | 235 | innate; Replicator | r85@Replicator (innate) | craft r85@Replicator | as wiki |  |
| Desperado's Outfit (Black) | 4032 | not innate; Lumen Station 520 Lumens | r5003@Lumen Station (innate, 520 Lumens) | craft r5003@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (Blue) | 2552 | not innate; Lumen Station 520 Lumens | r2554@Lumen Station (innate, 520 Lumens) | craft r2554@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (Brown) | 2524 | not innate; Lumen Station 520 Lumens | r2552@Lumen Station (innate, 520 Lumens) | craft r2552@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (Gray) | 4034 | not innate; Lumen Station 520 Lumens | r5005@Lumen Station (innate, 520 Lumens) | craft r5005@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (Purple) | 4033 | not innate; Lumen Station 520 Lumens | r5004@Lumen Station (innate, 520 Lumens) | craft r5004@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (Red) | 2525 | not innate; Lumen Station 520 Lumens | r2553@Lumen Station (innate, 520 Lumens) | craft r2553@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (Teal) | 4035 | not innate; Lumen Station 520 Lumens | r5006@Lumen Station (innate, 520 Lumens) | craft r5006@Lumen Station; Showcase (free) | as wiki |  |
| Desperado's Outfit (White) | 4036 | not innate; Lumen Station 520 Lumens | r5007@Lumen Station (innate, 520 Lumens) | craft r5007@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Black) | 768 | not innate; Lumen Station 520 Lumens | r2518@Lumen Station (innate, 520 Lumens) | craft r2518@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Blue) | 767 | not innate; Lumen Station 520 Lumens | r2517@Lumen Station (innate, 520 Lumens) | craft r2517@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Brown) | 1276 | not innate; Lumen Station 520 Lumens | r2521@Lumen Station (innate, 520 Lumens) | craft r2521@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Gold) | 769 | not innate; Lumen Station 520 Lumens | r2519@Lumen Station (innate, 520 Lumens) | craft r2519@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Green) | 1092 | not innate; Lumen Station 520 Lumens | r2520@Lumen Station (innate, 520 Lumens) | craft r2520@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Purple) | 549 | not innate; Lumen Station 520 Lumens | r2515@Lumen Station (innate, 520 Lumens) | craft r2515@Lumen Station; Showcase (free) | as wiki |  |
| Formal Victorian Wear (Red) | 766 | not innate; Lumen Station 520 Lumens | r2516@Lumen Station (innate, 520 Lumens) | craft r2516@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Black & Red) | 4302 | not innate; Lumen Station 520 Lumens | r5260@Lumen Station (innate, 520 Lumens) | craft r5260@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Black) | 4306 | not innate; Lumen Station 520 Lumens | r5264@Lumen Station (innate, 520 Lumens) | craft r5264@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Bronze & Green) | 4300 | not innate; Lumen Station 520 Lumens | r5258@Lumen Station (innate, 520 Lumens) | craft r5258@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Gold & Purple) | 4301 | not innate; Lumen Station 520 Lumens | r5259@Lumen Station (innate, 520 Lumens) | craft r5259@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Grey) | 4299 | not innate; Lumen Station 520 Lumens | r5257@Lumen Station (innate, 520 Lumens) | craft r5257@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Silver & Brown) | 4298 | not innate; Lumen Station 520 Lumens | r5256@Lumen Station (innate, 520 Lumens) | craft r5256@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Teal) | 4303 | not innate; Lumen Station 520 Lumens | r5261@Lumen Station (innate, 520 Lumens) | craft r5261@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (White) | 4304 | not innate; Lumen Station 520 Lumens | r5262@Lumen Station (innate, 520 Lumens) | craft r5262@Lumen Station; Showcase (free) | as wiki |  |
| Gallant Soldier's Chainmail (Yellow) | 4305 | not innate; Lumen Station 520 Lumens | r5263@Lumen Station (innate, 520 Lumens) | craft r5263@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Black) | 4037 | not innate; Lumen Station 520 Lumens | r5008@Lumen Station (innate, 520 Lumens) | craft r5008@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Brown) | 2553 | not innate; Lumen Station 520 Lumens | r2564@Lumen Station (innate, 520 Lumens) | craft r2564@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Gray) | 4041 | not innate; Lumen Station 520 Lumens | r5012@Lumen Station (innate, 520 Lumens) | craft r5012@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Green) | 4040 | not innate; Lumen Station 520 Lumens | r5011@Lumen Station (innate, 520 Lumens) | craft r5011@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Pink) | 4039 | not innate; Lumen Station 520 Lumens | r5010@Lumen Station (innate, 520 Lumens) | craft r5010@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Purple) | 4038 | not innate; Lumen Station 520 Lumens | r5009@Lumen Station (innate, 520 Lumens) | craft r5009@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (Red) | 2527 | not innate; Lumen Station 520 Lumens | r2563@Lumen Station (innate, 520 Lumens) | craft r2563@Lumen Station; Showcase (free) | as wiki |  |
| Gunslinger's Gear (White) | 2526 | not innate; Lumen Station 520 Lumens | r2562@Lumen Station (innate, 520 Lumens) | craft r2562@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Black) | 4284 | not innate; Lumen Station 520 Lumens | r5242@Lumen Station (innate, 520 Lumens) | craft r5242@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Gold) | 4283 | not innate; Lumen Station 520 Lumens | r5241@Lumen Station (innate, 520 Lumens) | craft r5241@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Green) | 4288 | not innate; Lumen Station 520 Lumens | r5246@Lumen Station (innate, 520 Lumens) | craft r5246@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Grey) | 4280 | not innate; Lumen Station 520 Lumens | r5238@Lumen Station (innate, 520 Lumens) | craft r5238@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Platinum) | 4287 | not innate; Lumen Station 520 Lumens | r5245@Lumen Station (innate, 520 Lumens) | craft r5245@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Purple) | 4282 | not innate; Lumen Station 520 Lumens | r5240@Lumen Station (innate, 520 Lumens) | craft r5240@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Red) | 4285 | not innate; Lumen Station 520 Lumens | r5243@Lumen Station (innate, 520 Lumens) | craft r5243@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (Teal) | 4281 | not innate; Lumen Station 520 Lumens | r5239@Lumen Station (innate, 520 Lumens) | craft r5239@Lumen Station; Showcase (free) | as wiki |  |
| Hero's Stalwart Guard (White) | 4286 | not innate; Lumen Station 520 Lumens | r5244@Lumen Station (innate, 520 Lumens) | craft r5244@Lumen Station; Showcase (free) | as wiki |  |
| Leaper's Launchers | 244 | innate; Replicator | r234@Replicator (innate) | craft r234@Replicator | as wiki |  |
| Molten Iron Orb | 1615 | innate; Replicator | r1055@Replicator (innate) | craft r1055@Replicator | as wiki |  |
| Neon Blitzing Boots | 233 | innate; Replicator | r83@Replicator (innate) | craft r83@Replicator | as wiki |  |
| New Conglomerate Commander's Gear | 1385 | not innate; Lumen Station 520 Lumens | r2512@Lumen Station (innate, 520 Lumens) | craft r2512@Lumen Station; Showcase (free) | as wiki |  |
| Nimbus Boots | 1120 | innate; Replicator | r1152@Replicator (innate) | craft r1152@Replicator | as wiki |  |
| Noble Crusader's Chainmail (Black) | 4279 | not innate; Lumen Station 520 Lumens | r5237@Lumen Station (innate, 520 Lumens) | craft r5237@Lumen Station; Showcase (free) | as wiki |  |
| Noble Crusader's Chainmail (Bronze & Green) | 4275 | not innate; Lumen Station 520 Lumens | r5233@Lumen Station (innate, 520 Lumens) | craft r5233@Lumen Station; Showcase (free) | as wiki |  |
| Noble Crusader's Chainmail (Gold & Purple) | 4276 | not innate; Lumen Station 520 Lumens | r5234@Lumen Station (innate, 520 Lumens) | craft r5234@Lumen Station; Showcase (free) | as wiki |  |
| Noble Crusader's Chainmail (Grey) | 4274 | not innate; Lumen Station 520 Lumens | r5232@Lumen Station (innate, 520 Lumens) | craft r5232@Lumen Station; Showcase (free) | as wiki |  |
| Noble Crusader's Chainmail (Silver & Brown) | 4273 | not innate; Lumen Station 520 Lumens | r5231@Lumen Station (innate, 520 Lumens) | craft r5231@Lumen Station; Showcase (free) | as wiki |  |
| Noble Crusader's Chainmail (White) | 4277 | not innate; Lumen Station 520 Lumens | r5235@Lumen Station (innate, 520 Lumens) | craft r5235@Lumen Station; Showcase (free) | as wiki |  |
| Noble Crusader's Chainmail (Yellow) | 4278 | not innate; Lumen Station 520 Lumens | r5236@Lumen Station (innate, 520 Lumens) | craft r5236@Lumen Station; Showcase (free) | as wiki |  |
| Outlaw's Disguise (Blue) | 2531 | not innate; Lumen Station 520 Lumens | r2557@Lumen Station (innate, 520 Lumens) | craft r2557@Lumen Station; Showcase (free) | as wiki |  |
| Outlaw's Disguise (Brown & Green) | 2554 | not innate; Lumen Station 520 Lumens | r2556@Lumen Station (innate, 520 Lumens) | craft r2556@Lumen Station; Showcase (free) | as wiki |  |
| Outlaw's Disguise (Red) | 2530 | not innate; Lumen Station 520 Lumens | r2555@Lumen Station (innate, 520 Lumens) | craft r2555@Lumen Station; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Black) | 878 | innate; Replicator | r525@Workshop/Replicator (innate) | craft r525@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Blue) | 1262 | innate; Replicator | r820@Workshop/Replicator (innate) | craft r820@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Green) | 547 | innate; Replicator | r520@Workshop/Replicator (innate) | craft r520@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Orange) | 877 | innate; Replicator | r524@Workshop/Replicator (innate) | craft r524@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Red) | 875 | innate; Replicator | r522@Workshop/Replicator (innate) | craft r522@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Rose) | 879 | innate; Replicator | r526@Workshop/Replicator (innate) | craft r526@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (White) | 1261 | innate; Replicator | r819@Workshop/Replicator (innate) | craft r819@Workshop/Replicator; Showcase (free) | as wiki |  |
| Pathfinder's Gear (Yellow) | 874 | innate; Replicator | r521@Workshop/Replicator (innate) | craft r521@Workshop/Replicator; Showcase (free) | as wiki |  |
| Rancher's Outfit (Black) | 2532 | not innate; Lumen Station 520 Lumens; marketplace | r2561@Lumen Station (innate, 520 Lumens) | craft r2561@Lumen Station; Showcase (free) | as wiki | wiki names the Marketplace: Showcase lists it |
| Rancher's Outfit (Blue) | 2533 | not innate; Lumen Station 520 Lumens; marketplace | r2559@Lumen Station (innate, 520 Lumens) | craft r2559@Lumen Station; Showcase (free) | as wiki | wiki names the Marketplace: Showcase lists it |
| Rancher's Outfit (Orange) | 2555 | not innate; Lumen Station 520 Lumens; marketplace | r2558@Lumen Station (innate, 520 Lumens) | craft r2558@Lumen Station; Showcase (free) | as wiki | wiki names the Marketplace: Showcase lists it |
| Rancher's Outfit (Red) | 2534 | not innate; Lumen Station 520 Lumens; marketplace | r2560@Lumen Station (innate, 520 Lumens) | craft r2560@Lumen Station; Showcase (free) | as wiki | wiki names the Marketplace: Showcase lists it |
| Rascal's Custom Finery (Black) | 4293 | not innate; Lumen Station 520 Lumens | r5251@Lumen Station (innate, 520 Lumens) | craft r5251@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Blue) | 4290 | not innate; Lumen Station 520 Lumens | r5248@Lumen Station (innate, 520 Lumens) | craft r5248@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Green) | 4294 | not innate; Lumen Station 520 Lumens | r5252@Lumen Station (innate, 520 Lumens) | craft r5252@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Midnight) | 4296 | not innate; Lumen Station 520 Lumens | r5254@Lumen Station (innate, 520 Lumens) | craft r5254@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Red) | 4291 | not innate; Lumen Station 520 Lumens | r5249@Lumen Station (innate, 520 Lumens) | craft r5249@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Tan) | 4289 | not innate; Lumen Station 520 Lumens | r5247@Lumen Station (innate, 520 Lumens) | craft r5247@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Teal) | 4295 | not innate; Lumen Station 520 Lumens | r5253@Lumen Station (innate, 520 Lumens) | craft r5253@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (Violet) | 4297 | not innate; Lumen Station 520 Lumens | r5255@Lumen Station (innate, 520 Lumens) | craft r5255@Lumen Station; Showcase (free) | as wiki |  |
| Rascal's Custom Finery (White) | 4292 | not innate; Lumen Station 520 Lumens | r5250@Lumen Station (innate, 520 Lumens) | craft r5250@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Black) | 4202 | not innate; Lumen Station 520 Lumens | r5160@Lumen Station (innate, 520 Lumens) | craft r5160@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Blue & Purple) | 4197 | not innate; Lumen Station 520 Lumens | r5155@Lumen Station (innate, 520 Lumens) | craft r5155@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Blue) | 4205 | not innate; Lumen Station 520 Lumens | r5163@Lumen Station (innate, 520 Lumens) | craft r5163@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Ivory) | 4201 | not innate; Lumen Station 520 Lumens | r5159@Lumen Station (innate, 520 Lumens) | craft r5159@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Ocean) | 4200 | not innate; Lumen Station 520 Lumens | r5158@Lumen Station (innate, 520 Lumens) | craft r5158@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Olive Green) | 4199 | not innate; Lumen Station 520 Lumens | r5157@Lumen Station (innate, 520 Lumens) | craft r5157@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Purple) | 4203 | not innate; Lumen Station 520 Lumens | r5161@Lumen Station (innate, 520 Lumens) | craft r5161@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (Teal) | 4198 | not innate; Lumen Station 520 Lumens | r5156@Lumen Station (innate, 520 Lumens) | craft r5156@Lumen Station; Showcase (free) | as wiki |  |
| Ritualist's Exotic Robes (White) | 4204 | not innate; Lumen Station 520 Lumens | r5162@Lumen Station (innate, 520 Lumens) | craft r5162@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (Black) | 4270 | not innate; Lumen Station 520 Lumens | r5228@Lumen Station (innate, 520 Lumens) | craft r5228@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (Blue) | 4267 | not innate; Lumen Station 520 Lumens | r5225@Lumen Station (innate, 520 Lumens) | craft r5225@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (Green) | 4271 | not innate; Lumen Station 520 Lumens | r5229@Lumen Station (innate, 520 Lumens) | craft r5229@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (Orange) | 4266 | not innate; Lumen Station 520 Lumens | r5224@Lumen Station (innate, 520 Lumens) | craft r5224@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (Red) | 4268 | not innate; Lumen Station 520 Lumens | r5226@Lumen Station (innate, 520 Lumens) | craft r5226@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (Violet) | 4272 | not innate; Lumen Station 520 Lumens | r5230@Lumen Station (innate, 520 Lumens) | craft r5230@Lumen Station; Showcase (free) | as wiki |  |
| Rogue's Lavish Mantle (White) | 4269 | not innate; Lumen Station 520 Lumens | r5227@Lumen Station (innate, 520 Lumens) | craft r5227@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Black) | 4316 | not innate; Lumen Station 520 Lumens | r5274@Lumen Station (innate, 520 Lumens) | craft r5274@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Bronze) | 4320 | not innate; Lumen Station 520 Lumens | r5278@Lumen Station (innate, 520 Lumens) | craft r5278@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Gold & Red) | 4321 | not innate; Lumen Station 520 Lumens | r5279@Lumen Station (innate, 520 Lumens) | craft r5279@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Green & Grey) | 4318 | not innate; Lumen Station 520 Lumens | r5276@Lumen Station (innate, 520 Lumens) | craft r5276@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Grey) | 4319 | not innate; Lumen Station 520 Lumens | r5277@Lumen Station (innate, 520 Lumens) | craft r5277@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Orange & Blue) | 4317 | not innate; Lumen Station 520 Lumens | r5275@Lumen Station (innate, 520 Lumens) | craft r5275@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Purple) | 4323 | not innate; Lumen Station 520 Lumens | r5281@Lumen Station (innate, 520 Lumens) | craft r5281@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Red & Tan) | 4315 | not innate; Lumen Station 520 Lumens | r5273@Lumen Station (innate, 520 Lumens) | craft r5273@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (Teal & Violet) | 4324 | not innate; Lumen Station 520 Lumens | r5282@Lumen Station (innate, 520 Lumens) | craft r5282@Lumen Station; Showcase (free) | as wiki |  |
| Ronin's Reinforced Coat (White) | 4322 | not innate; Lumen Station 520 Lumens | r5280@Lumen Station (innate, 520 Lumens) | craft r5280@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Black & Red) | 4222 | not innate; Lumen Station 520 Lumens | r5180@Lumen Station (innate, 520 Lumens) | craft r5180@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Black) | 4223 | not innate; Lumen Station 520 Lumens | r5181@Lumen Station (innate, 520 Lumens) | craft r5181@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Blue & Gold) | 4217 | not innate; Lumen Station 520 Lumens | r5175@Lumen Station (innate, 520 Lumens) | craft r5175@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Blue & Violet) | 4218 | not innate; Lumen Station 520 Lumens | r5176@Lumen Station (innate, 520 Lumens) | craft r5176@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Blue) | 4215 | not innate; Lumen Station 520 Lumens | r5173@Lumen Station (innate, 520 Lumens) | craft r5173@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Bronze) | 4220 | not innate; Lumen Station 520 Lumens | r5178@Lumen Station (innate, 520 Lumens) | craft r5178@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Maroon & Black) | 4216 | not innate; Lumen Station 520 Lumens | r5174@Lumen Station (innate, 520 Lumens) | craft r5174@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Purple & Black) | 4219 | not innate; Lumen Station 520 Lumens | r5177@Lumen Station (innate, 520 Lumens) | craft r5177@Lumen Station; Showcase (free) | as wiki |  |
| Slayer's War Leathers (Seafoam) | 4221 | not innate; Lumen Station 520 Lumens | r5179@Lumen Station (innate, 520 Lumens) | craft r5179@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Blue & Purple) | 4206 | not innate; Lumen Station 520 Lumens | r5164@Lumen Station (innate, 520 Lumens) | craft r5164@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Ivory) | 4211 | not innate; Lumen Station 520 Lumens | r5169@Lumen Station (innate, 520 Lumens) | craft r5169@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Ocean) | 4210 | not innate; Lumen Station 520 Lumens | r5168@Lumen Station (innate, 520 Lumens) | craft r5168@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Olive Green) | 4209 | not innate; Lumen Station 520 Lumens | r5167@Lumen Station (innate, 520 Lumens) | craft r5167@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Purple) | 4213 | not innate; Lumen Station 520 Lumens | r5171@Lumen Station (innate, 520 Lumens) | craft r5171@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Red) | 4208 | not innate; Lumen Station 520 Lumens | r5166@Lumen Station (innate, 520 Lumens) | craft r5166@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (Teal) | 4207 | not innate; Lumen Station 520 Lumens | r5165@Lumen Station (innate, 520 Lumens) | craft r5165@Lumen Station; Showcase (free) | as wiki |  |
| Spellweaver's Raiment (White) | 4214 | not innate; Lumen Station 520 Lumens | r5172@Lumen Station (innate, 520 Lumens) | craft r5172@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Blue) | 4330 | not innate; Lumen Station 520 Lumens | r5288@Lumen Station (innate, 520 Lumens) | craft r5288@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Bronze) | 4329 | not innate; Lumen Station 520 Lumens | r5287@Lumen Station (innate, 520 Lumens) | craft r5287@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Gold) | 4331 | not innate; Lumen Station 520 Lumens | r5289@Lumen Station (innate, 520 Lumens) | craft r5289@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Green) | 4327 | not innate; Lumen Station 520 Lumens | r5285@Lumen Station (innate, 520 Lumens) | craft r5285@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Grey) | 4328 | not innate; Lumen Station 520 Lumens | r5286@Lumen Station (innate, 520 Lumens) | craft r5286@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Orange) | 4326 | not innate; Lumen Station 520 Lumens | r5284@Lumen Station (innate, 520 Lumens) | craft r5284@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Purple) | 4333 | not innate; Lumen Station 520 Lumens | r5291@Lumen Station (innate, 520 Lumens) | craft r5291@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Red) | 4325 | not innate; Lumen Station 520 Lumens | r5283@Lumen Station (innate, 520 Lumens) | craft r5283@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (Teal) | 4334 | not innate; Lumen Station 520 Lumens | r5292@Lumen Station (innate, 520 Lumens) | craft r5292@Lumen Station; Showcase (free) | as wiki |  |
| Swordsman's Nimble Hauberk (White) | 4332 | not innate; Lumen Station 520 Lumens | r5290@Lumen Station (innate, 520 Lumens) | craft r5290@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Black) | 4243 | not innate; Lumen Station 520 Lumens | r5201@Lumen Station (innate, 520 Lumens) | craft r5201@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Green) | 4241 | not innate; Lumen Station 520 Lumens | r5199@Lumen Station (innate, 520 Lumens) | craft r5199@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Grey & Orange) | 4240 | not innate; Lumen Station 520 Lumens | r5198@Lumen Station (innate, 520 Lumens) | craft r5198@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Ivory & Blue) | 4244 | not innate; Lumen Station 520 Lumens | r5202@Lumen Station (innate, 520 Lumens) | craft r5202@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Red) | 4242 | not innate; Lumen Station 520 Lumens | r5200@Lumen Station (innate, 520 Lumens) | craft r5200@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Silver & Yellow) | 4246 | not innate; Lumen Station 520 Lumens | r5204@Lumen Station (innate, 520 Lumens) | craft r5204@Lumen Station; Showcase (free) | as wiki |  |
| Temple Guard's Uniform (Teal & Rose) | 4245 | not innate; Lumen Station 520 Lumens | r5203@Lumen Station (innate, 520 Lumens) | craft r5203@Lumen Station; Showcase (free) | as wiki |  |
| Terran Republic Commander's Gear | 1386 | not innate; Lumen Station 520 Lumens | r2514@Lumen Station (innate, 520 Lumens) | craft r2514@Lumen Station; Showcase (free) | as wiki |  |
| Traveler's Attire (Black) | 1249 | innate; Replicator | r813@Workshop/Replicator (innate) | craft r813@Workshop/Replicator; Showcase (free) | as wiki |  |
| Traveler's Attire (Emerald) | 1247 | not innate; Replicator | r812@Workshop (innate) | craft r812@Workshop; Showcase (free) | different route |  |
| Traveler's Attire (Green) | 1250 | innate; Replicator | r811@Workshop/Replicator (innate) | craft r811@Workshop/Replicator; Showcase (free) | as wiki |  |
| Traveler's Attire (Red) | 1246 | innate; Replicator | r810@Workshop/Replicator (innate) | craft r810@Workshop/Replicator; Showcase (free) | as wiki |  |
| Traveler's Attire (Yellow) | 1245 | innate; Replicator | r808@Workshop/Replicator (innate) | craft r808@Workshop/Replicator; Showcase (free) | as wiki |  |
| Treasure Seeker's Garb (Blue) | 1152 | not innate; Lumen Station 520 Lumens | r2549@Lumen Station (innate, 520 Lumens) | craft r2549@Lumen Station; Showcase (free) | as wiki |  |
| Treasure Seeker's Garb (Orange) | 1153 | not innate; Lumen Station 520 Lumens | r2551@Lumen Station (innate, 520 Lumens) | craft r2551@Lumen Station; Showcase (free) | as wiki |  |
| Treasure Seeker's Garb (Red) | 546 | not innate; Lumen Station 520 Lumens | r2550@Lumen Station (innate, 520 Lumens) | craft r2550@Lumen Station; Showcase (free) | as wiki |  |
| Treasure Seeker's Garb (White) | 1151 | not innate; Lumen Station 520 Lumens | r2548@Lumen Station (innate, 520 Lumens) | craft r2548@Lumen Station; Showcase (free) | as wiki |  |
| Vanu Sovereignty Commander's Gear | 1387 | not innate; Lumen Station 520 Lumens | r2513@Lumen Station (innate, 520 Lumens) | craft r2513@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Black & Yellow) | 4344 | not innate; Lumen Station 520 Lumens | r5302@Lumen Station (innate, 520 Lumens) | craft r5302@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Blue) | 4335 | not innate; Lumen Station 520 Lumens | r5293@Lumen Station (innate, 520 Lumens) | craft r5293@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Green) | 4336 | not innate; Lumen Station 520 Lumens | r5294@Lumen Station (innate, 520 Lumens) | craft r5294@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Grey & White) | 4341 | not innate; Lumen Station 520 Lumens | r5299@Lumen Station (innate, 520 Lumens) | craft r5299@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Mint) | 4345 | not innate; Lumen Station 520 Lumens | r5303@Lumen Station (innate, 520 Lumens) | craft r5303@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Olive) | 4343 | not innate; Lumen Station 520 Lumens | r5301@Lumen Station (innate, 520 Lumens) | craft r5301@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Purple & White) | 4340 | not innate; Lumen Station 520 Lumens | r5298@Lumen Station (innate, 520 Lumens) | craft r5298@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Red & Blue) | 4346 | not innate; Lumen Station 520 Lumens | r5304@Lumen Station (innate, 520 Lumens) | craft r5304@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Slate) | 4342 | not innate; Lumen Station 520 Lumens | r5300@Lumen Station (innate, 520 Lumens) | craft r5300@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Violet & White) | 4337 | not innate; Lumen Station 520 Lumens | r5295@Lumen Station (innate, 520 Lumens) | craft r5295@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (White) | 4339 | not innate; Lumen Station 520 Lumens | r5297@Lumen Station (innate, 520 Lumens) | craft r5297@Lumen Station; Showcase (free) | as wiki |  |
| Wanderer's Guise (Yellow) | 4338 | not innate; Lumen Station 520 Lumens | r5296@Lumen Station (innate, 520 Lumens) | craft r5296@Lumen Station; Showcase (free) | as wiki |  |
| Wandering Nomad's Finery (Black) | 770 | not innate; Lumen Station 520 Lumens | r2529@Lumen Station (innate, 520 Lumens) | craft r2529@Lumen Station; Showcase (free) | as wiki |  |
| Wandering Nomad's Finery (Dark Green) | 1278 | not innate; Lumen Station 520 Lumens | r2534@Lumen Station (innate, 520 Lumens) | craft r2534@Lumen Station; Showcase (free) | as wiki |  |
| Wandering Nomad's Finery (Grey & Blue) | 1094 | not innate; Lumen Station 520 Lumens | r2532@Lumen Station (innate, 520 Lumens) | craft r2532@Lumen Station; Showcase (free) | as wiki |  |
| Wandering Nomad's Finery (Navy Blue) | 805 | not innate; Lumen Station 520 Lumens | r2530@Lumen Station (innate, 520 Lumens) | craft r2530@Lumen Station; Showcase (free) | as wiki |  |
| Wandering Nomad's Finery (White) | 804 | not innate; Lumen Station 520 Lumens | r2531@Lumen Station (innate, 520 Lumens) | craft r2531@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Blue & Ivory) | 4174 | not innate; Lumen Station 520 Lumens | r5132@Lumen Station (innate, 520 Lumens) | craft r5132@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Blue & Orange) | 4180 | not innate; Lumen Station 520 Lumens | r5138@Lumen Station (innate, 520 Lumens) | craft r5138@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Blue) | 4170 | not innate; Lumen Station 520 Lumens | r5128@Lumen Station (innate, 520 Lumens) | craft r5128@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Bronze) | 4176 | not innate; Lumen Station 520 Lumens | r5134@Lumen Station (innate, 520 Lumens) | craft r5134@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Gold & Blue) | 4178 | not innate; Lumen Station 520 Lumens | r5136@Lumen Station (innate, 520 Lumens) | craft r5136@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Gold) | 4168 | not innate; Lumen Station 520 Lumens | r5126@Lumen Station (innate, 520 Lumens) | craft r5126@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Green & Purple) | 4177 | not innate; Lumen Station 520 Lumens | r5135@Lumen Station (innate, 520 Lumens) | craft r5135@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Green) | 4169 | not innate; Lumen Station 520 Lumens | r5127@Lumen Station (innate, 520 Lumens) | craft r5127@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Grey) | 4173 | not innate; Lumen Station 520 Lumens | r5131@Lumen Station (innate, 520 Lumens) | craft r5131@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Purple & Blue) | 4181 | not innate; Lumen Station 520 Lumens | r5139@Lumen Station (innate, 520 Lumens) | craft r5139@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Purple & Teal) | 4175 | not innate; Lumen Station 520 Lumens | r5133@Lumen Station (innate, 520 Lumens) | craft r5133@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Purple) | 4171 | not innate; Lumen Station 520 Lumens | r5129@Lumen Station (innate, 520 Lumens) | craft r5129@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Red) | 4179 | not innate; Lumen Station 520 Lumens | r5137@Lumen Station (innate, 520 Lumens) | craft r5137@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Rose & Blue) | 4167 | not innate; Lumen Station 520 Lumens | r5125@Lumen Station (innate, 520 Lumens) | craft r5125@Lumen Station; Showcase (free) | as wiki |  |
| Warmaster's Storm Armor (Rose & Gold) | 4172 | not innate; Lumen Station 520 Lumens | r5130@Lumen Station (innate, 520 Lumens) | craft r5130@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Black) | 4236 | not innate; Lumen Station 520 Lumens | r5194@Lumen Station (innate, 520 Lumens) | craft r5194@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Green) | 4233 | not innate; Lumen Station 520 Lumens | r5191@Lumen Station (innate, 520 Lumens) | craft r5191@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Ivory & Blue) | 4237 | not innate; Lumen Station 520 Lumens | r5195@Lumen Station (innate, 520 Lumens) | craft r5195@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Purple) | 4234 | not innate; Lumen Station 520 Lumens | r5192@Lumen Station (innate, 520 Lumens) | craft r5192@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Red) | 4235 | not innate; Lumen Station 520 Lumens | r5193@Lumen Station (innate, 520 Lumens) | craft r5193@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Silver & Yellow) | 4239 | not innate; Lumen Station 520 Lumens | r5197@Lumen Station (innate, 520 Lumens) | craft r5197@Lumen Station; Showcase (free) | as wiki |  |
| Warrior-Monk's Vestment (Teal & Rose) | 4238 | not innate; Lumen Station 520 Lumens | r5196@Lumen Station (innate, 520 Lumens) | craft r5196@Lumen Station; Showcase (free) | as wiki |  |
| Western Getup (Blue) | 2529 | not innate; Lumen Station 520 Lumens | r2565@Lumen Station (innate, 520 Lumens) | craft r2565@Lumen Station; Showcase (free) | as wiki |  |
| Western Getup (Green) | 2528 | not innate; Lumen Station 520 Lumens | r2566@Lumen Station (innate, 520 Lumens) | craft r2566@Lumen Station; Showcase (free) | as wiki |  |

### 4.6 Creature props (68)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| Abomination Skullslapper | 3146 | innate | prop 2435 innate | palette: innate; Showcase (free) | as wiki |  |
| Abomination Skulltosser | 3145 | not innate; on an Innate list | prop 2434 innate | palette: innate; Showcase (free) | as wiki |  |
| Abomination Slapper | 3144 | not innate; on an Innate list | prop 2433 innate | palette: innate; Showcase (free) | as wiki |  |
| Agitated Chomper | 3150 | not innate; on an Innate list | prop 2439 innate | palette: innate; Showcase (free) | as wiki |  |
| Barren Bowman | 3156 | not innate | prop 2445 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Fireshot | 3157 | not innate | prop 2446 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Flameblade | 3162 | not innate | prop 2451 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Flamemage | 3167 | not innate | prop 2456 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Grunt | 3161 | not innate | prop 2450 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Icebrute | 3163 | not innate | prop 2452 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Lightbringer | 3168 | not innate | prop 2457 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Protector | 3164 | not innate | prop 2453 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Shadowshot | 3158 | not innate | prop 2447 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Stormmancer | 3169 | not innate | prop 2458 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Barren Stormshot | 3159 | not innate | prop 2448 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Ebonguard Archer | 3237 | not innate | prop 2526 discovered: Recipe item 3565 (Lumen Station r2238@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Attacker | 3229 | not innate | prop 2518 discovered: Recipe item 3566 (Lumen Station r2239@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Brutalizer | 3230 | not innate | prop 2519 discovered: Recipe item 3567 (Lumen Station r2240@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Deathdealer | 3233 | not innate | prop 2522 discovered: Recipe item 3594 (Lumen Station r2267@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Firemancer | 3231 | not innate | prop 2520 discovered: Recipe item 3570 (Lumen Station r2243@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Flashmage | 3247 | not innate | prop 2536 discovered: Recipe item 3572 (Lumen Station r2245@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Plaguerider | 3251 | not innate | prop 2540 discovered: Recipe item 3598 (Lumen Station r2271@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Sentry | 3243 | not innate | prop 2532 discovered: Recipe item 3576 (Lumen Station r2249@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Shadowslayer | 3227 | not innate | prop 2516 discovered: Recipe item 3577 (Lumen Station r2250@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Toximancer | 3248 | not innate | prop 2537 discovered: Recipe item 3579 (Lumen Station r2252@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ancient Abomination | 3147 | not innate | prop 2436 discovered: Recipe item 3560 (Lumen Station r2233@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Barren Captain | 3166 | not innate | prop 2455 discovered: Recipe item 3561 (Lumen Station r2234@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Barren Lieutenant | 3165 | not innate | prop 2454 discovered: Recipe item 3562 (Lumen Station r2235@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Barren Mageking | 3171 | not innate | prop 2460 discovered: Recipe item 3563 (Lumen Station r2236@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ebonguard Commander | 3246 | not innate | prop 2535 discovered: Recipe item 3630 (Lumen Station r2303@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Enraged Chomper | 3151 | not innate | prop 2440 discovered: Recipe item 3580 (Lumen Station r2253@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Highfall Warrior | 3200 | not innate | prop 2489 discovered: Recipe item 3637 (Lumen Station r2310@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite NovaTech Alpha | 3214 | not innate | prop 2503 discovered: Recipe item 3639 (Lumen Station r2312@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite NovaTech Researcher | 3191 | not innate | prop 2480 discovered: Recipe item 3640 (Lumen Station r2313@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Scalewind Nightblade | 3186 | not innate | prop 2475 discovered: Recipe item 3644 (Lumen Station r2317@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Slaug Berserker | 3155 | not innate | prop 2444 discovered: Recipe item 3593 (Lumen Station r2266@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fiery Fungus | 3954 | not innate; on an Innate list | prop 2613 innate | palette: innate; Showcase (free) | as wiki |  |
| Highfall Bandit | 3943 | not innate | prop 2599 discovered: Recipe item 3967 (Lumen Station r2406@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Burstshot | 3201 | not innate | prop 2490 discovered: Recipe item 3581 (Lumen Station r2254@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Catapult | 4402 | not innate | prop 2941 discovered: Recipe item 4403 (Lumen Station r5330@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Centurion | 3942 | not innate | prop 2598 discovered: Recipe item 3966 (Lumen Station r2405@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Conjuror | 3935 | not innate | prop 2591 discovered: Recipe item 3959 (Lumen Station r2398@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Frostmancer | 3205 | not innate | prop 2494 discovered: Recipe item 3582 (Lumen Station r2255@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Lightblade | 3937 | not innate | prop 2593 discovered: Recipe item 3961 (Lumen Station r2400@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Lunging Chomper | 3148 | not innate; on an Innate list | prop 2437 innate | palette: innate; Showcase (free) | as wiki |  |
| Magma Giant | 3194 | not innate | prop 2483 discovered: Recipe item 3641 (Lumen Station r2314@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Colonist | 3215 | not innate | prop 2504 discovered: Recipe item 3607 (Lumen Station r2280@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Pilot | 3216 | not innate | prop 2505 discovered: Recipe item 3609 (Lumen Station r2282@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Pioneer | 3189 | not innate | prop 2478 discovered: Recipe item 3610 (Lumen Station r2283@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Scientist | 3190 | not innate | prop 2479 discovered: Recipe item 3611 (Lumen Station r2284@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Stormhound | 3213 | not innate | prop 2502 discovered: Recipe item 3612 (Lumen Station r2285@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Oakmire Stag | 3821 | not innate | prop 2612 discovered: Recipe item 3822 (Lumen Station r2392@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Powered Mega-Knight | 3955 | not innate | prop 2614 discovered: Recipe item 3979 (Lumen Station r2418@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Proximity Mine | 1314 | not innate | prop 1220 discovered: Recipe item 4394 (Lumen Station r5327@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Darkrunt | 3218 | not innate | prop 2507 discovered: Recipe item 3585 (Lumen Station r2258@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Dropper | 3173 | not innate | prop 2462 discovered: Recipe item 3586 (Lumen Station r2259@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Iceclipper | 3175 | not innate | prop 2464 discovered: Recipe item 3587 (Lumen Station r2260@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Lightrunt | 3219 | not innate | prop 2508 discovered: Recipe item 3588 (Lumen Station r2261@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Marine | 3180 | not innate | prop 2469 discovered: Recipe item 3621 (Lumen Station r2294@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Sparker | 3220 | not innate | prop 2509 discovered: Recipe item 3591 (Lumen Station r2264@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Toxifier | 3221 | not innate | prop 2510 discovered: Recipe item 3592 (Lumen Station r2265@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Weakener | 3223 | not innate | prop 2512 discovered: Recipe item 3626 (Lumen Station r2299@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Slaug Slasher | 3152 | not innate; on an Innate list | prop 2441 innate | palette: innate; Showcase (free) | as wiki |  |
| Slaug Stormer | 3153 | not innate; on an Innate list | prop 2442 innate | palette: innate; Showcase (free) | as wiki |  |
| Spitting Chomper | 3149 | not innate; on an Innate list | prop 2438 innate | palette: innate; Showcase (free) | as wiki |  |
| Stoneskin Banespitter | 3952 | not innate | prop 2608 discovered: Recipe item 3976 (Lumen Station r2415@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stoneskin Flamecrusher | 3947 | not innate | prop 2603 discovered: Recipe item 3971 (Lumen Station r2410@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Vine Trap | 4397 | not innate | prop 2932 discovered: Recipe item 4398 (Lumen Station r5328@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |

### 4.7 Props (847)

| Page [W] | Item | Wiki route [W] | Client data route [P] | Server route | Verdict | Notes |
|---|---|---|---|---|---|---|
| 1x1 Glass Window | 987 | innate | r682@no station mapping (never taught); prop 960 innate | palette: innate; Showcase (free) | as wiki |  |
| 2x1 Glass Window | 988 | innate | r683@no station mapping (never taught); prop 962 innate | palette: innate; Showcase (free) | as wiki |  |
| 3x2 Glass Window | 989 | innate | r684@no station mapping (never taught); prop 963 innate | palette: innate; Showcase (free) | as wiki |  |
| Amberleaf Plant | 735 | innate | r468@no station mapping (never taught); prop 497 innate | palette: innate; Showcase (free) | as wiki |  |
| Andiron | 349 | innate | r154@no station mapping (never taught); prop 658 innate | palette: innate; Showcase (free) | as wiki |  |
| Apples | 3714 | not innate | prop 2620 discovered: Recipe item 3877 (Lumen Station r5021@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Arched Fjorden Door | 2920 | not innate | prop 2207 discovered: Recipe item 3417 (Lumen Station r2090@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Arched Highfall Door (Left) | 2636 | not innate | r1821@no station mapping (taught with its prop); prop 2111 discovered: Recipe item 3266 (Lumen Station r1939@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Arched Highfall Door (Right) | 2640 | not innate | r1822@no station mapping (taught with its prop); prop 2115 discovered: Recipe item 3267 (Lumen Station r1940@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bahama Shutters | 1868 | not innate | r1255@no station mapping (taught with its prop); prop 1809 discovered: Recipe item 3511 (Lumen Station r2184@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Barrel (Large Iron) | 1846 | not innate | r1209@no station mapping (never taught); prop 1608 discovered: Recipe item 3418 (Lumen Station r2091@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Barrel (Large) | 294 | innate | r99@no station mapping (never taught); prop 603 innate | palette: innate; Showcase (free) | as wiki |  |
| Barrel (Medium) | 295 | not innate | r100@no station mapping (taught with its prop); prop 604 discovered: Recipe item 3268 (Lumen Station r1941@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Barrel (Utility) | 2508 | not innate | r1698@no station mapping (taught with its prop); prop 2048 discovered: Recipe item 3419 (Lumen Station r2092@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Barren Coldmage | 3170 | not innate | prop 2459 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Bat Colony Emitter | 2986 | not innate | prop 2264 discovered: Recipe item 3512 (Lumen Station r2185@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bath Tub | 1473 | not innate | r956@no station mapping (taught with its prop); prop 1411 discovered: Recipe item 3420 (Lumen Station r2093@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Batten Shutters, Left | 1874 | not innate | r1264@no station mapping (taught with its prop); prop 1817 discovered: Recipe item 3421 (Lumen Station r2094@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Batten Shutters, Right | 1882 | not innate | r1265@no station mapping (taught with its prop); prop 1825 discovered: Recipe item 3422 (Lumen Station r2095@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bear Ice Sculpture | 3792 | not innate | prop 2681 discovered: Recipe item 3879 (Lumen Station r5022@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Beaver Ice Sculpture | 3795 | not innate | prop 2684 discovered: Recipe item 3880 (Lumen Station r5023@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Blessed Hammer Mote Emitter | 2468 | not innate | prop 2030 discovered: Recipe item 3423 (Lumen Station r2096@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bloodtear Plant | 708 | innate | r441@no station mapping (never taught); prop 763 innate | palette: innate; Showcase (free) | as wiki |  |
| Bloodtear Plant (Large) | 709 | innate | r442@no station mapping (never taught); prop 764 innate | palette: innate; Showcase (free) | as wiki |  |
| Bloodtear Plant (Perfect) | 2799 | innate | prop 1620 innate | palette: innate; Showcase (free) | as wiki |  |
| Blue Emissive Cube | 2826 | not innate | prop 2136 discovered: Recipe item 3269 (Lumen Station r1942@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Blue Emissive Orb | 2815 | not innate | prop 2127 discovered: Recipe item 3270 (Lumen Station r1943@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Blue Light Orb | 967 | not innate | r675@no station mapping (taught with its prop); prop 951 discovered: Recipe item 3271 (Lumen Station r1944@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Blue Spotlight | 1180 | not innate | r895@no station mapping (taught with its prop); prop 1150 discovered: Recipe item 3272 (Lumen Station r1945@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bluebell Plant | 2800 | innate | prop 855 innate | palette: innate; Showcase (free) | as wiki |  |
| Bluebell Plant (Large) | 2801 | innate | prop 857 innate | palette: innate; Showcase (free) | as wiki |  |
| Bluebell Plant (Perfect) | 2802 | innate | prop 1623 innate | palette: innate; Showcase (free) | as wiki |  |
| Bouquet of Lillies | 1854 | innate | r1241@no station mapping (taught with its prop); prop 1792 discovered: Recipe item 3273 (Lumen Station r1946@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Braiser Pan (Large) | 1656 | not innate | r1047@no station mapping (taught with its prop); prop 1557 discovered: Recipe item 3275 (Lumen Station r1948@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Braiser Pan (Small) | 1659 | not innate | r1051@no station mapping (taught with its prop); prop 1560 discovered: Recipe item 3276 (Lumen Station r1949@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Brass Trimmed Basin | 1908 | not innate | r1222@no station mapping (never taught); prop 1614 discovered: Recipe item 3424 (Lumen Station r2097@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Brazier (Hanging Iron) | 961 | not innate | r670@no station mapping (taught with its prop); prop 777 discovered: Recipe item 3426 (Lumen Station r2099@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Brazier (Mounted Gothic) | 1518 | not innate | r979@no station mapping (taught with its prop); prop 1454 discovered: Recipe item 3427 (Lumen Station r2100@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Brazier (Stone) | 372 | not innate | r177@no station mapping (taught with its prop); prop 681 discovered: Recipe item 3428 (Lumen Station r2101@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Brazier (Wall Mounted) | 371 | not innate | r176@no station mapping (taught with its prop); prop 680 discovered: Recipe item 3429 (Lumen Station r2102@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bread (Sliced) with Cutting Board | 1338 | not innate | r851@no station mapping (taught with its prop); prop 1253 discovered: Recipe item 3278 (Lumen Station r1951@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bread with Cutting Board | 1339 | not innate | r852@no station mapping (taught with its prop); prop 1254 discovered: Recipe item 3279 (Lumen Station r1952@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bubbling Cauldron | 1506 | innate; event | r968@no station mapping (never taught); prop 1439 innate | palette: innate; Showcase (free) | as wiki |  |
| Buccaneer Captain's Chair | 3684 | not innate | prop 2572 discovered: Recipe item 3827 (Lumen Station r2477@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer Captain's Desk | 3697 | not innate | prop 2586 discovered: Recipe item 3839 (Lumen Station r2490@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Armchair | 3694 | not innate | prop 2582 discovered: Recipe item 3825 (Lumen Station r2487@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Banquet Table | 3690 | not innate | prop 2578 discovered: Recipe item 3837 (Lumen Station r2483@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Chandelier | 3689 | not innate | prop 2577 discovered: Recipe item 3831 (Lumen Station r2482@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Dining Chair | 3688 | not innate | prop 2576 discovered: Recipe item 3828 (Lumen Station r2481@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's End Table | 3692 | not innate | prop 2580 discovered: Recipe item 3829 (Lumen Station r2485@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Long Rug | 3686 | not innate | prop 2574 discovered: Recipe item 3830 (Lumen Station r2479@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Office Desk | 3696 | not innate | prop 2584 discovered: Recipe item 3838 (Lumen Station r2489@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Round Rug | 3685 | not innate | prop 2573 discovered: Recipe item 3832 (Lumen Station r2478@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Side Table | 3691 | not innate | prop 2579 discovered: Recipe item 3833 (Lumen Station r2484@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Sofa | 3695 | not innate | prop 2583 discovered: Recipe item 3834 (Lumen Station r2488@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Buccaneer's Wall Sconce | 3693 | not innate | prop 2581 discovered: Recipe item 3836 (Lumen Station r2486@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Bucket (Small Wooden) | 300 | innate | r105@no station mapping (never taught); prop 609 innate | palette: innate; Showcase (free) | as wiki |  |
| Burled Wood Coffee Table | 3784 | not innate | prop 2672 discovered: Recipe item 3854 (Lumen Station r5025@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Dining Bench | 3787 | not innate | prop 2675 discovered: Recipe item 3855 (Lumen Station r5026@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Dining Cabinet | 3780 | not innate | prop 2668 discovered: Recipe item 3856 (Lumen Station r5027@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Dining Chair | 3786 | not innate | prop 2674 discovered: Recipe item 3857 (Lumen Station r5028@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Dining Table | 3788 | not innate | prop 2676 discovered: Recipe item 3858 (Lumen Station r5029@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood End Table | 3783 | not innate | prop 2671 discovered: Recipe item 3859 (Lumen Station r5030@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Office Desk | 3789 | not innate | prop 2677 discovered: Recipe item 3861 (Lumen Station r5032@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Short Bookcase | 3781 | not innate | prop 2669 discovered: Recipe item 3862 (Lumen Station r5033@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Sleigh Bed | 3785 | not innate | prop 2673 discovered: Recipe item 3863 (Lumen Station r5034@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Sofa | 3779 | not innate | prop 2667 discovered: Recipe item 3864 (Lumen Station r5035@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Stool | 3776 | not innate | prop 2664 discovered: Recipe item 3865 (Lumen Station r5031@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Tall Bookcase | 3782 | not innate | prop 2670 discovered: Recipe item 3866 (Lumen Station r5037@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Burled Wood Vanity Table | 3778 | not innate | prop 2666 discovered: Recipe item 3867 (Lumen Station r5310@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Cake | 1340 | not innate | r853@no station mapping (taught with its prop); prop 1255 discovered: Recipe item 3280 (Lumen Station r1953@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Campfire Mote Emitter | 2112 | innate | prop 1882 innate | palette: innate; Showcase (free) | as wiki |  |
| Candle | 963 | innate | r695@no station mapping (never taught); prop 805 innate | palette: innate; Showcase (free) | as wiki |  |
| Candle Group (Large) | 964 | innate | r696@no station mapping (never taught); prop 773 innate | palette: innate; Showcase (free) | as wiki |  |
| Candle Group (Medium) | 965 | innate | r697@no station mapping (never taught); prop 803 innate | palette: innate; Showcase (free) | as wiki |  |
| Candy Cane (Green) | 1639 | innate; event | r1030@no station mapping (never taught); prop 1540 innate | palette: innate; Showcase (free) | as wiki |  |
| Candy Cane (Red) | 1641 | innate; event | r1032@no station mapping (never taught); prop 1542 innate | palette: innate; Showcase (free) | as wiki |  |
| Carved Hanging Horn | 564 | not innate | r339@no station mapping (taught with its prop); prop 797 discovered: Recipe item 3513 (Lumen Station r2186@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Castle Sawmill | 909 | not innate | r543@no station mapping (taught with its prop); prop 901 discovered: Recipe item 4374 (Lumen Station r5315@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Cavern Roots (Long with Dirt) | 1780 | innate | r1166@no station mapping (never taught); prop 1066 innate | palette: innate; Showcase (free) | as wiki |  |
| Cavern Roots (Long) | 1779 | innate | r1165@no station mapping (never taught); prop 1065 innate | palette: innate; Showcase (free) | as wiki |  |
| Cavern Roots (Wide with Dirt) 1 | 1782 | innate | r1168@no station mapping (never taught); prop 1068 innate | palette: innate; Showcase (free) | as wiki |  |
| Cavern Roots (Wide with Dirt) 2 | 1784 | innate | r1171@no station mapping (never taught); prop 1070 innate | palette: innate; Showcase (free) | as wiki |  |
| Cavern Roots (Wide) 1 | 1781 | innate | r1167@no station mapping (never taught); prop 1067 innate | palette: innate; Showcase (free) | as wiki |  |
| Cavern Roots (Wide) 2 | 1783 | innate | r1169@no station mapping (never taught); prop 1069 innate | palette: innate; Showcase (free) | as wiki |  |
| Chandelier (Intricate) | 1906 | not innate | r1220@no station mapping (never taught); prop 1756 discovered: Recipe item 3514 (Lumen Station r2187@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Chandelier (Iron) | 378 | not innate | r183@no station mapping (taught with its prop); prop 687 discovered: Recipe item 3431 (Lumen Station r2104@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Chest (Festive Gift Box) | 3133 | innate; event | prop 2358 innate | palette: innate; Showcase (free) | as wiki |  |
| Chest (Ornate Small) | 400 | not innate | r205@no station mapping (taught with its prop); prop 709 discovered: Recipe item 3515 (Lumen Station r2188@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Chest (Simple Large) | 396 | not innate | r201@no station mapping (taught with its prop); prop 705 discovered: Recipe item 3283 (Lumen Station r1956@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Chest (Simple Medium) | 2609 | innate; event | prop 2113 innate | palette: innate; Showcase (free) | as wiki |  |
| Chromasphere Mote Emitter | 2458 | not innate | prop 2022 discovered: Recipe item 3284 (Lumen Station r1957@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Coffee Table | 359 | innate | r164@no station mapping (never taught); prop 668 innate | palette: innate; Showcase (free) | as wiki |  |
| Coiled Rope | 1457 | not innate | r946@no station mapping (taught with its prop); prop 1398 discovered: Recipe item 3286 (Lumen Station r1959@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Construction Sign | 1690 | innate | r1093@no station mapping (never taught); prop 1592 innate | palette: innate; Showcase (free) | as wiki |  |
| Control Panel | 1205 | innate | r798@no station mapping (never taught); prop 1172 innate | palette: innate; Showcase (free) | as wiki |  |
| Control Panel (Round) | 1209 | not innate | r802@no station mapping (taught with its prop); prop 1176 discovered: Recipe item 3287 (Lumen Station r1960@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Control Panel (Small) | 1210 | not innate | r803@no station mapping (taught with its prop); prop 1177 discovered: Recipe item 3288 (Lumen Station r1961@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Control Point Mote Emitter | 2472 | not innate | prop 2034 discovered: Recipe item 3289 (Lumen Station r1962@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Coral (Short) | 2557 | innate | prop 2086 innate | palette: innate; Showcase (free) | as wiki |  |
| Coral (Tall) | 2556 | innate | prop 2085 innate | palette: innate; Showcase (free) | as wiki |  |
| Cordella Plant | 1793 | innate | r1172@no station mapping (never taught); prop 985 innate | palette: innate; Showcase (free) | as wiki |  |
| Cordella Plant (Large) | 1797 | innate | r1173@no station mapping (never taught); prop 986 innate | palette: innate; Showcase (free) | as wiki |  |
| Cordella Plant (Perfect) | 1798 | innate | r1174@no station mapping (never taught); prop 987 innate | palette: innate; Showcase (free) | as wiki |  |
| Cotton Plant | 736 | innate | r469@no station mapping (never taught); prop 501 innate | palette: innate; Showcase (free) | as wiki |  |
| Cotton Plant (Large) | 2803 | innate | prop 504 innate | palette: innate; Showcase (free) | as wiki |  |
| Cotton Plant (Perfect) | 2804 | innate | prop 1626 innate | palette: innate; Showcase (free) | as wiki |  |
| Crate (Utility) | 2509 | not innate | r1697@no station mapping (taught with its prop); prop 2056 discovered: Recipe item 3432 (Lumen Station r2105@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Crate (Wooden) | 398 | innate | r203@no station mapping (never taught); prop 707 innate | palette: innate; Showcase (free) | as wiki |  |
| Cross Brace | 1206 | innate | r799@no station mapping (never taught); prop 1173 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Glowing) 1 | 1791 | innate | r1181@no station mapping (never taught); prop 1077 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Glowing) 2 | 1792 | innate | r1182@no station mapping (never taught); prop 1078 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Glowing) 3 | 1794 | innate | r1183@no station mapping (never taught); prop 1079 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Medium) 1 | 1785 | innate | r1175@no station mapping (never taught); prop 1071 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Medium) 2 | 1786 | innate | r1176@no station mapping (never taught); prop 1072 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Medium) 3 | 1787 | innate | r1177@no station mapping (never taught); prop 1073 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Small) 1 | 1788 | innate | r1178@no station mapping (never taught); prop 1074 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Small) 2 | 1789 | innate | r1179@no station mapping (never taught); prop 1075 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystal (Small) 3 | 1790 | innate | r1180@no station mapping (never taught); prop 1076 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystia Plant | 1775 | innate | r1161@no station mapping (never taught); prop 1119 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystia Plant (Large) | 1776 | innate | r1162@no station mapping (never taught); prop 1120 innate | palette: innate; Showcase (free) | as wiki |  |
| Crystia Plant (Perfect) | 1777 | innate | r1164@no station mapping (never taught); prop 1624 innate | palette: innate; Showcase (free) | as wiki |  |
| Curved Hanging Chain | 1471 | not innate | r954@no station mapping (taught with its prop); prop 1399 discovered: Recipe item 3290 (Lumen Station r1963@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Curved Hanging Rope | 1458 | not innate | r947@no station mapping (taught with its prop); prop 1401 discovered: Recipe item 3291 (Lumen Station r1964@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Curved Highfall Door (Left) | 2635 | innate | r1819@no station mapping (never taught); prop 2110 innate | palette: innate; Showcase (free) | as wiki |  |
| Curved Highfall Door (Right) | 2639 | innate | r1820@no station mapping (never taught); prop 2114 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Log 1 | 2572 | innate | r1749@no station mapping (never taught); prop 434 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Log 2 | 2573 | innate | r1750@no station mapping (never taught); prop 435 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Sapling 1 | 2580 | innate | r1757@no station mapping (never taught); prop 1471 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Sapling 2 | 2581 | innate | r1758@no station mapping (never taught); prop 1516 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Sapling 3 | 2582 | innate | r1759@no station mapping (never taught); prop 1472 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Sapling 4 | 2583 | innate | r1760@no station mapping (never taught); prop 1517 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Stump (with Fireflies) 1 | 2765 | innate | prop 2099 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Stump 1 | 2584 | innate | r1761@no station mapping (never taught); prop 436 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Stump 2 | 2585 | innate | r1762@no station mapping (never taught); prop 437 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Stump 3 | 2586 | innate | r1763@no station mapping (never taught); prop 438 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Tree (Medium) 1 | 2574 | innate | r1751@no station mapping (never taught); prop 358 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Tree (Medium) 2 | 2575 | innate | r1752@no station mapping (never taught); prop 1513 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Tree (Medium) 3 | 2576 | innate | r1753@no station mapping (never taught); prop 359 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Tree (Medium) 4 | 2577 | innate | r1754@no station mapping (never taught); prop 1514 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Tree (Medium) 5 | 2578 | innate | r1755@no station mapping (never taught); prop 360 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Birch Tree (Medium) 6 | 2579 | innate | r1756@no station mapping (never taught); prop 1515 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Boulder 1 | 2771 | innate | prop 429 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Boulder 2 | 2772 | innate | prop 430 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Anemone 1 | 2766 | innate | prop 1477 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Anemone 2 | 2767 | innate | prop 1479 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Clamtrap 1 | 2763 | innate | prop 1528 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Clamtrap 2 | 2764 | innate | prop 1530 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed (Large with Butterflies) 1 | 2780 | innate | prop 2102 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed (Large with Butterflies) 2 | 2781 | innate | prop 2100 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed (Large) 1 | 2778 | innate | prop 1504 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed (Large) 2 | 2779 | innate | prop 1506 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed 1 | 2760 | innate | prop 1650 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed 2 | 2761 | innate | prop 1651 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Leafweed 3 | 2762 | innate | prop 1652 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Roots (Giant) 1 | 2757 | innate | prop 1508 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Roots (Giant) 2 | 2758 | innate | prop 1510 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Roots (Giant) 3 | 2759 | innate | prop 1512 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Shell (Spiral) 1 | 2773 | innate | prop 1603 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Shell (Spiral) 2 | 2774 | innate | prop 1606 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Shell (Whale) 1 | 2775 | innate | prop 1641 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Shell (Whale) 2 | 2776 | innate | prop 1642 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Cavern Shell (Whale) 3 | 2777 | innate | prop 1643 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Hedge (Leafy with Flies) 1 | 2749 | innate | prop 2097 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Hedge (Leafy) 1 | 2747 | innate | prop 370 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Hedge (Leafy) 2 | 2748 | innate | prop 371 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Plant Stalks 1 | 2752 | innate | prop 439 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Plant Stalks 2 | 2753 | innate | prop 441 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Plant Stalks 3 | 2754 | innate | prop 443 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Pumpkin 1 | 2782 | innate | prop 424 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Pumpkin 2 | 2783 | innate | prop 425 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Rock (Large Mossy) 1 | 2768 | innate | prop 431 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Rock (Large Mossy) 2 | 2769 | innate | prop 432 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Rock (Large Mossy) 3 | 2770 | innate | prop 433 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Rock (Small Mossy) 1 | 2750 | innate | prop 1474 innate | palette: innate; Showcase (free) | as wiki |  |
| Deciduous Rock (Small Mossy) 2 | 2751 | innate | prop 1475 innate | palette: innate; Showcase (free) | as wiki |  |
| Decorative Axe | 1652 | not innate | r1043@no station mapping (taught with its prop); prop 1553 discovered: Recipe item 3517 (Lumen Station r2190@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Decorative Shield | 1653 | not innate | r1044@no station mapping (taught with its prop); prop 1554 discovered: Recipe item 3518 (Lumen Station r2191@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Decorative Sword | 1654 | not innate | r1045@no station mapping (taught with its prop); prop 1555 discovered: Recipe item 3519 (Lumen Station r2192@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Deer Ice Sculpture | 3791 | not innate | prop 2680 discovered: Recipe item 3881 (Lumen Station r5040@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Desert Cactus | 694 | innate | r427@no station mapping (never taught); prop 332 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Cactus (Double) | 695 | innate | r428@no station mapping (never taught); prop 333 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Cactus (Tall with Flies) | 2717 | innate | prop 1059 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Cactus (Tall) | 696 | innate | r429@no station mapping (never taught); prop 334 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Flower | 698 | innate | r431@no station mapping (never taught); prop 249 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Flower (with Flies) 1 | 706 | innate | r439@no station mapping (never taught); prop 397 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Grass | 697 | innate | r430@no station mapping (never taught); prop 246 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Log | 640 | innate | r373@no station mapping (never taught); prop 257 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Log (with Flies) | 2720 | innate | prop 1060 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Paddle Tree (Medium) | 635 | innate | r368@no station mapping (never taught); prop 259 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Painting | 782 | not innate | r491@no station mapping (taught with its prop); prop 792 discovered: Recipe item 3520 (Lumen Station r2193@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Desert Rock (Medium) 1 | 671 | innate | r404@no station mapping (never taught); prop 252 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Rock (Medium) 2 | 672 | innate | r405@no station mapping (never taught); prop 253 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Rock (Medium) 3 | 2718 | innate | prop 767 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Rock (Small) 1 | 673 | innate | r406@no station mapping (never taught); prop 254 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Rock (Small) 2 | 674 | innate | r407@no station mapping (never taught); prop 255 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Rock 1 | 675 | innate | r408@no station mapping (never taught); prop 768 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Rock 2 | 676 | innate | r409@no station mapping (never taught); prop 769 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Scrub | 699 | innate | r432@no station mapping (never taught); prop 250 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Scrub (with Flies) 2 | 707 | innate | r440@no station mapping (never taught); prop 394 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Scrub Group 1 | 702 | innate | r435@no station mapping (never taught); prop 277 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Scrub Group 2 | 703 | innate | r436@no station mapping (never taught); prop 278 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Succulent (Bulbous Saw-Toothed) | 711 | innate | r444@no station mapping (never taught); prop 766 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Succulent (Bulbous) | 710 | innate | r443@no station mapping (never taught); prop 765 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Thorned Shrub | 701 | innate | r434@no station mapping (never taught); prop 276 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Thorned Shrub (with Butterflies) | 704 | innate | r437@no station mapping (never taught); prop 391 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Thornleaf (Medium) | 638 | innate | r371@no station mapping (never taught); prop 263 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Tree (Dead Medium with Flies) | 2716 | innate | prop 1061 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Tree (Dead Medium) | 641 | innate | r374@no station mapping (never taught); prop 258 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Tree (Dead Small with Flies) | 2719 | innate | prop 1062 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Tree (Dead Small) | 639 | innate | r372@no station mapping (never taught); prop 260 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Tree (Small) 1 | 636 | innate | r369@no station mapping (never taught); prop 261 innate | palette: innate; Showcase (free) | as wiki |  |
| Desert Tree (Small) 2 | 637 | innate | r370@no station mapping (never taught); prop 262 innate | palette: innate; Showcase (free) | as wiki |  |
| Direction Sign | 1691 | innate | r1094@no station mapping (never taught); prop 1593 innate | palette: innate; Showcase (free) | as wiki |  |
| Drylily Plant | 700 | innate | r433@no station mapping (never taught); prop 275 innate | palette: innate; Showcase (free) | as wiki |  |
| Drylily Plant (with Butterflies) | 705 | innate | r438@no station mapping (never taught); prop 392 innate | palette: innate; Showcase (free) | as wiki |  |
| Dust Devil Mote Emitter | 2469 | not innate | prop 2031 discovered: Recipe item 3433 (Lumen Station r2106@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Earthblood Plant | 737 | innate | r470@no station mapping (never taught); prop 505 innate | palette: innate; Showcase (free) | as wiki |  |
| Ebonguard Destroyer | 3242 | not innate | prop 2531 discovered: Recipe item 3568 (Lumen Station r2241@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Enforcer | 3244 | not innate | prop 2533 discovered: Recipe item 3595 (Lumen Station r2268@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Fireblade | 3225 | not innate | prop 2514 discovered: Recipe item 3569 (Lumen Station r2242@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Flashbrand | 3226 | not innate | prop 2515 discovered: Recipe item 3571 (Lumen Station r2244@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Flashshot | 3239 | not innate | prop 2528 discovered: Recipe item 3596 (Lumen Station r2269@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Frostshot | 3250 | not innate | prop 2539 discovered: Recipe item 3597 (Lumen Station r2270@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Nightcaller | 3235 | not innate | prop 2524 discovered: Recipe item 3573 (Lumen Station r2246@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Nightshot | 3249 | not innate | prop 2538 discovered: Recipe item 3574 (Lumen Station r2247@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Priestess | 3232 | not innate | prop 2521 discovered: Recipe item 3599 (Lumen Station r2272@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Sentinel | 3241 | not innate | prop 2530 discovered: Recipe item 3575 (Lumen Station r2248@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Skyshot | 3238 | not innate | prop 2527 discovered: Recipe item 3578 (Lumen Station r2251@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ebonguard Watcher | 3245 | not innate | prop 2534 discovered: Recipe item 3600 (Lumen Station r2273@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elbow Pipe | 1199 | not innate | r792@no station mapping (taught with its prop); prop 1166 discovered: Recipe item 3293 (Lumen Station r1966@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Electricity Mote Emitter | 2988 | innate | prop 2268 innate | palette: innate; Showcase (free) | as wiki |  |
| Elemental Barrage Mote Emitter | 2467 | not innate | prop 2029 discovered: Recipe item 3434 (Lumen Station r2107@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elf Crystal | 2855 | not innate | prop 2145 discovered: Recipe item 3547 (Lumen Station r2220@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ebonguard Captain | 3228 | not innate | prop 2517 discovered: Recipe item 3629 (Lumen Station r2302@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ebonguard Lightseeker | 3252 | not innate | prop 2541 discovered: Recipe item 3631 (Lumen Station r2304@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ebonguard Magi | 3236 | not innate | prop 2525 discovered: Recipe item 3632 (Lumen Station r2305@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ebonguard Pointer | 3240 | not innate | prop 2529 discovered: Recipe item 3633 (Lumen Station r2306@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Ebonguard Sorcerer | 3234 | not innate | prop 2523 discovered: Recipe item 3634 (Lumen Station r2307@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Highfall Gladiator | 3204 | not innate | prop 2493 discovered: Recipe item 3635 (Lumen Station r2308@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Highfall Magician | 3206 | not innate | prop 2495 discovered: Recipe item 3605 (Lumen Station r2278@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Highfall Outlaw | 3944 | not innate | prop 2600 discovered: Recipe item 3968 (Lumen Station r2407@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Highfall Wanderer | 3202 | not innate | prop 2491 discovered: Recipe item 3636 (Lumen Station r2309@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Highfall Witch | 3198 | not innate | prop 2487 discovered: Recipe item 3638 (Lumen Station r2311@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Scalewind Corruptor | 3187 | not innate | prop 2476 discovered: Recipe item 3642 (Lumen Station r2315@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Scalewind Icesmasher | 3185 | not innate | prop 2474 discovered: Recipe item 3643 (Lumen Station r2316@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Elite Scalewind Ranger | 3184 | not innate | prop 2473 discovered: Recipe item 3645 (Lumen Station r2318@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Embercup Plant | 2560 | innate | prop 1638 innate | palette: innate; Showcase (free) | as wiki |  |
| Embercup Plant (Perfect) | 2561 | innate | prop 1639 innate | palette: innate; Showcase (free) | as wiki |  |
| End Cap Pipe | 1200 | not innate | r793@no station mapping (taught with its prop); prop 1167 discovered: Recipe item 3294 (Lumen Station r1967@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| End Table (Small) | 360 | not innate | r165@no station mapping (taught with its prop); prop 669 discovered: Recipe item 3295 (Lumen Station r1968@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Explorer Flag | 436 |  | r272@Workshop (never taught); prop 384 innate | palette: innate; Showcase (free) | as client (wiki silent) |  |
| Fancy Candy Cane (Green) | 1640 | innate; event | r1031@no station mapping (never taught); prop 1541 innate | palette: innate; Showcase (free) | as wiki |  |
| Fancy Candy Cane (Red) | 1642 | innate; event | r1033@no station mapping (never taught); prop 1543 innate | palette: innate; Showcase (free) | as wiki |  |
| Festive Winter Holiday Tree | 3023 | innate; event | prop 2295 innate | palette: innate; Showcase (free) | as wiki |  |
| Fire Embers Mote Emitter | 2139 | innate | prop 1932 innate | palette: innate; Showcase (free) | as wiki |  |
| Fire Plume Type 1 Mote Emitter | 2142 | innate | prop 1935 innate | palette: innate; Showcase (free) | as wiki |  |
| Fire Plume Type 2 Mote Emitter | 2143 | innate | prop 1936 discovered: Recipe item 3296 (Lumen Station r1969@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Fireplace Screen | 1043 | not innate | r718@no station mapping (taught with its prop); prop 659 discovered: Recipe item 3297 (Lumen Station r1970@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fireplace Tools | 1044 | not innate | r719@no station mapping (taught with its prop); prop 660 discovered: Recipe item 3298 (Lumen Station r1971@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Book (Closed) | 2945 | not innate | prop 2229 discovered: Recipe item 3307 (Lumen Station r1980@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Book (Open) | 2944 | not innate | prop 2228 discovered: Recipe item 3308 (Lumen Station r1981@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Caber | 2955 | not innate | prop 2239 discovered: Recipe item 3309 (Lumen Station r1982@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Coal Piles | 2964 | not innate | prop 2244 discovered: Recipe item 3310 (Lumen Station r1983@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Fur (Draped) | 2938 | not innate | prop 2223 discovered: Recipe item 3311 (Lumen Station r1984@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Fur (Flat) | 2932 | not innate | prop 2217 discovered: Recipe item 3312 (Lumen Station r1985@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Fur Tanning Rack | 2940 | not innate | prop 2225 discovered: Recipe item 3313 (Lumen Station r1986@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Hide (Draped) | 2937 | not innate | prop 2222 discovered: Recipe item 3314 (Lumen Station r1987@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Hide (Flat) | 2931 | not innate | prop 2216 discovered: Recipe item 3315 (Lumen Station r1988@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Hide Tanning Rack | 2939 | not innate | prop 2224 discovered: Recipe item 3316 (Lumen Station r1989@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Iron Cauldron | 2956 | not innate | prop 2240 discovered: Recipe item 3438 (Lumen Station r2111@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Iron Chain | 2923 | not innate | prop 2210 discovered: Recipe item 3317 (Lumen Station r1990@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Iron Dragon Brazier | 2913 | not innate | prop 2201 discovered: Recipe item 3318 (Lumen Station r1991@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Light Fur (Draped) | 2936 | not innate | prop 2221 discovered: Recipe item 3319 (Lumen Station r1992@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Light Fur (Flat) | 2933 | not innate | prop 2218 discovered: Recipe item 3320 (Lumen Station r1993@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Log | 2951 | not innate | prop 2235 discovered: Recipe item 3321 (Lumen Station r1994@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Medium Fur (Draped) | 2935 | not innate | prop 2220 discovered: Recipe item 3322 (Lumen Station r1995@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Medium Fur (Flat) | 2934 | not innate | prop 2219 discovered: Recipe item 3323 (Lumen Station r1996@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Mounted Antlers | 2952 | not innate | prop 2236 discovered: Recipe item 3439 (Lumen Station r2112@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Mounted Buck | 2954 | not innate | prop 2238 discovered: Recipe item 3440 (Lumen Station r2113@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Mounted Wolf's Head | 2953 | not innate | prop 2237 discovered: Recipe item 3441 (Lumen Station r2114@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Portcullis | 2967 | not innate | prop 2247 discovered: Recipe item 3526 (Lumen Station r2199@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Portcullis Arch | 2968 | not innate | prop 2249 discovered: Recipe item 3527 (Lumen Station r2200@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Tusks | 2962 | not innate | prop 2242 discovered: Recipe item 3325 (Lumen Station r1998@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Tusks (Broken) | 2963 | not innate | prop 2243 discovered: Recipe item 3326 (Lumen Station r1999@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Wall Sconce | 2916 | not innate | prop 2204 discovered: Recipe item 3327 (Lumen Station r2000@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Wall Shield (Ornate) | 2946 | not innate | prop 2230 discovered: Recipe item 3443 (Lumen Station r2116@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Wall Shield (Simple) | 2947 | not innate | prop 2231 discovered: Recipe item 3442 (Lumen Station r2115@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Wall Shield (t) | 2948 | not innate | prop 2232 discovered: Recipe item 3444 (Lumen Station r2117@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fjorden Wall Shield (x) | 2949 | not innate | prop 2233 discovered: Recipe item 3445 (Lumen Station r2118@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Flame Geyser Eruption Mote Emitter | 2141 | not innate | prop 1934 discovered: Recipe item 3521 (Lumen Station r2194@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Flame Geyser Warning Mote Emitter | 2140 | not innate | prop 1933 discovered: Recipe item 3522 (Lumen Station r2195@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Flax Plant | 740 | innate | r473@no station mapping (never taught); prop 529 innate | palette: innate; Showcase (free) | as wiki |  |
| Floating Handle | 1207 | innate | r800@no station mapping (taught with its prop); prop 1174 discovered: Recipe item 3299 (Lumen Station r1972@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Floating Knob | 1204 | innate | r797@no station mapping (never taught); prop 1171 innate | palette: innate; Showcase (free) | as wiki |  |
| Floating Sphere | 1208 | not innate | r801@no station mapping (taught with its prop); prop 1175 discovered: Recipe item 3300 (Lumen Station r1973@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Flowers Stained Glass | 781 | not innate | r490@no station mapping (taught with its prop); prop 789 discovered: Recipe item 3523 (Lumen Station r2196@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fog (Medium) Mote Emitter | 2245 | not innate | prop 1957 discovered: Recipe item 3301 (Lumen Station r1974@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Fog (Small) Mote Emitter | 2246 | innate | prop 1958 innate | palette: innate; Showcase (free) | as wiki |  |
| Forest Painting | 783 | not innate | r492@no station mapping (taught with its prop); prop 801 discovered: Recipe item 3524 (Lumen Station r2197@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Frostbite Giant | 3193 | not innate | prop 2482 discovered: Recipe item 3601 (Lumen Station r2274@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Full Pie | 1674 | not innate | r1077@no station mapping (taught with its prop); prop 1579 discovered: Recipe item 3302 (Lumen Station r1975@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Gas Fire Mote Emitter | 2138 | not innate | prop 1931 discovered: Recipe item 3436 (Lumen Station r2109@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Gift Box (Green) | 1630 | innate; event | r1021@no station mapping (never taught); prop 1531 innate | palette: innate; Showcase (free) | as wiki |  |
| Gift Box (Red) | 1632 | innate; event | r1023@no station mapping (never taught); prop 1533 innate | palette: innate; Showcase (free) | as wiki |  |
| Gift Boxes (Stacked) | 1631 | innate; event | r1022@no station mapping (never taught); prop 1532 innate | palette: innate; Showcase (free) | as wiki |  |
| Gloom Giant | 3195 | not innate | prop 2484 discovered: Recipe item 3602 (Lumen Station r2275@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Gloombulb Plant | 743 | innate | r476@no station mapping (never taught); prop 520 innate | palette: innate; Showcase (free) | as wiki |  |
| Goblet | 1334 | innate | r847@no station mapping (never taught); prop 1249 innate | palette: innate; Showcase (free) | as wiki |  |
| Gothic Fence | 1507 | innate; event | r969@no station mapping (never taught); prop 1440 innate | palette: innate; Showcase (free) | as wiki |  |
| Gothic Fence Post | 1510 | innate; event | r972@no station mapping (never taught); prop 1443 innate | palette: innate; Showcase (free) | as wiki |  |
| Gothic Gate (Left) | 1519 | innate; event | r980@no station mapping (never taught); prop 1455 innate | palette: innate; Showcase (free) | as wiki |  |
| Gothic Gate (Right) | 1509 | innate; event | r971@no station mapping (never taught); prop 1442 innate | palette: innate; Showcase (free) | as wiki |  |
| Gravestone (Large) | 1511 | innate; event | r973@no station mapping (never taught); prop 1444 innate | palette: innate; Showcase (free) | as wiki |  |
| Gravestone (Medium) | 1512 | innate; event | r974@no station mapping (never taught); prop 1445 innate | palette: innate; Showcase (free) | as wiki |  |
| Gravestone (Small) | 1513 | innate; event | r975@no station mapping (never taught); prop 1446 innate | palette: innate; Showcase (free) | as wiki |  |
| Green Emissive Cube | 2827 | not innate | prop 2137 discovered: Recipe item 3303 (Lumen Station r1976@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Green Emissive Orb | 2819 | not innate | prop 2128 discovered: Recipe item 3304 (Lumen Station r1977@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Green Light Orb | 968 | not innate | r676@no station mapping (taught with its prop); prop 952 discovered: Recipe item 3305 (Lumen Station r1978@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Green Spotlight | 1181 | not innate | r896@no station mapping (taught with its prop); prop 1151 discovered: Recipe item 3306 (Lumen Station r1979@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Halloween Pumpkin (Fat) | 2978 | innate; event | prop 2256 innate | palette: innate; Showcase (free) | as wiki |  |
| Halloween Pumpkin (Tall) | 2977 | innate; event | prop 2255 innate | palette: innate; Showcase (free) | as wiki |  |
| Hanging Sconce (Small) | 382 | innate | r187@no station mapping (never taught); prop 691 innate | palette: innate; Showcase (free) | as wiki |  |
| Heavy Fjorden Door (Left) | 2921 | not innate | prop 2208 discovered: Recipe item 3446 (Lumen Station r2119@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Heavy Fjorden Door (Right) | 2925 | not innate | prop 2211 discovered: Recipe item 3447 (Lumen Station r2120@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Hexagonal Grate | 2495 | not innate | r1699@no station mapping (taught with its prop); prop 2050 discovered: Recipe item 3448 (Lumen Station r2121@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Befouler | 3197 | not innate | prop 2486 discovered: Recipe item 3603 (Lumen Station r2276@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Darkguard | 3940 | not innate | prop 2596 discovered: Recipe item 3964 (Lumen Station r2403@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Forceguard | 3938 | not innate | prop 2594 discovered: Recipe item 3962 (Lumen Station r2401@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Frostblade | 3199 | not innate | prop 2488 discovered: Recipe item 3604 (Lumen Station r2277@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Grand Bed | 2598 | not innate | r1746@no station mapping (taught with its prop); prop 2094 discovered: Recipe item 3516 (Lumen Station r2189@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Hanging Lamp | 2599 | innate | r1747@no station mapping (never taught); prop 2095 innate | palette: innate; Showcase (free) | as wiki |  |
| Highfall Shockblade | 3203 | not innate | prop 2492 discovered: Recipe item 3583 (Lumen Station r2256@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Tempest | 3936 | not innate | prop 2592 discovered: Recipe item 3960 (Lumen Station r2399@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Trooper | 3941 | not innate | prop 2597 discovered: Recipe item 3965 (Lumen Station r2404@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Highfall Warden | 3939 | not innate | prop 2595 discovered: Recipe item 3963 (Lumen Station r2402@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Hook Brace | 1482 | not innate | r965@no station mapping (taught with its prop); prop 1403 discovered: Recipe item 3331 (Lumen Station r2004@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Hot Spring Steam Mote Emitter | 2243 | not innate | prop 1955 discovered: Recipe item 3449 (Lumen Station r2122@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Human Bones | 2985 | innate; event | prop 2263 innate | palette: innate; Showcase (free) | as wiki |  |
| Human Bones with Skull | 2984 | innate; event | prop 2262 innate | palette: innate; Showcase (free) | as wiki |  |
| Human Femur | 2982 | innate; event | prop 2260 innate | palette: innate; Showcase (free) | as wiki |  |
| Human Skull | 2983 | innate; event | prop 2261 innate | palette: innate; Showcase (free) | as wiki |  |
| Indigo Emissive Cube | 2828 | not innate | prop 2138 discovered: Recipe item 3450 (Lumen Station r2123@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Indigo Emissive Orb | 2820 | not innate | prop 2129 discovered: Recipe item 3451 (Lumen Station r2124@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Indigo Light Orb | 969 | not innate | r677@no station mapping (taught with its prop); prop 953 discovered: Recipe item 3452 (Lumen Station r2125@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Indigo Spotlight | 1182 | not innate | r897@no station mapping (taught with its prop); prop 1152 discovered: Recipe item 3453 (Lumen Station r2126@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Intricate Copper-Banded Door (Left) | 1596 | not innate | r1013@no station mapping (taught with its prop); prop 1486 discovered: Recipe item 3332 (Lumen Station r2005@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Intricate Copper-Banded Door (Right) | 1599 | not innate | r1016@no station mapping (taught with its prop); prop 1489 discovered: Recipe item 3333 (Lumen Station r2006@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Intricate Copper-Plated Door (Left) | 1595 | not innate | r1012@no station mapping (taught with its prop); prop 1485 discovered: Recipe item 3334 (Lumen Station r2007@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Intricate Copper-Plated Door (Right) | 1598 | not innate | r1015@no station mapping (taught with its prop); prop 1488 discovered: Recipe item 3335 (Lumen Station r2008@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Intricate Wooden Door (Left) | 1597 | innate | r1014@no station mapping (taught with its prop); prop 1487 discovered: Recipe item 3336 (Lumen Station r2009@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Intricate Wooden Door (Right) | 1600 | innate | r1017@no station mapping (taught with its prop); prop 1490 discovered: Recipe item 3337 (Lumen Station r2010@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Iron Portcullis | 406 | not innate | prop 823 innate | palette: innate | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Iron Trimmed Basin | 565 | innate | r340@no station mapping (never taught); prop 798 innate | palette: innate; Showcase (free) | as wiki |  |
| Iron Trimmed Mug | 563 | innate | r338@no station mapping (never taught); prop 796 innate | palette: innate; Showcase (free) | as wiki |  |
| Jack o' Lantern (Fat) | 2979 | innate; event | prop 2257 innate | palette: innate; Showcase (free) | as wiki |  |
| Jack o' Lantern (Tall) | 2980 | innate; event | prop 2258 innate | palette: innate; Showcase (free) | as wiki |  |
| Jellyfish Mote Emitter | 2282 | not innate | prop 1960 discovered: Recipe item 3454 (Lumen Station r2127@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Jump Scare Skeleton | 2981 | innate; event | prop 2259 innate | palette: innate; Showcase (free) | as wiki |  |
| Junction Pipe | 1201 | not innate | r794@no station mapping (taught with its prop); prop 1168 discovered: Recipe item 3338 (Lumen Station r2011@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Jungle Banyan (Large with Mist) 1 | 2790 | innate | prop 1097 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan (Large with Mist) 2 | 2791 | innate | prop 1098 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan (Large) 1 | 627 | innate | r360@no station mapping (never taught); prop 205 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan (Large) 2 | 628 | innate | r361@no station mapping (never taught); prop 222 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan (Small) | 630 | innate | r363@no station mapping (never taught); prop 224 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan (Tiny) | 629 | innate | r362@no station mapping (never taught); prop 223 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan Root | 632 | innate | r365@no station mapping (never taught); prop 201 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Banyan Root (Dense) | 631 | innate | r364@no station mapping (never taught); prop 200 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Boulder 1 | 667 | innate | r400@no station mapping (never taught); prop 215 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Boulder 2 | 668 | innate | r401@no station mapping (never taught); prop 216 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cavern Vine (Long Hanging) | 1807 | innate | r1190@no station mapping (never taught); prop 838 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cavern Vine (Medium Hanging) | 1808 | innate | r1191@no station mapping (never taught); prop 839 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cavern Vine (Short Hanging) | 1809 | innate | r1193@no station mapping (never taught); prop 840 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cordate Cluster (with Butterflies) 3 | 2786 | innate | prop 1108 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cordate Cluster (with Fireflies) 2 | 2798 | innate | prop 1107 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cordate Cluster 1 | 2796 | innate | prop 841 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cordate Cluster 2 | 2797 | innate | prop 842 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Cordate Cluster 3 | 2785 | innate | prop 843 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Drupe (Glowing) 1 | 1800 | innate | r1187@no station mapping (never taught); prop 1103 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Drupe (Glowing) 2 | 1801 | innate | r1188@no station mapping (never taught); prop 1104 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Drupe (Glowing) 3 | 1802 | innate | r1189@no station mapping (never taught); prop 1105 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Fern (Medium with Butterflies) 2 | 2789 | innate | prop 1095 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Fern (Medium) 1 | 2787 | innate | prop 827 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Fern (Medium) 2 | 2788 | innate | prop 828 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Fern (with Butterflies) | 685 | innate | r418@no station mapping (never taught); prop 395 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Mist Mote Emitter | 2239 | not innate | prop 1952 discovered: Recipe item 3339 (Lumen Station r2012@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Jungle Palm (Curvy) | 633 | innate | r366@no station mapping (never taught); prop 225 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Hanging with Water) 1 | 1811 | innate | r1197@no station mapping (never taught); prop 1113 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Hanging with Water) 2 | 1813 | innate | r1199@no station mapping (never taught); prop 1115 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Hanging with Water) 3 | 1815 | innate | r1201@no station mapping (never taught); prop 1117 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Hanging) 1 | 1810 | innate | r1196@no station mapping (never taught); prop 1112 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Hanging) 2 | 1812 | innate | r1198@no station mapping (never taught); prop 1114 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Hanging) 3 | 1814 | innate | r1200@no station mapping (never taught); prop 1116 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palm (Tall) | 634 | innate | r367@no station mapping (never taught); prop 226 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palmetto | 625 | innate | r358@no station mapping (never taught); prop 198 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palmetto (Flat) | 624 | innate | r357@no station mapping (never taught); prop 197 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Palmetto (Tall) | 626 | innate | r359@no station mapping (never taught); prop 199 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Rock (Large) 1 | 665 | innate | r398@no station mapping (never taught); prop 217 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Rock (Large) 2 | 666 | innate | r399@no station mapping (never taught); prop 218 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Rock (Small) 1 | 669 | innate | r402@no station mapping (never taught); prop 219 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Rock (Small) 2 | 670 | innate | r403@no station mapping (never taught); prop 220 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Roots (Curled) 1 | 869 | innate | r509@no station mapping (never taught); prop 761 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Roots (Curled) 2 | 870 | innate | r510@no station mapping (never taught); prop 762 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Seedling 1 | 2792 | innate | prop 835 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Seedling 2 | 2793 | innate | prop 836 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Seedling 3 | 2794 | innate | prop 837 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Shrub | 687 | innate | r420@no station mapping (never taught); prop 203 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Shrub (Leafy) | 688 | innate | r421@no station mapping (never taught); prop 204 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Shrub (with Butterflies) | 686 | innate | r419@no station mapping (never taught); prop 396 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Tree Branch (Large) 1 | 1476 | innate | r959@no station mapping (never taught); prop 1414 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Tree Branch (Large) 2 | 1477 | innate | r960@no station mapping (never taught); prop 1415 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Tree Branch (Large) 3 | 1478 | innate | r961@no station mapping (never taught); prop 1416 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Tree Branch (Medium) | 1479 | innate | r962@no station mapping (never taught); prop 1417 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Tree Branch (Small) | 1480 | innate | r963@no station mapping (never taught); prop 1418 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Tree Trunk | 1481 | innate | r964@no station mapping (never taught); prop 1419 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Vines (Complex) | 690 | innate | r423@no station mapping (never taught); prop 213 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Vines (Simple) | 689 | innate | r422@no station mapping (never taught); prop 212 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Vines (Spread) | 691 | innate | r424@no station mapping (never taught); prop 214 innate | palette: innate; Showcase (free) | as wiki |  |
| Jungle Water Drops Mote Emitter | 2280 | not innate | prop 1959 discovered: Recipe item 3455 (Lumen Station r2128@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Jute Plant | 741 | innate | r474@no station mapping (never taught); prop 538 innate | palette: innate; Showcase (free) | as wiki |  |
| Jute Plant (Large) | 2805 | innate | prop 542 innate | palette: innate; Showcase (free) | as wiki |  |
| Jute Plant (Perfect) | 2806 | innate | prop 1621 innate | palette: innate; Showcase (free) | as wiki |  |
| Keg (Large Wood) | 1346 | not innate | r859@no station mapping (taught with its prop); prop 1261 discovered: Recipe item 3456 (Lumen Station r2129@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Keg (Medium) | 1333 | not innate | r846@no station mapping (taught with its prop); prop 1248 discovered: Recipe item 3340 (Lumen Station r2013@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Keg (Small Wood) | 1345 | innate | r858@no station mapping (never taught); prop 1260 innate | palette: innate; Showcase (free) | as wiki |  |
| Keg Tap | 3743 | innate | prop 2648 innate | palette: innate; Showcase (free) | as wiki |  |
| Lamp Post (Iron and Gold) | 1689 | not innate | r1092@no station mapping (taught with its prop); prop 1591 discovered: Recipe item 3341 (Lumen Station r2014@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Lamp Post (Iron) | 380 | innate | r185@no station mapping (never taught); prop 689 innate | palette: innate; Showcase (free) | as wiki |  |
| Lamp Post (Ornate) | 1455 | not innate | r945@no station mapping (taught with its prop); prop 1386 discovered: Recipe item 3457 (Lumen Station r2130@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Lamp Post (Wooden) | 1442 | not innate | r932@no station mapping (taught with its prop); prop 1379 discovered: Recipe item 3343 (Lumen Station r2016@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Large Bowl | 3777 | not innate | prop 2665 discovered: Recipe item 3887 (Lumen Station r5042@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Large Knotted Rope | 1460 | not innate | r949@no station mapping (taught with its prop); prop 1405 discovered: Recipe item 3344 (Lumen Station r2017@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Large Stinkhorn | 4116 | innate | prop 859 innate | palette: innate; Showcase (free) | as wiki |  |
| Lava Fire (Small) Mote Emitter | 2137 | innate | prop 1930 innate | palette: innate; Showcase (free) | as wiki |  |
| Lava Fire Mote Emitter | 2136 | innate | prop 1929 discovered: Recipe item 3346 (Lumen Station r2019@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Lion Ice Sculpture | 3794 | not innate | prop 2683 discovered: Recipe item 3882 (Lumen Station r5043@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Long Hanging Curtains | 1830 | not innate | r1225@no station mapping (taught with its prop); prop 1778 discovered: Recipe item 3348 (Lumen Station r2021@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Long Straight Pipe | 1203 | not innate | r796@no station mapping (taught with its prop); prop 1170 discovered: Recipe item 3349 (Lumen Station r2022@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Louvered Shutters, Left | 1890 | not innate | r1280@no station mapping (taught with its prop); prop 1833 discovered: Recipe item 3458 (Lumen Station r2131@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Louvered Shutters, Right | 1898 | not innate | r1282@no station mapping (taught with its prop); prop 1841 discovered: Recipe item 3459 (Lumen Station r2132@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Maggot Swarm Emitter | 2987 | not innate | prop 2267 discovered: Recipe item 3529 (Lumen Station r2202@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magic Fire (Blue) Mote Emitter | 2114 | innate | prop 1883 innate | palette: innate; Showcase (free) | as wiki |  |
| Magic Fire (Green) Mote Emitter | 2115 | innate | prop 1884 innate | palette: innate; Showcase (free) | as wiki |  |
| Magic Fire (Orange) Mote Emitter | 2121 | not innate | prop 1889 discovered: Recipe item 3350 (Lumen Station r2023@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magic Fire (Pink) Mote Emitter | 2116 | not innate | prop 1885 discovered: Recipe item 3351 (Lumen Station r2024@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magic Fire (Purple) Mote Emitter | 2117 | innate | prop 1886 innate | palette: innate; Showcase (free) | as wiki |  |
| Magic Fire (Red) Mote Emitter | 2118 | innate | prop 1887 discovered: Recipe item 3352 (Lumen Station r2025@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Magic Fire (Small Blue) Mote Emitter | 2123 | innate | prop 1890 innate | palette: innate; Showcase (free) | as wiki |  |
| Magic Fire (Small Green) Mote Emitter | 2124 | innate | prop 1891 innate | palette: innate; Showcase (free) | as wiki |  |
| Magic Fire (Small Pink) Mote Emitter | 2125 | not innate | prop 1892 discovered: Recipe item 3353 (Lumen Station r2026@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magic Fire (Small Purple) Mote Emitter | 2126 | not innate | prop 1919 discovered: Recipe item 3354 (Lumen Station r2027@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magic Fire (Small Red) Mote Emitter | 2127 | not innate; on an Innate list | prop 1920 innate | palette: innate; Showcase (free) | as wiki |  |
| Magic Fire (Small Yellow) Mote Emitter | 2128 | innate | prop 1921 discovered: Recipe item 3355 (Lumen Station r2028@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Magic Fire (Yellow) Mote Emitter | 2119 | not innate | prop 1888 discovered: Recipe item 3356 (Lumen Station r2029@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magical Monument | 900 | not innate | r539@no station mapping (taught with its prop); prop 881 discovered: Recipe item 4371 (Lumen Station r5312@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Magical Platform | 1289 | innate | r921@no station mapping (never taught); prop 1207 innate | palette: innate; Showcase (free) | as wiki |  |
| Mailbox | 569 | innate | r513@no station mapping (never taught); prop 489 innate | palette: innate; Showcase (free) | as wiki |  |
| Matte Ornament | 3011 | innate; event | prop 2287 innate | palette: innate; Showcase (free) | as wiki |  |
| Medium Fjorden Brazier | 2914 | not innate | prop 2202 discovered: Recipe item 3460 (Lumen Station r2133@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Metal Ring | 420 | not innate | r225@no station mapping (taught with its prop); prop 729 discovered: Recipe item 3357 (Lumen Station r2030@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Metal Switch | 1760 | not innate; on an Innate list | r1151@no station mapping (taught with its prop); prop 1599 discovered: Recipe item 3435 (Lumen Station r2108@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Microwave | 1688 | not innate | r1091@no station mapping (taught with its prop); prop 1589 discovered: Recipe item 3461 (Lumen Station r2134@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Miner's Banquet Table | 3746 | not innate | prop 2651 discovered: Recipe item 3868 (Lumen Station r5045@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Miner's Bench | 3744 | not innate | prop 2649 discovered: Recipe item 3870 (Lumen Station r5047@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Miner's End Table | 3740 | not innate | prop 2645 discovered: Recipe item 3871 (Lumen Station r5048@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Miner's Keg | 3741 | not innate | prop 2646 discovered: Recipe item 3853 (Lumen Station r5049@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Miner's Square Table | 3739 | not innate | prop 2644 discovered: Recipe item 3873 (Lumen Station r5050@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Miner's Stool | 3745 | not innate | prop 2650 discovered: Recipe item 3874 (Lumen Station r5051@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Mistletoe | 1651 | innate; event | r1042@no station mapping (never taught); prop 1552 innate | palette: innate; Showcase (free) | as wiki |  |
| Mixed Citrus | 3716 | not innate | prop 2622 discovered: Recipe item 3890 (Lumen Station r5052@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Molten Smelter | 895 | not innate | r531@no station mapping (taught with its prop); prop 892 discovered: Recipe item 4381 (Lumen Station r5322@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Mug | 1336 | innate | r849@no station mapping (never taught); prop 1251 innate | palette: innate; Showcase (free) | as wiki |  |
| Mug (Oxidized Brass) | 1867 | not innate | r1218@no station mapping (never taught); prop 1610 discovered: Recipe item 3462 (Lumen Station r2135@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Mushroom (Tall Glowing) 1 | 1772 | innate | r1159@no station mapping (never taught); prop 264 innate | palette: innate; Showcase (free) | as wiki |  |
| Mushroom (Tall Glowing) 2 | 1774 | innate | r1160@no station mapping (never taught); prop 265 innate | palette: innate; Showcase (free) | as wiki |  |
| Mushroom Group (Glowing) | 1771 | innate | r1158@no station mapping (never taught); prop 268 innate | palette: innate; Showcase (free) | as wiki |  |
| Nightbloom Plant | 692 | innate | r425@no station mapping (never taught); prop 209 innate | palette: innate; Showcase (free) | as wiki |  |
| Nightbloom Plant (Large) | 693 | innate | r426@no station mapping (never taught); prop 210 innate | palette: innate; Showcase (free) | as wiki |  |
| Nightbloom Plant (Perfect) | 2807 | innate | prop 1622 innate | palette: innate; Showcase (free) | as wiki |  |
| Nomad's Crown Plant | 2562 | innate | prop 1663 innate | palette: innate; Showcase (free) | as wiki |  |
| Nomad's Crown Plant (Large) | 2563 | innate | prop 1664 innate | palette: innate; Showcase (free) | as wiki |  |
| Nomad's Crown Plant (Perfect) | 2564 | innate | prop 1665 innate | palette: innate; Showcase (free) | as wiki |  |
| NovaTech Armchair | 3729 | not innate | prop 2635 discovered: Recipe item 3826 (Lumen Station r5053@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Blighthound | 3212 | not innate | prop 2501 discovered: Recipe item 3606 (Lumen Station r2279@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Dining Table | 3730 | not innate | prop 2636 discovered: Recipe item 3842 (Lumen Station r5054@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Divider 1 | 3736 | not innate | prop 2642 discovered: Recipe item 3843 (Lumen Station r5055@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Divider 2 | 3737 | not innate | prop 2643 discovered: Recipe item 3844 (Lumen Station r5056@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Double Bed | 3728 | not innate | prop 2634 discovered: Recipe item 3845 (Lumen Station r5057@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Firehound | 3211 | not innate | prop 2500 discovered: Recipe item 3584 (Lumen Station r2257@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Oval Platter | 3723 | not innate | prop 2629 discovered: Recipe item 3840 (Lumen Station r5058@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Square Platter | 3725 | not innate | prop 2631 discovered: Recipe item 3841 (Lumen Station r5059@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Stool | 3732 | not innate | prop 2638 discovered: Recipe item 3851 (Lumen Station r5060@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Urn | 3735 | not innate | prop 2641 discovered: Recipe item 3846 (Lumen Station r5061@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Utility Bowl | 3727 | not innate | prop 2633 discovered: Recipe item 3847 (Lumen Station r5062@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Utility Plate | 3720 | not innate | prop 2626 discovered: Recipe item 3848 (Lumen Station r5063@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| NovaTech Wheel | 3731 | not innate | prop 2637 discovered: Recipe item 3849 (Lumen Station r5064@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Obelisk Glow Mote Emitter | 2461 | not innate | prop 2024 discovered: Recipe item 3463 (Lumen Station r2136@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Obsidian Obelisk | 402 | not innate | r207@no station mapping (taught with its prop); prop 711 discovered: Recipe item 3530 (Lumen Station r2203@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ocean Kelp (Large with Jellyfish) | 2740 | innate | prop 2104 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Kelp (Large) | 2739 | innate | prop 1808 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Kelp (Medium with Fish) 1 | 2742 | innate | prop 2117 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Kelp (Medium with Fish) 2 | 2743 | innate | prop 2118 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Kelp (Medium with Silt) | 2741 | innate | prop 2105 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Kelp (Medium) | 2738 | innate | prop 1807 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Reef Rock | 2384 | innate | r1393@no station mapping (never taught); prop 1966 innate | palette: innate; Showcase (free) | as wiki |  |
| Ocean Rock (Bubble Vent) | 2744 | innate | prop 2091 innate | palette: innate; Showcase (free) | as wiki |  |
| Oil Geyser Eruption Mote Emitter | 2286 | not innate | prop 1964 discovered: Recipe item 3531 (Lumen Station r2204@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Oil Geyser Warning Mote Emitter | 2285 | not innate | prop 1963 discovered: Recipe item 3532 (Lumen Station r2205@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Old Growth Bell Cluster | 725 | innate | r458@no station mapping (never taught); prop 403 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Bump Cluster | 726 | innate | r459@no station mapping (never taught); prop 404 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Cone | 1799 | innate | r1186@no station mapping (never taught); prop 1149 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Diamond Pine (Large) 1 | 659 | innate | r392@no station mapping (never taught); prop 354 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Diamond Pine (Large) 2 | 658 | innate | r391@no station mapping (never taught); prop 353 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Fern (Curled) 2 | 730 | innate | r463@no station mapping (never taught); prop 375 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Fern Cluster 1 | 732 | innate | r465@no station mapping (never taught); prop 378 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Fern Cluster 2 | 733 | innate | r466@no station mapping (never taught); prop 379 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Ferns (Mixed with Fireflies) 2 | 2746 | innate | prop 1102 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Ferns (Mixed) 1 | 734 | innate | r467@no station mapping (never taught); prop 380 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Ferns (Mixed) 2 | 731 | innate | r464@no station mapping (never taught); prop 377 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Fungus Shelf (Medium) | 728 | innate | r461@no station mapping (never taught); prop 406 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Fungus Shelf (Small) | 727 | innate | r460@no station mapping (never taught); prop 405 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Gnarled Pine | 657 | innate | r390@no station mapping (never taught); prop 352 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Gnarled Pine (Large) | 660 | innate | r393@no station mapping (never taught); prop 355 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Gnarled Pine (Small) 1 | 661 | innate | r394@no station mapping (never taught); prop 356 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Gnarled Pine (Small) 2 | 662 | innate | r395@no station mapping (never taught); prop 357 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Honey Dripper | 723 | innate | r456@no station mapping (never taught); prop 401 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Honey Dripper Group | 724 | innate | r457@no station mapping (never taught); prop 402 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Rock (Mossy Large 1) | 679 | innate | r412@no station mapping (never taught); prop 408 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Rock (Mossy Large 2) | 680 | innate | r413@no station mapping (never taught); prop 409 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Rock (Mossy Small) 1 | 681 | innate | r414@no station mapping (never taught); prop 415 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Rock (Mossy Small) 2 | 682 | innate | r415@no station mapping (never taught); prop 416 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Rock (Mossy) 1 | 677 | innate | r410@no station mapping (never taught); prop 399 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Rock (Mossy) 2 | 678 | innate | r411@no station mapping (never taught); prop 400 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Root Tree 1 | 1061 | innate | prop 469 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Root Tree 2 | 1062 | innate | prop 470 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Bare Medium) | 656 | innate | r389@no station mapping (never taught); prop 351 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Hollowed) 1 | 663 | innate | r396@no station mapping (never taught); prop 463 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Hollowed) 2 | 664 | innate | r397@no station mapping (never taught); prop 468 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Huge) 1 | 652 | innate | r385@no station mapping (never taught); prop 343 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Huge) 2 | 653 | innate | r386@no station mapping (never taught); prop 344 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Medium) 1 | 654 | innate | r387@no station mapping (never taught); prop 345 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree (Medium) 2 | 655 | innate | r388@no station mapping (never taught); prop 349 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree Huge (with Butterflies 2) | 651 | innate | r384@no station mapping (never taught); prop 390 innate | palette: innate; Showcase (free) | as wiki |  |
| Old Growth Tree Huge (with Butterflies) 1 | 650 | innate | r383@no station mapping (never taught); prop 389 innate | palette: innate; Showcase (free) | as wiki |  |
| Orange Emissive Cube | 2829 | not innate | prop 2139 discovered: Recipe item 3465 (Lumen Station r2138@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Orange Emissive Orb | 2821 | not innate | prop 2130 discovered: Recipe item 3466 (Lumen Station r2139@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Orange Light Orb | 970 | not innate | r678@no station mapping (taught with its prop); prop 954 discovered: Recipe item 3467 (Lumen Station r2140@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Orange Pile | 3717 | not innate | prop 2623 discovered: Recipe item 3891 (Lumen Station r5065@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Orange Spotlight | 1183 | not innate | r898@no station mapping (taught with its prop); prop 1153 discovered: Recipe item 3468 (Lumen Station r2141@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ornament Wreath | 3013 | innate; event | prop 2289 innate | palette: innate; Showcase (free) | as wiki |  |
| Ornate Pitcher | 363 | innate | r168@no station mapping (never taught); prop 672 innate | palette: innate; Showcase (free) | as wiki |  |
| Ornate Stool | 1444 | not innate | r934@no station mapping (taught with its prop); prop 1381 discovered: Recipe item 3469 (Lumen Station r2142@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ornate Vase | 1440 | not innate | r930@no station mapping (taught with its prop); prop 1377 discovered: Recipe item 3470 (Lumen Station r2143@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ornate Wooden Chair | 346 | not innate | r151@no station mapping (taught with its prop); prop 655 discovered: Recipe item 3533 (Lumen Station r2206@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Outhouse Door | 2386 | innate | r1394@no station mapping (never taught); prop 1969 innate | palette: innate; Showcase (free) | as wiki |  |
| Owlbear Ice Sculpture | 3793 | not innate | prop 2682 discovered: Recipe item 3883 (Lumen Station r5066@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Padded Armchair | 1042 | not innate | r717@no station mapping (taught with its prop); prop 912 discovered: Recipe item 3358 (Lumen Station r2031@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Padded Sofa | 1041 | not innate | r716@no station mapping (taught with its prop); prop 845 discovered: Recipe item 3359 (Lumen Station r2032@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painted Wooden Chair | 994 | not innate | r688@no station mapping (taught with its prop); prop 966 discovered: Recipe item 3471 (Lumen Station r2144@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painted Wooden Settle | 339 | not innate | r750@no station mapping (taught with its prop); prop 648 discovered: Recipe item 3472 (Lumen Station r2145@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Deep Mines | 3747 | not innate | prop 2878 discovered: Recipe item 4128 (Lumen Station r5085@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Dungeon of Decay | 4083 | not innate | prop 2860 discovered: Recipe item 4108 (Lumen Station r5013@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Exploring the Undergrowth | 3897 | not innate | prop 2695 discovered: Recipe item 4109 (Lumen Station r5014@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Fiery Temple | 3898 | not innate | prop 2696 discovered: Recipe item 4110 (Lumen Station r5015@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Here be Dragons | 4082 | not innate | prop 2859 discovered: Recipe item 4111 (Lumen Station r5016@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Hidden Grotto | 3895 | not innate | prop 2693 discovered: Recipe item 4112 (Lumen Station r5017@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Shrine of the Sky | 4084 | not innate | prop 2861 discovered: Recipe item 4113 (Lumen Station r5018@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Sundered Lands | 3896 | not innate | prop 2694 discovered: Recipe item 4114 (Lumen Station r5019@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: The Badlands | 4124 | not innate | prop 2697 discovered: Recipe item 4115 (Lumen Station r5020@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: The Burned Woods | 3899 | not innate | prop 2879 discovered: Recipe item 4125 (Lumen Station r5083@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Painting: Withered Lands | 4127 | not innate | prop 2880 discovered: Recipe item 4126 (Lumen Station r5084@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Partial Pie | 1675 | not innate | r1078@no station mapping (taught with its prop); prop 1583 discovered: Recipe item 3360 (Lumen Station r2033@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Particle Forge | 913 | innate | r639@no station mapping (never taught); prop 904 innate | palette: innate; Showcase (free) | as wiki |  |
| Particle Smelter | 896 | innate | r532@no station mapping (never taught); prop 893 innate | palette: innate; Showcase (free) | as wiki |  |
| Pig on a Spit | 3005 | not innate | prop 2281 discovered: Recipe item 3535 (Lumen Station r2208@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Plate of Apples | 3721 | not innate | prop 2627 discovered: Recipe item 3878 (Lumen Station r5067@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Plate of Citrus | 3719 | not innate | prop 2625 discovered: Recipe item 3889 (Lumen Station r5068@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Plate of Oranges | 3722 | not innate | prop 2628 discovered: Recipe item 3888 (Lumen Station r5069@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Plumthistle Plant | 2808 | innate | prop 848 innate | palette: innate; Showcase (free) | as wiki |  |
| Plumthistle Plant (Large) | 2810 | innate | prop 850 innate | palette: innate; Showcase (free) | as wiki |  |
| Plumthistle Plant (Perfect) | 2809 | innate | prop 1753 innate | palette: innate; Showcase (free) | as wiki |  |
| Plush Pillow | 1055 | not innate | r729@no station mapping (taught with its prop); prop 979 discovered: Recipe item 3361 (Lumen Station r2034@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Red Emissive Cube | 2830 | not innate | prop 2140 discovered: Recipe item 3362 (Lumen Station r2035@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Red Emissive Orb | 2822 | not innate | prop 2131 discovered: Recipe item 3363 (Lumen Station r2036@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Red Light Orb | 971 | not innate | r679@no station mapping (taught with its prop); prop 955 discovered: Recipe item 3364 (Lumen Station r2037@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Red Spotlight | 1184 | not innate | r899@no station mapping (taught with its prop); prop 1154 discovered: Recipe item 3365 (Lumen Station r2038@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Regal Armoire | 1283 | not innate | r837@no station mapping (taught with its prop); prop 1187 discovered: Recipe item 3475 (Lumen Station r2148@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Regal Dresser | 1281 | not innate | r835@no station mapping (taught with its prop); prop 1185 discovered: Recipe item 3476 (Lumen Station r2149@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Regal Fireplace | 1275 | not innate | r832@no station mapping (taught with its prop); prop 1181 discovered: Recipe item 3536 (Lumen Station r2209@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Regal Master Bed | 1188 | not innate | r786@no station mapping (taught with its prop); prop 1159 discovered: Recipe item 3366 (Lumen Station r2039@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ring Brace | 1483 | not innate | r966@no station mapping (taught with its prop); prop 1404 discovered: Recipe item 3367 (Lumen Station r2040@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Ring Brace (Knotted) | 1484 | not innate | r967@no station mapping (taught with its prop); prop 1406 discovered: Recipe item 3368 (Lumen Station r2041@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Roast | 1343 | not innate | r856@no station mapping (taught with its prop); prop 1258 discovered: Recipe item 3477 (Lumen Station r2150@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Roast (Sliced) | 1344 | not innate | r857@no station mapping (taught with its prop); prop 1259 discovered: Recipe item 3478 (Lumen Station r2151@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Roast Turkey | 3715 | not innate | prop 2621 discovered: Recipe item 3876 (Lumen Station r5070@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Round Cushion | 1147 | not innate | r753@no station mapping (taught with its prop); prop 1146 discovered: Recipe item 3369 (Lumen Station r2042@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Round Pillow | 1050 | not innate | r724@no station mapping (taught with its prop); prop 972 discovered: Recipe item 3370 (Lumen Station r2043@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Row of Heavy Books | 1195 | not innate | r788@no station mapping (taught with its prop); prop 1162 discovered: Recipe item 3371 (Lumen Station r2044@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Row of Thin Books | 1197 | not innate | r790@no station mapping (taught with its prop); prop 1164 discovered: Recipe item 3372 (Lumen Station r2045@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rug (Large Rectangular) | 353 | not innate | r158@no station mapping (taught with its prop); prop 662 discovered: Recipe item 3479 (Lumen Station r2152@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rug (Small Round) | 354 | not innate | r159@no station mapping (taught with its prop); prop 663 discovered: Recipe item 3373 (Lumen Station r2046@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rustic Bookcase | 341 | not innate | r146@no station mapping (taught with its prop); prop 650 discovered: Recipe item 3481 (Lumen Station r2154@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rustic Stone Vault | 497 | not innate | r306@no station mapping (taught with its prop); prop 752 discovered: Recipe item 3537 (Lumen Station r2210@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rustic Wood Door (Left) | 408 | innate | r213@no station mapping (never taught); prop 717 innate | palette: innate; Showcase (free) | as wiki |  |
| Rustic Wood Door (Right) | 407 | innate | r212@no station mapping (never taught); prop 716 innate | palette: innate; Showcase (free) | as wiki |  |
| Rusty Brewhouse (Large) | 899 | not innate | r535@no station mapping (taught with its prop); prop 896 discovered: Recipe item 4372 (Lumen Station r5313@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rusty Brewhouse (Medium) | 898 | not innate | r534@no station mapping (taught with its prop); prop 895 discovered: Recipe item 4378 (Lumen Station r5319@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rusty Brewhouse (Small) | 897 | innate | r533@no station mapping (never taught); prop 894 innate | palette: innate; Showcase (free) | as wiki |  |
| Rusty Salvager (Large) | 902 | not innate | prop 883 discovered: Recipe item 4375 (Lumen Station r5316@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rusty Salvager (Medium) | 901 | not innate | prop 882 discovered: Recipe item 4382 (Lumen Station r5323@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rusty Salvager (Small) | 4364 | innate | prop 2891 innate | palette: innate; Showcase (free) | as wiki |  |
| Rusty Sifter (Large) | 534 | not innate | r318@Forge (innate); prop 760 discovered: Recipe item 4376 (Lumen Station r5317@Lumen Station (innate, 260 Lumens)) | craft r318@Forge; palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Rusty Sifter (Medium) | 533 | innate | r317@no station mapping (taught with its prop); prop 759 discovered: Recipe item 4383 (Lumen Station r5324@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Rusty Sifter (Small) | 513 | innate | r311@no station mapping (never taught); prop 754 innate | palette: innate; Showcase (free) | as wiki |  |
| Rusty Smelter | 894 | innate | r530@no station mapping (never taught); prop 891 innate | palette: innate; Showcase (free) | as wiki |  |
| Rusty Workshop | 892 | innate | r528@Workshop (innate); prop 889 innate | craft r528@Workshop; palette: innate; Showcase (free) | as wiki |  |
| Saloon Door (Left) | 2387 | not innate | r1395@no station mapping (taught with its prop); prop 1970 discovered: Recipe item 3376 (Lumen Station r2049@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Saloon Door (Right) | 2388 | not innate | r1396@no station mapping (taught with its prop); prop 1972 discovered: Recipe item 3377 (Lumen Station r2050@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Saucier Pan | 1662 | not innate | r1063@no station mapping (taught with its prop); prop 1563 discovered: Recipe item 3378 (Lumen Station r2051@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Burner | 3177 | not innate | prop 2466 discovered: Recipe item 3613 (Lumen Station r2286@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Chiller | 3208 | not innate | prop 2497 discovered: Recipe item 3614 (Lumen Station r2287@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Coldhunter | 3210 | not innate | prop 2499 discovered: Recipe item 3615 (Lumen Station r2288@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Darkener | 3224 | not innate | prop 2513 discovered: Recipe item 3616 (Lumen Station r2289@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Flamehunter | 3183 | not innate | prop 2472 discovered: Recipe item 3618 (Lumen Station r2291@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Hunter | 3209 | not innate | prop 2498 discovered: Recipe item 3619 (Lumen Station r2292@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Icer | 3178 | not innate | prop 2467 discovered: Recipe item 3620 (Lumen Station r2293@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Nightstalker | 3182 | not innate | prop 2471 discovered: Recipe item 3622 (Lumen Station r2295@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Recruit | 3172 | not innate | prop 2461 discovered: Recipe item 3589 (Lumen Station r2262@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Scout | 3174 | not innate | prop 2463 discovered: Recipe item 3590 (Lumen Station r2263@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Shaman | 3176 | not innate | prop 2465 discovered: Recipe item 3623 (Lumen Station r2296@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Smasher | 3222 | not innate | prop 2511 discovered: Recipe item 3624 (Lumen Station r2297@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Thunderer | 3207 | not innate | prop 2496 discovered: Recipe item 3625 (Lumen Station r2298@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scalewind Zapper | 3179 | not innate | prop 2468 discovered: Recipe item 3627 (Lumen Station r2300@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Scrying Pool Mote Emitter | 2464 | not innate | prop 2026 discovered: Recipe item 3482 (Lumen Station r2155@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Seahorse School Emitter | 2911 | not innate | prop 2197 discovered: Recipe item 3483 (Lumen Station r2156@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Secret Door Bookcase (Back Hinge) | 1279 | not innate | r833@no station mapping (taught with its prop); prop 1182 discovered: Recipe item 3538 (Lumen Station r2211@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Secret Door Bookcase (Front Hinge) | 1294 | not innate | r840@no station mapping (taught with its prop); prop 1195 discovered: Recipe item 3539 (Lumen Station r2212@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Serving Horn | 1335 | not innate | r848@no station mapping (taught with its prop); prop 1250 discovered: Recipe item 3540 (Lumen Station r2213@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Settler Flag | 435 |  | r271@Workshop (never taught); prop 387 innate | palette: innate; Showcase (free) | as client (wiki silent) |  |
| Shadow Cage Mote Emitter | 2466 | not innate | prop 2028 discovered: Recipe item 3484 (Lumen Station r2157@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Shadow Forge | 4366 | not innate | prop 2892 discovered: Recipe item 4377 (Lumen Station r5318@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Shadow Lamp Mote Emitter | 2470 | innate | prop 2032 discovered: Recipe item 3485 (Lumen Station r2158@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Shadow Portal Mote Emitter | 2465 | not innate | prop 2027 discovered: Recipe item 3541 (Lumen Station r2214@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Shadow Well Mote Emitter | 2463 | not innate | prop 2025 discovered: Recipe item 3542 (Lumen Station r2215@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Shag Rug | 1282 | not innate | r836@no station mapping (taught with its prop); prop 1186 discovered: Recipe item 3486 (Lumen Station r2159@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Shiny Ornament | 3010 | innate; event | prop 2286 innate | palette: innate; Showcase (free) | as wiki |  |
| Short Hanging Curtains | 1838 | not innate | r1234@no station mapping (taught with its prop); prop 1785 discovered: Recipe item 3379 (Lumen Station r2052@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Short Straight Pipe | 1202 | not innate | r795@no station mapping (taught with its prop); prop 1169 discovered: Recipe item 3380 (Lumen Station r2053@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Silkfall Plant | 742 | innate | r475@no station mapping (never taught); prop 545 innate | palette: innate; Showcase (free) | as wiki |  |
| Simple Stained Glass | 779 | not innate | r488@no station mapping (taught with its prop); prop 787 discovered: Recipe item 3488 (Lumen Station r2161@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Simple Stool | 1443 | innate | r933@no station mapping (never taught); prop 1380 innate | palette: innate; Showcase (free) | as wiki |  |
| Sink | 1474 | not innate | r957@no station mapping (taught with its prop); prop 1412 discovered: Recipe item 3489 (Lumen Station r2162@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Skull | 1515 | innate; event | r977@no station mapping (never taught); prop 1447 innate | palette: innate; Showcase (free) | as wiki |  |
| Skull (Jawless) | 1516 | innate; event | r978@no station mapping (never taught); prop 1448 innate | palette: innate; Showcase (free) | as wiki |  |
| Slice of Pie | 1676 | not innate | r1079@no station mapping (taught with its prop); prop 1575 discovered: Recipe item 3381 (Lumen Station r2054@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Small Fjorden Brazier | 2915 | not innate | prop 2203 discovered: Recipe item 3382 (Lumen Station r2055@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Small Knotted Rope | 1461 | not innate | r950@no station mapping (taught with its prop); prop 1407 discovered: Recipe item 3383 (Lumen Station r2056@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Small Lantern | 550 | innate | r329@no station mapping (never taught); prop 751 innate | palette: innate; Showcase (free) | as wiki |  |
| Smoke Plume Type 1 Mote Emitter | 2240 | innate | prop 1953 innate | palette: innate; Showcase (free) | as wiki |  |
| Smoke Plume Type 2 Mote Emitter | 2242 | innate | prop 1954 innate | palette: innate; Showcase (free) | as wiki |  |
| Snow Flurry Emitter | 3134 | innate; event | prop 2359 innate | palette: innate; Showcase (free) | as wiki |  |
| Snowfall Emitter | 3130 | innate; event | prop 2343 innate | palette: innate; Showcase (free) | as wiki |  |
| Snowfall Ornament | 3006 | innate; event | prop 2282 innate | palette: innate; Showcase (free) | as wiki |  |
| Snowflake Ornament | 3012 | innate; event | prop 2288 innate | palette: innate; Showcase (free) | as wiki |  |
| Snowman Carrot | 3026 | innate; event | prop 2298 innate | palette: innate; Showcase (free) | as wiki |  |
| Snowman Scarf | 3024 | innate; event | prop 2296 innate | palette: innate; Showcase (free) | as wiki |  |
| Snowman Top Hat | 3025 | innate; event | prop 2297 innate | palette: innate; Showcase (free) | as wiki |  |
| Spooky Tree | 2989 | innate; event | prop 2269 innate | palette: innate; Showcase (free) | as wiki |  |
| Spring-loaded Flinger | 1285 | innate | r918@no station mapping (never taught); prop 1200 innate | palette: innate; Showcase (free) | as wiki |  |
| Spring-loaded Launcher | 1315 | innate | r919@no station mapping (never taught); prop 1328 innate | palette: innate; Showcase (free) | as wiki |  |
| Square Coiled Rope | 2974 | not innate | prop 2254 discovered: Recipe item 3384 (Lumen Station r2057@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stackable Bookcase | 1280 | innate | r834@no station mapping (never taught); prop 1184 innate | palette: innate; Showcase (free) | as wiki |  |
| Standing Torch | 521 | innate | r316@no station mapping (never taught); prop 758 innate | palette: innate; Showcase (free) | as wiki |  |
| Starfish | 1040 | innate | r715@no station mapping (never taught); prop 936 innate | palette: innate; Showcase (free) | as wiki |  |
| Stasis Field Mote Emitter | 2460 | not innate | prop 2023 discovered: Recipe item 3385 (Lumen Station r2058@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Steam (Large) Mote Emitter | 2475 | not innate | prop 2040 discovered: Recipe item 3386 (Lumen Station r2059@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Steam (Small) Mote Emitter | 2474 | innate | prop 2039 innate | palette: innate; Showcase (free) | as wiki |  |
| Steam Whistle Mote Emitter | 2244 | not innate | prop 1956 discovered: Recipe item 3490 (Lumen Station r2163@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stein | 1337 | not innate | r850@no station mapping (taught with its prop); prop 1252 discovered: Recipe item 3387 (Lumen Station r2060@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Crystal Glass (Large) | 1383 | not innate | prop 1319 discovered: Recipe item 3491 (Lumen Station r2164@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Crystal Glass (Medium) | 1380 | not innate | prop 1316 discovered: Recipe item 3388 (Lumen Station r2061@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Crystal Glass (Short) | 1379 | not innate | prop 1315 discovered: Recipe item 3389 (Lumen Station r2062@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Crystal Glass (Tall) | 1382 | not innate | prop 1318 discovered: Recipe item 3492 (Lumen Station r2165@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Crystal Goblet (Large) | 1378 | not innate | prop 1314 discovered: Recipe item 3390 (Lumen Station r2063@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Crystal Goblet (Medium) | 1381 | not innate | prop 1317 discovered: Recipe item 3391 (Lumen Station r2064@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Glass (Large) | 1406 | not innate | r911@no station mapping (taught with its prop); prop 1335 discovered: Recipe item 3493 (Lumen Station r2166@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Glass (Medium) | 1403 | not innate | r908@no station mapping (taught with its prop); prop 1332 discovered: Recipe item 3392 (Lumen Station r2065@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Glass (Short) | 1402 | not innate | r907@no station mapping (taught with its prop); prop 1331 discovered: Recipe item 3393 (Lumen Station r2066@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Glass (Tall) | 1405 | not innate | r910@no station mapping (taught with its prop); prop 1334 discovered: Recipe item 3494 (Lumen Station r2167@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stemmed Goblet (Large) | 1401 | innate | r906@no station mapping (taught with its prop); prop 1330 discovered: Recipe item 3394 (Lumen Station r2067@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Stemmed Goblet (Medium) | 1404 | not innate | r909@no station mapping (taught with its prop); prop 1333 discovered: Recipe item 3395 (Lumen Station r2068@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stockpot (Large) | 1665 | not innate | r1068@no station mapping (taught with its prop); prop 1566 discovered: Recipe item 3396 (Lumen Station r2069@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stockpot (Medium) | 1668 | not innate | r1071@no station mapping (taught with its prop); prop 1569 discovered: Recipe item 3397 (Lumen Station r2070@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stockpot Lid (Medium Copper) | 1671 | not innate | r1074@no station mapping (taught with its prop); prop 1572 discovered: Recipe item 3398 (Lumen Station r2071@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stone Bench | 990 | not innate | r685@no station mapping (taught with its prop); prop 965 discovered: Recipe item 3543 (Lumen Station r2216@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stone Forge | 912 | innate | r638@Forge (innate); prop 903 innate | craft r638@Forge; palette: innate; Showcase (free) | as wiki |  |
| Stone Forge (Small) | 910 | innate | prop 902 innate | palette: innate; Showcase (free) | as wiki |  |
| Stone Gargoyle | 1508 | innate; event | r970@no station mapping (never taught); prop 1441 innate | palette: innate; Showcase (free) | as wiki |  |
| Stoneskin Coldbringer | 3946 | not innate | prop 2602 discovered: Recipe item 3970 (Lumen Station r2409@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stoneskin Groundsmasher | 3945 | not innate | prop 2601 discovered: Recipe item 3969 (Lumen Station r2408@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stoneskin Nightreaper | 3949 | not innate | prop 2605 discovered: Recipe item 3973 (Lumen Station r2412@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stoneskin Riftmender | 3950 | not innate | prop 2606 discovered: Recipe item 3974 (Lumen Station r2413@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stoneskin Starshooter | 3951 | not innate | prop 2607 discovered: Recipe item 3975 (Lumen Station r2414@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stoneskin Stormlasher | 3948 | not innate | prop 2604 discovered: Recipe item 3972 (Lumen Station r2411@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Stop Sign | 1692 | innate | r1095@no station mapping (never taught); prop 1594 innate | palette: innate; Showcase (free) | as wiki |  |
| Straight Hanging Chain | 1472 | not innate | r955@no station mapping (taught with its prop); prop 1400 discovered: Recipe item 3399 (Lumen Station r2072@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Straight Hanging Rope | 1459 | not innate | r948@no station mapping (taught with its prop); prop 1402 discovered: Recipe item 3400 (Lumen Station r2073@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| String Lights (Curved Hanging) | 4086 | innate; event | prop 1595 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Hanging) | 1633 | innate; event | r1024@no station mapping (never taught); prop 1534 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Large Hanging) | 1634 | innate; event | r1025@no station mapping (never taught); prop 1535 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Large Long) | 1636 | innate; event | r1027@no station mapping (never taught); prop 1537 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Large Short) | 1638 | innate; event | r1029@no station mapping (never taught); prop 1539 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Long Straight) | 4087 | innate; event | prop 1596 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Long) | 1635 | innate; event | r1026@no station mapping (never taught); prop 1536 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Short Straight) | 4088 | innate; event | prop 1597 innate | palette: innate; Showcase (free) | as wiki |  |
| String Lights (Short) | 1637 | innate; event | r1028@no station mapping (never taught); prop 1538 innate | palette: innate; Showcase (free) | as wiki |  |
| Striped Ornament | 3007 | innate; event | prop 2283 innate | palette: innate; Showcase (free) | as wiki |  |
| Sunblossom Plant | 738 | innate | r471@no station mapping (never taught); prop 509 innate | palette: innate; Showcase (free) | as wiki |  |
| Sunblossom Plant (Large) | 2811 | innate | prop 512 innate | palette: innate; Showcase (free) | as wiki |  |
| Sunblossom Plant (Perfect) | 2812 | innate | prop 1625 innate | palette: innate; Showcase (free) | as wiki |  |
| Sword Stained Glass | 780 | not innate | r489@no station mapping (taught with its prop); prop 786 discovered: Recipe item 3544 (Lumen Station r2217@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Table (Carved) | 348 | not innate | r153@no station mapping (taught with its prop); prop 208 discovered: Recipe item 3545 (Lumen Station r2218@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Table (Large Wooden) | 560 | innate | r335@no station mapping (never taught); prop 793 innate | palette: innate; Showcase (free) | as wiki |  |
| Table (Small Round Regal) | 1045 | not innate | r720@no station mapping (taught with its prop); prop 968 discovered: Recipe item 3546 (Lumen Station r2219@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Table (Small Stone ) | 566 | not innate | r341@no station mapping (taught with its prop); prop 799 discovered: Recipe item 3402 (Lumen Station r2075@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Table (Small Wooden) | 561 | innate | r336@no station mapping (never taught); prop 794 innate | palette: innate; Showcase (free) | as wiki |  |
| Tall Highfall Lamp Post | 2634 | not innate | r1823@no station mapping (taught with its prop); prop 2107 discovered: Recipe item 3342 (Lumen Station r2015@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Target Dummy | 4362 | innate | prop 2890 innate | palette: innate; Showcase (free) | as wiki |  |
| Teleporter Mote Emitter | 2471 | not innate | prop 2033 discovered: Recipe item 3498 (Lumen Station r2171@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Toaster | 1687 | not innate | r1090@no station mapping (taught with its prop); prop 1588 discovered: Recipe item 3404 (Lumen Station r2077@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Toaster with Toast | 1686 | not innate | r1089@no station mapping (taught with its prop); prop 1587 discovered: Recipe item 3405 (Lumen Station r2078@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Toilet | 1475 | not innate | r958@no station mapping (taught with its prop); prop 1413 discovered: Recipe item 3499 (Lumen Station r2172@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Torch Fire (Blue) Mote Emitter | 2130 | innate | prop 1923 innate | palette: innate; Showcase (free) | as wiki |  |
| Torch Fire (Green) Mote Emitter | 2131 | innate | prop 1924 innate | palette: innate; Showcase (free) | as wiki |  |
| Torch Fire (Pink) Mote Emitter | 2132 | innate | prop 1925 discovered: Recipe item 3406 (Lumen Station r2079@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Torch Fire (Purple) Mote Emitter | 2133 | innate | prop 1926 discovered: Recipe item 3407 (Lumen Station r2080@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Torch Fire (Red) Mote Emitter | 2134 | innate | prop 1927 innate | palette: innate; Showcase (free) | as wiki |  |
| Torch Fire (Yellow) Mote Emitter | 2135 | innate | prop 1928 discovered: Recipe item 3408 (Lumen Station r2081@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | different route | wiki innate; client data: a Recipe item grants the prop (grantId) — client wins; discovered via recipe drop + Lumen Station |
| Torch Fire Mote Emitter | 2129 | innate | prop 1922 innate | palette: innate; Showcase (free) | as wiki |  |
| Toxic Giant | 3196 | not innate | prop 2485 discovered: Recipe item 3646 (Lumen Station r2319@Lumen Station (innate, 520 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Trailblazer Flag | 437 |  | r273@Workshop (never taught); prop 386 innate | palette: innate; Showcase (free) | as client (wiki silent) |  |
| Translocation Pad | 1290 | innate | r920@no station mapping (never taught); prop 1196 innate | palette: innate; Showcase (free) | as wiki |  |
| Trapdoor (Indicite) | 1214 | not innate | r807@no station mapping (taught with its prop); prop 930 discovered: Recipe item 3410 (Lumen Station r2083@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Trapdoor (Iron) | 1213 | not innate | r806@no station mapping (taught with its prop); prop 929 discovered: Recipe item 3500 (Lumen Station r2173@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Trapdoor (Plain Wood) | 409 | innate | r214@no station mapping (never taught); prop 926 innate | palette: innate; Showcase (free) | as wiki |  |
| Trapdoor (Square) | 1758 | not innate | r1149@no station mapping (taught with its prop); prop 1748 discovered: Recipe item 3411 (Lumen Station r2084@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Trapdoor (Striped Wood) | 1212 | not innate | r805@no station mapping (taught with its prop); prop 928 discovered: Recipe item 3412 (Lumen Station r2085@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Tri-Bladed Fan | 1194 | not innate | r787@no station mapping (taught with its prop); prop 1161 discovered: Recipe item 3502 (Lumen Station r2175@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Tropical Painting | 784 | not innate | r493@no station mapping (taught with its prop); prop 825 discovered: Recipe item 3548 (Lumen Station r2221@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Tundra Berry Bush (Pink) 1 | 715 | innate | r448@no station mapping (never taught); prop 579 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Berry Bush (Pink) 2 | 716 | innate | r449@no station mapping (never taught); prop 580 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Berry Bush (Red) 1 | 717 | innate | r450@no station mapping (never taught); prop 581 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Berry Bush (Red) 2 | 718 | innate | r451@no station mapping (never taught); prop 582 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Boulder 1 | 744 | innate | r477@no station mapping (never taught); prop 808 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Boulder 2 | 745 | innate | r478@no station mapping (never taught); prop 809 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Centipine (Medium) 1 | 642 | innate | r375@no station mapping (never taught); prop 588 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Centipine (Medium) 2 | 643 | innate | r376@no station mapping (never taught); prop 589 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fir (Snowy) 1 | 2714 | innate | prop 1091 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fir (Snowy) 2 | 2715 | innate | prop 1092 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fir 1 | 644 | innate | r377@no station mapping (never taught); prop 337 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fir 2 | 645 | innate | r378@no station mapping (never taught); prop 338 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fungal Mound 1 | 721 | innate | r454@no station mapping (never taught); prop 586 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fungal Mound 2 | 722 | innate | r455@no station mapping (never taught); prop 587 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fungal Mound 3 | 2712 | innate | prop 1057 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Fungal Mound 4 | 2713 | innate | prop 1058 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Grove Tree (Large) 1 | 646 | innate | r379@no station mapping (never taught); prop 339 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Grove Tree (Large) 2 | 647 | innate | r380@no station mapping (never taught); prop 340 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Grove Tree (Small Snowy) 1 | 2710 | innate | prop 1093 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Grove Tree (Small Snowy) 2 | 2711 | innate | prop 1094 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Grove Tree (Small) 1 | 752 | innate | r485@no station mapping (never taught); prop 816 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Grove Tree (Small) 2 | 753 | innate | r486@no station mapping (never taught); prop 817 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Painting | 785 | not innate | r494@no station mapping (taught with its prop); prop 826 discovered: Recipe item 3549 (Lumen Station r2222@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Tundra Rock (Large) 1 | 746 | innate | r479@no station mapping (never taught); prop 810 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Rock (Large) 2 | 747 | innate | r480@no station mapping (never taught); prop 811 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Rock (Medium) 1 | 748 | innate | r481@no station mapping (never taught); prop 813 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Rock (Medium) 2 | 749 | innate | r482@no station mapping (never taught); prop 812 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Rock (Small) 1 | 750 | innate | r483@no station mapping (never taught); prop 815 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Rock (Small) 2 | 751 | innate | r484@no station mapping (never taught); prop 814 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Shoots Group 1 | 712 | innate | r445@no station mapping (never taught); prop 576 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Shoots Group 2 | 713 | innate | r446@no station mapping (never taught); prop 577 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Shoots Group 3 | 714 | innate | r447@no station mapping (never taught); prop 578 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Spindle Pine 1 | 648 | innate | r381@no station mapping (never taught); prop 341 innate | palette: innate; Showcase (free) | as wiki |  |
| Tundra Spindle Pine 2 | 649 | innate | r382@no station mapping (never taught); prop 342 innate | palette: innate; Showcase (free) | as wiki |  |
| Twin-Bladed Fan | 288 | not innate | r93@no station mapping (taught with its prop); prop 1160 discovered: Recipe item 3503 (Lumen Station r2176@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Unstable Laboratory | 2456 | not innate | r305@no station mapping (taught with its prop); r1649@no station mapping (taught with its prop); prop 2038 discovered: Recipe item 4370 (Lumen Station r5311@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station; xref matched 156, same-name item 2456 used |
| Utility Door (Interlocking Left) | 2661 | not innate | prop 2121 discovered: Recipe item 3504 (Lumen Station r2177@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Utility Door (Interlocking Right) | 2662 | not innate | prop 2122 discovered: Recipe item 3505 (Lumen Station r2178@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Utility Door (Single) | 2663 | not innate | prop 2123 discovered: Recipe item 3506 (Lumen Station r2179@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Vendor Stall (Alchemy) | 2462 | innate | r1650@no station mapping (never taught); prop 2042 innate | palette: innate; Showcase (free) | as wiki |  |
| Vendor Stall (General Goods) | 1770 | innate | r1157@no station mapping (never taught); prop 1752 innate | palette: innate; Showcase (free) | as wiki |  |
| Vendor Stall (Linen) | 144 | innate | r304@no station mapping (never taught); prop 451 innate | palette: innate; Showcase (free) | as wiki |  |
| Vilebloom Plant | 739 | innate | r472@no station mapping (never taught); prop 513 innate | palette: innate; Showcase (free) | as wiki |  |
| Vilebloom Plant (Large) | 2813 | innate | prop 516 innate | palette: innate; Showcase (free) | as wiki |  |
| Vilebloom Plant (Perfect) | 2814 | innate | prop 1627 innate | palette: innate; Showcase (free) | as wiki |  |
| Village Sawmill | 908 | not innate | r542@no station mapping (taught with its prop); prop 900 discovered: Recipe item 4380 (Lumen Station r5321@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Village Sawmill (Small) | 907 | innate | r541@no station mapping (never taught); prop 899 innate | palette: innate; Showcase (free) | as wiki |  |
| Violet Emissive Cube | 2831 | not innate | prop 2141 discovered: Recipe item 3550 (Lumen Station r2223@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Violet Emissive Orb | 2823 | not innate | prop 2132 discovered: Recipe item 3551 (Lumen Station r2224@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Violet Light Orb | 972 | not innate | r680@no station mapping (taught with its prop); prop 956 discovered: Recipe item 3552 (Lumen Station r2225@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Violet Spotlight | 1185 | not innate | r900@no station mapping (taught with its prop); prop 1155 discovered: Recipe item 3553 (Lumen Station r2226@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Void Vault | 441 | not innate | prop 407 innate | palette: innate; Showcase (free) | different route | wiki not innate; client data: no Recipe item grants the prop and no non-default recipe — innate |
| Volcanic Boulder (Pumice) 1 | 2734 | innate | prop 1656 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Boulder (Pumice) 2 | 2735 | innate | prop 1658 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Boulder (Pumice) 3 | 2736 | innate | prop 1660 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Rock (Large Pumice) | 2737 | innate | prop 1662 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Rock (Medium Basalt) 1 | 2725 | innate | prop 270 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Rock (Medium Basalt) 2 | 2726 | innate | prop 271 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent (Barrel with Smoke) | 2733 | innate | prop 2106 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent (Barrel) | 2727 | innate | prop 227 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent (Spiked) 1 | 2728 | innate | prop 229 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent (Spiked) 2 | 2729 | innate | prop 230 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent (Spiked) 3 | 2730 | innate | prop 232 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent 1 | 2731 | innate | prop 285 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Succulent 2 | 2732 | innate | prop 286 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Tree (Large Bare) | 2723 | innate | prop 1737 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Tree (Large Thorny) | 2722 | innate | prop 241 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Tree (Small Bare) | 2724 | innate | prop 1740 innate | palette: innate; Showcase (free) | as wiki |  |
| Volcanic Tree (Small Thorny) | 2721 | innate | prop 239 innate | palette: innate; Showcase (free) | as wiki |  |
| Wall Lantern | 384 | innate | prop 56 innate | palette: innate; Showcase (free) | as wiki |  |
| Wall Sconce (Small) | 383 | innate | r188@no station mapping (never taught); prop 692 innate | palette: innate; Showcase (free) | as wiki |  |
| Water Drip Mote Emitter | 2287 | not innate | prop 1965 discovered: Recipe item 3414 (Lumen Station r2087@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Water Geyser Eruption Mote Emitter | 2283 | not innate | prop 1961 discovered: Recipe item 3554 (Lumen Station r2227@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Water Geyser Warning Mote Emitter | 2284 | not innate | prop 1962 discovered: Recipe item 3555 (Lumen Station r2228@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Waterfall Mote Emitter | 2476 | innate | prop 2041 innate | palette: innate; Showcase (free) | as wiki |  |
| White Emissive Cube | 2832 | not innate | prop 2142 discovered: Recipe item 3556 (Lumen Station r2229@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| White Emissive Orb | 2824 | not innate | prop 2133 discovered: Recipe item 3557 (Lumen Station r2230@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| White Light Orb | 973 | not innate | r669@no station mapping (taught with its prop); prop 957 discovered: Recipe item 3558 (Lumen Station r2231@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| White Spotlight | 1186 | not innate | r901@no station mapping (taught with its prop); prop 1156 discovered: Recipe item 3559 (Lumen Station r2232@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Winter Garland | 3014 | innate; event | prop 2290 innate | palette: innate; Showcase (free) | as wiki |  |
| Winter Holiday Tree | 1773 | innate; event | r1163@no station mapping (never taught); prop 1754 innate | palette: innate; Showcase (free) | as wiki |  |
| Winter Star | 1644 | innate; event | r1035@no station mapping (never taught); prop 1545 innate | palette: innate; Showcase (free) | as wiki |  |
| Winter Star (Fancy) | 1643 | innate; event | r1034@no station mapping (never taught); prop 1544 innate | palette: innate; Showcase (free) | as wiki |  |
| Winter Wreath | 3015 | innate; event | prop 2291 innate | palette: innate; Showcase (free) | as wiki |  |
| Wood Bench | 1864 | innate | r1252@no station mapping (never taught); prop 1804 innate | palette: innate; Showcase (free) | as wiki |  |
| Wood Stool | 992 | not innate | r686@no station mapping (taught with its prop); prop 819 discovered: Recipe item 3416 (Lumen Station r2089@Lumen Station (innate, 65 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Wooden Bed | 337 | innate | r142@no station mapping (never taught); prop 781 innate | palette: innate; Showcase (free) | as wiki |  |
| Wooden Bookcase | 1284 | innate | r838@no station mapping (never taught); prop 1189 innate | palette: innate; Showcase (free) | as wiki |  |
| Wooden Lattice | 1520 | not innate; on an Innate list | r981@no station mapping (never taught); prop 1456 innate | palette: innate; Showcase (free) | as wiki |  |
| Wooden Loom (Large) | 905 | not innate | r538@no station mapping (taught with its prop); prop 887 discovered: Recipe item 4373 (Lumen Station r5314@Lumen Station (innate, 260 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Wooden Loom (Medium) | 904 | not innate | r537@no station mapping (taught with its prop); prop 886 discovered: Recipe item 4379 (Lumen Station r5320@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Wooden Loom (Small) | 903 | innate | r536@no station mapping (never taught); prop 885 innate | palette: innate; Showcase (free) | as wiki |  |
| Year Zero Trophy | 2483 |  | prop 2043 innate | palette: innate | as client (wiki silent) |  |
| Yellow Emissive Cube | 2833 | not innate | prop 2135 discovered: Recipe item 3507 (Lumen Station r2180@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Yellow Emissive Orb | 2825 | not innate | prop 2134 discovered: Recipe item 3508 (Lumen Station r2181@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Yellow Light Orb | 974 | not innate | r681@no station mapping (taught with its prop); prop 958 discovered: Recipe item 3509 (Lumen Station r2182@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Yellow Spotlight | 1187 | not innate | r902@no station mapping (taught with its prop); prop 1157 discovered: Recipe item 3510 (Lumen Station r2183@Lumen Station (innate, 130 Lumens)) | palette: taught; Showcase (free) | as wiki | discovered via recipe drop + Lumen Station |
| Young Stinkhorn | 4117 | innate | prop 858 innate | palette: innate; Showcase (free) | as wiki |  |

