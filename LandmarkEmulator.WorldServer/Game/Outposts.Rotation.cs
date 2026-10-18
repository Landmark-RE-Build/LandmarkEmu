using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Outposts changing caverns (trace/0695/audit-outposts-695.md F8; TASKS 3.14). Off by default
    /// (WorldServer.json OutpostRotationHours 0, Docker OUTPOST_ROTATION_HOURS): the outposts then stay where they were
    /// first stamped, as before.
    /// <para>
    /// <b>The sources.</b>
    /// <list type="bullet">
    /// <item>[P text] The client's help page HelpPages/ChaosCavern_2Outpost_FTE.html (same bytes in 0.589 and 0.695):
    /// "Outposts exist in the open world and change regularly." It stands under "Team Up With Other Luminaries", next to
    /// "If an Outpost is empty, another Luminary might've recently fought their way through it", so it can also mean the
    /// shared state (garrisons killed, then back), which is built (6.23). trace/0695/agent-outposts.md §4.1 reads it that
    /// way; this file is the other reading, behind a setting.</item>
    /// <item>[P] each Chaos Cavern always has an outpost (hotfix 26 Apr 2016, P4808): "always" fits a population that
    /// comes and goes. The rotation keeps it: a cavern's outpost is replaced
    /// in the same cavern, never removed.</item>
    /// <item>[P] The Environment Server monitor page (0.695 part_0091 ~33767, FUN_14320ea00) keeps per "Ruin Layer" a
    /// Desired / Active / Failed count and a growing Last ID (trace/architecture/terrain-service.md §1.1): ruins placed by
    /// jobs, with fresh instance ids, not a list fixed once. The client's packet registry has both
    /// VoxelConstruction SpawnExportedTemplateRequest and DespawnExportedTemplateRequest (0xF4 / 0x18, 0x19;
    /// re/out/packet_ids_695.tsv), so live's servers could take a stamped template out again.</item>
    /// <item>[I] No source gives a period, a share or a trigger. Players asked for it in May 2016 (more random ruins,
    /// or new ones at each restart, forum 57065; another complained of the same cavern every time), so at that time
    /// they saw
    /// none. Live restarted twice a week (Mon + Sat 3 a.m., RestartSchedule).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>What it does (ours [I]).</b> Every <see cref="rotationEvery"/> per landscape (the clock starts when a landscape's
    /// outposts are all placed and is kept in TerrainEdits/outposts-rotation.txt across restarts), the background worker
    /// replaces a share (<see cref="rotationShare"/>, OutpostRotationShare, default a quarter) of that landscape's
    /// outposts: each picked outpost's cavern gets another ruin in one of its own free ruin slots (live's slots, the
    /// placement file's CavernInstanceData rooms, as <see cref="ChooseSlots"/> / <see cref="FillCaverns"/>), the ruin with
    /// the fewest copies on that landscape first (P4971: one outpost per cavern, never two alike), never overlapping
    /// another outpost or an arrival platform (P5830). Outposts somebody could see are left alone: a player on that
    /// landscape within <see cref="OccupiedRange"/>, the outpost streamed to anyone, a crystal channel, or a woken crystal
    /// (someone fought there in the last 15 minutes). The last copy of a ruin on a landscape is never replaced, so every one
    /// of the 35 ruins stays on every landscape (the start-up's "each ruin once" stays true and never adds outposts).
    /// The outpost keeps its index: its prop guids, its banner site and the crystal lockout, which the client's page keys
    /// to the cavern ("You can only bond with a Lost Crystal once in each Chaos Cavern", ChaosCavern_3Crystal_FTE).
    /// </para>
    /// <para>
    /// <b>One replacement</b> (background worker; the world tick only retires and adds): the new ruin is decoded and its
    /// standing spot found; the world tick takes the old outpost out of play if nobody is there now (props, garrison, banner,
    /// navigation tiles); a journal line (outposts-rotation-pending.txt) records old and new; the old ruin's box goes back
    /// to the natural ground (EnvironmentServer RevertBox: every overlay voxel in the template's box, its vertex-only border
    /// included), the new ruin is stamped like any outpost (StampTemplateAt, saved and packed, cells marked permanent), the
    /// landscape's marker line is rewritten in place (TerrainEdits/outposts695d.txt or outposts695d_&lt;zone&gt;.txt; temp
    /// file + rename), the outpost goes live again on the next tick with its new garrison, crystal and chest spots, and the
    /// changed cells go to the players on that landscape (coalesced, once). The journal is cleared last. A shutdown in the
    /// middle leaves the journal: the next start rewrites the marker to the new ruin before the markers are read and the
    /// worker reverts and stamps again (both idempotent). Known edge: when two slots touch, the old ruin's revert also
    /// clears the neighbour's one-voxel vertex-only border on that face (vertex positions only, no voxel turns solid or air).
    /// Until the next restart, digs in the old ruin's cells don't heal: permanent marks are only rebuilt from the markers at
    /// start-up (an unmark would need a new Environment Server message).
    /// </para>
    /// </summary>
    public static partial class Outposts
    {
        /// <summary>How often a landscape's outposts rotate (zero = never, the default: OutpostRotationHours 0).</summary>
        private static TimeSpan rotationEvery = TimeSpan.Zero;
        /// <summary>The share of a landscape's outposts one rotation replaces (OutpostRotationShare, default 0.25) [I].</summary>
        private static double rotationShare = 0.25;
        /// <summary>A player this close to an outpost (same landscape, metres) keeps it from being replaced [I].</summary>
        private const float OccupiedRange = 400f;
        private const string RotationFile = "outposts-rotation.txt", RotationPendingFile = "outposts-rotation-pending.txt";

        /// <summary>Each landscape's last rotation (UTC), or when its clock started.</summary>
        private static readonly ConcurrentDictionary<uint, DateTime> lastRotation = new();
        private static readonly object rotationFileSync = new();
        /// <summary>Landscapes with a rotation queued or running.</summary>
        private static readonly ConcurrentDictionary<uint, byte> rotating = new();
        private static readonly ConcurrentQueue<RotationJob> rotationJobs = new();
        /// <summary>The worker's requests to take an outpost out of play, answered by the world tick.</summary>
        private static readonly ConcurrentQueue<(int Index, TaskCompletionSource<bool> Done)> retireRequests = new();
        private static DateTime nextRotationCheck;
        /// <summary>A replacement stopped half way (journal), finished first by the worker.</summary>
        private static volatile Replacement pendingRedo;

        /// <summary>One outpost as the world tick saw it when a rotation was queued.</summary>
        private sealed record Seen(int Index, int SlotCavern, string Ruin, (long X, long Y, long Z) Corner, bool Busy);

        /// <summary>A rotation of one landscape: its outposts then, and how many to replace (0 = the share).</summary>
        private sealed record RotationJob(uint Zone, List<Seen> Outposts, int Count);

        /// <summary>
        /// One outpost's new ruin. <paramref name="Live"/>: the outpost was retired at run time and goes live again when the
        /// terrain is done (false for a journal replayed at start-up: the outpost came back from the rewritten marker).
        /// </summary>
        private sealed record Replacement(uint Zone, int Index, int SlotCavern, string OldRuin, (long X, long Y, long Z) OldCorner,
            string NewRuin, (long X, long Y, long Z) NewCorner, Vector3 Floor, bool Live);

        // ---- start-up ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Start-up, before the markers are read: the settings, each landscape's last rotation, and a replacement the last
        /// shutdown stopped half way (its marker line rewritten to the new ruin now; its terrain finished by the worker).
        /// </summary>
        private static void LoadRotation(string folder)
        {
            WorldServerConfiguration config = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            double hours = config?.OutpostRotationHours ?? 0;
            rotationEvery = hours > 0 ? TimeSpan.FromHours(hours) : TimeSpan.Zero;
            double share = config?.OutpostRotationShare ?? 0.25;
            rotationShare = share > 0 ? Math.Min(1.0, share) : 0.25;
            CultureInfo c = CultureInfo.InvariantCulture;
            string times = Path.Combine(folder, RotationFile);
            if (File.Exists(times))
                foreach (string line in File.ReadAllLines(times))
                {
                    string[] f = line.Split('\t');
                    if (!line.StartsWith('#') && f.Length >= 2 && uint.TryParse(f[0], out uint zone)
                        && DateTime.TryParse(f[1], c, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime when))
                        lastRotation[zone] = when;
                }
            string pending = Path.Combine(folder, RotationPendingFile);
            if (File.Exists(pending) && File.ReadAllLines(pending).FirstOrDefault(l => l.Length > 0 && l[0] != '#') is string row
                && ParsePending(row) is Replacement r)
            {
                RewriteMarker(r);
                pendingRedo = r;
                log.Warn($"Outposts: a rotation was stopped half way (outpost {r.Index} on landscape {r.Zone}: {r.OldRuin} → {r.NewRuin}); "
                    + "its marker says the new ruin, the terrain is finished in the background.");
            }
            if (rotationEvery > TimeSpan.Zero)
                log.Info($"Outposts change caverns every {rotationEvery.TotalHours:0.#} h per landscape, {rotationShare:P0} of them at a time "
                    + "(OutpostRotationHours / OutpostRotationShare; outposts somebody is near are left alone).");
        }

        // ---- world tick ----------------------------------------------------------------------------------------------

        /// <summary>
        /// World tick: the worker's retire requests (an outpost leaves play only if nobody is there now), and once a minute
        /// the landscapes whose rotation is due.
        /// </summary>
        private static void RotationTick(DateTime now)
        {
            while (retireRequests.TryDequeue(out var request))
            {
                if (!byIndex.TryGetValue(request.Index, out Outpost o) || Occupied(o))
                {
                    request.Done.TrySetResult(false);
                    continue;
                }
                // Only when the worker still waits for the answer (it gives up after a while).
                if (request.Done.TrySetResult(true))
                    Retire(o);
            }
            if (rotationEvery <= TimeSpan.Zero || now < nextRotationCheck)
                return;
            nextRotationCheck = now.AddMinutes(1);
            bool started = false;
            foreach (uint zone in byZone.Keys.ToList())
            {
                if (!Ready(zone) || rotating.ContainsKey(zone))
                    continue;
                if (!lastRotation.TryGetValue(zone, out DateTime last))
                {
                    // The clock starts once the landscape's outposts are all placed: the first rotation a period later.
                    lastRotation[zone] = now;
                    started = true;
                    continue;
                }
                if (now - last >= rotationEvery)
                    QueueRotation(zone, 0);
            }
            if (started)
                Task.Run(SaveRotationTimes);
        }

        /// <summary>
        /// World thread (the due check, dev "outpostrotate"): queues one rotation of a landscape for the worker, with what
        /// the tick sees now. <paramref name="count"/> 0 = the configured share. Returns what happened, for the dev log.
        /// </summary>
        public static string QueueRotation(uint zone, int count)
        {
            zone = zone == 0 ? Home : zone;
            if (!Ready(zone) || InZone(zone).Count == 0)
                return $"landscape {zone}: its outposts aren't placed (cavelands)";
            if (!rotating.TryAdd(zone, 0))
                return $"landscape {zone}: a rotation is already queued or running";
            var seen = InZone(zone).Select(o => new Seen(o.Index, o.SlotCavern, o.Ruin, o.Corner, Occupied(o))).ToList();
            rotationJobs.Enqueue(new RotationJob(zone, seen, Math.Max(0, count)));
            StartWorker(force: true);
            wake.Release();
            return $"landscape {zone}: rotation queued ({seen.Count(s => !s.Busy)} of {seen.Count} outposts free now, "
                + $"{(count > 0 ? count.ToString(CultureInfo.InvariantCulture) : $"{rotationShare:P0}")} to replace)";
        }

        /// <summary>Could anyone see this outpost change? (world thread)</summary>
        private static bool Occupied(Outpost o)
        {
            // A woken crystal: someone killed its Elite within the last 15 minutes and may be on the way to it.
            if (o.EliteDead)
                return true;
            lock (sync)
                if (sent.Values.Any(s => s.Contains(o.Index)) || channels.Values.Any(c => c.Outpost == o))
                    return true;
            foreach (WorldSession s in PlayerManager.Snapshot())
                if (s.ZoneId == o.Zone && Vector3.Distance(s.Position, o.Floor) < OccupiedRange)
                    return true;
            return false;
        }

        /// <summary>
        /// World thread: an outpost leaves play for its replacement — its props from anyone holding them, its banner site,
        /// its garrison (the cavern's creature group is rebuilt without it) and the navigation tiles under it.
        /// </summary>
        private static void Retire(Outpost o)
        {
            List<WorldSession> viewers = Viewers(o);
            foreach (WorldSession s in viewers)
                foreach (ulong guid in o.Props.Select(p => p.Guid).Append(o.Crystal.Guid).Append(o.Chest.Guid))
                    s.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
            lock (sync)
            {
                foreach (HashSet<int> set in sent.Values)
                    set.Remove(o.Index);
                foreach (WorldSession s in channels.Where(c => c.Value.Outpost == o).Select(c => c.Key).ToList())
                    channels.Remove(s);
            }
            outposts.Remove(o);
            byCavern.Remove(o.Cavern);
            byIndex.Remove(o.Index);
            if (byZone.TryGetValue(o.Zone, out List<Outpost> list))
                list.Remove(o);
            OutpostSites.Drop(o.Index);
            Creatures.CavernChanged(o.Cavern);
            Pathing.OutpostsChanged(o.Zone);
            log.Info($"Outposts: {o.Name} (outpost {o.Index}, landscape {o.Zone}) leaves its cavern for another ruin.");
        }

        // ---- the worker ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The worker's next rotation work: a replacement to finish first, then a queued rotation — before the landscapes'
        /// preload, after a landscape someone is waiting for (<paramref name="afterPlacement"/>: asked when nothing else is
        /// left).
        /// </summary>
        private static Action TakeRotationWork(bool afterPlacement = false)
        {
            if (pendingRedo is Replacement redo)
                return () => Redo(redo);
            if (!afterPlacement)
                lock (workSync)
                    if (requested.Count > 0 || lightPending.Count > 0)
                        return null;
            if (rotationJobs.TryDequeue(out RotationJob job))
                return () =>
                {
                    try
                    {
                        Rotate(job);
                    }
                    finally
                    {
                        rotating.TryRemove(job.Zone, out _);
                    }
                };
            return null;
        }

        private static void RunRotation(Action work)
        {
            try
            {
                work();
            }
            catch (EnvironmentUnavailableException e)
            {
                // Nothing changed for a replacement not begun; one begun is finished first when the service is back.
                log.Warn($"Outpost rotation: {e.Message} Trying again in a minute.");
                Thread.Sleep(TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                log.Error(e, "Outpost rotation failed.");
            }
        }

        /// <summary>
        /// Background: one rotation of a landscape. The outposts nobody was near when it was queued, in a random order; for
        /// each, until the share is reached: the ruins that fit a free slot of its own cavern (not its own ruin), the one
        /// with the fewest copies on the landscape (at random among those), a random one of its free slots.
        /// </summary>
        private static void Rotate(RotationJob job)
        {
            uint zone = job.Zone;
            var (slots, gaps) = SlotsOf(zone);
            var sizes = new Dictionary<string, (uint X, uint Y, uint Z)>(StringComparer.Ordinal);
            foreach (string ruin in Ruins)
                try
                {
                    sizes[ruin] = ruinInfo.TryGetValue(ruin, out RuinInfo known) ? known.Size
                        : VoxelTemplate.SizeOf(Path.Combine(VoxelTemplate.Folder, ruin + ".otd"));
                }
                catch (Exception e)
                {
                    log.Warn($"Outpost ruin {ruin} unreadable: {e.Message}");
                }
            double voxel = TerrainEdits.VoxelSize;
            RuinSlots.Box BoxOf(string ruin, (long X, long Y, long Z) c)
            {
                var s = sizes[ruin];
                return new RuinSlots.Box((c.X + 1) * voxel, (c.Y + 1) * voxel, (c.Z + 1) * voxel,
                    (c.X + 1 + s.X) * voxel, (c.Y + 1 + s.Y) * voxel, (c.Z + 1 + s.Z) * voxel);
            }
            var current = job.Outposts.Where(o => o.Ruin != null).ToDictionary(o => o.Index);
            var copies = sizes.Keys.ToDictionary(r => r, r => current.Values.Count(o => o.Ruin == r), StringComparer.Ordinal);
            int want = job.Count > 0 ? job.Count : (int)Math.Ceiling(rotationShare * current.Count);
            var rng = new Random();
            List<Seen> order = current.Values.Where(o => !o.Busy && o.SlotCavern >= 0 && sizes.ContainsKey(o.Ruin)).OrderBy(_ => rng.Next()).ToList();
            int done = 0, last = 0, cramped = 0, refused = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (Seen c in order)
            {
                if (done >= want)
                    break;
                if (copies[c.Ruin] <= 1)
                {
                    last++;
                    continue;
                }
                var taken = new List<RuinSlots.Box>(gaps);
                taken.AddRange(current.Values.Where(o => o.Index != c.Index && sizes.ContainsKey(o.Ruin)).Select(o => BoxOf(o.Ruin, o.Corner)));
                var options = new List<(string Ruin, RuinSlots.Box Box)>();
                foreach (var s in slots.Where(s => s.Cavern == c.SlotCavern && taken.All(b => !b.Overlaps(s.Box))))
                    foreach ((string ruin, var size) in sizes)
                        if (ruin != c.Ruin && Math.Abs(s.Box.MaxX - s.Box.MinX - size.X * voxel) < 1 && Math.Abs(s.Box.MaxZ - s.Box.MinZ - size.Z * voxel) < 1)
                            options.Add((ruin, s.Box));
                if (options.Count == 0)
                {
                    cramped++;
                    continue;
                }
                int fewest = options.Min(o => copies[o.Ruin]);
                var pool = options.Where(o => copies[o.Ruin] == fewest).ToList();
                (string chosen, RuinSlots.Box box) = pool[rng.Next(pool.Count)];
                var corner = ((long)Math.Round(box.MinX / voxel) - 1, (long)Math.Round(box.MinY / voxel) - 1, (long)Math.Round(box.MinZ / voxel) - 1);
                var r = new Replacement(zone, c.Index, c.SlotCavern, c.Ruin, c.Corner, chosen, corner, Vector3.Zero, Live: true);
                if (!Replace(r))
                {
                    refused++;
                    continue;
                }
                copies[c.Ruin]--;
                copies[chosen]++;
                current[c.Index] = c with { Ruin = chosen, Corner = corner };
                done++;
            }
            lastRotation[zone] = DateTime.UtcNow;
            SaveRotationTimes();
            log.Info($"Outposts: landscape {zone} rotated {done} of {current.Count} outpost(s) in {watch.Elapsed.TotalSeconds:0} s "
                + $"({job.Outposts.Count(o => o.Busy)} had players near, {last} last copies kept, {cramped} without a free slot for another ruin, "
                + $"{refused} busy by the time it came to them). {EnvironmentServer.Instance.MemoryReport()}");
        }

        /// <summary>
        /// Background: one outpost replaced (the class notes). False when it can't be now (the new ruin unreadable, or
        /// someone came near before the world tick could take it out of play); nothing has changed then.
        /// </summary>
        private static bool Replace(Replacement r)
        {
            if (!EnvironmentServer.Instance.Available)
                throw new EnvironmentUnavailableException("the Environment Server is away.");
            VoxelTemplate template;
            try
            {
                template = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, r.NewRuin + ".otd"));
            }
            catch (Exception e)
            {
                log.Warn($"Outpost ruin {r.NewRuin} unreadable: {e.Message}");
                return false;
            }
            double voxel = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            var slotFloor = new Vector3((float)(((r.NewCorner.X + 1 + template.Size.X / 2.0) * voxel - origin.X) / 10),
                (float)(((r.NewCorner.Y + 1) * voxel - origin.Y) / 10), (float)(((r.NewCorner.Z + 1 + template.Size.Z / 2.0) * voxel - origin.Z) / 10));
            r = r with { Floor = StandingSpot(template, r.NewCorner, r.Zone) ?? slotFloor + new Vector3(0f, 8f, 0f) };
            if (!AskRetire(r.Index))
                return false;
            WritePending(r);
            try
            {
                Apply(r, template);
            }
            catch (EnvironmentUnavailableException)
            {
                pendingRedo = r;      // finished first when the service is back
                throw;
            }
            DeletePending();
            template = null;
            GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
            return true;
        }

        /// <summary>Background: asks the world tick to take an outpost out of play and waits for the answer (30 s at most).</summary>
        private static bool AskRetire(int index)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            retireRequests.Enqueue((index, done));
            if (!done.Task.Wait(TimeSpan.FromSeconds(30)) && done.TrySetResult(false))
                return false;
            return done.Task.Result;
        }

        /// <summary>
        /// Background: the terrain and records of one replacement — the old box back to the natural ground, the new ruin
        /// stamped (saved, packed, cells permanent), the marker line rewritten, the outpost live again (when retired at run
        /// time), the changed cells to the players. Each step can run again with the same result (a journal replay).
        /// </summary>
        private static void Apply(Replacement r, VoxelTemplate template)
        {
            if (!EnvironmentServer.Instance.Available)
                throw new EnvironmentUnavailableException("the Environment Server is away.");
            double voxel = TerrainEdits.VoxelSize;
            var reverted = new List<ulong>();
            if (InfoOf(r.OldRuin) is RuinInfo old)
            {
                // Template voxels 0..Size (the vertex-only border at 0, the explicit box above it): centres inside these bounds.
                (long x, long y, long z) = r.OldCorner;
                reverted = EnvironmentServer.Instance.RevertBox(x * voxel, y * voxel, z * voxel,
                    (x + old.Size.X + 1) * voxel, (y + old.Size.Y + 1) * voxel, (z + old.Size.Z + 1) * voxel, r.Zone);
                EnvironmentServer.Instance.Flush();
                EnvironmentServer.Instance.CompactNow(true);
            }
            else
                log.Warn($"Outpost ruin {r.OldRuin} unreadable: its box on landscape {r.Zone} is left as it is.");
            (List<ulong> cells, List<ulong> changed) = EnvironmentServer.Instance.StampTemplateAt(template, r.NewCorner.X, r.NewCorner.Y, r.NewCorner.Z, r.Zone);
            EnvironmentServer.Instance.Flush();
            EnvironmentServer.Instance.CompactNow(true);
            EnvironmentServer.Instance.MarkPermanent(cells, r.Zone);
            RewriteMarker(r);
            Remember(r.NewRuin, template);
            if (!Sight.HasRuin(r.NewRuin))
                Sight.AddRuin(r.NewRuin, template);
            if (r.Live)
                arrivals.Enqueue(new Arrival(r.Zone, r.Index, r.NewRuin, r.NewCorner, r.Floor, template.Props, r.SlotCavern));
            List<ulong> all = reverted.Concat(changed).Distinct().ToList();
            if (all.Count > 0)
                changedCells.Enqueue((r.Zone, all));
            log.Info($"Outposts: outpost {r.Index} on landscape {r.Zone} (cavern {r.SlotCavern}) is now {r.NewRuin} (was {r.OldRuin}): "
                + $"{reverted.Count} cell(s) reverted, {VoxelTemplate.LastStampReport}.");
        }

        /// <summary>Background: a replacement the journal holds, finished (start-up replay, or after the service was away).</summary>
        private static void Redo(Replacement r)
        {
            VoxelTemplate template;
            try
            {
                template = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, r.NewRuin + ".otd"));
            }
            catch (Exception e)
            {
                // Kept in the journal for the next start; the outpost's props stand without its voxels meanwhile.
                log.Error($"Outpost rotation: {r.NewRuin} unreadable ({e.Message}); outpost {r.Index} on landscape {r.Zone} left half done.");
                pendingRedo = null;
                return;
            }
            Apply(r, template);
            DeletePending();
            pendingRedo = null;
        }

        // ---- records -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The landscape's marker line of this outpost says the new ruin (temp file + rename). The home continent's lines are
        /// "slot cavern, ruin, corner" in index order: the old line is found by its fields. Another landscape's carry the
        /// outpost's number and its standing spot: found by the number. Nothing when the line already says the new ruin.
        /// </summary>
        private static void RewriteMarker(Replacement r)
        {
            bool home = r.Zone == Home;
            string path = Path.Combine(editsFolder ?? "", home ? Marker : MarkerOf(r.Zone));
            if (!File.Exists(path))
            {
                log.Warn($"Outpost rotation: no marker {Path.GetFileName(path)} to record outpost {r.Index}'s new ruin.");
                return;
            }
            string[] lines = File.ReadAllLines(path);
            string oldLine = string.Join('\t', r.SlotCavern, r.OldRuin, r.OldCorner.X, r.OldCorner.Y, r.OldCorner.Z);
            string newLine = home ? string.Join('\t', r.SlotCavern, r.NewRuin, r.NewCorner.X, r.NewCorner.Y, r.NewCorner.Z)
                : MarkerLine(new Placed(r.SlotCavern, r.NewRuin, r.NewCorner, r.Index - IndexBase(r.Zone), r.Floor));
            int at = -1;
            for (int i = 0; i < lines.Length && at < 0; i++)
            {
                string[] f = lines[i].Split('\t');
                if (lines[i] == newLine)
                    return;
                if (home ? lines[i] == oldLine : f.Length >= 9 && int.TryParse(f[5], out int n) && n == r.Index - IndexBase(r.Zone))
                    at = i;
            }
            if (at < 0)
            {
                log.Warn($"Outpost rotation: outpost {r.Index}'s line is not in {Path.GetFileName(path)}; its new ruin is not recorded.");
                return;
            }
            lines[at] = newLine;
            File.WriteAllLines(path + ".tmp", lines);
            File.Move(path + ".tmp", path, overwrite: true);
        }

        private static string PendingLine(Replacement r)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return string.Join('\t', r.Zone, r.Index, r.SlotCavern, r.OldRuin, r.OldCorner.X, r.OldCorner.Y, r.OldCorner.Z,
                r.NewRuin, r.NewCorner.X, r.NewCorner.Y, r.NewCorner.Z, r.Floor.X.ToString("R", c), r.Floor.Y.ToString("R", c), r.Floor.Z.ToString("R", c));
        }

        private static Replacement ParsePending(string line)
        {
            string[] f = line.Split('\t');
            CultureInfo c = CultureInfo.InvariantCulture;
            if (f.Length < 14 || !uint.TryParse(f[0], out uint zone) || !int.TryParse(f[1], out int index) || !int.TryParse(f[2], out int slot)
                || !long.TryParse(f[4], out long ox) || !long.TryParse(f[5], out long oy) || !long.TryParse(f[6], out long oz)
                || !long.TryParse(f[8], out long nx) || !long.TryParse(f[9], out long ny) || !long.TryParse(f[10], out long nz)
                || !float.TryParse(f[11], NumberStyles.Float, c, out float fx) || !float.TryParse(f[12], NumberStyles.Float, c, out float fy)
                || !float.TryParse(f[13], NumberStyles.Float, c, out float fz))
            {
                log.Warn($"Outpost rotation: unreadable journal line '{line}' ignored.");
                return null;
            }
            return new Replacement(zone, index, slot, f[3], (ox, oy, oz), f[7], (nx, ny, nz), new Vector3(fx, fy, fz), Live: false);
        }

        private static void WritePending(Replacement r)
        {
            Directory.CreateDirectory(editsFolder);
            string path = Path.Combine(editsFolder, RotationPendingFile);
            File.WriteAllLines(path + ".tmp", new[] { "#zone\tindex\tslot cavern\told ruin\tx\ty\tz\tnew ruin\tx\ty\tz\tfloor x\ty\tz", PendingLine(r) });
            File.Move(path + ".tmp", path, overwrite: true);
        }

        private static void DeletePending()
        {
            string path = Path.Combine(editsFolder ?? "", RotationPendingFile);
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void SaveRotationTimes()
        {
            if (editsFolder == null)
                return;
            try
            {
                lock (rotationFileSync)
                {
                    Directory.CreateDirectory(editsFolder);
                    string path = Path.Combine(editsFolder, RotationFile);
                    var lines = new List<string> { "#landscape\tlast rotation (UTC; or when its clock started)" };
                    lines.AddRange(lastRotation.OrderBy(p => p.Key).Select(p => $"{p.Key}\t{p.Value.ToString("o", CultureInfo.InvariantCulture)}"));
                    File.WriteAllLines(path + ".tmp", lines);
                    File.Move(path + ".tmp", path, overwrite: true);
                }
            }
            catch (IOException e)
            {
                log.Warn($"Outpost rotation times not saved: {e.Message}");
            }
        }

        /// <summary>Dev "cavelands": the rotation's settings and where each landscape stands.</summary>
        private static string RotationReport()
        {
            if (rotationEvery <= TimeSpan.Zero)
                return "rotation off (OutpostRotationHours 0)" + (pendingRedo != null ? ", one replacement being finished" : "");
            DateTime now = DateTime.UtcNow;
            string due = string.Join(", ", lastRotation.OrderBy(p => p.Key).Select(p =>
                $"{p.Key} {(rotating.ContainsKey(p.Key) ? "rotating" : $"in {Math.Max(0, (rotationEvery - (now - p.Value)).TotalHours):0.#} h")}"));
            return $"rotation every {rotationEvery.TotalHours:0.#} h, {rotationShare:P0} at a time" + (due.Length > 0 ? $" ({due})" : "");
        }
    }
}
