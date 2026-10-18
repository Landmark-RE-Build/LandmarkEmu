# Code Style

These are the conventions the existing code follows. When in doubt, match the file you are editing.

## Layout

- C#, .NET 10. Four spaces, braces on their own line, block-scoped namespaces named after the folder
  (`LandmarkEmulator.WorldServer.Game`, `LandmarkEmulator.WorldServer.Network.Message.Model`).
- Public members `PascalCase`; private fields and locals `camelCase`; a class's logger is
  `private static readonly ILogger log = LogManager.GetCurrentClassLogger();` (NLog).
- **One game system per class** in `LandmarkEmulator.WorldServer/Game/` (`Claims`, `Mail`, `InventoryFilters`, ...).
  A large system is split into partial files named `System.Part.cs` (`Achievements.Triggers.cs`,
  `InstancedGames.Rework.cs`).
- **Packets** are classes in `Network/Message/Model/`, marked with their opcode (`[ZoneMessage(ZoneMessageOpcode.X)]`)
  and implementing `IReadable` (client to server) and/or `IWritable` (server to client). Handlers are methods marked
  `[ZoneMessageHandler(ZoneMessageOpcode.X)]` in `Network/Handlers/`.
- Shared code goes in `LandmarkEmulator.Shared`; messages between the servers in `LandmarkEmulator.Internal`.

See [Code Documentation](Code-Documentation) for what each project does.

## Doc comments: say what the client does

Every packet, field and rule gets an XML doc comment that explains it **from the client's side**, with the evidence:

- the message family, sub-opcode, direction and wire layout (`Abilities 0x27 sub 2 (C→S) {u32 ability id}`);
- the client function that reads or writes it, as its address in the 0.695 `Landmark64.exe` (image base
  `0x140000000`, Ghidra names of the form `FUN_<address>`), or the data table, UI script or log line it comes from;
- what the client does with each value, and what the server must answer;
- for a rule taken from live: where it comes from (a patch-note date, a developer post date, a wiki page name). Write
  it in your own words; don't paste the source.

Contributors find the same place in their own copy of the client from the address. **Never paste decompiled code** into
the repository: describe what it does.

### Evidence marks

Mark how sure a statement is:

| Mark | Meaning |
|---|---|
| `[P]` | Proven from the client: its code, data, or what it did when it ran |
| `[I]` | An inference: likely, but not proven |
| `[I ours]`, "ours" | A value or name the project chose because live's isn't known |

For example: "the client gives a new filter NextFilterId and counts it up [P]; the server keeps at most 64 filters
[I ours]".

If something isn't found yet, say so in the comment and keep looking later. Nothing is called lost or unknown for
good.

## Gameplay settings: live by default, switchable

Every gameplay choice the server makes is a setting that **defaults to how live Landmark behaved**, and server owners
can change it. Extras beyond live default to off. Where live's value isn't known, the default is the project's own
and the note says so.

A new world setting is added in four places, with the same default in each:

1. the code that reads it (`SharedConfiguration.Configuration.GetValue("Name", default)` or a property of
   `WorldServerConfiguration`);
2. `LandmarkEmulator.WorldServer/WorldServer.example.json`;
3. `docker/world/docker-compose.yml` (`Name: ${ENV_NAME:-default}`);
4. `.env.example`, with a note that says what it does, what live did and the source ("true = live (notes 30 Jun
   2016)"), or that the value is ours.

`ConfigConsistencyTests` fails if the four disagree or if a setting is read nowhere. List new
settings in the [Settings Reference](Settings-Reference) too.

## Bandwidth

Landmark sends a lot of data (terrain, props, item definitions). Every feature keeps traffic small:

- send to the players in range only (props stream within `PROP_STREAM_RANGE`, nearest first);
- coalesce: one packet for many changes, one database write for a burst of saves;
- never resend what the client already has; send deltas, not the whole state, on each change;
- cut very large lists into pieces the client can take without freezing (item definitions go out in chunks);
- no per-tick traffic for state that hasn't changed.

## Tests

- Tests live in `LandmarkEmulator.Tests` (xUnit-style `[Fact]` / `[Theory]` with a small built-in runner, offline: no
  database, network or game client).
- A new packet gets a **layout test** that checks its bytes against what the client reads (and a round trip of the
  model). A new rule gets a **rule test**.
- `dotnet test LandmarkEmulator.Tests` must end with **0 failed, 0 skipped**. One check compares the opcode list with a
  research table that is not part of the repository; it reports as skipped in a plain clone, and that is the only skip
  allowed.

## Documentation

Each decoded format, packet family and system is explained in `docs/` (see [Code Documentation](Code-Documentation)
and [Asset Documentation](Asset-Documentation)). Update the matching document in the same change as the code.
