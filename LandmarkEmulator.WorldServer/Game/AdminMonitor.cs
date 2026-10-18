using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Social;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The live server's admin monitor data (BUILD-PATH-0695 Phase B.7; trace/architecture/admin-pages.md, admin-monitor.md).
    /// <para>
    /// The client has a performance monitor with server pages (Alt+F; 11 zone profiler, 12 zone stats, 24 gateway,
    /// 28 procedural data, 29 instanced games, 32 pathing, 33 Environment Server), but the retail client cannot select a
    /// page: the paged view is switched on only by an unreferenced debug body (OPEN-LEADS L3; nothing calls the page
    /// setter FUN_142cbd160 / 0.695 FUN_142d56520), and only pages 28 and 29 have readers for a server reply (L4). So
    /// (user's decision, 2026-10-15) the data is shown on the world's web page /admin/monitor (<see cref="Snapshot"/>,
    /// served by <see cref="WebPages"/> to admins), and the two client replies are still built exactly as the client
    /// reads them, to be proven later with a dev-only Frida page hook (admin-pages.md §8):
    /// </para>
    /// <list type="bullet">
    /// <item>Page 28: ProceduralDataMonitorPacketUpdateRequest 0x109 (sent by the client only while page 28 is shown) is
    /// answered for admins with ProceduralDataMonitorPacketUpdateResponse 0x10A (<see cref="ProceduralBody"/>). The request
    /// itself proves the page is up, so no setting gates it.</item>
    /// <item>Page 29: InstancedGameAdminMonitorPacket 0x10D sub 2 has no request, so it is pushed. Pushed only with the
    /// setting <c>AdminMonitorPush</c> (WorldServer.json or the environment; default <b>off</b>, because a retail client
    /// can't show page 29 and would just drop it), only to admins (<see cref="Admin.IsAdmin"/>) standing on or playing in
    /// a site game, at most every 2 s and only when the tree changed (or every 10 s to refresh a page opened later).</item>
    /// </list>
    /// It also keeps the per-system update times of the world tick (the page-11 / page-32 "Name Count AvgUs MinUs MaxUs"
    /// table, ours on the web page) in 5-second windows.
    /// </summary>
    public static class AdminMonitor
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        #region tick timing (world thread)

        private sealed class Timing
        {
            public long Count, Ticks, Max;
            public long Min = long.MaxValue;
        }

        private static readonly double WindowTicks = Stopwatch.Frequency * 5.0;
        private static Dictionary<string, Timing> current = new(), last = new();
        private static Timing currentTick = new(), lastTick = new();
        private static long windowStart = Stopwatch.GetTimestamp();
        private static double lastWindowSeconds;

        /// <summary>World thread: one system's update took <paramref name="elapsed"/> Stopwatch ticks.</summary>
        public static void RecordSystem(string name, long elapsed)
        {
            if (!current.TryGetValue(name, out Timing t))
                current[name] = t = new Timing();
            Add(t, elapsed);
        }

        /// <summary>World thread: the whole tick (all systems, not the 1 ms sleep) took <paramref name="elapsed"/> ticks.</summary>
        public static void RecordTick(long elapsed)
        {
            Add(currentTick, elapsed);
            long now = Stopwatch.GetTimestamp();
            if (now - windowStart < WindowTicks)
                return;
            lastWindowSeconds = (now - windowStart) / (double)Stopwatch.Frequency;
            windowStart = now;
            (last, current) = (current, new Dictionary<string, Timing>(last.Count));
            (lastTick, currentTick) = (currentTick, new Timing());
        }

        private static void Add(Timing t, long elapsed)
        {
            t.Count++;
            t.Ticks += elapsed;
            if (elapsed > t.Max)
                t.Max = elapsed;
            if (elapsed < t.Min)
                t.Min = elapsed;
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        #endregion

        #region page 29 push (InstancedGameAdminMonitorPacket 0x10D sub 2)

        private static bool? pushEnabled;

        /// <summary>Setting "AdminMonitorPush" (default false): push page 29 to admins on a site game.</summary>
        public static bool PushEnabled => pushEnabled ??= SharedConfiguration.Configuration?.GetValue("AdminMonitorPush", false) ?? false;

        private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(2), PushRefresh = TimeSpan.FromSeconds(10);
        private static readonly Dictionary<WorldSession, (DateTime At, byte[] Body)> pushed = new();
        private static DateTime nextPush;

        /// <summary>World tick: the page-29 push (only with <see cref="PushEnabled"/>).</summary>
        public static void Update()
        {
            if (!PushEnabled)
                return;
            DateTime now = DateTime.UtcNow;
            if (now < nextPush)
                return;
            nextPush = now + PushInterval;
            List<WorldSession> online = PlayerManager.Snapshot();
            foreach (WorldSession gone in pushed.Keys.Where(s => s.Disconnected || !online.Contains(s)).ToList())
                pushed.Remove(gone);
            foreach (WorldSession session in online)
            {
                if (session.Disconnected || !Admin.IsAdmin(session))
                    continue;
                List<InstancedGames.MonitorLine> lines = InstancedGames.MonitorLinesFor(session);
                if (lines == null)
                {
                    pushed.Remove(session);
                    continue;
                }
                byte[] body = InstancedGames.AdminMonitorBody(lines);
                if (pushed.TryGetValue(session, out var had) && now - had.At < PushRefresh && had.Body.AsSpan().SequenceEqual(body))
                    continue;
                pushed[session] = (now, body);
                session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, body);
            }
        }

        #endregion

        #region page 28 (0x109 → 0x10A)

        private static readonly TimeSpan ProceduralMinInterval = TimeSpan.FromSeconds(4);
        private static readonly Dictionary<WorldSession, DateTime> lastProcedural = new();

        /// <summary>
        /// ProceduralDataMonitorPacketUpdateRequest 0x109: the client shows page 28 and asks every 5 s. Admins get 0x10A
        /// for their landscape (at most one per 4 s); anyone else is ignored.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ProceduralDataMonitorUpdateRequest)]
        public static void HandleProceduralRequest(WorldSession session, ProceduralDataMonitorRequest request)
        {
            if (!Admin.IsAdmin(session))
            {
                OpcodeStats.Unanswered("ProceduralDataMonitor", 0x109);
                return;
            }
            DateTime now = DateTime.UtcNow;
            foreach (WorldSession gone in lastProcedural.Keys.Where(s => s.Disconnected).ToList())
                lastProcedural.Remove(gone);
            if (lastProcedural.TryGetValue(session, out DateTime at) && now - at < ProceduralMinInterval)
                return;
            lastProcedural[session] = now;
            byte[] body = ProceduralBody(session.ZoneId);
            session.EnqueueMessage(ZoneMessageOpcode.ProceduralDataMonitorUpdateResponse, body);
            log.Debug($"Page 28 (0x10A, {body.Length} bytes) to {session.CharacterName}.");
        }

        /// <summary>Most entities one 0x10A carries (each costs ~80 bytes with its table row).</summary>
        private const int MaxEntities = 400;
        /// <summary>The entity-list key of the old per-biome tree lists (no placement set; ours).</summary>
        private const uint OldTreeListKey = 100000;

        /// <summary>
        /// ProceduralDataMonitorPacketUpdateResponse 0x10A after the u16 opcode, for one landscape. Wire layout from the
        /// client's reader FUN_142bb4ba0 / 0.695 FUN_142c4e3b0 (trace/0695/agent-L1-L2.md, admin-pages.md §3; re-checked
        /// with re/tools/pkt.py 2026-10-16); every list is <c>u32 count, count × {u32 key, element}</c>:
        /// <code>
        /// u32 spawnCount; u32 nodeCount;
        /// list layers {u32 queued; list biomes {u32 used; u32 unused; f32; list entityLists {list entities {u32 a;
        ///   u32 nameTextId; u32 spawnCount; u32 min; u32 max; u32; u32; bool}; u32 nodeCount}}; u32 available;
        ///   u32 belowMin; u32 aboveMin; u32 overMax};
        /// u32 N; u32 P; u32 F;
        /// list EntityListEntities rows {u32 ID, ENTITY_LIST_ID, ENTITY_LIST_USE_TYPE_ID, ENTITY_ID, ENTITY_GROUP_ID,
        ///   MIN_SPAWNS_PER_ZONE, MAX_SPAWNS_PER_ZONE; f32 SPAWN_WEIGHTING; i32 LAYER_TIER_WHITELIST_ID};
        /// list EntityLists rows {u32 ID; string NAME; i32 LAYER_TIER_WHITELIST_ID}
        /// </code>
        /// The page draws an entity list only if its key is among the EntityLists rows and its entities' spawn total is
        /// above 0, and an entity only if its key is among the EntityListEntities rows and its count is above 0, so every
        /// key sent gets its row. Live's two id tables (ProceduralPlacementEntityLists / ...ListEntities.txt) were
        /// server-only (L2) and the meaning of layer / N / P / F / the undrawn fields is not proven (L82). The mapping is
        /// ours, from what the spawner has (<see cref="Gathering.NodeCounts"/>): layer = the placement set's
        /// PLACEMENT_DATA_GROUP_ID (0 = the old tree lists), biome = the set's BIOME_ID, entity list = the set, entity = the
        /// set entity (a = its id, name = its PropDefs prop's name); spawn count = nodes standing, node count = candidate
        /// spots; entity min / max and the rows' MIN_ / MAX_SPAWNS_PER_ZONE = the pool's minimum / maximum on this landscape
        /// (live's own per-zone fields, now that the respawn model has them: Gathering.Respawn.cs); layer "queued" =
        /// respawns waiting (min / max model) or nodes regrowing (regrow-in-place); Below Min / Above Min / Over Max = the
        /// standing nodes of entities under their minimum / from it to under their maximum / at it [I: the page prints them
        /// as counts per layer]; N / P / F = 0.
        /// </summary>
        public static byte[] ProceduralBody(uint zone) => ProceduralBody(Gathering.NodeCounts(zone), Props.NameIdOf);

        /// <summary>As <see cref="ProceduralBody(uint)"/> from given counts (<paramref name="nameOf"/>: prop → name text id).</summary>
        public static byte[] ProceduralBody(IEnumerable<Gathering.NodeCount> nodeCounts, Func<uint, uint> nameOf)
        {
            List<Gathering.NodeCount> counts = nodeCounts
                .OrderBy(c => c.Set?.Group ?? 0).ThenBy(c => c.Set?.Id ?? -1).ThenBy(c => c.Entity?.Id ?? c.Model)
                .Take(MaxEntities).ToList();
            int standing = counts.Sum(c => c.Standing), total = counts.Sum(c => c.Total);

            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((uint)standing);
            w.Write((uint)Math.Max(1, total));   // the page divides by it ("%d%% of nodes")

            uint nextKey = 1;
            var entityRows = new List<(uint Key, Gathering.NodeCount Count, uint ListKey)>();
            var listRows = new List<(uint Key, string Name)>();
            var layers = counts.GroupBy(c => (uint)Math.Max(0, c.Set?.Group ?? 0)).ToList();
            w.Write((uint)layers.Count);
            foreach (var layer in layers)
            {
                int layerTotal = layer.Sum(c => c.Total);
                w.Write(layer.Key);
                w.Write((uint)layer.Sum(c => c.Queued));                 // queued: respawns waiting / nodes regrowing (ours)
                var biomes = layer.GroupBy(c => (uint)Math.Max(0, c.Set?.BiomeId ?? 0)).ToList();
                w.Write((uint)biomes.Count);
                foreach (var biome in biomes)
                {
                    int used = biome.Sum(c => c.Standing), all = biome.Sum(c => c.Total);
                    w.Write(biome.Key);
                    w.Write((uint)used);
                    w.Write((uint)(all - used));
                    w.Write(all > 0 ? used * 100f / all : 0f);
                    var lists = biome.GroupBy(c => c.Set == null ? OldTreeListKey : (uint)c.Set.Id).ToList();
                    w.Write((uint)lists.Count);
                    foreach (var list in lists)
                    {
                        w.Write(list.Key);
                        Gathering.NodeCount first = list.First();
                        listRows.Add((list.Key, first.Set == null ? "Tree list (no placement set; ours)"
                            : $"Placement set {first.Set.Id} (biome {first.Set.BiomeId}{(string.IsNullOrEmpty(first.Set.MaskName) ? "" : ", " + first.Set.MaskName)})"));
                        w.Write((uint)list.Count());
                        foreach (Gathering.NodeCount c in list)
                        {
                            uint key = nextKey++;
                            entityRows.Add((key, c, list.Key));
                            w.Write(key);
                            w.Write(c.Entity?.Id ?? c.Model);                       // a: printed "%d"
                            w.Write(c.Entity != null ? nameOf(c.Entity.PropId) : 0u);
                            w.Write((uint)c.Standing);
                            w.Write((uint)c.Min);                                   // min
                            w.Write((uint)c.Max);                                   // max
                            w.Write(0u);
                            w.Write(0u);
                            w.Write(true);                                          // the reader's default
                        }
                        w.Write((uint)list.Sum(c => c.Total));                      // list node count
                    }
                }
                w.Write((uint)layerTotal);                                          // Available
                w.Write((uint)layer.Where(c => c.Standing < c.Min).Sum(c => c.Standing));                     // Below Min
                w.Write((uint)layer.Where(c => c.Standing >= c.Min && c.Standing < c.Max).Sum(c => c.Standing)); // Above Min
                w.Write((uint)layer.Where(c => c.Standing >= c.Max).Sum(c => c.Standing));                    // Over Max
            }
            w.Write(0u);   // N (L82)
            w.Write(0u);   // P
            w.Write(0u);   // F

            w.Write((uint)entityRows.Count);
            foreach ((uint key, Gathering.NodeCount c, uint listKey) in entityRows)
            {
                w.Write(key);
                w.Write(c.Entity?.Id ?? c.Model);                          // ID
                w.Write(listKey);                                          // ENTITY_LIST_ID
                w.Write(0u);                                               // ENTITY_LIST_USE_TYPE_ID
                w.Write(c.Entity?.PropId ?? 0u);                           // ENTITY_ID
                w.Write((uint)Math.Max(0, c.Set?.Group ?? 0));             // ENTITY_GROUP_ID
                w.Write((uint)c.Min);                                      // MIN_SPAWNS_PER_ZONE
                w.Write((uint)c.Max);                                      // MAX_SPAWNS_PER_ZONE
                w.Write(c.Entity?.Weight ?? 1f);                           // SPAWN_WEIGHTING
                w.Write(-1);                                               // LAYER_TIER_WHITELIST_ID (default)
            }
            var distinctLists = listRows.GroupBy(r => r.Key).Select(g => g.First()).ToList();
            w.Write((uint)distinctLists.Count);
            foreach ((uint key, string name) in distinctLists)
            {
                w.Write(key);
                w.Write(key);                                              // ID
                w.Write(name);                                             // NAME
                w.Write(-1);                                               // LAYER_TIER_WHITELIST_ID
            }
            return output.ToArray();
        }

        #endregion

        #region web snapshot (/admin/monitor)

        private static readonly DateTime started = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        private static string LandscapeName(uint zone)
        {
            string name = TextManager.Instance.GetTextForId(Landscapes.Get(zone)?.NameText ?? 0);
            return string.IsNullOrEmpty(name) ? $"Landscape {zone}" : name;
        }

        /// <summary>
        /// Everything the web page shows, built on the world thread (WebPages runs it there and caches it for 2 s, so
        /// open tabs cost one build per interval). Read-only.
        /// </summary>
        public static object Snapshot()
        {
            DateTime now = DateTime.UtcNow;
            List<WorldSession> online = PlayerManager.Snapshot().Where(s => !s.Disconnected).ToList();
            Dictionary<uint, int> creatures = Creatures.AliveByZone(), props = Props.PlacedByZone();
            var zones = online.Select(s => s.ZoneId).Concat(creatures.Keys).Concat(props.Keys).Distinct().OrderBy(z => z);

            var landscapes = zones.Select(z => new
            {
                id = z,
                name = LandscapeName(z),
                players = online.Count(s => s.ZoneId == z),
                creatures = creatures.GetValueOrDefault(z),
                props = props.GetValueOrDefault(z)
            }).ToList();

            var systems = last.Select(p => new
            {
                name = p.Key,
                count = p.Value.Count,
                avgUs = p.Value.Count > 0 ? Math.Round(Ms(p.Value.Ticks) * 1000 / p.Value.Count, 1) : 0,
                minUs = p.Value.Count > 0 ? Math.Round(Ms(p.Value.Min) * 1000, 1) : 0,
                maxUs = Math.Round(Ms(p.Value.Max) * 1000, 1),
                totalMs = Math.Round(Ms(p.Value.Ticks), 2)
            }).OrderByDescending(s => s.totalMs).ToList();

            double tickAvg = lastTick.Count > 0 ? Ms(lastTick.Ticks) / lastTick.Count : 0;
            var procedural = online.Select(s => s.ZoneId).Distinct().OrderBy(z => z).Select(z =>
            {
                List<Gathering.NodeCount> counts = Gathering.NodeCounts(z);
                return new
                {
                    landscape = z,
                    name = LandscapeName(z),
                    standing = counts.Sum(c => c.Standing),
                    nodes = counts.Sum(c => c.Total),
                    min = counts.Sum(c => c.Min),
                    queued = counts.Sum(c => c.Queued),
                    belowMin = counts.Count(c => c.Standing < c.Min),
                    sets = counts.GroupBy(c => c.Set?.Id ?? -1).Select(g => new
                    {
                        set = g.Key,
                        group = g.First().Set?.Group ?? 0,
                        biome = g.First().Set?.BiomeId ?? 0,
                        mask = g.First().Set?.MaskName ?? "",
                        standing = g.Sum(c => c.Standing),
                        nodes = g.Sum(c => c.Total),
                        min = g.Sum(c => c.Min),
                        queued = g.Sum(c => c.Queued)
                    }).OrderBy(x => x.set).ToList()
                };
            }).ToList();

            var packets = OpcodeStats.Snapshot().Take(200).Select(r =>
            {
                string name = "";
                try
                {
                    name = WorldSession.GetOpcode(Convert.FromHexString(r.Bytes)).Item1?.ToString() ?? "";
                }
                catch (FormatException)
                {
                }
                return new { dir = r.Direction, bytes = r.Bytes, name, count = r.Count, totalBytes = r.TotalBytes,
                    avgBytes = r.Count > 0 ? r.TotalBytes / r.Count : 0, worstBytes = r.WorstBytes };
            }).ToList();

            var social = SocialServices.Service is RemoteSocialService remote
                ? (remote.IsConnected ? "remote, connected" : "remote, not connected") : "in-process";
            return new
            {
                now,
                uptimeSeconds = (long)(now - started).TotalSeconds,
                world = new
                {
                    zoneServerId = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.ZoneServerId ?? 0,
                    shutdownPending = Shutdown.InProgress,
                    pushPage29 = PushEnabled
                },
                zone = new
                {
                    players = online.Count,
                    windowSeconds = Math.Round(lastWindowSeconds, 1),
                    ticks = lastTick.Count,
                    ticksPerSecond = lastWindowSeconds > 0 ? Math.Round(lastTick.Count / lastWindowSeconds, 1) : 0,
                    tickAvgMs = Math.Round(tickAvg, 3),
                    tickMaxMs = Math.Round(Ms(lastTick.Max), 3),
                    landscapes
                },
                systems,
                links = new
                {
                    gateway = GatewayLink.Enabled ? (GatewayLink.Connected ? "separate process, connected" : "separate process, not connected") : "in-process",
                    social,
                    worldChannel = WorldChannelLink.IsLinked ? "world-channel service linked" : "in the zone (no service linked)",
                    commerce = WorldChannelServices.Commerce.LinkState
                },
                // What the linked services report about themselves (ServiceStatus, every 10 s; B.7).
                services = ServiceStatuses.Snapshot(),
                terrain = new
                {
                    store = EnvironmentServer.Instance?.MemoryReport() ?? "",
                    healedCells = ZoneTerrain.HealedCells,
                    lastHeal = ZoneTerrain.LastHeal
                },
                games = InstancedGames.MonitorAll().Select(lines => lines.Select(l => new { key = l.Key, value = l.Value, indent = l.Indent })),
                procedural = new
                {
                    enabled = ProceduralPlacement.Enabled,
                    respawn = Gathering.Respawn == Gathering.RespawnModel.MinMax ? $"min / max (min = {Gathering.MinShare:0.##} x max), respawns elsewhere" : "in place",
                    loaded = ProceduralPlacement.Loaded,
                    sets = ProceduralPlacement.Sets.Count,
                    landscapes = procedural
                },
                // Page 32 "Pathing Information - Zone" (Game/Pathing, TASKS 6.61; field meanings ours [I]). The client's own
                // page can't be opened in retail and 0x4AA has no reader (L4), so only this page shows it.
                pathing = Pathing.Monitor(Creatures.PathingCharacters()),
                packets = new { since = OpcodeStats.Since, rows = packets },
                players = online.OrderBy(s => s.CharacterName).Select(s => new
                {
                    name = s.CharacterName,
                    landscape = s.ZoneId,
                    x = Math.Round(s.Position.X, 1),
                    y = Math.Round(s.Position.Y, 1),
                    z = Math.Round(s.Position.Z, 1),
                    admin = Admin.IsAdmin(s)
                }).ToList()
            };
        }

        #endregion
    }
}
