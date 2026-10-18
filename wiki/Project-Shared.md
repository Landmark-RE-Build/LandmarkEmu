# LandmarkEmulator.Shared

**Library** used by every server program: configuration, networking, cryptography, the game's data tables and texts.

Back to [Code Documentation](Code-Documentation).

## Folders

| Path | What |
|---|---|
| `Configuration/` | `SharedConfiguration` (a JSON file, then environment variables, then the command line), `ConfigurationManager<T>` (binds the file to a program's configuration class), `NetworkConfig` (Host / Port). See [Settings Reference](Settings-Reference). |
| `Network/` | The SOE UDP layer: `NetworkManager`, `NetworkSession`, `GameSession`, `ProtocolPacketReader` / `Writer`, `DataStreamInput` / `Output` (reliable sequenced data, fragments, acknowledgements), `GamePacketReader` / `Writer` (the little-endian game primitives), `TunnelChannel` |
| `Network/Message/` | The SOE protocol messages and the message manager that dispatches by opcode |
| `Network/Cryptography/` | RC4 (`Arc4Provider`), the SOE CRC, the SOE compression, SRP-6a (`Srp.cs`, `PasswordProvider.cs`) and random numbers |
| `Database/DatabaseManager.cs` | One place that holds the Auth and Character database objects for a program |
| `GameTable/` | Readers for the client's data tables (`Resources/*.txt`, `^`-separated with a header row): `GameTableManager`, `GameTable<T>`, the row models in `Model/` |
| `GameTable/Text/` | The client's texts (`Locale/en_us_data.dat`) and the Jenkins lookup2 hash the client keys them by |
| `Game/` | Small helpers shared by several programs (text tokens, item appearances, group packets, rate limiting) |
| `Command/` | The console command thread (used by the login server) |
| `ThreadManager.cs`, `Singleton.cs`, `IUpdate.cs` | The update loop and singletons |

Formats: [Format: Packet Framing](Format-Packet-Framing), [Format: Game Data Tables](Format-Game-Data-Tables).
