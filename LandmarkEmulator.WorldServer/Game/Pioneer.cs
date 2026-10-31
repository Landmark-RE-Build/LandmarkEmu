using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// New players start on a Pioneer landscape (zones 464-468), on a build site already placed for them. From the
    /// March 2016 design notes (design/game-rules.md): Pioneer lands are flat, kept for these automatically placed
    /// sites (players can't place their own there, <see cref="Claims"/>), and the rest of the world is reached through
    /// the Landscapes window. When the Pioneer surfaces are full a new site goes under a Pioneer land, then onto a
    /// classic landscape (<see cref="Place"/>, TASKS 6.48).
    /// </summary>
    public static partial class Pioneer
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Distance between candidate site centres, local metres: a 210 m site may not come within 285 m of another
        /// site's centre (Claims.IsFree), so centres 390 m apart are the closest a grid can pack them.
        /// </summary>
        private const float Spacing = 390f;
        /// <summary>Dry land at least this high (sea level is about 1000 m local).</summary>
        private const float DryLand = 1005f;
        /// <summary>Steepest ground (rise per metre) a starting site may sit on.</summary>
        private const float MaxSlope = 0.25f;
        /// <summary>Largest height difference between a site's middle and its corners, metres.</summary>
        private const float MaxStep = 6f;
        /// <summary>How far from a landscape's centre sites are looked for, local metres.</summary>
        private const float SearchRadius = 3600f;

        /// <summary>
        /// A brand-new character (never in the world: no saved position, no site) is given a site (<see cref="Place"/>:
        /// Pioneer surface, Pioneer cavern, classic landscape) and placed in its middle. Only landscapes with baked heights are used (the ground has
        /// to be known); without one the character starts at the Spire as before. Changes <paramref name="character"/>
        /// before the zone details are sent, so the client loads the Pioneer land directly.
        /// </summary>
        public static void Settle(CharacterModel character)
        {
            if (character.PositionX != 0f || character.PositionY != 0f || character.PositionZ != 0f
                || Claims.Owns(character.Id))
                return;
            if (Place(character.Id, character.Name) is not (uint zone, Vector3 at))
                return;
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (at.X, at.Y, at.Z);
            ulong id = character.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = id };
                c.Character.Attach(model);
                model.ZoneId = zone;
                model.PositionX = at.X;
                model.PositionY = at.Y;
                model.PositionZ = at.Z;
                c.MarkModified(model, nameof(model.ZoneId), nameof(model.PositionX), nameof(model.PositionY), nameof(model.PositionZ));
            });
            log.Info($"{character.Name} starts on landscape {zone} at {at}.");
        }

        // ---- the old Pioneer lands 417-421 -------------------------------------------------------------------------------
        //
        // Until 2026-10-17 the server used zones 417-421 as the Pioneer lands; live's are 464-468 (0b3247c, OPEN-LEADS L99).
        // 417-421 are no longer loaded (Landscapes.Load), so the client is never told of them, but nothing moved what was
        // already there: the sites stayed on 417 / 419 (dev world, 2026-10-18: sites 75 and 76, after many restarts) and
        // their owners' "travel to my site" and respawn went to a landscape the server no longer serves. The user decided
        // (2026-10-17) that those sites move to the replacement land of the same biome (Landscapes.OldPioneerToNew). The
        // move is ours (live never renumbered its Pioneer lands) and runs at every start: idempotent, since a moved site is
        // no longer on 417-421.

        /// <summary>Setting MoveOldPioneerSites (Docker MOVE_OLD_PIONEER_SITES; default true = the user's decision).</summary>
        private static bool MoveOldSitesOn => ConfigurationManager<WorldServerConfiguration>.Instance.Config?.MoveOldPioneerSites != false;
        /// <summary>The move waits for the Environment Server (start-up found it unreachable); retried on the world tick.</summary>
        private static volatile bool oldSitesPending;
        private static bool oldSitesWarned;

        /// <summary>
        /// Start-up (after Spire.Load: a new spot keeps clear of the replacement land's Spire, which is built first): every
        /// build site still on an old Pioneer land (417-421) moves to its replacement (<see cref="Landscapes.OldPioneerToNew"/>),
        /// logged per site. With the setting off they are only listed (left where they are: unreachable). Needs the
        /// Environment Server (the owner's old build is read into a design, the Spire may be stamped, veins are cleared): when
        /// it can't be reached yet the move runs on the first world tick that can (<see cref="UpdateOldSites"/>).
        /// </summary>
        public static void MoveOldSites()
        {
            List<ClaimModel> old = Claims.All().Where(c => Landscapes.OldPioneerToNew.ContainsKey(c.ZoneId)).OrderBy(c => c.Id).ToList();
            if (old.Count == 0)
            {
                log.Info("Old Pioneer lands 417-421: no build sites left on them.");
                return;
            }
            if (!MoveOldSitesOn)
            {
                log.Warn($"Old Pioneer lands 417-421: {old.Count} build site(s) stay there, unreachable (MoveOldPioneerSites off): "
                    + string.Join(", ", old.Select(c => $"site {c.Id} on {c.ZoneId} (owner 0x{c.OwnerGuid:X})")));
                return;
            }
            if (!Terrain.EnvironmentServer.Instance.Available)
            {
                oldSitesPending = true;
                log.Warn($"Old Pioneer lands 417-421: {old.Count} build site(s) move when the Environment Server is reachable.");
                return;
            }
            int moved = 0;
            foreach (ClaimModel claim in old)
                if (MoveOldSite(claim))
                    moved++;
            log.Info($"Old Pioneer lands 417-421: moved {moved} of {old.Count} build site(s) to the Pioneer lands 464-468"
                + (moved < old.Count ? " (the rest stay and are tried again at the next start)." : "."));
        }

        /// <summary>World tick: the move <see cref="MoveOldSites"/> put off, once the Environment Server answers.</summary>
        public static void UpdateOldSites()
        {
            if (!oldSitesPending)
                return;
            if (!Terrain.EnvironmentServer.Instance.Available)
            {
                if (!oldSitesWarned)
                    log.Warn("Old Pioneer lands 417-421: the site move waits for the Environment Server.");
                oldSitesWarned = true;
                return;
            }
            oldSitesPending = false;
            MoveOldSites();
        }

        /// <summary>
        /// Moves one site from an old Pioneer land: a design of the owner's build there is saved first, as for a lapsed
        /// site (<see cref="SiteDesigns.Save"/>; the voxels can't follow, the replacement land's ground is different), then
        /// the site takes the spot <see cref="FindPlace"/> gives with its replacement land first (same biome), keeping its
        /// id, owner, name, tags, access and settings (<see cref="Claims.Relocate"/>). The old build's voxels and props stay
        /// on the old land, unseen (never deleted here). The owner's Starting Location is placed on the new site at their
        /// next arrival (Claims.EnsureStartingLocation). False when no landscape has room (the site stays, logged).
        /// </summary>
        private static bool MoveOldSite(ClaimModel claim)
        {
            uint from = claim.ZoneId, to = Landscapes.OldPioneerToNew[from];
            if (FindPlace(to) is not (uint zone, Vector3 centre, Vector3 _, string step))
            {
                log.Warn($"Site {claim.Id} \"{claim.Name}\" on old Pioneer land {from}: no landscape has room; it stays there.");
                return false;
            }
            string design;
            try
            {
                design = SiteDesigns.Save(claim).Design is { } saved ? $"design {saved.Id} saved" : "nothing built, no design";
            }
            catch (Exception e)
            {
                log.Warn(e, $"Site {claim.Id}: could not save a design of its build on {from}; moving it anyway.");
                design = "design not saved";
            }
            var global = Terrain.TerrainEdits.ToGlobal(centre);
            if (!Claims.Relocate(claim.Id, zone, global.X, global.Y, global.Z))
                return false;
            log.Info($"Site {claim.Id} \"{claim.Name}\" (owner 0x{claim.OwnerGuid:X}) moved from old Pioneer land {from} to {zone} "
                + $"({step}{(zone == to ? "" : $"; {to} had no room")}) at {centre}; {design}.");
            return true;
        }

        /// <summary>
        /// At login (before the zone details): a character saved on an old Pioneer land (417-421, no longer served: the zone
        /// details would name the home island while the server kept them on the old land) starts at their own site when it
        /// is on a served landscape (it was moved, <see cref="MoveOldSites"/>), else at the Spire of the replacement land
        /// (its centre without one). Ours, as <see cref="Landscapes.LeaveClosedEventIsland"/>.
        /// </summary>
        public static void LeaveOldLand(CharacterModel character)
        {
            if (!Landscapes.OldPioneerToNew.TryGetValue(character.ZoneId, out uint to))
                return;
            uint from = character.ZoneId, zone;
            Vector3 at;
            if (Claims.ArrivalOf(character.Id) is (Vector3 site, uint siteZone, _) && Landscapes.Get(siteZone) != null)
                (zone, at) = (siteZone, site);
            else
                (zone, at) = (to, Spire.ArrivalOn(to) ?? Landscapes.ArrivalPoint(to));
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (at.X, at.Y, at.Z);
            ulong id = character.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = id };
                c.Character.Attach(model);
                model.ZoneId = zone;
                model.PositionX = at.X;
                model.PositionY = at.Y;
                model.PositionZ = at.Z;
                c.MarkModified(model, nameof(model.ZoneId), nameof(model.PositionX), nameof(model.PositionY), nameof(model.PositionZ));
            });
            log.Info($"{character.Name} was on the old Pioneer land {from}: starts on {zone} at {at}.");
        }

        /// <summary>
        /// Grants a new character's site in the live order (TASKS 6.48; P4116, 21 Mar 2016: a new character gets a
        /// build site, first on a Pioneer Landscape while there is room, then in an underground cavern of a Pioneer
        /// Landscape, and last on a classic landscape once the Pioneer islands are full; P4361, 1 Apr 2016: new
        /// characters may get an underground cave site on the Pioneer islands when their surface is full):
        ///   1. the surface of the least-crowded Pioneer land (<see cref="FindSpot"/>);
        ///   2. an underground cavern of a Pioneer land, near the surface (<see cref="FindCavern"/>);
        ///   3. the surface of the least-crowded classic landscape.
        /// Returns the landscape and the local spot to stand on, or null when every step is full (no site then). A site on a
        /// Pioneer surface gets the slate slab (<see cref="QueueSlab"/>, Pioneer.Slab.cs, L180).
        /// </summary>
        public static (uint Zone, Vector3 At)? Place(ulong ownerGuid, string ownerName)
        {
            if (FindPlace() is not (uint zone, Vector3 centre, Vector3 arrival, string step))
            {
                log.Warn($"No landscape has room for {ownerName}'s starting site (Pioneer surfaces, Pioneer caverns and classic landscapes all full).");
                return null;
            }
            var global = Terrain.TerrainEdits.ToGlobal(centre);
            ClaimModel claim = Claims.Grant(ownerGuid, ownerName, zone, global.X, global.Y, global.Z);
            log.Info($"{ownerName}: starting site on {zone} ({step}) at {centre}.");
            if (step == StepPioneerSurface)
                QueueSlab(claim);
            return (zone, arrival);
        }

        /// <summary>
        /// Where a site would go in the live order of <see cref="Place"/> (nothing is granted): the landscape, the site's
        /// centre (local metres; a surface site's on the ground, a cavern site's at its CavernMiddle01 piece), where its
        /// owner stands (1 m above a surface site's middle and clear of trees, 10 m above a cavern's floor piece) and which
        /// step found it. <paramref name="preferred"/> (a Pioneer land) is tried first at each Pioneer step, before the
        /// least-crowded order (<see cref="MoveOldSites"/>: a site keeps its biome). Null when every step is full.
        /// </summary>
        public static (uint Zone, Vector3 Centre, Vector3 Arrival, string Step)? FindPlace(uint? preferred = null)
        {
            var pioneer = Landscapes.Zones.Values.Where(z => z.Pioneer).OrderBy(z => z.Id == preferred ? 0 : 1)
                .ThenBy(z => Claims.CountOn(z.Id)).ThenBy(z => z.Id).ToList();
            var classic = Landscapes.Zones.Values.Where(z => !z.Pioneer).OrderBy(z => Claims.CountOn(z.Id)).ThenBy(z => z.Id).ToList();

            // 1. Pioneer surface. Only landscapes with baked heights (the ground has to be known); only the chosen land's
            // grid is loaded (FindSpot), the others are just checked on disk.
            foreach (Landscapes.Zone land in pioneer.Where(z => Terrain.TerrainHeights.Available(z.Id)))
                if (FindSpot(land.Id) is Vector3 spot)
                    return (land.Id, spot, Gathering.ClearOfTrees(spot + new Vector3(0f, 1f, 0f), land.Id), StepPioneerSurface);

            // 2. A cavern under a Pioneer land.
            foreach (Landscapes.Zone land in pioneer)
                if (FindCavern(land) is (Vector3 centre, Vector3 arrival))
                    return (land.Id, centre, arrival, "Pioneer cavern: the Pioneer surfaces are full");

            // 3. A classic landscape's surface.
            foreach (Landscapes.Zone land in classic.Where(z => Terrain.TerrainHeights.Available(z.Id)))
                if (FindSpot(land.Id) is Vector3 spot)
                    return (land.Id, spot, Gathering.ClearOfTrees(spot + new Vector3(0f, 1f, 0f), land.Id), "classic landscape (Pioneer lands full)");

            return null;
        }

        /// <summary>
        /// The free spot nearest a landscape's centre on a <see cref="Spacing"/> grid (so sites line up in rows) with dry,
        /// gentle ground under the whole site (its middle and four points 90 m out diagonally), clear of the landscape's
        /// Portal Spire (every landscape has one at its centre, TASKS 6.49: the grid's middle cell is the Spire's).
        /// </summary>
        private static Vector3? FindSpot(uint zone)
        {
            // The Spire is built first so its platform is known; a site's box (±105 m) may not reach it.
            var spire = Spire.PlatformOn(zone, SiteHalfWidthDm);
            int rings = (int)(SearchRadius / Spacing);
            for (int ring = 0; ring <= rings; ring++)
                for (int i = -ring; i <= ring; i++)
                    for (int j = -ring; j <= ring; j++)
                    {
                        if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring)
                            continue;   // only the square ring's edge: inner cells were tried already
                        float x = i * Spacing, z = j * Spacing;
                        if (Ground(zone, x, z) is not float y || !Flat(zone, x, z, y))
                            continue;
                        var global = Terrain.TerrainEdits.ToGlobal(new Vector3(x, y, z));
                        if (spire is var (lo, hi) && global.X > lo.X && global.X < hi.X && global.Z > lo.Z && global.Z < hi.Z)
                            continue;
                        if (Spire.BlocksSite(zone, global.X, global.Y, global.Z, SiteHalfWidthDm, SiteHalfHeightDm))
                            continue;   // the Spire area (90 m round the Hub): no build sites, as for players
                        if (Claims.IsFree(zone, global.X, global.Z))
                            return new Vector3(x, y, z);
                    }
            return null;
        }

        /// <summary><see cref="FindPlace"/>'s first step: the surface of a Pioneer land (the only sites that get a slab).</summary>
        private const string StepPioneerSurface = "Pioneer surface";

        /// <summary>Half a build site's width (Claims: 210 m sites), decimetres.</summary>
        private const double SiteHalfWidthDm = 1050;

        /// <summary>Half a build site's height (Claims: 180 m), decimetres.</summary>
        private const double SiteHalfHeightDm = 900;

        /// <summary>The cavern piece of the client's cave placements (Caves.CavernPiece: CavernMiddle01, verified open).</summary>
        private const uint CavernPiece = 104;
        /// <summary>
        /// Deepest a cavern site's middle may sit under the ground above it, metres. P4298 (21 Mar 2016): no build site
        /// far from the surface underground; the live limit is not in the client yet
        /// (TASKS 6.47), so this is INFERRED: one site height (180 m).
        /// </summary>
        private const float MaxCavernDepth = 180f;
        /// <summary>.cpa tile record: size, and the beginning-location flag (@32) and type (@38) bytes (Caves.cs class notes).</summary>
        private const int RecordSize = 39, BeginningFlagAt = 32, BeginningTypeAt = 38;
        /// <summary>Beginning-location type 2: a Pioneer isolated cavern (Caves.cs class notes).</summary>
        private const byte IsolatedCavernType = 2;

        /// <summary>Each Pioneer continent's isolated caverns (their site centres, local metres), read once.</summary>
        private static readonly Dictionary<uint, List<Vector3>> isolatedCaverns = new();

        /// <summary>
        /// Step 2: a free cavern under a Pioneer land, nearest its centre, not far under the surface, one site per cavern.
        /// Returns the site's centre (a CavernMiddle01 piece's origin in the middle of the cavern) and where the player
        /// arrives (10 m above it, as Caves.ArrivalOffset: open air in every cavern piece; they drop to its floor).
        ///
        /// The Pioneer lands are zones 464-468 (continents 440-444, Landscapes.PioneerZones), whose
        /// CommonData/Voxel/caves/ContinentCavePlacements[continent]_Underground.cpa files hold no Chaos Caverns but
        /// ~78,000 pieces of isolated caverns each (OPEN-LEADS L99). The client's own data marks each isolated cavern
        /// with one cave beginning location of type 2 (tile record @32 set, @38 = 2; Caves.cs class notes) [P data]: 957
        /// on 440, 950 / 1007 / 997 / 984 on 441-444 (trace/0695/agent-leads3-c.md L99, L51). P4300 says the isolated
        /// caverns are kept only for automatically placed build sites, so the type-2 location is the per-cavern site
        /// anchor [I].
        ///
        /// [P data, checked 2026-10-17 over all five files] each type-2 record starts a contiguous run of its cavern's
        /// pieces, up to the next flagged record: 65-101 records, CavernCorner / Wall / Middle pieces all at the anchor's
        /// height plus one Cap01 (tile 32), every piece within 1 km of the anchor and every run with CavernMiddle01
        /// pieces; the caverns are ~460-600 m square, laid out from the anchor's corner. The site's centre is the run's
        /// CavernMiddle01 nearest the run's x / z middle (at most 133 m from it). Before 2026-10-17 every CavernMiddle01
        /// (23,168 on 440) was a candidate, so two sites could share one cavern.
        /// </summary>
        private static (Vector3 Centre, Vector3 Arrival)? FindCavern(Landscapes.Zone land)
        {
            List<Vector3> caverns;
            lock (isolatedCaverns)
                if (!isolatedCaverns.TryGetValue(land.Continent, out caverns))
                {
                    caverns = ReadIsolatedCaverns(land.Continent);
                    if (caverns == null)
                        return null;
                    isolatedCaverns[land.Continent] = caverns;
                    log.Info($"Pioneer: {caverns.Count} isolated caverns under continent {land.Continent}.");
                }
            foreach (Vector3 cavern in caverns.OrderBy(c => c.X * c.X + c.Z * c.Z))
            {
                // Not far from the surface (P4298); without baked heights for the land the depth can't be checked.
                if (Terrain.TerrainHeights.HeightAt(cavern.X, cavern.Z, land.Id) is not float surface || surface - cavern.Y > MaxCavernDepth)
                    continue;
                var global = Terrain.TerrainEdits.ToGlobal(cavern);
                if (Claims.IsFree(land.Id, global.X, global.Y, global.Z))
                    return (cavern, cavern + new Vector3(0f, 10f, 0f));
            }
            return null;
        }

        /// <summary>
        /// The site centre of every isolated cavern of a continent (<see cref="FindCavern"/>): per type-2 beginning
        /// location, the CavernMiddle01 of its run nearest the run's x / z middle. Null when the file is missing or
        /// unreadable.
        /// </summary>
        private static List<Vector3> ReadIsolatedCaverns(uint continent)
        {
            string dir = Landscapes.GameDirectory;
            if (string.IsNullOrEmpty(dir))
                return null;
            string path = Path.Combine(dir, "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
            if (!File.Exists(path))
                return null;
            var centres = new List<Vector3>();
            try
            {
                byte[] d = File.ReadAllBytes(path);
                int count = Math.Min(BitConverter.ToInt32(d, 4), (d.Length - 8) / RecordSize);
                var origin = Terrain.TerrainStore.ZoneOrigin;
                static int At(int i) => 8 + i * RecordSize;
                for (int i = 0; i < count; i++)
                {
                    int o = At(i);
                    if (d[o + BeginningFlagAt] == 0 || d[o + BeginningTypeAt] != IsolatedCavernType)
                        continue;
                    // The cavern's run: this record up to the next flagged one.
                    int end = i + 1;
                    while (end < count && d[At(end) + BeginningFlagAt] == 0)
                        end++;
                    double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                    for (int j = i; j < end; j++)
                    {
                        double x = BitConverter.ToDouble(d, At(j) + 4), z = BitConverter.ToDouble(d, At(j) + 20);
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minZ = Math.Min(minZ, z);
                        maxZ = Math.Max(maxZ, z);
                    }
                    double midX = (minX + maxX) / 2, midZ = (minZ + maxZ) / 2, best = double.MaxValue;
                    int pick = -1;
                    for (int j = i; j < end; j++)
                    {
                        if (BitConverter.ToUInt32(d, At(j)) != CavernPiece)
                            continue;
                        double dx = BitConverter.ToDouble(d, At(j) + 4) - midX, dz = BitConverter.ToDouble(d, At(j) + 20) - midZ;
                        if (dx * dx + dz * dz < best)
                        {
                            best = dx * dx + dz * dz;
                            pick = j;
                        }
                    }
                    if (pick >= 0)
                    {
                        int p = At(pick);
                        centres.Add(new Vector3((float)((BitConverter.ToDouble(d, p + 4) - origin.X) / 10),
                            (float)((BitConverter.ToDouble(d, p + 12) - origin.Y) / 10), (float)((BitConverter.ToDouble(d, p + 20) - origin.Z) / 10)));
                    }
                    i = end - 1;
                }
            }
            catch (Exception e)
            {
                log.Warn($"Could not read {path}: {e.Message}");
                return null;
            }
            return centres;
        }

        private static bool Flat(uint zone, float x, float z, float y)
        {
            foreach ((float dx, float dz) in new[] { (-90f, -90f), (90f, -90f), (-90f, 90f), (90f, 90f) })
                if (Ground(zone, x + dx, z + dz) is not float corner || MathF.Abs(corner - y) > MaxStep)
                    return false;
            return true;
        }

        private static float? Ground(uint zone, float x, float z) =>
            Terrain.TerrainHeights.HeightAt(x, z, zone) is float y && y >= DryLand
                && Terrain.TerrainHeights.Slope(x, z, 2f, zone) <= MaxSlope ? y : null;
    }
}
