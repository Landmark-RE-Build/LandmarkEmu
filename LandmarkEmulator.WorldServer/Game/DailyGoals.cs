using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Daily goals (wiki "Daily Goals", Journal → Daily): straightforward, time-intensive goals that reset every 24 hours,
    /// each worth 30-45 Lumens; a daily with some progress survives the reset until it is completed, and the same goal
    /// can come up again any day.
    /// <para>
    /// The client knows 21 of them by name (texts 575703-575723 "Daily: Build Site Tourist" … "Daily: Lightning
    /// Wielder"); a live capture holds two records (669 Scalewind Hunt, 679 Shadow Wielder: achievement = 575040 + name
    /// text, objective = achievement + 588, required 25, 30 Lumens), and the rest are built from them (Achievements).
    /// Counts from the wiki where it gives them (Cavern Crawler 5 Lost Crystals, Shrewd Miner 16000, Lucky Logger 4000,
    /// On the Warpath 50); the hunts and wielders 25 as captured. A character gets <see cref="PerDay"/> a day, kept in
    /// Unlocks.DailyGoal as day × 100 + index.
    /// </para>
    /// </summary>
    public static class DailyGoals
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public sealed record Daily(uint Id, uint NameText, uint ObjectiveText, uint Required, uint Lumens);

        public const uint Tourist = 663, Critic = 664, CavernCrawler = 665, Warpath = 666, MonsterHunt = 667,
            BarrenHunt = 668, ScalewindHunt = 669, HighfallHunt = 670, NovaTechHunt = 671, EbonguardHunt = 672,
            StoneskinHunt = 673, CommonMiner = 674, ShrewdMiner = 675, LuckyLogger = 676, FireWielder = 677,
            WaterWielder = 678, ShadowWielder = 679, RadianceWielder = 680, AfflictionWielder = 681, EarthWielder = 682,
            LightningWielder = 683;

        public static readonly Daily[] All =
        {
            new(Tourist, 575703, 575727, 5, 30),           // Build Sites visited
            new(Critic, 575704, 575728, 5, 30),            // Build Site "Likes" given
            new(CavernCrawler, 575705, 575240, 5, 45),     // Lost Crystal reached
            new(Warpath, 575706, 575736, 50, 40),          // Enemies killed
            new(MonsterHunt, 575707, 575129, 25, 30),      // Monsters killed
            new(BarrenHunt, 575708, 575130, 25, 30),
            new(ScalewindHunt, 575709, 575131, 25, 30),
            new(HighfallHunt, 575710, 575132, 25, 30),
            new(NovaTechHunt, 575711, 575133, 25, 30),
            new(EbonguardHunt, 575712, 575134, 25, 30),
            new(StoneskinHunt, 575713, 575135, 25, 30),
            new(CommonMiner, 575714, 575270, 16000, 35),   // Resources mined (dirt, sand, stone, ice)
            new(ShrewdMiner, 575715, 575270, 16000, 40),   // Resources mined (ore, gems, uncommon stone)
            new(LuckyLogger, 575716, 575317, 4000, 40),    // wood harvested from trees
            new(FireWielder, 575717, 575158, 25, 30),
            new(WaterWielder, 575718, 575159, 25, 30),
            new(ShadowWielder, 575719, 575160, 25, 30),
            new(RadianceWielder, 575720, 575161, 25, 30),
            new(AfflictionWielder, 575721, 575162, 25, 30),
            new(EarthWielder, 575722, 575163, 25, 30),
            new(LightningWielder, 575723, 575164, 25, 30)
        };

        public const int PerDay = 3;
        private const uint First = Tourist;

        public static bool IsDaily(uint achievementId) => achievementId >= First && achievementId <= LightningWielder;

        private static uint Today => (uint)(DateTime.UtcNow - DateTime.UnixEpoch).TotalDays;

        private static readonly Dictionary<WorldSession, uint> assignedDay = new();
        private static readonly Dictionary<WorldSession, (uint Day, HashSet<ulong> Sites)> visited = new();
        private static readonly object sync = new();

        /// <summary>
        /// The character's Daily goals now: today's (topped up to <see cref="PerDay"/>, each new one active at 0), plus
        /// older ones still in progress; finished or untouched older ones go.
        /// </summary>
        public static HashSet<uint> Assign(WorldSession session)
        {
            ulong character = session.PlayerGuid;
            uint today = Today;
            var entries = Unlocks.Values(character, Unlocks.DailyGoal).Select(v => (Day: v / 100, Id: First + v % 100)).ToList();
            var keep = entries.Where(e => IsDaily(e.Id) && (e.Day == today
                || (!Achievements.IsComplete(character, e.Id) && Achievements.HasProgress(character, e.Id)))).ToList();
            var keepIds = keep.Select(e => e.Id).ToHashSet();
            int todays = keep.Count(e => e.Day == today);
            if (todays < PerDay)
            {
                var rng = new Random(Shared.StableHash.Combine((int)character, (int)(character >> 32), (int)today, 0x6461));
                foreach (Daily d in All.Where(d => !keepIds.Contains(d.Id)).OrderBy(_ => rng.Next()).Take(PerDay - todays))
                {
                    keep.Add((today, d.Id));
                    keepIds.Add(d.Id);
                    Achievements.ResetObjectives(character, d.Id, 1);
                }
            }
            foreach (var dropped in entries.Where(e => IsDaily(e.Id) && !keepIds.Contains(e.Id)))
                Achievements.ResetObjectives(character, dropped.Id, 0);
            if (!entries.Select(e => e.Day * 100 + (e.Id - First)).ToHashSet().SetEquals(keep.Select(e => e.Day * 100 + (e.Id - First))))
                Unlocks.Replace(session, Unlocks.DailyGoal, keep.Select(e => e.Day * 100 + (e.Id - First)));
            lock (sync)
                assignedDay[session] = today;
            log.Info($"{session.CharacterName}: daily goals {string.Join(", ", keepIds.OrderBy(i => i))}.");
            return keepIds;
        }

        /// <summary>Has the day turned since this player's dailies were given (their Journal needs the new ones)?</summary>
        public static bool NewDay(WorldSession session)
        {
            lock (sync)
                return assignedDay.TryGetValue(session, out uint day) && day != Today;
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                assignedDay.Remove(session);
                visited.Remove(session);
            }
        }

        private static void Count(WorldSession session, uint daily, uint amount = 1)
            => Achievements.Progress(session, daily + 588, amount);   // only counts while the goal is active

        // ---- What counts --------------------------------------------------------------------------------------------

        /// <summary>A kill: On the Warpath, the faction's hunt (or Monster Hunt), and the held weapon's affinity.</summary>
        public static void Killed(WorldSession killer, string creatureName)
        {
            Count(killer, Warpath);
            string n = creatureName ?? "";
            uint hunt = n.Contains("Barren") ? BarrenHunt : n.Contains("Scalewind") ? ScalewindHunt
                : n.Contains("Highfall") ? HighfallHunt : n.Contains("NovaTech") ? NovaTechHunt
                : n.Contains("Ebonguard") ? EbonguardHunt : n.Contains("Stoneskin") ? StoneskinHunt : MonsterHunt;
            Count(killer, hunt);
            uint wielder = HitEffects.HeldAffinity(killer) switch
            {
                "Fire" => FireWielder, "Water" => WaterWielder, "Shadow" => ShadowWielder, "Radiance" => RadianceWielder,
                "Affliction" => AfflictionWielder, "Physical" => EarthWielder, "Lightning" => LightningWielder, _ => 0
            };
            if (wielder != 0)
                Count(killer, wielder);
        }

        /// <summary>A harvest: wood for Lucky Logger, common ground for Common Miner, ore / gems / uncommon stone for Shrewd Miner.</summary>
        public static void Harvested(WorldSession session, bool wood, bool commonGround, uint count)
            => Count(session, wood ? LuckyLogger : commonGround ? CommonMiner : ShrewdMiner, count);

        /// <summary>A Lost Crystal used (Cavern Crawler).</summary>
        public static void CrystalReached(WorldSession session) => Count(session, CavernCrawler);

        /// <summary>A Build Site "Like" given (Build Site Critic).</summary>
        public static void LikeGiven(WorldSession session) => Count(session, Critic);

        /// <summary>Walking onto someone else's build site, once per site a day (Build Site Tourist).</summary>
        public static void Moved(WorldSession session, Vector3 position)
        {
            if (Claims.ClaimAt(session.ZoneId, position) is not ulong claim || Claims.OwnerOf(claim) == session.PlayerGuid)
                return;
            uint today = Today;
            lock (sync)
            {
                if (!visited.TryGetValue(session, out var v) || v.Day != today)
                    visited[session] = v = (today, new HashSet<ulong>());
                if (!v.Sites.Add(claim))
                    return;
            }
            Count(session, Tourist);
            Achievements.SiteVisited(session);
            log.Debug($"{session.CharacterName} visited build site {claim}.");
        }
    }
}
