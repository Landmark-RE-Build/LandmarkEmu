# System: gathering — mining, logging, extractors, land / tree regrowth, harvest results

Status: [~] wiki + client harvest result traced (2026-10-12). Server: docs/terrain.md (cells, veins, regrowth).

## Client: harvest result (Abilities 0x27 sub 0xD EffectEnd → FUN_142d7aca0)
The harvest effect's EffectEnd carries the outcome: f32 +0x4C fraction (1.0 = "Complete success", 0 < f < 1 =
"Partial success: %f"), u32 +0x50 reason when 0, u32 +0x54 resource id, u32 +0x58 tool id. Reasons → texts
(trace/code-strings.md):
- 2 / 5 → Harvest.Fail.Tier "…Resource Rating of your current tool is too low." (with resource + tool ids:
  Harvest.Fail.Tier.Specific "[resource] requires the [tool] or better"; 5 with ids → Harvest.Fail.IsPulverizer
  "[resource] can't be harvested or destroyed")
- 3 → Harvest.Fail.Prevent "It is not possible to harvest that."
- 4 → Harvest.Fail.Multiple
- 7 → resource discovery / T4 details path (loot-style notice with "resource")
- 8 → Harvest.Fail.NotAllowed "You cannot harvest here." (e.g. inside a build site; Harvest.Fail.InClaim exists as
  server-side text)
- 6 → nothing.
Client range check: Harvest.CastFailMessage "You are too far away to harvest that" (FUN_142d79ee0).
Gap: check what our server puts in EffectEnd for harvests (Buffs.cs sends EffectEnd for buffs only?) and send the
reason codes on refusal (build site → 8).

## Live behaviour (wiki: Gathering, Tier, Gathering Tools, Pick, Axe, Extractor — launch 2016)
- Gathering only on unclaimed land, never inside a build site (buffer zones are allowed).
- Land regenerates about 20 minutes after it was dug (back to its original state); trees respawn too (timer
  unconfirmed). A player logged out in a hole is lifted to the surface when the land regrows.
- Resources: trees → Plain Wood (Jungle, Tundra, Deciduous), Burled Wood (Old Growth, Volcanic; some elsewhere),
  Striped Wood (Desert), Tree Fiber + Tree Sap (all trees). Surface: Marble, Amaranthine, Copper, Tin, Iron, Agate,
  Sapphire. Underground (surface caves + Chaos Caverns): Obsidian, Alabaster, Iron, Silver, Gold, Ruby, Emerald,
  Diamond — deeper = more variety. Common (passive, gathered alongside anything): Dirt, Common Stone (everywhere),
  Sand (Desert), Ice / Snow (Tundra); not refined. Everything else is refined at a Replicator before use.
- Tools (no hard tiers at launch — any axe chops any tree): Picks (Adventurer's starter; Farseer outlines resources;
  Charmed luck → recipes / special resources; Goliath bigger area; Whirlwind faster), Axes (Adventurer's; Charmed luck;
  Hotfoot speed boost after logging; Whirlwind faster; Goliath more per swing, slower) — axes auto-chop after one click.
  Extractors (recipe looted or bought with Lumens; gather everything, tunnel, faster): NovaTech Extractor, Harvestron
  (+yield), Holoseeker (outlines), Velocitizer (speed buff). Founder's Pickaxe = pick + axe. Helpers: Ore Prospector,
  Ground Sounder, Cave Sounder / Cave Seeker, Delver's Grappling Hook.
- Drops while gathering: Essences, prop/gear recipes, Lumens — more likely from veins and trees than from dirt.
- Party: members harvesting within 200 m share what is gathered ("In party" buff).
- Full inventory: particles hang in the air, no message (live bug).
- "You have entered [tier] ([name])" (Tier.EnteredNamed) when changing depth layer (surface, Layer 1-8).

## Rebuild status (2026-10-12)
Done: mining refused inside any build site (Harvest.Fail.InClaim text); party share within 200 m (Gathering.GiveHarvest
for ore, stone / dirt, wood, fibre, sap, plants). Already present: 20-minute land regrowth (TerrainEdits
RegenerationDelay), ore veins moving, tree / plant regrowth, Harvest Bonus.
Open: harvest result codes in EffectEnd (tier / prevent / not allowed — our tools have no tier limits at launch, so
only "not allowed" applies, sent today as chat); loot chests (common in surface caves, Lost Chests in Outposts; 50 m
everyone-gets-loot) and Lost Crystals — with the Chaos Caverns content; drops while gathering (essences, recipes,
Lumens) — check Abilities yield code for recipe / Lumen drops; extractor variants' bonuses (Harvestron yield,
Velocitizer speed buff) and pick / axe bonuses (Charmed luck, Hotfoot speed, Goliath size, Whirlwind speed).
