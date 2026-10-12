using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Small per-character unlocks and choices kept in the character_unlock table as (kind, value) rows: titles owned
    /// and the one shown (<see cref="Titles"/>), props a recipe item taught (<see cref="RecipeBook"/>), storage sizes
    /// raised by expansions (<see cref="ItemUses"/>). Loaded once at login and cached; changes are written straight
    /// through.
    /// </summary>
    public static class Unlocks
    {
        /// <summary>
        /// Kinds: 1 title owned, 2 title shown, 3 bag size, 4 prop taught by a recipe item, 5 vault size, 6 design
        /// (voxel) storage size, 7 a lapsed build site whose contents wait in a Build Site Contents item (value: the
        /// claim id), 8 an achievement the player tracks in the Goals panel, 9 a first-time event (help page) already
        /// seen, 10 first-time events turned off (value 1; see <see cref="FirstTimeEvents"/>), 11 a build site the player
        /// rated (value: the claim id; see <see cref="Ratings"/>), 12 the music chosen for the player's build site,
        /// 13 the build site timer in days and 14 the last login day (days since 2000; see <see cref="SiteUpkeep"/>).
        /// Sizes are single rows
        /// holding the current size.
        /// </summary>
        public const uint TitleOwned = 1, TitleShown = 2, BagSize = 3, PropTaught = 4, VaultSize = 5, DesignStorage = 6,
            SiteContents = 7, TrackedAchievement = 8, FirstTimeEventSeen = 9, FirstTimeEventsOff = 10,
            ClaimVote = 11, SiteMusic = 12, UpkeepDays = 13, UpkeepLastLoginDay = 14, DailyGoal = 15;

        private static readonly ConcurrentDictionary<ulong, HashSet<(uint Kind, uint Value)>> cache = new();

        /// <summary>Reads a character's unlocks at login.</summary>
        public static void Load(ulong characterId)
            => cache[characterId] = DatabaseManager.Instance.CharacterDatabase.GetUnlocks(characterId)
                .Select(u => (u.Kind, u.Value)).ToHashSet();

        private static HashSet<(uint, uint)> Of(ulong characterId) => cache.GetOrAdd(characterId, _ => new HashSet<(uint, uint)>());

        public static bool Has(WorldSession session, uint kind, uint value)
        {
            var set = Of(session.PlayerGuid);
            lock (set)
                return set.Contains((kind, value));
        }

        /// <summary>
        /// The values of one kind for a character who may be offline: read from the database (and cached) when the
        /// character's unlocks aren't loaded.
        /// </summary>
        public static List<uint> ValuesOfAnyone(ulong characterId, uint kind)
        {
            if (!cache.ContainsKey(characterId))
                Load(characterId);
            return Values(characterId, kind);
        }

        /// <summary>The values a character has of one kind.</summary>
        public static List<uint> Values(ulong characterId, uint kind)
        {
            var set = Of(characterId);
            lock (set)
                return set.Where(u => u.Item1 == kind).Select(u => u.Item2).ToList();
        }

        /// <summary>Adds an unlock; false when the character already had it.</summary>
        public static bool Add(WorldSession session, uint kind, uint value) => Add(session.PlayerGuid, kind, value);

        /// <summary>Adds an unlock for a character who may be offline (it is read again at their next login).</summary>
        public static bool Add(ulong characterId, uint kind, uint value)
        {
            var set = Of(characterId);
            lock (set)
                if (!set.Add((kind, value)))
                    return false;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
                c.CharacterUnlock.Add(new CharacterUnlockModel { CharacterId = characterId, Kind = kind, Value = value }));
            return true;
        }

        /// <summary>Sets the single value of a kind (0 = none), e.g. the title shown.</summary>
        public static void Set(WorldSession session, uint kind, uint value)
        {
            ulong characterId = session.PlayerGuid;
            var set = Of(characterId);
            lock (set)
            {
                set.RemoveWhere(u => u.Item1 == kind);
                if (value != 0)
                    set.Add((kind, value));
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.CharacterUnlock.RemoveRange(c.CharacterUnlock.Where(u => u.CharacterId == characterId && u.Kind == kind));
                if (value != 0)
                    c.CharacterUnlock.Add(new CharacterUnlockModel { CharacterId = characterId, Kind = kind, Value = value });
            });
        }

        /// <summary>Replaces every unlock of one kind with <paramref name="values"/>, in a single save.</summary>
        public static void Replace(WorldSession session, uint kind, IEnumerable<uint> values)
        {
            ulong characterId = session.PlayerGuid;
            uint[] list = values.Distinct().ToArray();
            var set = Of(characterId);
            lock (set)
            {
                set.RemoveWhere(u => u.Item1 == kind);
                foreach (uint v in list)
                    set.Add((kind, v));
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.CharacterUnlock.RemoveRange(c.CharacterUnlock.Where(u => u.CharacterId == characterId && u.Kind == kind));
                foreach (uint v in list)
                    c.CharacterUnlock.Add(new CharacterUnlockModel { CharacterId = characterId, Kind = kind, Value = v });
            });
        }

        /// <summary>Removes every unlock of some kinds (dev resets).</summary>
        public static void Clear(WorldSession session, params uint[] kinds)
        {
            ulong characterId = session.PlayerGuid;
            var set = Of(characterId);
            lock (set)
                set.RemoveWhere(u => kinds.Contains(u.Item1));
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
                c.CharacterUnlock.RemoveRange(c.CharacterUnlock.Where(u => u.CharacterId == characterId && kinds.Contains(u.Kind))));
        }
    }
}
