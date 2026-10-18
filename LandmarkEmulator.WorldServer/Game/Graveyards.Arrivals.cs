using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Where a trip into a cavern graveyard arrives, and which graveyards a random trip may use (bug 2026-10-18: "teleporting
    /// to an underground adventure put me under the map"; docs/gameplay.md "Underground Adventure").
    /// <para>
    /// <b>The geometry [P, from the files; checked on all 1,944 island graveyards by <see cref="CheckArrivals"/>].</b> Every
    /// island's Underground .cpa has 81 type-3 cave beginning locations, all Graveyard01 pieces (tile 111) with rotation 0,
    /// and 81 graveyard_launch placements whose box minimum lies (−22.0, −26.4, −6.4) m from the piece; the box is square,
    /// so its place does not depend on the placement's quarter turn. The template corner is one voxel below and outside
    /// the box, the plinth's top solid sample is template y 11 and its first air sample y 12, so the plinth top is
    /// corner + 4.8 m = piece − 22.0 m for every piece and turn: the "22 m below the piece" of both logged trips. The
    /// arrival column (the area's offset (0, ·, 6) from the centrepiece, turned with the template like the props) is the
    /// same template spot on every graveyard: on the plinth, 7 m from its edge, 5.9 m from the nearest prop. Under it the
    /// client's own generated cave floor (CavePieces, no server edits) lies 3.4-3.8 m lower (4.0 m under the arrival on
    /// 405 graveyards, 4.4 m on 1,539): the plinth stands in the generated cavern's air, so the plinth top exists only as
    /// the server's stamped cells. No floor of the arrival's cave pocket within 40 m is lower than the stamped box's
    /// bottom, 5.4 m under the arrival. The client's own beginning location
    /// (AddCave FUN_143f813e0 copies the tile record's three doubles at +8; GetRandomCaveBeginningLocation FUN_143f84420
    /// returns them) is the piece origin itself, 22 m above the plinth, with no offset or spawn point of its own.
    /// </para>
    /// <para>
    /// <b>The fall.</b> Both logged trips (landscape 406, graveyard 38 at y −604.4 and 24 at y 435.6) went to that same
    /// template spot, and the dev world's stored cells hold the plinth at both (layer 0, version 1, identical columns). The
    /// second trip stood (movement word 0x41 → 0x69 → 0x401 → 0x441 as the loading screen dropped); the first started
    /// falling the moment its screen dropped (0x441 → 0x29) and was at −101 m/s two seconds later with the client's
    /// "fell off" bit 0x1000000 set: it had no collision under it for a long way [P, world log 01:23:30-33]. The client
    /// holds the player's physics until its feet cell and the voxel above are in its server-cell cache
    /// (FUN_143324910 via the cell manager at core +0x5a80, FUN_142af2080 / FUN_142af5110, which requests a missing
    /// cell), so the cells were there; what was missing at the drop is not visible in the logs [I]. What the first trip
    /// has that the second and every working trip before lacks: it is 2,028 m below its ground (a 1,423 m mountain
    /// above it), the second 747 m. The cavern trip of 2026-10-14 that fell (an entry at y −673.4, 1,768-1,977 m below
    /// the ground) and the −780 / −895 entries still loading after 35 s (2,300+ m) are as deep; the depth sweep's passes
    /// were 1,024-1,419 m below the ground. The old rule measured y only (≥ −650), which passes 392 graveyards that are
    /// more than 1,500 m below the ground, graveyard 38 among them. With the rules below, 1,568 of the 1,944 graveyards
    /// stay open to random trips (125 are below y −650, 251 more than 1,600 m below the ground; dev "cavecheck all").
    /// </para>
    /// <para>
    /// <b>The rules (ours) [I].</b> A random trip uses a graveyard only when (<see cref="TripProblem"/>): its arrival is at
    /// y ≥ <see cref="Caves.MinTravelDepth"/> as before, at most <see cref="TripMaxDepthBelowGround"/> below the landscape's
    /// ground (the depth the working trips stayed within), more than <see cref="UndergroundDepth"/> below it (a cavern, not
    /// the surface), and with the client's own floor at most <see cref="OwnFloorDropLimit"/> under it (so a plinth
    /// that is not there yet is a short drop, never a fall) and no floor in reach lower than the fall guard's depth
    /// (<see cref="CavernArrivalGuard"/>), which catches what is left. The arrival is <see cref="ArrivalAboveSurface"/>
    /// above the first air sample, as the cave-floor arrival of before 2026-10-17 was (Caves.Floor: 1 m above the top
    /// rock sample), not on the sample boundary of a server-stamped surface.
    /// </para>
    /// </summary>
    public static partial class Graveyards
    {
        /// <summary>Most a trip's arrival may be below the landscape's ground (metres) [I: see the class notes].</summary>
        public const float TripMaxDepthBelowGround = 1600f;
        /// <summary>The arrival above the first standable air sample (metres): 1 m above the top solid sample.</summary>
        public const float ArrivalAboveSurface = 0.6f;
        /// <summary>Most the client's own cave floor may lie under a trip's arrival (metres) [I ours].</summary>
        public const float OwnFloorDropLimit = 6f;
        /// <summary>How far around the arrival the cave's floors are looked at for <see cref="ArrivalCheck.PitDepth"/> (metres).</summary>
        public const float PitReach = 40f;
        /// <summary>The spacing of the columns looked at within <see cref="PitReach"/> (metres).</summary>
        private const float PitStep = 4f;

        /// <summary>
        /// One cavern graveyard's arrival as a trip would use it, and why a random trip would leave it out (null: it may
        /// use it). <paramref name="OwnFloorDrop"/>: how far under the arrival the client's own generated floor is (null:
        /// none within 40 m); <paramref name="PitDepth"/>: the lowest floor of the arrival's cave pocket within
        /// <see cref="PitReach"/>, below the arrival; <paramref name="Ground"/>: the landscape's ground over it.
        /// </summary>
        public sealed record ArrivalCheck(int Placement, Vector3 Piece, int Turns, bool Templated, Vector3 Centre, Vector3? Arrival,
            float? Ground, float? OwnFloorDrop, float PitDepth, bool InOwnRock, string Problem);

        /// <summary>
        /// Why a random trip must not use an arrival (null: it may): the rules in the class notes, from the arrival's height,
        /// the ground over it, how far under it the client's own floor is and the deepest floor in reach.
        /// </summary>
        public static string TripProblem(float arrivalY, float? ground, float? ownFloorDrop, float pitDepth, bool inOwnRock = false)
        {
            if (arrivalY < Caves.MinTravelDepth)
                return $"below y {Caves.MinTravelDepth:0}";
            if (ground is float g && g - arrivalY > TripMaxDepthBelowGround)
                return $"{g - arrivalY:0} m below the ground (more than {TripMaxDepthBelowGround:0})";
            if (ground is float s && s - arrivalY <= UndergroundDepth)
                return $"only {s - arrivalY:0} m below the ground";
            if (inOwnRock)
                return "in the client's own rock (only the stamp makes it air)";
            if (ownFloorDrop is not float drop)
                return "no floor of the client's own under it";
            if (drop > OwnFloorDropLimit)
                return $"the client's own floor {drop:0.0} m under it";
            if (pitDepth > CavernArrivalGuard.FallDepth - 2f)
                return $"a floor {pitDepth:0.0} m below it within {PitReach:0} m (the fall guard would take it for a fall)";
            return null;
        }

        /// <summary>
        /// The lowest standable voxel index in a column, from <paramref name="from"/> up to (not including)
        /// <paramref name="to"/>: solid under it, air at it and 0.8 m / 1.6 m above it (2 m of headroom), by
        /// <paramref name="solid"/> (true = solid at that index). Null when there is none.
        /// </summary>
        public static long? StandableIndex(Func<long, bool> solid, long from, long to)
        {
            for (long y = from; y < to; y++)
                if (solid(y - 1) && !solid(y) && !solid(y + 2) && !solid(y + 4))
                    return y;
            return null;
        }

        /// <summary>Is a local point air in the client's own generated caves (no server edits)?</summary>
        private static bool OwnAir(Vector3 local, uint zone)
        {
            (double gx, double gy, double gz) = TerrainEdits.ToGlobal(local);
            return CavePieces.IsAir(gx, gy, gz, zone);
        }

        /// <summary>How far under a point the client's own cave floor is (metres; null: none within 40 m, or the point is rock).</summary>
        private static float? OwnFloorDrop(Vector3 at, uint zone)
        {
            float step = (float)(TerrainEdits.VoxelSize / 10);
            if (!OwnAir(at, zone))
                return null;
            for (float d = step; d <= 40f; d += step)
                if (!OwnAir(at - new Vector3(0f, d, 0f), zone))
                    return d;
            return null;
        }

        /// <summary>
        /// The lowest floor of the cave pocket an arrival is in, within <see cref="PitReach"/> around it (metres below the
        /// arrival; 0 when nothing is lower): per column every <see cref="PitStep"/>, the client's cave air spans (CavePieces.AirSpans) joined
        /// where they touch, and the bottom of the one holding the arrival's height. <paramref name="lowest"/> is a floor
        /// that is known anyway (the stamped box's bottom).
        /// </summary>
        private static float PitDepth(Vector3 arrival, uint zone, float lowest)
        {
            var origin = TerrainStore.ZoneOrigin;
            double ay = (arrival.Y + 1f) * 10 + origin.Y;
            var spans = new List<(double Lo, double Hi)>();
            for (float ox = -PitReach; ox <= PitReach; ox += PitStep)
                for (float oz = -PitReach; oz <= PitReach; oz += PitStep)
                {
                    if (ox * ox + oz * oz > PitReach * PitReach)
                        continue;
                    spans.Clear();
                    CavePieces.AirSpans((arrival.X + ox) * 10 + origin.X, (arrival.Z + oz) * 10 + origin.Z, zone, spans);
                    spans.Sort();
                    // Join the spans that touch (within 1 dm), then take the joined span holding the arrival's height.
                    int k = 0;
                    while (k < spans.Count)
                    {
                        double lo = spans[k].Lo, hi = spans[k].Hi;
                        for (k++; k < spans.Count && spans[k].Lo <= hi + 1; k++)
                            hi = Math.Max(hi, spans[k].Hi);
                        if (lo <= ay && ay <= hi)
                        {
                            lowest = Math.Min(lowest, (float)((lo - origin.Y) / 10));
                            break;
                        }
                    }
                }
            return Math.Max(0f, arrival.Y - lowest);
        }

        /// <summary>
        /// A graveyard_launch graveyard's centrepiece and arrival with its corner at a global voxel and its quarter turns
        /// (the template's own centrepiece where the turned template has it; the arrival on the first standable voxel of the
        /// stamped result at the area's offset (0, ·, 6) turned with the template, <see cref="ArrivalAboveSurface"/> up),
        /// with the checks of <see cref="TripProblem"/>. The one computation <see cref="PlaceTemplated"/> and
        /// <see cref="CheckArrivals"/> share.
        /// </summary>
        private static ArrivalCheck EvaluateTemplated(VoxelTemplate template, VoxelTemplate.Prop centre, int placement, Vector3 piece,
            (long X, long Y, long Z) corner, int turns, uint zone)
        {
            float metre = (float)(TerrainEdits.VoxelSize / 10);
            Vector3 cornerLocal = CornerLocal(corner);
            (double x, double z) = template.TurnPoint(centre.Position.X / metre, centre.Position.Z / metre, turns);
            Vector3 centreAt = cornerLocal + new Vector3((float)(x * metre), centre.Position.Y, (float)(z * metre));
            Vector3 aim = centreAt + Vector3.Transform(ArrivalOffset, VoxelTemplate.Turn(turns));
            Vector3? arrival = StandingAt(template, corner, cornerLocal, turns, aim, zone) is Vector3 stand
                ? stand + new Vector3(0f, ArrivalAboveSurface, 0f) : null;
            if (arrival is not Vector3 a)
                return new ArrivalCheck(placement, piece, turns, true, centreAt, null, null, null, 0f, false, "no standable point on the plinth");
            float? ground = TerrainHeights.HeightAt(a.X, a.Z, zone);
            bool inRock = !OwnAir(a + new Vector3(0f, 1f, 0f), zone);
            float? drop = OwnFloorDrop(a, zone);
            float pit = PitDepth(a, zone, cornerLocal.Y);
            return new ArrivalCheck(placement, piece, turns, true, centreAt, a, ground, drop, pit, inRock,
                TripProblem(a.Y, ground, drop, pit, inRock));
        }

        /// <summary>The same checks for a graveyard that is only its centrepiece on the cave floor (not laid out from the template).</summary>
        private static ArrivalCheck EvaluateFloor(int placement, Vector3 piece, Vector3 centre, Vector3 arrival, uint zone)
        {
            float? ground = TerrainHeights.HeightAt(arrival.X, arrival.Z, zone);
            float? drop = OwnFloorDrop(arrival, zone);
            float pit = PitDepth(arrival, zone, arrival.Y);
            return new ArrivalCheck(placement, piece, 0, false, centre, arrival, ground, drop, pit, false,
                TripProblem(arrival.Y, ground, drop, pit));
        }

        /// <summary>A global voxel corner in local metres.</summary>
        private static Vector3 CornerLocal((long X, long Y, long Z) corner)
        {
            double voxel = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            return new Vector3((float)((corner.X * voxel - origin.X) / 10), (float)((corner.Y * voxel - origin.Y) / 10),
                (float)((corner.Z * voxel - origin.Z) / 10));
        }

        /// <summary>The template corner of a placement box: one voxel below and outside its minimum (the outposts' rule).</summary>
        private static (long X, long Y, long Z) CornerOf(RuinSlots.Box box)
        {
            double voxel = TerrainEdits.VoxelSize;
            return ((long)Math.Round(box.MinX / voxel) - 1, (long)Math.Round(box.MinY / voxel) - 1, (long)Math.Round(box.MinZ / voxel) - 1);
        }

        /// <summary>The Graveyard01 piece (local metres) a placement box belongs to (<see cref="PieceFromBox"/>).</summary>
        private static Vector3 PieceOf(RuinSlots.Box box)
        {
            var origin = TerrainStore.ZoneOrigin;
            return new Vector3((float)((box.MinX + PieceFromBox.X - origin.X) / 10), (float)((box.MinY + PieceFromBox.Y - origin.Y) / 10),
                (float)((box.MinZ + PieceFromBox.Z - origin.Z) / 10));
        }

        /// <summary>
        /// A placement file's graveyard placements: template 76 whose box is the template's turned size (83 × 22 × 83
        /// voxels = 332 × 88 × 332 dm on every island; the old continents' 76 is another, smaller graveyard and fails this
        /// test), with their quarter turns.
        /// </summary>
        private static List<(int Index, RuinSlots.Box Box, int Turns)> GraveyardPlacements(string path, (uint X, uint Y, uint Z) size)
        {
            double voxel = TerrainEdits.VoxelSize;
            var placements = new List<(int, RuinSlots.Box, int)>();
            foreach (RuinSlots.ManualTemplate m in RuinSlots.ManualTemplates(path))
            {
                int turns = (int)(m.A % 4);
                (uint X, uint Y, uint Z) turned = VoxelTemplate.TurnedSize(size, turns);
                if (m.C == CavernTemplateId && size.X > 0 && Math.Abs(m.Box.MaxX - m.Box.MinX - turned.X * voxel) < 1
                    && Math.Abs(m.Box.MaxY - m.Box.MinY - turned.Y * voxel) < 1 && Math.Abs(m.Box.MaxZ - m.Box.MinZ - turned.Z * voxel) < 1)
                    placements.Add((m.Index, m.Box, turns));
            }
            return placements;
        }

        /// <summary>A landscape's Underground cave placement file.</summary>
        private static string UndergroundFile(uint zone)
        {
            uint continent = Landscapes.Zones.Count == 0 ? zone : Landscapes.Get(zone)?.Continent ?? zone;
            return Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
        }

        /// <summary>
        /// Offline-capable check of every cavern graveyard of a landscape as a trip would see it (dev "cavecheck", and the
        /// verification over all islands): the placements, their Graveyard01 pieces (type-3 beginning locations, tile 111)
        /// and the same arrival computation the server uses (<see cref="EvaluateTemplated"/>), with the corner and turn a
        /// marker recorded when there is one. Needs only the game folder (<see cref="Load"/>), VoxelTemplate.Folder, the
        /// cave pieces and the baked heights; reads no terrain edits and spawns nothing.
        /// </summary>
        public static (List<ArrivalCheck> Checks, List<string> Notes) CheckArrivals(uint zone)
        {
            var checks = new List<ArrivalCheck>();
            var notes = new List<string>();
            string path = UndergroundFile(zone);
            if (!File.Exists(path))
            {
                notes.Add($"no Underground placement file for landscape {zone}");
                return (checks, notes);
            }
            if (CavernTemplateOrNull() is not VoxelTemplate template || template.Props.FirstOrDefault(p => p.PropId == CentrepieceProp) is not VoxelTemplate.Prop centre)
            {
                notes.Add($"no {CavernTemplate} (or no centrepiece in it)");
                return (checks, notes);
            }
            // The type-3 cave beginning locations (Caves): every one should have exactly one placement, and the other way round.
            var origin = TerrainStore.ZoneOrigin;
            byte[] d = File.ReadAllBytes(path);
            var beginnings = new List<(Vector3 At, uint Tile, uint Rotation)>();
            if (BitConverter.ToUInt32(d, 0) == 14)
                for (int i = 0, o = 8, n = BitConverter.ToInt32(d, 4); i < n && o + 39 <= d.Length; i++, o += 39)
                    if (d[o + 32] != 0 && d[o + 38] == 3)
                        beginnings.Add((new Vector3((float)((BitConverter.ToDouble(d, o + 4) - origin.X) / 10),
                            (float)((BitConverter.ToDouble(d, o + 12) - origin.Y) / 10), (float)((BitConverter.ToDouble(d, o + 20) - origin.Z) / 10)),
                            BitConverter.ToUInt32(d, o), BitConverter.ToUInt32(d, o + 28)));
            var done = stamped.GetValueOrDefault(zone);
            var placements = GraveyardPlacements(path, template.Size);
            int unmatched = 0;
            foreach ((int index, RuinSlots.Box box, int turns) in placements)
            {
                Vector3 piece = PieceOf(box);
                (long X, long Y, long Z) corner = CornerOf(box);
                int turn = turns;
                if (done != null && done.TryGetValue(index, out var was))
                    (corner, turn) = was;
                if (!beginnings.Any(b => Vector3.DistanceSquared(b.At, piece) < 1f))
                    unmatched++;
                checks.Add(EvaluateTemplated(template, centre, index, piece, corner, turn, zone));
            }
            int orphan = beginnings.Count(b => !checks.Any(c => Vector3.DistanceSquared(c.Piece, b.At) < 1f));
            var kinds = beginnings.GroupBy(b => (b.Tile, b.Rotation)).Select(g => $"{g.Count()} tile {g.Key.Tile} rotation {g.Key.Rotation}");
            notes.Add($"{beginnings.Count} type-3 beginning locations ({string.Join(", ", kinds)}), {placements.Count} {CavernTemplate} placements; "
                + $"{unmatched} placement(s) without a Graveyard01 piece, {orphan} piece(s) without a placement");
            return (checks, notes);
        }

        /// <summary>A <see cref="TripProblem"/> without its numbers, for counting.</summary>
        public static string ProblemKind(string problem)
            => problem == null ? null : problem.Contains("below the ground (more than") ? $"more than {TripMaxDepthBelowGround:0} m below the ground"
            : problem.StartsWith("only ") ? "less than 100 m below the ground" : problem;

        /// <summary>A summary of <see cref="CheckArrivals"/> for one landscape: counts by problem, the drops and depths.</summary>
        public static string CheckReport(uint zone)
        {
            (List<ArrivalCheck> checks, List<string> notes) = CheckArrivals(zone);
            if (checks.Count == 0)
                return $"landscape {zone}: " + string.Join("; ", notes);
            int ok = checks.Count(c => c.Problem == null);
            var problems = checks.Where(c => c.Problem != null).GroupBy(c => ProblemKind(c.Problem)).Select(g => $"{g.Count()} {g.Key}");
            var drops = checks.Where(c => c.OwnFloorDrop != null).Select(c => c.OwnFloorDrop.Value).ToList();
            var depths = checks.Where(c => c.Ground != null && c.Arrival != null).Select(c => c.Ground.Value - c.Arrival.Value.Y).ToList();
            return $"landscape {zone}: {checks.Count} cavern graveyards, {ok} open to random trips"
                + (problems.Any() ? $" (left out: {string.Join(", ", problems)})" : "")
                + $"; on the plinth {checks.Count(c => c.Arrival != null)}, the client's own floor under the arrival "
                + (drops.Count > 0 ? $"{drops.Min():0.0}-{drops.Max():0.0} m" : "none")
                + $", deepest floor in reach {checks.Max(c => c.PitDepth):0.0} m below, depth below the ground "
                + (depths.Count > 0 ? $"{depths.Min():0}-{depths.Max():0} m" : "unknown")
                + $", in the client's own rock {checks.Count(c => c.InOwnRock)}; " + string.Join("; ", notes);
        }

        /// <summary>
        /// The arrival a random trip may use at the cavern graveyard of a Graveyard01 piece (<see cref="CavernArrival"/> when
        /// the graveyard passes <see cref="TripProblem"/>), else null. Never builds the graveyards (world thread).
        /// </summary>
        public static Vector3? TripArrival(uint zone, Vector3 piece)
        {
            if (CavernYardsIfReady(zone) is not List<Yard> list)
                return null;
            Yard yard = list.Where(y => y.Cavern).OrderBy(y => Vector3.DistanceSquared(y.Piece, piece)).FirstOrDefault();
            if (yard == null || yard.TripProblem != null)
                return null;
            return CavernArrival(zone, piece);
        }

        /// <summary>Why a random trip leaves a graveyard out (null: it doesn't, or the graveyard isn't known yet).</summary>
        public static string TripProblemAt(uint zone, Vector3 piece)
            => CavernYardsIfReady(zone)?.Where(y => y.Cavern).OrderBy(y => Vector3.DistanceSquared(y.Piece, piece)).FirstOrDefault()?.TripProblem;

        /// <summary>
        /// Where the fall guard sends a player whose arrival failed twice (<see cref="CavernArrivalGuard"/>): the arrival of the
        /// graveyard of that landscape open to trips that lies least deep below the ground, other than the failed one.
        /// </summary>
        public static Vector3? ShallowestTripArrival(uint zone, Vector3 not)
        {
            if (CavernYardsIfReady(zone) is not List<Yard> list)
                return null;
            return list.Where(y => y.Cavern && y.TripProblem == null && (!y.Templated || y.Stamped) && Vector3.Distance(y.Arrival, not) > 50f)
                .OrderBy(y => (TerrainHeights.HeightAt(y.Arrival.X, y.Arrival.Z, zone) ?? y.Arrival.Y) - y.Arrival.Y)
                .Select(y => (Vector3?)y.Arrival).FirstOrDefault();
        }
    }
}
