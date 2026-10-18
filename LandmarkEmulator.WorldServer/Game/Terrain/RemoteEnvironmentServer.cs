using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Environment;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The Environment Server as a remote container (LandmarkEmulator.EnvServer; terrain-service.md §5, §6, §7 steps 3-6),
    /// chosen by ENVSERVER_ADDRESS (<see cref="EnvironmentServer.Configure"/>). The zone dials the service
    /// (<see cref="InternalDialer"/>, reconnecting by itself) and every <see cref="IEnvironmentServer"/> call becomes one
    /// request / answer (LandmarkEmulator.Internal/Environment/EnvironmentMessages.cs), waited for on the calling (world)
    /// thread — Building and the other callers read the answers synchronously, as they always did.
    /// <para>
    /// <b>No voxel mirror</b> (spec §5.5): the zone keeps only
    /// <list type="bullet">
    /// <item>the <b>version index</b> (spec §5.3 item 1): every (landscape, cell, layer) the service holds with its version,
    /// from Hello's snapshot and the CellVersionsChanged pushes (one sequence number; a gap → the whole index again).
    /// <see cref="Has"/>, <see cref="LayerVersion"/>, <see cref="LayerVersions"/>, <see cref="CurrentVersion"/> answer
    /// from it, and <see cref="BuildManifest"/> builds the zone-in manifest from it (TerrainStore.BuildManifest with the
    /// index's entries) without a round trip — the re-zone rule sends it right after ClientBeginZoning;</item>
    /// <item>a <b>cache of finished CellsUpdate bodies</b> (spec §5.3 item 3): key (landscape, cell), kept with the layer
    /// versions it carries, used only while the index still has those versions, 64 MB at most (least recently used out
    /// first). Many players fetching the same Spire / outpost / site cells hit it.</item>
    /// </list>
    /// Everything that writes is one transaction in the service with the zone's checks done first (permissions) or sent
    /// along (the material budget, the destruction filter).
    /// </para>
    /// <para>
    /// <b>Pushes</b> by reason: heals queue for <see cref="Regenerate"/> and vein changes for <see cref="UpdateVeins"/> (so
    /// ZoneTerrain sends them exactly as in-process), LOD refreshes raise <see cref="LodCopiesChanged"/>, another zone's
    /// edits and changes found by the service's periodic comparison go to the players through ZoneTerrain.Broadcast; all of
    /// these run on the world thread in <see cref="Update"/>.
    /// </para>
    /// <para>
    /// <b>While the service is away</b> (spec §6; <see cref="Available"/> false): cell requests are answered from the cache
    /// or "up to date" (TerrainHandler remembers the cell and sends a manifest note when the service is back); edits,
    /// undo, paste and the Heal tool are refused (Building: Response 1 + LostClaimServerConnection 0xF9/0x12, text 562155); Voxel Vision
    /// answers every cell up to date; digs change nothing and yield nothing; heals, veins and LOD pause in the service
    /// itself. A site revert or vein clear asked for meanwhile is kept and sent on reconnect; permanent-cell marks are
    /// kept and sent again on every connect (the service holds them in memory only). A stamp throws
    /// <see cref="EnvironmentUnavailableException"/>, so no placement marker is written for a stamp that never happened.
    /// </para>
    /// </summary>
    public sealed class RemoteEnvironmentServer : IEnvironmentServer, IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>How long a call waits for its answer: edits and stamps get longer (a 1,024-cell edit, a ruin).</summary>
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5), EditTimeout = TimeSpan.FromSeconds(20),
            StampTimeout = TimeSpan.FromMinutes(3);
        /// <summary>Cells per fetch (a CellsUpdate body is up to ~75 KB; the frame limit is 16 MB).</summary>
        private const int FetchBatch = 64;
        /// <summary>The CellsUpdate body cache's size limit (spec §5.3: ≈ 6,000 cells at the measured mean).</summary>
        private const long CacheLimit = 64L * 1024 * 1024;
        private static readonly TimeSpan KeepOutInterval = TimeSpan.FromSeconds(5), PositionInterval = TimeSpan.FromSeconds(2);

        private readonly string address;
        private readonly uint worldId;
        private readonly InternalDialer dialer;
        private readonly Func<IEnumerable<(uint Zone, double[] Box)>> siteBoxes, noVeinBoxes;
        private readonly ManualResetEventSlim firstSync = new(false);

        private readonly object sync = new();
        private InternalLink link;
        private volatile bool synced;
        /// <summary>This link's snapshot is in (pushes before it are covered by it and skipped); under indexSync.</summary>
        private bool indexReady;
        private bool resyncing;
        private ulong sequence;
        private readonly Dictionary<uint, TaskCompletionSource<(uint Result, InternalReader Reader)>> pending = new();
        private int nextRequest;

        // ---- the version index (spec §5.3) -----------------------------------------------------------------------------
        private readonly object indexSync = new();
        private Dictionary<uint, Dictionary<ulong, uint[]>> index = new();
        private readonly Dictionary<uint, int> generation = new();
        private readonly Dictionary<uint, (int Generation, byte[] Body)> manifests = new();
        private readonly ConcurrentDictionary<ushort, uint> oreItems = new();
        private readonly PayloadCache cache = new(CacheLimit);

        // ---- world-thread work ------------------------------------------------------------------------------------------
        private readonly ConcurrentQueue<(uint Zone, List<ulong> Cells)> healed = new(), veins = new(), others = new();
        private readonly ConcurrentQueue<(uint Zone, uint Layer, List<(ulong Cell, uint Version)> Copies)> lod = new();
        private readonly ConcurrentQueue<Action> worldQueue = new();
        /// <summary>Site reverts and vein clears asked for while away, sent on reconnect (world thread).</summary>
        private readonly List<Action> deferred = new();
        /// <summary>Every permanent-cell mark so far (the service keeps them in memory): sent again on every connect.</summary>
        private readonly Dictionary<uint, HashSet<ulong>> permanent = new();
        private byte[] lastKeepOut;
        private DateTime nextKeepOut, nextPositions;

        public event Action<uint, uint, IReadOnlyList<(ulong Cell, uint Version)>> LodCopiesChanged;

        /// <param name="siteBoxes">Every build site's box (global dm) with its landscape (Claims.AllBoxes).</param>
        /// <param name="noVeinBoxes">Every place without ore veins: the Spire platforms (Spire.PlatformBoxes).</param>
        public RemoteEnvironmentServer(string address, uint worldId, Func<IEnumerable<(uint Zone, double[] Box)>> siteBoxes,
            Func<IEnumerable<(uint Zone, double[] Box)>> noVeinBoxes)
        {
            this.address = address;
            this.worldId = worldId;
            this.siteBoxes = siteBoxes;
            this.noVeinBoxes = noVeinBoxes;
            dialer = new InternalDialer(address);
            dialer.Connected += OnConnected;
        }

        /// <summary>Dials the service and waits up to <paramref name="wait"/> for its index (start-up stamps need it).</summary>
        public void Start(TimeSpan wait)
        {
            dialer.Start();
            log.Info($"Environment Server: remote at {address}; waiting up to {wait.TotalSeconds:0} s for it.");
            if (!firstSync.Wait(wait))
                log.Error($"Environment Server at {address} not reached in {wait.TotalSeconds:0} s: terrain is read-only from the "
                    + "cache until it is (building refused, digs do nothing); stamps that are due fail.");
        }

        public bool Available => synced && link is { IsOpen: true };

        public string LinkState => $"environment server at {address}, {(Available ? "connected" : "not connected")}";

        public void Dispose() => dialer.Dispose();

        private static uint Key(uint zone) => zone == 0 ? TerrainStore.HomeContinent : zone;

        // ---- the link -----------------------------------------------------------------------------------------------------

        private void OnConnected(InternalLink newLink)
        {
            newLink.Received += OnReceived;
            newLink.Closed += (l, error) =>
            {
                lock (sync)
                {
                    if (link == l)
                    {
                        link = null;
                        synced = false;
                    }
                    foreach (var waiting in pending.Values)
                        waiting.TrySetResult((uint.MaxValue, null));
                    pending.Clear();
                }
                lock (indexSync)
                {
                    indexReady = false;
                    resyncing = false;
                }
                log.Warn($"Environment Server link lost{(error != null ? ": " + error.Message : "")}; terrain is read-only until it is back.");
            };
            lock (sync)
                link = newLink;
            // The link starts reading after this returns: the hello waits for its answer on another thread.
            Task.Run(() => Handshake(newLink));
        }

        private void Handshake(InternalLink newLink)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.HelloSub, 0,
                w => w.U32(worldId).Str($"world {worldId} zone"), TimeSpan.FromSeconds(60), requireSync: false);
            if (!ok || result != 0)
            {
                log.Error("Environment Server hello not answered; closing the link to try again.");
                newLink.Close();
                return;
            }
            EnvIndex snapshot = EnvIndex.Read(r, withOre: true);
            foreach ((ushort material, uint item) in snapshot.OreItems)
                oreItems[material] = item;
            ApplySnapshot(snapshot);
            // The service keeps permanent marks in memory only: every one again, before the keep-out boxes that let its
            // heal run (one work thread there, so the order holds).
            List<(uint Zone, List<ulong> Cells)> marks;
            lock (permanent)
                marks = permanent.Select(p => (p.Key, p.Value.ToList())).ToList();
            foreach ((uint zone, List<ulong> cells) in marks)
                Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.MarkPermanentSub, zone, w => EnvWire.Cells(w, cells), requireSync: false);
            lastKeepOut = null;
            nextKeepOut = DateTime.MinValue;
            SendKeepOut(force: true);
            synced = true;
            firstSync.Set();
            worldQueue.Enqueue(Reconnected);
            log.Info($"Environment Server linked: {snapshot.Landscapes.Values.Sum(l => l.Count)} index entries on "
                + $"{snapshot.Landscapes.Count} landscape(s), sequence {snapshot.Sequence}.");
        }

        private void ApplySnapshot(EnvIndex snapshot)
        {
            var fresh = new Dictionary<uint, Dictionary<ulong, uint[]>>();
            foreach ((uint zone, var entries) in snapshot.Landscapes)
            {
                var cells = new Dictionary<ulong, uint[]>(entries.Count);
                foreach ((ulong cell, byte layer, uint version) in entries)
                {
                    if (!cells.TryGetValue(cell, out uint[] v))
                        cells[cell] = v = new uint[4];
                    if (layer < 4)
                        v[layer] = version;
                }
                fresh[Key(zone)] = cells;
            }
            lock (indexSync)
            {
                index = fresh;
                foreach (uint zone in fresh.Keys.Concat(generation.Keys).ToList())
                    generation[zone] = generation.GetValueOrDefault(zone) + 1;
                sequence = snapshot.Sequence;
                resyncing = false;
                indexReady = true;
            }
        }

        /// <summary>Runs on the link's reading task: index updates and queues only.</summary>
        private void OnReceived(InternalLink from, IInternalMessage message)
        {
            if (message is not ServicePacket packet)
                return;
            if (EnvironmentMessages.IsReply(packet))
            {
                (uint id, uint result, InternalReader r) = EnvironmentMessages.ReadReply(packet);
                TaskCompletionSource<(uint, InternalReader)> waiting;
                lock (sync)
                    if (pending.Remove(id, out waiting))
                        waiting.TrySetResult((result, r));
                return;
            }
            if (packet.Family == EnvironmentMessages.TerrainCellFamily && packet.Sub == EnvironmentMessages.CellVersionsChangedSub)
            {
                Changed(CellVersionsChanged.Read(packet));
                return;
            }
            if (ServiceStatuses.TryTake(packet))
                return;
            log.Debug($"Environment Server message 0x{packet.Family:X}/0x{packet.Sub:X} not expected here.");
        }

        private void Changed(CellVersionsChanged change)
        {
            uint zone = Key(change.Landscape);
            bool gap = false;
            lock (indexSync)
            {
                // Before this link's snapshot: its answer covers these (the service sends them first).
                if (!indexReady)
                    return;
                if (change.Sequence <= sequence)
                    return;
                gap = change.Sequence != sequence + 1 && !resyncing;
                sequence = change.Sequence;
                if (!index.TryGetValue(zone, out var cells))
                    index[zone] = cells = new Dictionary<ulong, uint[]>();
                foreach ((ulong cell, uint code, uint version, byte _) in change.Entries)
                {
                    if (code < 1 || code > 4)
                        continue;
                    if (!cells.TryGetValue(cell, out uint[] v))
                        cells[cell] = v = new uint[4];
                    v[code - 1] = version;
                    if (v.All(x => x == 0))
                        cells.Remove(cell);
                }
                generation[zone] = generation.GetValueOrDefault(zone) + 1;
                if (gap)
                    resyncing = true;
            }
            List<ulong> changedCells = change.Entries.Select(e => e.Cell).Distinct().ToList();
            switch (change.Reason)
            {
                case EnvChangeReason.Heal:
                    healed.Enqueue((zone, changedCells));
                    break;
                case EnvChangeReason.Veins:
                    veins.Enqueue((zone, changedCells));
                    break;
                case EnvChangeReason.Lod:
                    foreach (var byLayer in change.Entries.GroupBy(e => e.Code - 1))
                        lod.Enqueue((zone, byLayer.Key, byLayer.Select(e => (e.Cell, e.Version)).ToList()));
                    break;
                case EnvChangeReason.OtherZone:
                case EnvChangeReason.Reconcile:
                    others.Enqueue((zone, changedCells));
                    break;
            }
            if (gap)
            {
                log.Warn($"Environment Server sequence gap at {change.Sequence}: fetching the whole version index again.");
                Task.Run(Resync);
            }
        }

        private void Resync()
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.VersionIndexSub, 0, null,
                TimeSpan.FromSeconds(60), requireSync: false);
            if (ok && result == 0)
                ApplySnapshot(EnvIndex.Read(r, withOre: false));
            else
                lock (indexSync)
                    resyncing = false;
        }

        // ---- calls ----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// One request and its answer, waited for here. False (nothing done, as far as the zone can tell) when the service
        /// is away or does not answer in time.
        /// </summary>
        private (bool Ok, uint Result, InternalReader Reader) Call(ushort family, uint sub, uint zone, Action<InternalWriter> payload,
            TimeSpan timeout, bool requireSync = true)
        {
            InternalLink current;
            uint id;
            var waiting = new TaskCompletionSource<(uint Result, InternalReader Reader)>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync)
            {
                current = link;
                if (current == null || !current.IsOpen || (requireSync && !synced))
                    return (false, 0, null);
                id = (uint)Interlocked.Increment(ref nextRequest);
                if (id == 0)
                    id = (uint)Interlocked.Increment(ref nextRequest);
                pending[id] = waiting;
            }
            if (!current.Send(EnvironmentMessages.Request(family, sub, id, zone, payload)))
            {
                lock (sync)
                    pending.Remove(id);
                return (false, 0, null);
            }
            if (!waiting.Task.Wait(timeout))
            {
                lock (sync)
                    pending.Remove(id);
                log.Warn($"Environment Server did not answer 0x{family:X}/0x{sub:X} within {timeout.TotalSeconds:0} s.");
                return (false, 0, null);
            }
            (uint result, InternalReader reader) = waiting.Task.Result;
            return reader == null ? (false, 0, null) : (true, result, reader);
        }

        /// <summary>A request without an answer (request id 0); false when it could not be sent.</summary>
        private bool Post(ushort family, uint sub, uint zone, Action<InternalWriter> payload, bool requireSync = true)
        {
            InternalLink current;
            lock (sync)
            {
                current = link;
                if (current == null || !current.IsOpen || (requireSync && !synced))
                    return false;
            }
            return current.Send(EnvironmentMessages.Request(family, sub, 0, zone, payload));
        }

        // ---- world thread ------------------------------------------------------------------------------------------------

        /// <summary>
        /// World tick (HostedService "environment server link"): pushes that reach players (another zone's edits, the
        /// periodic comparison, LOD notes), the keep-out boxes when they change, and the work queued at a reconnect.
        /// </summary>
        public void Update()
        {
            while (worldQueue.TryDequeue(out Action action))
                action();
            while (others.TryDequeue(out var other))
                ZoneTerrain.Broadcast(other.Cells, other.Zone);
            while (lod.TryDequeue(out var copies))
                LodCopiesChanged?.Invoke(copies.Zone, copies.Layer, copies.Copies);
            if (DateTime.UtcNow >= nextKeepOut)
                SendKeepOut(force: false);
        }

        /// <summary>Back after a loss: what waited is sent, and players told about cells they were answered "up to date" for.</summary>
        private void Reconnected()
        {
            List<Action> waiting;
            lock (deferred)
            {
                waiting = deferred.ToList();
                deferred.Clear();
            }
            foreach (Action action in waiting)
                action();
            ZoneTerrain.PayOwedNotes();
        }

        /// <summary>The build-site and no-vein boxes, sent whole when they differ from the last ones (spec §5.2).</summary>
        private void SendKeepOut(bool force)
        {
            nextKeepOut = DateTime.UtcNow + KeepOutInterval;
            var keepOut = new EnvKeepOut();
            try
            {
                foreach ((uint zone, double[] box) in siteBoxes())
                    (keepOut.Landscapes.TryGetValue(zone, out var l) ? l : keepOut.Landscapes[zone] = (new(), new())).Sites.Add(box);
                foreach ((uint zone, double[] box) in noVeinBoxes())
                    (keepOut.Landscapes.TryGetValue(zone, out var l) ? l : keepOut.Landscapes[zone] = (new(), new())).NoVeins.Add(box);
            }
            catch (InvalidOperationException)
            {
                return;   // a collection changed while read: next time
            }
            var w = new InternalWriter();
            keepOut.Write(w);
            byte[] bytes = w.ToArray();
            if (!force && lastKeepOut != null && bytes.AsSpan().SequenceEqual(lastKeepOut))
                return;
            if (Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.KeepOutSub, 0, x => x.Bytes(bytes), requireSync: false))
                lastKeepOut = bytes;
        }

        // ---- start-up and upkeep: the service does its own -----------------------------------------------------------------

        public void Load(string terrainEditsFolder)
            => log.Info($"Terrain edits are the Environment Server's ({address}); this world's {terrainEditsFolder} keeps only its placement markers.");
        public void LoadVeins(string gameDirectory, uint continent) { }
        public void Flush() { }
        public void MigrateToLayers() { }
        public void BuildLodCopies() { }
        public void UpdateLodCopies() { }

        public void CompactNow(bool all)
            => Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.CompactSub, 0, w => w.Bool(all));

        public string MemoryReport()
        {
            (bool ok, _, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.MemoryReportSub, 0, null, CallTimeout);
            return $"Environment Server {(ok ? r.Str() : "unreachable")}; zone cache {cache.Report()}";
        }

        // ---- cells ------------------------------------------------------------------------------------------------------------

        private uint[] Versions(ulong cell, uint zone)
        {
            lock (indexSync)
                return index.TryGetValue(Key(zone), out var cells) && cells.TryGetValue(cell, out uint[] v) ? (uint[])v.Clone() : null;
        }

        public bool Has(ulong cell, uint zone) => Versions(cell, zone) != null;
        public bool HasCapturedCell(ulong cell) => TerrainStore.Get(cell) != null;
        public uint LayerVersion(ulong cell, uint zone, uint layer) => layer < 4 ? Versions(cell, zone)?[layer] ?? 0 : 0;

        public uint[] LayerVersions(ulong cell, uint zone)
        {
            uint[] v = Versions(cell, zone);
            var versions = new uint[TerrainEdits.LayerCount];
            if (v != null)
                Array.Copy(v, versions, versions.Length);
            return versions;
        }

        public uint CurrentVersion(ulong cell, uint zone)
        {
            uint[] v = Versions(cell, zone);
            uint sum = 0;
            for (int layer = 0; v != null && layer < TerrainEdits.LayerCount; layer++)
                sum += v[layer];
            return sum;
        }

        public byte[] BuildCellsUpdate(ulong cell, uint zone, bool always = false) => BuildCellsUpdates(new[] { cell }, zone, always)[0];

        public IReadOnlyList<byte[]> BuildCellsUpdates(IReadOnlyList<ulong> cells, uint zone, bool always = false)
        {
            var updates = new byte[cells.Count][];
            var missing = new List<int>();
            for (int i = 0; i < cells.Count; i++)
            {
                uint[] versions = Versions(cells[i], zone);
                if (versions == null)
                {
                    // Nothing held there: as in-process, null, or with "always" every layer empty (undoes a refused edit).
                    if (always && TerrainStore.Decode(cells[i]).Level == TerrainEdits.Level)
                        updates[i] = EmptyUpdate(cells[i]);
                    continue;
                }
                if (cache.TryGet(Key(zone), cells[i], versions, out byte[] body))
                    updates[i] = body;
                else
                    missing.Add(i);
            }
            for (int start = 0; start < missing.Count; start += FetchBatch)
            {
                List<int> batch = missing.Skip(start).Take(FetchBatch).ToList();
                (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.CellFetchSub, zone, w =>
                {
                    w.Bool(always);
                    EnvWire.Cells(w, batch.Select(i => cells[i]).ToList());
                }, CallTimeout);
                if (!ok || result != 0)
                    break;   // away: these stay null (callers answer from what they have, spec §6)
                uint n = r.U32();
                for (int k = 0; k < n && k < batch.Count; k++)
                {
                    ulong cell = r.U64();
                    byte[] body = EnvWire.Blob(r);
                    if (body.Length == 0)
                        continue;
                    updates[batch[k]] = body;
                    // The index is current at this point (the service sends version changes before its answers).
                    if (Versions(cell, zone) is uint[] now)
                        cache.Put(Key(zone), cell, now, body);
                }
            }
            return updates;
        }

        private static byte[] emptyLayer;

        /// <summary>A CellsUpdate with every layer empty (version 0): what TerrainEdits.BuildUpdate gives for an empty cell with "always".</summary>
        private static byte[] EmptyUpdate(ulong cell)
        {
            emptyLayer ??= new BlockData().ToLayerBlob();
            var layers = new List<(uint, uint, byte[])>();
            for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                layers.Add((layer, 0u, emptyLayer));
            return TerrainStore.BuildBlocksUpdate(cell, layers);
        }

        public byte[] BuildCapturedCellsUpdate(IEnumerable<ulong> cells) => TerrainStore.BuildCellsUpdate(cells);

        public byte[] BuildManifest(uint zone)
        {
            uint key = Key(zone);
            int gen;
            lock (indexSync)
            {
                gen = generation.GetValueOrDefault(key);
                if (manifests.TryGetValue(key, out var built) && built.Generation == gen)
                    return built.Body;
            }
            byte[] body = TerrainStore.BuildManifest(zone, (z, layer) =>
            {
                lock (indexSync)
                    return index.TryGetValue(Key(z), out var cells)
                        ? cells.Where(c => c.Value[layer] != 0).Select(c => (c.Key, c.Value[layer])).ToList()
                        : new List<(ulong, uint)>();
            });
            lock (indexSync)
                manifests[key] = (gen, body);
            return body;
        }

        public void SampleVersion(ulong cell, uint zone, uint[] held)
            => Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.SampleVersionSub, zone, w =>
            {
                w.U64(cell);
                for (int i = 0; i < 4; i++)
                    w.U32(i < held.Length ? held[i] : 0);
            });

        public string Describe(Vector3 local, uint zone)
        {
            (bool ok, _, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DescribeSub, zone,
                w => w.F32(local.X).F32(local.Y).F32(local.Z), CallTimeout);
            return ok ? r.Str() : "Environment Server unreachable";
        }

        // ---- build edits, undo / redo, pastes ------------------------------------------------------------------------------

        public EditResult ApplyEdit(EditRequest request)
        {
            var message = new EnvApplyEdit
            {
                Owner = request.Owner, Compare = request.Compare, Budget = EnvironmentWire.ToWire(request.Budget),
                Cells = request.Cells.Select(c => (c.Cell, c.HasVersions, c.BaseVersion, c.NewVersion, c.Packed)).ToList()
            };
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ApplyDiffSub,
                request.Zone, message.Write, EditTimeout);
            if (!ok || result == EnvironmentMessages.ResultFailed)
                return new EditResult { Result = EnvResult.Failed, Unavailable = true };
            EnvApplyEdit.Answer answer = EnvApplyEdit.Answer.Read(r);
            return new EditResult
            {
                Result = result, Delta = EnvironmentWire.FromWire(answer.Delta), Stale = answer.Stale, Voxels = answer.Voxels,
                CompareReport = string.IsNullOrEmpty(answer.CompareReport) ? null : answer.CompareReport, Cells = EnvironmentWire.FromWire(answer.Cells)
            };
        }

        public WriteResult WriteCells(uint zone, ulong owner, IReadOnlyList<(ulong Cell, List<TerrainEdits.VoxelValue> Values)> cells,
            MaterialBudget budget, bool returnValues)
        {
            var message = new EnvWriteCells
            {
                Owner = owner, JobType = (byte)(returnValues ? 11 : 9), ReturnValues = returnValues, Budget = EnvironmentWire.ToWire(budget),
                Cells = cells.Select(c => (c.Cell, EnvironmentWire.ToWire(c.Values))).ToList()
            };
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.WriteCellsSub, zone,
                message.Write, EditTimeout);
            if (!ok || result == EnvironmentMessages.ResultFailed)
                return new WriteResult { Result = EnvResult.Failed, Unavailable = true };
            return new WriteResult { Result = result, Delta = EnvironmentWire.FromWire(EnvWire.Delta(r)), Cells = EnvironmentWire.FromWire(EnvWire.CellValues(r)) };
        }

        public ClearResult ClearCells(uint zone, ulong owner, IReadOnlyList<(ulong Cell, byte[] Changed)> cells)
        {
            var message = new EnvClearCells { Owner = owner, Cells = cells.Select(c => (c.Cell, c.Changed)).ToList() };
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ClearCellsSub, zone,
                message.Write, EditTimeout);
            if (!ok || result != 0)
                return new ClearResult { Unavailable = true };
            return new ClearResult { Delta = EnvironmentWire.FromWire(EnvWire.Delta(r)), Voxels = (int)r.U32(), Cells = EnvironmentWire.FromWire(EnvWire.CellValues(r)) };
        }

        public List<TerrainEdits.VoxelValue> Read(ulong cell, byte[] changed, uint zone, uint layer = TerrainEdits.BuildLayer)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ReadSub, zone, w =>
            {
                w.U32(layer).U64(cell);
                EnvWire.Packed(w, changed);
            }, CallTimeout);
            if (ok && result == 0)
                return EnvironmentWire.FromWire(EnvWire.Voxels(r));
            log.Warn($"Read of cell {cell:X} not answered (Environment Server away): empty values.");
            return TerrainEdits.ChangedIndices(changed).Select(i => new TerrainEdits.VoxelValue(i, 0, 0, 0, 0, 0)).ToList();
        }

        public void Write(ulong cell, List<TerrainEdits.VoxelValue> values, uint zone, ulong owner = 0, uint layer = TerrainEdits.BuildLayer)
        {
            (bool ok, _, _) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.WriteSub, zone, w =>
            {
                w.U32(layer).U64(owner).U64(cell);
                EnvWire.Voxels(w, EnvironmentWire.ToWire(values));
            }, CallTimeout);
            if (!ok)
                log.Warn($"Write to cell {cell:X} not done: Environment Server away.");
        }

        public Dictionary<ushort, int> MaterialDelta(ulong cell, List<TerrainEdits.VoxelValue> values, uint zone, uint layer = TerrainEdits.BuildLayer)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.MaterialDeltaSub, zone, w =>
            {
                w.U32(layer).U64(cell);
                EnvWire.Voxels(w, EnvironmentWire.ToWire(values));
            }, CallTimeout);
            return ok && result == 0 ? EnvironmentWire.FromWire(EnvWire.Delta(r)) : new Dictionary<ushort, int>();
        }

        // ---- digs, box operations, heals ----------------------------------------------------------------------------------

        public (List<ulong> Cells, int Voxels) Sphere(Vector3 local, double radius, ushort material, uint zone,
            Dictionary<ushort, int> oreMined = null, HashSet<ulong> oreCells = null, ushort? shellMaterial = null,
            VoxelFilter allowed = null, bool carveBuilds = false, TimeSpan? healDelay = null)
        {
            var message = new EnvSphere
            {
                X = local.X, Y = local.Y, Z = local.Z, Radius = radius, Material = material, Shell = shellMaterial,
                Filter = EnvironmentWire.ToWire(allowed), CarveBuilds = carveBuilds, HealSeconds = healDelay?.TotalSeconds,
                CountOre = oreMined != null || oreCells != null
            };
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.SphereSub, zone, message.Write, CallTimeout);
            if (!ok || result != 0)
                return (new List<ulong>(), 0);
            List<ulong> cells = EnvWire.Cells(r);
            int voxels = (int)r.U32();
            for (uint n = r.U32(); n > 0; n--)
            {
                ushort ore = r.U16();
                int count = (int)r.U32();
                uint item = r.U32();
                if (item != 0)
                    oreItems[ore] = item;
                if (oreMined != null)
                    oreMined[ore] = oreMined.GetValueOrDefault(ore) + count;
            }
            foreach (ulong cell in EnvWire.Cells(r))
                oreCells?.Add(cell);
            return (cells, voxels);
        }

        public (List<ulong> Cells, int Voxels) DestroyArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight,
            VoxelFilter allowed = null)
            => Area(EnvironmentMessages.DestroyAreaSub, zone, centre, radius, halfHeight, allowed);

        public (List<ulong> Cells, int Voxels) HealArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight)
            => Area(EnvironmentMessages.HealAreaSub, zone, centre, radius, halfHeight, null);

        private (List<ulong> Cells, int Voxels) Area(uint sub, uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight,
            VoxelFilter allowed)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, sub, zone, w =>
            {
                EnvWire.F64(w, centre.X); EnvWire.F64(w, centre.Y); EnvWire.F64(w, centre.Z);
                EnvWire.F64(w, radius); EnvWire.F64(w, halfHeight);
                if (sub == EnvironmentMessages.DestroyAreaSub)
                    EnvironmentWire.ToWire(allowed).Write(w);
            }, CallTimeout);
            if (!ok || result != 0)
                return (new List<ulong>(), 0);
            return (EnvWire.Cells(r), (int)r.U32());
        }

        public List<ulong> RevertBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, uint zone)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ClaimsFamily, EnvironmentMessages.RevertTerrainSub, zone,
                w => EnvWire.Box(w, minX, minY, minZ, maxX, maxY, maxZ), EditTimeout);
            if (ok && result == 0)
                return EnvWire.Cells(r);
            // Kept and done on reconnect (a site removed meanwhile must not keep its building in the service's layer 1).
            log.Warn($"Revert of box ({minX}, {minY}, {minZ})-({maxX}, {maxY}, {maxZ}) on {zone}: Environment Server away, done when it is back.");
            lock (deferred)
                deferred.Add(() => ZoneTerrain.Broadcast(RevertBox(minX, minY, minZ, maxX, maxY, maxZ, zone), zone));
            return new List<ulong>();
        }

        public void ClearCell(ulong cell, uint zone)
            => Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.ClearCellSub, zone, w => w.U64(cell), CallTimeout);

        /// <summary>The cells the service's heal queue gave back since the last call (pushed with reason Heal).</summary>
        public IReadOnlyList<(uint Zone, List<ulong> Cells)> Regenerate() => Drain(healed);

        private static IReadOnlyList<(uint Zone, List<ulong> Cells)> Drain(ConcurrentQueue<(uint Zone, List<ulong> Cells)> queue)
        {
            if (queue.IsEmpty)
                return Array.Empty<(uint, List<ulong>)>();
            var byZone = new Dictionary<uint, HashSet<ulong>>();
            while (queue.TryDequeue(out var item))
                (byZone.TryGetValue(item.Zone, out var set) ? set : byZone[item.Zone] = new()).UnionWith(item.Cells);
            return byZone.Select(z => (z.Key, z.Value.ToList())).ToList();
        }

        public void SetRegenerationDelay(TimeSpan delay)
            => Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.RegenerationDelaySub, 0, w => EnvWire.F64(w, delay.TotalSeconds), CallTimeout);

        public void MarkPermanent(IEnumerable<ulong> cells, uint zone)
        {
            List<ulong> list = cells.ToList();
            lock (permanent)
                (permanent.TryGetValue(zone, out var set) ? set : permanent[zone] = new()).UnionWith(list);
            Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.MarkPermanentSub, zone, w => EnvWire.Cells(w, list));
        }

        // ---- exported-template stamps -------------------------------------------------------------------------------------

        public List<ulong> StampTemplate(VoxelTemplate template, uint cellX, uint cellY, uint cellZ, uint zone)
            => Stamp(template, EnvStamp.CellCorner, cellX, cellY, cellZ, zone).Changed;

        public (List<ulong> Cells, List<ulong> Changed) StampTemplateAt(VoxelTemplate template, long voxelX, long voxelY, long voxelZ, uint zone, int turns = 0)
            => Stamp(template, (turns & 3) == 0 ? EnvStamp.VoxelCorner : EnvStamp.VoxelCornerTurned, voxelX, voxelY, voxelZ, zone, (byte)(turns & 3));

        /// <summary>
        /// A template read from the game's templates folder goes by file name (the service reads its own copy, so a ruin's
        /// cells never cross the network); one read from bytes (Landmarks of Landmark) goes inline as a minimal .otd.
        /// </summary>
        private (List<ulong> Cells, List<ulong> Changed) Stamp(VoxelTemplate template, byte mode, long x, long y, long z, uint zone, byte turns = 0)
        {
            bool fromFile = template.Source != null && string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(template.Source)),
                System.IO.Path.GetFullPath(VoxelTemplate.Folder).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            var message = new EnvStamp
            {
                Mode = mode, X = x, Y = y, Z = z, Source = fromFile ? EnvStamp.FromFile : EnvStamp.Inline,
                Name = fromFile ? System.IO.Path.GetFileName(template.Source) : System.IO.Path.GetFileName(template.Source ?? "(inline)"),
                Otd = fromFile ? Array.Empty<byte>() : template.CellsAsOtd() ?? throw new InvalidOperationException("Template without its cells."),
                Turns = turns
            };
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.SpawnTemplateSub, zone,
                message.Write, StampTimeout);
            if (!ok || result != 0)
                throw new EnvironmentUnavailableException($"Stamp of {message.Name} on {zone}: Environment Server at {address} "
                    + (ok ? $"answered {result}" : "unreachable") + ".");
            List<ulong> cells = EnvWire.Cells(r), changed = EnvWire.Cells(r);
            VoxelTemplate.LastStampReport = r.Str();
            return (cells, changed);
        }

        // ---- voxel ownership ----------------------------------------------------------------------------------------------

        public (List<ulong> UpToDate, List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)> Data) OwnershipQuery(uint zone,
            IReadOnlyList<(ulong Cell, uint Version)> cells)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.OwnershipSub, zone, w =>
            {
                w.U32((uint)cells.Count);
                foreach ((ulong cell, uint version) in cells)
                    w.U64(cell).U32(version);
            }, EditTimeout);
            if (ok && result == 0)
                return EnvOwnership.ReadAnswer(r);
            // Away (spec §6): every cell up to date, so Voxel Vision shows what the client has and nothing waits.
            return (cells.Select(c => c.Cell).ToList(), new List<(ulong, uint, byte[], List<ulong>)>());
        }

        public List<(ulong Cell, List<int> Indices)> OwnedBy(uint zone, ulong owner, VoxelBox box)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.OwnedBySub, zone, w =>
            {
                w.U64(owner);
                EnvWire.Box(w, box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ);
            }, EditTimeout);
            var owned = new List<(ulong, List<int>)>();
            if (!ok || result != 0)
            {
                log.Warn($"Voxels of 0x{owner:X} on {zone}: Environment Server away, none listed.");
                return owned;
            }
            for (uint n = r.U32(); n > 0; n--)
                owned.Add((r.U64(), TerrainEdits.ChangedIndices(EnvWire.Packed(r))));
            return owned;
        }

        public Dictionary<ulong, int> OwnershipCensus(uint zone, ulong cell)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.CensusSub, zone, w => w.U64(cell), CallTimeout);
            var census = new Dictionary<ulong, int>();
            if (ok && result == 0)
                for (uint n = r.U32(); n > 0; n--)
                    census[r.U64()] = (int)r.U32();
            return census;
        }

        // ---- creature navigation -----------------------------------------------------------------------------------------

        /// <summary>
        /// One request per site rebuild (Game/Pathing, background thread; the call waits there, never on the world thread).
        /// Null while the service is away or on a failed answer: the zone keeps its old grid and asks again later.
        /// </summary>
        public SolidityGrid SolidityOf(uint zone, VoxelBox box)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.ConstructionFamily, EnvironmentMessages.SolidityOfSub, zone,
                w => EnvWire.Box(w, box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ), EditTimeout);
            if (!ok || result != 0 || !r.Bool())
                return null;
            return SolidityGrid.Read(r);
        }

        // ---- ore veins ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Sends the players' positions (at most every 2 s; the service's vein update runs on its own tick) and returns the
        /// vein cells the service changed since the last call (pushed with reason Veins).
        /// </summary>
        public IReadOnlyList<(uint Zone, List<ulong> Cells)> UpdateVeins(IEnumerable<(uint Zone, Vector3 Position)> players)
        {
            if (DateTime.UtcNow >= nextPositions && Available)
            {
                nextPositions = DateTime.UtcNow + PositionInterval;
                List<(uint Zone, Vector3 Position)> list = players.ToList();
                Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.PositionsSub, 0, w =>
                {
                    w.U32((uint)list.Count);
                    foreach ((uint zone, Vector3 p) in list)
                        w.U32(zone).F32(p.X).F32(p.Y).F32(p.Z);
                });
            }
            return Drain(veins);
        }

        public List<ulong> EnsureVeinsAround(uint zone, Vector3 local)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.EnsureVeinsSub, zone,
                w => w.F32(local.X).F32(local.Y).F32(local.Z), CallTimeout);
            return ok && result == 0 ? EnvWire.Cells(r) : new List<ulong>();
        }

        public void DisturbVeins(IEnumerable<ulong> cells, uint zone)
        {
            List<ulong> list = cells.ToList();
            Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DisturbVeinsSub, zone, w => EnvWire.Cells(w, list));
        }

        public List<ulong> ClearVeinsInSite(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.ClearVeinsSub, zone,
                w => EnvWire.Box(w, min.X, min.Y, min.Z, max.X, max.Y, max.Z), CallTimeout);
            if (ok && result == 0)
                return EnvWire.Cells(r);
            lock (deferred)
                deferred.Add(() => ZoneTerrain.ClearVeinsInSite(zone, min, max));
            return new List<ulong>();
        }

        public List<(OreVeins.Vein Vein, Vector3 Nearest, float Distance)> VeinsNear(Vector3 position, float radius, uint zone)
        {
            (bool ok, uint result, InternalReader r) = Call(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.VeinsNearSub, zone,
                w => w.F32(position.X).F32(position.Y).F32(position.Z).F32(radius), CallTimeout);
            var near = new List<(OreVeins.Vein, Vector3, float)>();
            if (!ok || result != 0)
                return near;
            for (uint n = r.U32(); n > 0; n--)
            {
                EnvVein v = EnvVein.Read(r);
                // The zone's copy carries what its callers read (VeinOverlay, Consumables, dev nearvein): kind, item, points, cells.
                var vein = new OreVeins.Vein
                {
                    Id = v.Id,
                    Info = new OreVeins.Info(v.Mapping, new VeinGenerator.Definition { Id = v.Mapping, Name = v.Name }, v.Item, null, null, 1200),
                    Region = (v.RegionX, v.RegionZ),
                    Cave = v.Cave,
                    Points = v.Points.Select(p => new VeinGenerator.Point(p.X, p.Y, p.Z, p.Width, p.Seed)).ToList(),
                    Cells = v.Cells
                };
                near.Add((vein, new Vector3(v.NearX, v.NearY, v.NearZ), v.Distance));
            }
            return near;
        }

        public (uint Item, Vector3 At, float Distance)? NearestVein(Vector3 position, float radius, ISet<uint> items, uint zone)
            => VeinsNear(position, radius, zone).Where(v => items.Contains(v.Vein.Item)).Select(v => ((uint, Vector3, float)?)(v.Vein.Item, v.Nearest, v.Distance))
                .FirstOrDefault();

        public uint OreItemFor(ushort material) => oreItems.GetValueOrDefault(material);

        // ---- LOD copies ---------------------------------------------------------------------------------------------------

        public void RefreshLodCopies(uint zone) => Post(EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.RefreshLodSub, zone, null);

        /// <summary>
        /// The CellsUpdate body cache (spec §5.3 item 3): one body per (landscape, cell) with the layer versions it carries;
        /// a lookup with other versions misses (and drops the stale body). Least recently used out first above the limit.
        /// </summary>
        private sealed class PayloadCache
        {
            private readonly long limit;
            private readonly object sync = new();
            private readonly Dictionary<(uint, ulong), LinkedListNode<((uint, ulong) Key, uint[] Versions, byte[] Body)>> map = new();
            private readonly LinkedList<((uint, ulong) Key, uint[] Versions, byte[] Body)> order = new();
            private long bytes, hits, misses;

            public PayloadCache(long limit) => this.limit = limit;

            public bool TryGet(uint zone, ulong cell, uint[] versions, out byte[] body)
            {
                lock (sync)
                {
                    if (map.TryGetValue((zone, cell), out var node))
                    {
                        if (node.Value.Versions.AsSpan().SequenceEqual(versions))
                        {
                            order.Remove(node);
                            order.AddFirst(node);
                            hits++;
                            body = node.Value.Body;
                            return true;
                        }
                        Remove(node);
                    }
                    misses++;
                    body = null;
                    return false;
                }
            }

            public void Put(uint zone, ulong cell, uint[] versions, byte[] body)
            {
                lock (sync)
                {
                    if (map.TryGetValue((zone, cell), out var old))
                        Remove(old);
                    var node = order.AddFirst(((zone, cell), (uint[])versions.Clone(), body));
                    map[(zone, cell)] = node;
                    bytes += body.Length;
                    while (bytes > limit && order.Last != null)
                        Remove(order.Last);
                }
            }

            private void Remove(LinkedListNode<((uint, ulong) Key, uint[] Versions, byte[] Body)> node)
            {
                order.Remove(node);
                map.Remove(node.Value.Key);
                bytes -= node.Value.Body.Length;
            }

            public string Report()
            {
                lock (sync)
                    return $"{map.Count} cell bodies, {bytes / 1024} KB, {hits} hits / {misses} misses";
            }
        }
    }
}
