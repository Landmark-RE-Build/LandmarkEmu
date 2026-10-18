using System;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The rare cosmetic drops of the 0.695-era updates. Rates are ours: the notes only say "rare" / "a chance".
    /// </summary>
    public static class RareDrops
    {
        /// <summary>
        /// Lop Ear Bunny figurine from an Outpost elite (28 Jul 2016, P6143: outpost elites rarely drop the Lop Ear
        /// Bunny pet). Ours: 2 % (trace/0695/spec-pets.md 5.6).
        /// </summary>
        public const double LopEarChance = 0.02;

        /// <summary>
        /// Hat unlockers from Outposts and the higher-tier Reward Chests (29 Sep 2016 game update: outposts and the
        /// higher player reward chests may drop unlockers for two new hats, and those unlockers can be traded; P6505).
        /// The two new hats are taken to be
        /// the Ninja Mask 4522 and Samurai Helmet 4523 (inferred: the Cowboy Hat 4521 is not called new). Ours: 5 %.
        /// </summary>
        public const double HatChance = 0.05;

        public const uint NinjaMaskUnlocker = 4522, SamuraiHelmetUnlocker = 4523;

        /// <summary>
        /// Repair Bot Beacon 4129 is also a rare drop anywhere (26 Apr 2016, P4835): from creatures and rich
        /// gathering. Ours: 0.5 %.
        /// </summary>
        public const uint RepairBotBeacon = 4129;
        public const double BeaconChance = 0.005;

        /// <summary>The beacon at <see cref="BeaconChance"/>, else 0.</summary>
        public static uint Beacon(Random random) => random.NextDouble() < BeaconChance ? RepairBotBeacon : 0;

        /// <summary>
        /// When hat unlockers drop (HAT_DROPS): Season = live — they came with the Halloween update (29 Sep 2016, P6505) and
        /// Halloween items stopped dropping from outposts on 6 Dec (P6565), so only while <see cref="Current"/> is
        /// Halloween (default); Always = all year; Off = never. A server-owner toggle (user 2026-10-18).
        /// </summary>
        public enum HatMode { Season, Always, Off }

        public static HatMode HatDrops { get; set; } = ParseHatMode(Environment.GetEnvironmentVariable("HAT_DROPS"));

        /// <summary>HAT_DROPS text → mode (always / all / on, off / never, anything else season).</summary>
        public static HatMode ParseHatMode(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "always" or "all" or "on" => HatMode.Always,
            "off" or "never" => HatMode.Off,
            _ => HatMode.Season
        };

        /// <summary>A hat unlocker at <see cref="HatChance"/> while hats drop (<see cref="HatDrops"/>), else 0.</summary>
        public static uint Hat(Random random)
        {
            if (HatDrops == HatMode.Off || HatDrops == HatMode.Season && Current(DateTime.UtcNow) != Season.Halloween)
                return 0;
            return random.NextDouble() < HatChance ? (random.Next(2) == 0 ? NinjaMaskUnlocker : SamuraiHelmetUnlocker) : 0;
        }

        /// <summary>
        /// The seasonal event drops of late 2016 (TASKS 4.17, trace/0695/cosmetics-695.md §5):
        /// <list type="bullet">
        /// <item><b>Halloween</b> (from the 29 Sep 2016 update, P6504-P6507: skeleton bodysuits can be found too; no
        /// more Halloween items from outposts from 6 Dec, P6565): a Skeletal Bodysuit from
        /// Outposts.</item>
        /// <item><b>Winter</b> (from 6 Dec 2016, P6566 / P6567 / P6569): a festive Colonist's Flight Suit or one of the three
        /// older Holiday Sweaters from Outposts; a festive Flight Suit from the higher-end Reward Chests.</item>
        /// </list>
        /// Default auto = live (the 2016 calendar every year; user 2026-10-18). Switch with the environment variable
        /// LANDMARK_SEASON = off | auto | halloween | winter (or set <see cref="Setting"/>). "auto" follows the 2016 calendar:
        /// Halloween 28 Sep - 5 Dec, Winter 6 Dec - 10 Jan (the Winter Holiday Island stayed until early January, P6560
        /// /
        /// P6573; the end day is ours). The hats (<see cref="Hat"/>) follow the Halloween season too (HAT_DROPS). The 2016
        /// Green / Blue sweaters were giveaways (P6570), never drops.
        /// </summary>
        public enum Season { Off, Auto, Halloween, Winter }

        /// <summary>The configured season (LANDMARK_SEASON, default auto = live).</summary>
        public static Season Setting { get; set; } = ParseSeason(Environment.GetEnvironmentVariable("LANDMARK_SEASON"));

        public static Season ParseSeason(string value) => value?.Trim().ToLowerInvariant() switch
        {
            "off" or "none" => Season.Off,
            "halloween" => Season.Halloween,
            "winter" or "holiday" => Season.Winter,
            _ => Season.Auto
        };

        /// <summary>The season in effect now: the setting, or for "auto" the 2016 dates (UTC).</summary>
        public static Season Current(DateTime utcNow)
        {
            if (Setting != Season.Auto)
                return Setting;
            int day = utcNow.Month * 100 + utcNow.Day;
            if (day >= 928 && day <= 1205)
                return Season.Halloween;
            if (day >= 1206 || day <= 110)
                return Season.Winter;
            return Season.Off;
        }

        /// <summary>Chance of a seasonal outfit per Outpost Lost Crystal loot / higher-end chest. Ours: the notes say "a chance".</summary>
        public const double SeasonalOutfitChance = 0.05;

        /// <summary>An Outpost's seasonal outfit (Lost Crystal loot) at <see cref="SeasonalOutfitChance"/>, else 0.</summary>
        public static uint OutpostSeasonal(Random random) => Current(DateTime.UtcNow) switch
        {
            Season.Halloween => Pick(random, Cosmetics.HalloweenOutfits),
            Season.Winter => Pick(random, Cosmetics.WinterOutpostOutfits),
            _ => 0
        };

        /// <summary>
        /// A Superb / Legendary Reward Chest's seasonal outfit (winter: festive Flight Suits, P6569) at
        /// <see cref="SeasonalOutfitChance"/>, else 0. For RewardChests.Roll (not wired from here: that file belongs to
        /// the reward-chest work; cosmetics-695.md hand-off).
        /// </summary>
        public static uint ChestSeasonal(Random random)
            => Current(DateTime.UtcNow) == Season.Winter ? Pick(random, Cosmetics.WinterChestOutfits) : 0;

        private static uint Pick(Random random, uint[] items)
            => random.NextDouble() < SeasonalOutfitChance ? items[random.Next(items.Length)] : 0;
    }
}
