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
        /// 13 the build site timer in days and 14 the last login day (days since 2000; see <see cref="SiteUpkeep"/>),
        /// 15 daily goals, 16 a Player Studio design the player has taken (value: the product id; see <see cref="Commerce"/>),
        /// 17 the unique days the player has logged in and 18 an absence already warned about (see <see cref="SiteUpkeep"/>),
        /// 19 Build Site Expansions used (extra site slots; see <see cref="Claims.SlotsOf"/>), 20 a Starter Castle design
        /// the character has beyond the defaults (value: its ExportedTemplate id 1119-1324, the client's
        /// ExportedTemplateCategoryMap category 1; value 0 marks the one-time grant of all pieces to characters made
        /// before 2026-10-16; see <see cref="Templates.HasStarterPiece"/>).
        /// Sizes are single rows
        /// holding the current size.
        /// 21 a Lost Crystal loot lockout (outpost index and end hour; see Outposts.LockedOut).
        /// 22 a Reward Chest of the character's that was emptied while they were offline, not yet counted towards goal
        /// objective 2209 (value: the chest guid folded to 32 bits; see <see cref="RewardChests"/>).
        /// 23 Crimson Parlor customization knowledge from an unlocker item (value: type &lt;&lt; 24 | id, type 3 a
        /// CharacterCreateOptions id, 4 an ArtTint id, as in CharacterCustomization 0x11C sub 2; see <see cref="Unlockers"/>).
        /// 24 an armour / weapon recipe that dropped today (value: day since 2000 &lt;&lt; 2 | 1 armour, 2 weapon; only
        /// today's rows are kept; see <see cref="Pacing.NoteRecipeDrop"/>).
        /// 25 a Lost Crystal loot lockout at an outpost of another landscape than the home continent (outpost index ≥ 128
        /// in the top 12 bits, end hour since 2016 in the low 20; kind 21 keeps the home continent's rows unchanged; see
        /// Outposts.LockedOut).
        /// 26 an entitlement package already granted (value: the package id, 1 founder, 2 Year Zero, 3 promotional; see
        /// <see cref="Entitlements"/>).
        /// 27-31 the grief state (single rows; see <see cref="Grief"/>): 27 grief points, 28 damage points, 29 grief level,
        /// 30 the minute (since 2016) grief last decayed, 31 the minute a grief suspension ends.
        /// 32 a Player Studio creator share (commerce service CommercePayout) already mailed to this character (value: the
        /// payout id): written straight to the database with <c>CharacterDatabase.TryClaimUnlock</c> before the mail, so a
        /// share handed to two worlds, or handed again after its confirmation was lost, is mailed once (review 2026-10-18 #1).
        /// 33 the Lumens this character spent on the store and on Player Studio designs (single row; the Player Studio
        /// reviewer rule, <see cref="Commerce.MayReview"/>).
        /// 34 the starter kit was given (value 1; a character holding items at its first login after 2026-10-18 is marked
        /// too), so an emptied inventory gets no second kit (<see cref="Inventory.Inventory"/>).
        /// </summary>
        public const uint TitleOwned = 1, TitleShown = 2, BagSize = 3, PropTaught = 4, VaultSize = 5, DesignStorage = 6,
            SiteContents = 7, TrackedAchievement = 8, FirstTimeEventSeen = 9, FirstTimeEventsOff = 10,
            ClaimVote = 11, SiteMusic = 12, UpkeepDays = 13, UpkeepLastLoginDay = 14, DailyGoal = 15, StudioDesign = 16,
            UpkeepLoginDays = 17, UpkeepWarned = 18, SiteExpansions = 19, StarterDesign = 20, CrystalLockout = 21,
            RewardChestThanks = 22, ParlorKnowledge = 23, RecipeDropDay = 24, CrystalLockoutWide = 25,
            EntitlementGranted = 26, GriefPoints = 27, GriefDamagePoints = 28, GriefLevel = 29, GriefDecayedAt = 30,
            GriefSuspendedUntil = 31, StudioPayoutMailed = 32, StoreSpent = 33, StarterKitGiven = 34;

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
