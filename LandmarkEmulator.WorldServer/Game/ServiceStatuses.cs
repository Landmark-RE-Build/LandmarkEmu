using LandmarkEmulator.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The latest <see cref="ServiceStatus"/> each linked service sent (gateway, social, world channel, commerce), for the
    /// admin monitor page (TASKS B.7). Filled from whichever link brought it; a status older than three intervals is shown
    /// as stale.
    /// </summary>
    public static class ServiceStatuses
    {
        private static readonly Dictionary<string, (DateTime At, List<(string Key, string Value)> Rows)> latest = new();
        private static readonly object sync = new();

        /// <summary>A link's ServicePacket: kept when it is a status (true), else left to the link's own handling.</summary>
        public static bool TryTake(ServicePacket packet)
        {
            if (!ServiceStatus.TryRead(packet, out string service, out List<(string, string)> rows))
                return false;
            lock (sync)
                latest[service] = (DateTime.UtcNow, rows);
            return true;
        }

        /// <summary>For the monitor: service, age in seconds, stale, rows.</summary>
        public static object Snapshot()
        {
            DateTime now = DateTime.UtcNow;
            lock (sync)
                return latest.OrderBy(s => s.Key).Select(s => new
                {
                    service = s.Key,
                    ageSeconds = (int)(now - s.Value.At).TotalSeconds,
                    stale = now - s.Value.At > ServiceStatus.Interval * 3,
                    rows = s.Value.Rows.Select(r => new { key = r.Key, value = r.Value }).ToList()
                }).ToList();
        }
    }
}
