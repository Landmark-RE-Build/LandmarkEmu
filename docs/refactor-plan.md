# Refactor plan: modules and shared helpers

Status: plan (2026-10-29). The work happens on the branch `local/refactor`; nothing changes behaviour, and every step
keeps `dotnet test LandmarkEmulator.sln` at 0 failed, 0 skipped. `CONTRIBUTING.md` describes the code as it is.

## Why

- `LandmarkEmulator.WorldServer/Game/` holds 183 files side by side; nothing shows which belong together.
- 139 of them are static classes with their state in static fields, several of 2,000-4,000 lines (`Creatures.cs`
  4,063, `DevCommands.cs` 2,650, `Props.cs` 2,529, `StoryTools.cs` 2,497, `Claims.cs` 2,353, `InstancedGames.cs`
  2,234).
- The same few patterns are written out again and again (counted in WorldServer):
  - 177 × `var output = new List<byte>(); var writer = new GamePacketWriter(output); ... output.ToArray()`, and 16
    private `Build(sub, write)` helpers doing the same per class;
  - 41 × "send this to everyone on the landscape" loops over `PlayerManager.InZone`;
  - 84 × `ConfigurationManager<WorldServerConfiguration>.Instance.Config` (often with its own null check);
  - 48 × local ↔ global coordinate conversions written by hand around `TerrainStore.ZoneOrigin`;
  - 34 × "attach a model, set fields, MarkModified" partial saves;
  - several copies of small geometry helpers (flat distance, box tests).
- All 22 files of `LandmarkEmulator.EnvServer.Core` still declare `namespace LandmarkEmulator.WorldServer.Game.Terrain`.

## Steps (each one its own commit, tests green after each)

1. **Namespaces that match the project:** `EnvServer.Core` → `LandmarkEmulator.EnvServer.Terrain`.
2. **Modules:** move every `Game/*.cs` into its module folder and namespace (table below). Module names never
   repeat a class name (a namespace `Props` next to the class `Props` would break every `Props.X` call). One
   `GlobalUsings.cs` per project makes every module visible, so code inside the modules needs no extra `using` lines.
3. **Shared helpers** (in `Game/Common` or `Network/`), then each module switched to them:
   - `Packets.Build(write)` / `Packets.Build(sub, write)` for the writer boilerplate;
   - `PlayerManager.SendToLandscape(zone, opcode, body, except)` and `SendToViewers(...)` for the broadcast loops;
   - `WorldConfig.Current` for the configuration;
   - `Coords.ToLocal / ToGlobal` for the coordinate conversions;
   - `CharacterDatabase.SaveFields(model, ...)` for the partial saves;
   - `Geometry.Flat / InBox` for the small geometry helpers.
4. **Big files split by concern** into partial files in their module (for example `Creatures.Spawning.cs`,
   `Creatures.Ai.cs`, `Creatures.Network.cs`), keeping each part's comments with its code.
5. **Docs:** `CONTRIBUTING.md` and `docs/gameplay.md` file paths updated to the new layout.

Not in this refactor (bigger, later): turning the static modules into instance services with interfaces, and
moving the reverse-engineering detail from code comments into `docs/`.

## The modules

Existing folders keep their names and namespaces: `Combat`, `Inventory`, `Npc`, `Social`, `Terrain`, `WorldChannel`.

