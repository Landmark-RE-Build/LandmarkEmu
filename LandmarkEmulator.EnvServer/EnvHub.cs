using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Environment;
using LandmarkEmulator.WorldServer.Game.Terrain;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// The service's links and work loop (terrain-service.md §4.4, §5): the zone process(es) of the world dial the internal
    /// listener; every request is answered by <see cref="LocalEnvironmentServer"/> — the same store code the world runs
    /// in-process — on <b>one work thread</b>, together with the store's upkeep (heal queue, vein regions, flush, the
    /// one-time layer move, LOD refresh), so the stores see exactly the single-threaded use they always had in the world
    /// process (only the LOD copies build in the background, as before).
    /// <para>
    /// <b>Version index pushes</b> (spec §5.2 CellVersionsChanged, §5.3): the service keeps what it last told the zones per
    /// (landscape, cell) — every layer's version — and after every change sends the differences to every link, with one
    /// sequence number for the service: to the requesting link as <see cref="EnvChangeReason.Request"/> before its answer
    /// (so the zone's index is current when its call returns), to other links as <see cref="EnvChangeReason.OtherZone"/>;
    /// heals, vein updates and LOD refreshes with their own reason; every 30 s the whole index is compared again
    /// (<see cref="EnvChangeReason.Reconcile"/>) so nothing changed by a path that reports no cells is missed. Payloads
    /// are never pushed (bandwidth, spec §5.4): the zone fetches a cell's body when a player needs it.
    /// </para>
    /// <para>
    /// <b>Keep-out boxes</b>: the build sites and Spire platforms come from the zone (<see cref="EnvKeepOut"/>). Until a
    /// zone has sent them, nothing that must respect them runs — no heal, no vein spawning, no layer move — because an
    /// empty list would let a heal take site voxels; the heal queue is persisted and catches up (overdue first) once they
    /// arrive.
    /// </para>
    /// </summary>
    public sealed class EnvHub : IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50), ReconcileInterval = TimeSpan.FromSeconds(30),
            AliveInterval = TimeSpan.FromSeconds(10);

        private sealed class LinkState
        {
            public InternalLink Link;
            public uint World;
            public string Name = "";
            public List<(uint Zone, Vector3 Position)> Positions = new();
        }

        private readonly EnvServerConfiguration config;
        private readonly string data;
        private readonly InternalListener listener;
        private readonly LocalEnvironmentServer env;
        private readonly BlockingCollection<Action> work = new();
        private readonly CancellationTokenSource stop = new();
        private Thread thread;
        private readonly List<LinkState> links = new();
        private readonly JobCounters jobs = new();
        private readonly RuinRecords ruins;

        // ---- version index (work thread only) --------------------------------------------------------------------------
        private ulong sequence;
        private readonly Dictionary<uint, Dictionary<ulong, uint[]>> sent = new();
        private readonly HashSet<uint> landscapes = new() { TerrainStore.HomeContinent };

        // ---- keep-out boxes (merged from every link; read by the stores on the work thread and by LOD tasks) ---------------
        private volatile Dictionary<uint, List<double[]>> sites = new(), noVeins = new();
        /// <summary>
        /// The latest boxes from each zone (world / name), kept after its link closes: a zone restarting or a link blip must
        /// never leave the heal with no sites (it would heal site voxels); the zone's next snapshot replaces them.
        /// </summary>
        private readonly Dictionary<string, EnvKeepOut> keepOutBy = new();
        private bool keepOutKnown, warnedKeepOut;

        private DateTime nextTick, nextReconcile, nextAlive, nextStatus;

        public EnvHub(EnvServerConfiguration config, string data, IPAddress address, int port)
        {
            this.config = config;
            this.data = data;
            ruins = new RuinRecords(data, config.RuinLayers, config.RuinLayerDesired);
            env = new LocalEnvironmentServer(SitesIn);
            OreVeins.NoVeins = NoVeinsIn;
            env.LodCopiesChanged += (zone, layer, copies) => work.Add(() => PublishLod(zone, layer, copies));
            env.LoadVeins(config.GameDirectory, config.Continent);
            env.Load(Path.Combine(data, "TerrainEdits"));
            env.BuildLodCopies();   // in the background, as in the world process
            listener = new InternalListener(address, port);
            listener.Accepted += OnAccepted;
        }

        public void Start()
        {
            // The first index: what the store holds after loading (no link to tell yet).
            Reconcile(publish: false);
            thread = new Thread(Loop) { Name = "Environment Server", IsBackground = true };
            thread.Start();
            listener.Start();
            log.Info($"Environment Server listening on port {listener.Port} for the world's zones; {env.MemoryReport()}; "
                + $"heal queue {(config.HealQueue ? "on" : "paused")} ({HealQueue.Count} pending), ruin layers {ruins.Summary()}.");
        }

        public void Dispose()
        {
            listener.Dispose();
            stop.Cancel();
            thread?.Join(TimeSpan.FromSeconds(30));
            try
            {
                env.Flush();
            }
            catch (Exception e)
            {
                log.Error(e, "Final flush failed.");
            }
            foreach (LinkState link in links.ToList())
                link.Link.Close();
        }

        // ---- links ------------------------------------------------------------------------------------------------------

        private void OnAccepted(InternalLink link)
        {
            var state = new LinkState { Link = link };
            link.Received += (_, message) =>
            {
                if (message is not ServicePacket packet)
                    return;
                int type = JobCounters.TypeOf(packet.Family, packet.Sub);
                jobs.Queued(type);
                work.Add(() => Handle(state, packet, type));
            };
            link.Closed += (_, error) => work.Add(() =>
            {
                links.Remove(state);
                log.Warn($"Zone link {state.Name} ({link.Remote}) closed{(error != null ? ": " + error.Message : "")}.");
            });
            work.Add(() => links.Add(state));
            link.Start();
            log.Info($"Zone connected from {link.Remote}.");
        }

        private static void Send(LinkState state, ServicePacket packet)
        {
            try
            {
                state.Link.Send(packet);
            }
            catch (ArgumentException e)
            {
                // Over the frame limit (a huge answer): the zone's call times out and it treats the service as away for it.
                log.Error($"Answer to {state.Name} not sent: {e.Message}");
            }
        }

        // ---- the work loop ------------------------------------------------------------------------------------------------

        private void Loop()
        {
            while (!stop.IsCancellationRequested)
            {
                TimeSpan wait = nextTick - DateTime.UtcNow;
                try
                {
                    if (work.TryTake(out Action action, (int)Math.Max(0, wait.TotalMilliseconds), stop.Token))
                        Run(action);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                if (DateTime.UtcNow >= nextTick)
                {
                    nextTick = DateTime.UtcNow + TickInterval;
                    Run(Tick);
                }
            }
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                log.Error(e, "Environment Server work failed.");
            }
        }

        /// <summary>The store's upkeep, as the world tick ran it (HostedService "terrain regeneration" … "terrain LOD").</summary>
        private void Tick()
        {
            DateTime now = DateTime.UtcNow;
            if (keepOutKnown)
            {
                if (config.HealQueue)
                    foreach ((uint zone, List<ulong> cells) in env.Regenerate())
                    {
                        jobs.Count(JobCounters.TemporaryModifications, cells.Count);
                        Publish(zone, cells, EnvChangeReason.Heal, null);
                    }
                var positions = links.SelectMany(l => l.Positions).ToList();
                foreach ((uint zone, List<ulong> cells) in env.UpdateVeins(positions))
                {
                    jobs.Count(JobCounters.SpawnVein, cells.Count);
                    Publish(zone, cells, EnvChangeReason.Veins, null);
                }
                env.MigrateToLayers();
            }
            else if (!warnedKeepOut && links.Count > 0)
            {
                warnedKeepOut = true;
                log.Info("Waiting for the zone's build-site boxes: heals, vein spawning and the layer move wait until they arrive.");
            }
            env.Flush();
            env.UpdateLodCopies();
            if (now >= nextReconcile)
            {
                nextReconcile = now + ReconcileInterval;
                Reconcile(publish: true);
            }
            if (now >= nextStatus)
            {
                nextStatus = now + ServiceStatus.Interval;
                ServicePacket status = ServiceStatus.Build("environment server", StatusRows());
                foreach (LinkState link in links)
                    Send(link, status);
            }
            if (now >= nextAlive && !string.IsNullOrWhiteSpace(config.HealthFile))
            {
                nextAlive = now + AliveInterval;
                try
                {
                    File.WriteAllText(config.HealthFile, now.ToString("u"));
                }
                catch (IOException)
                {
                }
            }
        }

        private IEnumerable<(string, string)> StatusRows()
        {
            yield return ("zones linked", links.Count == 0 ? "none" : string.Join(", ", links.Select(l => $"{l.Name} (world {l.World})")));
            yield return ("store", env.MemoryReport());
            yield return ("heal queue", $"{(config.HealQueue ? "on" : "paused")}, {HealQueue.Count} pending{(keepOutKnown ? "" : ", waiting for site boxes")}");
            yield return ("version index", $"{sent.Values.Sum(s => s.Count)} cells on {sent.Count} landscape(s), sequence {sequence}");
            yield return ("jobs", jobs.Summary());
            yield return ("ruin layers", ruins.Summary());
        }

        // ---- keep-out boxes -------------------------------------------------------------------------------------------------

        private void MergeKeepOut()
        {
            var s = new Dictionary<uint, List<double[]>>();
            var v = new Dictionary<uint, List<double[]>>();
            foreach (EnvKeepOut keepOut in keepOutBy.Values)
                foreach ((uint zone, (List<double[]> siteBoxes, List<double[]> veinBoxes)) in keepOut.Landscapes)
                {
                    (s.TryGetValue(zone, out var a) ? a : s[zone] = new()).AddRange(siteBoxes);
                    (v.TryGetValue(zone, out var b) ? b : v[zone] = new()).AddRange(veinBoxes);
                }
            sites = s;
            noVeins = v;
            keepOutKnown = keepOutBy.Count > 0;
        }

        private static bool Touches(double[] b, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
            => b[0] <= max.X && b[3] >= min.X && b[1] <= max.Y && b[4] >= min.Y && b[2] <= max.Z && b[5] >= min.Z;

        /// <summary>Claims.BoxesIn's answer from the zone's boxes (same zone keys, same bounds test).</summary>
        private List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)> SitesIn(uint zone,
            (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            if (!sites.TryGetValue(zone, out var boxes))
                return new();
            return boxes.Where(b => Touches(b, min, max)).Select(b => (b[0], b[1], b[2], b[3], b[4], b[5])).ToList();
        }

        /// <summary>Spire.Touches's answer (strict overlap, as there) from the zone's no-vein boxes.</summary>
        private bool NoVeinsIn(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
            => noVeins.TryGetValue(zone, out var boxes)
                && boxes.Any(b => max.X > b[0] && min.X < b[3] && max.Y > b[1] && min.Y < b[4] && max.Z > b[2] && min.Z < b[5]);

        // ---- the version index ------------------------------------------------------------------------------------------

        private static uint Key(uint zone) => zone == 0 ? TerrainStore.HomeContinent : zone;

        /// <summary>Sends the differences for some cells of a landscape (and remembers them).</summary>
        private void Publish(uint zone, IEnumerable<ulong> cells, EnvChangeReason reason, LinkState origin)
        {
            zone = Key(zone);
            landscapes.Add(zone);
            if (!sent.TryGetValue(zone, out var known))
                sent[zone] = known = new();
            var entries = new List<(ulong, uint, uint, byte)>();
            foreach (ulong cell in cells.Distinct())
            {
                uint[] now = EnvironmentWire.IndexVersions(cell, zone);
                known.TryGetValue(cell, out uint[] before);
                for (int layer = 0; layer < 4; layer++)
                    if ((before?[layer] ?? 0) != now[layer])
                        entries.Add((cell, (uint)layer + 1, now[layer], (byte)0));
                if (now.All(v => v == 0))
                    known.Remove(cell);
                else
                    known[cell] = now;
            }
            Push(zone, entries, reason, origin);
        }

        private void PublishLod(uint zone, uint layer, IReadOnlyList<(ulong Cell, uint Version)> copies)
        {
            zone = Key(zone);
            jobs.Count((int)Math.Min(layer, 3u), copies.Count);
            if (!sent.TryGetValue(zone, out var known))
                sent[zone] = known = new();
            var entries = new List<(ulong, uint, uint, byte)>();
            foreach ((ulong cell, uint version) in copies)
            {
                if (!known.TryGetValue(cell, out uint[] versions))
                    known[cell] = versions = new uint[4];
                if (versions[layer] == version)
                    continue;
                versions[layer] = version;
                entries.Add((cell, layer + 1, version, (byte)0));
            }
            Push(zone, entries, EnvChangeReason.Lod, null);
        }

        private void Push(uint zone, List<(ulong, uint, uint, byte)> entries, EnvChangeReason reason, LinkState origin)
        {
            if (entries.Count == 0)
                return;
            ulong number = ++sequence;
            foreach (LinkState link in links)
            {
                EnvChangeReason why = link == origin ? EnvChangeReason.Request : reason == EnvChangeReason.Request ? EnvChangeReason.OtherZone : reason;
                Send(link, new CellVersionsChanged { Landscape = zone, Sequence = number, Reason = why, Entries = entries }.ToPacket());
            }
        }

        /// <summary>The whole index read from the store and compared with what was sent (every 30 s, and before a snapshot).</summary>
        private void Reconcile(bool publish)
        {
            foreach (uint z in TerrainEdits.Zones())
                landscapes.Add(Key(z));
            foreach (uint zone in landscapes.ToList())
            {
                var fresh = new Dictionary<ulong, uint[]>();
                for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                    foreach ((ulong cell, uint version) in TerrainEdits.ManifestEntries(zone, layer))
                    {
                        if (!fresh.TryGetValue(cell, out uint[] versions))
                            fresh[cell] = versions = new uint[4];
                        versions[layer] = version;
                    }
                sent.TryGetValue(zone, out var known);
                known ??= new();
                var entries = new List<(ulong, uint, uint, byte)>();
                foreach ((ulong cell, uint[] now) in fresh)
                {
                    known.TryGetValue(cell, out uint[] before);
                    for (int layer = 0; layer < 4; layer++)
                        if ((before?[layer] ?? 0) != now[layer])
                            entries.Add((cell, (uint)layer + 1, now[layer], (byte)0));
                }
                foreach ((ulong cell, uint[] before) in known)
                    if (!fresh.ContainsKey(cell))
                        for (int layer = 0; layer < 4; layer++)
                            if (before[layer] != 0)
                                entries.Add((cell, (uint)layer + 1, 0u, (byte)0));
                sent[zone] = fresh;
                if (publish && entries.Count > 0)
                {
                    log.Debug($"Reconcile: {entries.Count} index change(s) on landscape {zone} not reported before.");
                    Push(zone, entries, EnvChangeReason.Reconcile, null);
                }
            }
        }

        private EnvIndex Snapshot(bool withOre)
        {
            jobs.Count(JobCounters.Manifest);
            Reconcile(publish: true);
            return new EnvIndex
            {
                Sequence = sequence,
                Landscapes = sent.ToDictionary(z => z.Key, z => z.Value
                    .SelectMany(c => Enumerable.Range(0, 4).Where(l => c.Value[l] != 0).Select(l => (c.Key, (byte)l, c.Value[l]))).ToList()),
                OreItems = withOre ? OreVeins.OreItems() : new()
            };
        }

        // ---- requests ------------------------------------------------------------------------------------------------------

        private void Handle(LinkState state, ServicePacket packet, int type)
        {
            if (EnvironmentMessages.IsReply(packet))
                return;
            (uint id, uint zone, InternalReader r) = EnvironmentMessages.ReadRequest(packet);
            jobs.Started(type);
            int countAs = -1;
            try
            {
                countAs = Answer(state, packet.Family, packet.Sub, id, zone, r);
            }
            catch (Exception e)
            {
                log.Error(e, $"Request 0x{packet.Family:X}/0x{packet.Sub:X} from {state.Name} failed.");
                if (id != 0)
                    Send(state, EnvironmentMessages.Reply(id, EnvironmentMessages.ResultFailed));
            }
            finally
            {
                jobs.Finished(type, countAs);
            }
        }

        private void Reply(LinkState state, uint id, uint result, Action<InternalWriter> payload = null)
        {
            if (id != 0)
                Send(state, EnvironmentMessages.Reply(id, result, payload));
        }

        /// <summary>Runs one request; returns the job type to count it as when that differs from its family / sub's (-1).</summary>
        private int Answer(LinkState state, ushort family, uint sub, uint id, uint zone, InternalReader r)
        {
            switch (family, sub)
            {
                // ---- link set-up ----------------------------------------------------------------------------------------
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.HelloSub):
                {
                    state.World = r.U32();
                    state.Name = r.Str();
                    EnvIndex index = Snapshot(withOre: true);
                    Reply(state, id, 0, w => index.Write(w, withOre: true));
                    log.Info($"Zone {state.Name} (world {state.World}) linked: index of {index.Landscapes.Values.Sum(l => l.Count)} entries sent.");
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.VersionIndexSub):
                {
                    EnvIndex index = Snapshot(withOre: false);
                    Reply(state, id, 0, w => index.Write(w, withOre: false));
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.KeepOutSub):
                {
                    bool first = !keepOutKnown;
                    keepOutBy[$"{state.World}/{state.Name}"] = EnvKeepOut.Read(r);
                    MergeKeepOut();
                    if (first)
                        log.Info($"Build-site boxes from {state.Name}: {sites.Values.Sum(s => s.Count)} site(s), "
                            + $"{noVeins.Values.Sum(s => s.Count)} no-vein box(es); heals and veins run from now.");
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.PositionsSub):
                {
                    var positions = new List<(uint, Vector3)>();
                    for (uint n = EnvWire.Count(r, 16); n > 0; n--)
                    {
                        uint z = r.U32();
                        positions.Add((z, new Vector3(r.F32(), r.F32(), r.F32())));
                        landscapes.Add(Key(z));
                    }
                    state.Positions = positions;
                    return -1;
                }

                // ---- cells ------------------------------------------------------------------------------------------------
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.CellFetchSub):
                {
                    bool always = r.Bool();
                    List<ulong> cells = EnvWire.Cells(r);
                    IReadOnlyList<byte[]> updates = env.BuildCellsUpdates(cells, zone, always);
                    Reply(state, id, 0, w =>
                    {
                        w.U32((uint)cells.Count);
                        for (int i = 0; i < cells.Count; i++)
                        {
                            w.U64(cells[i]);
                            EnvWire.Blob(w, updates[i]);
                        }
                    });
                    return -1;
                }

                // ---- build edits ------------------------------------------------------------------------------------------
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ApplyDiffSub):
                {
                    EnvApplyEdit request = EnvApplyEdit.Read(r);
                    var edit = new EditRequest
                    {
                        Zone = zone, Owner = request.Owner, Compare = request.Compare, Budget = EnvironmentWire.FromWire(request.Budget)
                    };
                    foreach ((ulong cell, bool has, uint b, uint n, byte[] packed) in request.Cells)
                    {
                        byte[] raw = TerrainStore.Unpack(packed, 0, packed.Length);
                        var changed = new byte[BlockData.Count / 8];
                        Array.Copy(raw, 0, changed, 0, changed.Length);
                        edit.Cells.Add(new EditCell
                        {
                            Cell = cell, Changed = changed, Result = BlockData.Deserialize(raw, changed.Length), HasVersions = has, BaseVersion = b,
                            NewVersion = n
                        });
                    }
                    EditResult result = env.ApplyEdit(edit);
                    Publish(zone, result.Cells.Select(c => c.Cell), EnvChangeReason.Request, state);
                    var answer = new EnvApplyEdit.Answer
                    {
                        Delta = EnvironmentWire.ToWire(result.Delta), Stale = result.Stale, Voxels = result.Voxels,
                        CompareReport = result.CompareReport ?? "", Cells = EnvironmentWire.ToWire(result.Cells)
                    };
                    Reply(state, id, result.Result, answer.Write);
                    return -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.WriteCellsSub):
                {
                    EnvWriteCells request = EnvWriteCells.Read(r);
                    WriteResult result = env.WriteCells(zone, request.Owner,
                        request.Cells.Select(c => (c.Cell, EnvironmentWire.FromWire(c.Values))).ToList(),
                        EnvironmentWire.FromWire(request.Budget), request.ReturnValues);
                    if (result.Result == EnvResult.Ok)
                        Publish(zone, request.Cells.Select(c => c.Cell), EnvChangeReason.Request, state);
                    Reply(state, id, result.Result, w =>
                    {
                        EnvWire.Delta(w, EnvironmentWire.ToWire(result.Delta));
                        EnvWire.CellValues(w, EnvironmentWire.ToWire(result.Cells));
                    });
                    return request.JobType == JobCounters.Paste ? JobCounters.Paste : -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ClearCellsSub):
                {
                    EnvClearCells request = EnvClearCells.Read(r);
                    ClearResult result = env.ClearCells(zone, request.Owner, request.Cells);
                    Publish(zone, result.Cells.Select(c => c.Cell), EnvChangeReason.Request, state);
                    Reply(state, id, 0, w =>
                    {
                        EnvWire.Delta(w, EnvironmentWire.ToWire(result.Delta));
                        w.U32((uint)result.Voxels);
                        EnvWire.CellValues(w, EnvironmentWire.ToWire(result.Cells));
                    });
                    return -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ReadSub):
                {
                    uint layer = r.U32();
                    ulong cell = r.U64();
                    byte[] mask = EnvWire.Packed(r);
                    List<TerrainEdits.VoxelValue> values = env.Read(cell, mask, zone, layer);
                    Reply(state, id, 0, w => EnvWire.Voxels(w, EnvironmentWire.ToWire(values)));
                    return -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.WriteSub):
                {
                    uint layer = r.U32();
                    ulong owner = r.U64(), cell = r.U64();
                    env.Write(cell, EnvironmentWire.FromWire(EnvWire.Voxels(r)), zone, owner, layer);
                    Publish(zone, new[] { cell }, EnvChangeReason.Request, state);
                    Reply(state, id, 0);
                    return -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.MaterialDeltaSub):
                {
                    uint layer = r.U32();
                    ulong cell = r.U64();
                    Dictionary<ushort, int> delta = env.MaterialDelta(cell, EnvironmentWire.FromWire(EnvWire.Voxels(r)), zone, layer);
                    Reply(state, id, 0, w => EnvWire.Delta(w, EnvironmentWire.ToWire(delta)));
                    return -1;
                }

                // ---- digs, box operations, heals --------------------------------------------------------------------------
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.SphereSub):
                {
                    EnvSphere s = EnvSphere.Read(r);
                    Dictionary<ushort, int> ore = s.CountOre ? new() : null;
                    HashSet<ulong> oreCells = s.CountOre ? new() : null;
                    (List<ulong> cells, int voxels) = env.Sphere(new Vector3(s.X, s.Y, s.Z), s.Radius, s.Material, zone, ore, oreCells, s.Shell,
                        EnvironmentWire.FromWire(s.Filter), s.CarveBuilds, s.HealSeconds is double h ? TimeSpan.FromSeconds(h) : null);
                    Publish(zone, cells.Concat(oreCells ?? Enumerable.Empty<ulong>()), EnvChangeReason.Request, state);
                    Reply(state, id, 0, w =>
                    {
                        EnvWire.Cells(w, cells);
                        w.U32((uint)voxels).U32((uint)(ore?.Count ?? 0));
                        foreach ((ushort material, int n) in ore ?? new())
                            w.U16(material).U32((uint)n).U32(env.OreItemFor(material));
                        EnvWire.Cells(w, oreCells?.ToList() ?? new List<ulong>());
                    });
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DestroyAreaSub):
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.HealAreaSub):
                {
                    var centre = (EnvWire.F64(r), EnvWire.F64(r), EnvWire.F64(r));
                    double radius = EnvWire.F64(r), half = EnvWire.F64(r);
                    (List<ulong> cells, int voxels) = sub == EnvironmentMessages.DestroyAreaSub
                        ? env.DestroyArea(zone, centre, radius, half, EnvironmentWire.FromWire(EnvFilter.Read(r)))
                        : env.HealArea(zone, centre, radius, half);
                    Publish(zone, cells, EnvChangeReason.Request, state);
                    Reply(state, id, 0, w =>
                    {
                        EnvWire.Cells(w, cells);
                        w.U32((uint)voxels);
                    });
                    return -1;
                }
                case (EnvironmentMessages.ClaimsFamily, EnvironmentMessages.RevertTerrainSub):
                {
                    double[] b = EnvWire.Box(r);
                    List<ulong> cells = env.RevertBox(b[0], b[1], b[2], b[3], b[4], b[5], zone);
                    Publish(zone, cells, EnvChangeReason.Request, state);
                    Reply(state, id, 0, w => EnvWire.Cells(w, cells));
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.ClearCellSub):
                {
                    ulong cell = r.U64();
                    env.ClearCell(cell, zone);
                    Publish(zone, new[] { cell }, EnvChangeReason.Request, state);
                    Reply(state, id, 0);
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.RegenerationDelaySub):
                    env.SetRegenerationDelay(TimeSpan.FromSeconds(EnvWire.F64(r)));
                    log.Info($"Heal delay set to {TerrainEdits.RegenerationDelay} by {state.Name}.");
                    Reply(state, id, 0);
                    return -1;
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.MarkPermanentSub):
                    env.MarkPermanent(EnvWire.Cells(r), zone);
                    Reply(state, id, 0);
                    return -1;
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.CompactSub):
                    env.CompactNow(r.Bool());
                    Reply(state, id, 0);
                    return -1;
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.SampleVersionSub):
                {
                    ulong cell = r.U64();
                    env.SampleVersion(cell, zone, new[] { r.U32(), r.U32(), r.U32(), r.U32() });
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.MemoryReportSub):
                {
                    string report = env.MemoryReport();
                    Reply(state, id, 0, w => w.Str(report));
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DescribeSub):
                {
                    string text = env.Describe(new Vector3(r.F32(), r.F32(), r.F32()), zone);
                    Reply(state, id, 0, w => w.Str(text ?? ""));
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.RefreshLodSub):
                    env.RefreshLodCopies(zone);
                    Reply(state, id, 0);
                    return -1;

                // ---- stamps ----------------------------------------------------------------------------------------------
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.SpawnTemplateSub):
                    Stamp(state, id, zone, EnvStamp.Read(r));
                    return -1;

                // ---- ownership -------------------------------------------------------------------------------------------
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.OwnershipSub):
                {
                    var cells = new List<(ulong, uint)>();
                    for (uint n = EnvWire.Count(r, 12); n > 0; n--)
                        cells.Add((r.U64(), r.U32()));
                    var (upToDate, list) = env.OwnershipQuery(zone, cells);
                    Reply(state, id, 0, w => EnvOwnership.WriteAnswer(w, upToDate, list));
                    return -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.OwnedBySub):
                {
                    ulong owner = r.U64();
                    double[] b = EnvWire.Box(r);
                    var owned = env.OwnedBy(zone, owner, new VoxelBox(b[0], b[1], b[2], b[3], b[4], b[5]));
                    Reply(state, id, 0, w =>
                    {
                        w.U32((uint)owned.Count);
                        foreach ((ulong cell, List<int> indices) in owned)
                        {
                            var mask = new byte[BlockData.Count / 8];
                            foreach (int i in indices)
                                mask[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                            w.U64(cell);
                            EnvWire.Packed(w, mask);
                        }
                    });
                    return -1;
                }
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.CensusSub):
                {
                    Dictionary<ulong, int> census = env.OwnershipCensus(zone, r.U64());
                    Reply(state, id, 0, w =>
                    {
                        w.U32((uint)census.Count);
                        foreach ((ulong owner, int n) in census)
                            w.U64(owner).U32((uint)n);
                    });
                    return -1;
                }

                // ---- creature navigation (OPEN-LEADS L133) --------------------------------------------------------------
                case (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.SolidityOfSub):
                {
                    double[] b = EnvWire.Box(r);
                    SolidityGrid grid = env.SolidityOf(zone, new VoxelBox(b[0], b[1], b[2], b[3], b[4], b[5]));
                    Reply(state, id, 0, w =>
                    {
                        w.Bool(grid != null);
                        grid?.Write(w);
                    });
                    return -1;
                }

                // ---- ore veins -------------------------------------------------------------------------------------------
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.EnsureVeinsSub):
                {
                    List<ulong> made = keepOutKnown ? env.EnsureVeinsAround(zone, new Vector3(r.F32(), r.F32(), r.F32())) : new();
                    Publish(zone, made, EnvChangeReason.Request, state);
                    Reply(state, id, 0, w => EnvWire.Cells(w, made));
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DisturbVeinsSub):
                    env.DisturbVeins(EnvWire.Cells(r), zone);
                    Reply(state, id, 0);
                    return -1;
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.ClearVeinsSub):
                {
                    double[] b = EnvWire.Box(r);
                    List<ulong> changed = env.ClearVeinsInSite(zone, (b[0], b[1], b[2]), (b[3], b[4], b[5]));
                    Publish(zone, changed, EnvChangeReason.Request, state);
                    Reply(state, id, 0, w => EnvWire.Cells(w, changed));
                    return -1;
                }
                case (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.VeinsNearSub):
                {
                    var at = new Vector3(r.F32(), r.F32(), r.F32());
                    var near = env.VeinsNear(at, r.F32(), zone);
                    Reply(state, id, 0, w =>
                    {
                        w.U32((uint)near.Count);
                        foreach ((OreVeins.Vein vein, Vector3 nearest, float distance) in near)
                            new EnvVein(vein.Id, vein.Info.MappingId, vein.Item, vein.Info.Definition?.Name ?? "", vein.Cave, vein.Region.X, vein.Region.Z,
                                vein.Points.Select(p => (p.X, p.Y, p.Z, p.Width, p.Seed)).ToList(), vein.Cells, nearest.X, nearest.Y, nearest.Z, distance).Write(w);
                    });
                    return -1;
                }

                // ---- the admin monitor's numbers ------------------------------------------------------------------------
                case (EnvironmentMessages.CommandFamily, EnvironmentMessages.MonitorRequestSub):
                {
                    EnvMetrics metrics = jobs.Metrics(ruins.Rows());
                    Reply(state, id, 0, metrics.Write);
                    return -1;
                }

                default:
                    log.Warn($"Unknown request 0x{family:X}/0x{sub:X} from {state.Name}.");
                    Reply(state, id, EnvironmentMessages.ResultFailed);
                    return -1;
            }
        }

        /// <summary>One loaded template kept for the next stamp (an outpost ruin is stamped for several copies in a row).</summary>
        private (string Name, VoxelTemplate Template) lastTemplate;

        private void Stamp(LinkState state, uint id, uint zone, EnvStamp request)
        {
            VoxelTemplate template;
            string name;
            if (request.Source == EnvStamp.FromFile)
            {
                // Only a file name: read from this service's own copy of the game's templates folder.
                name = Path.GetFileName(request.Name);
                if (lastTemplate.Name == name)
                    template = lastTemplate.Template;
                else
                {
                    lastTemplate = default;
                    template = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, name));
                }
            }
            else
            {
                name = request.Name;
                template = VoxelTemplate.Load(new MemoryStream(request.Otd));
            }
            List<ulong> cells, changed;
            if (request.Mode == EnvStamp.CellCorner)
            {
                changed = env.StampTemplate(template, (uint)request.X, (uint)request.Y, (uint)request.Z, zone);
                cells = changed;
                VoxelTemplate.LastStampReport = $"{changed.Count} cell(s) changed";
            }
            else
                (cells, changed) = env.StampTemplateAt(template, request.X, request.Y, request.Z, zone,
                    request.Mode == EnvStamp.VoxelCornerTurned ? request.Turns : 0);
            env.Flush();
            env.CompactNow(true);
            lastTemplate = request.Source == EnvStamp.FromFile ? (name, template) : default;
            string corner = request.Mode switch
            {
                EnvStamp.CellCorner => "cell",
                EnvStamp.VoxelCornerTurned => $"voxel+{request.Turns * 90}",
                _ => "voxel"
            };
            ruins.Stamped(name, Key(zone), corner, request.X, request.Y, request.Z);
            Publish(zone, changed, EnvChangeReason.Request, state);
            string report = VoxelTemplate.LastStampReport;
            log.Info($"Stamped {name} on landscape {Key(zone)} at {corner} "
                + $"({request.X}, {request.Y}, {request.Z}) for {state.Name}: {report}.");
            Reply(state, id, 0, w =>
            {
                EnvWire.Cells(w, cells);
                EnvWire.Cells(w, changed);
                w.Str(report ?? "");
            });
            // A decoded ruin is large (~0.4 MB per cell): drop it after a quiet minute.
            VoxelTemplate kept = lastTemplate.Template;
            if (kept != null)
                System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => work.Add(() =>
                {
                    if (lastTemplate.Template == kept)
                    {
                        lastTemplate = default;
                        GC.Collect(2, GCCollectionMode.Optimized, false, true);
                    }
                }));
        }
    }
}
