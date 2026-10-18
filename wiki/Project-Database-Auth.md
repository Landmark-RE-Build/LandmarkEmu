# LandmarkEmulator.Database.Auth

**Library.** The EF Core model and migrations of `lmemu_auth`: accounts and the server list.

Back to [Code Documentation](Code-Documentation).

## Main files

| Path | What |
|---|---|
| `AuthContext.cs` | The EF Core context (tables `account`, `zone_server`) |
| `AuthDatabase.cs` | The queries the programs use: accounts (SRP salt and verifier, session keys, server tickets, membership), the server list and heartbeats |
| `Model/AccountModel.cs`, `Model/ZoneServerModel.cs` | The rows |
| `Migrations/` | The schema history; applied at start by the login server and the worlds (`DatabaseManager` in Shared) |

Tables and columns: [Database: Auth](Database-Auth).

To add a migration: `dotnet ef migrations add <Name> --project LandmarkEmulator.Database.Auth --startup-project
LandmarkEmulator.AuthServer` (the design-time factory is in `LandmarkEmulator.AuthServer/Design`).
