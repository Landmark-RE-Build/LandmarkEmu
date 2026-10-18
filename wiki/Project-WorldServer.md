# LandmarkEmulator.WorldServer

**Program** (Docker service `world`, one container per world). The world: the zone simulation that runs every game
system, plus, by default, everything else a world needs in the same process.

Back to [Code Documentation](Code-Documentation).

## What runs in it

- **The zone**: every game system (below), on one world update loop.
- **Its gateway** ([Gateway](Project-Gateway)) on `WORLD_PORT`, unless `GatewayInternalAddress` is set: then the world
  opens no UDP port and dials a [GatewayServer](Project-GatewayServer) instead.
- **In-process services**, each replaced by a link to the remote service when its address is set:

  | Part | In-process | Remote (setting) |
  |---|---|---|
  | Presence, friends, ignores, groups, guilds, chat, mail, Game Finder | `Game/Social/LocalSocialService.cs` | `RemoteSocialService.cs` (`SOCIAL_ADDRESS`) |
  | Player Studio, store, contests, portraits, following | `Game/WorldChannel/LocalCommerceService.cs` | `RemoteCommerceService.cs` (`COMMERCE_ADDRESS`) |
  | Terrain store | `LocalEnvironmentServer` ([EnvServer.Core](Project-EnvServer-Core)) | `Game/Terrain/RemoteEnvironmentServer.cs` (`ENVSERVER_ADDRESS`) |
  | Channel-1 brokers (trade, duels, likes) | `Game/WorldChannel/LocalWorldChannelService.cs` | a [WorldChannel](Project-WorldChannel) container registered with the gateway |

- **In-game web pages** (`Game/WebPages.cs`, `WEB_PAGES_PORT`, default 19001): the Showcase window's store, Player
  Studio, gallery and "My vendors" pages, their JSON API, and the admin menu (`/admin`, `/admin/monitor`). Players are
  signed in by a per-session token the server gives their client.
- **Server list entry and heartbeat** (`Game/ServerHeartbeat.cs`): the world writes its `zone_server` row (name, address,
  players, limit) every few seconds; the login server reads it.
- **Watchdog** (`Game/Watchdog.cs`): a hung world ends itself so Docker restarts it (`WATCHDOG_SECONDS`).
- **Dev commands** (`Game/DevCommands.cs`): see [Command Documentation](Command-Documentation).

## Folders

| Path | What |
|---|---|
| `WorldServer.cs`, `HostedService.cs` | Start-up order: configuration, databases (migrations), game tables and texts, the server-list row, the game systems, the services, the network; the world loop; graceful shutdown (players are sent to character select and saved) |
| `WorldServerConfiguration.cs` | `WorldServer.json` ([Settings Reference](Settings-Reference#worldserverjson)) |
| `Network/` | `ClientSession.cs` (the gateway side of a connection in the in-process gateway), `WorldSession.cs` (the zone side: one player's game state), `GatewayLink.cs` (the link to a separate gateway), `PositionUpdate.cs` / `MovementFanout.cs` (movement and who sees it), `OpcodeStats.cs` (the packet monitor) |
| `Network/Handlers/` | Packet handlers by family: `PlayerHandler`, `InventoryHandler`, `TerrainHandler`, `ClaimsHandler`, `PropHandler`, `CraftingHandler`, `CombatHandler`, `ChatHandler`, `InteractionHandler` (commands, admin commands), `MiscHandler` … |
| `Network/Message/` | Packet models and the zone opcode tables, numbered as the 0.695 client numbers them |
| `Game/` | The game systems, one file (or partial-class set) per system: `Inventory/`, `Combat/`, `Terrain/`, `Social/`, `WorldChannel/`, and files such as `Abilities.cs`, `Building.cs`, `Claims.cs`, `Props*.cs`, `Crafting.cs`, `Gathering*.cs`, `Creatures.cs`, `Pathing.cs`, `Caves.cs`, `Outposts*.cs`, `Spire.cs`, `Landscapes*.cs`, `Teleport*.cs`, `InstancedGames*.cs`, `StoryTools.cs`, `Achievements*.cs`, `DailyGoals.cs`, `Mail.cs`, `Guilds.cs`, `Groups.cs`, `Trades.cs`, `Vendors.cs`, `Store.cs`, `Commerce.cs`, `Membership.cs`, `Entitlements.cs`, `WorldClock.cs`, `Grief.cs`, `Admin*.cs` |
| `Storage/` | Database helpers |
| `Resources/` | `Web/` (the Showcase pages), `PacketDumps/` (reference records the server sends, such as the SendSelf template and reference data) |

What each system does, in player terms and in packets: `docs/gameplay.md`, `docs/protocol.md` and the other
documents in `docs/` in the repository.

## Saved files

Besides the databases, a world keeps files in its data folder (Docker: `server-data/worlds/<WORLD_KEY>`): terrain
edits, felled trees and gathered nodes, contest screenshots and portraits, design images, reports, `console.log` and
`dev_commands.txt`. See [Format: World Data Folder](Format-World-Data-Folder).

## Talks to

- Game clients (through its gateway), their in-game browsers (web pages).
- `lmemu_auth`, `lmemu_character` and its own `lmemu_world_<key>` ([Database Documentation](Database-Documentation)).
- The optional services over [Internal](Project-Internal) links.
- The game data folder (`GameDataPath`) and the Landmark install (`GameDirectory`, read only) for tables, caves,
  templates and biome maps.