| Module | What | Files | Lines | Systems (and their partial files) |
|---|---|---|---|---|
| `Game/Operations` | Admins, dev commands, server settings, monitoring, restarts and shutdown | 28 | 8,689 | Admin, AdminCommands, AdminMonitor, AdminMonitorPage, AdminSessions, Broadcasts, DevCommands, DynamicCommands, IconExport, ItemAudit, Kicks, LoginQueue, Reports, RestartSchedule, ServerClock, ServerHeartbeat, ServerSettings, ServerSettingsPage, ServiceStatuses, Shutdown, Telemetry, Watchdog |
| `Game/Sessions` | Who is online and where: the player list, positions, sight | 5 | 1,524 | Afk, Pacing, PlayerManager, PlayerPositions, Sight |
| `Game/ClientUi` | Talking to the client UI: system messages, client Lua, right-click menus, web pages | 5 | 1,290 | ClientFixes, ClientLua, RightClickMenus, SystemMessages, WebPages |
| `Game/Construction` | Voxel building: tools, copy / paste, designs, palettes, digging | 14 | 5,153 | BuildModeHolster, Building, BuildingClipboard, BuildingOffGridPaste, DesignItems, DesignMaterials, ModelFootprints, Palettes, SiteDesigns, TemplateInstances, Templates, VoxelDestruction, WorldEditing |
| `Game/Fixtures` | Props and everything placed: chests, movers, platforms, fireworks, prop settings | 14 | 6,466 | Chests, Fireworks, LootChests, PropAnimations, PropEffects, PropLimits, PropSettings, Props |
| `Game/Sites` | Build sites: claims, permissions, upkeep, contents, Pioneer sites | 7 | 3,842 | ClaimInventory, Claims, Pioneer, SiteContents, SiteScreenshots, SiteUpkeep |
| `Game/World` | The landscapes: Spire, outposts, caves, graveyards, travel and teleports, world clock | 24 | 11,436 | CavernArrivalGuard, Caves, CrossWorld, Graveyards, LandmarksOfLandmark, Landscapes, OutpostSites, Outposts, Pathing, ProceduralPlacement, RuinSlots, SafeEject, Spire, Teleport, TeleportRequests, Travel, TravelQueue, WarpNetwork, WorldClock |
| `Game/Npc` | Creatures, their weapons, pets, world NPCs, creature loot | 5 | 5,435 | CreatureWeapons, Creatures, LootPinatas, Pets, RareDrops |
| `Game/Combat` | Abilities, health, buffs, projectiles, weapons, encounters, stats | 15 | 5,979 | Abilities, BootAbilities, Buffs, CharacterStats, DeadBodies, DefaultCalculatedStats, Encounters, Grief, Health, HeroicMovement, HitEffects, MovementEffects, Projectiles, StatNames, WeaponAttacks |
| `Game/Items` | Items outside the bag code: uses, consumables, vaults, unlocks, salvage, wear | 10 | 2,784 | Consumables, Durability, InventoryFilters, ItemUses, OverflowExpiry, RewardChests, Salvaging, Unlockers, Unlocks, Vaults |
| `Game/Production` | Gathering and crafting: nodes, recipes, stations | 6 | 2,842 | BarrenRecipes, Crafting, CraftingStations, Gathering, RecipeBook |
| `Game/Progression` | Goals and rewards: achievements, daily goals, titles, first-time events, membership | 10 | 4,323 | Achievements, DailyGoals, Entitlements, FirstTimeEvents, Membership, Titles |
| `Game/Characters` | The character itself: looks, emotes, preferences, inspect, deletion | 10 | 2,257 | ArtTints, Barbershop, CharacterDeletion, CharacterResources, EmoteHolster, Emotes, Inspect, Portraits, Preferences, TemporaryAppearance |
| `Game/Social` | Chat, friends, groups, guilds, mail, trades | 15 | 6,162 | Chat, ChatBubbleColor, ChatRooms, FriendPositions, Friends, Groups, Guilds, Ignores, Mail, PublicChannels, RandomRoll, ShowcaseFollow, TargetNotOnline, Trades, Who |
| `Game/Economy` | Store, commerce, vendors, gallery, contests, ratings | 7 | 3,571 | Commerce, Contests, Gallery, Ratings, Store, StoreBundles, Vendors |
| `Game/Games` | Site games and story tools | 8 | 7,791 | AreaDefinitions, InstancedGames, StoryTools |

## Working on top of it

The refactor moves almost every file, so a branch made before it will conflict on merge. Moves are kept apart from
content changes (step 2 only moves files and changes `namespace` lines), so `git merge` with rename detection
(`-X find-renames`) carries most older changes over by itself.
