# FEATURES.md audit against the server code (2026-10-17)

Read-only audit of every row of trace/0695/FEATURES.md against the code on branch `client-0.695` (HEAD e73a744),
using TASKS.md, OPEN-LEADS.md, coverage-695.md, CLIENT-DIFF.md, the wiki pages and TESTING-NEEDED.md for the live
rule and the verification state.

**FEATURES.md is out of date.** Most of its ❌ / 🆕 rows (Counting Node, Scoreboard, Random Link Filter, Server Time
Node, Combat Node, IF Gate, Respawn Point, Fireworks, Reward Chests, Play Mode Rules, Game Table rework, Game Finder,
Parlor sex / size, Bio / Inspect, gear stats, buying design materials, placing a site from the window, the upkeep
warning, cross-world social) were built on 2026-10-15 / 16 (TASKS §1-§6, §8). The statuses below describe the code
as it is now, not as FEATURES.md has them.

Code paths: `G/` = `LandmarkEmu/LandmarkEmulator.WorldServer/Game/`, `H/` = `.../WorldServer/Network/Handlers/`.

Statuses:
- **DONE**: built, and the client's reaction was checked (by the harness or the user, on 0.695 or on the 0.589
  harness for families that coverage-695 marks "same").
- **DONE-UNTESTED**: built, but no in-game check is on record.
- **PARTIAL**: part of the live rule is missing (what is missing is given).
- **MISSING**: no code.
- **N/A**: proven to need no server work.

## Summary

| Status | Rows |
|---|---|
| DONE | 50 |
| DONE-UNTESTED | 23 |
| PARTIAL | 18 |
| MISSING | 2 |
| N/A | 2 |
| **Total** | **95** (the FEATURES rows; "Rank 2 / 3 abilities, Roll Wraps, playdead" is split into its three parts) |

New gaps that are not in TASKS.md yet:
1. **No check of the Get Inspired (copy) permission.** `Claims.PermissionCopy` (G/Claims.cs:81) is never read.
   Design saves and copies of someone else's site go through with no permission check (G/BuildingClipboard.cs:228-246).
   Only paste checks permissions.
