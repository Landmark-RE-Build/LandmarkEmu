using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Underground caves. The client generates the caves itself from its own data
    /// (CommonData/Voxel/caves/ContinentCavePlacements[continent]_Underground.cpa places the .cav pieces), so the
    /// server only needs to know where they are to send players there ("An Underground Adventure" in Travel).
    /// .cpa layout: u32 version (14), u32 n, then n × 39-byte tile records; more data follows (not needed here).
    /// Record fields [P] (on disk → the client's 0x30-byte in-memory record, walked by LoadContinentCaves
    /// FUN_143ffcde0; trace/0695/agent-leads3-c.md L51):
    /// <list type="bullet">
    /// <item>@0 u32 tile (VoxelCaveTileDefinitions) → +0;</item>
    /// <item>@4 / @12 / @20 f64 x, y, z (global decimetres, the piece's origin) → +8 / +0x10 / +0x18;</item>
    /// <item>@28 u32 rotation (0-3) → +0x20;</item>
    /// <item>@32 u8 cave beginning-location flag → +0x24: when set, AddCave (FUN_143f813e0) is called for the piece;</item>
    /// <item>@33 u32 property group (VoxelLayerPropertyGroups: biome, tier; "Invalid Property Group Id") → +0x28;</item>
    /// <item>@37 i8 → +0x2c, passed to the tile-instance ctor FUN_143fa1d20 (-1 on surface entrances, 0 underground);</item>
    /// <item>@38 u8 beginning-location type → +0x2d (AddCave's 2nd argument): <b>1</b> surface entrance (tiles 48-50
    /// Enterance01-03 in the _Surface files), <b>2</b> Pioneer isolated cavern (one per cavern, its first record, on
    /// 440-444 and 432; Pioneer.FindCavern), <b>3</b> cavern graveyard (the 81 Graveyard01 pieces, tile 111, of each
    /// classic island's _Underground file; WarpNetwork / Graveyards). Only type 3 goes into the client's random-pick
    /// table (GetRandomCaveBeginningLocation FUN_143f84420, the server half: "Player attempting to roll random
    /// cave").</item>
    /// </list>
    /// The type-3 locations of every loaded landscape are kept (<see cref="GraveyardPiecesOf"/>): an Underground Adventure
    /// rolls among them (<see cref="TravelRandom"/>). Counted in the 0.695 test copy: 81 on each of the 24 islands, none
    /// on 432 / 440-444 (type 2 only), and the v11-v13 files of the older continents are not read for them.
    /// Piece 104 is an open cavern (verified in game), so its centre, a little above the origin, is a safe arrival
    /// point: the player drops onto the cavern floor.
    /// <para>
    /// Every landscape with caves (audit-outposts-695.md F2, TASKS 3.14): Underground Adventure trips go to Chaos
    /// Caverns in any zone, as Lost Crystals already did (30 Jun 2016, P5927), and Lost Crystals reach other worlds and
    /// biomes (7 Jun 2016, P5734). The 0.695 install ships an Underground .cpa for
    /// the islands 391, 393-408, 410-416 (409 is NOTLIVE) and Pioneer lands (464-468) are skipped and the event islands have none, so
    /// the rule of no Chaos Caverns on Pioneer lands (P4299) holds by the data, and a trip from a Pioneer land goes to
    /// a
    /// non-Pioneer one (P5928 / P6092). The caverns of every landscape share one index space (<see cref="Caverns"/>): the
    /// home continent's first (0-519, as before, so its creature guids and outposts keep their numbers), then the others
    /// in landscape order; <see cref="ZoneOf"/> tells a cavern's landscape. Every landscape uses the same zone origin
    /// (StaticZoneData GEOMETRY_OFFSET 2558400, 2000, 2556450 m on every island), so local metres mean the same on all of
    /// them. Setting WorldServer.json CavernLandscapes ("all" by default; "home" = the home continent only, as before
    /// 2026-10-16; or a list of zone ids) picks which landscapes take part.
    /// </para>
    /// </summary>
    public static class Caves
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const uint CavernPiece = 104;
        private const int RecordSize = 39;
        /// <summary>
        /// The .cpa position is the piece's origin, the cavern's x/z centre (CavePieces); 10 m above it is open air in
        /// every cavern piece (CavernMiddle01 is air from about −76 to +75 m around it).
        /// </summary>
        private static readonly Vector3 ArrivalOffset = new(0f, 10f, 0f);

        private static readonly List<Vector3> caverns = new();
        /// <summary>
        /// Deepest cavern entry (local metres) a random Underground Adventure picks (see TravelRandom); also no more than
        /// Graveyards.TripMaxDepthBelowGround below the landscape's ground (Graveyards.Arrivals.cs).
        /// </summary>
        public const float MinTravelDepth = -650f;
        /// <summary>
        /// The caverns with their cave biome (VoxelBiomes: 41 AncientForestCavern, 45 CrystalCavern, 62 DeciduousCavern,
        /// 63 VolcanicCavern …), from the placement's property group (VoxelLayerPropertyGroups → biome, tier): the Chaos
        /// Caverns' monsters live here (Creatures). Indices are shared by every landscape (<see cref="ZoneOf"/>).
        /// </summary>
        public static IReadOnlyList<(Vector3 At, uint Biome)> Caverns => cavernBiomes;
        private static readonly List<(Vector3 At, uint Biome)> cavernBiomes = new();
        /// <summary>Each cavern's landscape (parallel to <see cref="Caverns"/>).</summary>
        private static readonly List<uint> cavernZones = new();
        /// <summary>Each landscape's caverns: a range of <see cref="Caverns"/>.</summary>
        private static readonly Dictionary<uint, (int Start, int Count)> ranges = new();
        /// <summary>The landscapes with caverns, the home continent first, then by zone id.</summary>
        private static readonly List<uint> landscapes = new();
        /// <summary>The floor under each cavern's middle (cached; filled ahead in the background, <see cref="WarmFloors"/>).</summary>
        private static readonly ConcurrentDictionary<int, Vector3> floors = new();
        private static readonly Random random = new();
        private static Dictionary<uint, uint> propertyGroups;

        /// <summary>The .cpa version whose tile records carry the beginning-location fields @32 / @38 (every 0.695 island file).</summary>
        private const uint BeginningVersion = 14;
        /// <summary>Beginning-location type (@38) of the cavern graveyards, the only type in the client's random-pick table.</summary>
        private const byte GraveyardBeginning = 3;
        /// <summary>
        /// Each landscape's type-3 cave beginning locations: the origins (local metres) of its Graveyard01 pieces, in file
        /// order (81 per classic island; none on the Pioneer lands, whose flagged pieces are type 2).
        /// </summary>
        private static readonly Dictionary<uint, List<Vector3>> graveyardPieces = new();
        /// <summary>The landscapes with cavern graveyards, in load order (the home continent first).</summary>
        private static readonly List<uint> graveyardLandscapes = new();
        /// <summary>
        /// The graveyard a player within this distance of (metres) is "at" (the client roll's current cave, left out):
        /// half the islands' graveyard spacing (a 9 × 9 grid about 700 m apart) [I: the distance is ours].
        /// </summary>
        private const float CurrentGraveyardRange = 350f;

        private static uint Home => Terrain.TerrainStore.HomeContinent;

        /// <summary>Reads the continent's underground placements and keeps the cavern pieces (local metres).</summary>
        public static void Load(string gameDirectory, uint continent)
        {
            string path = Path.Combine(gameDirectory, "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa");
            try
            {
                int added = Add(gameDirectory, path, continent);
                log.Info($"Caves: {added} caverns under continent {continent}, {GraveyardPiecesOf(continent).Count} cavern graveyards (type-3 beginning locations).");
            }
            catch (Exception e)
            {
                log.Warn($"No cave placements ({e.Message}); underground travel is off.");
            }
        }

        /// <summary>
        /// The other landscapes' caverns (after Landscapes.Load), appended after the home continent's: every non-Pioneer
        /// landscape of the main world with an Underground placement file, filtered by <paramref name="setting"/>
        /// (WorldServer.json CavernLandscapes): "all" (default), "home" / "off" (none: the home continent only, as before),
        /// or zone ids separated by commas.
        /// </summary>
        public static void LoadLandscapes(string gameDirectory, string setting)
        {
            setting = string.IsNullOrWhiteSpace(setting) ? "all" : setting.Trim();
            if (setting.Equals("home", StringComparison.OrdinalIgnoreCase) || setting.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                log.Info("Caves: other landscapes' Chaos Caverns off (CavernLandscapes = home): underground trips stay on the home continent.");
                return;
            }
            HashSet<uint> wanted = null;
            if (!setting.Equals("all", StringComparison.OrdinalIgnoreCase))
                wanted = setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => uint.TryParse(s, out uint z) ? z : 0).Where(z => z != 0).ToHashSet();
            var added = new List<string>();
            foreach (Game.Landscapes.Zone zone in Game.Landscapes.Zones.Values.Where(z => !z.Pioneer && !z.EventIsland && z.Id != Home).OrderBy(z => z.Id))
            {
                if (wanted != null && !wanted.Contains(zone.Id))
                    continue;
                string path = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "caves", $"ContinentCavePlacements[{zone.Continent}]_Underground.cpa");
                if (!File.Exists(path))
                    continue;
                try
                {
                    int n = Add(gameDirectory, path, zone.Id);
                    if (n > 0)
                        added.Add($"{zone.Id} ({n})");
                }
                catch (Exception e)
                {
                    log.Warn($"Caves of landscape {zone.Id} unreadable ({e.Message}).");
                }
            }
            log.Info($"Caves: Chaos Caverns on {added.Count} more landscape(s) (CavernLandscapes = {setting}): {string.Join(", ", added)}; "
                + $"{caverns.Count} caverns in all; cavern graveyards (Underground Adventure arrivals) on {graveyardLandscapes.Count} landscape(s), "
                + $"{graveyardPieces.Values.Sum(l => l.Count)} in all.");
        }

        /// <summary>Appends one placement file's cavern pieces for a landscape; returns how many.</summary>
        private static int Add(string gameDirectory, string path, uint zone)
        {
            if (propertyGroups == null)
            {
                propertyGroups = new Dictionary<uint, uint>();
                string groupFile = Path.Combine(gameDirectory, "Resources", "VoxelLayerPropertyGroups.txt");
                if (File.Exists(groupFile))
                    foreach (string line in File.ReadLines(groupFile))
                    {
                        string[] f = line.Split('^');
                        if (f.Length >= 3 && uint.TryParse(f[0], out uint id) && uint.TryParse(f[1], out uint biome))
                            propertyGroups[id] = biome;
                    }
            }
            byte[] d = File.ReadAllBytes(path);
            uint version = BitConverter.ToUInt32(d, 0);
            int n = BitConverter.ToInt32(d, 4);
            var origin = Terrain.TerrainStore.ZoneOrigin;
            int start = caverns.Count;
            var yards = new List<Vector3>();
            for (int i = 0, o = 8; i < n && o + RecordSize <= d.Length; i++, o += RecordSize)
            {
                // The cave beginning locations of type 3 (the cavern graveyards, see the class notes): only the v14 files
                // have the record layout read here (the v11-v13 files of the older continents use others, L47).
                if (version == BeginningVersion && d[o + 32] != 0 && d[o + 38] == GraveyardBeginning)
                    yards.Add(new Vector3((float)((BitConverter.ToDouble(d, o + 4) - origin.X) / 10),
                        (float)((BitConverter.ToDouble(d, o + 12) - origin.Y) / 10), (float)((BitConverter.ToDouble(d, o + 20) - origin.Z) / 10)));
                if (BitConverter.ToUInt32(d, o) != CavernPiece)
                    continue;
                double x = BitConverter.ToDouble(d, o + 4), y = BitConverter.ToDouble(d, o + 12), z = BitConverter.ToDouble(d, o + 20);
                var at = new Vector3((float)((x - origin.X) / 10), (float)((y - origin.Y) / 10), (float)((z - origin.Z) / 10))
                    + ArrivalOffset;
                caverns.Add(at);
                cavernBiomes.Add((at, propertyGroups.GetValueOrDefault(BitConverter.ToUInt32(d, o + 33))));
                cavernZones.Add(zone);
            }
            int count = caverns.Count - start;
            if (count > 0)
            {
                ranges[zone] = (start, count);
                landscapes.Add(zone);
            }
            if (yards.Count > 0)
            {
                graveyardPieces[zone] = yards;
                graveyardLandscapes.Add(zone);
            }
            return count;
        }

        /// <summary>Switch for Travel → An Underground Adventure.</summary>
        public static bool Enabled = true;

        /// <summary>The landscapes with Chaos Caverns (the home continent first).</summary>
        public static IReadOnlyList<uint> Landscapes => landscapes;

        /// <summary>Does this landscape have caverns (0 = the home continent)?</summary>
        public static bool HasCaverns(uint zone) => ranges.ContainsKey(zone == 0 ? Home : zone);

        /// <summary>A cavern's landscape.</summary>
        public static uint ZoneOf(int cavern) => cavern >= 0 && cavern < cavernZones.Count ? cavernZones[cavern] : Home;

        /// <summary>The caverns (indices into <see cref="Caverns"/>) of one landscape (0 = the home continent).</summary>
        public static IEnumerable<int> In(uint zone)
            => ranges.TryGetValue(zone == 0 ? Home : zone, out var r) ? Enumerable.Range(r.Start, r.Count) : Enumerable.Empty<int>();

        /// <summary>The cavern nearest a position (local metres) on a landscape, or null when none were loaded.</summary>
        public static Vector3? Nearest(Vector3 position, uint zone = 0)
        {
            List<int> list = In(zone).ToList();
            return list.Count == 0 ? null : caverns[list.OrderBy(i => Vector3.DistanceSquared(caverns[i], position)).First()];
        }

        /// <summary>A random cavern arrival point of the home continent, or null when no placements were loaded.</summary>
        public static Vector3? RandomCavern()
        {
            List<int> home = In(Home).ToList();
            return home.Count == 0 ? null : FloorOf(home[random.Next(home.Count)]);
        }

        /// <summary>
        /// The floor under a cavern's middle (cached): where its creature group stands, what streaming measures to and where
        /// trips arrive. Thread-safe (the landscape preparation fills it ahead, <see cref="WarmFloors"/>).
        /// </summary>
        public static Vector3 FloorOf(int cavern)
            => floors.GetOrAdd(cavern, i => Floor(caverns[i], ZoneOf(i)));

        /// <summary>
        /// Background (the landscape preparation in Outposts): every cavern floor of a landscape found ahead, so the first
        /// player there doesn't make the world tick scan hundreds of caverns (and build the landscape's cave index).
        /// </summary>
        public static void WarmFloors(uint zone)
        {
            foreach (int i in In(zone))
                FloorOf(i);
        }

        /// <summary>
        /// The cave floor at (x, z) near a height: from a point in the air, down to the rock below (at most
        /// <paramref name="reach"/> m); from inside rock, up to the first air. Null when there is no floor within reach
        /// (a wall, a drop) — a creature doesn't step there. <paramref name="zone"/>: the landscape (0 = the home continent).
        /// </summary>
        public static Vector3? FloorNear(Vector3 near, float reach = 6f, uint zone = 0)
        {
            double step = Terrain.TerrainEdits.VoxelSize;
            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(near + new Vector3(0f, 1f, 0f));
            if (Terrain.CavePieces.IsAir(gx, gy, gz, zone))
            {
                for (double y = gy; y > gy - reach * 10; y -= step)
                    if (!Terrain.CavePieces.IsAir(gx, y, gz, zone))
                        return near with { Y = near.Y + 1f - (float)((gy - y) / 10) + 0.4f };
                return null;
            }
            for (double y = gy; y < gy + reach * 10; y += step)
                if (Terrain.CavePieces.IsAir(gx, y, gz, zone))
                    return near with { Y = near.Y + 1f + (float)((y - gy) / 10) };
            return null;
        }

        /// <summary>
        /// The cavern floor below a point in the cavern's air (the client's own cave shapes, CavePieces): stepping
        /// down a voxel at a time until rock, then 1 m above it. Unchanged when the caves aren't known.
        /// </summary>
        public static Vector3 Floor(Vector3 inAir, uint zone = 0)
        {
            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(inAir);
            if (!Terrain.CavePieces.IsAir(gx, gy, gz, zone))
                return inAir;
            for (double y = gy; y > gy - 2000; y -= Terrain.TerrainEdits.VoxelSize)
                if (!Terrain.CavePieces.IsAir(gx, y, gz, zone))
                    return inAir with { Y = inAir.Y - (float)((gy - y) / 10) + 1f };
            return inAir;
        }

        /// <summary>
        /// Developer check (dev "cavecheck [zone]"): for every cavern entry point of a landscape, whether
        /// <see cref="Floor"/> finds a floor — starting in rock, or no ground within 200 m below, leaves the arrival in the
        /// air or in rock.
        /// </summary>
        public static string CheckFloors(uint zone = 0)
        {
            int inRock = 0, noFloor = 0, ok = 0, all = 0;
            var bad = new List<string>();
            foreach (int i in In(zone))
            {
                Vector3 c = caverns[i];
                all++;
                (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(c);
                if (!Terrain.CavePieces.IsAir(gx, gy, gz, ZoneOf(i))) { inRock++; if (bad.Count < 8) bad.Add($"rock {c}"); continue; }
                Vector3 f = Floor(c, ZoneOf(i));
                if (f == c) { noFloor++; if (bad.Count < 8) bad.Add($"air {c}"); }
                else ok++;
            }
            return $"landscape {(zone == 0 ? Home : zone)}: {all} caverns: {ok} floor found, {inRock} start in rock, {noFloor} no floor within 200 m; "
                + string.Join("; ", bad);
        }

        /// <summary>
        /// Group members asked to follow a player underground: the destination, until the offer runs out. World: the other
        /// world the player's trip went to (L131; the spot is that world's pick, so At is unused), null = this world.
        /// </summary>
        private static readonly Dictionary<(WorldSession Member, ulong Inviter), (Vector3 At, uint Zone, Landscapes.OtherWorldInfo World, DateTime Until)> groupTrips = new();

        /// <summary>How long a group member may still accept the trip (ours).</summary>
        private static readonly TimeSpan GroupTripOffer = TimeSpan.FromMinutes(2);

        /// <summary>
        /// When someone travels to a new Chaos Cavern from the Travel menu, the others are asked whether to come along
        /// and may decline (dev, 4 May 2016, P5188): every other online member of the
        /// traveller's group gets the client's join prompt (Groups.SendContentInvite). Only members on this world: a trip
        /// that went to another world offers the members left here before the hand-over (<see cref="TravelToOtherWorld"/>).
        /// </summary>
        private static void OfferGroupTrip(WorldSession session, Vector3 at, uint zone, Landscapes.OtherWorldInfo world = null,
            List<WorldSession> members = null)
        {
            members ??= Groups.MembersOf(session).Where(m => m != session && !m.Disconnected).ToList();
            if (members.Count == 0)
                return;
            DateTime until = DateTime.UtcNow + GroupTripOffer;
            lock (groupTrips)
            {
                foreach (var stale in groupTrips.Where(t => t.Value.Until < DateTime.UtcNow).Select(t => t.Key).ToList())
                    groupTrips.Remove(stale);
                foreach (WorldSession m in members)
                    groupTrips[(m, session.PlayerGuid)] = (at, zone, world, until);
            }
            foreach (WorldSession m in members)
                Groups.SendContentInvite(m, session);
            log.Info($"{session.CharacterName}'s group ({members.Count}) asked to follow underground"
                + (world != null ? $" on world {world.ServerWorld}." : "."));
        }

        /// <summary>
        /// A group member said yes: to the same cavern spot (re-zoning to that cavern's landscape when elsewhere); for a trip
        /// that went to another world (L131), the same hand-over to that world and landscape, arriving beside the member
        /// they follow (<see cref="ArriveFromOtherWorld"/>).
        /// </summary>
        public static void AcceptGroupTrip(WorldSession session, ulong inviter)
        {
            (Vector3 At, uint Zone, Landscapes.OtherWorldInfo World, DateTime Until) trip;
            lock (groupTrips)
                if (!groupTrips.Remove((session, inviter), out trip) || trip.Until < DateTime.UtcNow)
                {
                    log.Info($"{session.CharacterName}: group trip from 0x{inviter:X} expired or unknown.");
                    return;
                }
            if (trip.World != null)
            {
                if (!CrossWorld.CavernTrip(session, trip.World, trip.Zone, Internal.CavernTripKind.UndergroundAdventure, follow: inviter))
                    Chat.System(session, "That world can't be reached right now.");
                return;
            }
            Go(session, trip.At, trip.Zone);
            CavernArrivalGuard.Arm(session, trip.At, trip.Zone);
            Achievements.Progress(session, Achievements.UndergroundAdventure);
            FirstTimeEvents.Trigger(session, FirstTimeEvents.ChaosCavern);   // queued by the client while it loads
            log.Info($"{session.CharacterName} follows their group underground.");
        }

        /// <summary>
        /// Travel → An Underground Adventure, the world roll first (OPEN-LEADS L131; P5927: any zone, as Lost Crystals
        /// already did; P5734: other worlds and biomes; see <see cref="CrossWorld.CavernTripsEnabled"/>):
        /// with CavernTripsOtherWorlds on and other worlds on this gateway, this world and each of them are equally likely.
        /// Another world: the player is handed over to one of its Chaos Cavern landscapes, and that world makes the usual
        /// landscape-then-graveyard roll (<see cref="ArriveFromOtherWorld"/>); the group members still here are offered to
        /// follow first (P5188). False: the trip stays here (<see cref="TravelRandom"/>).
        /// </summary>
        public static bool TravelToOtherWorld(WorldSession session)
        {
            if (caverns.Count == 0 || !Enabled || CrossWorld.RollTripWorld(session) is not Landscapes.OtherWorldInfo world)
                return false;
            uint land = CrossWorld.TripLandscape(session, graveyardLandscapes.Count > 0 ? graveyardLandscapes : landscapes);
            // The members on this world, before the player leaves it (their group view may drop the leaver).
            List<WorldSession> members = Groups.MembersOf(session).Where(m => m != session && !m.Disconnected).ToList();
            Vector3 from = session.Position;
            if (land == 0 || !CrossWorld.CavernTrip(session, world, land, Internal.CavernTripKind.UndergroundAdventure))
                return false;
            // (The goal and first-time event are the arrival's, on that world.)
            OfferGroupTrip(session, from, land, world, members);
            return true;
        }

        /// <summary>
        /// An Underground Adventure from another world (L131; <see cref="CrossWorld.ArriveOnCavernTrip"/>): beside the group
        /// member it follows when they are here (an accepted group trip), else the usual roll — on the landscape the player
        /// was handed to when its graveyards are ready, else any landscape here (<see cref="TravelRandom"/>, which also
        /// offers this world's group members to follow).
        /// </summary>
        public static void ArriveFromOtherWorld(WorldSession session, ulong follow)
        {
            if (follow != 0 && Abilities.ArriveBesideFriend(session, follow))
            {
                Achievements.Progress(session, Achievements.UndergroundAdventure);
                FirstTimeEvents.Trigger(session, FirstTimeEvents.ChaosCavern);
                log.Info($"{session.CharacterName} follows their group underground from another world.");
                return;
            }
            uint land = session.ZoneId;
            TravelRandom(session, HasCaverns(land) && TripReady(land) ? land : null);
        }

        /// <summary>To a cavern spot: a plain move on the player's landscape (no ground snap), else a re-zone there.</summary>
        private static void Go(WorldSession session, Vector3 at, uint zone)
        {
            if (session.ZoneId == zone)
                Teleport.To(session, at, snapToGround: false);
            else
                Teleport.ToZone(session, at, zone);
        }

        /// <summary>A cavern this near the player counts as the one they are in (ours).</summary>
        private const float CurrentCavernRange = 150f;

        /// <summary>The landscapes with cavern graveyards (type-3 cave beginning locations).</summary>
        public static IReadOnlyList<uint> GraveyardLandscapes => graveyardLandscapes;

        /// <summary>A landscape's cavern graveyard pieces (local metres; empty when it has none).</summary>
        public static IReadOnlyList<Vector3> GraveyardPiecesOf(uint zone)
            => graveyardPieces.TryGetValue(zone == 0 ? Home : zone, out var list) ? list : Array.Empty<Vector3>();

        /// <summary>
        /// Can a trip land on this landscape's cavern graveyards now: its outposts placed (Outposts.Ready, as before) and its
        /// cavern graveyards laid out and, with CavernGraveyardStamps, stamped (Graveyards.CavernsReady)?
        /// </summary>
        private static bool TripReady(uint zone) => Outposts.Ready(zone) && Graveyards.CavernsReady(zone);

        /// <summary>Queues what a landscape still lacks for trips: its outposts and its cavern graveyards (background).</summary>
        private static void PrepareTrip(uint zone)
        {
            if (!Outposts.Ready(zone))
                Outposts.Prepare(zone);
            if (!Graveyards.CavernsReady(zone))
                Graveyards.PrepareCaverns(zone);
        }

        /// <summary>
        /// Travel → "An Underground Adventure" (TeleportBase 0xFC sub 0x0B PacketTeleportRandomUndergroundRequest, 0.695
        /// FUN_14316eb80 "same" as 0.589; trace/0695/coverage-695.md): a random cavern graveyard, keeping the exact height
        /// (no ground snap). User decision 2026-10-17 (OPEN-LEADS L51 / L129, "landscape, then graveyard"):
        /// <list type="number">
        /// <item>a landscape at random among <see cref="GraveyardLandscapes"/>, as since 30 Jun 2016 (Chaos Caverns in
        /// any zone, P5927). The Pioneer lands have only type-2 beginning locations, so they are never picked (and are
        /// not
        /// loaded: <see cref="LoadLandscapes"/>). [I] Ours: a landscape not ready for trips (<see cref="TripReady"/>: outposts
        /// stamped lazily, cavern graveyards laid out and stamped on first use) is queued and the trip goes to a ready
        /// landscape instead, so players land on a finished graveyard where every Chaos Cavern has its outpost (P4808);</item>
        /// <item>then one of its type-3 cave beginning locations — the cavern graveyards (.cpa tile record @32 set, @38 = 3:
        /// its 81 Graveyard01 pieces) — never the one the player is at now: the client's own roll, whose server half is
        /// compiled into the client and never called there (VoxelFarm::CaveManager GetRandomCaveBeginningLocation
        /// FUN_143f84420: a random entry of the type-3 pick list whose id differs from the current cave; "Player attempting
        /// to roll random cave with no caves loaded!") [P mechanism; I that live's server called it for this request: its
        /// log text names a player roll];</item>
        /// <item>the arrival is that graveyard's arrival point (Graveyards.CavernArrival): 0.6 m over the stamped
        /// graveyard_launch plinth (22 m below the piece), or with CavernGraveyardStamps off (and never stamped) on the cave
        /// floor at the graveyard, as before.</item>
        /// </list>
        /// Only graveyards that pass Graveyards.TripProblem are picked (Graveyards.TripArrival; bug 2026-10-18, the player
        /// fell through the world 2,028 m below a mountain): arrival at y ≥ <see cref="MinTravelDepth"/> as before, at most
        /// Graveyards.TripMaxDepthBelowGround below the landscape's ground, the client's own cave floor close under it.
        /// 1,568 of the 1,944 island graveyards pass (125 are below y −650, 251 more than 1,600 m below the ground; dev
        /// "cavecheck all"). The fall guard (CavernArrivalGuard) watches
        /// every trip's first seconds and brings a player who falls through back. When the landscape is not ready
        /// (a fresh world's first trips, nothing ready anywhere) the trip goes to a random cavern of it as before 2026-10-17
        /// (<see cref="TravelToCavern"/>), never onto an unstamped graveyard whose plinth would then be stamped around the
        /// player. The group follow (P5188) and the Lost Crystal's own pick (Outposts.NextCave, with its loot lockouts;
        /// it falls back to this trip) are unchanged. <paramref name="zone"/> (dev "cave &lt;zone&gt;"): that landscape.
        /// </summary>
        public static void TravelRandom(WorldSession session, uint? zone = null)
        {
            if (caverns.Count == 0 || !Enabled)
            {
                Chat.System(session, "Underground adventures aren't open on this server yet.");
                return;
            }
            uint land;
            if (zone is uint asked && HasCaverns(asked))
                land = asked == 0 ? Home : asked;
            else if (graveyardLandscapes.Count == 0)
            {
                // No v14 placement file anywhere (no graveyards known): the cavern trip of before.
                TravelToCavern(session, PickCavernLandscape());
                return;
            }
            else
            {
                land = graveyardLandscapes[random.Next(graveyardLandscapes.Count)];
                if (!TripReady(land))
                {
                    PrepareTrip(land);
                    List<uint> ready = graveyardLandscapes.Where(TripReady).ToList();
                    if (ready.Count > 0)
                        land = ready[random.Next(ready.Count)];
                }
            }
            if (!graveyardPieces.TryGetValue(land, out List<Vector3> pieces) || !Graveyards.CavernsReady(land))
            {
                if (pieces != null)
                    PrepareTrip(land);
                log.Info($"{session.CharacterName}: no cavern graveyard ready on landscape {land} yet; a cavern instead.");
                // As before: a landscape whose outposts are placed, when this one's aren't (a dev-chosen one stays).
                TravelToCavern(session, zone == null && !Outposts.Ready(land) ? PickCavernLandscape() : land);
                return;
            }
            var choices = Enumerable.Range(0, pieces.Count).Where(i => Graveyards.TripArrival(land, pieces[i]) != null).ToList();
            if (choices.Count == 0)
            {
                // None of this landscape's graveyards is safe to arrive at (none on the shipped islands): its caverns instead.
                log.Info($"{session.CharacterName}: no cavern graveyard on landscape {land} passes the arrival checks; a cavern instead.");
                TravelToCavern(session, land);
                return;
            }
            // Never the graveyard the player is at (FUN_143f84420 skips the current cave id): the nearest one, when the
            // player is that close to it, while others remain.
            if (session.ZoneId == land && choices.Count > 1)
            {
                int nearest = choices.OrderBy(i => Vector3.DistanceSquared(pieces[i], session.Position)).First();
                if (Vector3.Distance(pieces[nearest], session.Position) <= CurrentGraveyardRange)
                    choices.Remove(nearest);
            }
            int pick = choices[random.Next(choices.Count)];
            if (Graveyards.TripArrival(land, pieces[pick]) is not Vector3 at)
            {
                log.Info($"{session.CharacterName}: cavern graveyard {pick} on landscape {land} has no arrival point; a cavern instead.");
                TravelToCavern(session, land);
                return;
            }
            Arrive(session, at, land, $"cavern graveyard {pick} (piece {pieces[pick]})");
        }

        /// <summary>A landscape at random among those with caverns, a ready one when the pick isn't (the trip of before).</summary>
        private static uint PickCavernLandscape()
        {
            uint land = landscapes[random.Next(landscapes.Count)];
            if (!Outposts.Ready(land))
            {
                Outposts.Prepare(land);
                List<uint> ready = landscapes.Where(Outposts.Ready).ToList();
                if (ready.Count > 0)
                    land = ready[random.Next(ready.Count)];
            }
            return land;
        }

        /// <summary>
        /// The trip of before 2026-10-17 (the fallback of <see cref="TravelRandom"/>): one of a landscape's cavern pieces at
        /// random, never one within <see cref="CurrentCavernRange"/> of the player while others remain.
        /// </summary>
        private static void TravelToCavern(WorldSession session, uint land)
        {
            List<int> all = In(land).ToList();
            if (all.Count == 0)
            {
                Chat.System(session, "Underground adventures aren't open on this server yet.");
                return;
            }
            // Random trips use the caverns down to MinTravelDepth: deeper ones (the .cpa has them to about y -1000) were
            // still loading on the client 35 s after arrival and the player then fell through (y -673 -> -1305,
            // y -865 -> -1882; the audit of 2026-10-14). Entries down to y -640 landed on the floor every time.
            // The same depth below the ground as the graveyard trips (Graveyards.TripMaxDepthBelowGround, bug 2026-10-18).
            var reachable = all.Where(i => caverns[i].Y >= MinTravelDepth && !TooDeepBelowGround(caverns[i], land)).ToList();
            // Never the cavern the player is in (as the client's CaveManager picks a "random cave": the beginning
            // locations other than the current cave; architecture-leads.md, L51): any cavern within this distance of
            // the player is left out while others remain.
            if (session.ZoneId == land && reachable.Count > 1)
            {
                var others = reachable.Where(i => Vector3.Distance(FloorOf(i), session.Position) > CurrentCavernRange).ToList();
                if (others.Count > 0)
                    reachable = others;
            }
            Vector3 at = FloorOf(reachable.Count > 0 ? reachable[random.Next(reachable.Count)] : all[random.Next(all.Count)]);
            Arrive(session, at, land, "a cavern");
        }

        /// <summary>Is a cavern point more than Graveyards.TripMaxDepthBelowGround below its landscape's ground?</summary>
        private static bool TooDeepBelowGround(Vector3 at, uint zone)
            => Terrain.TerrainHeights.HeightAt(at.X, at.Z, zone) is float ground && ground - at.Y > Graveyards.TripMaxDepthBelowGround;

        /// <summary>The trip itself: the move (re-zoning to another landscape), the goal, the first-time event, the group offer.</summary>
        private static void Arrive(WorldSession session, Vector3 at, uint land, string what)
        {
            // A spot on another landscape (or the home continent's from elsewhere): the trip re-zones there.
            Go(session, at, land);
            // Its first seconds watched: a fall through the world comes back to the spot (CavernArrivalGuard).
            CavernArrivalGuard.Arm(session, at, land);
            log.Info($"{session.CharacterName} goes underground at {at} on landscape {land}: {what}.");
            Achievements.Progress(session, Achievements.UndergroundAdventure);
            FirstTimeEvents.Trigger(session, FirstTimeEvents.ChaosCavern);   // queued by the client while it loads
            OfferGroupTrip(session, at, land);
        }
    }
}
