using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Outposts of the Chaos Caverns (wiki "Outposts", "Lost Chests", "Lost Crystals", "Elite Monsters", launch 2016):
    /// player-built ruins (the "Cavern Ruins" contest winners) placed by the developers in the largest caverns. Each has
    /// monsters and one Elite, one Lost Chest (more loot than a common chest, often half buried) and one Lost Crystal,
    /// which wakes (a column of light) once the Elite is dead; using it channels a few seconds (not in combat; a hit
    /// cancels it), gives loot and offers a trip to another cavern ("Eureka!").
    /// <para>
    /// Built from the client's own data (trace/0695/audit-outposts-695.md F1): the ruins are the 0.695 install's
    /// CommonData/Voxel/templates/ruin_*.otd (version 6; stamped into the cavern like the Spire's base, cells kept from
    /// regrowth). Each template holds its outpost: decor props, the Lost Crystal (prop 2654) where it sits in the build,
    /// "Spawner: Ruins: &lt;faction&gt; Patroller / Melee / Ranged / Caster / Hound / Human (100 Limit)" props (one creature
    /// each), "... BOSS (1 Limit)" props (one Elite per BOSS kind, at one of its spots; boss display), cave monster
    /// spawners (Fungus, Chomper, Slaug, Abomination...), and 1-55 "Spawner: Ruins: Chests (1 Limit)" spots (2731): the one
    /// Lost Treasure Chest (prop 2930) sits at one of them, a new one each time it comes back (30 Jun 2016, P5925 /
    /// P5926: exactly one treasure chest per outpost, at new and well hidden spots; the 0.589 copies had 0-12 spots).
    /// The
    /// older cavern_ruins_*.otd (no crystal, spawners or chest spots) were used before 2026-10-15; their stamps are
    /// reverted once (<see cref="RevertOldRuins"/>). Effects: chest glow 11279 / open 12830, crystal light 10621,
    /// discovery fanfare 11674. The trip is TeleportBase NextUndergroundOffer (0xFC sub 0x0C {u64}, client dialog
    /// Teleport.LeylineTeleportDialogTitle "Eureka!") answered by NextUndergroundReply (sub 0x0D {u64}).
    /// Every Chaos Cavern has one outpost (26 Apr 2016, P4808; the 35 ruins once each, then copies, see FillCaverns);
    /// which ruin live put in which cavern is not known.
    /// </para>
    /// <para>
    /// Every landscape with caves (audit F2, TASKS 3.14; Caves.Landscapes): the home continent's outposts are placed at
    /// start-up as before (marker outposts695d.txt, indices 0-80). Each other landscape gets its own (81 Chaos Caverns on
    /// most islands, 80-82 on a few) the same way — 35 ruins once each in live's slots, then a copy in every cavern still
    /// empty — but lazily: a landscape is stamped in the background the first time a player is there or a trip picks it
    /// (<see cref="Prepare"/>), one landscape at a time, one decoded ruin at a time (a landscape takes minutes, ~1 GB of
    /// heap while it runs and ~110 MB of terrain edits); setting OutpostPreload also queues the landscapes nobody has
    /// visited. Its marker is TerrainEdits/outposts695d_&lt;zone&gt;.txt (one line per stamped outpost, "#complete" at the
    /// end), so a restart only marks the cells permanent again and rebuilds the props, without decoding a ruin. Outpost
    /// indices (prop guids, crystal lockouts) are per landscape: <see cref="IndexBase"/> + the marker's number, never
    /// reusing the home continent's 0-127.
    /// </para>
    /// <para>
    /// Outposts changing caverns (audit F8, setting OutpostRotationHours, off by default): Outposts.Rotation.cs gives a
    /// share of a landscape's outposts another ruin in their own cavern now and then, on the same worker, keeping each
    /// outpost's index (guids, banner, lockout).
    /// </para>
    /// </summary>
    public static partial class Outposts
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The 0.695 outpost ruins: every non-empty ruin_*.otd in the templates folder.</summary>
        private static string[] Ruins => Directory.GetFiles(VoxelTemplate.Folder, "ruin_*.otd")
            .Where(f => new FileInfo(f).Length > 0).Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        private const uint LostChestProp = 2930, LostCrystalProp = 2654, ChestSpotProp = 2731;
        /// <summary>Most creatures one outpost spawns (guids keep them under 256 per cavern).</summary>
        private const int MaxGarrison = 150;
        private const uint ChestGlow = 11279, ChestOpen = 12830, CrystalLight = 10621, DiscoveryFanfare = 11674,
            ChannelEffect = 11673;   // PFX_Teleport_LeylineCrystal_Cave on the channelling player
        private const ulong GuidBase = 0x5450_0000_0000_0000;
        /// <summary>
        /// The placement marker. outposts.txt held the old cavern_ruins; outposts695.txt the first 0.695 stamps, whose
        /// cells a start-up cleanup bug stripped (2026-10-15, ruin-stamping.md §4.1): both are reverted once and the ruins
        /// stamped again, with interior air written as air. outposts695c.txt: live's slots, but stamped while
        /// VoxelTemplate.StampAt read and wrote cell voxels x-fastest (fixed 2026-10-16, 7d0c63f / 48bf3cc); its corners are
        /// never on the 40-voxel grid, so x and y were mixed inside every cell: reverted once and stamped again here.
        /// </summary>
        private const string Marker = "outposts695d.txt";
        /// <summary>Earlier markers (corners in cells): reverted once.</summary>
        private static readonly string[] OldMarkers = { "outposts.txt", "outposts695.txt", "outposts695b.txt" };
        /// <summary>Earlier markers with voxel corners (live's slots): reverted once.</summary>
        private static readonly string[] OldVoxelMarkers = { "outposts695c.txt" };
        /// <summary>Everyone this close to an opened Lost Chest gets their own loot (wiki "Chests": 50 m).</summary>
        private const float ChestShareRange = 50f;
        private static readonly TimeSpan ChestRespawn = TimeSpan.FromMinutes(20), ChannelTime = TimeSpan.FromSeconds(10),
            InCombatFor = TimeSpan.FromSeconds(6);
        private const float ViewRange = 160f, DropRange = 220f, NameRange = 60f;
        /// <summary>
        /// Each "Eureka!" offer's u64, echoed by the client's NextUndergroundReply: a fresh token per offer, and a reply is
        /// only taken for the player's own open offer (audit-outposts-695.md F9) — a reply sent without one, or twice,
        /// moves nobody.
        /// </summary>
        private static readonly Dictionary<WorldSession, ulong> openOffers = new();

        public sealed class Outpost
        {
            public int Index;
            /// <summary>Its landscape (the home continent, or another with caves).</summary>
            public uint Zone = TerrainStore.HomeContinent;
            /// <summary>The server's cavern (index into Caves.Caverns: creature group, streaming, trips).</summary>
            public int Cavern;
            /// <summary>
            /// The placement file's cavern its ruin slot belongs to (CavernInstanceData index, the marker's first field; −1
            /// unknown): where a rotation looks for the next ruin's slot (Outposts.Rotation.cs).
            /// </summary>
            public int SlotCavern = -1;
            public string Ruin;
            public string Name;
            public Vector3 Floor;
            /// <summary>The template's corner, a global voxel (0.4 m).</summary>
            public (long X, long Y, long Z) Corner;
            public List<PlayerUpdateAddLightweightProp> Props = new();
            public PlayerUpdateAddLightweightProp Chest, Crystal;
            /// <summary>The template's spawner props (prop id, its name, local position).</summary>
            public List<(uint PropId, string Name, Vector3 At)> Spawners = new();
            /// <summary>Where the Lost Chest can be (the "Chests (1 Limit)" spots, local).</summary>
            public List<Vector3> ChestSpots = new();
            public bool ChestReady = true;
            public DateTime ChestBack;
            public bool EliteDead;
            /// <summary>When the woken crystal goes dormant again (<see cref="AwakeFor"/> after the Elite's death).</summary>
            public DateTime AwakeUntil;
        }

        private static readonly List<Outpost> outposts = new();
        private static readonly Dictionary<int, Outpost> byCavern = new();
        /// <summary>By outpost index (prop guids, lockouts, dev commands).</summary>
        private static readonly Dictionary<int, Outpost> byIndex = new();
        /// <summary>Each landscape's outposts (streaming, line of sight).</summary>
        private static readonly Dictionary<uint, List<Outpost>> byZone = new();
        private static uint Home => TerrainStore.HomeContinent;

        /// <summary>Whether a cavern (index into Caves.Caverns) has an Outpost.</summary>
        public static bool HasOutpost(int cavern) => byCavern.ContainsKey(cavern);
        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, HashSet<int>> sent = new();
        private static readonly Dictionary<WorldSession, HashSet<int>> named = new();
        private static readonly Dictionary<WorldSession, (Outpost Outpost, DateTime Until)> channels = new();
        private static readonly Random random = new();
        private static DateTime nextStream;

        public static IReadOnlyList<Outpost> All => outposts;

        /// <summary>One landscape's outposts (0 = the home continent); world thread.</summary>
        public static IReadOnlyList<Outpost> InZone(uint zone)
            => byZone.TryGetValue(zone == 0 ? Home : zone, out List<Outpost> list) ? list : Array.Empty<Outpost>();

        /// <summary>The outpost with this index (home 0-80; another landscape <see cref="IndexBase"/> + n), or null.</summary>
        public static Outpost ByIndex(int index) => byIndex.GetValueOrDefault(index);

        /// <summary>
        /// Places the ruins once in live's own ruin slots and stamps them (remembered in TerrainEdits/outposts695c.txt:
        /// cavern, ruin, corner voxel), then builds each outpost's props.
        /// <para>
        /// The slots are the "rooms" of the underground placement file's CavernInstanceData (ContinentCavePlacements
        /// [c]_Underground.cpa, parser FUN_143fa2e20; trace/0695/ruin-stamping.md §5): 4,030 boxes on 406, 100 m tall, 8 m
        /// above the cavern's bottom, sized exactly like the ruins (40 × 40 m to 220 × 220 m) and voxel-aligned. Live's
        /// Environment Server filled them ("Ruin Layer Info"); which ruin went where isn't in the client, so each ruin
        /// takes a free slot of its own size (unrotated), one per cavern first, clear of the cavern's arrival platform
        /// (CaveTileObjectGaps) and the other chosen slots. The template's corner is the slot's corner less one voxel (its
        /// vertex-only border).
        /// </para>
        /// <para>
        /// Then the other landscapes (<see cref="LoadLandscapes"/>): those stamped before come back from their markers at
        /// once; the rest are placed later in the background.
        /// </para>
        /// </summary>
        public static void Load(string editsFolder)
        {
            Outposts.editsFolder = editsFolder;
            // A rotation stopped half way by the last shutdown: its marker line says the new ruin before anything reads
            // the markers, and the worker finishes its terrain (Outposts.Rotation.cs).
            LoadRotation(editsFolder);
            LoadHome(editsFolder);
            ready[Home] = 1;
            LoadLandscapes();
            if (rotationEvery > TimeSpan.Zero || pendingRedo != null)
                StartWorker(force: true);
        }

        /// <summary>The home continent's outposts, placed and stamped at start-up (unchanged since e181565).</summary>
        private static void LoadHome(string editsFolder)
        {
            if (!Caves.HasCaverns(Home) || !Directory.Exists(VoxelTemplate.Folder))
                return;
            RevertOldRuins(editsFolder);
            string marker = Path.Combine(editsFolder, Marker);
            var saved = new List<(int Cavern, string Ruin, (long, long, long) Corner)>();
            // Re-stamping reverted ruins (outposts695c.txt, the voxel-order fix): the same ruins in the same slots, all of
            // them stamped again, rather than a new random choice of caverns.
            string source = marker;
            bool restamp = false;
            foreach (string old in OldVoxelMarkers)
                if (!File.Exists(marker) && File.Exists(Path.Combine(editsFolder, old + ".reverted")))
                {
                    source = Path.Combine(editsFolder, old + ".reverted");
                    restamp = true;
                }
            if (File.Exists(source))
                foreach (string line in File.ReadAllLines(source))
                {
                    string[] f = line.Split('\t');
                    if (f.Length == 5)
                        saved.Add((int.Parse(f[0]), f[1], (long.Parse(f[2]), long.Parse(f[3]), long.Parse(f[4]))));
                }
            int stampedBefore = restamp ? 0 : saved.Count;
            // Only the sizes to choose slots: a decoded template is ~0.4 MB per cell (the 35 together ~6.6 GB), so each is
            // loaded when it is stamped and dropped after.
            var templates = new Dictionary<string, (uint X, uint Y, uint Z)>();
            foreach (string ruin in Ruins)
                try
                {
                    templates[ruin] = VoxelTemplate.SizeOf(Path.Combine(VoxelTemplate.Folder, ruin + ".otd"));
                }
                catch (Exception e)
                {
                    log.Warn($"Outpost ruin {ruin} unreadable: {e.Message}");
                }
            if (templates.Count == 0)
                log.Warn($"No ruin_*.otd in {VoxelTemplate.Folder}: no outposts (the 0.695 install has 35).");
            string[] missing = templates.Keys.Where(r => saved.All(x => x.Ruin != r)).ToArray();
            var (slots, gaps) = SlotsOf(Home);
            if (missing.Length > 0)
                saved.AddRange(ChooseSlots(missing, templates, saved, slots, gaps, Home));
            saved.AddRange(FillCaverns(templates, saved, slots, gaps, Home));
            bool stampedAny = false;
            // Each ruin is decoded once for all its copies (81 outposts share the 35 ruins; a decoded ruin is up to ~0.6 GB,
            // ~1.4 s to read): its copies are stamped (new ones) or marked (stamped before), and their standing spots and
            // props taken, before the next ruin is read.
            var floors = new Vector3[saved.Count];
            var ruinProps = new List<VoxelTemplate.Prop>[saved.Count];
            foreach (IGrouping<string, int> copies in Enumerable.Range(0, saved.Count).Where(n => templates.ContainsKey(saved[n].Ruin))
                .GroupBy(n => saved[n].Ruin))
            {
                VoxelTemplate template;
                try
                {
                    template = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, copies.Key + ".otd"));
                }
                catch (Exception e)
                {
                    log.Warn($"Outpost ruin {copies.Key} unreadable: {e.Message}");
                    continue;
                }
                foreach (int n in copies)
                {
                    (long X, long Y, long Z) corner = saved[n].Corner;
                    double voxel = TerrainEdits.VoxelSize;
                    var origin = TerrainStore.ZoneOrigin;
                    // The slot's floor middle (local metres): the corner plus the border voxel, half the footprint across.
                    var floor = new Vector3((float)(((corner.X + 1 + template.Size.X / 2.0) * voxel - origin.X) / 10),
                        (float)(((corner.Y + 1) * voxel - origin.Y) / 10), (float)(((corner.Z + 1 + template.Size.Z / 2.0) * voxel - origin.Z) / 10));
                    // Where a player can stand: the first air with solid ground under it in the stamped result (the ruin's own
                    // voxels where it writes them, the cave elsewhere), in the middle or the nearest column that has one. The
                    // cave floor alone can be inside the ruin's plinth (2026-10-15: players fell through outpost 0).
                    floors[n] = StandingSpot(template, corner, Home) ?? floor + new Vector3(0f, 8f, 0f);
                    List<ulong> cells;
                    if (n >= stampedBefore)
                    {
                        (cells, _) = EnvironmentServer.Instance.StampTemplateAt(template, corner.X, corner.Y, corner.Z, TerrainStore.HomeContinent);
                        log.Info($"Stamped {copies.Key} (cavern {saved[n].Cavern}): {VoxelTemplate.LastStampReport}; heap "
                            + $"{GC.GetTotalMemory(false) / 1048576} MB, process {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576} MB.");
                        // Saved and packed at once, so stamping every ruin never holds them all expanded.
                        EnvironmentServer.Instance.Flush();
                        EnvironmentServer.Instance.CompactNow(true);
                        stampedAny = true;
                    }
                    else
                        cells = CellsOf(template, corner);
                    EnvironmentServer.Instance.MarkPermanent(cells, TerrainStore.HomeContinent);
                    ruinProps[n] = template.Props;      // only the props are kept: the cells live in the terrain overlays now
                }
                // Its walls for line of sight (F4), one coarse grid shared by every copy (on every landscape).
                Sight.AddRuin(copies.Key, template);
                // What the other landscapes' restarts need of it without decoding it again (F2).
                Remember(copies.Key, template);
                // Collected before the next ruin (large-object heap compacted too), so loading all 35 never holds several.
                template = null;
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
            }
            // The outposts in the marker's order (their index is in prop guids and crystal lockouts).
            var lines = new List<string>();
            for (int n = 0; n < saved.Count; n++)
            {
                (int slotCavern, string ruin, (long X, long Y, long Z) corner) = saved[n];
                if (ruinProps[n] == null)
                    continue;
                if (Add(Home, outposts.Count, ruin, corner, floors[n], ruinProps[n], slotCavern) == null)
                    continue;
                lines.Add($"{slotCavern}\t{ruin}\t{corner.X}\t{corner.Y}\t{corner.Z}");
            }
            if (stampedAny)
            {
                Directory.CreateDirectory(editsFolder);
                File.WriteAllLines(marker, lines);
            }
            // Loading the ruins' cells (permanent marks, the revert) leaves many of them expanded (~0.4 MB each): pack
            // them again (saved first: changed cells are never compacted), or the world holds hundreds of MB it doesn't
            // need until a player digs there.
            EnvironmentServer.Instance.Flush();
            EnvironmentServer.Instance.CompactNow(true);
            log.Info($"Outposts: {outposts.Count} in the Chaos Caverns ({saved.Count - stampedBefore} stamped now). {EnvironmentServer.Instance.MemoryReport()}");
        }

        /// <summary>
        /// The lowest standable point (local metres) of a stamped ruin: a voxel that is air (template air / interior air, or
        /// not written and cave air) with 2 m of air above and solid under it (template solid, or not written and cave
        /// rock). Searched in the middle column first, then rings of columns 2 m apart out to 20 m.
        /// </summary>
        private static Vector3? StandingSpot(VoxelTemplate template, (long X, long Y, long Z) corner, uint zone)
        {
            const int size = Terrain.BlockData.Size;
            var cells = template.Cells.ToDictionary(c => (c.X, c.Y, c.Z));
            double voxel = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            // 1 = solid, 0 = air at a template voxel.
            int State(long x, long y, long z)
            {
                if (x >= 0 && y >= 0 && z >= 0 && cells.TryGetValue(((int)(x / size), (int)(y / size), (int)(z / size)), out VoxelTemplate.Cell c))
                {
                    int i = Terrain.BlockData.Index((int)(x % size), (int)(y % size), (int)(z % size));
                    if ((c.Changed[i >> 3] & (0x80 >> (i & 7))) != 0 && (c.Blocks.Flags[i] & 1) != 0)
                        return c.Blocks.Materials[i] is 0 or VoxelTemplate.InteriorAir ? 0 : 1;
                }
                return CavePieces.IsAir((corner.X + x + 0.5) * voxel, (corner.Y + y + 0.5) * voxel, (corner.Z + z + 0.5) * voxel, zone) ? 0 : 1;
            }
            long mx = template.Size.X / 2, mz = template.Size.Z / 2;
            for (int ring = 0; ring <= 10; ring++)
                for (int dx = -ring; dx <= ring; dx++)
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring)
                            continue;
                        long x = mx + dx * 5, z = mz + dz * 5;      // 5 voxels = 2 m
                        if (x < 0 || z < 0 || x >= template.Size.X || z >= template.Size.Z)
                            continue;
                        for (long y = 1; y + 5 < template.Size.Y; y++)
                            if (State(x, y - 1, z) == 1 && State(x, y, z) == 0 && State(x, y + 2, z) == 0 && State(x, y + 4, z) == 0)
                                return new Vector3((float)(((corner.X + x + 0.5) * voxel - origin.X) / 10), (float)(((corner.Y + y) * voxel - origin.Y) / 10),
                                    (float)(((corner.Z + z + 0.5) * voxel - origin.Z) / 10));
                    }
            return null;
        }

        /// <summary>The level-2 cells a template covers with its corner at a voxel.</summary>
        private static List<ulong> CellsOf(VoxelTemplate template, (long X, long Y, long Z) corner)
            => CellsOf(template.Cells.Select(c => (c.X, c.Y, c.Z)), corner);

        /// <summary>The level-2 cells a template covers, from its cell positions (<see cref="VoxelTemplate.CellPositions"/>).</summary>
        private static List<ulong> CellsOf(IEnumerable<(int X, int Y, int Z)> templateCells, (long X, long Y, long Z) corner)
        {
            const int size = Terrain.BlockData.Size;
            var cells = new HashSet<ulong>();
            foreach (var c in templateCells)
                for (int dx = 0; dx < 2; dx++)
                    for (int dy = 0; dy < 2; dy++)
                        for (int dz = 0; dz < 2; dz++)
                        {
                            long x = (corner.X + (long)c.X * size + dx * (size - 1)) / size, y = (corner.Y + (long)c.Y * size + dy * (size - 1)) / size,
                                z = (corner.Z + (long)c.Z * size + dz * (size - 1)) / size;
                            cells.Add(TerrainStore.CellId(TerrainEdits.Level, (uint)x, (uint)y, (uint)z));
                        }
            return cells.ToList();
        }

        /// <summary>
        /// Free slots for the ruins: exact x / z size match (template voxels × 0.4 m), largest ruins first (the big slots
        /// are few), one ruin per cavern while possible, never overlapping a chosen slot or a cavern's arrival platform.
        /// Deterministic per world (seeded by the slot list).
        /// </summary>
        private static List<(int Cavern, string Ruin, (long, long, long) Corner)> ChooseSlots(string[] ruins,
            Dictionary<string, (uint X, uint Y, uint Z)> templates, List<(int Cavern, string Ruin, (long X, long Y, long Z) Corner)> placed,
            List<(int Cavern, RuinSlots.Box Box)> slots, List<RuinSlots.Box> gaps, uint zone)
        {
            var result = new List<(int, string, (long, long, long))>();
            if (slots.Count == 0)
            {
                log.Warn($"No ruin slots in the cave placements of landscape {zone}: outposts not placed.");
                return result;
            }
            double voxel = TerrainEdits.VoxelSize;
            var taken = new List<RuinSlots.Box>(gaps);
            var cavernsUsed = new HashSet<int>(placed.Select(p => p.Cavern));
            foreach (var p in placed)
                if (templates.TryGetValue(p.Ruin, out var size))
                    taken.Add(new RuinSlots.Box((p.Corner.X + 1) * voxel, (p.Corner.Y + 1) * voxel, (p.Corner.Z + 1) * voxel,
                        (p.Corner.X + 1 + size.X) * voxel, (p.Corner.Y + 1 + size.Y) * voxel, (p.Corner.Z + 1 + size.Z) * voxel));
            var rng = new Random(slots.Count * 7919 + 35);
            foreach (string ruin in ruins.OrderByDescending(r => (long)templates[r].X * templates[r].Z).ThenBy(r => r, StringComparer.Ordinal))
            {
                var t = templates[ruin];
                double w = t.X * voxel, d = t.Z * voxel;
                var fits = slots.Where(s => Math.Abs(s.Box.MaxX - s.Box.MinX - w) < 1 && Math.Abs(s.Box.MaxZ - s.Box.MinZ - d) < 1
                    && taken.All(b => !b.Overlaps(s.Box))).ToList();
                var fresh = fits.Where(s => !cavernsUsed.Contains(s.Cavern)).ToList();
                var pool = fresh.Count > 0 ? fresh : fits;
                if (pool.Count == 0)
                {
                    log.Warn($"Outpost ruin {ruin} ({w / 10:0} × {d / 10:0} m): no free slot of its size on landscape {zone}.");
                    continue;
                }
                var slot = pool[rng.Next(pool.Count)];
                taken.Add(slot.Box);
                cavernsUsed.Add(slot.Cavern);
                result.Add((slot.Cavern, ruin, ((long)Math.Round(slot.Box.MinX / voxel) - 1, (long)Math.Round(slot.Box.MinY / voxel) - 1,
                    (long)Math.Round(slot.Box.MinZ / voxel) - 1)));
            }
            return result;
        }

        /// <summary>
        /// An outpost in every Chaos Cavern: each cavern always has an outpost (hotfix 26 Apr 2016, P4808 [P]; TASKS
        /// 6.8). The caverns are the placement file's CavernInstanceData (81 on
        /// 406, every one with 6-94 ruin slots; the 81 underground portal crystals are the same caverns). Each cavern no
        /// placed ruin sits in gets one copy of a ruin that fits one of its free slots: the ruin with the fewest copies so
        /// far, at random among those, and never a ruin the cavern already has (no cavern holds two copies of one
        /// outpost any more, P4971 [P]). Which ruin live put in which cavern isn't in the client
        /// [I]. Deterministic per world, like <see cref="ChooseSlots"/>.
        /// </summary>
        private static List<(int Cavern, string Ruin, (long, long, long) Corner)> FillCaverns(
            Dictionary<string, (uint X, uint Y, uint Z)> templates, List<(int Cavern, string Ruin, (long X, long Y, long Z) Corner)> placed,
            List<(int Cavern, RuinSlots.Box Box)> slots, List<RuinSlots.Box> gaps, uint zone)
        {
            var result = new List<(int, string, (long, long, long))>();
            if (templates.Count == 0)
                return result;
            double voxel = TerrainEdits.VoxelSize;
            var taken = new List<RuinSlots.Box>(gaps);
            foreach (var p in placed)
                if (templates.TryGetValue(p.Ruin, out var size))
                    taken.Add(new RuinSlots.Box((p.Corner.X + 1) * voxel, (p.Corner.Y + 1) * voxel, (p.Corner.Z + 1) * voxel,
                        (p.Corner.X + 1 + size.X) * voxel, (p.Corner.Y + 1 + size.Y) * voxel, (p.Corner.Z + 1 + size.Z) * voxel));
            string[] ruins = templates.Keys.OrderBy(r => r, StringComparer.Ordinal).ToArray();
            var copies = ruins.ToDictionary(r => r, r => placed.Count(p => p.Ruin == r));
            var rng = new Random(slots.Count * 7919 + 81);
            foreach (int cavern in slots.Select(s => s.Cavern).Distinct().OrderBy(c => c))
            {
                if (placed.Any(p => p.Cavern == cavern))
                    continue;
                var options = new List<(string Ruin, RuinSlots.Box Box)>();
                foreach (var s in slots.Where(s => s.Cavern == cavern && taken.All(b => !b.Overlaps(s.Box))))
                    foreach (string ruin in ruins)
                        if (Math.Abs(s.Box.MaxX - s.Box.MinX - templates[ruin].X * voxel) < 1
                            && Math.Abs(s.Box.MaxZ - s.Box.MinZ - templates[ruin].Z * voxel) < 1)
                            options.Add((ruin, s.Box));
                if (options.Count == 0)
                {
                    log.Warn($"Chaos Cavern {cavern} of landscape {zone}: no free ruin slot fits any ruin, so it has no outpost.");
                    continue;
                }
                int fewest = options.Min(o => copies[o.Ruin]);
                var pool = options.Where(o => copies[o.Ruin] == fewest).ToList();
                (string chosen, RuinSlots.Box box) = pool[rng.Next(pool.Count)];
                taken.Add(box);
                copies[chosen]++;
                result.Add((cavern, chosen, ((long)Math.Round(box.MinX / voxel) - 1, (long)Math.Round(box.MinY / voxel) - 1,
                    (long)Math.Round(box.MinZ / voxel) - 1)));
            }
            if (result.Count > 0)
                log.Info($"Outposts: {result.Count} more Chaos Cavern(s) of landscape {zone} get a ruin copy (one outpost in every cavern).");
            return result;
        }

        /// <summary>A landscape's ruin slots and arrival platforms (its Underground placement file).</summary>
        private static (List<(int Cavern, RuinSlots.Box Box)> Slots, List<RuinSlots.Box> Gaps) SlotsOf(uint zone)
        {
            uint continent = Landscapes.Get(zone)?.Continent ?? zone;
            return RuinSlots.Load(Path.Combine(VoxelTemplate.Folder, "..", "caves", $"ContinentCavePlacements[{continent}]_Underground.cpa"));
        }

        /// <summary>
        /// The ruins placed before 2026-10-15 (cavern_ruins_*, marker outposts.txt): their stamped cells go back to the
        /// natural ground once, then the marker is renamed. The new ruins are stamped afterwards.
        /// </summary>
        private static void RevertOldRuins(string editsFolder)
        {
            foreach (string name in OldMarkers)
                RevertMarker(Path.Combine(editsFolder, name));
            foreach (string name in OldVoxelMarkers)
                RevertMarker(Path.Combine(editsFolder, name), voxelCorners: true);
        }

        /// <summary>
        /// Puts one old marker's ruin boxes back to the natural ground and renames it. <paramref name="voxelCorners"/>: the
        /// marker's corners are template-voxel coordinates (outposts695c.txt), else cell coordinates.
        /// </summary>
        private static void RevertMarker(string marker, bool voxelCorners = false)
        {
            if (!File.Exists(marker))
                return;
            int reverted = 0, cells = 0;
            double size = TerrainEdits.CellSize, voxel = TerrainEdits.VoxelSize;
            foreach (string line in File.ReadAllLines(marker))
            {
                string[] f = line.Split('\t');
                if (f.Length != 5 || !uint.TryParse(f[2], out uint x) || !uint.TryParse(f[3], out uint y) || !uint.TryParse(f[4], out uint z))
                    continue;
                string path = Path.Combine(VoxelTemplate.Folder, f[1] + ".otd");
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    continue;
                VoxelTemplate old = VoxelTemplate.Load(path);
                double unit = voxelCorners ? voxel : size;
                List<ulong> changed = EnvironmentServer.Instance.RevertBox(x * unit, y * unit, z * unit,
                    x * unit + old.Size.X * voxel, y * unit + old.Size.Y * voxel, z * unit + old.Size.Z * voxel, TerrainStore.HomeContinent);
                cells += changed.Count;
                reverted++;
                // Each reverted cell is expanded (~0.4 MB): packed after every ruin, or all of them (17,000 cells for 35
                // ruins) sit expanded at once, several GB.
                EnvironmentServer.Instance.Flush();
                EnvironmentServer.Instance.CompactNow(true);
            }
            EnvironmentServer.Instance.Flush();
            EnvironmentServer.Instance.CompactNow(true);
            File.Move(marker, marker + ".reverted", true);
            log.Info($"Outposts: reverted {reverted} old ruin stamp(s) of {Path.GetFileName(marker)}, {cells} cell(s); stamped again below.");
        }

        /// <summary>"ruin_hadder_bunker" → "Hadder Bunker".</summary>
        private static string Title(string ruin)
        {
            string s = ruin.Replace("ruin_", "").Replace('_', ' ').Trim();
            return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length > 1 && char.IsLetter(w[0]) ? char.ToUpperInvariant(w[0]) + w[1..] : w));
        }

        /// <summary>
        /// The ruin's props where it puts them: decor is shown; spawner props are never shown (they become creatures and
        /// chest spots); the Lost Crystal stands where the template has it (one per outpost; the first when a ruin has two).
        /// </summary>
        private static void BuildProps(Outpost o, IEnumerable<VoxelTemplate.Prop> props)
        {
            Vector3 corner = CornerLocal(o);
            ulong next = 0;
            Vector3? crystalAt = null;
            Quaternion crystalTurn = Quaternion.Identity;
            foreach (VoxelTemplate.Prop p in props)
            {
                Vector3 at = corner + p.Position;
                if (p.PropId == ChestSpotProp)
                    o.ChestSpots.Add(at);
                else if (p.PropId == LostCrystalProp)
                {
                    if (crystalAt == null)
                        (crystalAt, crystalTurn) = (at, p.Rotation);
                }
                else if (Creatures.PropName(p.PropId) is string name && name.Contains("Spawner", StringComparison.OrdinalIgnoreCase))
                    o.Spawners.Add((p.PropId, name, at));
                else if (p.ModelId != 0)
                    o.Props.Add(Prop(o, next++, p.ModelId, at, p.Rotation, p.Scale, 0));
            }
            o.Chest = ChestProp(o);
            Vector3 crystal = crystalAt ?? Caves.FloorNear(o.Floor + new Vector3(-6f, 2f, -4f), 10f, o.Zone) ?? o.Floor;
            o.Crystal = Prop(o, 1001, Props.ModelOf(LostCrystalProp), crystal, crystalTurn, Vector3.One, LostCrystalProp);
        }

        /// <summary>The Lost Chest at a random one of the ruin's chest spots (the cavern floor if it has none).</summary>
        private static PlayerUpdateAddLightweightProp ChestProp(Outpost o)
        {
            Vector3 at = o.ChestSpots.Count > 0 ? o.ChestSpots[random.Next(o.ChestSpots.Count)]
                : (Caves.FloorNear(o.Floor + new Vector3(9f, 2f, 7f), 10f, o.Zone) ?? o.Floor) - new Vector3(0f, 0.35f, 0f);
            return Prop(o, 1000, Props.ModelOf(LostChestProp), at, Quaternion.Identity, Vector3.One, LostChestProp);
        }

        private static Vector3 CornerLocal(Outpost o)
        {
            var origin = TerrainStore.ZoneOrigin;
            double voxel = TerrainEdits.VoxelSize;
            return new Vector3((float)((o.Corner.X * voxel - origin.X) / 10), (float)((o.Corner.Y * voxel - origin.Y) / 10),
                (float)((o.Corner.Z * voxel - origin.Z) / 10));
        }

        private static PlayerUpdateAddLightweightProp Prop(Outpost o, ulong n, uint model, Vector3 at, Quaternion rotation, Vector3 scale, uint propId) => new()
        {
            Guid = GuidBase | ((ulong)o.Index << 16) | n,
            TransientId = PlayerManager.AllocateTransientId(),
            ModelId = model,
            Position = new Vector4(at, 1f),
            Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale = new Vector4(scale, 1f),
            NameId = propId == 0 ? 0 : GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0
        };

        // ---- Garrisons (Creatures puts them into the cavern's group) --------------------------------------------------

        /// <summary>
        /// The outpost's garrison from its spawner props: one creature per "(100 Limit)" spawner at its spot; one Elite per
        /// BOSS kind ("(1 Limit)": one alive at a time) at a random one of that kind's spots. Capped at
        /// <see cref="MaxGarrison"/>.
        /// </summary>
        public static IEnumerable<(uint Spawner, string Name, bool Boss, Vector3 At)> GarrisonFor(int cavern)
        {
            if (!byCavern.TryGetValue(cavern, out Outpost o))
                yield break;
            var rng = new Random(cavern * 7919 + 101);
            int n = 0;
            foreach (var kind in o.Spawners.Where(s => s.Name.Contains("BOSS", StringComparison.Ordinal)).GroupBy(s => s.PropId))
            {
                var spots = kind.ToList();
                var pick = spots[rng.Next(spots.Count)];
                if (n++ < MaxGarrison)
                    yield return (pick.PropId, pick.Name, true, pick.At);
            }
            foreach (var s in o.Spawners.Where(s => !s.Name.Contains("BOSS", StringComparison.Ordinal)))
                if (n++ < MaxGarrison)
                    yield return (s.PropId, s.Name, false, s.At);
        }

        /// <summary>The Elite fell: the crystal wakes (its light), for everyone who has the outpost on screen.</summary>
        public static void EliteDied(int cavern, WorldSession killer)
        {
            if (!byCavern.TryGetValue(cavern, out Outpost o))
                return;
            o.EliteDead = true;
            o.AwakeUntil = DateTime.UtcNow + AwakeFor;
            HitEffects.PlayOn(o.Crystal.Guid, CrystalLight, Viewers(o));
            if (killer != null)
                Chat.System(killer, "The Lost Crystal stirs: a spire of light shoots from it.");
        }

        /// <summary>
        /// How long a crystal stays awake after its Elite died: killing the Elite no longer unlocks the crystal for
        /// good (11 Aug 2016, P6387; audit-outposts-695.md F5). The Elite comes back after 15 minutes (launch notes,
        /// Creatures.RespawnDelay), so the crystal sleeps again then — on the clock, even when nobody is in the cavern to
        /// see the Elite return (dead creatures only respawn while someone streams their cavern).
        /// </summary>
        private static readonly TimeSpan AwakeFor = TimeSpan.FromMinutes(15);

        /// <summary>The Elite is back: the crystal sleeps again.</summary>
        public static void EliteBack(int cavern)
        {
            if (byCavern.TryGetValue(cavern, out Outpost o))
                o.EliteDead = false;
        }

        // ---- Streaming ---------------------------------------------------------------------------------------------

        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            CompleteChannels(now);
            TakeArrivals();
            // Outposts changing caverns (F8; off unless OutpostRotationHours is set): the worker's retire requests, and
            // once a minute whether a landscape's next rotation is due.
            RotationTick(now);
            if (now < nextStream)
                return;
            nextStream = now.AddSeconds(1);
            // A player on a landscape whose outposts aren't placed yet: its first visit queues it (background, F2).
            foreach (WorldSession session in PlayerManager.Snapshot())
                if (!session.StreamsOnHold && Caves.HasCaverns(session.ZoneId) && !Ready(session.ZoneId))
                    Prepare(session.ZoneId);
            if (outposts.Count == 0)
                return;
            // Woken crystals whose time is up go dormant again (F5); a player channelling one finishes (CompleteChannels).
            foreach (Outpost o in outposts.Where(o => o.EliteDead && now >= o.AwakeUntil))
                o.EliteDead = false;
            foreach (WorldSession session in PlayerManager.Snapshot())
                Stream(session, now);
            foreach (Outpost o in outposts.Where(o => !o.ChestReady && now >= o.ChestBack))
            {
                o.Chest = ChestProp(o);         // back at a new spot
                o.ChestReady = true;
                foreach (WorldSession s in Viewers(o))
                    SendChest(s, o);
            }
        }

        private static void Stream(WorldSession session, DateTime now)
        {
            if (session.StreamsOnHold)
                return;
            HashSet<int> have, seen;
            lock (sync)
            {
                if (!sent.TryGetValue(session, out have))
                    sent[session] = have = new HashSet<int>();
                if (!named.TryGetValue(session, out seen))
                    named[session] = seen = new HashSet<int>();
            }
            uint zone = session.ZoneId;
            // Off the landscape or out of range: dropped (outposts are only sent to players on their own landscape).
            foreach (int index in have.ToList())
                if (!byIndex.TryGetValue(index, out Outpost gone))
                    have.Remove(index);
                else if (gone.Zone != zone || Vector3.Distance(gone.Floor, session.Position) > DropRange)
                {
                    have.Remove(index);
                    var remove = gone.Props.Select(p => p.Guid).Append(gone.Crystal.Guid).Append(gone.Chest.Guid);
                    foreach (ulong guid in remove)
                        session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
                }
            foreach (Outpost o in InZone(zone))
            {
                float d = Vector3.Distance(o.Floor, session.Position);
                if (d <= ViewRange && have.Add(o.Index))
                {
                    foreach (PlayerUpdateAddLightweightProp p in o.Props)
                        session.EnqueueMessage(p);
                    session.EnqueueMessage(o.Crystal);
                    if (o.ChestReady)
                        SendChest(session, o);
                    var flagging = new DynamicInteractionFlagging();
                    flagging.Guids.Add(o.Crystal.Guid);
                    flagging.Guids.Add(o.Chest.Guid);
                    session.EnqueueMessage(flagging);
                    if (o.EliteDead)
                        HitEffects.PlayOn(o.Crystal.Guid, CrystalLight, new[] { session });
                }
                // The outpost's name is the client's walk-on banner of its site (OutpostSites, L102); the first-time help as
                // players come in.
                if (d <= NameRange && seen.Add(o.Index))
                    FirstTimeEvents.Trigger(session, FirstTimeEvents.Outpost);
            }
            // The banner sites of the outposts streamed to this player (added / removed in one packet).
            OutpostSites.Sync(session, have);
        }

        private static void SendChest(WorldSession session, Outpost o)
        {
            session.EnqueueMessage(o.Chest);
            HitEffects.PlayOn(o.Chest.Guid, ChestGlow, new[] { session });
        }

        private static List<WorldSession> Viewers(Outpost o)
        {
            lock (sync)
                return sent.Where(s => s.Value.Contains(o.Index)).Select(s => s.Key).ToList();
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                sent.Remove(session);
                named.Remove(session);
                channels.Remove(session);
                openOffers.Remove(session);
            }
            OutpostSites.Forget(session);
        }

        // ---- Interaction ---------------------------------------------------------------------------------------------

        private static Outpost ByGuid(ulong guid) =>
            (guid & 0xFFFF_0000_0000_0000) == GuidBase ? byIndex.GetValueOrDefault((int)((guid >> 16) & 0xFFFF)) : null;

        public static bool IsChest(ulong guid) => ByGuid(guid) is Outpost o && o.Chest.Guid == guid;
        public static bool IsCrystal(ulong guid) => ByGuid(guid) is Outpost o && o.Crystal.Guid == guid;
        public static bool Usable(ulong guid) => IsChest(guid) || IsCrystal(guid);

        public static bool SendFull(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Outpost o)
                return false;
            PlayerUpdateAddLightweightProp p = o.Chest.Guid == guid ? o.Chest : o.Crystal.Guid == guid ? o.Crystal
                : o.Props.FirstOrDefault(x => x.Guid == guid);
            if (p == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = p.TransientId });
            return true;
        }

        /// <summary>
        /// A Lost Chest opened: everyone within 50 m gets their own loot (wiki "Chests": everyone within 50 m gets
        /// loot, different items for each) — plenty of resources, a chance at a recipe and at High
        /// Yield Dynamite; the chest goes and comes back later.
        /// </summary>
        public static void OpenChest(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Outpost o || !o.ChestReady || Health.IsDead(session))
                return;
            o.ChestReady = false;
            o.ChestBack = DateTime.UtcNow + ChestRespawn;
            List<WorldSession> viewers = Viewers(o);
            HitEffects.PlayOn(o.Chest.Guid, ChestOpen, viewers);
            Vector3 at = new(o.Chest.Position.X, o.Chest.Position.Y, o.Chest.Position.Z);
            ContentTables.ChestTable table = ContentTables.LostChestTable();
            uint[] resources = table.ResourcePool is { Length: > 0 } ? table.ResourcePool
                : new uint[] { 34, 35, 36, 37, 39, 51, 56, 57, 58, 2000, 2001, 2002, 46 };
            int rolls = table.ResourceRolls > 0 ? table.ResourceRolls : 4;
            uint rMin = table.ResourceMin != 0 ? table.ResourceMin : 10u;
            uint rMax = table.ResourceMax != 0 ? table.ResourceMax : 30u;
            if (rMin > rMax)
                (rMin, rMax) = (rMax, rMin);
            foreach (WorldSession p in PlayerManager.Snapshot().Where(p => p.ZoneId == o.Zone && Vector3.Distance(p.Position, at) <= ChestShareRange))
            {
                var loot = new List<(uint, uint)>();
                for (int i = 0; i < rolls; i++)
                    loot.Add((resources[random.Next(resources.Length)], (uint)random.Next((int)rMin, (int)rMax + 1)));
                if (random.NextDouble() < table.RecipeChance
                    && RecipeBook.RandomUnknownRecipeItem(p, random) is uint recipe and not 0)
                    loot.Add((recipe, 1));
                if (random.NextDouble() < table.DynamiteChance)
                    loot.Add((table.DynamiteItem != 0 ? table.DynamiteItem : 511u, 1));
                ContentTables.AddChanceExtras(loot, table.Extras, random);
                // Essences on each player's session curve (P4776, TASKS 6.17).
                Pacing.PaceEssences(loot, p, random);
                LootPinatas.SpawnBag(p, at + new Vector3(0f, 0.6f, 0f), loot);
            }
            foreach (WorldSession s in viewers)
                s.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = o.Chest.Guid });
            log.Info($"{session.CharacterName} opened the Lost Chest of {o.Name}.");
        }

        /// <summary>
        /// Using a Lost Crystal: only once the outpost's Elite is dead, never in combat; a 10 s channel that a hit cancels
        /// (13 Apr 2016, P3529: a 10 second channel at the crystal; the wiki says several seconds).
        /// </summary>
        public static void UseCrystal(WorldSession session, ulong guid)
        {
            if (ByGuid(guid) is not Outpost o || Health.IsDead(session))
                return;
            if (!o.EliteDead)
            {
                Chat.System(session, "The Lost Crystal is dormant. Defeat the outpost's Elite to wake it.");
                return;
            }
            if (Health.HurtWithin(session, InCombatFor))
            {
                Chat.System(session, "You can't use the Lost Crystal while in combat.");
                return;
            }
            lock (sync)
                channels[session] = (o, DateTime.UtcNow + ChannelTime);
            FirstTimeEvents.Trigger(session, FirstTimeEvents.LostCrystal);
            HitEffects.PlayOn(session.PlayerGuid, ChannelEffect, Viewers(o));
            Chat.System(session, "You channel the Lost Crystal...");
        }

        /// <summary>A hit cancels a crystal channel (wiki: an attack breaks the channel).</summary>
        public static void Interrupt(WorldSession session)
        {
            bool had;
            lock (sync)
                had = channels.Remove(session);
            if (had)
                Chat.System(session, "Your channel was interrupted.");
        }

        private static void CompleteChannels(DateTime now)
        {
            List<(WorldSession Session, Outpost Outpost)> done;
            lock (sync)
            {
                done = channels.Where(c => now >= c.Value.Until).Select(c => (c.Key, c.Value.Outpost)).ToList();
                foreach ((WorldSession s, _) in done)
                    channels.Remove(s);
            }
            foreach ((WorldSession session, Outpost o) in done)
            {
                // Loot lockout (dev, 4 May 2016, P5180 / P5181: per Lost Crystal, using it during the lockout only
                // offers the teleport, with no second reward; 15 Jun, P5829: the lockout made much longer).
                // The refusal (L10): StringHashToValue CaveRewards.TooSoonToUse = 574500 "You've recently bonded with a
                // Lost Crystal in this Chaos Cavern. Use the Travel menu to start a new underground adventure!" (text in
                // both 0.589 and 0.695). The client never reads that key (its oaat hash 0x479250ed and the id are in no
                // client code): it is a server value, shown with the client's own system line (PacketShowSystemMessage,
                // SystemMessages) [P text and key; I when: here, with the trip offer the 4 May 2016 post describes].
                if (LockedOut(session, o))
                {
                    SystemMessages.Show(session, TooSoonToUseText);
                    SendNextCaveOffer(session);
                    log.Info($"{session.CharacterName} used the Lost Crystal of {o.Name} while locked out: the trip only.");
                    continue;
                }
                LockOut(session, o);
                // Loot (wiki "Lost Crystals": a good chance of 1 or 2 recipes, plus Cozy Campfires, essences and
                // Lumens), the discovery fanfare, then the offer of another cavern. Rates: ContentTables lost-crystal.json.
                ContentTables.CrystalTable xt = ContentTables.CrystalLootTable();
                var loot = new List<(uint, uint)>();
                int recipes = random.NextDouble() < xt.RecipeChance
                    ? 1 + (random.NextDouble() < xt.SecondRecipeChance ? 1 : 0) : 0;
                for (int i = 0; i < recipes; i++)
                    if (RecipeBook.RandomUnknownRecipeItem(session, random) is uint recipe and not 0)
                        loot.Add((recipe, 1));
                if (random.NextDouble() < xt.CampfireChance)
                    loot.Add((xt.CampfireItem, 1));
                uint[] essences = xt.AffinityEssences is { Length: > 0 } ? xt.AffinityEssences
                    : new uint[] { 1587, 1588, 1589, 1590, 1591, 1592, 1593 };
                uint aMin = Math.Min(xt.AffinityMin, xt.AffinityMax), aMax = Math.Max(xt.AffinityMin, xt.AffinityMax);
                loot.Add((essences[random.Next(essences.Length)], (uint)random.Next((int)aMin, (int)aMax + 1)));
                uint advMin = Math.Min(xt.AdventureMin, xt.AdventureMax), advMax = Math.Max(xt.AdventureMin, xt.AdventureMax);
                loot.Add((Gathering.AdventureEssence, (uint)random.Next((int)advMin, (int)advMax + 1)));
                uint lMin = Math.Min(xt.LumenMin, xt.LumenMax), lMax = Math.Max(xt.LumenMin, xt.LumenMax);
                loot.Add((1579, (uint)random.Next((int)lMin, (int)lMax + 1)));
                if (RareDrops.Hat(random) is uint hat and not 0)
                    loot.Add((hat, 1));                       // P6505: hat unlockers from outposts
                if (RareDrops.OutpostSeasonal(random) is uint outfit and not 0)
                    loot.Add((outfit, 1));                    // P6506 / P6566 / P6567: seasonal outfits (LANDMARK_SEASON, off by default)
                Vector3 at = new(o.Crystal.Position.X, o.Crystal.Position.Y, o.Crystal.Position.Z);
                // Essences on the session curve (P4776, TASKS 6.17).
                Pacing.PaceEssences(loot, session, random);
                LootPinatas.SpawnBag(session, at + new Vector3(0f, 1f, 0f), loot);
                HitEffects.PlayOn(session.PlayerGuid, DiscoveryFanfare, Viewers(o));
                SendNextCaveOffer(session);
                DailyGoals.CrystalReached(session);
                log.Info($"{session.CharacterName} used the Lost Crystal of {o.Name}.");
            }
        }

        /// <summary>
        /// How long one player can't loot the same Lost Crystal again. Ours: live's length is unknown (L10). The
        /// lockout was made much longer on 16 Jun 2016 (P5829); the only number, a player's guess of about once a day,
        /// is from 24 Jun 2016, after that increase (forum 57065 #213; trace/0695/agent-leads2-c.md) [I].
        /// </summary>
        private static readonly TimeSpan LockoutTime = TimeSpan.FromHours(24);

        /// <summary>CaveRewards.TooSoonToUse (StringHashToValue.txt:68): client text 574500, the locked-out crystal's line.</summary>
        public const uint TooSoonToUseText = 574500;

        private static readonly DateTime LockoutEpoch = new(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Lockouts are <see cref="Unlocks.CrystalLockout"/> rows: outpost index (7 bits: the low 6 in bits 26-31, the 7th
        /// in bit 25, so rows written with the old 6-bit index read the same) and the hour the lockout ends (hours since
        /// 2016, 25 bits), kept across logouts and restarts. 81 outposts since every Chaos Cavern has one (TASKS 6.8).
        /// </summary>
        private static uint LockoutValue(int outpost, DateTime until)
            => ((uint)(outpost & 0x3F) << 26) | ((uint)((outpost >> 6) & 1) << 25)
                | ((uint)Math.Ceiling((until - LockoutEpoch).TotalHours) & LockoutHours);

        private const uint LockoutHours = 0x1FFFFFF;

        private static int LockoutOutpost(uint value) => (int)(value >> 26) | (int)((value >> 25) & 1) << 6;

        /// <summary>
        /// The other landscapes' outposts (index ≥ <see cref="PerLandscape"/>, F2) lock out in their own rows,
        /// <see cref="Unlocks.CrystalLockoutWide"/>: the index in the top 12 bits and the end hour (hours since 2016) in the
        /// low 20 (to 2135), so the home continent's rows (kind 21) stay exactly as they were.
        /// </summary>
        private const int WideIndexShift = 20;
        private const uint WideHours = (1u << WideIndexShift) - 1;

        private static uint WideLockoutValue(int outpost, DateTime until)
            => ((uint)outpost << WideIndexShift) | ((uint)Math.Ceiling((until - LockoutEpoch).TotalHours) & WideHours);

        private static bool LockedOut(WorldSession session, Outpost o)
        {
            uint now = (uint)(DateTime.UtcNow - LockoutEpoch).TotalHours;
            if (o.Index >= PerLandscape)
                return Unlocks.Values(session.PlayerGuid, Unlocks.CrystalLockoutWide)
                    .Any(v => (int)(v >> WideIndexShift) == o.Index && (v & WideHours) > now);
            return Unlocks.Values(session.PlayerGuid, Unlocks.CrystalLockout)
                .Any(v => LockoutOutpost(v) == o.Index && (v & LockoutHours) > now);
        }

        private static void LockOut(WorldSession session, Outpost o)
        {
            uint now = (uint)(DateTime.UtcNow - LockoutEpoch).TotalHours;
            if (o.Index >= PerLandscape)
            {
                var others = Unlocks.Values(session.PlayerGuid, Unlocks.CrystalLockoutWide)
                    .Where(v => (int)(v >> WideIndexShift) != o.Index && (v & WideHours) > now);
                Unlocks.Replace(session, Unlocks.CrystalLockoutWide, others.Append(WideLockoutValue(o.Index, DateTime.UtcNow + LockoutTime)));
                return;
            }
            // Keep the other crystals' lockouts that still run; replace this one's.
            var keep = Unlocks.Values(session.PlayerGuid, Unlocks.CrystalLockout)
                .Where(v => LockoutOutpost(v) != o.Index && (v & LockoutHours) > now);
            Unlocks.Replace(session, Unlocks.CrystalLockout, keep.Append(LockoutValue(o.Index, DateTime.UtcNow + LockoutTime)));
        }

        /// <summary>NextUndergroundOffer (0xFC sub 0x0C {u64}): the client's "Eureka!" dialog with the trip button.</summary>
        private static void SendNextCaveOffer(WorldSession session)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)0x0C);
            ulong token = (ulong)random.NextInt64(1, long.MaxValue);
            lock (sync)
                openOffers[session] = token;
            w.Write(token);
            session.EnqueueMessage(ZoneMessageOpcode.TeleportBase, output.ToArray());
        }

        /// <summary>The reply's token matches the player's open offer (used up here).</summary>
        public static bool TakeOffer(WorldSession session, ulong token)
        {
            lock (sync)
                return openOffers.TryGetValue(session, out ulong open) && open == token && openOffers.Remove(session);
        }

        /// <summary>
        /// A group's next cavern from one crystal (P5203), kept a few minutes for the members who follow: an outpost here, or
        /// (a trip to another world, L131) that world and landscape, where <see cref="arrivalPicks"/> keeps the group together.
        /// </summary>
        private static readonly Dictionary<(uint Group, int From), GroupPick> groupNext = new();

        private sealed record GroupPick(Outpost To, Landscapes.OtherWorldInfo World, uint Zone, DateTime Until);

        /// <summary>How long a group's pick holds for the members who use the same crystal after the first (ours).</summary>
        private static readonly TimeSpan GroupPickHold = TimeSpan.FromMinutes(5);

        /// <summary>
        /// NextUndergroundReply (0xFC sub 0x0D {u64}; 0.695 FUN_14316e830 "same", coverage-695.md): off to another outpost's
        /// cavern - "Lost Crystals can now teleport you to other worlds and biomes each time they're used" (7 Jun 2016,
        /// P5734). The client's offer packet carries no destination (audit fact 4): the pick is the server's.
        /// <list type="number">
        /// <item>Other worlds (OPEN-LEADS L131; "world" = server in the game's words, see <see cref="CrossWorld.CavernTripsEnabled"/>):
        /// with CavernTripsOtherWorlds on and other worlds on this gateway, this world and each of them are equally likely;
        /// another world gets the player handed over to one of its Chaos Cavern landscapes, and it picks the outpost there
        /// (<see cref="ArriveFromOtherWorld"/>) [I].</item>
        /// <item>Here: any landscape whose outposts are placed, preferring another landscape with another cave biome (below).</item>
        /// </list>
        /// [I] Each trip also queues one random landscape whose outposts aren't placed yet, so the network grows with use (as
        /// Caves.TravelRandom does).
        /// </summary>
        public static void NextCave(WorldSession session)
        {
            if (Caves.Landscapes.Count > 0 && Caves.Landscapes[random.Next(Caves.Landscapes.Count)] is uint someLand && !Ready(someLand))
                Prepare(someLand);
            Outpost here = InZone(session.ZoneId).OrderBy(o => Vector3.Distance(o.Floor, session.Position)).FirstOrDefault();
            // Group members using the same crystal go to the same next Chaos Cavern (P5203): the first member's
            // pick holds for the group at that crystal for a few minutes (ours).
            (uint Group, int From)? key = Groups.IdOf(session) is uint gid && here != null ? (gid, here.Index) : null;
            GroupPick held = null;
            lock (groupNext)
                if (key is (uint, int) k0 && groupNext.TryGetValue(k0, out GroupPick picked) && picked.Until > DateTime.UtcNow)
                    held = picked;
            // Another world (L131): the group's, else the roll.
            Landscapes.OtherWorldInfo world = held != null ? held.World : CrossWorld.RollTripWorld(session);
            if (world != null)
            {
                uint land = held?.Zone ?? CrossWorld.TripLandscape(session, Caves.Landscapes);
                if (land != 0 && CrossWorld.CavernTrip(session, world, land, Internal.CavernTripKind.LostCrystal))
                {
                    if (key is (uint, int) k1)
                        lock (groupNext)
                            groupNext[k1] = new GroupPick(null, world, land, DateTime.UtcNow + GroupPickHold);
                    return;
                }
                held = null;   // that world can't be reached now: a cavern here
            }
            Outpost to = held?.To;
            if (to == null)
            {
                // Caverns whose crystal the player can still loot first (P5847: trips into recently looted caverns gave
                // nothing).
                List<Outpost> others = outposts.Where(o => o != here && !LockedOut(session, o)).ToList();
                if (others.Count == 0)
                    others = outposts.Where(o => o != here).ToList();
                // Other worlds and biomes (P5734, 7 Jun 2016) after players reported landing in the same cavern every
                // time (May 2016 threads; trace/0695/agent-leads2-c.md L129): on this world, prefer another landscape with
                // another cave biome, then another landscape, then another biome here [I: the order is ours].
                uint hereBiome = here != null ? BiomeOf(here.Cavern) : 0;
                List<Outpost> elsewhere = others.Where(o => o.Zone != session.ZoneId && BiomeOf(o.Cavern) != hereBiome).ToList();
                if (elsewhere.Count == 0)
                    elsewhere = others.Where(o => o.Zone != session.ZoneId).ToList();
                if (elsewhere.Count == 0)
                    elsewhere = others.Where(o => BiomeOf(o.Cavern) != hereBiome).ToList();
                if (elsewhere.Count > 0)
                    others = elsewhere;
                if (others.Count == 0)
                {
                    Caves.TravelRandom(session);
                    return;
                }
                to = others[random.Next(others.Count)];
            }
            if (key is (uint, int) k2)
                lock (groupNext)
                    groupNext[k2] = new GroupPick(to, null, 0, DateTime.UtcNow + GroupPickHold);
            // The cavern's own floor (a cave-piece floor the client always accepts; the ruin's standing spot isn't
            // proven yet: L94); on another landscape through its re-zone.
            Vector3 at = Caves.FloorOf(to.Cavern) + new Vector3(0f, 1f, 0f);
            if (to.Zone == session.ZoneId)
                Teleport.To(session, at, snapToGround: false, loadingScreen: true);
            else
                Teleport.ToZone(session, at, to.Zone);
            log.Info($"{session.CharacterName} follows the leyline to {to.Name} (landscape {to.Zone}).");
        }

        /// <summary>
        /// Where the members of a group who arrive here from another world's crystal go (P5203 across worlds, L131): the
        /// first one's outpost, per group and landscape, for <see cref="GroupPickHold"/> (ours).
        /// </summary>
        private static readonly Dictionary<(uint Group, uint Zone), (Outpost To, DateTime Until)> arrivalPicks = new();

        /// <summary>
        /// A Lost Crystal trip from another world (L131; <see cref="CrossWorld.ArriveOnCavernTrip"/>): to an outpost's cavern
        /// on the landscape the player was handed to - one whose crystal they can still loot (the lockouts are the
        /// character's, P5847) - else on any landscape here whose outposts are placed (that landscape is queued meanwhile),
        /// else a random cavern (Caves.TravelRandom). Group members who come from the same crystal share the pick.
        /// </summary>
        public static void ArriveFromOtherWorld(WorldSession session)
        {
            uint zone = session.ZoneId;
            if (!Ready(zone))
                Prepare(zone);
            uint? group = Groups.IdOf(session);
            Outpost to = null;
            lock (arrivalPicks)
                if (group is uint g && arrivalPicks.TryGetValue((g, zone), out var picked) && picked.Until > DateTime.UtcNow)
                    to = picked.To;
            if (to == null)
            {
                List<Outpost> pool = outposts.Where(o => o.Zone == zone && !LockedOut(session, o)).ToList();
                if (pool.Count == 0)
                    pool = outposts.Where(o => !LockedOut(session, o)).ToList();
                if (pool.Count == 0)
                    pool = outposts.ToList();
                if (pool.Count == 0)
                {
                    Caves.TravelRandom(session);
                    return;
                }
                to = pool[random.Next(pool.Count)];
            }
            if (group is uint g2)
                lock (arrivalPicks)
                    arrivalPicks[(g2, zone)] = (to, DateTime.UtcNow + GroupPickHold);
            // A real re-zone either way: the client is still on the world it came from (WorldSession.FullRezoneNext).
            Teleport.ToZone(session, Caves.FloorOf(to.Cavern) + new Vector3(0f, 1f, 0f), to.Zone, snapToGround: false);
            log.Info($"{session.CharacterName} arrives from another world's Lost Crystal at {to.Name} (landscape {to.Zone}).");
        }

        /// <summary>A cavern's cave biome (VoxelBiomes id; 0 when unknown).</summary>
        private static uint BiomeOf(int cavern) => cavern >= 0 && cavern < Caves.Caverns.Count ? Caves.Caverns[cavern].Biome : 0;

        // ---- Every landscape (audit F2, TASKS 3.14) ----------------------------------------------------------------------

        /// <summary>Outposts one landscape can hold in its index space (81 Chaos Caverns on most islands, 82 at most).</summary>
        private const int PerLandscape = 128;

        /// <summary>
        /// The first outpost index of a landscape: 0 for the home continent (its 0-80, as before), 128 × (zone − 390) for the
        /// islands 391-416 (128-3455, never 0-127): it fits the prop guid's 16 bits and the wide lockout's 12. −1: a
        /// landscape outside 391-421 gets no outposts (none has caves).
        /// </summary>
        public static int IndexBase(uint zone)
        {
            if (zone == 0 || zone == Home)
                return 0;
            uint slot = zone - 390;    // wraps for zones below 390
            return slot >= 1 && slot < 32 ? (int)slot * PerLandscape : -1;
        }

        /// <summary>A landscape's placement marker (TerrainEdits): one line per stamped outpost, "#complete" when all are.</summary>
        private static string MarkerOf(uint zone) => $"outposts695d_{zone}.txt";

        private static string editsFolder;
        /// <summary>Landscapes whose outposts are all placed (the home continent after start-up).</summary>
        private static readonly ConcurrentDictionary<uint, byte> ready = new();
        /// <summary>WorldServer.json OutpostPreload: also place the landscapes nobody has visited, one after another.</summary>
        private static bool preload;

        /// <summary>Are a landscape's outposts all placed (0 = the home continent)?</summary>
        public static bool Ready(uint zone) => ready.ContainsKey(zone == 0 ? Home : zone);

        /// <summary>What a restart (and the placement of more copies) needs of a ruin, without its decoded cells.</summary>
        private sealed record RuinInfo((uint X, uint Y, uint Z) Size, List<VoxelTemplate.Prop> Props, List<(int X, int Y, int Z)> Cells);
        private static readonly ConcurrentDictionary<string, RuinInfo> ruinInfo = new();

        private static void Remember(string ruin, VoxelTemplate t)
            => ruinInfo[ruin] = new RuinInfo(t.Size, t.Props, t.Cells.Select(c => (c.X, c.Y, c.Z)).ToList());

        /// <summary>A ruin's size, props and cell positions: from the home continent's start-up, else read without its cells.</summary>
        private static RuinInfo InfoOf(string ruin)
        {
            if (ruinInfo.TryGetValue(ruin, out RuinInfo info))
                return info;
            string path = Path.Combine(VoxelTemplate.Folder, ruin + ".otd");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                return null;
            try
            {
                VoxelTemplate t;
                using (var stream = File.OpenRead(path))
                    t = VoxelTemplate.Load(stream, withCells: false);
                info = new RuinInfo(t.Size, t.Props, VoxelTemplate.CellPositions(path));
                ruinInfo[ruin] = info;
                return info;
            }
            catch (Exception e)
            {
                log.Warn($"Outpost ruin {ruin} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>One line of a landscape marker: slot cavern, ruin, corner voxel, number in the landscape, standing spot.</summary>
        private sealed record Placed(int Cavern, string Ruin, (long X, long Y, long Z) Corner, int N, Vector3 Floor);

        private static List<Placed> ReadMarker(uint zone, out bool complete)
        {
            complete = false;
            var list = new List<Placed>();
            string path = Path.Combine(editsFolder ?? "", MarkerOf(zone));
            if (!File.Exists(path))
                return list;
            CultureInfo c = CultureInfo.InvariantCulture;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#complete", StringComparison.Ordinal))
                {
                    complete = true;
                    continue;
                }
                string[] f = line.Split('\t');
                if (line.StartsWith('#') || f.Length < 9 || !int.TryParse(f[0], out int cavern) || !long.TryParse(f[2], out long x)
                    || !long.TryParse(f[3], out long y) || !long.TryParse(f[4], out long z) || !int.TryParse(f[5], out int n)
                    || !float.TryParse(f[6], NumberStyles.Float, c, out float fx) || !float.TryParse(f[7], NumberStyles.Float, c, out float fy)
                    || !float.TryParse(f[8], NumberStyles.Float, c, out float fz))
                    continue;
                list.Add(new Placed(cavern, f[1], (x, y, z), n, new Vector3(fx, fy, fz)));
            }
            return list;
        }

        private static string MarkerLine(Placed p)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return string.Join('\t', p.Cavern, p.Ruin, p.Corner.X, p.Corner.Y, p.Corner.Z, p.N,
                p.Floor.X.ToString("R", c), p.Floor.Y.ToString("R", c), p.Floor.Z.ToString("R", c));
        }

        /// <summary>
        /// Registers an outpost (world thread / start-up): the server's cavern of its landscape nearest to it (one outpost
        /// each), its props. Null when it has no cavern or the index is taken.
        /// </summary>
        private static Outpost Add(uint zone, int index, string ruin, (long X, long Y, long Z) corner, Vector3 floor, IEnumerable<VoxelTemplate.Prop> props,
            int slotCavern)
        {
            if (byIndex.ContainsKey(index))
                return null;
            int cavern = Caves.In(zone).Where(i => !byCavern.ContainsKey(i))
                .OrderBy(i => Vector3.DistanceSquared(Caves.Caverns[i].At, floor)).DefaultIfEmpty(-1).First();
            if (cavern < 0)
                return null;
            var o = new Outpost
            {
                Index = index,
                Zone = zone,
                Cavern = cavern,
                SlotCavern = slotCavern,
                Ruin = ruin,
                Name = Title(ruin),
                Floor = floor,
                Corner = corner
            };
            BuildProps(o, props);
            outposts.Add(o);
            byCavern[cavern] = o;
            byIndex[index] = o;
            if (!byZone.TryGetValue(zone, out List<Outpost> list))
                byZone[zone] = list = new List<Outpost>();
            list.Add(o);
            return o;
        }

        // ---- start-up: what was placed before ----------------------------------------------------------------------------

        private static readonly object workSync = new();
        /// <summary>Landscapes asked for (a first visit, a trip, a placement stopped half way), in order.</summary>
        private static readonly List<uint> requested = new();
        /// <summary>Landscapes whose cavern floors, treasure chests and sight grids are still to be made ready.</summary>
        private static readonly List<uint> lightPending = new();
        private static readonly HashSet<uint> lightDone = new(), failed = new();
        /// <summary>The ruins each landscape's restored outposts use (sight grids to build when the home set lacks one).</summary>
        private static readonly Dictionary<uint, HashSet<string>> restoredRuins = new();
        private static readonly ConcurrentDictionary<uint, string> landStatus = new();
        private static readonly SemaphoreSlim wake = new(0);
        private static Thread worker;
        /// <summary>The landscape the background worker is on (0 = none).</summary>
        private static volatile uint working;

        /// <summary>Placed outposts handed from the background worker to the world tick (<see cref="TakeArrivals"/>).</summary>
        private sealed record Arrival(uint Zone, int Index, string Ruin, (long X, long Y, long Z) Corner, Vector3 Floor, List<VoxelTemplate.Prop> Props,
            int SlotCavern);
        private static readonly ConcurrentQueue<Arrival> arrivals = new();
        private static readonly ConcurrentQueue<(uint Zone, List<ulong> Cells)> changedCells = new();

        /// <summary>
        /// Start-up, after the home continent: every other landscape with caverns (Caves.Landscapes). Outposts stamped in an
        /// earlier run come back from the landscape's marker without decoding a ruin (cell positions and props only; their
        /// cells marked permanent before the first heal pass, which would otherwise regrow them); a landscape whose marker
        /// says "#complete" is ready. The rest wait for <see cref="Prepare"/> (or OutpostPreload).
        /// </summary>
        private static void LoadLandscapes()
        {
            preload = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.OutpostPreload ?? false;
            List<uint> lands = Caves.Landscapes.Where(z => z != Home).ToList();
            if (lands.Count == 0)
                return;
            if (!Directory.Exists(VoxelTemplate.Folder))
            {
                foreach (uint zone in lands)
                    ready[zone] = 1;
                log.Warn($"No outpost ruins ({VoxelTemplate.Folder} missing): the other landscapes' Chaos Caverns have no outposts.");
                return;
            }
            int restored = 0, complete = 0, partial = 0;
            foreach (uint zone in lands)
            {
                lightPending.Add(zone);
                int baseIndex = IndexBase(zone);
                if (baseIndex < 0)
                {
                    ready[zone] = 1;
                    log.Warn($"Landscape {zone} has caverns but no outpost index space (zones 391-421 only): no outposts there.");
                    continue;
                }
                List<Placed> saved = ReadMarker(zone, out bool done);
                var cells = new HashSet<ulong>();
                var ruins = new HashSet<string>();
                foreach (Placed p in saved)
                {
                    if (p.N < 0 || p.N >= PerLandscape || InfoOf(p.Ruin) is not RuinInfo info)
                        continue;
                    cells.UnionWith(CellsOf(info.Cells, p.Corner));
                    if (Add(zone, baseIndex + p.N, p.Ruin, p.Corner, p.Floor, info.Props, p.Cavern) != null)
                    {
                        restored++;
                        ruins.Add(p.Ruin);
                    }
                }
                if (cells.Count > 0)
                    EnvironmentServer.Instance.MarkPermanent(cells, zone);
                restoredRuins[zone] = ruins;
                if (done)
                {
                    ready[zone] = 1;
                    complete++;
                    landStatus[zone] = "ready";
                }
                else if (saved.Count > 0)
                {
                    // Stopped half way (a restart while it was stamping): finished in the background.
                    requested.Add(zone);
                    partial++;
                    landStatus[zone] = "queued (unfinished)";
                }
            }
            log.Info($"Outposts on {lands.Count} more landscape(s): {complete} placed before ({restored} outposts restored), {partial} to finish; "
                + $"the rest are placed on their first visit{(preload ? ", and one after another in the background (OutpostPreload)" : "")}.");
            StartWorker();
        }

        // ---- the background worker: one landscape at a time ------------------------------------------------------------

        /// <summary>
        /// Queues a landscape's outposts for placement (a player is there for the first time, or a trip picked it): the
        /// background worker places them before the landscapes merely preloaded. Nothing when they are placed or queued.
        /// </summary>
        public static void Prepare(uint zone)
        {
            zone = zone == 0 ? Home : zone;
            if (!Caves.HasCaverns(zone) || Ready(zone))
                return;
            lock (workSync)
            {
                if (requested.Contains(zone) || working == zone || failed.Contains(zone))
                    return;
                requested.Add(zone);
                landStatus[zone] = "queued";
            }
            log.Info($"Outposts: landscape {zone} queued for placement (first visit or a trip).");
            StartWorker();
            wake.Release();
        }

        /// <summary>Dev "cavelands &lt;zone&gt;": queues a landscape now, also one whose placement failed before.</summary>
        public static void Retry(uint zone)
        {
            lock (workSync)
                failed.Remove(zone == 0 ? Home : zone);
            Prepare(zone);
        }

        private static void StartWorker(bool force = false)
        {
            lock (workSync)
            {
                if (worker != null || (!force && Caves.Landscapes.All(z => z == Home)))
                    return;
                // Below the world tick: the stamping takes CPU only when the server has it to spare.
                worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "outpost placement" };
                worker.Start();
            }
        }

        private static void Work()
        {
            while (true)
            {
                uint zone = 0;
                bool stamp = false;
                // A rotation the last shutdown stopped half way first, then rotations (below the landscapes' placement).
                if (TakeRotationWork() is Action rotation)
                {
                    RunRotation(rotation);
                    continue;
                }
                lock (workSync)
                {
                    if (requested.Count > 0)
                    {
                        zone = requested[0];
                        requested.RemoveAt(0);
                        stamp = true;
                    }
                    else if (lightPending.Count > 0)
                    {
                        zone = lightPending[0];
                        lightPending.RemoveAt(0);
                    }
                    else if (preload && Caves.Landscapes.FirstOrDefault(z => !ready.ContainsKey(z) && !failed.Contains(z)) is uint next and not 0)
                    {
                        zone = next;
                        stamp = true;
                    }
                    working = zone;
                }
                if (zone == 0)
                {
                    if (TakeRotationWork(afterPlacement: true) is Action later)
                        RunRotation(later);
                    else
                        wake.Wait(TimeSpan.FromMinutes(1));
                    continue;
                }
                try
                {
                    PrepareLandscape(zone, stamp);
                }
                catch (EnvironmentUnavailableException e)
                {
                    // A remote Environment Server is away: nothing was recorded for the stamp that failed; try again later.
                    log.Warn($"Outposts of landscape {zone}: {e.Message} Trying again in a minute.");
                    landStatus[zone] = "waiting for the Environment Server";
                    Thread.Sleep(TimeSpan.FromMinutes(1));
                    lock (workSync)
                        if (!requested.Contains(zone))
                            requested.Add(zone);
                }
                catch (Exception e)
                {
                    log.Error(e, $"Outposts of landscape {zone} could not be placed.");
                    landStatus[zone] = $"failed: {e.Message}";
                    lock (workSync)
                        failed.Add(zone);
                }
                finally
                {
                    working = 0;
                }
            }
        }

        /// <summary>
        /// Background: a landscape made ready for players — its cavern floors found (and its cave index built), its treasure
        /// chests' sites, the sight grids of ruins only it uses — and with <paramref name="stamp"/> its outposts placed.
        /// </summary>
        private static void PrepareLandscape(uint zone, bool stamp)
        {
            bool first;
            lock (workSync)
            {
                first = lightDone.Add(zone);
                lightPending.Remove(zone);
            }
            if (first)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                landStatus[zone] = "preparing";
                Caves.WarmFloors(zone);
                LootChests.PrepareLandscape(zone);
                HashSet<string> ruins;
                lock (workSync)
                    ruins = restoredRuins.TryGetValue(zone, out var set) ? new HashSet<string>(set) : new HashSet<string>();
                foreach (string ruin in ruins.Where(r => !Sight.HasRuin(r)))
                {
                    try
                    {
                        VoxelTemplate t = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, ruin + ".otd"));
                        Sight.AddRuin(ruin, t);
                        Remember(ruin, t);
                    }
                    catch (Exception e)
                    {
                        log.Warn($"Outpost ruin {ruin}: no sight grid ({e.Message}).");
                    }
                    GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
                }
                log.Info($"Outposts: landscape {zone} prepared (cavern floors, treasure chests, sight) in {watch.ElapsedMilliseconds} ms.");
            }
            if (stamp && !Ready(zone))
                Place(zone);
            landStatus[zone] = Ready(zone) ? "ready" : "not placed yet (on its first visit)";
        }

        /// <summary>
        /// Background: places a landscape's outposts as the home continent's are (<see cref="ChooseSlots"/>: the 35 ruins once
        /// each in live's slots; <see cref="FillCaverns"/>: a copy in every cavern still empty), keeping what its marker says
        /// was stamped already, and stamps the new ones one decoded ruin at a time: its copies stamped, saved and packed, its
        /// cells marked permanent, a marker line each (so a restart resumes), then the ruin dropped before the next. Each
        /// outpost goes live on the next world tick (<see cref="TakeArrivals"/>), its cells to the players there.
        /// </summary>
        private static void Place(uint zone)
        {
            int baseIndex = IndexBase(zone);
            List<Placed> saved = ReadMarker(zone, out bool complete);
            if (complete || baseIndex < 0)
            {
                ready[zone] = 1;
                return;
            }
            var templates = new Dictionary<string, (uint X, uint Y, uint Z)>();
            foreach (string ruin in Ruins)
                try
                {
                    templates[ruin] = ruinInfo.TryGetValue(ruin, out RuinInfo known) ? known.Size
                        : VoxelTemplate.SizeOf(Path.Combine(VoxelTemplate.Folder, ruin + ".otd"));
                }
                catch (Exception e)
                {
                    log.Warn($"Outpost ruin {ruin} unreadable: {e.Message}");
                }
            var plan = saved.Select(p => (p.Cavern, p.Ruin, p.Corner)).ToList();
            int before = plan.Count;
            var (slots, gaps) = SlotsOf(zone);
            string[] missing = templates.Keys.Where(r => plan.All(x => x.Ruin != r)).ToArray();
            if (missing.Length > 0)
                plan.AddRange(ChooseSlots(missing, templates, plan, slots, gaps, zone));
            plan.AddRange(FillCaverns(templates, plan, slots, gaps, zone));
            int next = saved.Count == 0 ? 0 : saved.Max(p => p.N) + 1;
            var fresh = new List<(int Cavern, string Ruin, (long X, long Y, long Z) Corner, int N)>();
            foreach (var p in plan.Skip(before))
            {
                if (next >= PerLandscape)
                {
                    log.Warn($"Landscape {zone}: more than {PerLandscape} outposts planned; the rest left out.");
                    break;
                }
                fresh.Add((p.Cavern, p.Ruin, p.Corner, next++));
            }
            log.Info($"Outposts: placing {fresh.Count} outpost(s) on landscape {zone} ({saved.Count} placed before), one ruin at a time "
                + "in the background.");
            Directory.CreateDirectory(editsFolder);
            string marker = Path.Combine(editsFolder, MarkerOf(zone));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;
            double voxel = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            foreach (var copies in fresh.GroupBy(f => f.Ruin))
            {
                VoxelTemplate template;
                try
                {
                    template = VoxelTemplate.Load(Path.Combine(VoxelTemplate.Folder, copies.Key + ".otd"));
                }
                catch (Exception e)
                {
                    log.Warn($"Outpost ruin {copies.Key} unreadable: {e.Message}");
                    continue;
                }
                Remember(copies.Key, template);
                if (!Sight.HasRuin(copies.Key))
                    Sight.AddRuin(copies.Key, template);
                foreach (var c in copies)
                {
                    // As at home: the first standable spot of the stamped result, else above the slot's floor middle.
                    var slotFloor = new Vector3((float)(((c.Corner.X + 1 + template.Size.X / 2.0) * voxel - origin.X) / 10),
                        (float)(((c.Corner.Y + 1) * voxel - origin.Y) / 10), (float)(((c.Corner.Z + 1 + template.Size.Z / 2.0) * voxel - origin.Z) / 10));
                    Vector3 floor = StandingSpot(template, c.Corner, zone) ?? slotFloor + new Vector3(0f, 8f, 0f);
                    (List<ulong> cells, List<ulong> changed) = EnvironmentServer.Instance.StampTemplateAt(template, c.Corner.X, c.Corner.Y, c.Corner.Z, zone);
                    // Saved and packed at once, so a landscape's ruins never sit expanded together.
                    EnvironmentServer.Instance.Flush();
                    EnvironmentServer.Instance.CompactNow(true);
                    EnvironmentServer.Instance.MarkPermanent(cells, zone);
                    File.AppendAllLines(marker, new[] { MarkerLine(new Placed(c.Cavern, c.Ruin, c.Corner, c.N, floor)) });
                    arrivals.Enqueue(new Arrival(zone, baseIndex + c.N, c.Ruin, c.Corner, floor, template.Props, c.Cavern));
                    if (changed.Count > 0)
                        changedCells.Enqueue((zone, changed));
                    done++;
                    landStatus[zone] = $"stamping {done}/{fresh.Count}";
                    log.Info($"Stamped {c.Ruin} on landscape {zone} (cavern {c.Cavern}, outpost {baseIndex + c.N}): {VoxelTemplate.LastStampReport}; "
                        + $"heap {GC.GetTotalMemory(false) / 1048576} MB.");
                }
                // Dropped before the next ruin: a background collection, no long pause for the world tick.
                template = null;
                GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
            }
            File.AppendAllLines(marker, new[] { $"#complete\t{saved.Count + done}" });
            ready[zone] = 1;
            // The large-object heap is compacted at the runtime's next full collection (not forced here).
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            log.Info($"Outposts: landscape {zone} has its {saved.Count + done} outpost(s) ({done} stamped now in {watch.Elapsed.TotalMinutes:0.0} min). "
                + EnvironmentServer.Instance.MemoryReport());
        }

        /// <summary>
        /// World tick: outposts the worker placed go live (their cavern's creature group is rebuilt with the garrison, and the
        /// creature navigation drops its tiles under the new ruin, Pathing.OutpostsChanged), and their stamped cells go to the
        /// players on that landscape.
        /// </summary>
        private static void TakeArrivals()
        {
            while (arrivals.TryDequeue(out Arrival a))
                if (Add(a.Zone, a.Index, a.Ruin, a.Corner, a.Floor, a.Props, a.SlotCavern) is Outpost o)
                {
                    Pathing.OutpostsChanged(a.Zone);
                    Creatures.CavernChanged(o.Cavern);
                }
            while (changedCells.TryDequeue(out var c))
                ZoneTerrain.Broadcast(c.Cells, c.Zone);
        }

        /// <summary>Dev "cavelands": every landscape with caverns, its outposts and where its placement stands.</summary>
        public static string LandscapeReport()
        {
            var rows = Caves.Landscapes.Select(z => $"{z}: {Caves.In(z).Count()} caverns, {InZone(z).Count} outposts (index {IndexBase(z)}+), "
                + (Ready(z) ? "ready" : landStatus.GetValueOrDefault(z, "not placed yet")));
            uint now = working;
            return $"Chaos Cavern landscapes: {Caves.Landscapes.Count}, outposts {outposts.Count}, preload {preload}, worker "
                + (now != 0 ? $"on {now}" : "idle") + "; " + RotationReport() + "; " + string.Join("; ", rows);
        }
    }
}
