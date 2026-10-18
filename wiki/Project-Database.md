# LandmarkEmulator.Database

**Library.** Database configuration and the EF Core provider set-up shared by the model projects
([Database.Auth](Project-Database-Auth), [Database.Character](Project-Database-Character),
[Database.Social](Project-Database-Social)).

Back to [Code Documentation](Code-Documentation).

## Main files

| Path | What |
|---|---|
| `Configuration/DatabaseConfig.cs` | The `Database` settings block: `Auth`, `Character`, `Zone` (a world's own database) and `Social`, each a connection string and a provider |
| `Configuration/DatabaseType.cs`, `DatabaseProvider.cs` | Which database, which provider |
| `Extensions.cs` | `UseConfiguration`: MySQL / MariaDB through Pomelo, with retries on failure; the server version is detected once per connection string |
| `DatabaseDataException.cs` | Data errors |

Only MySQL / MariaDB is supported (the `MySql` provider); any other provider is refused at start with a message saying so.

See [Database Documentation](Database-Documentation).
