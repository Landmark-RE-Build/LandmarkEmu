# LandmarkEmulator.Database.Character

**Library.** The EF Core model and migrations of the character database (`lmemu_character`) and of each world's own
database (`lmemu_world_<WORLD_KEY>`), which use the same model.

Back to [Code Documentation](Code-Documentation).

## Main files

| Path | What |
|---|---|
| `CharacterContext.cs` | The EF Core context: every table, its keys and indexes. A context opened for `DatabaseType.Zone` uses the world's own database |
| `CharacterDatabase.cs` | The queries; `NewWorldContext()` / `SaveWorld()` for world data, which go to the world's own database when `Database:Zone` is set and to the character database otherwise |
| `Model/` | One class per table (`CharacterModel`, `CharacterItemModel`, `ClaimModel`, `WorldPropModel`, `MailModel`, `GuildModel`, `TemplateModel`, `PlayerStudioProductModel` …) |
| `ISaveCharacter.cs` | Game objects that save themselves into a context |
| `Migrations/` | The schema history; applied at start (the character database, and the world's own database when it has one) |

Tables and columns: [Database: Character](Database-Character) and [Database: World](Database-World).

To add a migration: `dotnet ef migrations add <Name> --project LandmarkEmulator.Database.Character --startup-project
LandmarkEmulator.AuthServer`. Keep the model, the migration and `Migrations/CharacterContextModelSnapshot.cs` in step
(a text column a migration makes NOT NULL needs `.IsRequired()` in the model).
