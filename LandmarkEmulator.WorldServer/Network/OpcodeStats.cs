using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// Counts zone packets by their first four wire bytes (u16 base opcode, then the sub-opcode and the start of the
    /// body), received and sent, and merges the counts into opcode_stats.tsv beside the server once a minute. A research
    /// aid: re/tools/coverage.py reads the file to rank what clients really send in play against what the server answers.
    /// Movement updates are not zone packets and are not counted.
    /// </summary>
    public static class OpcodeStats
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<string, long> counts = new();
        private static readonly string path = Path.Combine(AppContext.BaseDirectory, "opcode_stats.tsv");
        private static readonly Timer flush = new(_ => Flush(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        public static void Received(ReadOnlySpan<byte> data) => Count("in", data);

        /// <summary>
        /// A client request that reached a handler's default branch (no case for its sub-opcode): counted as
        /// "unanswered, family, sub" so re/tools/coverage.py can list what the client really sent that nothing handles.
        /// </summary>
        public static void Unanswered(string family, long sub)
            => counts.AddOrUpdate($"unanswered\t{family}\t0x{sub:X2}", 1, (_, n) => n + 1);
        public static void Sent(ReadOnlySpan<byte> data) => Count("out", data);

        private static void Count(string direction, ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
                return;
            string key = direction + "\t" + Convert.ToHexString(data[..Math.Min(4, data.Length)]);
            counts.AddOrUpdate(key, 1, (_, n) => n + 1);
            // The admin monitor's packet table (/admin/monitor, the server-side analogue of the client's page 27
            // PACKET MONITOR): counts and bytes since start, never flushed. Bounded so odd first bytes can't grow it forever.
            if (!live.TryGetValue(key, out LiveStat stat))
            {
                if (live.Count >= MaxLiveKeys)
                    return;
                stat = live.GetOrAdd(key, _ => new LiveStat());
            }
            Interlocked.Increment(ref stat.Count);
            Interlocked.Add(ref stat.Bytes, data.Length);
            long worst;
            while (data.Length > (worst = Interlocked.Read(ref stat.Worst)) && Interlocked.CompareExchange(ref stat.Worst, data.Length, worst) != worst)
            {
            }
        }

        private sealed class LiveStat
        {
            public long Count, Bytes, Worst;
        }

        private const int MaxLiveKeys = 4096;
        private static readonly ConcurrentDictionary<string, LiveStat> live = new();
        private static readonly DateTime started = DateTime.UtcNow;

        /// <summary>One row of the admin monitor's packet table: direction ("in" / "out"), the first wire bytes (hex).</summary>
        public sealed record Row(string Direction, string Bytes, long Count, long TotalBytes, long WorstBytes);

        /// <summary>Since when the live counts run (the server start).</summary>
        public static DateTime Since => started;

        /// <summary>The live counts (admin monitor), heaviest first.</summary>
        public static List<Row> Snapshot() => live
            .Select(p =>
            {
                int tab = p.Key.IndexOf('\t');
                return new Row(p.Key[..tab], p.Key[(tab + 1)..], Interlocked.Read(ref p.Value.Count),
                    Interlocked.Read(ref p.Value.Bytes), Interlocked.Read(ref p.Value.Worst));
            })
            .OrderByDescending(r => r.TotalBytes).ToList();

        /// <summary>Adds the counts gathered since the last flush to the file (lines: direction, first bytes, count).</summary>
        public static void Flush()
        {
            if (counts.IsEmpty)
                return;
            try
            {
                lock (counts)
                {
                    var merged = new Dictionary<string, long>();
                    if (File.Exists(path))
                        foreach (string line in File.ReadAllLines(path))
                        {
                            int tab = line.LastIndexOf('\t');
                            if (tab > 0 && long.TryParse(line[(tab + 1)..], out long n))
                                merged[line[..tab]] = n;
                        }
                    foreach (string key in counts.Keys.ToList())
                        if (counts.TryRemove(key, out long n))
                            merged[key] = merged.GetValueOrDefault(key) + n;
                    File.WriteAllLines(path, merged.OrderBy(p => p.Key).Select(p => $"{p.Key}\t{p.Value}"));
                }
            }
            catch (Exception e)
            {
                log.Warn(e, "Could not write opcode stats.");
            }
        }
    }
}
