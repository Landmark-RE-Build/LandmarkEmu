# Contributing to LandmarkEmu

A map of the code for people who want to fix or add something. For running a server see the README; for how the
client talks to the server see `docs/` (start with `docs/README.md`).

## Build and test

Needs the **.NET 10 SDK**.

```
dotnet build LandmarkEmulator.sln
dotnet test LandmarkEmulator.sln
```

Every change should keep the test run at **0 failed, 0 skipped**. Most tests are in `LandmarkEmulator.Tests/Rules`
(game rules, using the in-memory `Support/FakeWorld`) and `LandmarkEmulator.Tests/Packets` (byte layouts, many
checked against captures of the live game in `LandmarkEmulator.WorldServer/Resources/PacketDumps`).

## Where things are

Almost all game code is in **`LandmarkEmulator.WorldServer`** (about 108,000 lines). The other projects are listed in
the README's "For developers" table.

| Folder | What is in it |
|---|---|
| `Network/` | The connection: `WorldSession` (one per player: character, inventory, outgoing queue), the gateway link, movement fan-out, `OpcodeStats` |
| `Network/Message/` | `ZoneMessageOpcode` (every opcode the server knows) and `Model/` (one class per packet that has a fixed layout: `IReadable` for what the client sends, `IWritable` for what the server sends) |
| `Network/Handlers/` | Entry points: methods marked `[ZoneMessageHandler(ZoneMessageOpcode.X)]` receive a decoded packet and call the game system |
| `Game/` | The game systems, one static class per system (`Claims`, `Props`, `Guilds`, ...). Large systems are split into partial files (`Props.Movers.cs`, `InstancedGames.Finder.cs`, ...) |
| `Game/Combat`, `Game/Inventory`, `Game/Npc`, `Game/Social`, `Game/Terrain`, `Game/WorldChannel` | Systems that already have their own folder |
| `Storage/` | Database helpers |
| `Resources/` | Data shipped with the server (web pages, NPC definitions, packet dumps for tests) |
| `HostedService.cs` | Start-up order and the world tick (what runs every frame) |
| `WorldServerConfiguration.cs` | Every setting (`WorldServer.json` / Docker environment) |

The terrain store itself (voxel edits, heals, veins, ownership) is **`LandmarkEmulator.EnvServer.Core`**; the world
talks to it only through `IEnvironmentServer` (`Game/Terrain/EnvironmentServer.cs`), so it can run in-process or as
its own service.

### Finding the code for a feature

1. Find the packet: `docs/protocol.md` gives the opcode and sub-opcode; `ZoneMessageOpcode.cs` has the name.
2. Search `Network/Handlers/` for that opcode: the handler names the `Game/` class that does the work.
3. Families with a sub-opcode (`ClaimsBase`, `PropBase`, `TemplateBase`, `InstancedGameBase`, ...) are handed to a
   `Handle(session, sub, body)` method in the game class, which switches on the sub.
4. `docs/gameplay.md` describes each system in words, with the file it lives in.

## How a request flows

```
client ─UDP─> Gateway (LandmarkEmulator.Gateway / GatewayServer)
           ─> WorldSession (Network/WorldSession.cs) reads the zone opcode
           ─> MessageManager finds the [ZoneMessageHandler] for it and builds the packet model
           ─> Network/Handlers/XHandler.Method(session, model)
           ─> Game/X.cs does the work, then answers with session.EnqueueMessage(opcode, body)
              (or sends to everyone in sight / on the landscape through PlayerManager)
```

Services in their own process (social, commerce, world channel, Environment Server) are reached through
`LandmarkEmulator.Internal` messages; each has a `Local...` (in-process) and a `Remote...` implementation behind one
interface, so a world works with or without them.

## Adding things

- **A packet the client sends:** a model in `Network/Message/Model/` (or read the body by hand in a family's `Handle`),
  a `[ZoneMessageHandler]` in `Network/Handlers/`, the logic in `Game/`. Add a layout test in `Tests/Packets/`.
- **A packet the server sends:** write it with `GamePacketWriter` and send it with `session.EnqueueMessage`. Check
  the field order against the client's reader (the docs name it) and add a test that reads it back the client's
  way: many client readers drop a packet that has a byte too many or too few.
- **A setting:** `WorldServerConfiguration`, `Game/ServerSettings.Table.cs`, `docker/world/docker-compose.yml`,
  `.env.example`, `WorldServer.example.json` and `docs/docker.md`, then run `python tools/wiki_reference.py`.
  Gameplay settings default to what the live game did.
- **Work on the world tick:** anything that changes game state later (timers, delayed effects) runs from the tick
  (`HostedService`, e.g. `Abilities.Later`), not from `Task.Run` / `Task.Delay` continuations: the game classes are
  not written for two threads at once.
- **Terrain:** always go through `IEnvironmentServer`; inside `EnvServer.Core` mark changed cells with
  `MarkDirty` / `ClearDirty`.

## Conventions

- Comments explain *why* and cite the client function or capture a rule comes from (`FUN_14xxxxxxx`, see
  `docs/README.md` "Conventions"). Keep them when you move code.
- `[P]` proven from the client or a capture, `[I]` inferred: say which when you add a rule.
- No game files, client code or decompiled code in the repository.

## The planned module layout

The `Game/` folder is flat (183 files) and most systems are static classes. `docs/refactor-plan.md` describes the
planned layout (one folder and namespace per module), the shared helpers that replace repeated code, and the order of
the work.