2. **Graveyards.** "Flee" goes only to placed respawn / graveyard props, Starting Locations, the Spire or your own
   site (G/Graveyards.cs:32-50). There are no landscape graveyards (the map's red circles). The 81 Chaos Cavern
   graveyard pieces per island (tile 111, already found by G/WarpNetwork.cs:49) are not respawn sites and have no
   safe zone, so a death in a cavern sends the player back to the surface Spire.
3. **The spawner's Equip / Unequip weapon Links (577074 / 577075) do nothing.** They are offered (G/StoryTools.cs:378)
   but fall into the `default` "has no effect yet" branch (G/StoryTools.cs:1334-1341).
4. **Teleport to a friend works only on the same world** (G/Abilities.cs:412-420): a friend on another world gets
   "Your friend isn't online."
5. **The Game Table's "On Game Ended" Link (P6517) is missing.** Table 2313 has only "Complete Adventure"
   (G/StoryTools.cs:348).
6. **Rank 2 / 3 weapon abilities and the playdead emote have no code.** CLIENT-DIFF §2; G/Emotes.cs:31-40 has no
   "playdead".

## Feature table

### Story tools and Links

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 1 | General rules: innate, 50 Cut Plain Wood, tintable, owner-only Name, hidden in Play Mode except the Scoreboard / Starting Location | PARTIAL | recipe cost from the data; renaming from ALLOW_USER_RENAME (TASKS 2.16); tint G/Props.cs SetTint | How the client hides story tools in Play Mode, and why the Scoreboard stays visible, is not traced (OPEN-LEADS L20). The server sends nothing for it. |
| 2 | Links, Play / Edit Mode | DONE | G/StoryTools.cs Handle :2021, Add :2140, PlayModeChanged :922; G/Claims.cs SetAdventureStatus :1790 | Harness on 0.589 (TESTING-NEEDED "Story tools (Links)"); 0.695 Links window (shots_counting_node_links_695.png). |
| 3 | Toggle props and lockable doors / chests | DONE | G/StoryTools.cs namedKinds :89-133, Execute :1035 | Harness on 0.589 (switch → door / torch). The 6.36 sconce / emissive / control-panel kinds are untested. |
| 4 | "When clicked" on every prop | DONE-UNTESTED | G/StoryTools.cs clickOnlyKind :447, FlagClickable :1771 | TASKS 2.13: "untested in game". |
| 5 | Signs, books, scrolls show text | DONE-UNTESTED | G/StoryTools.cs :140-144 (2939 / 2228 / 2229 / 2639) | No in-game check on record. |
| 6 | Story Node | PARTIAL | G/StoryTools.cs ActivateNode :1350, Perform :1371, SayNodeText :1435, AskQuestion :1501, DialogResponse :1519; G/PropSettings.cs drop-downs | Missing: row 41 Weapon Visuals (Equipped / Sheathed, 577237-577239) and the "weapon visuals" event (L95). Mood / stance / emote on creatures that aren't bipeds is unproven (L117). 0.695 tests: rich text in the bubble, question via Lua. Lists and announce lines untested. |
| 7 | Logic Filter (lights, 0.695 wording) | DONE | G/StoryTools.cs SetCogs :1570, kind :282 | 0.695 test: blue light (shots_logic_filter_light_695.png). The silent reset is untested. |
| 8 | Probability Filter | DONE | G/StoryTools.cs DiceOdds :862 | 0.695 window shows "Odds of Winning 50" (shots_settings_odds_695.png); the rename is client text. |
| 9 | Timer Filter: loop, reset-event rule | DONE-UNTESTED | G/StoryTools.cs TimerLength :860, Update :1901 (TimerAutoRestart) | Window checked on 0.695 (shots_settings_timer_695.png). The loop and the "resets only on Reset" rule (TASKS 2.16) are not checked in game. |
| 10 | Focus Node | DONE-UNTESTED | G/StoryTools.cs :1152-1170, Creatures.StoryFocus | "Focus on what activated this" through 2.34 is untested. |
| 11 | Damage Node (%, Heal Instead, area targets) | DONE-UNTESTED | G/StoryTools.cs DamageOf :872, HurtCreature :877, HurtPlayer :887 | TASKS 2.25 / 2.26 untested. The "Damage % Multiplier" is a spawner row (#22). |
| 12 | Reset Node | DONE-UNTESTED | G/StoryTools.cs case ResetStory :1277 | Silent restore built (2.32); message 577089 route unknown (L22). |
| 13 | Starting Location | DONE | G/Claims.cs EnsureStartingLocation :1296 | Harness on 0.589. One-per-site (6.37) and facing on arrival (ed74045) untested. |
| 14 | Respawn Point / Location 2701 | DONE-UNTESTED | G/StoryTools.cs kind :318, RespawnSite :1653 | TASKS 2.24: untested. |
| 15 | Counting Node | DONE | G/StoryTools.cs CountingKind :175, EditCount :703, QueueState :692 | 0.695 test (TASKS 2.2). The Edit-Mode digits (2.19) are untested. |
| 16 | Scoreboard | PARTIAL | same runtime, kindsById[3000] | Visible in Play Mode while the Counting Node is hidden: mechanism not traced (L20). |
| 17 | Random Link Filter | DONE-UNTESTED | G/StoryTools.cs :313, RandomTrigger :682 | No test on record. |
| 18 | Server Time Node / Hour Node | DONE-UNTESTED | G/StoryTools.cs ServerTimeReached :1979, GameHour :916; G/PropSettings.cs DesiredTime | Dev-command check only (TASKS 2.8). The Desired Time rows are the server's own (L19). |
| 19 | Combat Node | DONE-UNTESTED | G/StoryTools.cs :255, :479, :1325; Creatures.StoryUseAbility | One ability per creature kind is our choice (L19 / L23). |
| 20 | IF Gate | DONE-UNTESTED | G/StoryTools.cs IfHolds :1465; G/PropSettings.cs ifGateRows | Comparison words and trigger-check labels are ours (L19). |
| 21 | Linking Area (enter / leave / damage, Destroy / Heal Voxels, voxel permission, parse) | DONE-UNTESTED | G/StoryTools.cs :334-345, CreaturesIn :901; G/VoxelDestruction.cs AreaAction :235; G/AreaDefinitions.cs | Dynamite allow / refuse checked on 0.695 (TASKS 2.6). Area Destroy / Heal is untested. |
| 22 | Creature spawners (incl. 0.695 health / damage, tactics, weapon visuals) | PARTIAL | G/StoryTools.cs spawnerKind :373, Spawn :1592; G/Creatures.cs SpawnStory :2821; G/PropSettings.cs :120 | **The Equip / Unequip weapon Link actions 577074 / 577075 are no-ops** (G/StoryTools.cs:1340). Row 40 "Spawn With Weapons Equipped" is not offered (L95). Health / damage % (2.14) and duplicates (6.30) are untested. |
| 23 | Travel Point | DONE | G/StoryTools.cs :1132, CreatureArrived :1708 | Harness on 0.589 (walk route). |
| 24 | Fireworks Emitter | DONE | G/StoryTools.cs LaunchFirework :1254; G/PropEffects.cs | 0.695 test: the client loaded the firework sound banks at each launch (TASKS 3.9). |
| 25 | Reward Chest | DONE-UNTESTED | G/RewardChests.cs Loot :196; G/StoryTools.cs :1063 | Settings window and owner refusal checked on 0.695. Visitor loot (two players) and tier amounts (U1-U12, ours) are untested. |

### Site games

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 26 | 0.589 rule sets (DM / TDM / ZC / Adventure), HUD, scoreboard, observers, queues, editor | DONE | G/InstancedGames.cs, .Editor.cs, .Queue.cs | Harness on 0.589 (TESTING-NEEDED "Site games", "Zone control", "Site game queues"). |
| 27 | 0.695 Game Table settings and rule types | DONE | G/InstancedGames.Rework.cs (SettingKeyHash, AddOrRemoveRule, GetConfiguration) | Checked on 0.695 (TASKS 2.12): teams, join / start / spawn, HUD "Top Score", end banner. Settings-window clicks and team switching still need a hand test. The site-game block's trailing u32 is unknown (L37). |
| 28 | Score Node, Victory Node, yellow / green spawners, Control Point | PARTIAL | G/InstancedGames.Rework.cs :68-80; G/StoryTools.cs :325-342; G/InstancedGames.cs :69 | **Missing: the Game Table's "On Game Ended" Link (P6517).** Score / Victory Node placement and the owner of 577665 / 577666 / 577668 are inferred (L21). Score was only exercised by the `gamescore` dev command. |
| 29 | Game Finder across worlds | PARTIAL | G/InstancedGames.Finder.cs; social matchmaking (IMatchmakingService) | Counts across worlds and "Match found" on the player's own world are built (3.25 checked on 0.695). **Joining a match on another world is not built** (G/InstancedGames.Finder.cs:158, :192; L31). |

### Play Mode Rules

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 30 | Play Mode Rules (cameras, grapple, boot abilities, voxel destruction) | DONE | G/Claims.cs SetAdventureFlags :1850, :1033 / :1227 (mask in record / info 0x17), RuleBlocks :1614; G/VoxelDestruction.cs | 0.695 test: mask 0x1 → 0x5 → 0x4 kept by the client; dynamite refused with EffectEnd 9; crater heals after ~104 s. The client enforces cameras / grapple / boots (TASKS 2.1). |

### Building

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 31 | Voxel tools, undo / redo 20 | DONE | G/Building.cs (UndoDepth :146), G/WorldEditing.cs, G/BuildingClipboard.cs | Harness on 0.589 (2026-10-13 / 14). |
| 32 | Free terrain materials, costs | DONE | G/Building.cs | Harness on 0.589. |
| 33 | Designs (save / list / place / rename / delete, categories, 6M → 48M) | DONE | G/Templates.cs (MaxVoxelExpandable :137), G/ItemUses.cs :54, G/SiteDesigns.cs | Tested; 3.7 categories wait for the user's look. |
| 34 | Prop palette, innate / discovered, tints, recycle refunds | DONE | G/Props.cs, G/Palettes.cs, G/ArtTints.cs, G/RecipeBook.cs | Recycle-returns-materials (6.38) is untested. |
| 35 | Mesh Shapes tab / Fill smoothing | DONE-UNTESTED | G/Building.Tools.cs HandleTool :72 | TASKS 3.1: untested. Palette and smoothing are client-only. |
| 36 | Buy a Design's missing materials | DONE | G/DesignMaterials.cs SendOffer :82, Purchase :157 | 0.695 test: bought 11 bundles (shots_buy_materials_*). Spec U1-U6 are our choices. |
| 37 | Paste / preview progress, site-wide LOD | N/A | none | Client only. PasteThread / PreviewGenerationThread use no packet (TASKS §1). Claims.Get/SetAllowClaimWideLOD is a client-only binding (CLIENT-DIFF.md:159). |

### Build sites

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 38 | Size, buffer, placement, delete, 8-site cap | DONE | G/Claims.cs, G/SiteContents.cs | Harness on 0.589. Height limits (6.47) and delete-as-materials (6.39) are untested. |
| 39 | Active Days upkeep | DONE | G/SiteUpkeep.cs | Isolated-stack test (TASKS 3.18). |
| 40 | Warning before expiry | DONE | G/SiteUpkeep.cs MailWarning :92; G/Claims.cs :760 | Isolated-stack test. The client's GAQ notice is dead code (proof in TASKS 3.18). The letter text is ours (L87). |
| 41 | Permissions, Play Around default, specific players | DONE | G/Claims.cs :76-81 | Harness (Special Access list). The copy bit is not enforced: see #93. |
| 42 | Name, description, tags, Gallery, screenshots, rating, contests, music | DONE-UNTESTED | G/Claims.cs, G/Gallery.cs, G/Ratings.cs, G/Contests.cs, G/SiteScreenshots.cs | The gallery rules of 6.43 are untested. Older harness checks passed. |
| 43 | 50 storage chests (and 125 spawners) | DONE | G/PropLimits.cs | Harness (proplimits 3/3). |
| 44 | Starting Location placed on new sites | DONE | G/Claims.cs :1296 | Harness on 0.589. |
| 45 | Place from window, expansions via Marketplace, Pioneer sites | PARTIAL | G/Claims.cs UsesFlags :205, :229-240, SlotsOf :678 | Placing from the window is checked on 0.695 (shots_site_place_695.png). Missing: the real Build Site Expansion id and its Marketplace listing ("unlock more" opens the Marketplace); 900001 is our id (L86). |
| 46 | Play Mode Rules (site) | DONE | as #30 | |

### Death and respawn

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 47 | Revive here (15 Lumens), flee to nearest graveyard / site respawn | PARTIAL | G/Health.cs ReviveCost :45, :592-605; G/Graveyards.cs SitesFor :32, Nearest :56 | Revive / flee checked by the harness. **Missing: landscape graveyards (map red circles) and Chaos Cavern graveyards with a safe zone (wiki Graveyard).** The server's only open-land sites are placed props 1225 / 1518 / 2108 and the Spire. The 81 cavern Graveyard01 spots (tile 111, G/WarpNetwork.cs:49) are not respawn sites. |
| 48 | Respawn Location 2701 | DONE-UNTESTED | as #14 | |
| 49 | Durability loss and repair | DONE | G/Durability.cs (ToolUsed :137) | Harness. Tool wear (6.53) is untested. |

### Combat, gear and creatures

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 50 | Weapon attacks, abilities, hit effects, statuses, damage numbers | DONE | G/Combat/WeaponCombat.cs, G/WeaponAttacks.cs, G/Abilities.cs, G/HitEffects.cs, G/Combat/Statuses.cs | Harness (TESTING-NEEDED "Weapon combat"). |
| 51 | Affinity effects | DONE | G/Combat/Gear.cs | Chances / sizes are ours. |
| 52 | Gear stats (max life, damage by armour weight) | DONE | G/Combat/GearStats.cs WeightOf :68 | 0.695 test: heavy armour gives ~1800 max (TASKS 5.1). Numbers ours. |
| 53 | Creatures, factions, elites, outposts, loot | DONE | G/Creatures.cs, G/Outposts.cs, G/Encounters.cs, G/LootChests.cs, G/LootPinatas.cs | Harness (monsters, outposts). Many 6.x launch rules from 2026-10-16 are untested. |
| 54 | Cozy Campfire | DONE-UNTESTED | G/Consumables.cs TryUse :188 | The launch rules rebuilt in 6.19 (no heal in combat, full heal, airborne refusal) are untested. |
| 55a | Rank 2 / 3 weapon abilities | MISSING | none | Only table rows exist in 0.695 (functions-695.md §8). Nothing in the server grants or uses them, and there is no open lead. |
| 55b | Roll Wraps roll | PARTIAL | G/BootAbilities.cs; caster info in H/MiscHandler.cs | Abilities 1207-1214 are wired (4.15). How players got the item is unknown (L26), so only admins can get it. |
| 55c | playdead emote | MISSING | G/Emotes.cs :31-40 (no "playdead") | Texts 577222 / 577224 / 577225 are new in 0.695, and AnimationSelection gained rows 844, 845, 848, 849. Not traced or built. |
| 56 | 0.695 creature props (Ebonguard, Slaug, Abomination, Chomper, Target Dummy, Vine Trap, Outcast, Catapult) | DONE-UNTESTED | G/StoryTools.cs IsCreatureProp :427; G/Creatures.cs SpawnerKind :2770 | Every category-98 prop spawns with its own model. Dummy, mine / Vine Trap and catapult have their own behaviour. The rest are melee with name-based numbers (ours). |

### Exploration and travel

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 57 | Worlds, landscapes, Spire travel, Other Worlds, Pioneer starts | DONE-UNTESTED | G/Travel.cs Handle :33, G/Spire.cs TravelTo :504, G/Landscapes.cs, G/Pioneer.cs, G/Teleport.cs, G/CrossWorld.cs | Cross-world travel checked on the test stack (B.10). A Spire on every landscape (6.49) is untested. Local Travel crystals on non-home landscapes are an open question (L130). |
| 58 | Foundations Museum | N/A | G/Landscapes.cs (zones 431-434 left out) | Closed at launch (P4235, 21 Mar 2016; TASKS 5.2). |
| 59 | Caves, Chaos Caverns, Outposts, Elites, Lost Chests / Crystals | DONE | G/Caves.cs, G/Outposts.cs, G/LootChests.cs | The client reached outposts 0 / 40 / 70 / 80 (TASKS 6.8); 10 s channel (6.1). |
| 60 | Outpost name and builder on approach | PARTIAL | G/Outposts.cs :698 (chat line "Outpost: name") | Live showed the site-entry banner with name and creation date (outposts were build sites, L102). |
| 61 | Party shares loot in the caverns | DONE-UNTESTED | G/LootChests.cs ShareRange :38, :230; G/Gathering.cs PartyShareRange :32; G/RewardChests.cs GroupRange | No two-player test. |
| 62 | Day length 8× | DONE | G/WorldClock.cs DefaultCycleSpeed :20 | Wiki Astronomy; the client follows the server's clock sync. |
| 63 | Teleport to a friend | PARTIAL | G/Abilities.cs :412-436 | Works on the same world only. **A friend on another world gets "Your friend isn't online."** It needs the presence query (G/Social/RemoteSocialService.cs Find, :555-558) and the cross-world hand-over. |
| 64 | Dynamite craters that heal back | DONE | G/WorldEditing.cs, G/Consumables.cs, G/VoxelDestruction.cs | 0.695 test (TASKS 2.6). |
| 65 | 0.695 Underground Adventure from any zone, Lost Crystal lockout | PARTIAL | G/Caves.cs TravelRandom :338, G/Outposts.cs | Lockout is built (6.25 / F3). Caverns on every island (F2) are untested. Missing: outpost rotation (F8), portal crystals off the home landscape (L130), other worlds (L131). |

### Gathering and resources

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 66 | Trees, mining, veins, passive resources, regrowth | DONE | G/Gathering.cs, EnvServer.Core/Terrain/TerrainEdits.cs :480-536 | Harness. The 6.12 / 6.13 rules are untested. |
| 67 | Tool perks | DONE-UNTESTED | G/Abilities.cs, G/Gathering.cs | Axe perks and harvest bonus on trees (6.14, 2026-10-16) are untested. |
| 68 | Refining and composites | DONE | G/Crafting.cs, G/CraftingStations.cs | Harness. |

### Crafting, items and storage

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 69 | Replicator, recipes, refining, discovery cards | DONE | G/Crafting.cs, G/RecipeBook.cs | Harness. |
| 70 | Lumen Station (recipes, bundles, outfits, pockets) | DONE-UNTESTED | station 55 via H/CraftingHandler.cs + client tables | One 0.695 recipe crafted (TASKS 4.20). The categories have not been gone through in game. |
| 71 | Lumens from activities; revive / Parlor costs | DONE | G/Pacing.cs, G/Health.cs, G/Barbershop.cs | The pacing curve is ours. |
| 72 | Repair-o-Matic | DONE | G/Durability.cs | Harness. |
| 73 | Inventory tabs, overflow, vaults, expansions | DONE | G/Inventory/Inventory.cs, G/Vaults.cs, G/ItemUses.cs | Harness (vaults, 2026-10-12). |
| 74 | Consumable stats | DONE | G/Consumables.cs, G/Buffs.cs | Harness. |

### Character

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 75 | Character creation, appearance, starter gear | DONE | AuthServer character handlers, G/Combat/Gear.cs | In use. |
| 76 | Character sheet, titles, Luminary Score | DONE | G/Titles.cs Handle :132, G/Achievements.cs | Harness. The new 0.695 titles (4.16) still need a check with a free bag. |
| 77 | Crimson Parlor options and costs | DONE | G/Barbershop.cs Handle :43 | 0.695 tints gated by unlocks (B4). |
| 78 | Parlor sex / size, Fantasy Parlor Pack, hat unlockers | PARTIAL | G/Barbershop.cs; G/Unlockers.cs TryUse :178; G/RareDrops.cs | Sex / size (60 / 30 Lumens) checked on 0.695. Using the Pack is checked on 0.695. **Missing: the Pack as a Marketplace product** (MarketName / MarketCost, and the Parlor's Unlock button searching the Marketplace; TASKS 4.9). Today the free web store gives it out (G/Store.cs:207). |
| 79 | Biography, Inspect | DONE | G/Inspect.cs Handle :39; bio in SendSelfToClient | 0.695 test (shots_inspect_695.png). Other players' tooltips (3.22) untested; L28 fields open. |
| 80 | Grappling hooks | DONE | client + G/Abilities.cs | |
| 81 | Outfits | DONE | G/Crafting.cs, G/Cosmetics.cs, G/Store.cs | |
| 82 | Founder's outfits | PARTIAL | G/Cosmetics.cs :57, :110-111 (Tech Commander's / Sergeant's Gear only) | No Noble's / Courtier's Regalia definitions and no grant route. The user decided on 2026-10-17 to give them to everyone behind a setting (TASKS 6.59); that work is on branch `e-store`, not on client-0.695. |

### Social, Showcase and progression

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 83 | Chat channels, tells, custom channels, flood limits | DONE | G/Chat.cs Handle :63, G/ChatRooms.cs Handle :57, SocialServer ChatService | Harness. Public channel on / off: #91. |
| 84 | General chat, friends, guilds, mail across worlds | DONE | G/Social/*, LandmarkEmulator.SocialServer | B.1 / B.9 checked on the isolated stack. Some clicks are still listed in TESTING-NEEDED. |
| 85 | Friends, guilds, groups (4, 200 m), ignore | DONE | G/Friends.cs, G/Guilds.cs, G/Groups.cs, G/Ignores.cs | Harness (popups, roster). |
| 86 | Trade, mail, chests with permissions | DONE | G/Trades.cs, G/Mail.cs, G/Chests.cs | Harness. |
| 87 | Showcase: store, Gallery + Visit, competitions, Player Studio, vendors | PARTIAL | G/Store.cs Catalogue :171, G/WebPages.cs, G/Contests.cs, G/Commerce.cs, G/Vendors.cs | The store gives out every outfit and categorised prop for free, with no Daybreak Cash / Lumen prices and no Marketplace product list (TASKS 6.60, waiting on the user). The Vendor Booth is still a shop, though live made it decorative (6.52, waiting on the user). |
| 88 | Journal goals, dailies, titles, Luminary Score, watch list | PARTIAL | G/Achievements*.cs, G/DailyGoals.cs | 5.6 / 5.10 still open: the item-name tracker goals, and the riddle goals the wiki marks "?" (L45). |
| 89 | Reward Chests, Thank You Tokens, "Champion of the People" | DONE-UNTESTED | G/RewardChests.cs :387-436 (objective 2209), G/Cosmetics.cs (title 4519) | The token id 900401 is ours (live's: L24). |

### Interface and commands

| # | Feature | Status | Server code | Gap / evidence |
|---|---|---|---|---|
| 90 | Emotes, /emotelist, bug and site reports | DONE | G/Emotes.cs, G/Reports.cs | Harness (b83fb4b). |
| 91 | /afk, /random, /who, channel join / leave, friend confirm, map positions, ItemDefinitionRequest | PARTIAL | G/Afk.cs Toggle :23, G/RandomRoll.cs :33, G/Who.cs, G/Friends.cs, G/FriendPositions.cs :24; H/InteractionHandler.cs | /who and the friend-confirm popup are checked on 0.695, and 0x49 → 0x4A is checked. **Not handled: CommandPacketChatChannelOn / Off 0x1E/0x22 / 0x23 and SetChatBubbleColor 0x15** (no case in InteractionHandler; coverage-695 rows 532 / 542). Public channels and General on / off (P5310) depend on them (L65). |
| 92 | Emitters, gadgets | DONE | G/Props.cs, G/PropEffects.cs, G/Abilities.cs, G/Consumables.cs | Harness (Cave Seeker, beacons). |
| 93 | Swaps (Get Inspired permission) | PARTIAL | G/Claims.cs :81 (PermissionCopy, unused) | **The server never checks the copy permission.** A SaveDesign or Copy selection (G/BuildingClipboard.cs:228-246) on another player's site is accepted. Paste checks permissions (:477-492); copy and save do not. |

## PARTIAL / MISSING work, in priority order

1. **#93 Get Inspired.** In `BuildingClipboard.HandleSelection`, before `SaveDesignAction` and `Copy`, refuse any
   selection that touches a site where the player's mask lacks `PermissionCopy` (0x100), the same way paste checks
   build rights (:477-492). Answer with InsufficientPermissions. Owners and Ultimate Power holders pass. Open land
   is fine. This needs no new trace.
2. **#47 Graveyards.**
   - (a) Add the cavern Graveyard01 spots (tile 111 in each island's Underground .cpa, already parsed by
     WarpNetwork) to `Graveyards.SitesFor` for players underground, so a cavern death respawns in the cavern.
   - (b) Add a creature no-aggro buffer around them in `Creatures` (wiki: "buffer zone of safety"). The radius is not
     known yet: add an open lead.
   - (c) Surface graveyards (map red circles): not traced yet. Search the client data / map icons / .cpa surface
     pieces, and add an OPEN-LEADS entry before building.
3. **#22 Spawner weapon Links.** Give 577074 / 577075 a runtime instead of the "no effect" default. That needs the
   NPC draw / sheathe mechanism (L95). Then offer row 40 "Spawn With Weapons Equipped" and the Story Node row 41
   Weapon Visuals (#6).
4. **#63 Cross-world teleport to a friend.** When `PlayerManager.FindByGuid` misses, ask the social service's
   presence (`RemoteSocialService.Find`, async, not on the tick). If the friend is on another world, travel with the
   B.10 hand-over to their zone and position.
5. **#29 Game Finder across worlds.** Move a matched player to the other world's game (L31): trace the
   MatchmakingUpdate u64, then join through the B.10 hand-over and JoinTeam.
6. **#28 "On Game Ended" Link (P6517).** Add an event to the Game Table kind (G/StoryTools.cs:348), fired from the
   round end in InstancedGames. The sentence id is not found yet (L21: search text_695 for the table's event first).
   Also settle Score / Victory Node placement (L21).
7. **#91 Chat channel on / off and bubble colour.** Handle 0x1E sub 0x22 / 0x23 {string} (join / leave a public
   channel, kept in the social service's room membership) and 0x15 (store the colour per character; check what the
   client expects back). This depends on L65 (wire identity of public channels).
8. **#60 Outpost banner.** Send the outposts as server-owned build-site records with the name and creation date, the
   way LandmarksOfLandmark does with its admin-template site, instead of the chat line (L102).
9. **#65 Underground Adventure.** Run the F2 client test. Rotate outposts between caverns (F8). Add per-landscape
   Local Travel crystals (L130). Handle other worlds (L131).
10. **#55a Rank 2 / 3 weapon abilities.** Add an open lead and trace which table rows (AbilityLines / AbilityLineMembers
    in gameData695) the ranks are and how the client picks a rank (item record, stat, account). Build only once traced.
11. **#55c playdead.** Trace the client's /playdead path (texts 577222-577225, new AnimationSelection rows 844-849),
    then add it to `Emotes.emotes`. Check the "with [target]" variant.
12. **#55b Roll Wraps.** Find the source (L26) and give it an acquisition route.
13. **#78 / #87 Marketplace.** Build an in-client Marketplace product list (InGamePurchase) with prices for the Fantasy
    Parlor Pack, the six pets and the Build Site Expansion (#45, L86), so the Parlor's Unlock button and "unlock more
    sites" find them. Decide 6.60 (free store as a switchable dev convenience) and 6.52 (Vendor Booth) with the user.
14. **#82 Founder's items.** Finish the 6.59 entitlement setting (branch `e-store`), add Noble's / Courtier's Regalia
    definitions, and merge into client-0.695.
15. **#88 Journal.** Item-name tracker goals (5.10). Riddle goals stay open leads (L45).
16. **#1 / #16 Play-Mode visibility of story tools and the Scoreboard.** Trace L20 (callers of FUN_142d99b70, Models
    1728 vs 1736). If the client does it itself, mark N/A with that proof.
17. **#6 Story Node on non-biped creatures.** L117: decode the creature animation networks, or record the limitation.

## Notes

- Of the DONE-UNTESTED rows, those from 2026-10-15 / 16 are listed with test steps in TESTING-NEEDED.md. The largest
  batches are "Story tools: Story Node lists ...", "Gear, goals, Spires ...", "Chaos Caverns on every island" and
  "Mesh Shapes".
- Many DONE rows were checked on the 0.589 harness before the 0.695 switch. coverage-695.md marks those packet
  families "same". A pass on the 0.695 test copy (<0.695 test copy>) would make them first-hand.
- FEATURES.md should get its Status column updated from this table. The six new gaps at the top should go into
  TASKS.md.
