# Database Style

The servers keep their data in **MariaDB** (11.4 in the Docker setup) through **Entity Framework Core 9** with the
Pomelo MySQL provider. What each table holds is in the [Database Documentation](Database-Documentation); this page is
about how new tables and columns are written.

## The databases and their contexts

| Database | Code | Holds |
|---|---|---|
| `lmemu_auth` | `LandmarkEmulator.Database.Auth` (`AuthContext`, `AuthDatabase`) | Accounts, the server list |
| `lmemu_character` | `LandmarkEmulator.Database.Character` (`CharacterContext`, `CharacterDatabase`) | Characters, items, guilds, mail, designs and everything else shared by all worlds |
| `lmemu_world_<WORLD_KEY>` | the same `CharacterContext` (database type `Zone`) | One world's own build sites, props, prop links and settings, chests and vendor items |
| `lmemu_social` | `LandmarkEmulator.Database.Social` (`SocialContext`, `SocialDatabase`) | The optional social service: presence, friends, ignore lists, guilds, mail |
| `lmemu_commerce` | `LandmarkEmulator.CommerceServer` (`CommerceStore`) | The optional commerce service: Player Studio, contests, portraits, following |
| `lmemu_wc_<WORLD_KEY>` | `LandmarkEmulator.WorldChannel` (`VoteStore`) | The optional world channel: build-site likes |

- A world database has the same tables as `lmemu_character` (both are made by the same migrations); each uses its own
  part. World tables are read and written through `CharacterDatabase.NewWorldContext()` / `SaveWorld()`, everything
  else through the shared context.
- The commerce and world-channel services don't use EF Core: they create their tables themselves with
  `CREATE TABLE IF NOT EXISTS` (MySqlConnector) on their first start.
- A **new database** also needs its grant in `docker/db-init/01-databases.sql` (new installs), and a note in the docs
  with the one-line `GRANT` for databases created before it.

## Tables and columns

Follow the existing tables (`CharacterContext.OnModelCreating` is the reference):

- **Table names**: `snake_case`, singular, prefixed by what they belong to: `character_item`,
  `character_inventory_filter`, `claim_permission`, `world_prop_setting`, `social_mail_attachment`.
- **Column names**: `camelCase` (`characterId`, `filterId`, `filterString`), set with `HasColumnName`.
- **Column types** spelled out with `HasColumnType`, in MariaDB terms: `bigint(20) unsigned` for ids and guids,
  `int(10) unsigned`, `tinyint(3) unsigned`, `tinyint(1)` for a bool, `float` / `double`, `varchar(n)` with a real
  limit, `datetime`.
- **Primary keys** named `PRIMARY` (`HasKey(...).HasName("PRIMARY")`); composite keys for per-character rows
  (`characterId`, `filterId`).
- Tables use `utf8mb4`.
- **Text and blob columns created `NOT NULL` need `.IsRequired()`** in the model. The database projects have no
  nullable reference types, so a `string` or `byte[]` property is otherwise treated as nullable and EF reports pending
  model changes against the snapshot.

## Model classes

- One class per table in the project's `Model/` folder, named `<Name>Model` (`CharacterInventoryFilterModel`), with
  public auto-properties.
- Doc comments say what the row is **for the client**: the client structure it mirrors, the packet it comes from and
  goes back in, and the limits the server applies (see [Code Style](Code-Style)).
- Add a `DbSet` with a one-line doc comment to the context, and the table's mapping in `OnModelCreating`.

## Reading and saving

- Saves go through the database classes (`CharacterDatabase.Save`, `SaveWorld`, the `ISaveCharacter` /
  `ISaveAuth` interfaces) so each runs in one transaction and failures are logged.
- **Coalesce writes**: a burst of changes from one player becomes one write (a newer set replaces a pending one while
  a write runs), as the bag filters and client preferences do.
- Reads that don't change anything use `AsNoTracking()`.
- Ids the servers choose themselves are allocated per world so two worlds never collide: world `n` takes item guids
  from `n × 2^28` and guild ids from `n × 2^20`.

## Changing the schema

Every schema change is a migration: [Database Migrations](Database-Migrations).
