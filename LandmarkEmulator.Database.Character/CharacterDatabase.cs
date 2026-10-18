using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Database.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.Database.Character
{
    public class CharacterDatabase
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IDatabaseConfig config;

        public CharacterDatabase(IDatabaseConfig config)
        {
            this.config = config;
        }

        /// <summary>A context for a short query or update (the caller disposes it).</summary>
        public CharacterContext NewContext() => new CharacterContext(config);

        /// <summary>
        /// True when this world server keeps its world data (build sites, props, chests, vendors, prop links) in its
        /// own database (Database:Zone set), apart from the shared character database: several worlds then share the
        /// characters, as on live Landmark (one character travels to every server; each server has its own sites).
        /// Without Database:Zone everything stays in the character database, as before.
        /// </summary>
        public bool SeparateWorld => !string.IsNullOrWhiteSpace(config.GetConnectionString(DatabaseType.Zone)?.ConnectionString);

        private DatabaseType WorldType => SeparateWorld ? DatabaseType.Zone : DatabaseType.Character;

        /// <summary>A context on this world's own data (see <see cref="SeparateWorld"/>).</summary>
        public CharacterContext NewWorldContext() => new CharacterContext(config, WorldType);

        /// <summary><see cref="Save(Action{CharacterContext})"/> for world data (build sites, props, chests, vendors, links).</summary>
        public Task SaveWorld(Action<CharacterContext> action) => Save(action, WorldType);

        /// <summary>
        /// Runs one change set in its own context. Most callers fire and forget the returned task, so a failure is
        /// logged here (otherwise it would vanish unobserved and the change would silently not persist) and rethrown
        /// for any caller that does await it.
        /// </summary>
        public Task Save(Action<CharacterContext> action) => Save(action, DatabaseType.Character);

        private async Task Save(Action<CharacterContext> action, DatabaseType type)
        {
            try
            {
                await using var context = new CharacterContext(config, type);
                action.Invoke(context);
                await context.SaveChangesAsync();
            }
            catch (Exception e)
            {
                log.Error(e, "Character save failed.");
                throw;
            }
        }

        public Task Save(ISaveCharacter entity) => Save(entity.Save);

        public Task Save(IEnumerable<ISaveCharacter> entities) => Save(context =>
        {
            foreach (ISaveCharacter entity in entities)
                entity.Save(context);
        });

        public void Migrate()
        {
            Migrate(DatabaseType.Character);
            // A world's own database has the same tables (it uses the world ones).
            if (SeparateWorld)
                Migrate(DatabaseType.Zone);
        }

        private void Migrate(DatabaseType type)
        {
            using var context = new CharacterContext(config, type);

            List<string> migrations = context.Database.GetPendingMigrations().ToList();
            if (migrations.Count > 0)
            {
                log.Info($"Applying {migrations.Count} {(type == DatabaseType.Zone ? "world" : "character")} database migration(s)...");
                foreach (string migration in migrations)
                    log.Info(migration);

                context.Database.Migrate();
            }
        }

        /// <summary>
        /// Deleted characters (DeleteTime set), deleted at or after <paramref name="since"/> (null: all): id, account,
        /// the name they had (OriginalName; the auth server clears Name on delete) and when. Read-only; the world
        /// server's deletion clean-up (TASKS 6.42) reads it.
        /// </summary>
        public List<(ulong Id, ulong AccountId, string Name, DateTime DeleteTime)> GetDeletedCharacters(DateTime? since = null)
        {
            using var context = new CharacterContext(config);
            return context.Character.AsNoTracking()
                .Where(c => c.DeleteTime != null && (since == null || c.DeleteTime >= since))
                .Select(c => new { c.Id, c.AccountId, c.OriginalName, c.Name, c.DeleteTime })
                .AsEnumerable()
                .Select(c => (c.Id, c.AccountId, c.OriginalName ?? c.Name ?? "", c.DeleteTime.Value))
                .ToList();
        }

        public List<CharacterModel> GetAllCharacters()
        {
            using var context = new CharacterContext(config);
            return context.Character.Where(c => c.DeleteTime == null).ToList();
        }

        public ulong GetNextCharacterId()
        {
            using var context = new CharacterContext(config);
            return context.Character
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
        }

        public async Task<CharacterModel> GetCharacterById(ulong characterId)
        {
            await using var context = new CharacterContext(config);
            return await context.Character.FirstOrDefaultAsync(e => e.Id == characterId);
        }

        public async Task<CharacterModel> GetCharacterByName(string name)
        {
            await using var context = new CharacterContext(config);
            return await context.Character.FirstOrDefaultAsync(e => e.Name == name);
        }

        public async Task<List<CharacterModel>> GetCharacters(ulong accountId)
        {
            using var context = new CharacterContext(config);
            return await context.Character.Where(c => c.AccountId == accountId)
                .AsSplitQuery()
                .Include(c => c.Customisation)
                .Include(c => c.Items)
                .ToListAsync();
        }

        /// <summary>
        /// Highest item guid in use, so the server can hand out new ones without collisions.
        /// </summary>
        public List<ClaimModel> GetClaims()
        {
            using var context = NewWorldContext();
            return context.Claim.AsNoTracking().ToList();
        }

        /// <summary>When each of these characters was last in the world (null if never recorded).</summary>
        public Dictionary<ulong, DateTime?> GetLastOnline(IEnumerable<ulong> characterIds)
        {
            var ids = characterIds.Distinct().ToList();
            using var context = new CharacterContext(config);
            return context.Character.AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionary(c => c.Id, c => c.LastOnline);
        }

        public List<CharacterActionSlotModel> GetActionSlots(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterActionSlot.AsNoTracking().Where(s => s.CharacterId == characterId).ToList();
        }

        public List<uint> GetLearnedRecipes(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterRecipe.AsNoTracking().Where(r => r.CharacterId == characterId).Select(r => r.RecipeId).ToList();
        }

        /// <summary>A character's stored client preference document of this kind (Game.Preferences), or null.</summary>
        public byte[] GetPreference(ulong characterId, uint kind)
        {
            using var context = new CharacterContext(config);
            return context.CharacterPreference.AsNoTracking()
                .Where(p => p.CharacterId == characterId && p.Kind == kind)
                .Select(p => p.Xml)
                .FirstOrDefault();
        }

        /// <summary>
        /// Stores a character's client preference document of this kind, replacing the one before (one statement:
        /// INSERT … ON DUPLICATE KEY UPDATE on the (characterId, kind) key). A failure is logged and rethrown.
        /// </summary>
        public async Task SavePreference(ulong characterId, uint kind, byte[] xml)
        {
            try
            {
                await using var context = new CharacterContext(config);
                DateTime now = DateTime.UtcNow;
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO character_preference (characterId, kind, xml, updatedAt) VALUES ({characterId}, {kind}, {xml}, {now}) ON DUPLICATE KEY UPDATE xml = VALUES(xml), updatedAt = VALUES(updatedAt)");
            }
            catch (Exception e)
            {
                log.Error(e, $"Saving preferences of character {characterId:X} failed.");
                throw;
            }
        }

        /// <summary>A character's saved bag filters (Game.InventoryFilters), by filter id.</summary>
        public List<CharacterInventoryFilterModel> GetInventoryFilters(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterInventoryFilter.AsNoTracking()
                .Where(f => f.CharacterId == characterId)
                .OrderBy(f => f.FilterId)
                .ToList();
        }

        /// <summary>
        /// Replaces a character's saved bag filters with <paramref name="filters"/> (all of them: the server keeps the whole
        /// set and writes it as one transaction, delete then insert, so an add / remove / reset burst coalesces into one
        /// write). A failure is logged and rethrown.
        /// </summary>
        public async Task ReplaceInventoryFilters(ulong characterId, IReadOnlyList<CharacterInventoryFilterModel> filters)
        {
            try
            {
                await using var context = new CharacterContext(config);
                // The retrying execution strategy (Database/Extensions.cs) needs a user transaction inside it, run as a unit.
                IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM character_inventory_filter WHERE characterId = {characterId}");
                    foreach (CharacterInventoryFilterModel filter in filters)
                        context.CharacterInventoryFilter.Add(new CharacterInventoryFilterModel
                        {
                            CharacterId = characterId,
                            FilterId = filter.FilterId,
                            Name = filter.Name,
                            FilterString = filter.FilterString
                        });
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                });
            }
            catch (Exception e)
            {
                log.Error(e, $"Saving the bag filters of character {characterId:X} failed.");
                throw;
            }
        }

        /// <summary>A character's unlocks (see <see cref="CharacterUnlockModel"/>).</summary>
        public List<CharacterUnlockModel> GetUnlocks(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterUnlock.AsNoTracking().Where(u => u.CharacterId == characterId).ToList();
        }

        /// <summary>
        /// Adds one unlock row atomically in the database (INSERT IGNORE on the (characterId, kind, value) primary key):
        /// true when this call added it, false when the row was already there — also when another world sharing the
        /// character database added it a moment ago. Used where several worlds may be handed the same one-time event
        /// (a Player Studio creator share, review 2026-10-18 #1); the per-world unlock cache is not consulted, since
        /// it only knows this world's writes.
        /// </summary>
        public bool TryClaimUnlock(ulong characterId, uint kind, uint value)
        {
            using var context = new CharacterContext(config);
            return context.Database.ExecuteSqlInterpolated(
                $"INSERT IGNORE INTO character_unlock (characterId, kind, value) VALUES ({characterId}, {kind}, {value})") == 1;
        }

        /// <summary>
        /// The sum of one unlock kind's values over an account's characters, leaving one character out (the caller has
        /// that one's current value in memory) — one query, for a single-row counter such as the Lumens spent on the store
        /// (the Player Studio reviewer rule, review 2026-10-18b #3). Blocking: call it off the world thread.
        /// </summary>
        public long SumUnlockValuesOfAccount(ulong accountId, uint kind, ulong exceptCharacterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterUnlock.AsNoTracking()
                .Where(u => u.Kind == kind && u.CharacterId != exceptCharacterId
                    && context.Character.Any(c => c.Id == u.CharacterId && c.AccountId == accountId))
                .Sum(u => (long)u.Value);
        }

        /// <summary>Removes one unlock row (undoes <see cref="TryClaimUnlock"/> when the work it guarded failed).</summary>
        public void ReleaseUnlock(ulong characterId, uint kind, uint value)
        {
            using var context = new CharacterContext(config);
            context.Database.ExecuteSqlInterpolated(
                $"DELETE FROM character_unlock WHERE characterId = {characterId} AND kind = {kind} AND value = {value}");
        }

        /// <summary>The achievement objectives this character has progress on.</summary>
        public List<CharacterObjectiveModel> GetObjectives(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterObjective.AsNoTracking().Where(o => o.CharacterId == characterId).ToList();
        }

        /// <summary>Inserts or updates objective rows.</summary>
        public void SaveObjectives(IEnumerable<CharacterObjectiveModel> rows)
        {
            using var context = new CharacterContext(config);
            // One batch can hold the same objective twice (two updates in one tick): both were inserted as new and
            // the save failed on the duplicate key, losing the whole batch. The last value wins.
            foreach (CharacterObjectiveModel row in rows.GroupBy(r => (r.CharacterId, r.ObjectiveId)).Select(g => g.Last()))
            {
                CharacterObjectiveModel existing = context.CharacterObjective
                    .FirstOrDefault(o => o.CharacterId == row.CharacterId && o.ObjectiveId == row.ObjectiveId);
                if (existing == null)
                    context.CharacterObjective.Add(row);
                else
                {
                    existing.State = row.State;
                    existing.Current = row.Current;
                }
            }
            context.SaveChanges();
        }

        /// <summary>The characters on this character's friends list.</summary>
        public List<CharacterModel> GetFriends(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterFriend.AsNoTracking().Where(f => f.CharacterId == characterId)
                .Join(context.Character.AsNoTracking(), f => f.FriendId, c => c.Id, (f, c) => c)
                .Where(c => c.DeleteTime == null).ToList();
        }

        /// <summary>The characters who have this character on their friends list.</summary>
        public List<ulong> GetFriendOf(ulong friendId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterFriend.AsNoTracking().Where(f => f.FriendId == friendId).Select(f => f.CharacterId).ToList();
        }

        public CharacterModel FindCharacterByName(string name)
        {
            using var context = new CharacterContext(config);
            return context.Character.AsNoTracking().FirstOrDefault(c => c.Name == name && c.DeleteTime == null);
        }

        public List<GuildModel> GetGuilds()
        {
            using var context = new CharacterContext(config);
            return context.Guild.AsNoTracking().ToList();
        }

        public List<GuildMemberModel> GetGuildMembers()
        {
            using var context = new CharacterContext(config);
            return context.GuildMember.AsNoTracking().ToList();
        }

        /// <summary>Names of characters by id (for guild rosters).</summary>
        public Dictionary<ulong, string> GetCharacterNames(IEnumerable<ulong> ids)
        {
            var set = ids.ToList();
            using var context = new CharacterContext(config);
            return context.Character.AsNoTracking().Where(c => set.Contains(c.Id)).ToDictionary(c => c.Id, c => c.Name);
        }

        public List<ClaimPermissionModel> GetClaimPermissions()
        {
            using var context = NewWorldContext();
            return context.ClaimPermission.AsNoTracking().ToList();
        }

        public List<WorldPropModel> GetWorldProps()
        {
            using var context = NewWorldContext();
            return context.WorldProp.AsNoTracking().ToList();
        }

        public List<ChestItemModel> GetChestItems()
        {
            using var context = NewWorldContext();
            return context.ChestItem.AsNoTracking().ToList();
        }

        /// <summary>
        /// The highest item guid in use in [<paramref name="low"/>, <paramref name="high"/>), in bags (shared database),
        /// chests and vendor listings (this world's data). Worlds sharing the character database each hand out guids
        /// from their own range (Inventory.NextItemGuid), so they never pick the same one.
        /// </summary>
        public ulong GetMaxItemGuid(ulong low = 0, ulong high = ulong.MaxValue)
        {
            ulong items;
            using (var context = new CharacterContext(config))
                items = context.CharacterItem.Where(r => r.Id >= low && r.Id < high).Select(r => r.Id).DefaultIfEmpty().Max();
            using var world = NewWorldContext();
            ulong chests = world.ChestItem.Where(r => r.Id >= low && r.Id < high).Select(r => r.Id).DefaultIfEmpty().Max();
            // Build site vendor listings use item guids too.
            ulong vendor = world.ClaimVendorItem.Where(r => r.Id >= low && r.Id < high).Select(r => r.Id).DefaultIfEmpty().Max();
            return System.Math.Max(System.Math.Max(items, chests), vendor);
        }

        public bool CharacterNameExists(string characterName)
        {
            using var context = new CharacterContext(config);
            return context.Character.Any(c => c.Name == characterName);
        }
    }
}
