using NLog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// Records gaps between movement updates and logs min/median/max every 10 s, to tell server-side jitter from
    /// client-side. Cheap enough to leave on: a timestamp per update and one log line per period.
    /// </summary>
    public class MovementTiming
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        private readonly string name;
        private readonly List<long> gaps = new();
        private long last = -1;
        private long periodStart = clock.ElapsedMilliseconds;

        public MovementTiming(string name)
        {
            this.name = name;
        }

        public void Mark(string owner)
        {
            long now = clock.ElapsedMilliseconds;
            lock (gaps)
            {
                if (last >= 0 && now - last < 2000)
                    gaps.Add(now - last);
                last = now;

                if (now - periodStart < 10000)
                    return;
                periodStart = now;
                if (gaps.Count < 5)
                {
                    gaps.Clear();
                    return;
                }

                gaps.Sort();
                log.Info($"Movement {name} {owner}: {gaps.Count + 1} updates, gap ms min {gaps[0]} median {gaps[gaps.Count / 2]} " +
                    $"p90 {gaps[gaps.Count * 9 / 10]} max {gaps[^1]}");
                gaps.Clear();
            }
        }
    }
}
