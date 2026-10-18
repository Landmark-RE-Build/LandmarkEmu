# Server Building

You only need to build the code yourself to develop or test changes. To **host** a server, Docker builds everything
for you ([Server Installation](Server-Installation)).

- [Building on Windows](Server-Building-Windows)
- [Building on Linux](Server-Building-Linux)

## Requirements

- **.NET 10 SDK.** Every project targets `net10.0`; LaunchPad targets `net10.0-windows` (WPF).
- **git** (or a downloaded ZIP of the code: [Server Cloning](Server-Cloning)).
- Optional: an IDE for `LandmarkEmulator.sln`, such as Visual Studio or JetBrains Rider (a version that supports
  .NET 10), or VS Code with the C# extension.
- To run servers outside Docker: a MariaDB server.

NuGet restores the packages on the first build (EF Core 9 with the Pomelo MySQL provider, NLog, SharpZipLib, WebView2
for LaunchPad).

## The projects

| Project | What it builds |
|---|---|
| `LandmarkEmulator.AuthServer` | The login server and the account service (with `LandmarkEmulator.WebAPI`) |
| `LandmarkEmulator.WorldServer` | A world (zone) server |
| `LandmarkEmulator.SocialServer`, `LandmarkEmulator.CommerceServer`, `LandmarkEmulator.EnvServer`, `LandmarkEmulator.GatewayServer`, `LandmarkEmulator.WorldChannel` | The optional services |
| `LandmarkEmulator.LaunchPad` | LaunchPad (Windows only) |
| `LandmarkEmulator.Tests` | The offline tests |
| the rest | Shared code, database contexts, the internal transport, patch packs, the capture parser |

More: [Code Documentation](Code-Documentation).

## Tests

```
dotnet test LandmarkEmulator.Tests
```

The tests run offline (no database, network or game client) and end with a line such as
`Tests: 1527 total, 1527 passed, 0 failed, 0 skipped`. A change is ready when nothing fails and nothing is skipped.
One check compares the opcode list against a research table that isn't part of the repository; in a plain clone it
reports as skipped, and that is the only expected skip.

To run some of the tests only, run the project with a name filter:

```
dotnet run --project LandmarkEmulator.Tests -- ConfigConsistency
```

## Running the servers

The servers are meant to run in Docker; to try a change in Docker, rebuild the images with
`docker compose up -d --build` from the project folder ([Server Installation](Server-Installation)).

They can also run straight from the build output, against your own MariaDB. Each server reads a JSON file next to its
exe, made from the `*.example.json` in the project (`AuthServer.example.json` → `AuthServer.json`,
`WorldServer.example.json` → `WorldServer.json`): set the database connection strings, `GameDataPath` (a folder with the
game data, see [Game Data](Game-Data)) and, for a world, `GameDirectory` (your Landmark folder). Create the
`lmemu_auth` and `lmemu_character` databases first (`docker/db-init/01-databases.sql` shows the statements); the
servers create their tables themselves. This is the older way of running the project and isn't what the guides
describe; prefer Docker.
