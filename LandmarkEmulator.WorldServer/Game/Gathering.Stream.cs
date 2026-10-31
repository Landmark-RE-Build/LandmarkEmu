using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Interest-range streaming of trees and plants (player report 2026-11-01: "the grass and trees don't load in the
    /// horizon"; research note trace/0695/horizon.md). A player holds the 64 m regions whose nearest edge lies within
    /// <see cref="StreamRange"/> (setting TreeStreamRange, Docker TREE_STREAM_RANGE, default 400 m) and loses those more
    /// than <see cref="StreamDropMargin"/> beyond it (480 m), as placed props (Props.Stream.cs).
    /// <para>
    /// <b>Why the trees stopped at 160-250 m [P].</b> Until 2026-11-01 the range was a fixed 160 m (dropped at 224 m), our
    /// own first choice (MODLOG 2026-10). The 0.695 client itself has no tree or prop draw distance short of that:
    /// <list type="bullet">
    /// <item>An actor is skipped only when its LOD metric passes the last &lt;Lod&gt; of its model's .adr, an entry with a
    /// distance and no fileName (parser FUN_143e0d9a0 → FUN_143da5d10 stores each distance; FUN_143d0b3d0 picks the LOD
    /// from the camera distance × actor +0x378 / (mean scale × model +0x4e8); FUN_143cffe20 draws nothing past the last
    /// mesh). Models.txt holds no LOD data.</item>
    /// <item>The cull distances of the trees we place: Birch_Medium02/03 (351/352) 440 / 571 m, Oaks_Medium01-03
    /// (1467-1469) 800 / 735 / 770 m, JaggedPine 806-926 m, GnarledPine_Large 1,200 m, DiamondPine 1,700 / 2,480 m,
    /// Lavastorm 518-985 m; the placement sets' palms 380 / 390 m, large banyans 2,500 m, small banyans never. The
    /// metric's view factor (about 1.1 in the third-person camera) is [I].</item>
    /// <item>ModelQuality 3 keeps the raw distances (×1.2 at 2, ×1.4 at 1: FUN_142d53730); Render Distance ≤ 0 (the
    /// default -1) becomes 99999 (FUN_143489560); the sky's far plane is 3,900-7,000 m (Sky_*.xml FadeRange Far).</item>
    /// </list>
    /// So the client draws every tree the server sends out to several hundred metres, and the bare far plain was the
    /// server's 160 m.
    /// </para>
    /// <para>
    /// <b>Why 400 m [I].</b> Live's server range is not known (no capture, no developer post found). In 2016 footage
    /// (Verlin, 7 Jun 2016) birches run to the horizon, the smallest visible ones about 250-450 m away and some among the
    /// buildings of neighbouring sites (at least 285 m off). 400 m is also the placed-prop range (the Render Distance
    /// slider's last bounded step, 120 + 2.81 × 99 %, FUN_1435cfd10), so trees and props end at the same line.
    /// </para>
    /// <para>
    /// <b>Bandwidth [P].</b> Each tree is one AddLightweightProp, 296 B on the zone layer (290 B body + 6 B opcode); the
    /// 0.695 client has no batched spawn the server could use instead (PropBase BulkCreate 0xED/0x0E has no client
    /// reader), and a dropped tree is one RemovePlayer (16 B). Measured with this generator (old tree lists, as the dev
    /// world runs), per login or re-zone (a re-zone deletes every entity, ClientBeginZoning FUN_142d1ccf0 case 8):
    /// landscape 464 58 trees / 17 KB at 160 m and 271 trees / 78 KB at 400 m; 406 183 / 53 KB and 827 / 239 KB (p90
    /// 350 KB). Walking costs about 2.2 times the 160 m bytes per km (464: 54 → 121 KB/km; 406: 155 → 344 KB/km, 2.75 KB/s
    /// at 8 m/s). With ProceduralPlacement on the Pioneer jungle 464 holds 2,242 trees within 400 m (648 KB). To keep the
    /// far ring off the reliable channel's back:
    /// <list type="bullet">
    /// <item>regions are sent nearest first, re-sorted every tick as the player moves;</item>
    /// <item>those within <see cref="ImmediateRange"/> (the old 160 m) go at once, as the whole set went before;</item>
    /// <item>beyond that at most <see cref="FarRegionsPerTick"/> regions per 1 s tick, and only while the tick (the 160 m
    /// regions included) has sent fewer than <see cref="StreamBudget"/> nodes (whole regions, so the last may go over);
    /// these caps are per player, so they bound one player's work, not the world thread's;</item>
    /// <item>a region not generated yet beyond 160 m also waits while all players together have spent this second's
    /// <see cref="RegionGenerationPerSecond"/> (25 ms) generating regions (the 160 m rings' new regions spend it too but
    /// never wait for it). Generation runs on the world thread under <see cref="sync"/> (<see cref="Region"/>), in the
    /// loop that also runs "network", movement and combat (HostedService.cs), at 0.02 ms a region with the tree lists and
    /// 0.4-0.9 ms with the placement sets (ProceduralPlacement; trace/0695/horizon.md section 5). Without it, N players
    /// arriving after a restart (empty region cache) on spread-out sites would cost N × up to 32 far regions a second;
    /// with it the far ring adds at most 25 ms a second (plus the region that crosses it) to the 160 m rings' own cost,
    /// which is the cost before 2026-11-01. Regions already cached (another player was there) cost nothing and never
    /// wait;</item>
    /// <item>nothing streams while <see cref="WorldSession.StreamsOnHold"/> (loading screen, arrival, re-zone), as
    /// before.</item>
    /// </list>
    /// The client turns only its nearest 50 lightweight entities full (FUN_142ee8540, trace/0695/full-npc-695.md), so far
    /// trees add no FullCharacterDataRequest / LightweightToFullProp traffic of their own [I: that the cap covers props].
    /// </para>
    /// <para>
    /// <b>Grass is not streamed [P].</b> The client scatters grass itself, per terrain triangle on cell levels 2-3 only
    /// (FUN_1436c17a0 → FUN_1436c7e90), and draws it only within its FloraManager radius, 64 m by default (FUN_14373d210
    /// +0x2250; the cut in FUN_1437a7c90). FloraQuality changes density only (0.25 / 0.5 / 1.0, FUN_14349af30); the
    /// radius changes only with the admin-gated console command "/flora Radius n" (FUN_1433a5710 → FUN_14349f530). No
    /// packet sets it, so grass ending about 64 m out is the client's own behaviour, as on live (docs/terrain.md
    /// "Grass (flora)").
    /// </para>
    /// </summary>
    public static partial class Gathering
    {
        /// <summary>Default of the setting TreeStreamRange (metres; see the class comment).</summary>
        public const float DefaultStreamRange = 400f;

        /// <summary>Lowest and highest TreeStreamRange taken (one region; 1,000 m ≈ 1.2 MB per login on 406).</summary>
        public const float MinStreamRange = 64f, MaxStreamRange = 1000f;

        /// <summary>Regions are dropped only this far beyond the send range (hysteresis: 400 → 480 m), as placed props.</summary>
        public const float StreamDropMargin = 80f;

        /// <summary>
        /// Regions this close (or within the range, if smaller) are sent at once: the 160 m the whole set used before
        /// 2026-11-01, so what a player stands among still comes in one go when a loading screen drops.
        /// </summary>
        public const float ImmediateRange = 160f;

        /// <summary>
        /// Most nodes (trees, plants, decoration) sent to one player per tick, counting the regions within
        /// <see cref="ImmediateRange"/> (which always go whole): regions beyond it are taken only while the tick is under it.
        /// </summary>
        internal static int StreamBudget = 200;

        /// <summary>
        /// Most regions beyond <see cref="ImmediateRange"/> taken per tick for one player (the generation work across players
        /// is bounded by <see cref="RegionGenerationPerSecond"/>).
        /// </summary>
        internal static int FarRegionsPerTick = 32;

        /// <summary>
        /// World-thread time all players together may spend per second generating regions (cache misses in
        /// <see cref="StreamRegions"/>). Every generated region spends it; only those beyond <see cref="ImmediateRange"/>
        /// wait for it. 25 ms: about 1,250 regions a second with the tree lists (not reached in practice: one player's whole
        /// 400 m ring is about 150 regions), about 30-65 with the placement sets (0.38-0.86 ms a region).
        /// </summary>
        internal static TimeSpan RegionGenerationPerSecond = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// Stopwatch ticks of this second's <see cref="RegionGenerationPerSecond"/> not spent yet; may go below zero (the
        /// 160 m rings never wait, and the region that crosses zero is finished). Unlimited until the first
        /// <see cref="RefillRegionGeneration"/>, so a direct <see cref="StreamRegions"/> call has only the per-player caps.
        /// Under <see cref="sync"/>.
        /// </summary>
        private static long generationLeft = long.MaxValue;

        private static DateTime nextGenerationRefill;

        /// <summary>
        /// Called by <see cref="Update"/> before the players' stream ticks: starts a new second of
        /// <see cref="RegionGenerationPerSecond"/> once the last one is up. What a second overspent is not carried over (the
        /// 160 m rings that overspent it are the old cost, not a debt). A clock set back more than a second refills at once.
        /// </summary>
        private static void RefillRegionGeneration(DateTime now)
        {
            lock (sync)
            {
                if (now < nextGenerationRefill && nextGenerationRefill - now <= UpdateInterval)
                    return;
                nextGenerationRefill = now + UpdateInterval;
                generationLeft = (long)(RegionGenerationPerSecond.TotalSeconds * Stopwatch.Frequency);
            }
        }

        private static float? streamRangeOverride;

        /// <summary>
        /// Metres around a player whose regions' trees and plants are sent: the dev command "treestream" until the restart,
        /// else the setting TreeStreamRange (default <see cref="DefaultStreamRange"/>); clamped to
        /// <see cref="MinStreamRange"/>-<see cref="MaxStreamRange"/>.
        /// </summary>
        public static float StreamRange
        {
            get
            {
                float range = streamRangeOverride
                    ?? ConfigurationManager<WorldServerConfiguration>.Instance.Config?.TreeStreamRange ?? DefaultStreamRange;
                return float.IsFinite(range) ? Math.Clamp(range, MinStreamRange, MaxStreamRange) : DefaultStreamRange;
            }
            set => streamRangeOverride = value;
        }

        /// <summary>Depth below the ground (m) at which surface objects are dropped, and above which they return.</summary>
        private const float UndergroundDropDepth = 60f, UndergroundShowDepth = 40f;

        /// <summary>
        /// One player's stream tick (<see cref="Update"/>, at most every <see cref="UpdateInterval"/>): the regions to add
        /// and drop (<see cref="StreamRegions"/>), removals first.
        /// </summary>
        private static void Stream(WorldSession session)
        {
            uint zone = session.ZoneId;
            if (DevCommands.NoStreaming || !TerrainHeights.Has(zone))
                return;   // no baked heights for this landscape: nothing can be placed on its ground
            if (session.StreamsOnHold)
                return;
            // Min / max model: the census runs on the first visit since the start (seconds, in the background); only a
            // landscape's very first population waits for it, after a restart the saved free spots apply at once.
            if (AwaitCensus(zone))
                return;
            Vector3 position = session.Position;
            var spawns = new List<PlayerUpdateAddLightweightProp>();
            var removals = new List<ulong>();
            lock (sync)
            {
                if (!sent.TryGetValue(session, out var have))
                    sent[session] = have = new HashSet<long>();
                // Surface objects aren't sent to players deep underground (caves), and are dropped once they get
                // there; the two depths differ so a player near the threshold doesn't flicker between them.
                float depth = TerrainHeights.DepthBelowGround(position, zone);
                StreamRegions(zone, position, StreamRange, depth > UndergroundDropDepth, depth < UndergroundShowDepth, have,
                    spawns, removals);
            }
            foreach (ulong guid in removals)
                session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
            foreach (PlayerUpdateAddLightweightProp spawn in spawns)
                session.EnqueueMessage(spawn);
        }

        /// <summary>
        /// The regions a player gains and loses this tick. Drops every held region of another landscape (the client lost
        /// those entities with its re-zone, so nothing is sent for them), all of them with <paramref name="hideSurface"/>
        /// (deep underground), and those beyond <paramref name="range"/> + <see cref="StreamDropMargin"/>, adding their
        /// standing nodes to <paramref name="removals"/>. Then, with <paramref name="showSurface"/>, adds the regions within
        /// <paramref name="range"/> not held yet, nearest first: all within <see cref="ImmediateRange"/>, beyond it at most
        /// <see cref="FarRegionsPerTick"/> regions while the tick's nodes are under <see cref="StreamBudget"/> and, for a
        /// region not generated yet, while the players' shared <see cref="generationLeft"/> lasts; their standing nodes go to
        /// <paramref name="spawns"/>. Every region generated here spends <see cref="generationLeft"/>. Caller holds
        /// <see cref="sync"/>.
        /// </summary>
        private static void StreamRegions(uint zone, Vector3 position, float range, bool hideSurface, bool showSurface,
            HashSet<long> have, List<PlayerUpdateAddLightweightProp> spawns, List<ulong> removals)
        {
            float drop = range + StreamDropMargin, immediate = MathF.Min(range, ImmediateRange);
            foreach (long key in have.ToList())
            {
                (uint keyZone, int rx, int rz) = Unpack(key);
                if (keyZone == zone && !hideSurface && RegionDistance(rx, rz, position) <= drop)
                    continue;
                have.Remove(key);
                if (keyZone == zone)
                    removals.AddRange(Region(keyZone, rx, rz).Where(n => n.Standing).Select(n => n.Guid));
            }
            if (!showSurface)
                return;
            var wanted = new List<(long Key, int Rx, int Rz, float Distance)>();
            int r0x = (int)MathF.Floor((position.X - range) / RegionSize), r1x = (int)MathF.Floor((position.X + range) / RegionSize);
            int r0z = (int)MathF.Floor((position.Z - range) / RegionSize), r1z = (int)MathF.Floor((position.Z + range) / RegionSize);
            for (int rz = r0z; rz <= r1z; rz++)
                for (int rx = r0x; rx <= r1x; rx++)
                {
                    long key = Pack(zone, rx, rz);
                    if (have.Contains(key))
                        continue;
                    float distance = RegionDistance(rx, rz, position);
                    if (distance <= range)
                        wanted.Add((key, rx, rz, distance));
                }
            wanted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int start = spawns.Count, farRegions = 0;
            foreach ((long key, int rx, int rz, float distance) in wanted)
            {
                bool far = distance > immediate;
                if (far && (farRegions >= FarRegionsPerTick || spawns.Count - start >= StreamBudget))
                    break;   // the rest on the next ticks, nearest first again from wherever the player is then
                bool generate = !regions.ContainsKey(key);
                if (far && generate && generationLeft <= 0)
                    break;   // this second's generation is spent (by any player): the rest next tick, nearest first
                long started = generate ? Stopwatch.GetTimestamp() : 0;
                Node[] nodes = Region(zone, rx, rz);
                if (generate)
                    generationLeft -= Stopwatch.GetTimestamp() - started;
                have.Add(key);
                spawns.AddRange(nodes.Where(n => n.Standing).Select(n => n.Spawn));
                if (far)
                    farRegions++;
            }
        }

        /// <summary>Dev "treestream": a player's tree stream (range, regions held and still to come, nodes held) for the log.</summary>
        public static string StreamState(WorldSession session)
        {
            float range = StreamRange;
            uint zone = session.ZoneId;
            Vector3 at = session.Position;
            lock (sync)
            {
                if (!sent.TryGetValue(session, out var have))
                    return $"range {range:0} m (drop {range + StreamDropMargin:0} m), nothing held (re-zoning, on hold or not in the world)";
                int held = 0, nodes = 0, toCome = 0;
                foreach (long key in have)
                {
                    (uint keyZone, int rx, int rz) = Unpack(key);
                    if (keyZone != zone)
                        continue;
                    held++;
                    if (regions.TryGetValue(key, out Node[] cached))
                        nodes += cached.Count(n => n.Standing);
                }
                int r0x = (int)MathF.Floor((at.X - range) / RegionSize), r1x = (int)MathF.Floor((at.X + range) / RegionSize);
                int r0z = (int)MathF.Floor((at.Z - range) / RegionSize), r1z = (int)MathF.Floor((at.Z + range) / RegionSize);
                for (int rz = r0z; rz <= r1z; rz++)
                    for (int rx = r0x; rx <= r1x; rx++)
                        if (!have.Contains(Pack(zone, rx, rz)) && RegionDistance(rx, rz, at) <= range)
                            toCome++;
                return $"range {range:0} m (drop {range + StreamDropMargin:0} m, at once within {MathF.Min(range, ImmediateRange):0} m), "
                    + $"{held} region(s) held with {nodes} standing node(s), {toCome} region(s) in range still to come"
                    + $"{(session.StreamsOnHold ? " (stream on hold)" : "")}";
            }
        }
    }
}
