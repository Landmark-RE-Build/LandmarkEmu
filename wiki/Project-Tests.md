# LandmarkEmulator.Tests

**Test program.** Offline tests: no database, no network, no game client. They reference the server programs and
libraries and test their code directly.

Back to [Code Documentation](Code-Documentation).

## Running

```
dotnet run --project LandmarkEmulator.Tests                 # every test
dotnet run --project LandmarkEmulator.Tests -- Pioneer      # tests whose Class.Method contains "Pioneer"
dotnet test LandmarkEmulator.Tests                          # through the VSTest target
```

The project carries its own small xUnit-style framework (`Framework/`: `[Fact]`, `[Theory]`, the asserts and a console
runner); the exit code is 0 when nothing failed.

## Folders

| Folder | What it checks |
|---|---|
| `Packets/` | Packet layouts byte for byte against what the client reads: primitives, login packets, CellsUpdate, SendSelf, inventory filters, server notices, internal messages, opcode encoding, model round trips |
| `Rules/` | Game rules: admins, achievements, inventory, mail, trade, store, build mode, auto-resupply, geometry, the terrain store, relog, review spending, Pioneer slabs, culture-independent number parsing |
| `Config/` | That every setting agrees in its three places (code default, compose default, `.env.example`) and that the services' settings bind (`ConfigSources.cs` reads all of them) |
| `PatchPacks/` | The pack security rules shared by LaunchPad and the server, the built-in lists, the February opt-in |
| `Support/` | Test helpers: a fake world, a wire helper, the repository root |

Run them before sending a change ([Contributing](Contributing)). After changing a setting, also refresh the reference
pages: `python tools/wiki_reference.py`.
