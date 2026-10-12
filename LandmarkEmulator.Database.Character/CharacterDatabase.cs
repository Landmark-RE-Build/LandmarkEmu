using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Database.Configuration;
using Microsoft.EntityFrameworkCore;
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
        /// Runs one change set in its own context. Most callers fire and forget the returned task, so a failure is
        /// logged here (otherwise it would vanish unobserved and the change would silently not persist) and rethrown
        /// for any caller that does await it.
        /// </summary>
        public async Task Save(Action<CharacterContext> action)
        {
            try
            {
                await using var context = new CharacterContext(config);
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
            using var context = new CharacterContext(config);

            List<string> migrations = context.Database.GetPendingMigrations().ToList();
            if (migrations.Count > 0)
            {
                log.Info($"Applying {migrations.Count} character database migration(s)...");
                foreach (string migration in migrations)
                    log.Info(migration);

                context.Database.Migrate();
            }
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
            using var context = new CharacterContext(config);
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

        /// <summary>A character's unlocks (see <see cref="CharacterUnlockModel"/>).</summary>
        public List<CharacterUnlockModel> GetUnlocks(ulong characterId)
        {
            using var context = new CharacterContext(config);
            return context.CharacterUnlock.AsNoTracking().Where(u => u.CharacterId == characterId).ToList();
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
            foreach (CharacterObjectiveModel row in rows)
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
            using var context = new CharacterContext(config);
            return context.ClaimPermission.AsNoTracking().ToList();
        }

        public List<WorldPropModel> GetWorldProps()
        {
            using var context = new CharacterContext(config);
            return context.WorldProp.AsNoTracking().ToList();
        }

        public List<ChestItemModel> GetChestItems()
        {
            using var context = new CharacterContext(config);
            return context.ChestItem.AsNoTracking().ToList();
        }

        /// <summary>The highest item guid in use, in bags or in chests.</summary>
        public ulong GetMaxItemGuid()
        {
            using var context = new CharacterContext(config);
            ulong items = context.CharacterItem
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
            ulong chests = context.ChestItem
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
            // Build site vendor listings use item guids too.
            ulong vendor = context.ClaimVendorItem
                .Select(r => r.Id)
                .DefaultIfEmpty()
                .Max();
            return System.Math.Max(System.Math.Max(items, chests), vendor);
        }

        public bool CharacterNameExists(string characterName)
        {
            using var context = new CharacterContext(config);
            return context.Character.Any(c => c.Name == characterName);
        }
    }
}
