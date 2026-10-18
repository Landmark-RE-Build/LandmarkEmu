using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Live's respawn model for trees and plants: a minimum and a maximum per type and landscape, respawns elsewhere
    /// (OPEN-LEADS L2 / L113, trace/0695/agent-leads3-b.md "L2 / L113", trace/0695/procedural-placement-695.md §4; the
    /// user's choice of 2026-10-17). Setting <c>ProceduralRespawn</c> (Docker PROCEDURAL_RESPAWN): <c>minmax</c> (default)
    /// or <c>inplace</c> (the older model: a gathered node regrows where it stood after its respawn minutes).
    /// <para>
    /// The source [P] is Domino's post "A note on respawn issues in Landmark" (forum 55352 #1, 19 Jun 2015;
    /// design/forum/all/2015-06-19-55352-a-note-on-respawn-issues-in-landmark.md): resources are spread at random over
    /// an island by rules set per layer and biome for every kind of spawn (each type of tree, plant, NPC and so on);
    /// there are many more possible locations than spawns; at server start a number between the minimum and the maximum
    /// is placed at random among the allowed locations; a cut tree changes nothing while its type is above the minimum;
    /// below it another one of that type grows back within about 20 minutes, at a random allowed spot in the same biome
    /// of the island and never where the old one stood. In #18 the same developer: spawn points under claims are no
    /// longer used, and the spawn moves to another point that is still allowed.
    /// The server-only tables' schema agrees [P]: ProceduralPlacementEntityListEntities has MIN_SPAWNS_PER_ZONE /
    /// MAX_SPAWNS_PER_ZONE per entity of an entity list (admin page 28 shows them per layer and biome, AdminMonitor).
    /// </para>
    /// <para>
    /// How it is built here:
    /// <list type="bullet">
    /// <item>The unit (a <b>pool</b>) is one landscape × one placement set × one set entity [I]: live's minimum / maximum
    /// were per zone and per entity of an entity list (the schema above); our entity lists are the placement sets (as page
    /// 28 already shows them), and a set has exactly one BIOME_ID, so "per biome" holds too. Keeping the set (rather than
    /// pooling one prop across sets) means a respawn always lands on a spot made by the same set's rules — biome, altitude,
    /// slope, soil, mask, spacing, encroachment — and with that spot's own scale / heading, so trunks never overlap. The
    /// old tree lists (Volcanic, Deciduous, shore, or everything with ProceduralPlacement off) pool per landscape × biome
    /// under the spot × model.</item>
    /// <item>The candidate spots ("possible locations") are every node the placement makes today on the whole
    /// landscape: a background <b>census</b> runs the same deterministic generator over every 64 m region once per
    /// landscape after a restart (first visit; a few seconds), off the world thread. <b>max</b> = a pool's candidate count
    /// (= today's placed count, the user's rule), <b>min</b> = round(PROCEDURAL_RESPAWN_MIN_SHARE × max), at least 1
    /// (default share 0.67 [I]: the post's 1000 / 1500, numbers the developer called made up). Decoration (rocks, bushes:
    /// never gathered) always stands.</item>
    /// <item>First population [P]: a random count in [min, max] of each pool's spots stands, chosen at random; the other
    /// spots are free. Saved (gathered_nodes.txt), so a restart neither re-rolls nor double-spawns — unlike live, whose
    /// restarts reset every spawn (the user's rule). Re-rolled only when the generator changes (settings
    /// ProceduralPlacement / Decor / Masks).</item>
    /// <item>A felled tree's spot becomes free. While its pool is below min, one respawn per missing node is queued after
    /// the set's MIN..MAX_RESPAWN_MINUTES (12-18 on most sets, the post's "about 20 minutes"; the old lists' fixed 30
    /// min) and
    /// then stands at a random free spot of the pool, re-checked then against build sites (Claims), the Spire's
    /// platform, graveyards' "Prevent Spawning" areas and players standing there. A spot felled within the last hour is
    /// not used while another free spot is (ours: "not ... at the exact same place"); a pool with no usable spot tries
    /// again a minute later. A pool of a single spot therefore regrows in place.</item>
    /// <item>Outposts need no check: they are stamped in the Chaos Caverns, hundreds of metres under the surface
    /// (Outposts.cs, OutpostSites.cs: caverns below MinTravelDepth −650 m vs ground ~1000 m) [P].</item>
    /// <item>Bandwidth: unchanged — a respawned node is one AddLightweightProp to the players who have its 64 m region
    /// (sent within 160 m), a felled one a RemovePlayer to the same; nothing per tick.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Known limits: a build site placed after the census leaves its candidates in the pool (counted, never used for a
    /// respawn while covered; a tree already standing under it stays, as before); a site removed after the census adds
    /// no candidates until the next restart.
    /// </para>
    /// </summary>
    public static partial class Gathering
    {
        /// <summary>What happens after a node is gathered (setting ProceduralRespawn).</summary>
        public enum RespawnModel
        {
            /// <summary>Live's: min / max per pool, respawns at another free spot (default).</summary>
            MinMax,
            /// <summary>The older model: the node regrows where it stood after its respawn minutes.</summary>
            InPlace
        }

        /// <summary>Setting "ProceduralRespawn" (minmax | inplace; default minmax). Docker: PROCEDURAL_RESPAWN.</summary>
        public static RespawnModel Respawn { get; private set; } = RespawnModel.MinMax;

        /// <summary>Setting "ProceduralRespawnMinShare" (0-1, default 0.67): a pool's minimum as a share of its maximum.
        /// Docker: PROCEDURAL_RESPAWN_MIN_SHARE.</summary>
        public static double MinShare { get; private set; } = 0.67;

        /// <summary>A spot felled this recently is not used for a respawn (ours; the post: never where it was cut).</summary>
        private static readonly TimeSpan SameSpotCooldown = TimeSpan.FromMinutes(60);
        /// <summary>A queued respawn that found no usable spot tries again after this.</summary>
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
        /// <summary>Metres kept between a respawning trunk (its footprint) and any player (ours).</summary>
        private const float PlayerClearance = 2f;
        /// <summary>The census covers regions −64..63 on both axes: ±4096 m, an 8 km landscape (Pack's 16-bit fields).</summary>
        private const int CensusReach = 64;

        private readonly record struct PoolKey(uint Zone, int Set, uint Entity, int Biome);

        /// <summary>One pool: the candidate spots (node keys) of one landscape × set × entity (or × biome × model).</summary>
        private sealed class Pool
        {
            public PoolKey Key;
            public NodeType Type;
            public ProceduralPlacement.PlacementSet Set;
            public ProceduralPlacement.SetEntity Entity;
            public uint Model;
            public int Biome;
            public readonly List<ulong> Keys = new();
            public readonly HashSet<ulong> Members = new();
            public int Max, Min, Standing, Pending;
            /// <summary>Decoration is never gathered: always all standing.</summary>
            public bool Fixed => Type == NodeType.Decor;
        }

        private sealed class Census
        {
            public uint Zone;
            public string Generator;
            public readonly Dictionary<PoolKey, Pool> Pools = new();
            public int Nodes;
            public TimeSpan Took;
        }

        /// <summary>Candidate spots with nothing on them now (min / max model; saved as "E key").</summary>
        private static readonly HashSet<ulong> empty = new();
        /// <summary>Landscapes whose first population was rolled, with the generator it was rolled for ("Z zone generator").</summary>
        private static readonly Dictionary<uint, string> rolled = new();
        private static readonly Dictionary<uint, Census> censuses = new();
        private static readonly HashSet<uint> censusRequested = new();
        private static readonly BlockingCollection<(uint Zone, string Generator)> censusQueue = new();
        private static readonly ConcurrentQueue<Census> censusDone = new();
        private static Thread censusWorker;
        private static readonly List<(Pool Pool, DateTime At)> respawns = new();
        private static readonly Dictionary<ulong, DateTime> recentlyFelled = new();
        private static DateTime nextRespawnCheck, nextFelledPrune;

        /// <summary>Reads ProceduralRespawn / ProceduralRespawnMinShare (WorldServer.json or the environment).</summary>
        private static void LoadRespawnSettings(IConfiguration config)
        {
            string model = config?.GetValue("ProceduralRespawn", "minmax") ?? "minmax";
            Respawn = model.Trim().Equals("inplace", StringComparison.OrdinalIgnoreCase) ? RespawnModel.InPlace : RespawnModel.MinMax;
            if (!model.Trim().Equals("inplace", StringComparison.OrdinalIgnoreCase) && !model.Trim().Equals("minmax", StringComparison.OrdinalIgnoreCase))
                log.Warn($"ProceduralRespawn \"{model}\" is not minmax or inplace; minmax is used.");
            double share = 0.67;
            try
            {
                share = config?.GetValue("ProceduralRespawnMinShare", 0.67) ?? 0.67;
            }
            catch (InvalidOperationException)
            {
                log.Warn("ProceduralRespawnMinShare is not a number; 0.67 is used.");
            }
            MinShare = double.IsFinite(share) ? Math.Clamp(share, 0.0, 1.0) : 0.67;
            log.Info(Respawn == RespawnModel.MinMax
                ? $"Gathering respawn: min / max per type and landscape (min = {MinShare:0.##} × max), respawns elsewhere."
                : "Gathering respawn: in place.");
        }

        /// <summary>A saved min / max line ("E key" or "Z zone generator"); false for any other line.</summary>
        private static bool LoadRespawnLine(string[] f)
        {
            if (f.Length == 2 && f[0] == "E" && ulong.TryParse(f[1], out ulong key))
            {
                if (Respawn == RespawnModel.MinMax)
                    empty.Add(key);
                return true;
            }
            if (f.Length == 3 && f[0] == "Z" && uint.TryParse(f[1], out uint zone))
            {
                if (Respawn == RespawnModel.MinMax)
                    rolled[zone] = f[2];
                return true;
            }
            return false;
        }

        /// <summary>The min / max state as save-file lines (caller holds <see cref="sync"/>).</summary>
        private static IEnumerable<string> RespawnLines()
        {
            if (Respawn != RespawnModel.MinMax)
                return Array.Empty<string>();
            return rolled.Select(r => $"Z {r.Key} {r.Value}").Concat(empty.Select(k => $"E {k}")).ToList();
        }

        /// <summary>
        /// Which generator a landscape's spots come from: the placement sets (with decoration / masks, which change where
        /// trees go: disabled kinds take no room) or the old lists. A saved population rolled for another one is re-rolled.
        /// </summary>
        private static string Generator => ProceduralPlacement.Enabled && ProceduralPlacement.Loaded
            ? "sets" + (ProceduralPlacement.Decor ? "+decor" : "") + (ProceduralPlacement.Masks ? "+masks" : "")
            : "lists";

        /// <summary>The state a freshly generated node takes from what is saved (caller holds <see cref="sync"/>).</summary>
        private static DateTime SavedState(Node node)
        {
            if (Respawn == RespawnModel.InPlace)
                return gathered.GetValueOrDefault(node.Key);
            return node.Type != NodeType.Decor && empty.Contains(node.Key) ? DateTime.MaxValue : default;
        }

        private static PoolKey PoolKeyOf(Node node) => node.Set != null
            ? new PoolKey(node.Zone, node.Set.Id, node.Entity.Id, 0)
            : new PoolKey(node.Zone, node.Type == NodeType.Plant ? -2 : -1, node.Spawn.ModelId, node.Biome);

        private static Pool PoolOf(Node node) =>
            censuses.TryGetValue(node.Zone, out Census c) && c.Pools.TryGetValue(PoolKeyOf(node), out Pool pool) ? pool : null;

        private static uint ZoneOfKey(ulong key) => Unpack((long)(key >> 8)).Item1;

        /// <summary>A pool's minimum: round(share × max), at least 1 while it has spots (a share of 0: no respawns).</summary>
        private static int MinFor(int max) =>
            max <= 0 || MinShare <= 0 ? 0 : Math.Clamp((int)Math.Round(MinShare * max, MidpointRounding.AwayFromZero), 1, max);

        /// <summary>How long until a queued respawn: the set's MIN..MAX_RESPAWN_MINUTES, the old lists' fixed times.</summary>
        private static TimeSpan RespawnDelay(Pool pool) =>
            pool.Set?.Respawn(random) ?? (pool.Type == NodeType.Plant ? PlantRegrowTime : TreeRegrowTime);

        /// <summary>
        /// May the landscape's nodes be shown and gathered? Always under the regrow-in-place model; under min / max once
        /// its census is in, or at once when its population was already rolled for this generator (a restart): the saved
        /// free spots are then applied as regions are generated (<see cref="SavedState"/>), and a tree felled before the
        /// census is in is counted by it (its spot is free) and gets its respawn queued then (<see cref="Install"/>).
        /// Only a landscape's very first population waits. Caller may hold <see cref="sync"/>.
        /// </summary>
        private static bool NodesReady(uint zone)
        {
            if (Respawn != RespawnModel.MinMax)
                return true;
            lock (sync)
                return censuses.ContainsKey(zone) || rolled.TryGetValue(zone, out string generator) && generator == Generator;
        }

        /// <summary>
        /// Queues the landscape's census on the first call (min / max model); true while its nodes must wait for it
        /// (<see cref="NodesReady"/>). World thread. The Spire's site is made first, so its platform is left out of the
        /// candidates.
        /// </summary>
        private static bool AwaitCensus(uint zone)
        {
            if (Respawn != RespawnModel.MinMax)
                return false;
            lock (sync)
            {
                if (censuses.ContainsKey(zone))
                    return false;
                if (!censusRequested.Add(zone))
                    return !NodesReady(zone);
            }
            Spire.Prepare(zone);
            lock (sync)
            {
                if (censusWorker == null)
                {
                    censusWorker = new Thread(CensusWork) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "gathering census" };
                    censusWorker.Start();
                }
            }
            censusQueue.Add((zone, Generator));
            bool wait = !NodesReady(zone);
            log.Info($"Gathering: census of landscape {zone} queued" + (wait ? " (its first population: trees show when it is done)." : "."));
            return wait;
        }

        private static void CensusWork()
        {
            foreach ((uint zone, string generator) in censusQueue.GetConsumingEnumerable())
            {
                try
                {
                    censusDone.Enqueue(RunCensus(zone, generator));
                }
                catch (Exception e)
                {
                    log.Error(e, $"Gathering census of landscape {zone} failed; it is tried again on the next visit.");
                    lock (sync)
                        censusRequested.Remove(zone);
                }
            }
        }

        /// <summary>
        /// Every node the generator makes on the landscape, sorted into pools. Census thread: <see cref="Generate"/> reads
        /// only thread-safe state (height / biome grids, placement tables, masks, claims, Spire, graveyards; all locked or
        /// immutable) and touches none of Gathering's.
        /// </summary>
        private static Census RunCensus(uint zone, string generator)
        {
            var census = new Census { Zone = zone, Generator = generator };
            var watch = Stopwatch.StartNew();
            for (int rz = -CensusReach; rz < CensusReach; rz++)
                for (int rx = -CensusReach; rx < CensusReach; rx++)
                    foreach (Node node in Generate(zone, rx, rz, census: true))
                    {
                        PoolKey key = PoolKeyOf(node);
                        if (!census.Pools.TryGetValue(key, out Pool pool))
                            census.Pools[key] = pool = new Pool
                            {
                                Key = key, Type = node.Type, Set = node.Set, Entity = node.Entity, Model = node.Spawn.ModelId, Biome = node.Biome
                            };
                        if (pool.Members.Add(node.Key))
                            pool.Keys.Add(node.Key);
                        census.Nodes++;
                    }
            census.Took = watch.Elapsed;
            return census;
        }

        /// <summary>
        /// Takes a finished census in (world thread): min / max per pool, the first population when none is saved (or it
        /// was rolled for another generator), the state of regions already generated, and respawns for pools below min.
        /// </summary>
        private static void Install(Census census, DateTime now)
        {
            lock (sync)
            {
                uint zone = census.Zone;
                bool roll = !rolled.TryGetValue(zone, out string had) || had != census.Generator;
                var candidates = new HashSet<ulong>(census.Pools.Values.SelectMany(p => p.Keys));
                // Saved free spots that are no candidate any more (or all of them, when re-rolling) are dropped.
                empty.RemoveWhere(k => ZoneOfKey(k) == zone && (roll || !candidates.Contains(k)));
                int standing = 0, free = 0;
                foreach (Pool pool in census.Pools.Values)
                {
                    pool.Max = pool.Keys.Count;
                    pool.Min = pool.Fixed ? pool.Max : MinFor(pool.Max);
                    if (roll && !pool.Fixed)
                    {
                        // "somewhere between 1000-1500 curved palm trees will be randomly distributed" [P]
                        int stand = random.Next(pool.Min, pool.Max + 1);
                        ulong[] order = pool.Keys.OrderBy(_ => random.Next()).ToArray();
                        for (int i = stand; i < order.Length; i++)
                            empty.Add(order[i]);
                    }
                    pool.Standing = pool.Fixed ? pool.Max : pool.Keys.Count(k => !empty.Contains(k));
                    standing += pool.Standing;
                    free += pool.Max - pool.Standing;
                    for (int missing = pool.Min - pool.Standing - pool.Pending; missing > 0; missing--)
                    {
                        pool.Pending++;
                        respawns.Add((pool, now + RespawnDelay(pool)));
                    }
                }
                rolled[zone] = census.Generator;
                censuses[zone] = census;
                dirty = true;
                // Regions generated before the census (arrival checks, targeting) take the state now.
                foreach ((long key, Node[] nodes) in regions)
                    if (Unpack(key).Item1 == zone)
                        foreach (Node node in nodes)
                            node.RegrowsAt = SavedState(node);
                log.Info($"Gathering census of landscape {zone} ({census.Generator}): {census.Nodes} spots in {census.Pools.Count} pools "
                    + $"in {census.Took.TotalSeconds:0.0} s; {(roll ? "first population rolled" : "saved state applied")}: {standing} standing, "
                    + $"{free} free, {census.Pools.Values.Sum(p => p.Pending)} respawn(s) queued.");
            }
        }

        /// <summary>A node was felled or picked (caller holds <see cref="sync"/>): its spot is free; below min, a respawn is queued.</summary>
        private static void Felled(Node node, DateTime now)
        {
            node.RegrowsAt = DateTime.MaxValue;
            empty.Add(node.Key);
            recentlyFelled[node.Key] = now;
            dirty = true;
            if (PoolOf(node) is not Pool pool || !pool.Members.Contains(node.Key))
                return;   // not a census candidate (a build site removed since): its spot just stays free
            pool.Standing--;
            if (pool.Standing + pool.Pending < pool.Min)
            {
                pool.Pending++;
                respawns.Add((pool, now + RespawnDelay(pool)));
            }
        }

        /// <summary>
        /// Min / max model, every tick: takes finished censuses in; once a second, makes queued respawns that are due
        /// stand at a free spot and shows them to the players who have that region.
        /// </summary>
        private static void RespawnUpdate(DateTime now)
        {
            if (Respawn != RespawnModel.MinMax)
                return;
            while (censusDone.TryDequeue(out Census census))
                Install(census, now);
            if (now < nextRespawnCheck)
                return;
            nextRespawnCheck = now + UpdateInterval;
            // Players' positions before the lock (PlayerManager has its own lock).
            List<WorldSession> online = PlayerManager.Snapshot();
            var players = online.Select(s => (s.ZoneId, s.Position)).ToList();
            var shown = new List<Node>();
            lock (sync)
            {
                if (now >= nextFelledPrune)
                {
                    nextFelledPrune = now.AddMinutes(1);
                    foreach (ulong key in recentlyFelled.Where(f => now - f.Value >= SameSpotCooldown).Select(f => f.Key).ToList())
                        recentlyFelled.Remove(key);
                }
                for (int i = respawns.Count - 1; i >= 0; i--)
                {
                    (Pool pool, DateTime at) = respawns[i];
                    if (at > now)
                        continue;
                    if (pool.Standing >= pool.Min)
                    {
                        pool.Pending--;
                        respawns.RemoveAt(i);
                        continue;
                    }
                    if (SpawnElsewhere(pool, now, players) is Node node)
                    {
                        pool.Pending--;
                        pool.Standing++;
                        respawns.RemoveAt(i);
                        shown.Add(node);
                        log.Debug($"Respawned {PoolName(pool)} at {Base(node)} on landscape {node.Zone} ({pool.Standing}/{pool.Max}, min {pool.Min}).");
                    }
                    else
                        respawns[i] = (pool, now + RetryDelay);
                }
            }
            foreach (Node node in shown)
            {
                long region = RegionKey(node.Zone, Base(node));
                foreach (WorldSession session in online)
                    lock (sync)
                        if (sent.TryGetValue(session, out var have) && have.Contains(region))
                            session.EnqueueMessage(node.Spawn);
            }
        }

        /// <summary>
        /// Makes one free spot of the pool stand: random spots first, then (few free left) all of them in a random
        /// order. A spot must be free, not felled within <see cref="SameSpotCooldown"/> (unless every free spot was), still
        /// allowed (build sites, the Spire, graveyards) and clear of players. Caller holds <see cref="sync"/>.
        /// </summary>
        private static Node SpawnElsewhere(Pool pool, DateTime now, List<(uint Zone, Vector3 Position)> players)
        {
            int count = pool.Keys.Count;
            if (count == 0)
                return null;
            bool Usable(ulong key) => empty.Contains(key)
                && !(recentlyFelled.TryGetValue(key, out DateTime felled) && now - felled < SameSpotCooldown);
            var tried = new HashSet<ulong>();
            for (int attempt = 0; attempt < 48; attempt++)
            {
                ulong key = pool.Keys[random.Next(count)];
                if (tried.Add(key) && Usable(key) && Stand(key, players) is Node node)
                    return node;
            }
            foreach (ulong key in pool.Keys.Where(k => !tried.Contains(k) && Usable(k)).OrderBy(_ => random.Next()).Take(32).ToList())
                if (Stand(key, players) is Node node)
                    return node;
            // Every free spot was cut within the hour (a pool that started near its maximum, cut below its minimum in one
            // go): any free spot, at random, rather than holding the respawn past "within about 20 minutes" [P].
            foreach (ulong key in pool.Keys.Where(empty.Contains).OrderBy(_ => random.Next()).Take(32).ToList())
                if (Stand(key, players) is Node node)
                    return node;
            return null;
        }

        /// <summary>The node of a free spot made to stand (its region generated if nobody has been near), or null.</summary>
        private static Node Stand(ulong key, List<(uint Zone, Vector3 Position)> players)
        {
            Node node = Find(key);
            if (node == null || node.Type == NodeType.Decor)
                return null;   // a candidate whose region no longer makes it (a build site placed since the census)
            Vector3 at = Base(node);
            if (!Allowed(node.Zone, at.X, at.Y + 0.3f, at.Z))
                return null;
            float clear = (ModelFootprints.Of(node.Spawn.ModelId)?.Radius ?? 0.5f) * node.Spawn.Scale.X + PlayerClearance;
            foreach ((uint zone, Vector3 position) in players)
                if (zone == node.Zone && HorizontalDistance(position, at) < clear)
                    return null;
            empty.Remove(key);
            node.Hits = 0;
            node.RegrowsAt = default;
            dirty = true;
            return node;
        }

        /// <summary>Counts from the landscape's census (min / max model), or null when there is none yet.</summary>
        private static List<NodeCount> CensusCounts(uint zone)
        {
            if (Respawn != RespawnModel.MinMax)
                return null;
            lock (sync)
            {
                if (!censuses.TryGetValue(zone, out Census census))
                    return null;
                // Old-list pools of one model on several biomes are one row, as before.
                return census.Pools.Values
                    .GroupBy(p => (p.Set?.Id ?? p.Key.Set, p.Entity?.Id ?? p.Model))
                    .Select(g => new NodeCount(g.First().Set, g.First().Entity, g.First().Model, g.Sum(p => p.Standing), g.Sum(p => p.Max),
                        g.Sum(p => p.Min), g.Sum(p => p.Max), g.Sum(p => p.Pending)))
                    .ToList();
            }
        }

        private static string PoolName(Pool pool) => pool.Set != null
            ? $"set {pool.Set.Id} entity {pool.Entity.Id} (prop {pool.Entity.PropId}, model {pool.Model})"
            : $"list model {pool.Model} on biome {pool.Biome}";

        // ---- dev commands (DevCommands "treepools") ----

        /// <summary>The pools of a landscape, most short of their minimum first (dev command "treepools").</summary>
        public static string RespawnReport(uint zone, int top = 25)
        {
            if (Respawn != RespawnModel.MinMax)
                return "Gathering respawn model: in place (ProceduralRespawn=inplace).";
            lock (sync)
            {
                if (!censuses.TryGetValue(zone, out Census census))
                    return $"Landscape {zone}: no census yet ({(censusRequested.Contains(zone) ? "running" : "not requested")}).";
                var pools = census.Pools.Values.Where(p => !p.Fixed).ToList();
                var lines = new List<string>
                {
                    $"Landscape {zone} ({census.Generator}): {pools.Count} pools, {pools.Sum(p => p.Standing)} standing of {pools.Sum(p => p.Max)} spots, "
                    + $"{pools.Count(p => p.Standing < p.Min)} below min, {pools.Sum(p => p.Pending)} respawn(s) queued "
                    + $"(next {respawns.Where(r => r.Pool.Key.Zone == zone).Select(r => (DateTime?)r.At).Min()?.ToString("HH:mm:ss") ?? "-"} UTC)."
                };
                lines.AddRange(pools.OrderBy(p => p.Standing - p.Min).ThenByDescending(p => p.Max).Take(top)
                    .Select(p => $"  {PoolName(p)}: {p.Standing}/{p.Max} standing, min {p.Min}, queued {p.Pending}"));
                return string.Join("\n", lines);
            }
        }

        /// <summary>
        /// Fells (server side, without drops) up to <paramref name="count"/> standing nodes of the pool of the tree nearest
        /// the player, nearest first among the regions already generated, then anywhere (dev command "treepools fell").
        /// </summary>
        public static string DevFell(WorldSession session, int count)
        {
            if (Respawn != RespawnModel.MinMax)
                return "treepools fell: only with ProceduralRespawn=minmax.";
            var felled = new List<Node>();
            string name;
            lock (sync)
            {
                Node nearest = Nearby(session.ZoneId, session.Position, 64f).Where(n => n.Standing && n.Type == NodeType.Tree)
                    .OrderBy(n => HorizontalDistance(Base(n), session.Position)).FirstOrDefault();
                if (nearest == null || PoolOf(nearest) is not Pool pool)
                    return "treepools fell: no standing tree within 64 m (or no census yet).";
                name = PoolName(pool);
                DateTime now = DateTime.UtcNow;
                foreach (ulong key in pool.Keys.Where(k => !empty.Contains(k))
                    .OrderBy(k => regions.ContainsKey((long)(k >> 8)) ? 0 : 1).ToList())
                {
                    if (felled.Count >= count)
                        break;
                    if (Find(key) is not Node node || !node.Standing)
                        continue;
                    node.Hits = 0;
                    Felled(node, now);
                    felled.Add(node);
                }
                name += $": {pool.Standing}/{pool.Max} standing, min {pool.Min}, queued {pool.Pending}";
            }
            foreach (Node node in felled)
                Despawn(node);
            return $"treepools fell: {felled.Count} felled of {name}.";
        }

        /// <summary>Makes every queued respawn of a landscape due now (dev command "treepools now").</summary>
        public static string DevRespawnNow(uint zone)
        {
            lock (sync)
            {
                int n = 0;
                for (int i = 0; i < respawns.Count; i++)
                    if (respawns[i].Pool.Key.Zone == zone)
                    {
                        respawns[i] = (respawns[i].Pool, DateTime.MinValue);
                        n++;
                    }
                nextRespawnCheck = DateTime.MinValue;
                return $"treepools now: {n} queued respawn(s) on landscape {zone} due now.";
            }
        }
    }
}
