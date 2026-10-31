using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
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
    /// The landscapes' own graveyards (FEATURES #47; full write-up trace/0695/agent-graveyards.md). Wiki "Graveyard":
    /// graveyards are spread over the landscapes' surface and deep in the Chaos Caverns, where they give a safe buffer
    /// zone that roaming monsters don't attack; none lies inside a build site's limits; the map shows them as small red
    /// circles.
    /// <para>
    /// <b>What the client holds [P].</b> A graveyard is a static area: GlobalAreas / GlobalAreaShapes / AreaProperties
    /// area <see cref="GraveyardArea"/> 23 is a sphere of radius 30 m (GlobalAreaShapes row 20: shape 1, params 0, 3, 6,
    /// 30 = offset x, y, z and radius, as rows 6 / 8 read) with five properties: type 33 Graveyard
    /// (AreaPropertyDataGraveyard, type function FUN_143db3540 returns 0x21; no client area class), type 17
    /// TerrainPermission with INT_PARAM1 4, type 18 ProcedurallyPlacedEntities with INT_PARAM1 1 ("Prevent Spawning",
    /// UI/Default/Data.xml TriggerVolumePropertyTypes), and the server-only types 36 and 37 (no client class, no Data.xml
    /// entry; meaning not found yet). A prop spawn names its static area at base +0x260 (PlayerUpdateAddLightweightProp
    /// StaticAreaId, 0.695 FUN_142d54040; movers-695.md §3). The client's TerrainPermission area class
    /// (ClientAreaTerrainPermission, vtable 1447d2450) turns every sphere shape of an area whose INT_PARAM1 (+0x68) is 4 into
    /// a ClaimPlacementExclusionZoneSphere centred on the prop with the shape's radius (FUN_1436de260) and files it with
    /// the claims manager (DAT_145431190, FUN_142db3880). Placing a build site then fails client-side with
    /// "Claims.InvalidClaimLocation" (code 3) when the site's box comes within the radius plus the box's half-diagonal of
    /// the prop (FUN_142dc6870 → sphere test FUN_1436ddd80). Those spheres are the map's red circles [I: the map drawing
    /// itself was not traced; the claims manager is what the map's close handler and "Show Buildable Land" use].
    /// </para>
    /// <para>
    /// <b>Where they are.</b> Chaos Caverns [P]: every island's ContinentCavePlacements[c]_Underground.cpa has 81
    /// Graveyard01 cave pieces (VoxelCaveTileDefinitions 111) and 81 ManuallyPlacedExportedTemplateData boxes of
    /// 33.2 × 8.8 × 33.2 m = graveyard_launch.otd (Graveyard Centerpiece 1518, gravestones, glowing mushrooms, gargoyles);
    /// in every v14 file (islands 391-416) each box's minimum lies at (−22.0, −26.4, −6.4) m from its Graveyard01 piece,
    /// whatever the box's first field (1-4), so the graveyard's middle is at (−5.4, ·, +10.2) m from the piece and its
    /// floor about 22 m below the piece's origin (centrepiece at template y 3.2 m). Live arrival into a cavern was the
    /// graveyard ("Porting from my claim to a chaos cavern (graveyard)", forum 57166). Those boxes are the .cpa's
    /// ManuallyPlacedExportedTemplateData records {u32 record version, Room, u32 a, u32 b, u32 c} (RuinSlots.ManualTemplates;
    /// trace/0695/agent-leads2-d1.md L47): on the islands always template c = 76 with b = 4 and a = 1..4, the quarter turn
    /// (VoxelTemplate's turn notes: its parity proven by the old continents' non-square templates, its sense inferred). The
    /// retail client parses them and never uses them (their only collector FUN_143ff7bc0 has no reference) [P], so live's
    /// server built them into the terrain [I], as ours stamps the outpost ruins: graveyard_launch.otd (9 cells: a 26 m
    /// plinth 4.4 m high in a 33.2 m box of explicit air, 25 props) stamped once per placement with its corner one voxel
    /// below and outside the box minimum (the ruins' rule, ruin-stamping.md §5), turned in its box, its props where the
    /// turned template puts them (<see cref="StampMissing"/>; setting CavernGraveyardStamps, default on). The surface [I]: no client file
    /// places them (the Surface .cpa files hold only caves; no table names a graveyard; the prop "Graveyard Origin Marker"
    /// 1480 is server-only), so live's server placed them (P6194, 28 Jul 2016: graveyards on the Pioneer islands are
    /// never sunk too deep into the ground). Ours: one per 1 km square of dry, level land (<see
    /// cref="SurfaceSpacing"/>),
    /// clear of the landscape's middle (the Spire), made of graveyard01.otd — the props-only graveyard template (Graveyard
    /// Centerpiece 1518 and 42 gravestones / fence posts; no voxels, so nothing is written to the terrain). Setting
    /// WorldServer.json SurfaceGraveyards (Docker SURFACE_GRAVEYARDS, default on).
    /// </para>
    /// <para>
    /// <b>What the server does.</b> Respawn: the nearest graveyard of the player's layer is a Flee site
    /// (<see cref="SitesFor"/>). Safety: no creature picks or keeps a player inside a graveyard's sphere as its target
    /// (the wiki's buffer zone; radius = the area's 30 m [I: that the buffer is this sphere is inferred — it is the only
    /// volume the graveyard carries, and its Prevent Spawning property is the same idea]), and nothing procedural (trees,
    /// creatures) spawns inside it (type 18). Build sites: the server refuses a site the client would refuse (code 3), and
    /// a graveyard that an existing build site already overlaps stays hidden while the site is there [I: live removed such
    /// sites instead, P3540 / P3548 May 2015; we keep the player's site]. Bandwidth: the surface centrepieces of the
    /// player's landscape go once on arrival (one prop each, so every red circle and placement check is known); the rest of
    /// a graveyard's props stream within <see cref="ViewRange"/>; cavern centrepieces stream by 3D distance; nothing is sent
    /// per tick.
    /// </para>
    /// </summary>
    public static partial class Graveyards
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>PropDefs 1518 "Graveyard Centerpiece" (Models 1302 Theme_Widgets_GraveyardPieces_Center01).</summary>
        public const uint CentrepieceProp = 1518;
        /// <summary>GlobalAreas id of a graveyard (the area with the Graveyard property, type 33).</summary>
        public const uint GraveyardArea = 23;
        /// <summary>The graveyard area's sphere radius, metres (GlobalAreaShapes row 20).</summary>
        public const float SafeRadius = 30f;
        /// <summary>VoxelCaveTileDefinitions 111 Biome_Caves_Pieces_Graveyard01.cav.</summary>
        private const uint GraveyardTile = 111;
        /// <summary>The graveyard's middle from its Graveyard01 piece (metres; measured on every v14 placement file).</summary>
        private static readonly Vector3 CavernMiddleOffset = new(-5.4f, 0f, 10.2f);
        /// <summary>Where to start looking down for the cavern graveyard's floor: inside the template box (top at −17.6 m).</summary>
        private const float CavernSearchBelowPiece = 18f;
        /// <summary>
        /// Where a player arrives, from the centrepiece (metres): the area sphere's own offset (0, 3, 6) on the ground [I:
        /// the offset is the area's; that live put arriving players there is ours].
        /// </summary>
        private static readonly Vector3 ArrivalOffset = new(0f, 0f, 6f);
        /// <summary>
        /// A player this far below the ground is on the cavern layer (metres) [I]: surface caves are a few dozen metres deep,
        /// the launch Chaos Caverns' pieces several hundred.
        /// </summary>
        private const float UndergroundDepth = 100f;
        /// <summary>
        /// Surface placement (all ours [I]): grid square, landscape half-width (the baked grids span ±4000 m), Spire
        /// clearance, dry land (sea level ~1000 m), the footprint's half-width (graveyard01 is 25.6 × 23.6 m), the largest
        /// ground step under it, the steepest slope and the seeded tries per square. On the baked grids this gives about
        /// 25-36 graveyards on a launch island (391, 395, 406) and 64 on the old Pioneer land 417 (now unlisted).
        /// </summary>
        private const float SurfaceSpacing = 1000f, LandscapeHalf = 4000f, SpireClear = 500f, DryLand = 1005f, MaxStep = 4f,
            Footprint = 13f, MaxSlope = 0.4f;
        private const int Tries = 60;
        private const string SurfaceTemplate = "graveyard01.otd";
        /// <summary>The cavern graveyard template and its placement id in the islands' cave files (L47).</summary>
        private const string CavernTemplate = "graveyard_launch.otd";
        private const uint CavernTemplateId = 76;
        /// <summary>
        /// A placement box's minimum to its Graveyard01 piece (global dm): (+220, +264, +64) in every v14 file, whatever the
        /// turn (the box is square), so the unstamped layout stands exactly where it stood when it was found from the pieces.
        /// </summary>
        private static readonly (double X, double Y, double Z) PieceFromBox = (220, 264, 64);
        /// <summary>Graveyard prop guids: "GY" in the top bytes, the graveyard's index from bit 8, the prop's below.</summary>
        private const ulong GuidBase = 0x4759_0000_0000_0000;
        private const float ViewRange = 160f, DropRange = 220f;
        /// <summary>How long a graveyard's "covered by a build site" answer is kept.</summary>
        private static readonly TimeSpan ClaimRecheck = TimeSpan.FromSeconds(30);

        /// <summary>WorldServer.json SurfaceGraveyards (read at start-up).</summary>
        public static bool SurfaceEnabled { get; private set; } = true;
        /// <summary>WorldServer.json CavernGraveyardStamps (read at start-up): stamp the cavern graveyards' template.</summary>
        public static bool StampsEnabled { get; private set; } = true;
        private static string gameDirectory, editsFolder;
        /// <summary>Each landscape's stamped cavern graveyards from its marker: placement → corner, turns.</summary>
        private static readonly ConcurrentDictionary<uint, Dictionary<int, ((long X, long Y, long Z) Corner, int Turns)>> stamped = new();
        /// <summary>Stamped cells for the players of a landscape, handed from the stamping task to the world tick.</summary>
        private static readonly ConcurrentQueue<(uint Zone, List<ulong> Cells)> stampedCells = new();
        /// <summary>One landscape's graveyards are stamped at a time.</summary>
        private static readonly SemaphoreSlim stampGate = new(1, 1);
        private static readonly ConcurrentDictionary<uint, string> stampStatus = new();
        private static VoxelTemplate cavernTemplate;
        private static bool cavernTemplateTried;

        private sealed class Yard
        {
            public int Index;
            public uint Zone;
            public bool Cavern;
            /// <summary>A cavern graveyard's Graveyard01 piece origin (local metres): its cave beginning location (Caves).</summary>
            public Vector3 Piece;
            /// <summary>The centrepiece's foot (local metres): the area's centre.</summary>
            public Vector3 Centre;
            public Vector3 Arrival;
            /// <summary>The centrepiece first (it carries the static area), then the template's other props.</summary>
            public List<PlayerUpdateAddLightweightProp> Props = new();
            public bool Covered;
            public DateTime CoveredChecked;
            /// <summary>A cavern graveyard laid out from the template (stamped, or to be stamped): its placement record.</summary>
            public bool Templated;
            public int Placement = -1;
            /// <summary>The template's corner (global voxel) and its quarter turns.</summary>
            public (long X, long Y, long Z) Corner;
            public int Turns;
            /// <summary>Written into the terrain (now or in an earlier run).</summary>
            public volatile bool Stamped;
            /// <summary>A cavern graveyard a random trip leaves out, and why (Graveyards.TripProblem); null: trips may use it.</summary>
            public string TripProblem;
            public ulong Guid => GuidBase | ((ulong)Index << 8);
        }

        private static readonly ConcurrentDictionary<uint, Lazy<List<Yard>>> surface = new(), caverns = new();
        private static readonly List<Yard> all = new();
        private static readonly object sync = new();

        /// <summary>What a player has been sent: the landscape, the centrepieces and the graveyards shown in full.</summary>
        private sealed class Sent
        {
            public uint Zone;
            public HashSet<int> Centres = new(), Full = new();
        }
        private static readonly Dictionary<WorldSession, Sent> sent = new();
        private static readonly HashSet<uint> warming = new();
        private static DateTime nextStream;
        private static VoxelTemplate surfaceTemplate;
        private static bool templateTried;

        /// <summary>Start-up: the game folder (cave placements) and the SurfaceGraveyards / CavernGraveyardStamps settings.</summary>
        public static void Load(string gameDirectory, bool surfaceGraveyards, bool cavernStamps = true)
        {
            Graveyards.gameDirectory = gameDirectory;
            SurfaceEnabled = surfaceGraveyards;
            StampsEnabled = cavernStamps;
            log.Info($"Graveyards: cavern graveyards from the cave placements, their template {(cavernStamps ? "stamped on a landscape's first visit" : "not stamped")} "
                + $"(CavernGraveyardStamps); surface graveyards {(surfaceGraveyards ? "on" : "off")} (SurfaceGraveyards).");
        }

        /// <summary>A landscape's cavern graveyard marker (TerrainEdits): one line per stamped graveyard, "#complete" at the end.</summary>
        private static string MarkerOf(uint zone) => $"graveyards_{zone}.txt";

        /// <summary>
        /// Start-up, after the terrain is loaded: the cavern graveyards stamped in earlier runs (every graveyards_&lt;zone&gt;.txt),
        /// their cells marked permanent again before the first heal pass, which would otherwise regrow them — from the
        /// template's size alone (the turned box and its border), nothing decoded. Runs whatever CavernGraveyardStamps says:
        /// what is in the terrain stays.
        /// </summary>
        public static void LoadStamps(string editsFolder)
        {
            Graveyards.editsFolder = editsFolder;
            if (!Directory.Exists(editsFolder))
                return;
            (uint X, uint Y, uint Z) size;
            try
            {
                size = VoxelTemplate.SizeOf(Path.Combine(VoxelTemplate.Folder, CavernTemplate));
            }
            catch (Exception e)
            {
                if (Directory.GetFiles(editsFolder, "graveyards_*.txt").Length > 0)
                    log.Warn($"Graveyards: {CavernTemplate} unreadable ({e.Message}); stamped cavern graveyards not restored.");
                return;
            }
            int landscapes = 0, yards = 0;
            foreach (string path in Directory.GetFiles(editsFolder, "graveyards_*.txt"))
            {
                if (!uint.TryParse(Path.GetFileNameWithoutExtension(path)["graveyards_".Length..], out uint zone))
                    continue;
                var list = new Dictionary<int, ((long, long, long), int)>();
                var cells = new HashSet<ulong>();
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] f = line.Split('\t');
                    if (line.StartsWith('#') || f.Length < 6 || !int.TryParse(f[0], out int placement) || !long.TryParse(f[2], out long x)
                        || !long.TryParse(f[3], out long y) || !long.TryParse(f[4], out long z) || !int.TryParse(f[5], out int turns))
                        continue;
                    list[placement] = ((x, y, z), turns & 3);
                    cells.UnionWith(VoxelTemplate.CellsCovering(x, y, z, VoxelTemplate.TurnedSize(size, turns)));
                }
                stamped[zone] = list;
                if (cells.Count > 0)
                    EnvironmentServer.Instance.MarkPermanent(cells, zone);
                landscapes++;
                yards += list.Count;
            }
            if (landscapes > 0)
                log.Info($"Graveyards: {yards} cavern graveyard stamp(s) on {landscapes} landscape(s) from earlier runs, cells kept permanent.");
        }

        // ---- Layers and lookups ---------------------------------------------------------------------------------------

        /// <summary>Is a point on the cavern layer (well below the ground of its landscape)? See <see cref="UndergroundDepth"/>.</summary>
        public static bool IsUnderground(uint zone, Vector3 at)
            => TerrainHeights.HeightAt(at.X, at.Z, zone) is float ground && ground - at.Y > UndergroundDepth;

        /// <summary>The usable graveyards of one layer of a landscape (computed on first use).</summary>
        private static IEnumerable<Yard> YardsOf(uint zone, bool cavern)
        {
            List<Yard> list = cavern ? CavernYards(zone) : SurfaceEnabled ? SurfaceYards(zone) : null;
            return list == null ? Enumerable.Empty<Yard>() : list.Where(y => !CoveredBySite(y));
        }

        private static List<Yard> SurfaceYards(uint zone)
            => surface.GetOrAdd(zone, z => new Lazy<List<Yard>>(() => Guarded(z, BuildSurface), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        private static List<Yard> CavernYards(uint zone)
            => caverns.GetOrAdd(zone, z => new Lazy<List<Yard>>(() => Guarded(z, BuildCaverns), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        /// <summary>The cavern graveyards only when already found (the floor search is not run from a creature's tick).</summary>
        private static List<Yard> CavernYardsIfReady(uint zone)
            => caverns.TryGetValue(zone, out var lazy) && lazy.IsValueCreated ? lazy.Value : null;

        private static List<Yard> Guarded(uint zone, Func<uint, List<Yard>> build)
        {
            try
            {
                return build(zone);
            }
            catch (Exception e)
            {
                log.Warn(e, $"Graveyards of landscape {zone} could not be found.");
                return new List<Yard>();
            }
        }

        /// <summary>
        /// Is a point inside a graveyard's buffer zone (the 30 m sphere)? Creatures do not take a player there as a target.
        /// Graveyards hidden by a build site don't count (story creatures on that site fight as usual).
        /// </summary>
        public static bool InSafeZone(uint zone, Vector3 at)
        {
            const float r2 = SafeRadius * SafeRadius;
            if (SurfaceEnabled && !IsUnderground(zone, at))
            {
                foreach (Yard y in SurfaceYards(zone))
                    if (Vector3.DistanceSquared(y.Centre, at) < r2 && !CoveredBySite(y))
                        return true;
                return false;
            }
            if (CavernYardsIfReady(zone) is List<Yard> list)
                foreach (Yard y in list)
                    if (Vector3.DistanceSquared(y.Centre, at) < r2)
                        return true;
            return false;
        }

        /// <summary>
        /// The graveyard area's "Prevent Spawning" (AreaProperties type 18, INT_PARAM1 1): no tree, plant or creature is
        /// spawned within its sphere. Uses every graveyard found from the terrain, hidden or not, so what grows where never
        /// depends on build sites coming and going.
        /// </summary>
        public static bool PreventsSpawning(uint zone, Vector3 at)
        {
            const float r2 = SafeRadius * SafeRadius;
            List<Yard> list = IsUnderground(zone, at) ? CavernYardsIfReady(zone) : SurfaceEnabled ? SurfaceYards(zone) : null;
            if (list != null)
                foreach (Yard y in list)
                    if (Vector3.DistanceSquared(y.Centre, at) < r2)
                        return true;
            return false;
        }

        /// <summary>
        /// Would a build site centred on a global point (decimetres) with these half extents (decimetres) be refused for a
        /// graveyard? As the client tests its exclusion spheres (FUN_1436ddd80): the distance from the centrepiece to the
        /// box's middle against the radius plus the box's half-diagonal. Only graveyards that are shown count.
        /// </summary>
        public static bool BlocksSite(uint zone, double x, double y, double z, double halfSize, double halfHeight)
        {
            if (!SurfaceEnabled)
                return false;
            var origin = TerrainStore.ZoneOrigin;
            var middle = new Vector3((float)((x - origin.X) / 10), (float)((y - origin.Y) / 10), (float)((z - origin.Z) / 10));
            // The client's whole test (Claims.InExclusionSphere): the x / z column overlap as well as the sphere; with the
            // sphere alone the server refused spots beside a graveyard that the client allows.
            return YardsOf(zone, cavern: false).Any(g => Claims.InExclusionSphere(g.Centre, SafeRadius, middle, halfSize, halfHeight));
        }

        /// <summary>
        /// Does a build site already cover this graveyard (by the same test the client makes when a site is placed)? Kept
        /// <see cref="ClaimRecheck"/>; a covered graveyard is not shown, not a respawn site and not a buffer zone.
        /// </summary>
        private static bool CoveredBySite(Yard yard)
        {
            if (yard.Cavern)
                return false;
            DateTime now = DateTime.UtcNow;
            lock (yard)
            {
                if (now - yard.CoveredChecked < ClaimRecheck)
                    return yard.Covered;
                (double gx, double gy, double gz) = TerrainEdits.ToGlobal(yard.Centre);
                const double around = 4000;   // 400 m: more than any site's half-diagonal plus the radius
                var boxes = Claims.BoxesIn(yard.Zone, (gx - around, gy - around, gz - around), (gx + around, gy + around, gz + around));
                yard.Covered = boxes.Any(b =>
                {
                    double hx = (b.MaxX - b.MinX) / 2, hy = (b.MaxY - b.MinY) / 2, hz = (b.MaxZ - b.MinZ) / 2;
                    double cx = b.MinX + hx - gx, cy = b.MinY + hy - gy, cz = b.MinZ + hz - gz;
                    double reach = SafeRadius * 10 + Math.Sqrt(hx * hx + hy * hy + hz * hz);
                    return cx * cx + cy * cy + cz * cz < reach * reach;
                });
                yard.CoveredChecked = now;
                return yard.Covered;
            }
        }

        // ---- Finding them ---------------------------------------------------------------------------------------------

        /// <summary>
        /// A landscape's cavern graveyards: each graveyard placement of its Underground cave file (template 76 boxes of
        /// graveyard_launch's size; without any, each Graveyard01 piece). A placement stamped already — or to be stamped
        /// (CavernGraveyardStamps) — is laid out from the turned template (<see cref="PlaceTemplated"/>); otherwise its
        /// centrepiece stands on the floor under the graveyard's middle (the client's cave pieces, Caves.Floor), as before.
        /// Positions only: the stamping runs in the background (<see cref="StampMissing"/>).
        /// </summary>
        private static List<Yard> BuildCaverns(uint zone)
        {
            var yards = new List<Yard>();
            uint continent = Landscapes.Get(zone)?.Continent ?? zone;
            string path = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
            if (!File.Exists(path))
                return yards;
            var origin = TerrainStore.ZoneOrigin;
            var done = stamped.GetValueOrDefault(zone) ?? new Dictionary<int, ((long X, long Y, long Z) Corner, int Turns)>();
            VoxelTemplate template = StampsEnabled || done.Count > 0 ? CavernTemplateOrNull() : null;
            (uint X, uint Y, uint Z) size = template?.Size ?? SizeOrZero();
            List<(int Index, RuinSlots.Box Box, int Turns)> placements = GraveyardPlacements(path, size);
            var pieces = new List<Vector3>();
            if (placements.Count > 0)
                foreach ((_, RuinSlots.Box box, _) in placements)
                    pieces.Add(PieceOf(box));
            else
            {
                // No placements read (or no template to size them): each Graveyard01 piece, as before.
                byte[] d = File.ReadAllBytes(path);
                int n = BitConverter.ToInt32(d, 4);
                for (int i = 0, o = 8; i < n && o + 39 <= d.Length; i++, o += 39)
                    if (BitConverter.ToUInt32(d, o) == GraveyardTile)
                        pieces.Add(new Vector3((float)((BitConverter.ToDouble(d, o + 4) - origin.X) / 10),
                            (float)((BitConverter.ToDouble(d, o + 12) - origin.Y) / 10), (float)((BitConverter.ToDouble(d, o + 20) - origin.Z) / 10)));
            }
            int templated = 0;
            for (int k = 0; k < pieces.Count; k++)
            {
                if (k < placements.Count && template != null && (StampsEnabled || done.ContainsKey(placements[k].Index)))
                {
                    (int index, RuinSlots.Box box, int turns) = placements[k];
                    // The template's corner: one voxel below and outside the box minimum (its vertex-only border; the
                    // outposts' rule). A placement stamped before keeps the corner and turn it was stamped with.
                    (long X, long Y, long Z) corner = CornerOf(box);
                    bool already = done.TryGetValue(index, out var was);
                    if (already)
                        (corner, turns) = was;
                    if (PlaceTemplated(zone, template, index, pieces[k], corner, turns, already) is Yard t)
                    {
                        t.Piece = pieces[k];
                        yards.Add(t);
                        templated++;
                        continue;
                    }
                }
                Yard onFloor = PlaceOnFloor(zone, pieces[k]);
                onFloor.Piece = pieces[k];
                onFloor.TripProblem = EvaluateFloor(k < placements.Count ? placements[k].Index : -1, pieces[k], onFloor.Centre, onFloor.Arrival, zone).Problem;
                yards.Add(onFloor);
            }
            int stampedCount = yards.Count(y => y.Stamped);
            var leftOut = yards.Where(y => y.TripProblem != null).GroupBy(y => ProblemKind(y.TripProblem))
                .Select(g => $"{g.Count()} {g.Key}").ToList();
            log.Info($"Graveyards: {yards.Count} cavern graveyards on landscape {zone} ({templated} from {CavernTemplate}, {stampedCount} stamped before); "
                + $"{yards.Count(y => y.TripProblem == null)} open to Underground Adventure trips"
                + (leftOut.Count > 0 ? $" (left out: {string.Join(", ", leftOut)})." : "."));
            return yards;
        }

        private static (uint X, uint Y, uint Z) SizeOrZero()
        {
            try
            {
                return VoxelTemplate.SizeOf(Path.Combine(VoxelTemplate.Folder, CavernTemplate));
            }
            catch (Exception)
            {
                return (0, 0, 0);
            }
        }

        /// <summary>
        /// A cavern graveyard as it stood before the template was stamped: the centrepiece on the cave floor under the
        /// graveyard's middle (Caves.Floor from inside the box; else under the piece), the arrival 6 m along +z.
        /// </summary>
        private static Yard PlaceOnFloor(uint zone, Vector3 piece)
        {
            Vector3 middle = piece + CavernMiddleOffset;
            // Caves.Floor gives the point 1 m above the rock; the centrepiece stands on the rock.
            Vector3 floor = FloorBelow(middle with { Y = piece.Y - CavernSearchBelowPiece }, zone)
                ?? FloorBelow(piece + new Vector3(0f, 10f, 0f), zone) ?? piece;
            Vector3 arrival = FloorBelow(middle with { Y = piece.Y - CavernSearchBelowPiece } + ArrivalOffset, zone) ?? floor;
            var yard = new Yard { Zone = zone, Cavern = true, Centre = floor - new Vector3(0f, 1f, 0f), Arrival = arrival };
            Register(yard);
            yard.Props.Add(Spawn(yard, 0, CentrepieceProp, 0, yard.Centre, Quaternion.Identity, Vector3.One, GraveyardArea));
            return yard;
        }

        /// <summary>
        /// A cavern graveyard laid out from graveyard_launch.otd with its corner at a global voxel, turned
        /// <paramref name="turns"/> quarter turns in its box: every template prop where the turned template has it (corner
        /// + turned position, its own rotation then the turn; the outposts' rule, without any ground snap: the stamped plinth
        /// is what they stand on), the Graveyard Centerpiece 1518 first and carrying the graveyard area (so it is the
        /// template's own centrepiece, never a second one). The arrival is the area's offset (0, ·, 6) turned with the
        /// template [I], <see cref="ArrivalAboveSurface"/> above the first standable voxel of the stamped result in that
        /// column (<see cref="EvaluateTemplated"/>, which also decides whether random trips use it). Null without a
        /// centrepiece.
        /// </summary>
        private static Yard PlaceTemplated(uint zone, VoxelTemplate template, int placement, Vector3 piece, (long X, long Y, long Z) corner,
            int turns, bool stampedBefore)
        {
            VoxelTemplate.Prop centre = template.Props.FirstOrDefault(p => p.PropId == CentrepieceProp);
            if (centre == null)
                return null;
            float metre = (float)(TerrainEdits.VoxelSize / 10);
            Vector3 cornerLocal = CornerLocal(corner);
            Quaternion turn = VoxelTemplate.Turn(turns);
            Vector3 At(Vector3 local)
            {
                (double x, double z) = template.TurnPoint(local.X / metre, local.Z / metre, turns);
                return cornerLocal + new Vector3((float)(x * metre), local.Y, (float)(z * metre));
            }
            ArrivalCheck check = EvaluateTemplated(template, centre, placement, piece, corner, turns, zone);
            var yard = new Yard
            {
                Zone = zone, Cavern = true, Templated = true, Placement = placement, Corner = corner, Turns = turns, Stamped = stampedBefore,
                Centre = check.Centre, TripProblem = check.Problem
            };
            Register(yard);
            yard.Arrival = check.Arrival ?? yard.Centre + new Vector3(0f, 1.5f, 0f);
            yard.Props.Add(Spawn(yard, 0, CentrepieceProp, centre.ModelId, yard.Centre,
                Quaternion.Normalize(Quaternion.Concatenate(centre.Rotation, turn)), centre.Scale, GraveyardArea));
            int n = 1;
            foreach (VoxelTemplate.Prop p in template.Props.Where(p => !ReferenceEquals(p, centre)))
                yard.Props.Add(Spawn(yard, n++, p.PropId, p.ModelId, At(p.Position), Quaternion.Normalize(Quaternion.Concatenate(p.Rotation, turn)),
                    p.Scale, 0));
            return yard;
        }

        /// <summary>
        /// The lowest standable point (local metres) in the column of <paramref name="aim"/> once the turned template is
        /// stamped: an air voxel with 2 m of air above and solid under it — the template's own samples inside its box (all
        /// explicit there), the generated cave outside (as Outposts.StandingSpot).
        /// </summary>
        private static Vector3? StandingAt(VoxelTemplate template, (long X, long Y, long Z) corner, Vector3 cornerLocal, int turns, Vector3 aim, uint zone)
        {
            double voxel = TerrainEdits.VoxelSize;
            float metre = (float)(voxel / 10);
            long dx = (long)Math.Round((aim.X - cornerLocal.X) / metre), dz = (long)Math.Round((aim.Z - cornerLocal.Z) / metre);
            int State(long dy)
            {
                int s = template.SampleAt(dx, dy, dz, turns);
                return s >= 0 ? s : CavePieces.IsAir((corner.X + dx + 0.5) * voxel, (corner.Y + dy + 0.5) * voxel, (corner.Z + dz + 0.5) * voxel, zone) ? 0 : 1;
            }
            return StandableIndex(y => State(y) == 1, 1, template.Size.Y + 10) is long y
                ? new Vector3(cornerLocal.X + dx * metre, cornerLocal.Y + y * metre, cornerLocal.Z + dz * metre) : null;
        }

        private static VoxelTemplate CavernTemplateOrNull()
        {
            lock (sync)
            {
                if (!cavernTemplateTried)
                {
                    cavernTemplateTried = true;
                    try
                    {
                        cavernTemplate = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, CavernTemplate));
                    }
                    catch (Exception e)
                    {
                        log.Warn($"Graveyards: no cavern template {CavernTemplate} ({e.Message}); cavern graveyards stay centrepieces on the cave floor.");
                    }
                }
                return cavernTemplate;
            }
        }

        /// <summary>
        /// Background, on a landscape's first visit (<see cref="Warm"/>): every templated cavern graveyard not stamped yet is
        /// written into the terrain's layer 0 as the outpost ruins are (EnvironmentServer StampTemplateAt with the
        /// placement's turn; air on the generated cave's air is left out, so the plinth and the box's explicit air are what
        /// change), its cells marked permanent (no heal), a marker line each (a restart resumes and never stamps again),
        /// "#complete" at the end. One landscape at a time; the changed cells go to that landscape's players from the world
        /// tick (<see cref="Update"/>). A remote Environment Server that is away leaves the rest for a retry a minute later.
        /// </summary>
        private static void StampMissing(uint zone, List<Yard> yards)
        {
            List<Yard> todo = yards.Where(y => y.Templated && !y.Stamped).ToList();
            if (todo.Count == 0 || !StampsEnabled || CavernTemplateOrNull() is not VoxelTemplate template || editsFolder == null)
            {
                if (yards.Any(y => y.Templated))
                    stampStatus[zone] = todo.Count == 0 ? "stamped" : "not stamped (CavernGraveyardStamps off)";
                return;
            }
            stampGate.Wait();
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Directory.CreateDirectory(editsFolder);
                string marker = Path.Combine(editsFolder, MarkerOf(zone));
                long written = 0;
                int done = 0;
                foreach (Yard y in todo)
                {
                    stampStatus[zone] = $"stamping {done}/{todo.Count}";
                    (List<ulong> cells, List<ulong> changed) = EnvironmentServer.Instance.StampTemplateAt(template, y.Corner.X, y.Corner.Y, y.Corner.Z, zone, y.Turns);
                    EnvironmentServer.Instance.MarkPermanent(cells, zone);
                    File.AppendAllLines(marker, new[] { string.Join('\t', y.Placement, CavernTemplate, y.Corner.X, y.Corner.Y, y.Corner.Z, y.Turns) });
                    y.Stamped = true;
                    lock (sync)
                        (stamped.GetOrAdd(zone, _ => new Dictionary<int, ((long, long, long), int)>()))[y.Placement] = (y.Corner, y.Turns);
                    if (changed.Count > 0)
                        stampedCells.Enqueue((zone, changed));
                    written += cells.Count;
                    // Saved and packed now and then, so a landscape's stamps never sit expanded together.
                    if (++done % 8 == 0)
                    {
                        EnvironmentServer.Instance.Flush();
                        EnvironmentServer.Instance.CompactNow(true);
                    }
                    log.Debug($"Stamped {CavernTemplate} on landscape {zone} (placement {y.Placement}, {y.Turns * 90}°): {VoxelTemplate.LastStampReport}.");
                }
                EnvironmentServer.Instance.Flush();
                EnvironmentServer.Instance.CompactNow(true);
                if (yards.All(y => !y.Templated || y.Stamped))
                    File.AppendAllLines(marker, new[] { $"#complete\t{yards.Count(y => y.Stamped)}" });
                stampStatus[zone] = "stamped";
                log.Info($"Graveyards: stamped {done} cavern graveyard(s) on landscape {zone} in {watch.Elapsed.TotalSeconds:0.0} s ({written} cell(s)). "
                    + EnvironmentServer.Instance.MemoryReport());
            }
            catch (EnvironmentUnavailableException e)
            {
                // Nothing was recorded for the stamp that failed: tried again on the next visit after a minute.
                log.Warn($"Graveyards of landscape {zone}: {e.Message} Trying again in a minute.");
                stampStatus[zone] = "waiting for the Environment Server";
                Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ =>
                {
                    lock (warming)
                        warming.Remove(zone);
                });
            }
            catch (Exception e)
            {
                log.Error(e, $"Graveyards of landscape {zone} could not be stamped.");
                stampStatus[zone] = $"failed: {e.Message}";
            }
            finally
            {
                stampGate.Release();
            }
        }

        /// <summary>
        /// Are a landscape's cavern graveyards ready for arrivals (Caves.TravelRandom): laid out (found in the background,
        /// <see cref="PrepareCaverns"/>) and, with CavernGraveyardStamps, every templated one stamped — so an arrival on the
        /// plinth never comes before the plinth, and no stamp is written around a player who arrived first. Never runs
        /// the floor search itself (safe on the world thread).
        /// </summary>
        public static bool CavernsReady(uint zone)
            => CavernYardsIfReady(zone) is List<Yard> list && list.Count > 0 && (!StampsEnabled || list.All(y => !y.Templated || y.Stamped));

        /// <summary>Starts laying out (and with CavernGraveyardStamps stamping) a landscape's cavern graveyards in the background, once.</summary>
        public static void PrepareCaverns(uint zone) => Warm(zone);

        /// <summary>
        /// Where a trip arrives at the cavern graveyard of a Graveyard01 piece (local metres; Caves.TravelRandom): the
        /// graveyard's own arrival point — on a stamped graveyard_launch the first standable voxel at the area's offset
        /// (0, ·, 6) turned with the template, i.e. on the plinth; on one never stamped (CavernGraveyardStamps off) the cave
        /// floor there (<see cref="PlaceOnFloor"/>). Null when the landscape's graveyards aren't laid out yet or the piece
        /// has no graveyard; also for a templated one not stamped yet (see <see cref="CavernsReady"/>).
        /// </summary>
        public static Vector3? CavernArrival(uint zone, Vector3 piece)
        {
            if (CavernYardsIfReady(zone) is not List<Yard> list)
                return null;
            Yard yard = list.Where(y => y.Cavern).OrderBy(y => Vector3.DistanceSquared(y.Piece, piece)).FirstOrDefault();
            // The placements' pieces (box minimum + PieceFromBox) and the tile records' agree exactly on every v14 file.
            if (yard == null || Vector3.DistanceSquared(yard.Piece, piece) > 4f || (yard.Templated && !yard.Stamped))
                return null;
            return yard.Arrival;
        }

        /// <summary>
        /// Where to stand at a Graveyard01 piece's own column (local metres; the underground portal crystal's site,
        /// Game/WarpNetwork) once its cavern graveyard is laid out from graveyard_launch.otd: the first standable voxel of the
        /// stamped result there. On the islands the piece's column runs through the template's box (the piece is at
        /// (22.4, ·, 6.8) m from the template corner; the box is 33.2 m square, explicit air around a 4.4 m plinth), so the
        /// ground there is the stamp's, which the client's cave pieces alone (Caves.Floor) don't know. Null when that piece has no templated graveyard (CavernGraveyardStamps off
        /// and not stamped before, no placements, no template) or the graveyards can't be built yet (before Load /
        /// LoadStamps and the templates folder: nothing is cached then).
        /// </summary>
        public static Vector3? StandAtPiece(uint zone, Vector3 piece)
        {
            if (gameDirectory == null || editsFolder == null || string.IsNullOrEmpty(VoxelTemplate.Folder))
                return null;
            List<Yard> yards = CavernYards(zone);
            if (CavernTemplateOrNull() is not VoxelTemplate template)
                return null;
            double voxel = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            foreach (Yard y in yards)
            {
                if (!y.Templated)
                    continue;
                // The placement box's minimum is the corner plus its border voxel; the piece lies PieceFromBox from it.
                var at = new Vector3((float)(((y.Corner.X + 1) * voxel + PieceFromBox.X - origin.X) / 10),
                    (float)(((y.Corner.Y + 1) * voxel + PieceFromBox.Y - origin.Y) / 10), (float)(((y.Corner.Z + 1) * voxel + PieceFromBox.Z - origin.Z) / 10));
                if (Vector3.DistanceSquared(at, piece) > 1f)
                    continue;
                var cornerLocal = new Vector3((float)((y.Corner.X * voxel - origin.X) / 10), (float)((y.Corner.Y * voxel - origin.Y) / 10),
                    (float)((y.Corner.Z * voxel - origin.Z) / 10));
                return StandingAt(template, y.Corner, cornerLocal, y.Turns, piece, zone);
            }
            return null;
        }

        /// <summary>The cavern floor under a point in the air (1 m above the rock), or null when the point is in rock.</summary>
        private static Vector3? FloorBelow(Vector3 inAir, uint zone)
        {
            Vector3 floor = Caves.Floor(inAir, zone);
            return floor == inAir ? null : floor;
        }

        /// <summary>
        /// A landscape's surface graveyards [I]: in each 1 km square, the first of up to <see cref="Tries"/> seeded spots with
        /// dry ground whose 26 m footprint is level within <see cref="MaxStep"/>; none within 500 m of the landscape's middle
        /// (the Spire). Only the terrain decides, so they stand in the same places after every restart.
        /// </summary>
        private static List<Yard> BuildSurface(uint zone)
        {
            var yards = new List<Yard>();
            if (!TerrainHeights.Has(zone) || SurfaceTemplateOrNull() is not VoxelTemplate template)
                return yards;
            VoxelTemplate.Prop centre = template.Props.FirstOrDefault(p => p.PropId == CentrepieceProp);
            if (centre == null)
                return yards;
            int cells = (int)(LandscapeHalf * 2 / SurfaceSpacing);
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    var rng = new Random(Shared.StableHash.Combine((int)zone, i, j, 0x4759));
                    for (int attempt = 0; attempt < Tries; attempt++)
                    {
                        float x = -LandscapeHalf + (i + 0.15f + 0.7f * (float)rng.NextDouble()) * SurfaceSpacing;
                        float z = -LandscapeHalf + (j + 0.15f + 0.7f * (float)rng.NextDouble()) * SurfaceSpacing;
                        if (x * x + z * z < SpireClear * SpireClear || !Level(zone, x, z, out float ground))
                            continue;
                        yards.Add(Place(zone, template, centre, new Vector3(x, ground, z)));
                        break;
                    }
                }
            log.Info($"Graveyards: {yards.Count} surface graveyards on landscape {zone}.");
            return yards;
        }

        /// <summary>Dry ground at x/z whose footprint corners are within <see cref="MaxStep"/> of it.</summary>
        private static bool Level(uint zone, float x, float z, out float ground)
        {
            ground = 0f;
            if (TerrainHeights.HeightAt(x, z, zone) is not float y || y < DryLand || TerrainHeights.Slope(x, z, 4f, zone) > MaxSlope)
                return false;
            foreach ((float dx, float dz) in new[] { (-Footprint, -Footprint), (Footprint, -Footprint), (-Footprint, Footprint), (Footprint, Footprint) })
                if (TerrainHeights.HeightAt(x + dx, z + dz, zone) is not float h || h < DryLand || MathF.Abs(h - y) > MaxStep)
                    return false;
            ground = y;
            return true;
        }

        /// <summary>
        /// A surface graveyard: the template laid out so its centrepiece stands at <paramref name="foot"/>, each prop on
        /// the ground under it at its height above the template's floor (0.4 m, one voxel) [I].
        /// </summary>
        private static Yard Place(uint zone, VoxelTemplate template, VoxelTemplate.Prop centre, Vector3 foot)
        {
            var yard = new Yard { Zone = zone, Centre = foot };
            Register(yard);
            Vector3 corner = foot - centre.Position with { Y = 0f };
            float arrivalGround = TerrainHeights.HeightAt(foot.X + ArrivalOffset.X, foot.Z + ArrivalOffset.Z, zone) ?? foot.Y;
            yard.Arrival = new Vector3(foot.X + ArrivalOffset.X, arrivalGround + 0.5f, foot.Z + ArrivalOffset.Z);
            yard.Props.Add(Spawn(yard, 0, CentrepieceProp, centre.ModelId, foot + new Vector3(0f, centre.Position.Y - 0.4f, 0f),
                centre.Rotation, centre.Scale, GraveyardArea));
            int n = 1;
            foreach (VoxelTemplate.Prop p in template.Props.Where(p => !ReferenceEquals(p, centre)))
            {
                float x = corner.X + p.Position.X, z = corner.Z + p.Position.Z;
                float y = (TerrainHeights.HeightAt(x, z, zone) ?? foot.Y) + p.Position.Y - 0.4f;
                yard.Props.Add(Spawn(yard, n++, p.PropId, p.ModelId, new Vector3(x, y, z), p.Rotation, p.Scale, 0));
            }
            return yard;
        }

        private static VoxelTemplate SurfaceTemplateOrNull()
        {
            lock (sync)
            {
                if (!templateTried)
                {
                    templateTried = true;
                    try
                    {
                        surfaceTemplate = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, SurfaceTemplate));
                    }
                    catch (Exception e)
                    {
                        log.Warn($"Graveyards: no surface template {SurfaceTemplate} ({e.Message}); no surface graveyards.");
                    }
                }
                return surfaceTemplate;
            }
        }

        private static void Register(Yard yard)
        {
            lock (sync)
            {
                yard.Index = all.Count;
                all.Add(yard);
            }
        }

        private static PlayerUpdateAddLightweightProp Spawn(Yard yard, int n, uint propId, uint modelId, Vector3 at, Quaternion rotation,
            Vector3 scale, uint area) => new()
        {
            Guid = yard.Guid | (uint)n,
            TransientId = PlayerManager.AllocateTransientId(),
            ModelId = modelId != 0 ? modelId : Props.ModelOf(propId),
            Position = new Vector4(at, 1f),
            Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale = new Vector4(scale, 1f),
            NameId = GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0,
            // The graveyard's static area (claim exclusion sphere, Graveyard property) rides on the centrepiece only.
            StaticAreaId = area
        };

        // ---- Streaming ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Once a second: each player's landscape graveyards. The surface centrepieces of their landscape once on arrival,
        /// a graveyard's other props within <see cref="ViewRange"/> (dropped beyond <see cref="DropRange"/>), cavern
        /// centrepieces by 3D distance. A landscape's cavern graveyards are first looked for in the background.
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextStream)
                return;
            nextStream = now.AddSeconds(1);
            // Stamped graveyard cells go to the players of their landscape (from the world tick, as the outposts' do).
            while (stampedCells.TryDequeue(out var c))
                ZoneTerrain.Broadcast(c.Cells, c.Zone);
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                if (session.StreamsOnHold)
                    continue;
                uint zone = session.ZoneId;
                Warm(zone);
                Sent state;
                lock (sync)
                    if (!sent.TryGetValue(session, out state))
                        sent[session] = state = new Sent { Zone = zone };
                if (state.Zone != zone)
                {
                    RemoveAll(session, state);
                    state.Zone = zone;
                }
                Stream(session, state, SurfaceEnabled ? SurfaceYards(zone) : null, everywhere: true);
                Stream(session, state, CavernYardsIfReady(zone), everywhere: false);
            }
        }

        /// <summary>
        /// Starts the background search for a landscape's cavern graveyards (once), then stamps those not stamped yet
        /// (<see cref="StampMissing"/>, with CavernGraveyardStamps): never on the world thread, so logins don't wait.
        /// </summary>
        private static void Warm(uint zone)
        {
            lock (warming)
                if (!warming.Add(zone))
                    return;
            Task.Run(() =>
            {
                List<Yard> yards = CavernYards(zone);
                if (StampsEnabled && yards.Any(y => y.Templated && !y.Stamped))
                    StampMissing(zone, yards);
            });
        }

        private static void Stream(WorldSession session, Sent state, List<Yard> yards, bool everywhere)
        {
            if (yards == null)
                return;
            foreach (Yard y in yards)
            {
                bool shown = !CoveredBySite(y);
                float d = Vector3.Distance(y.Centre, session.Position);
                bool centre = shown && (everywhere || d <= ViewRange);
                if (centre && state.Centres.Add(y.Index))
                    session.EnqueueMessage(y.Props[0]);
                else if (!centre && (!shown || d > DropRange) && state.Centres.Remove(y.Index))
                    session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = y.Props[0].Guid });
                if (y.Props.Count < 2)
                    continue;
                if (shown && d <= ViewRange && state.Full.Add(y.Index))
                    foreach (PlayerUpdateAddLightweightProp p in y.Props.Skip(1))
                        session.EnqueueMessage(p);
                else if ((!shown || d > DropRange) && state.Full.Remove(y.Index))
                    foreach (PlayerUpdateAddLightweightProp p in y.Props.Skip(1))
                        session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = p.Guid });
            }
        }

        private static void RemoveAll(WorldSession session, Sent state)
        {
            List<Yard> list;
            lock (sync)
                list = all.ToList();
            foreach (int i in state.Centres)
                session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = list[i].Props[0].Guid });
            foreach (int i in state.Full)
                foreach (PlayerUpdateAddLightweightProp p in list[i].Props.Skip(1))
                    session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = p.Guid });
            state.Centres.Clear();
            state.Full.Clear();
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
                sent.Remove(session);
        }

        private static PlayerUpdateAddLightweightProp PropOf(ulong guid)
        {
            if ((guid & 0xFFFF_0000_0000_0000) != GuidBase)
                return null;
            int index = (int)((guid & 0x0000_FFFF_FFFF_FFFF) >> 8), n = (int)(guid & 0xFF);
            lock (sync)
                return index < all.Count && n < all[index].Props.Count ? all[index].Props[n] : null;
        }

        /// <summary>LightweightToFullProp for a graveyard prop the client wants solid.</summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            if (PropOf(guid) is not PlayerUpdateAddLightweightProp prop)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            return true;
        }

        // ---- Developer ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Dev "graveyard": the nearest graveyard of the player's layer (or of the named layer) and the landscape's counts;
        /// the arrival point and whether it is underground, for "graveyard go".
        /// </summary>
        public static (string Report, Vector3? Arrival, bool Underground) Describe(WorldSession session, bool? cavern = null)
        {
            uint zone = session.ZoneId;
            bool layer = cavern ?? IsUnderground(zone, session.Position);
            List<Yard> cavernList = CavernYards(zone), surfaceList = SurfaceEnabled ? SurfaceYards(zone) : new List<Yard>();
            Yard nearest = (layer ? cavernList : surfaceList).OrderBy(y => Vector3.Distance(y.Centre, session.Position)).FirstOrDefault();
            string report = $"landscape {zone}: {cavernList.Count} cavern ({cavernList.Count(y => y.Stamped)} stamped of {cavernList.Count(y => y.Templated)} "
                + $"from {CavernTemplate}, CavernGraveyardStamps {(StampsEnabled ? "on" : "off")}, {stampStatus.GetValueOrDefault(zone, "not stamping")}) / {surfaceList.Count} surface graveyards "
                + $"({surfaceList.Count(CoveredBySite)} hidden by build sites); player on the {(IsUnderground(zone, session.Position) ? "cavern" : "surface")} layer, "
                + $"in a buffer zone: {InSafeZone(zone, session.Position)}";
            if (nearest != null)
                report += $"; nearest {(layer ? "cavern" : "surface")} graveyard #{nearest.Index} at {nearest.Centre}, "
                    + $"{Vector3.Distance(nearest.Centre, session.Position):F0} m{(CoveredBySite(nearest) ? " (hidden by a build site)" : "")}";
            return (report, nearest?.Arrival, nearest?.Cavern ?? false);
        }
    }
}
