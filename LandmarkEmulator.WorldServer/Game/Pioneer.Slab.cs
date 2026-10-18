using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The slate slab under a new Pioneer site (OPEN-LEADS L180; trace/0695/agent-leadsC.md §L180).
    /// <para>
    /// Live: fixed problems with how the slate slab is placed on new player sites (patch notes 30 Jun 2016,
    /// design/forum/patches/2016-06-29-57596) [P that new player sites got a slate slab, placed by the server].
    /// </para>
    /// <para>
    /// The slab assets [P, decoded with <see cref="VoxelTemplate"/>, checked 2026-10-18]: the client ships
    /// CommonData/Voxel/templates/claim1x1 / 2x1 / 3x1 / 3x3.otd (identical in 0.589 and 0.695), each one 2 m slab of one
    /// material in template layers y = 1..5 (y = 0 empty), every solid voxel with centred vertex offsets (a flat box).
    /// claim3x3 is 525 × 5 × 524 voxels (210 m, the launch site's footprint, Claims: 210 × 180 × 210 m), 763,000 solid
    /// voxels in a plus: a 175-voxel (70 m) column over the full depth and a 175-voxel row over the full width; its four
    /// 70 m corner tiles hold no slab, only 116,025 scattered template-air voxels (flags 3, material 0). claim1x1 is one
    /// full 175 × 5 × 175 tile (the pre-launch 70 m site), no air. No client code or table loads them (no "claim1x1" /
    /// "claim3x3" / ".otd" string in the 0.695 decompile, not in VoxelSystemTemplates.txt) [P negative], so they are
    /// server-side assets shipped with the client, like the cavern ruins [I strong].
    /// </para>
    /// <para>
    /// What the server stamps (setting <see cref="WorldServerConfiguration.PioneerSlab"/>, Docker PIONEER_SLAB; live's
    /// choice of template and height are not known, [I]):
    /// - the template's solid voxels only (<see cref="VoxelTemplate.SolidOnly"/>): its air marks would punch scattered
    ///   holes into the ground of the corner tiles once the slab lies flush, so they are left out, as is anything within
    ///   40 m of the site's -x / +z corner, where the Starting Location stands (Claims.EnsureStartingLocation: 10 m in
    ///   from that corner) — claim3x3 has no slab there anyway (its tiles are 174 / 175 / 176 voxels wide: the middle
    ///   column is template x 175..349, so a full 70 m corner square would clip one voxel row of the north arm);
    /// - every solid voxel in <see cref="WorldServerConfiguration.PioneerSlabMaterial"/> (default 26 Worked Slate): the
    ///   templates store VoxelMaterials ARRAY_INDEX 53, "Raw Amaranthine" (an ore) in both client builds, a stale index
    ///   [I] — block data and template material maps use ARRAY_INDEX (trace/0695/ruin-stamping.md §1);
    /// - centred under the site (x / z), its top layer on the ground voxel at the site's middle, so the slab replaces the
    ///   top 2 m of the flat Pioneer ground flush with it [I: the Pioneer lands are flat (P4031), and a raised platform
    ///   would not read as ground]. Where the ground is not level the slab is partly buried or stands proud;
    /// - into layer 0 (the server's stamps), owned by nobody: the owner's site Design and the delete refund count only the
    ///   owner's own voxels (SiteDesigns.Save, EnvironmentServer.OwnedBy). The cells are not marked permanent: the
    ///   heal pass already leaves layer-0 voxels inside build sites alone, and a deleted or lapsed site reverts its box
    ///   (Claims delete, SiteContents.Pack: RevertBox), so the slab goes with the site.
    /// </para>
    /// <para>
    /// Only a site granted on a Pioneer land's surface (<see cref="Place"/> step 1) is queued; moved sites
    /// (<see cref="MoveOldSites"/>), cavern and classic-landscape sites are not. Each site is stamped once: the marker
    /// TerrainEdits/pioneer_slabs.txt holds a "pending" line when a site is queued and a "stamped" line when it is done
    /// (a restart in between stamps it at the next start; a stamped site is never stamped again). The stamp runs in the
    /// background, one at a time; the changed cells go to the landscape's players from the world tick
    /// (<see cref="UpdateSlabs"/>). A remote Environment Server that is away leaves the site pending for a retry a
    /// minute later.
    /// </para>
    /// </summary>
    public static partial class Pioneer
    {
        private const string SlabMarkerFile = "pioneer_slabs.txt";
        /// <summary>The square kept clear around the Starting Location's corner (site -x / +z), metres from that corner on both axes.</summary>
        private const double StartingLocationCorner = 40;
        private const double VoxelMetres = TerrainEdits.VoxelSize / 10;

        private static string slabFolder;
        private static readonly object slabSync = new();
        /// <summary>Sites stamped (or given up: gone, moved off a Pioneer land) per the marker.</summary>
        private static readonly HashSet<ulong> slabsDone = new();
        /// <summary>Sites waiting for their slab, in order.</summary>
        private static readonly List<ulong> slabsPending = new();
        private static readonly ConcurrentQueue<(uint Zone, List<ulong> Cells)> slabCells = new();
        private static int slabRunning;
        private static DateTime slabRetryAt = DateTime.MaxValue;
        /// <summary>The prepared slab (solid voxels, remapped material) as a minimal .otd, per template and material.</summary>
        private static (string Key, byte[] Otd) slabCache;

        private static WorldServerConfiguration Config => ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>The slab template's file name for the setting: claim3x3.otd / claim1x1.otd, or null (off or unknown).</summary>
        public static string SlabTemplateFile(string setting) => (setting ?? "").Trim().ToLowerInvariant() switch
        {
            "claim3x3" => "claim3x3.otd",
            "claim1x1" => "claim1x1.otd",
            _ => null
        };

        /// <summary>
        /// Start-up (after the terrain is loaded): the marker of earlier runs — stamped sites are never stamped again, and
        /// sites queued but not stamped (a restart in between) are queued again.
        /// </summary>
        public static void LoadSlabs(string editsFolder)
        {
            slabFolder = editsFolder;
            string path = Path.Combine(editsFolder, SlabMarkerFile);
            string setting = Config?.PioneerSlab;
            if (SlabTemplateFile(setting) == null && !string.Equals(setting?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
                log.Warn($"PioneerSlab \"{setting}\" is not claim3x3, claim1x1 or off: no slabs.");
            if (!File.Exists(path))
                return;
            var pending = new List<ulong>();
            lock (slabSync)
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] f = line.Split('\t');
                    if (f.Length < 2 || !ulong.TryParse(f[1], out ulong claim))
                        continue;
                    if (f[0] == "pending")
                        pending.Add(claim);
                    else
                        slabsDone.Add(claim);   // "stamped" or "skipped"
                }
                foreach (ulong claim in pending.Distinct())
                    if (!slabsDone.Contains(claim) && !slabsPending.Contains(claim))
                        slabsPending.Add(claim);
            }
            log.Info($"Pioneer slabs: {slabsDone.Count} site(s) done in earlier runs, {slabsPending.Count} still to stamp.");
            if (slabsPending.Count > 0)
                KickSlabs();
        }

        /// <summary>
        /// A new Pioneer surface site (<see cref="Place"/>) or the dev command "pioneerslab": queued for its slab unless the
        /// setting is off or it has one. False when nothing was queued.
        /// </summary>
        public static bool QueueSlab(ClaimModel claim)
        {
            if (claim == null || SlabTemplateFile(Config?.PioneerSlab) == null)
                return false;
            if (slabFolder == null)
            {
                log.Warn($"Pioneer slab for site {claim.Id}: no terrain edits folder yet (LoadSlabs not run); not stamped.");
                return false;
            }
            lock (slabSync)
            {
                if (slabsDone.Contains(claim.Id) || slabsPending.Contains(claim.Id))
                    return false;
                slabsPending.Add(claim.Id);
                AppendSlabMarker("pending", claim.Id, claim.ZoneId);
            }
            KickSlabs();
            return true;
        }

        /// <summary>
        /// World tick: the changed cells of finished stamps go to the landscape's players, and a stamp the Environment
        /// Server put off is tried again after a minute. Cheap when idle.
        /// </summary>
        public static void UpdateSlabs()
        {
            while (slabCells.TryDequeue(out var done))
                ZoneTerrain.Broadcast(done.Cells, done.Zone);
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
            string file = SlabTemplateFile(Config?.PioneerSlab);
            if (file == null)
            {
                FinishSlab(claimId, "skipped", claim.ZoneId, "PioneerSlab off");
                return;
            }
            ushort material = (ushort)Math.Min(Config?.PioneerSlabMaterial ?? 26u, VoxelTemplate.InteriorAir - 1u);
            VoxelTemplate slab = VoxelTemplate.Load(new MemoryStream(SlabOtd(file, material)));
            double voxel = TerrainEdits.VoxelSize;
            // Centred on the site (the template's solids run 1..Size on x and z, so its middle is at (Size + 2) / 2 voxels
            // from the corner); its top layer (template y = Size.Y) on the ground voxel at the site's middle (the site's
            // box is centred on that ground, Claims.Grant).
            double cx = (claim.MinX + claim.MaxX) / 2, cy = (claim.MinY + claim.MaxY) / 2, cz = (claim.MinZ + claim.MaxZ) / 2;
            long vx = (long)Math.Round(cx / voxel - (slab.Size.X + 2) / 2.0);
            long vz = (long)Math.Round(cz / voxel - (slab.Size.Z + 2) / 2.0);
            long vy = (long)Math.Floor(cy / voxel) - slab.Size.Y;
            (List<ulong> cells, List<ulong> changed) = EnvironmentServer.Instance.StampTemplateAt(slab, vx, vy, vz, claim.ZoneId);
            EnvironmentServer.Instance.Flush();
            EnvironmentServer.Instance.CompactNow(true);
            if (changed.Count > 0)
                slabCells.Enqueue((claim.ZoneId, changed));
            FinishSlab(claimId, "stamped", claim.ZoneId, string.Join('\t', file, vx, vy, vz, material));
            log.Info($"Pioneer slab {file} (material {material}) under site {claim.Id} on landscape {claim.ZoneId} at voxel "
                + $"({vx}, {vy}, {vz}): {VoxelTemplate.LastStampReport}.");
        }

        private static void FinishSlab(ulong claimId, string state, uint zone, string detail)
        {
            lock (slabSync)
            {
                slabsPending.Remove(claimId);
                slabsDone.Add(claimId);
                AppendSlabMarker(state, claimId, zone, detail);
            }
            if (state == "skipped")
                log.Info($"Pioneer slab for site {claimId}: {detail}.");
        }

        /// <summary>Marker line: state, site, landscape, then the details (template, corner voxel, material). Under slabSync.</summary>
        private static void AppendSlabMarker(string state, ulong claimId, uint zone, string detail = null)
        {
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
        /// corner on both axes, where Claims.EnsureStartingLocation puts the Starting Location (10 m in from that corner)?
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
                return slabsDone.Contains(claimId) ? "done" : slabsPending.Contains(claimId) ? "pending" : "none";
        }
    }
}
