using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build sites left on an event island when the Events world switches islands (OPEN-LEADS L110, spec B3 in
    /// trace/0695/agent-leads4.md).
    /// <para>
    /// <b>Live</b> [W, developer posts]: forum 58077 (Domino, 21 Nov 2016): with that morning's restart the Halloween
    /// Party island left the Events world, and the sites still on it were expired that morning. The 2015 beta did the
    /// same with its Halloween island (forum 55925, hotfix notes 10 Dec 2015: the island would close the next week;
    /// nothing had to be done, but players could take their builds down early to reuse their claim flags and
    /// materials). So the island closed at a restart and the
    /// sites on it were expired.
    /// </para>
    /// <para>
    /// <b>Here</b>: the island open this run (<see cref="EventIsland"/>, chosen at start-up from the EventIsland setting) is
    /// remembered in the world database (world_prop_setting row propId 0, key "events_island_open", value = zone id, 0 =
    /// none open; prop guids are never 0). At start-up, when the remembered island differs from the one open now, that
    /// island has closed. With the setting EventIslandExpireOnClose (Docker EVENT_ISLAND_EXPIRE_ON_CLOSE, default false:
    /// it deletes players' builds, the server owner decides) its sites go through the normal expiry path on the first
    /// world tick that can reach the Environment Server (<see cref="Claims.Expire"/> → <see cref="SiteContents.Pack"/>:
    /// a Design saved, the terrain reverted, props and resources mailed to the owner with the Build Site Contents item),
    /// with an island letter instead of the upkeep one (<see cref="SiteUpkeep.MailIslandClosed"/>, wording ours [I]).
    /// Every expired site is logged. With the setting off the sites stay (as before) and are logged as left behind.
    /// </para>
    /// <para>
    /// <b>Inferred</b>: what the owner got back. 55925's "remove your builds early if you wish to use the claim flags or
    /// materials" may mean live's close returned nothing; this server's expiry rule (TASKS 6.39, SiteContents) returns the
    /// build, so no second loss model is made up for the islands [I]. Which island was open before the first run with
    /// this code is not known: that run only records, nothing expires.
    /// </para>
    /// <para>
    /// Dev command <c>eventexpire</c> lists the state and every site on the event islands, i.e. what would expire (dry
    /// run); <c>eventexpire confirm &lt;zone&gt;</c> expires the sites on a closed island now (refused for the open one).
    /// </para>
    /// </summary>
    public static partial class Landscapes
    {
        /// <summary>The world setting row that remembers the open island (no prop has guid 0).</summary>
        private const ulong EventStateProp = 0;
        private const string EventStateKey = "events_island_open";

        /// <summary>
        /// The islands as the developers' posts name them (58006 / 58077 "Halloween Party Island", "Winter Holiday Island";
        /// the third only by its zone name text 572678 "IsleOfJudgement"), for the letter.
        /// </summary>
        private static readonly Dictionary<uint, string> eventIslandTitles = new()
        {
            [460] = "Halloween Party Island",
            [461] = "Winter Holiday Island",
            [462] = "Isle of Judgement"
        };

        /// <summary>What the database said was open at the previous run (null = never recorded), for the dry run.</summary>
        private static uint? previousEventZone;
        /// <summary>The closed island whose sites expire on the next tick that reaches the Environment Server.</summary>
        private static uint? pendingEventExpiry;
        private static bool pendingEventWarned;

        private static bool ExpireOnClose => ConfigurationManager<WorldServerConfiguration>.Instance.Config?.EventIslandExpireOnClose == true;

        /// <summary>
        /// Start-up (end of <see cref="Load"/>, after <see cref="Claims.Load"/>): compares the island open now with the one
        /// remembered from the last run. A change with sites left on the closed island either queues their expiry
        /// (setting on; <see cref="UpdateEventExpiry"/>) or logs them (off). The new island is recorded at once, except
        /// while an expiry is queued: then after it, so a server stopped before the tick tries again next start.
        /// </summary>
        private static void StartEventExpiry()
        {
            uint open = EventIsland?.Id ?? 0;
            (bool read, uint? recorded) = ReadRecordedEventZone();
            if (!read)
                return;   // logged; nothing recorded or expired this run
            previousEventZone = recorded;
            if (recorded is not uint before)
            {
                log.Info($"Event island expiry: no island recorded yet (first run with this code): recording {open} as open, nothing expires.");
                RecordEventZone(open);
                return;
            }
            if (before == open)
                return;
            List<ClaimModel> left = eventIslandDefs.ContainsKey(before) ? Claims.On(before) : new List<ClaimModel>();
            if (left.Count == 0)
            {
                log.Info($"Event island {(before == 0 ? "none" : before.ToString())} → {open}: no build sites left on the closed island.");
                RecordEventZone(open);
                return;
            }
            if (!ExpireOnClose)
            {
                log.Warn($"Event island {before} closed (now {open}): {left.Count} build site(s) stay on it "
                    + "(EventIslandExpireOnClose off; dev command \"eventexpire\" lists them).");
                foreach (string line in SiteLines(left))
                    log.Warn($"  left on {before}: {line}");
                RecordEventZone(open);
                return;
            }
            pendingEventExpiry = before;
            log.Warn($"Event island {before} closed (now {open}): its {left.Count} build site(s) expire on the first world "
                + "tick (EventIslandExpireOnClose on).");
        }

        /// <summary>
        /// World tick: runs a queued island expiry once the Environment Server can be reached (a lapsed site's design is
        /// read from the terrain before it reverts, as <see cref="Claims.Update"/> waits too), then records the island open now.
        /// </summary>
        public static void UpdateEventExpiry()
        {
            if (pendingEventExpiry is not uint zone)
                return;
            if (!Terrain.EnvironmentServer.Instance.Available)
            {
                if (!pendingEventWarned)
                    log.Warn($"Event island {zone}: expiry waits for the Environment Server.");
                pendingEventWarned = true;
                return;
            }
            pendingEventExpiry = null;
            ExpireClosedIsland(zone, "closed at start-up");
            RecordEventZone(EventIsland?.Id ?? 0);
        }

        /// <summary>
        /// Expires every build site on an event island (the switch above, or the dev command): each is logged (id, name,
        /// owner, centre), then removed and packed as a lapsed site with the island letter. Returns how many.
        /// </summary>
        public static int ExpireClosedIsland(uint zone, string why)
        {
            List<ClaimModel> sites = Claims.On(zone);
            if (sites.Count == 0)
            {
                log.Info($"Event island {zone} ({why}): no build sites to expire.");
                return 0;
            }
            foreach (string line in SiteLines(sites))
                log.Warn($"Event island {zone} ({why}): expiring {line}");
            string island = eventIslandTitles.GetValueOrDefault(zone, "event island");
            Claims.Expire(sites, $"event island {zone} {why}", (owner, name) => SiteUpkeep.MailIslandClosed(owner, name, island));
            log.Warn($"Event island {zone} ({why}): {sites.Count} build site(s) expired; owners mailed.");
            return sites.Count;
        }

        /// <summary>
        /// The dev command "eventexpire" (no changes): the setting, the island remembered from the last run, the one open
        /// now, and every site on each event island with what happens to it.
        /// </summary>
        public static List<string> EventExpiryReport()
        {
            uint open = EventIsland?.Id ?? 0;
            var lines = new List<string>
            {
                $"EventIslandExpireOnClose {(ExpireOnClose ? "on" : "off")}; open now: {(open == 0 ? "none" : open.ToString())}; "
                    + $"open at the previous run: {(previousEventZone is uint p ? (p == 0 ? "none" : p.ToString()) : "not recorded")}"
                    + (pendingEventExpiry is uint q ? $"; expiry of {q} queued (waits for the Environment Server)" : "")
            };
            foreach (uint zone in eventIslandDefs.Keys.OrderBy(z => z))
            {
                List<ClaimModel> sites = Claims.On(zone);
                string state = zone == open
                    ? (ExpireOnClose ? "open: these expire at the first start with another island (or none) open"
                        : "open: these stay when it closes (setting off)")
                    : pendingEventExpiry == zone ? "closed: expiring now"
                    : $"closed: left here (\"eventexpire confirm {zone}\" expires them)";
                lines.Add($"Island {zone} {eventIslandTitles[zone]}: {sites.Count} site(s)" + (sites.Count > 0 ? $", {state}" : ""));
                lines.AddRange(SiteLines(sites).Select(l => "  " + l));
            }
            return lines;
        }

        /// <summary>The dev command "eventexpire confirm &lt;zone&gt;": a closed event island's sites now. Returns the reply.</summary>
        public static string ExpireIslandByHand(uint zone)
        {
            if (!eventIslandDefs.ContainsKey(zone))
                return $"{zone} is not an event island (460-462).";
            if (EventIsland?.Id == zone)
                return $"Island {zone} is open this run: its sites are not expired.";
            if (!Terrain.EnvironmentServer.Instance.Available)
                return "The Environment Server is unreachable: try again later.";
            if (pendingEventExpiry == zone)
                pendingEventExpiry = null;
            return $"Island {zone}: {ExpireClosedIsland(zone, "dev command")} site(s) expired.";
        }

        /// <summary>One line per site: id, name, owner, centre (local metres) and size.</summary>
        private static IEnumerable<string> SiteLines(List<ClaimModel> sites)
        {
            Dictionary<ulong, string> names = sites.Count == 0 ? new()
                : DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(sites.Select(c => c.OwnerGuid).Distinct());
            var origin = Terrain.TerrainStore.ZoneOrigin;
            foreach (ClaimModel c in sites)
            {
                double x = ((c.MinX + c.MaxX) / 2 - origin.X) / 10, y = ((c.MinY + c.MaxY) / 2 - origin.Y) / 10,
                    z = ((c.MinZ + c.MaxZ) / 2 - origin.Z) / 10;
                yield return $"site {c.Id} \"{c.Name}\" of {names.GetValueOrDefault(c.OwnerGuid, "?")} (0x{c.OwnerGuid:X}) at "
                    + $"({x:F0}, {y:F0}, {z:F0}), {(c.MaxX - c.MinX) / 10:F0} x {(c.MaxY - c.MinY) / 10:F0} x {(c.MaxZ - c.MinZ) / 10:F0} m";
            }
        }

        /// <summary>The island recorded as open (null = no record yet); Ok false when the database could not be read.</summary>
        private static (bool Ok, uint? Zone) ReadRecordedEventZone()
        {
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewWorldContext();
                WorldPropSettingModel row = context.WorldPropSetting.FirstOrDefault(s => s.PropId == EventStateProp && s.Key == EventStateKey);
                return (true, row == null ? null : (uint)row.Value);
            }
            catch (Exception e)
            {
                log.Error(e, "Event island expiry: could not read the open island of the last run; nothing is recorded or expired this run.");
                return (false, null);
            }
        }

        private static void RecordEventZone(uint zone)
        {
            try
            {
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                {
                    WorldPropSettingModel row = c.WorldPropSetting.FirstOrDefault(s => s.PropId == EventStateProp && s.Key == EventStateKey);
                    if (row == null)
                        c.WorldPropSetting.Add(new WorldPropSettingModel { PropId = EventStateProp, Key = EventStateKey, Value = zone });
                    else
                        row.Value = zone;
                }).Wait();
            }
            catch (Exception e)
            {
                log.Error(e, $"Event island expiry: could not record island {zone} as open.");
            }
        }
    }
}
