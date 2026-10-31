using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The slate slab under a new Pioneer site (OPEN-LEADS L180 / L210 / L260; the live evidence with its sources is in
    /// trace/0695/pioneer-slab-live.md).
    /// <para>
    /// <b>Live (2016).</b> New characters from 21 Mar 2016 got a site on a Pioneer land (update notes 21 Mar, P4116) with
    /// a slate slab under it; the 30 Jun notes "Corrected some issues with the slate slab placement on new player claims"
    /// (P5921, design/forum/patches/2016-06-29-57596) [P]. Four 2016 videos (Olgrey mRL8wKF36u4 24 Mar 2016, R3KT
    /// RAUbrp1aZNg, Verlin YnBmejA5MWE and Lj7o-3o93Kg, markeedragon J-YDmIWHMzM) and the wiki's Pioneer-castle.jpg (1 Jul
    /// 2016, after the fix) show:
    /// - the grey slab exactly fills the floor of the site box on every March-June map view, the site's corner beams on
    ///   its corners: 210 × 210 m, the site being 210 × 180 × 210 m (client ClaimCursor.ClaimSize and NewbieClaimSize
    ///   "210 180 210", 525 × 450 × 525 voxels of 0.4 m) [P maps, I strong for the exact size]; a plain full square, no
    ///   plus, no empty corners [P];
    /// - one level top plane; where the ground is lower a crisp vertical face down to the ground (no gap under it), where
    ///   the ground was higher a vertical bank exactly on the outline: the ground above the slab's top was cut away inside
    ///   the footprint [P frames]; after the fix the full square stands about 1-2 m proud (wiki, 1 Jul 2016) [P image, I
    ///   for the number];
    /// - nothing on it: no grass, trees, rocks or harvestables; ground, bushes and trees start right at its edge [P];
    /// - the Starting Location ("Teleport Arrival Point") on the slab about 3 m in from both edges at the north-west
    ///   corner, where "Travel back to your Build Site" lands (21 Mar notes, P4269: "defaults to the lower north west
    ///   corner of your claim") [P]; a first login drops about 3 m onto the slab, probably near its middle [I];
    /// - cracked grey slate [P look]: Worked Slate (VoxelMaterials ARRAY_INDEX 26) fits [I].
    /// The client decides grass only by the surface material (Worked Slate has no flora rows), the slope and the
    /// settings — no area, claim or layer flag [P, cross-checked 2026-10-31]: so the slab is real solid voxels (material
    /// bit set, not vertex-only) and no generated ground is left above its top inside the footprint.
    /// </para>
    /// <para>
    /// <b>What the server stamps by default</b> (setting <see cref="WorldServerConfiguration.PioneerSlab"/> "site", Docker
    /// PIONEER_SLAB; <see cref="StampSiteSlab"/>, <see cref="SiteSlabOf"/>):
    /// - one solid square of <see cref="WorldServerConfiguration.PioneerSlabMaterial"/> (default 26 Worked Slate) over
    ///   exactly the site's footprint: the 525 × 525 voxel samples inside the box (the client samples at voxel corners and
    ///   draws a cube without a vertex flag at its centre, docs/terrain.md "How the client draws a dig", so with centred
    ///   vertices the faces lie half a voxel outside the outermost samples: on the box's outline for a Pioneer site, whose
    ///   box edges fall on half voxels) [P geometry]. Every column and layer written has its centre inside the box, the
    ///   test the heal pass and RevertBox keep a layer-0 voxel by (TerrainEdits.Regenerate / RevertBox) [P code]: a
    ///   site whose edges are not on half voxels (a placed one) loses a partial +x / +z column rather than having it grow
    ///   back 20 minutes later;
    /// - <see cref="SlabLayers"/> = 5 voxels (2 m) thick, one level top: its bottom layer is the first air sample above the
    ///   generated ground at the site's middle (the site's box is centred on that ground, Claims.Grant), so its top is
    ///   1.8-2.2 m above that ground [I: "stands about 1-2 m proud", wiki 1 Jul 2016];
    /// - per 0.4 m column, from the baked heights (<see cref="TerrainHeights.HeightAt"/>, bilinear on the client's own 2 m
    ///   grid; a column is one lookup, cheap enough for all 275,625: a whole 210 m stamp on flat ground, 1.9 million voxels
    ///   in 196 cells, takes under a second in the tests, so per column rather than per 2 m): solid
    ///   slate from the slab's bottom down to the generated ground and <see cref="GroundMargin"/> voxels into it (against
    ///   the grid's rounding), so a dip shows a face down to the ground; air from the slab's top up to the generated ground
    ///   plus <see cref="GroundMargin"/> where the ground rises above the slab, so a hill becomes a vertical bank on the
    ///   outline. The cut reaches as high as the ground does inside the site's box — far more than the MaxStep (6 m) a
    ///   corner may rise over the middle (<see cref="FindSpot"/>) plus 2 m — so no ground is left hanging over the slab;
    ///   the fill reaches down to the box's bottom at most;
    /// - cave air in a column is never filled (and air on it changes nothing): <see cref="CavePieces.AirSpans"/> per column
    ///   at the samples' own positions; the slab's own 5 layers are written whatever lies there (a full square);
    /// - every voxel written with flags 3 (material + vertex) and centred offsets, the client templates' own convention
    ///   for solids and air (claim1x1.otd; <see cref="VoxelTemplate"/>): a flat box, no vertex-only voxels anywhere. The
    ///   cubes just outside the -x / -z outline keep the generated world's vertex (centre where the ground doesn't cross
    ///   them), which is also on the outline; nothing else outside the footprint is written, except that the one column
    ///   just outside the -x face is reset to the generated world where an older claim3x3 stamp of the site reached it
    ///   (until 2026-11-01 its corner was rounded half to even, one column out on every other grid column:
    ///   <see cref="TemplateCorner"/>);
    /// - built directly per destination cell (one cell's values at a time, written through
    ///   <see cref="IEnvironmentServer.Write"/> into layer 0) rather than as 3 × 3 claim1x1 tiles: the fill and cut
    ///   depend on every column's ground, which no template carries, and a 9-tile template would only cover the 5 slab
    ///   layers (its seams would be invisible — every sample solid, every vertex centred — but the per-column work remains).
    ///   Memory stays at one cell plus the overlays between compactions (every 50 cells). Re-running a stamp writes the
    ///   same values (it reads only the generated heights), so a stamp an away remote Environment Server cut short is
    ///   simply run again;
    /// - the Starting Location on the slab's top, <see cref="StartingLocationInset"/> = 3 m in from the site's -x / +z
    ///   corner (<see cref="StartingLocationOnSlab"/>, Claims.EnsureStartingLocation); north-west is -x / +z: the
    ///   client's compass turns by atan2f(x, z) of the camera (0.695 FUN_14332c6b0 UpdateHudCompass → FUN_1437f7c8c
    ///   atan2f → WorldMapRotateCompass), so heading 0 and north are +z [P]; +x east follows from the engine's
    ///   left-handed frame [I] (the compass art was not checked). One left inside the footprint on the old ground of a
    ///   site stamped again goes onto the slab when the stamp is done (<see cref="LiftStartingLocation"/>): one lower
    ///   than the slab's top, and one on generated ground above it that the cut took away; one standing higher (on the
    ///   owner's build) stays, and none is moved out of the site's box;
    /// - a first login arrives <see cref="FirstLoginDrop"/> = 3 m above the slab's middle (<see cref="Place"/>) [I]. The
    ///   stamp starts when the site is granted, at login before the zone details, and takes about a second (tests), so it
    ///   is normally done before the client has loaded the zone [I]; trees, plants and decoration in the site are taken
    ///   out when it is placed (Gathering.SitePlaced), and none grow back in a site (Gathering.Allowed).
    /// </para>
    /// <para>
    /// <b>The client's slab templates</b> (the older choices "claim3x3" / "claim1x1", kept selectable) [P, decoded with
    /// <see cref="VoxelTemplate"/>, checked 2026-10-18]: CommonData/Voxel/templates/claim1x1 / 2x1 / 3x1 / 3x3.otd
    /// (identical in 0.589 and 0.695), each one 2 m slab of one material in template layers y = 1..5 (y = 0 empty), every
    /// solid voxel with centred vertex offsets. claim3x3 is 525 × 5 × 524 voxels, 763,000 solid voxels in a plus (a
    /// 175-voxel column over the full depth and a 175-voxel row over the full width; its four 70 m corner tiles hold only
    /// 116,025 scattered template-air voxels, flags 3 material 0); claim1x1 is one full 175 × 5 × 175 tile, no air. No
    /// client code or table loads them [P negative], so they are server-side assets shipped with the client [I strong].
    /// The live slab is neither (a full 210 m square, not a plus or a 70 m tile) [P maps]. Those stamps keep their old
    /// rules: the template's solid voxels only (<see cref="VoxelTemplate.SolidOnly"/>; its air would pit the ground of
    /// the corner tiles), nothing within 40 m of the site's -x / +z corner (where the Starting Location then stood 10 m
    /// in), in the slab material (the templates store ARRAY_INDEX 53, "Raw Amaranthine", a stale index [I]), centred on
    /// the site with the top layer on the ground voxel at its middle (flush with the ground).
    /// </para>
    /// <para>
    /// <b>Bookkeeping.</b> Everything goes into layer 0 (the server's stamps), owned by nobody: the owner's site Design and
    /// the delete refund count only the owner's own voxels (SiteDesigns.Save, EnvironmentServer.OwnedBy). The cells are not
    /// marked permanent: the heal pass leaves layer-0 voxels inside build sites alone, and a deleted or lapsed site reverts
    /// its box (Claims delete, SiteContents.Pack: RevertBox), so the slab goes with the site. Only a site granted on a
    /// Pioneer land's surface (<see cref="Place"/> step 1) is queued; moved sites (<see cref="MoveOldSites"/>), cavern and
    /// classic-landscape sites are not, and no existing site changes by itself. Only a surface site is ever queued or
    /// stamped (<see cref="IsSurfaceSite"/>: its box centred on the generated ground at its middle; a cavern site's
    /// middle lies up to 180 m under it, and a surface slab would land on the open ground above the cavern or outside the
    /// box). The dev command "pioneerslab" queues the caller's own Pioneer site, and "pioneerslab again" stamps one that
    /// already has a slab once more in the current shape: how an owner gives an old Pioneer site (one stamped with
    /// claim3x3, or none) the full slab. A site whose box holds any building (<see cref="HasBuilding"/>: layer 1) is
    /// skipped, because the slab shows through wherever the build has no voxel of its own (its rooms fill with slate, the
    /// ground under it is cut away); "pioneerslab again force" stamps it anyway. Each queue is stamped once: the marker
    /// TerrainEdits/pioneer_slabs.txt holds a "pending" line when a site is queued ("pending … force" for a forced one)
    /// and a "stamped" (or "skipped") line when it is done; a site's last line counts (a restart in between stamps it at
    /// the next start). Claim ids are handed out again after a restart (Claims.Load: from the highest stored id), so the
    /// marker is keyed by an id that may come back as another site: a "removed" line (<see cref="ForgetSlab"/>) clears a
    /// site's slab state when it is deleted (Claims.Remove) or moved (Claims.Relocate), and when its id is handed out
    /// (Claims.Grant / PlaceConfirm: after a wiped claims table nothing was removed). The stamp runs in the background,
    /// one at a time; the changed cells go to the landscape's players from the world tick (<see cref="UpdateSlabs"/>),
    /// with a manifest note to those still loading (ZoneTerrain.NoteLoading). A remote Environment Server that is away
    /// leaves the site pending for a retry a minute later.
    /// </para>
    /// </summary>
    public static partial class Pioneer
    {
        private const string SlabMarkerFile = "pioneer_slabs.txt";
        /// <summary>The setting value (and marker kind) of the full-site slab.</summary>
        public const string SiteSlab = "site";
        /// <summary>A "site" slab's thickness in voxels (5 × 0.4 m = 2 m, the client templates' own slab).</summary>
        public const int SlabLayers = 5;
        /// <summary>
        /// Voxels the fill reaches into the generated ground and the cut above it (<b>ours</b>): the baked heights are a
        /// bilinear 2 m grid of the client's own function, a few cm off between its points; two voxels (0.8 m) keep a
        /// dip's face on the ground and no ground voxel above the slab's top whatever that rounding.
        /// </summary>
        private const int GroundMargin = 2;
        /// <summary>The Starting Location on a "site" slab: metres in from both edges at the north-west corner [P videos].</summary>
        public const float StartingLocationInset = 3f;
        /// <summary>A first login on a "site" slab arrives this high above its middle (live: a drop of about 3 m) [I].</summary>
        public const float FirstLoginDrop = 3f;
        /// <summary>The square kept clear around the Starting Location's corner (site -x / +z) by the template slabs, metres from that corner on both axes.</summary>
        private const double StartingLocationCorner = 40;
        private const double VoxelMetres = TerrainEdits.VoxelSize / 10;
        /// <summary>A "site" stamp packs the overlays it wrote every this many cells (memory).</summary>
        private const int CompactEvery = 50;
        /// <summary>
        /// claim3x3.otd's solid width: template x = 1..525 on every full-width row [P, decoded 2026-10-18 with
        /// <see cref="VoxelTemplate"/>]; what an older claim3x3 stamp covered (<see cref="StampSiteSlab"/>, pass 3).
        /// </summary>
        private const int Claim3x3Width = 525;
        /// <summary>The marker's detail on a "pending" line queued with "pioneerslab … force" (stamped over a build).</summary>
        private const string ForceMark = "force";

        private static string slabFolder;
        private static readonly object slabSync = new();
        /// <summary>Sites stamped (or given up: gone, moved off a Pioneer land) per the marker.</summary>
        private static readonly HashSet<ulong> slabsDone = new();
        /// <summary>Sites waiting for their slab, in order.</summary>
        private static readonly List<ulong> slabsPending = new();
        /// <summary>Pending sites queued with "force": stamped even with building in the box (<see cref="HasBuilding"/>).</summary>
        private static readonly HashSet<ulong> slabsForced = new();
        /// <summary>What a stamped site got: "site" or the template file, and the slab's bottom layer (global voxel y).</summary>
        private static readonly Dictionary<ulong, (string Kind, long Bottom)> slabsStamped = new();
        /// <summary>Finished stamps for the world tick: the cells to send, and the site when it got a "site" slab.</summary>
        private static readonly ConcurrentQueue<(uint Zone, List<ulong> Cells, ulong SiteSlabClaim)> slabCells = new();
        private static int slabRunning;
        private static DateTime slabRetryAt = DateTime.MaxValue;
        /// <summary>The prepared template slab (solid voxels, remapped material) as a minimal .otd, per template and material.</summary>
        private static (string Key, byte[] Otd) slabCache;

        private static WorldServerConfiguration Config => ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>
        /// The slab a setting asks for: <see cref="SiteSlab"/> ("site"), a template file (claim3x3.otd / claim1x1.otd), or
        /// null (off or unknown).
        /// </summary>
        public static string SlabKind(string setting) => (setting ?? "").Trim().ToLowerInvariant() switch
        {
            SiteSlab => SiteSlab,
            "claim3x3" => "claim3x3.otd",
            "claim1x1" => "claim1x1.otd",
            _ => null
        };

        /// <summary>The slab template's file name for the setting: claim3x3.otd / claim1x1.otd, or null ("site", off or unknown).</summary>
        public static string SlabTemplateFile(string setting) => SlabKind(setting) is string kind && kind != SiteSlab ? kind : null;

        /// <summary>
        /// Start-up (after the terrain is loaded): the marker of earlier runs, each site by its last line — stamped (or
        /// skipped) sites are never stamped again unless queued again, sites queued but not stamped (a restart in
        /// between) are queued again (with their "force"), and a "removed" line (<see cref="ForgetSlab"/>: the site was
        /// deleted or moved, or its id handed out again) leaves the id with no slab state at all.
        /// </summary>
        public static void LoadSlabs(string editsFolder)
        {
            slabFolder = editsFolder;
            string path = Path.Combine(editsFolder, SlabMarkerFile);
            string setting = Config?.PioneerSlab;
            if (SlabKind(setting) == null && !string.Equals(setting?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
                log.Warn($"PioneerSlab \"{setting}\" is not site, claim3x3, claim1x1 or off: no slabs.");
            if (!File.Exists(path))
                return;
            var last = new Dictionary<ulong, string[]>();
            var order = new List<ulong>();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] f = line.Split('\t');
                if (f.Length < 2 || !ulong.TryParse(f[1], out ulong claim))
                    continue;
                if (!last.ContainsKey(claim))
                    order.Add(claim);
                last[claim] = f;
            }
            lock (slabSync)
            {
                foreach (ulong claim in order)
                {
                    string[] f = last[claim];
                    // The id no longer names the site that had this slab: nothing stamped, nothing pending.
                    if (f[0] == "removed")
                        continue;
                    if (f[0] == "pending")
                    {
                        if (!slabsDone.Contains(claim) && !slabsPending.Contains(claim))
                            slabsPending.Add(claim);
                        if (f.Length > 3 && f[3] == ForceMark)
                            slabsForced.Add(claim);
                        continue;
                    }
                    slabsDone.Add(claim);   // "stamped" or "skipped"
                    // stamped: state, site, landscape, kind (site / template file), then x, y (the bottom layer for a
                    // "site" slab, the template's corner for a template), z, material.
                    if (f[0] == "stamped" && f.Length >= 6 && long.TryParse(f[5], out long y))
                        slabsStamped[claim] = (f[3], f[3] == SiteSlab ? y : y + 1);
                }
            }
            log.Info($"Pioneer slabs: {slabsDone.Count} site(s) done in earlier runs, {slabsPending.Count} still to stamp.");
            if (slabsPending.Count > 0)
                KickSlabs();
        }

        /// <summary>
        /// A new Pioneer surface site (<see cref="Place"/>) or the dev command "pioneerslab": queued for its slab unless the
        /// setting is off, it has one or it is not a surface site (<see cref="IsSurfaceSite"/>: a cavern site never gets
        /// one); <paramref name="again"/> ("pioneerslab again") queues a site that has one too, to stamp it in the current
        /// shape (an old site); <paramref name="force"/> ("pioneerslab … force") stamps it even with building in its box
        /// (<see cref="HasBuilding"/>, checked when the stamp runs). False when nothing was queued.
        /// </summary>
        public static bool QueueSlab(ClaimModel claim, bool again = false, bool force = false)
        {
            if (claim == null || SlabKind(Config?.PioneerSlab) == null || !IsSurfaceSite(claim))
                return false;
            if (slabFolder == null)
            {
                log.Warn($"Pioneer slab for site {claim.Id}: no terrain edits folder yet (LoadSlabs not run); not stamped.");
                return false;
            }
            lock (slabSync)
            {
                if (slabsPending.Contains(claim.Id) || (slabsDone.Contains(claim.Id) && !again))
                    return false;
                slabsDone.Remove(claim.Id);
                slabsStamped.Remove(claim.Id);
                slabsPending.Add(claim.Id);
                if (force)
                    slabsForced.Add(claim.Id);
                AppendSlabMarker("pending", claim.Id, claim.ZoneId, force ? ForceMark : null);
            }
            KickSlabs();
            return true;
        }

        /// <summary>
        /// A site's slab state is no longer that site's: it was deleted (Claims.Remove) or moved (Claims.Relocate: the
        /// slab's voxels stay at the old spot), or its id was just handed out to a new site (Claims.Grant / PlaceConfirm).
        /// Claim ids restart from the highest stored one at every start (Claims.Load), so a deleted newest site's id — or
        /// every id from 2 after a wiped claims table (the planned account reset, with TerrainEdits kept) — comes back as
        /// another site; its old marker line would refuse that site its slab (<see cref="QueueSlab"/>) and put its
        /// Starting Location and arrival at the old site's slab height (<see cref="SiteSlabSurface"/>) [P code, review
        /// 2026-11-01]. Drops the id from the stamped, done, pending and forced sets and, when it was in any of them, adds a
        /// "removed" line to the marker (<see cref="LoadSlabs"/> skips it). A stamp already running for it still finishes.
        /// </summary>
        public static void ForgetSlab(ulong claimId, uint zone, string why)
        {
            lock (slabSync)
            {
                bool known = slabsDone.Remove(claimId) | slabsStamped.Remove(claimId) | slabsPending.Remove(claimId);
                slabsForced.Remove(claimId);
                if (known)
                    AppendSlabMarker("removed", claimId, zone, why);
            }
        }

        /// <summary>
        /// World tick: the changed cells of finished stamps go to the landscape's players — and as manifest notes to those
        /// still loading (<see cref="ZoneTerrain.NoteLoading"/>: past their zone-in manifest, not yet in PlayerManager, which
        /// Broadcast alone would miss: a new player's own slab finishing late) —, a Starting Location left on the old
        /// ground under or above a new "site" slab goes onto it (<see cref="LiftStartingLocation"/>), and a stamp the
        /// Environment Server put off is tried again after a minute. Cheap when idle.
        /// </summary>
        public static void UpdateSlabs()
        {
            while (slabCells.TryDequeue(out var done))
            {
                ZoneTerrain.Broadcast(done.Cells, done.Zone);
                ZoneTerrain.NoteLoading(done.Cells, done.Zone);
                if (done.SiteSlabClaim != 0)
                    LiftStartingLocation(done.SiteSlabClaim);
            }
            if (DateTime.UtcNow >= slabRetryAt)
            {
                slabRetryAt = DateTime.MaxValue;
                KickSlabs();
            }
        }

        private static void KickSlabs()
        {
            if (Interlocked.CompareExchange(ref slabRunning, 1, 0) != 0)
                return;
            Task.Run(() =>
            {
                try
                {
                    RunSlabs();
                }
                finally
                {
                    Interlocked.Exchange(ref slabRunning, 0);
                }
                // A site queued while the last one finished.
                bool more;
                lock (slabSync)
                    more = slabsPending.Count > 0 && slabRetryAt == DateTime.MaxValue;
                if (more)
                    KickSlabs();
            });
        }

        private static void RunSlabs()
        {
            while (true)
            {
                ulong claimId;
                lock (slabSync)
                {
                    if (slabsPending.Count == 0)
                        return;
                    claimId = slabsPending[0];
                }
                try
                {
                    StampSlab(claimId);
                }
                catch (EnvironmentUnavailableException e)
                {
                    log.Warn($"Pioneer slab for site {claimId}: {e.Message} Trying again in a minute.");
                    slabRetryAt = DateTime.UtcNow.AddMinutes(1);
                    return;
                }
                catch (Exception e)
                {
                    log.Error(e, $"Pioneer slab for site {claimId} could not be stamped; left out.");
                    FinishSlab(claimId, "skipped", 0, $"failed: {e.Message}");
                }
            }
        }

        /// <summary>One site's slab (see the class notes); the marker line and the cells to broadcast.</summary>
        private static void StampSlab(ulong claimId)
        {
            ClaimModel claim = Claims.Get(claimId);
            if (claim == null || Landscapes.Get(claim.ZoneId) is not { Pioneer: true })
            {
                FinishSlab(claimId, "skipped", claim?.ZoneId ?? 0, claim == null ? "site gone" : "no longer on a Pioneer land");
                return;
            }
            string kind = SlabKind(Config?.PioneerSlab);
            if (kind == null)
            {
                FinishSlab(claimId, "skipped", claim.ZoneId, "PioneerSlab off");
                return;
            }
            // Also covers "pending" lines of earlier runs, queued before these checks existed.
            if (!IsSurfaceSite(claim))
            {
                FinishSlab(claimId, "skipped", claim.ZoneId, "not a surface site (a cavern site: its middle lies under the ground): no slab");
                return;
            }
            bool forced;
            lock (slabSync)
                forced = slabsForced.Contains(claimId);
            if (!forced && HasBuilding(claim))
            {
                FinishSlab(claimId, "skipped", claim.ZoneId, "the site has building in it (layer 1): a slab would fill its rooms "
                    + "and cut the ground from under it; \"pioneerslab again force\" stamps it anyway");
                return;
            }
            ushort material = (ushort)Math.Min(Config?.PioneerSlabMaterial ?? 26u, VoxelTemplate.InteriorAir - 1u);
            if (kind == SiteSlab)
            {
                // Material 0 would carve the footprint: the "site" slab has no stored material to fall back on.
                if (material == 0)
                    material = 26;
                (List<ulong> written, string report) = StampSiteSlab(claim, material);
                SlabBox box = SiteSlabOf(claim);
                slabCells.Enqueue((claim.ZoneId, written, claim.Id));
                FinishSlab(claimId, "stamped", claim.ZoneId, string.Join('\t', SiteSlab, box.X0, box.Bottom, box.Z0, material), (SiteSlab, box.Bottom));
                log.Info($"Pioneer slab \"site\" (material {material}) under site {claim.Id} on landscape {claim.ZoneId}, corner voxel "
                    + $"({box.X0}, {box.Bottom}, {box.Z0}), top at {box.SurfaceY:F1} m{(forced ? ", forced over its build" : "")}: {report}.");
                return;
            }
            VoxelTemplate slab = VoxelTemplate.Load(new MemoryStream(SlabOtd(kind, material)));
            (long vx, long vy, long vz) = TemplateCorner(claim, slab.Size);
            (List<ulong> cells, List<ulong> changed) = EnvironmentServer.Instance.StampTemplateAt(slab, vx, vy, vz, claim.ZoneId);
            EnvironmentServer.Instance.Flush();
            EnvironmentServer.Instance.CompactNow(true);
            if (changed.Count > 0)
                slabCells.Enqueue((claim.ZoneId, changed, 0));
            FinishSlab(claimId, "stamped", claim.ZoneId, string.Join('\t', kind, vx, vy, vz, material), (kind, vy + 1));
            log.Info($"Pioneer slab {kind} (material {material}) under site {claim.Id} on landscape {claim.ZoneId} at voxel "
                + $"({vx}, {vy}, {vz}): {VoxelTemplate.LastStampReport}.");
        }

        private static void FinishSlab(ulong claimId, string state, uint zone, string detail, (string Kind, long Bottom)? stamped = null)
        {
            lock (slabSync)
            {
                slabsPending.Remove(claimId);
                slabsForced.Remove(claimId);
                slabsDone.Add(claimId);
                if (stamped is { } s)
                    slabsStamped[claimId] = s;
                AppendSlabMarker(state, claimId, zone, detail);
            }
            if (state == "skipped")
                log.Info($"Pioneer slab for site {claimId}: {detail}.");
        }

        /// <summary>Marker line: state, site, landscape, then the details (kind, corner / bottom voxel, material). Under slabSync.</summary>
        private static void AppendSlabMarker(string state, ulong claimId, uint zone, string detail = null)
        {
            if (slabFolder == null)
                return;
            try
            {
                Directory.CreateDirectory(slabFolder);
                File.AppendAllLines(Path.Combine(slabFolder, SlabMarkerFile),
                    new[] { string.Join('\t', new[] { state, claimId.ToString(), zone.ToString(), detail }.Where(s => s != null)) });
            }
            catch (IOException e)
            {
                log.Warn($"Pioneer slabs: could not write {SlabMarkerFile} ({e.Message}).");
            }
        }

        // ---- the "site" slab ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Where a "site" slab lies, in global voxels (0.4 m; voxel n is the sample at n × 0.4 m global): its first sample
        /// column (<see cref="X0"/>, <see cref="Z0"/>), the number of sample columns across x and z (525 for a 210 m site)
        /// and its bottom layer.
        /// </summary>
        public readonly record struct SlabBox(long X0, long Z0, int Width, int Depth, long Bottom)
        {
            /// <summary>The top sample layer.</summary>
            public long Top => Bottom + SlabLayers - 1;

            /// <summary>
            /// The walking surface, local metres: the top layer's centred vertices, half a voxel above its samples (the
            /// client draws a cube's vertex at its centre, docs/terrain.md "How the client draws a dig").
            /// </summary>
            public float SurfaceY => SurfaceOf(Bottom);

            /// <summary><see cref="SurfaceY"/> of a slab whose bottom layer is <paramref name="bottom"/>.</summary>
            public static float SurfaceOf(long bottom)
                => (float)(((bottom + SlabLayers - 0.5) * TerrainEdits.VoxelSize - TerrainStore.ZoneOrigin.Y) / 10);
        }

        /// <summary>
        /// A site's "site" slab: the sample columns inside its box on x / z (n with min ≤ n × 0.4 m &lt; max, i.e. the
        /// samples whose half-voxel cells make up the box: 525 × 525, the faces on the outline when the box edges fall on
        /// half voxels, as a granted Pioneer site's do), and the bottom layer: the first air sample above the generated
        /// ground at the site's middle (samples below the height are solid), or above the box's middle without baked heights.
        /// A last +x / +z column whose centre, (n + 0.5) × 0.4 m, lies past the box's max edge is left out: the heal pass
        /// (TerrainEdits.Regenerate) and RevertBox keep a layer-0 voxel inside a site only by that centre test [P code], so
        /// that column would grow back 20 minutes after the stamp. It happens when a max edge lies less than half a voxel
        /// past a sample, as a placed site's unsnapped f64 centre (Claims.PlaceConfirm) or float corners give; a granted
        /// site's edges are exactly on half voxels, whose last centre lies exactly on the edge (kept). The -x / -z columns
        /// always have their centre inside.
        /// </summary>
        public static SlabBox SiteSlabOf(ClaimModel claim)
        {
            double v = TerrainEdits.VoxelSize;
            const double nudge = 1e-6;   // a box edge exactly on a sample stays the first one in, not the last one out
            long x0 = (long)Math.Ceiling(claim.MinX / v - nudge), x1 = (long)Math.Ceiling(claim.MaxX / v - nudge) - 1;
            long z0 = (long)Math.Ceiling(claim.MinZ / v - nudge), z1 = (long)Math.Ceiling(claim.MaxZ / v - nudge) - 1;
            // The heal's own arithmetic (cell origin + (index + 0.5) × voxel is exact in doubles: whole decimetres).
            if ((x1 + 0.5) * v > claim.MaxX)
                x1--;
            if ((z1 + 0.5) * v > claim.MaxZ)
                z1--;
            var origin = TerrainStore.ZoneOrigin;
            float mx = (float)(((claim.MinX + claim.MaxX) / 2 - origin.X) / 10), mz = (float)(((claim.MinZ + claim.MaxZ) / 2 - origin.Z) / 10);
            double ground = TerrainHeights.HeightAt(mx, mz, claim.ZoneId) is float h ? h * 10 + origin.Y : (claim.MinY + claim.MaxY) / 2;
            return new SlabBox(x0, z0, (int)(x1 - x0 + 1), (int)(z1 - z0 + 1), (long)Math.Ceiling(ground / v));
        }

        /// <summary>
        /// Writes a site's "site" slab (see the class notes) through the Environment Server into layer 0 and returns the
        /// cells written and a report for the log. Idempotent: it reads only the generated heights and cave pieces, so a
        /// second run writes the same values. Throws <see cref="EnvironmentUnavailableException"/> when the service is
        /// away before or after, or a cell's layer-0 version did not move with its write (a remote write was lost; the
        /// caller runs the whole stamp again a minute later).
        /// </summary>
        public static (List<ulong> Cells, string Report) StampSiteSlab(ClaimModel claim, ushort material)
        {
            IEnvironmentServer env = EnvironmentServer.Instance;
            if (env == null || !env.Available)
                throw new EnvironmentUnavailableException("Environment Server away.");
            SlabBox s = SiteSlabOf(claim);
            if (s.Width <= 0 || s.Depth <= 0)
                return (new List<ulong>(), "an empty site box: nothing written");
            uint zone = claim.ZoneId;
            double v = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            // The layers whose centre lies inside the box (as SiteSlabOf's columns): the heal keeps no other.
            long boxBottom = (long)Math.Ceiling(claim.MinY / v), boxTop = (long)Math.Floor(claim.MaxY / v);
            if ((boxTop + 0.5) * v > claim.MaxY)
                boxTop--;

            // Pass 1, per column: the lowest and highest layer written (fill down to the ground, cut up to it), and the cave
            // air in the fill / cut, if any.
            int columns = s.Width * s.Depth;
            var low = new long[columns];
            var high = new long[columns];
            var caves = new Dictionary<int, List<(double Lo, double Hi)>>();
            var spans = new List<(double Lo, double Hi)>();
            long minLow = long.MaxValue, maxHigh = long.MinValue;
            int noGround = 0, dips = 0, rises = 0;
            for (int k = 0; k < s.Depth; k++)
                for (int i = 0; i < s.Width; i++)
                {
                    long gx = s.X0 + i, gz = s.Z0 + k;
                    int col = k * s.Width + i;
                    float lx = (float)((gx * v - origin.X) / 10), lz = (float)((gz * v - origin.Z) / 10);
                    long ground;   // the column's top solid sample of the generated world
                    if (TerrainHeights.HeightAt(lx, lz, zone) is float h)
                        ground = (long)Math.Ceiling((h * 10 + origin.Y) / v) - 1;
                    else
                    {
                        ground = s.Bottom - 1;   // unknown: as at the middle
                        noGround++;
                    }
                    if (ground + 1 < s.Bottom)
                        dips++;
                    else if (ground > s.Top)
                        rises++;
                    long lo = Math.Max(boxBottom, Math.Min(s.Bottom, ground + 1 - GroundMargin));
                    long hi = Math.Min(boxTop, Math.Max(s.Top, ground + GroundMargin));
                    low[col] = lo;
                    high[col] = hi;
                    minLow = Math.Min(minLow, lo);
                    maxHigh = Math.Max(maxHigh, hi);
                    spans.Clear();
                    CavePieces.AirSpans(gx * v, gz * v, zone, spans);
                    foreach ((double a, double b) in spans)
                        if (a <= (s.Bottom - 1) * v && b >= lo * v || a <= hi * v && b >= (s.Top + 1) * v)
                        {
                            caves[col] = spans.ToList();
                            break;
                        }
                }

            // Pass 2, per destination cell: its voxels of every column, one cell's values at a time.
            const int size = BlockData.Size;
            byte centre = BlockData.CentreOffset;
            var cells = new List<ulong>();
            long solids = 0, air = 0, caveSkipped = 0;
            int lost = 0;
            for (long cz = FloorDiv(s.Z0, size); cz <= FloorDiv(s.Z0 + s.Depth - 1, size); cz++)
                for (long cx = FloorDiv(s.X0, size); cx <= FloorDiv(s.X0 + s.Width - 1, size); cx++)
                    for (long cy = FloorDiv(minLow, size); cy <= FloorDiv(maxHigh, size); cy++)
                    {
                        var values = new List<TerrainEdits.VoxelValue>();
                        for (int z = 0; z < size; z++)
                        {
                            long k = cz * size + z - s.Z0;
                            if (k < 0 || k >= s.Depth)
                                continue;
                            for (int x = 0; x < size; x++)
                            {
                                long i = cx * size + x - s.X0;
                                if (i < 0 || i >= s.Width)
                                    continue;
                                int col = (int)(k * s.Width + i);
                                long lo = Math.Max(low[col], cy * size), hi = Math.Min(high[col], cy * size + size - 1);
                                caves.TryGetValue(col, out var cave);
                                for (long j = lo; j <= hi; j++)
                                {
                                    bool slate = j <= s.Top;
                                    if ((j < s.Bottom || j > s.Top) && cave != null && cave.Exists(c => j * v >= c.Lo && j * v <= c.Hi))
                                    {
                                        caveSkipped++;
                                        continue;
                                    }
                                    values.Add(new TerrainEdits.VoxelValue(TerrainEdits.VoxelIndex(x, (int)(j - cy * size), z), 3,
                                        slate ? material : (ushort)0, centre, centre, centre));
                                    if (slate)
                                        solids++;
                                    else
                                        air++;
                                }
                            }
                        }
                        if (values.Count == 0)
                            continue;
                        ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                        // Every write raises the layer's version: one that didn't (a remote call that timed out) is lost.
                        uint before = env.LayerVersion(id, zone, TerrainEdits.PoiLayer);
                        env.Write(id, values, zone, 0, TerrainEdits.PoiLayer);
                        if (env.LayerVersion(id, zone, TerrainEdits.PoiLayer) == before)
                            lost++;
                        cells.Add(id);
                        if (cells.Count % CompactEvery == 0)
                        {
                            env.Flush();
                            env.CompactNow(true);
                        }
                    }

            // Pass 3: the column(s) just outside the -x face that an older claim3x3 stamp of this site covered. Its corner
            // was rounded half to even until 2026-11-01 (TemplateCorner), so on a site whose centre voxel is even (every
            // other grid column) its x = 1..525 ran one column out over the -x outline [P: the decoded claim3x3.otd and the
            // old StampSlab arithmetic, review 2026-11-01]: a 70 m strip of slate 2 m deep against the new face, whose centre
            // lies exactly on MinX, so the heal kept it. Its stored solids in that stamp's 5 layers (top on the ground voxel
            // at the box's middle) go back to the generated world (flags 0, TerrainEdits.Write). Only on such sites (that
            // stamp's first column lies outside the footprint) and only stored solids; cells with nothing in layer 0 are
            // not read.
            int strip = 0;
            long oldX = (long)Math.Round((claim.MinX + claim.MaxX) / 2 / v - (Claim3x3Width + 2) / 2.0, MidpointRounding.ToEven) + 1;
            long oldTop = (long)Math.Floor((claim.MinY + claim.MaxY) / 2 / v), oldBottom = oldTop - (SlabLayers - 1);
            for (long gx = oldX; gx < s.X0; gx++)
            {
                long cx = FloorDiv(gx, size);
                int x = (int)(gx - cx * size);
                for (long cz = FloorDiv(s.Z0, size); cz <= FloorDiv(s.Z0 + s.Depth - 1, size); cz++)
                    for (long cy = FloorDiv(oldBottom, size); cy <= FloorDiv(oldTop, size); cy++)
                    {
                        ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                        uint before = env.LayerVersion(id, zone, TerrainEdits.PoiLayer);
                        if (before == 0)
                            continue;   // nothing stored in layer 0 there
                        var mask = new byte[BlockData.Count / 8];
                        for (long z = Math.Max(0, s.Z0 - cz * size); z <= Math.Min(size - 1, s.Z0 + s.Depth - 1 - cz * size); z++)
                            for (long y = Math.Max(0, oldBottom - cy * size); y <= Math.Min(size - 1, oldTop - cy * size); y++)
                            {
                                int index = TerrainEdits.VoxelIndex(x, (int)y, (int)z);
                                mask[index >> 3] |= (byte)(0x80 >> (index & 7));   // TerrainEdits.ChangedIndices: high bit first
                            }
                        List<TerrainEdits.VoxelValue> clear = env.Read(id, mask, zone, TerrainEdits.PoiLayer)
                            .Where(o => (o.Flags & 1) != 0 && o.Material != 0)
                            .Select(o => new TerrainEdits.VoxelValue(o.Index, 0, 0, 0, 0, 0)).ToList();
                        if (clear.Count == 0)
                            continue;
                        env.Write(id, clear, zone, 0, TerrainEdits.PoiLayer);
                        if (env.LayerVersion(id, zone, TerrainEdits.PoiLayer) == before)
                            lost++;
                        if (!cells.Contains(id))
                            cells.Add(id);
                        strip += clear.Count;
                    }
            }

            env.Flush();
            env.CompactNow(true);
            if (lost > 0 || !env.Available)
                throw new EnvironmentUnavailableException($"Environment Server away during the stamp ({lost} of {cells.Count} cell(s) not written).");
            string report = $"{solids} slate voxel(s), {air} cut, {caveSkipped} on cave air skipped, {cells.Count} cell(s); "
                + $"{dips} column(s) filled down to lower ground, {rises} cut out of higher ground"
                + (noGround > 0 ? $", {noGround} without baked heights (taken as level)" : "")
                + (strip > 0 ? $", {strip} voxel(s) of an older claim3x3 stamp outside the -x face cleared" : "");
            return (cells, report);
        }

        /// <summary>
        /// The walking surface (local metres) of a site's "site" slab: stamped, or queued while the setting is "site" and
        /// the site is a surface site (a queued cavern site is skipped when its stamp runs, <see cref="IsSurfaceSite"/>);
        /// null for a site without one (none, a template slab, or not queued).
        /// </summary>
        public static float? SiteSlabSurface(ClaimModel claim)
        {
            if (claim == null)
                return null;
            lock (slabSync)
            {
                if (slabsStamped.TryGetValue(claim.Id, out var stamped))
                    return stamped.Kind == SiteSlab ? SlabBox.SurfaceOf(stamped.Bottom) : null;
                if (!slabsPending.Contains(claim.Id) || SlabKind(Config?.PioneerSlab) != SiteSlab)
                    return null;
            }
            return IsSurfaceSite(claim) ? SiteSlabOf(claim).SurfaceY : null;
        }

        /// <summary>
        /// Is this a surface site — one whose box is centred on the generated ground at its middle, within
        /// <see cref="MaxStep"/> (6 m) — as every site of <see cref="Place"/> step 1 is (<see cref="FindSpot"/>'s height is
        /// <see cref="TerrainHeights.HeightAt"/> there, and Claims.Grant centres the box on it) [P code]? A cavern site
        /// (step 2, <see cref="FindCavern"/>; or an old site moved into one, <see cref="MoveOldSites"/>) is centred on its
        /// CavernMiddle01 piece up to <see cref="MaxCavernDepth"/> under the ground, so a "site" slab, whose height comes
        /// from that ground (<see cref="SiteSlabOf"/>), would be stamped on the open surface above the cavern (or, deeper,
        /// recorded with nothing written) and the cavern's Starting Location lifted up to it [review 2026-11-01]; live
        /// gave the slab to new characters' surface sites (P4116) [P]. Only surface sites are queued or stamped. Without
        /// baked heights the site counts as a surface site, as <see cref="SiteSlabOf"/> then takes the box's middle.
        /// </summary>
        public static bool IsSurfaceSite(ClaimModel claim)
        {
            var origin = TerrainStore.ZoneOrigin;
            float mx = (float)(((claim.MinX + claim.MaxX) / 2 - origin.X) / 10), mz = (float)(((claim.MinZ + claim.MaxZ) / 2 - origin.Z) / 10);
            return TerrainHeights.HeightAt(mx, mz, claim.ZoneId) is not float h
                || Math.Abs(h * 10 + origin.Y - (claim.MinY + claim.MaxY) / 2) <= MaxStep * 10;   // decimetres
        }

        /// <summary>
        /// Does anything a builder stored lie in a site's box: a layer-1 (<see cref="TerrainEdits.BuildLayer"/>) voxel of
        /// any kind (placed, carved, vertex only), whoever put it there (a Special Access builder's too, so not
        /// EnvironmentServer.OwnedBy), with its centre inside the box (the heal's and RevertBox's test)? A slab goes into
        /// layer 0, and the client merges the block layers per voxel: a later layer's written voxel over an earlier one's,
        /// an unwritten one letting the layer below show (0.695 FUN_143fd4940 / FUN_1440498d0, trace/0695/ruin-stamping.md;
        /// docs/terrain.md: a player's doorway is unwritten air) [P]. So over a build the slab's slate fills its
        /// ground-floor rooms and doorways (up to the slab's top) and its cut takes the generated ground from under it,
        /// leaving the owner's voxels hanging; the Heal tool clears only layer 1 [P code, review 2026-11-01]. Such a site is
        /// not stamped unless forced (<see cref="QueueSlab"/>). Cells whose layer-1 version is 0 are skipped unread (a
        /// remote Environment Server's versions are the zone's own index), so an unbuilt site costs one lookup a cell
        /// (about 3,000 for a 210 × 180 × 210 m box). False without an Environment Server.
        /// </summary>
        public static bool HasBuilding(ClaimModel claim)
        {
            IEnvironmentServer env = EnvironmentServer.Instance;
            if (env == null || claim == null)
                return false;
            double v = TerrainEdits.VoxelSize;
            // Voxel n's centre is (n + 0.5) × 0.4 m: n from ceil(min / v - 0.5) to floor(max / v - 0.5).
            long x0 = (long)Math.Ceiling(claim.MinX / v - 0.5), x1 = (long)Math.Floor(claim.MaxX / v - 0.5);
            long y0 = (long)Math.Ceiling(claim.MinY / v - 0.5), y1 = (long)Math.Floor(claim.MaxY / v - 0.5);
            long z0 = (long)Math.Ceiling(claim.MinZ / v - 0.5), z1 = (long)Math.Floor(claim.MaxZ / v - 0.5);
            const int size = BlockData.Size;
            for (long cz = FloorDiv(z0, size); cz <= FloorDiv(z1, size); cz++)
                for (long cx = FloorDiv(x0, size); cx <= FloorDiv(x1, size); cx++)
                    for (long cy = FloorDiv(y0, size); cy <= FloorDiv(y1, size); cy++)
                    {
                        ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                        if (env.LayerVersion(id, claim.ZoneId, TerrainEdits.BuildLayer) == 0)
                            continue;
                        var mask = new byte[BlockData.Count / 8];
                        for (long z = Math.Max(0, z0 - cz * size); z <= Math.Min(size - 1, z1 - cz * size); z++)
                            for (long x = Math.Max(0, x0 - cx * size); x <= Math.Min(size - 1, x1 - cx * size); x++)
                                for (long y = Math.Max(0, y0 - cy * size); y <= Math.Min(size - 1, y1 - cy * size); y++)
                                {
                                    int index = TerrainEdits.VoxelIndex((int)x, (int)y, (int)z);
                                    mask[index >> 3] |= (byte)(0x80 >> (index & 7));   // TerrainEdits.ChangedIndices: high bit first
                                }
                        if (env.Read(id, mask, claim.ZoneId, TerrainEdits.BuildLayer).Exists(o => o.Flags != 0))
                            return true;
                    }
            return false;
        }

        /// <summary>
        /// Where a site's Starting Location goes on its "site" slab (Claims.EnsureStartingLocation): <see cref="StartingLocationInset"/>
        /// in from the -x and +z edges (north-west, see the class notes), on <paramref name="surface"/>.
        /// </summary>
        public static Vector3 SlabStartingLocation(ClaimModel claim, float surface)
        {
            var origin = TerrainStore.ZoneOrigin;
            return new Vector3((float)((claim.MinX - origin.X) / 10) + StartingLocationInset, surface,
                (float)((claim.MaxZ - origin.Z) / 10) - StartingLocationInset);
        }

        /// <summary>The Starting Location's spot on a site's "site" slab, or null when the site has none (<see cref="SiteSlabSurface"/>).</summary>
        public static Vector3? StartingLocationOnSlab(ClaimModel claim)
            => SiteSlabSurface(claim) is float surface ? SlabStartingLocation(claim, surface) : null;

        /// <summary>
        /// Where a first login arrives over a new site's "site" slab: <see cref="FirstLoginDrop"/> above its top, x / z
        /// kept (live: a drop of about 3 m onto the slab [I]); without one the arrival as given (1 m above the ground at the
        /// middle, which would be inside a slab). <see cref="Place"/>.
        /// </summary>
        internal static Vector3 SlabArrival(ClaimModel claim, Vector3 arrival)
            => SiteSlabSurface(claim) is float slab ? new Vector3(arrival.X, slab + FirstLoginDrop, arrival.Z) : arrival;

        /// <summary>
        /// After a "site" stamp (world tick): the site's Starting Location, when it stands inside the footprint on the old
        /// ground of a site stamped again, goes onto the slab, its x / z kept; everyone streaming it is sent the move
        /// (Props.MoveTo). Two cases: below the slab's top (buried by the fill), and above it on generated ground that the
        /// cut took away (<see cref="StampSiteSlab"/> cuts every column whose ground rises over the top up to that ground
        /// plus <see cref="GroundMargin"/> voxels; an old site's Starting Location stood 10 m in from the -x / +z corner,
        /// on ground <see cref="FindSpot"/> lets rise up to 6 m over the middle, so it was left in the air) [P code, review
        /// 2026-11-01]. One standing higher than that (on the owner's build) or on the slab already is left alone, and
        /// none is moved out of the site's box (Props.StartingLocationOn and Claims.ClaimAt test the box in 3D: a prop
        /// above it would no longer be the site's, and a new one would be placed at every entry).
        /// </summary>
        private static void LiftStartingLocation(ulong claimId)
        {
            ClaimModel claim = Claims.Get(claimId);
            if (claim == null || SiteSlabSurface(claim) is not float top
                || Props.StartingLocationOn(claim.ZoneId, claim.Id) is not Props.PlacedProp start)
                return;
            var at = new Vector3(start.Spawn.Position.X, start.Spawn.Position.Y, start.Spawn.Position.Z);
            var global = TerrainEdits.ToGlobal(at);
            if (global.X < claim.MinX || global.X > claim.MaxX || global.Z < claim.MinZ || global.Z > claim.MaxZ)
                return;
            if (TerrainEdits.ToGlobal(new Vector3(at.X, top, at.Z)).Y > claim.MaxY)
                return;
            bool buried = at.Y < top - 0.05f;
            bool onCutGround = at.Y > top + 0.05f && TerrainHeights.HeightAt(at.X, at.Z, claim.ZoneId) is float ground && ground > top
                && at.Y <= ground + GroundMargin * (float)VoxelMetres + 0.05f;
            if (!buried && !onCutGround)
                return;
            Props.MoveTo(start, new Vector3(at.X, top, at.Z));
            log.Info($"Site {claim.Id}'s Starting Location moved onto its slab ({at.Y:F1} → {top:F1} m"
                + $"{(buried ? "" : ", off the ground the stamp cut away")}).");
        }

        /// <summary>
        /// Where a template slab's corner goes (global voxel; claim3x3 / claim1x1): centred on the site on x / z — the
        /// template's solids run 1..Size, so its middle is (Size + 2) / 2 voxels from the corner — rounded half away from
        /// zero, which for claim3x3 (525 wide, so always a half) puts its x = 1..525 exactly on the footprint's 525 sample
        /// columns (<see cref="SiteSlabOf"/>: X0 = corner + 1) on every site; its top layer (template y = Size.Y) on the
        /// ground voxel at the site's middle (the site's box is centred on that ground, Claims.Grant). Until 2026-11-01 the
        /// rounding was .NET's default, half to even, which on sites whose centre voxel is even (every other grid column)
        /// put claim3x3 one column out over the -x outline [review 2026-11-01]; <see cref="StampSiteSlab"/> clears it.
        /// </summary>
        internal static (long X, long Y, long Z) TemplateCorner(ClaimModel claim, (uint X, uint Y, uint Z) size)
        {
            double voxel = TerrainEdits.VoxelSize;
            double cx = (claim.MinX + claim.MaxX) / 2, cy = (claim.MinY + claim.MaxY) / 2, cz = (claim.MinZ + claim.MaxZ) / 2;
            return ((long)Math.Round(cx / voxel - (size.X + 2) / 2.0, MidpointRounding.AwayFromZero),
                (long)Math.Floor(cy / voxel) - size.Y,
                (long)Math.Round(cz / voxel - (size.Z + 2) / 2.0, MidpointRounding.AwayFromZero));
        }

        private static long FloorDiv(long a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        // ---- the template slabs (claim3x3 / claim1x1) ----------------------------------------------------------------------

        /// <summary>
        /// The slab to stamp: the template's solid voxels outside the Starting Location's corner, in
        /// <paramref name="material"/> (0 = the stored one), as a minimal .otd (a few hundred KB; the decoded template is
        /// ~56 MB, so only these bytes are kept). A remote Environment Server gets them inline.
        /// </summary>
        private static byte[] SlabOtd(string file, ushort material)
        {
            string key = $"{file}/{material}";
            var cache = slabCache;
            if (cache.Key == key)
                return cache.Otd;
            VoxelTemplate source = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, file));
            var size = source.Size;
            VoxelTemplate solid = source.SolidOnly((x, _, z, stored) => InStartingLocationCorner(x, z, size) ? null : material == 0 ? stored : material);
            if (solid.Cells.Count == 0)
                throw new InvalidDataException($"{file} has no solid voxels.");
            byte[] otd = solid.CellsAsOtd();
            slabCache = (key, otd);
            return otd;
        }

        /// <summary>
        /// Does a template voxel (x / z from its corner) of a slab centred on a site fall within 40 m of the site's -x / +z
        /// corner on both axes, where the Starting Location stood (10 m in from that corner) when the template slabs were
        /// the default? (The "site" slab covers that corner: its Starting Location stands on it.)
        /// </summary>
        public static bool InStartingLocationCorner(int x, int z, (uint X, uint Y, uint Z) size)
        {
            double half = SiteHalfWidthDm / 10;
            double siteX = (x + 0.5 - (size.X + 2) / 2.0) * VoxelMetres + half;   // metres from the site's -x edge
            double siteZ = (z + 0.5 - (size.Z + 2) / 2.0) * VoxelMetres + half;   // metres from the site's -z edge
            return siteX < StartingLocationCorner && siteZ > 2 * half - StartingLocationCorner;
        }

        /// <summary>Dev "pioneerslab": a site's slab state (for the log).</summary>
        public static string SlabState(ulong claimId)
        {
            lock (slabSync)
                return slabsPending.Contains(claimId) ? "pending"
                    : slabsStamped.TryGetValue(claimId, out var s) ? $"done ({(s.Kind == SiteSlab ? "site" : s.Kind)})"
                    : slabsDone.Contains(claimId) ? "done" : "none";
        }
    }
}
