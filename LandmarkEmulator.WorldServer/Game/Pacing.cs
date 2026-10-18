using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Loot pacing: the play-session curve for Lumens and essences, and the daily limit on gear recipe drops.
    /// <para>
    /// <b>Session curve.</b> Fewer Lumens early in a play session and more in longer ones, always more than repairs
    /// cost (hotfix 26 Apr
    /// 2016, P4774 / P4775; TASKS lists them as P3477-P3479 / P4295, ids that drifted), and the same for essences
    /// (monster, adventure, fire essence and the others, P4776) [P]. No hard daily cap (1 Apr 2016, P4437) [P].
    /// Live's curve is unknown: ours rises linearly from <see cref="StartFactor"/> at login to <see cref="LongFactor"/>
    /// after <see cref="RampTime"/> (TASKS 6.3 / 6.17) [ours]. Lumen chances are multiplied by <see cref="Factor"/> where
    /// they are rolled; essence counts in a loot list go through <see cref="PaceEssences"/>.
    /// </para>
    /// <para>
    /// <b>Gear recipes per day.</b> 1 Apr 2016 (P4438): recipe drops for props and spawners got similar limits and a
    /// cap by category, one armour or one weapon recipe a day, with every weapon and armour still reachable in less
    /// than three weeks [P]. Read here as one armour recipe
    /// and one weapon recipe per character per day [I] (the sentence can also be read as one of either; three weeks for
    /// the 21 armour and 19 weapon "Recipe:" items (items_summary.tsv, slot 8 / slot 2 outputs; a few are goal rewards and
    /// never drop) fits one of each a day better than one in total, which would take about 40 days).
    /// "Day" is the UTC calendar day [ours: live's reset hour is unknown, OPEN-LEADS L119]. Trinket (slot 3), tool and
    /// prop / creature recipes are not limited by this rule. Enforced in <see cref="RecipeBook.RandomUnknownRecipeItem"/>,
    /// which every random recipe drop goes through (creature kills, fungi, gathering, Lost Chests / Crystals, Reward
    /// Chests); the drops of the day are kept in character_unlock kind <see cref="Unlocks.RecipeDropDay"/>, so a relog
    /// doesn't reset them.
    /// </para>
    /// </summary>
    public static class Pacing
    {
        public const double StartFactor = 0.6, LongFactor = 1.4;
        public static readonly TimeSpan RampTime = TimeSpan.FromHours(2);

        /// <summary>When each session started earning (first loot roll; a new login is a new session object).</summary>
        private static readonly ConditionalWeakTable<WorldSession, StrongBox<DateTime>> started = new();

        /// <summary>The multiplier for Lumen and essence chances of this session now.</summary>
        public static double Factor(WorldSession session)
        {
            if (session == null)
                return 1.0;
            DateTime since = started.GetValue(session, _ => new StrongBox<DateTime>(DateTime.UtcNow)).Value;
            double t = Math.Clamp((DateTime.UtcNow - since) / RampTime, 0.0, 1.0);
            return StartFactor + (LongFactor - StartFactor) * t;
        }

        // ---- Essences (TASKS 6.17) ----------------------------------------------------------------------------------

        /// <summary>
        /// The essence items (items_summary.tsv): 1586 Primal, 1587 Earth, 1588 Affliction, 1589 Fire, 1590 Water,
        /// 1591 Radiance, 1592 Shadow, 1593 Lightning, 3682 Monster, 3683 Adventure Essence [P]. (2835 "Test Essence" is
        /// left out: nothing drops it.)
        /// </summary>
        private static readonly HashSet<uint> essences = new() { 1586, 1587, 1588, 1589, 1590, 1591, 1592, 1593, 3682, 3683 };

        public static bool IsEssence(uint item) => essences.Contains(item);

        /// <summary>
        /// A count on the session curve: <paramref name="count"/> × <see cref="Factor"/>, the fraction rounded up at
        /// random (2 × 0.6 = 1.2 → 1, one time in five 2), so small amounts (0-1) are paced too and the average is exact.
        /// </summary>
        public static uint Paced(WorldSession session, uint count, Random random)
        {
            if (count == 0)
                return 0;
            double scaled = count * Factor(session);
            uint whole = (uint)Math.Floor(scaled);
            return whole + (random.NextDouble() < scaled - whole ? 1u : 0u);
        }

        /// <summary>
        /// Puts the essences of a loot list on the session curve (P4776): every essence entry's count goes through
        /// <see cref="Paced"/>; an entry that comes to 0 is dropped. Other items are left as they are. Returns the list.
        /// </summary>
        public static List<(uint Item, uint Count)> PaceEssences(List<(uint Item, uint Count)> loot, WorldSession session, Random random)
        {
            if (loot == null || session == null)
                return loot;
            for (int i = loot.Count - 1; i >= 0; i--)
            {
                (uint item, uint count) = loot[i];
                if (!essences.Contains(item))
                    continue;
                uint paced = Paced(session, count, random);
                if (paced == 0)
                    loot.RemoveAt(i);
                else
                    loot[i] = (item, paced);
            }
            return loot;
        }

        // ---- Gear recipes per day (TASKS 6.3) ----------------------------------------------------------------------

        /// <summary>Gear recipe categories limited per day (P4438): an armour (chest, slot 8) or a weapon (slot 2) recipe.</summary>
        public enum RecipeCategory : uint { Other = 0, Armour = 1, Weapon = 2 }

        /// <summary>Days since 2000-01-01 (UTC), as <see cref="Unlocks.UpkeepLastLoginDay"/> counts them.</summary>
        private static uint Today => (uint)(DateTime.UtcNow.Date - new DateTime(2000, 1, 1)).TotalDays;

        /// <summary>
        /// May this character get a recipe of this category today? Always for <see cref="RecipeCategory.Other"/>; armour
        /// and weapon recipes once a day each (P4438).
        /// </summary>
        public static bool RecipeAllowedToday(WorldSession session, RecipeCategory category)
        {
            if (category == RecipeCategory.Other || session == null)
                return true;
            return !Unlocks.Has(session, Unlocks.RecipeDropDay, Today << 2 | (uint)category);
        }

        /// <summary>
        /// Notes that an armour or weapon recipe dropped for this character today. Rows of earlier days are dropped in the
        /// same save (kind <see cref="Unlocks.RecipeDropDay"/> holds at most today's two rows).
        /// </summary>
        public static void NoteRecipeDrop(WorldSession session, RecipeCategory category)
        {
            if (category == RecipeCategory.Other || session == null)
                return;
            uint today = Today;
            var keep = Unlocks.Values(session.PlayerGuid, Unlocks.RecipeDropDay).Where(v => v >> 2 == today).ToList();
            keep.Add(today << 2 | (uint)category);
            Unlocks.Replace(session, Unlocks.RecipeDropDay, keep);
        }
    }
}
