# Database Migrations

Every change to a table in the EF Core databases (`lmemu_auth`, `lmemu_character` and the world databases,
`lmemu_social`) is an EF Core migration. The servers apply pending migrations themselves when they start, so a server
owner never runs anything by hand: after an update, `docker compose up -d --build` is enough.

| Context | Project | Migrations folder | Applied by |
|---|---|---|---|
| `AuthContext` | `LandmarkEmulator.Database.Auth` | `Migrations/` | the auth server and each world server at start-up |
| `CharacterContext` | `LandmarkEmulator.Database.Character` | `Migrations/` | the auth server and each world server at start-up (a world also applies them to its own `lmemu_world_<key>` database) |
| `SocialContext` | `LandmarkEmulator.Database.Social` | `Migrations/` | the social service at start-up |

The start-up log lists each migration it applies ("Applying 2 character database migration(s)...").

The commerce and world-channel services keep their own small databases without EF Core: a new table there is a
`CREATE TABLE IF NOT EXISTS` in their store class, and a new column needs an `ALTER TABLE` that is safe to run again.

## Naming

A migration is named `<id>_<Name>`:

- `<Name>` says what it adds or changes, in PascalCase: `CharacterInventoryFilter`, `ItemOverflowExpiry`,
  `ClaimGroupGuildPermissions`, `ZoneServerMaxPlayers`.
- `<id>` is a 14-digit timestamp `yyyyMMddHHmmss`. EF applies migrations in id order, so **the id must sort after the
  newest migration of that context**. Several existing ids are later than the day they were written; look at the last
  file in the folder and pick an id after it, not today's date.

## Adding a migration

1. Change the model: the `Model/<Name>Model.cs` class, the `DbSet` and the mapping in the context's
   `OnModelCreating` ([Database Style](Database-Style)).
2. Write the migration `Migrations/<id>_<Name>.cs`, as the recent ones are:

   ```csharp
   [DbContext(typeof(CharacterContext))]
   [Migration("20261104094500_CharacterInventoryFilter")]
   public partial class CharacterInventoryFilter : Migration
   {
       protected override void Up(MigrationBuilder migrationBuilder) { /* CreateTable / AddColumn ... */ }
       protected override void Down(MigrationBuilder migrationBuilder) { /* the exact reverse */ }
   }
   ```

   Give it a doc comment that says what the table or column is for and which client packet or system uses it.
   `Down` must undo `Up` exactly.
3. Update the context's `Migrations/<Context>ModelSnapshot.cs` to match the new model, **in the same commit**. If the
   snapshot and the model disagree, EF thinks there are changes without a migration.
4. Build, run the tests, and start a server against a copy of a database to see the migration apply.

You can also let the EF tools write the migration, the Designer file and the snapshot:

```
dotnet tool install --global dotnet-ef
dotnet ef migrations add CharacterInventoryFilter --project LandmarkEmulator.Database.Character --startup-project LandmarkEmulator.AuthServer --context CharacterContext
```

The design-time factories are in the startup projects:

- `LandmarkEmulator.AuthServer/Design/` (auth and character contexts) reads the connection strings from
  `AuthServer.json` in the AuthServer build output (`bin/Debug/net10.0/`, made from `AuthServer.example.json`) and needs
  a reachable MariaDB, because EF asks the server for its version.
- `LandmarkEmulator.SocialServer/Design/` (social context, `--startup-project LandmarkEmulator.SocialServer`) uses a
  fixed MariaDB 11.4 version and needs no running database.

Check the generated code against the conventions before committing: explicit column types and names, `PRIMARY` keys.

## Rules

- **Never edit a migration that has been released.** Add a new one.
- **Migrations must keep existing data.** Add columns with a default, copy data before dropping anything, and mention
  any change that loses data in the pull request and the release notes.
- New text columns are `NOT NULL` with a default (`''`) unless "no value" means something; mark them `.IsRequired()`
  in the model.
- One migration per subject, named for that subject.
