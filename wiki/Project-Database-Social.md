# LandmarkEmulator.Database.Social

**Library.** The EF Core model and migrations of `lmemu_social`, the shared social service's own database.

Back to [Code Documentation](Code-Documentation).

## Main files

| Path | What |
|---|---|
| `SocialContext.cs` | The EF Core context |
| `SocialDatabase.cs`, `ISocialStore.cs` | The store the social service uses |
| `SocialImport.cs` | The one-time copy of the friends and ignore lists and the character names from the character database on the service's first start (`SOCIAL_IMPORT_FROM`) |
| `Model/` | `SocialCharacterModel`, `FriendModel`, `IgnoreEntryModel`, `SocialGuildModel`, `SocialMailModel` |
| `Migrations/` | The schema history; applied at start by the social service |

Tables and columns: [Database: Social](Database-Social). The design-time factory is in
`LandmarkEmulator.SocialServer/Design`.
