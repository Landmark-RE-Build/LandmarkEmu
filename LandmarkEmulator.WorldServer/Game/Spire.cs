using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
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
    /// The Portal Spires: one at the centre of every landscape (TASKS 6.49). Each is the teleporter hub "Spire" in the
    /// Travel window takes players to (cast 185) and the landscape's hub / graveyard respawn. Props come from the client's
    /// template CommonData/Voxel/templates/base_portal_spire.otd (<see cref="Terrain.VoxelTemplate"/>): the Hub (Models.txt
    /// 596), Replicator, Repair Station, Mailbox, The Crimson Parlor, Portal Crystal. Marker TerrainEdits/spire_base.txt
    /// (home) / spire_base_&lt;zone&gt;.txt holds the template corner as a global voxel.
    ///
    /// Placement (merge, 2026-10-19). The Spire template is only an air box over a vertex-only floor [P] — stamping it
    /// carved cliffs into hillsides and needed a fill pad downhill. Instead the Hub prop is seated into the landscape:
    /// the walkable deck (collision at pivot −2.924 m) sits just above the highest ground under its footprint
    /// (<see cref="DeckReference"/>) plus <see cref="Raise"/> (P5647), so the ~18 m plinth and ramp feet sit underground
    /// and players climb the ramps from natural ground. No embankment. The air template is not stamped; only columns
    /// where baked ground still pokes through the deck are carved to air (<see cref="ClearDeck"/>). Older cell-aligned
    /// and voxel+fill markers are reverted on start-up / first use (<see cref="RevertOldStamp"/>,
    /// <see cref="RevertVoxelStamp"/>) and placed again this way.
    ///
    /// Sources: wiki "Spire" / "Servers and Landscapes"; P5647 central spires raised slightly; P4946 nearest portal
    /// spire (<see cref="TravelTo"/>); P4965 pioneer arrival at <see cref="ArrivalOn"/>. No client per-landscape Spire
    /// position: every landscape uses <see cref="SpotFor"/> (nearest dry, level ground to the centre — including home,
    /// which left (0, 0) when that was a steep slope). Hub model AABB y −20.94 … +43.74, ramps' feet at pivot −20.94
    /// [P: .dme / .cdt].
    /// </summary>
    public static class Spire
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Search origin for every landscape's Spire (local metres; the landscape centre).</summary>
        private const float X = 0f, Z = 0f;
        private const uint BaseModel = 612, SpiresModel = 596;
        private const ulong GuidBase = 0x5350_0000_0000_0000;
        private const string TemplateFile = "base_portal_spire.otd", Marker = "spire_base.txt";

        /// <summary>The Spire of one landscape.</summary>
        private sealed class Site
        {
            public uint Zone;
            /// <summary>The template's corner (global voxel, 0.4 m grid); null for the template-less fallback.</summary>
            public (long X, long Y, long Z)? Corner;
            /// <summary>Air carved where ground poked through the deck (global voxels), null when none (<see cref="ClearDeck"/>).</summary>
            public FillBox? Deck;
            /// <summary>The Hub's pivot (local metres).</summary>
            public Vector3 Position;
            public List<PlayerUpdateAddLightweightProp> Props;
        }

        /// <summary>Spires by landscape; a null value: that landscape has none (no baked ground, no template, no room).</summary>
        private static readonly ConcurrentDictionary<uint, Site> sites = new();
        private static readonly object createLock = new();
        private static string editsFolder;

        /// <summary>Spire prop guid → its prop definition (PropDefs), for the ones players can use (every landscape's).</summary>
        private static readonly ConcurrentDictionary<ulong, uint> propDefinitions = new();

        /// <summary>The Spire's mailbox (PropDefs 489 "Mailbox").</summary>
        private const uint MailboxProp = 489;

        /// <summary>
        /// The Spire's barbershop (PropDefs 2942 "The Crimson Parlor"): clicking it opens the client's Barbershop window
        /// (CharacterCustomization.StartCustomization, run through InteractionRunClientLua).
        /// </summary>
        private const uint BarberProp = 2942;

        /// <summary>
        /// The Spire's Resupply Chest (PropDefs 884, in the Spire template). 31 Mar 2015 hotfix: new players start with
        /// building tools, and the chest at the spires replaces missing ones [P]. The client keeps item group 6
        /// = {3253 Adventurer's Mining Pick, 3259 Adventurer's Logging Axe} (ItemGroupMappings, the starter tools) and the
        /// text 562716 "Retrieving your missing Adventurer's Tools." that no client table uses [P]; using the chest gives back
        /// each of the two the player no longer has [I: the trigger and rule from the hotfix note] (OPEN-LEADS L161).
        /// </summary>
        public const uint ResupplyChestProp = 884;
        private static readonly uint[] StarterTools = { 3253, 3259 };
        private const uint RetrievingToolsText = 562716;

        public static bool IsResupplyChest(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == ResupplyChestProp;

        /// <summary>
        /// The Founder's Chest (PropDefs 898, name text 562116 "Theme_FoundersPack_Chests_Founder01", model 724
        /// Theme_FoundersPack_Chests_Founder01.adr [P prop dump]): the Founder's Chest at each hub gives back whatever
        /// is missing (hotfix 28 Mar 2014) and a fix for the Founder's Chest sometimes not returning lost items (17 Dec
        /// 2014) [P developer notes]; the 2016 wiki still lists it at the Spire
        /// (it gives missing items only to alpha and beta testers, Spire; "travel
        /// to the spire and click the yellow and blue Founder's Chest", Noble's Regalia) [W]. Using it runs
        /// <see cref="Entitlements.RestoreFounderItems"/> (trace/0695/audit-wiki-gaps.md #11).
        /// <para>
        /// Where it stands: the client's Spire template holds no prop 898, only the Resupply Chest 884 — whose model is the
        /// same founder chest (724) [P]. So it is added by the server beside the Resupply Chest, <see cref="FounderChestOffset"/>
        /// along that chest's own sideways axis, same turn and scale [I: live's spot is not known; that live may have had
        /// one chest doing both is an open question, OPEN-LEADS].
        /// </para>
        /// </summary>
        public const uint FounderChestProp = 898;
        private const uint FounderChestModel = 724;
        /// <summary>Beside the Resupply Chest, in that chest's own frame (metres) [I].</summary>
        private static readonly Vector3 FounderChestOffset = new(1.6f, 0f, 0f);
        /// <summary>The Founder's Chest's guid index on every Spire, outside the template's 1..n sequence.</summary>
        private const ulong FounderChestIndex = 0x898;

        public static bool IsFounderChest(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == FounderChestProp;

        /// <summary>The Resupply Chest: gives back the starter tools this player has none of (bag, equipped or vault).</summary>
        public static int Resupply(WorldSession session)
        {
            int given = 0;
            foreach (uint tool in StarterTools)
                if (session.Inventory.CountOf(tool) == 0)
                {
                    session.GiveItem(tool, 1);
                    given++;
                }
            if (given > 0)
                Chat.System(session, Shared.GameTable.Text.TextManager.Instance.GetTextForId(RetrievingToolsText)
                    ?? "Retrieving your missing Adventurer's Tools.");
            log.Info($"{session.CharacterName}: Resupply Chest, {given} starter tool(s) given back.");
            return given;
        }
        private static Terrain.VoxelTemplate template;
        private static Terrain.VoxelTemplate.Prop templateSpire;

        /// <summary>
        /// Where players arrive at the Spire (travel, respawn, first entry), from its pivot (the Hub prop): open platform
        /// beside the Portal Crystal. In front of the Spire (+Z) is inside its pillar, and the client pushed players out
        /// under the platform. Checked against the model's collision mesh (Theme_Widgets_Teleporters_PortalSpires01.cdt,
        /// the Hub turned by the template's rotation (0, 0.9222, 0, 0.3868), 134.5° about +y) [P]: the point is 9.7 m from
        /// the pivot, over the central dais, whose top is at pivot −1.537 m and reaches ~10-11 m out (the Portal Crystal
        /// prop stands at exactly that height, pivot −1.533); the deck around it is at −2.924 (where the Replicator,
        /// Repair Station and Crimson Parlor stand, −2.924) and the raised arms at −1.144 (Mailbox, Resupply Chest). So
        /// the arrival is 1.04 m over solid collision, with nothing above it below the crystal's own collision (5.4 m and
        /// more over the dais). The player's position is its feet: at home the client settled at 1203.70 = pivot −1.537
        /// (MODLOG 2026-10-11). Falling through to the floor (landscape 395, 2026-10-17) is the deck's collision not
        /// being there yet, not this point: see <see cref="Enter"/>.
        /// </summary>
        private static readonly Vector3 ArrivalOffset = new(7.2f, -0.5f, -6.5f);

        private static uint Home => Terrain.TerrainStore.HomeContinent;

        /// <summary>The home continent's Spire arrival point (local metres), or null where it couldn't be placed.</summary>
        public static Vector3? Arrival => ArrivalOn(Home);

        /// <summary>
        /// A landscape's Spire arrival point (local metres), or null when it has none. Builds (stamps) that landscape's
        /// Spire on first use.
        /// </summary>
        public static Vector3? ArrivalOn(uint zone) => Ensure(zone) is Site site ? site.Position + ArrivalOffset : null;

        /// <summary>The home Spire's guid (also its id as a respawn site).</summary>
        public static ulong Guid => GuidBase | 1;

        /// <summary>A landscape's Spire guid (its respawn site id): the home one is <see cref="Guid"/>.</summary>
        public static ulong GuidOn(uint zone) => GuidFor(zone, 1);

        /// <summary>
        /// Prop guids: the home Spire's keep GuidBase | index (saved respawn choices and the warp hub node use them);
        /// another landscape's carry its zone id from bit 24 up, so every Spire's props are distinct entities.
        /// </summary>
        private static ulong GuidFor(uint zone, ulong index) => GuidBase | (zone == Home ? 0 : (ulong)zone << 24) | index;

        /// <summary>The home Spire's foot (local metres), or null where it couldn't be placed.</summary>
        public static Vector3? Position => Ensure(Home)?.Position;

        /// <summary>
        /// Loads the Spire template and builds the home continent's Spire (stamping its base into the terrain the first
        /// time, after TerrainEdits.Load). Other landscapes get theirs on first use (<see cref="Ensure"/>).
        /// </summary>
        public static void Load(string gameDirectory, string editsFolder)
        {
            Spire.editsFolder = editsFolder;
            string path = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "templates", TemplateFile);
            if (!File.Exists(path))
                log.Warn($"Spire template {path} not found; the home Spire stands on the bare ground, other landscapes have none.");
            else
            {
                try
                {
                    template = Terrain.VoxelTemplate.Load(path);
                    templateSpire = template.Props.FirstOrDefault(p => p.ModelId == SpiresModel) ?? template.Props.FirstOrDefault();
                    if (templateSpire == null)
                        template = null;
                }
                catch (Exception e)
                {
                    log.Error(e, "Could not read the Spire template.");
                    template = null;
                }
            }
            // No ore on any Spire platform: new veins keep out (veins already there are cleared when a Spire is built).
            Terrain.OreVeins.NoVeins = Touches;
            if (Ensure(Home) is Site home && home.Corner != null)
            {
                var (min, max) = PlatformBox(home);
                int before = Terrain.EnvironmentServer.Instance.VeinsNear(home.Position, 120f, Home).Count;
                Terrain.ZoneTerrain.ClearVeinsInSite(Home, min, max);
                int after = Terrain.EnvironmentServer.Instance.VeinsNear(home.Position, 120f, Home).Count;
                log.Info($"Spire platform kept free of ore: veins within 120 m of the Spire {before} -> {after}.");
            }
        }

        /// <summary>A landscape's Spire if it is already built (never builds one: safe inside terrain / vein code).</summary>
        private static Site Peek(uint zone) => sites.TryGetValue(zone, out Site site) ? site : null;

        /// <summary>
        /// Builds a landscape's Spire now if it isn't yet (Landscapes / Pioneer call this before anyone arrives, so the
        /// platform is in the terrain before the client streams it). Returns whether the landscape has one.
        /// </summary>
        public static bool Prepare(uint zone) => Ensure(zone) != null;

        /// <summary>
        /// A landscape's Spire, building it the first time: from its saved marker, else by finding its spot and stamping
        /// the template (cells broadcast to anyone already there). Null when the landscape can't have one.
        /// </summary>
        private static Site Ensure(uint zone)
        {
            if (sites.TryGetValue(zone, out Site site))
                return site;
            lock (createLock)
            {
                if (sites.TryGetValue(zone, out site))
                    return site;
                try
                {
                    site = Create(zone);
                    // Its props too, before anyone is moved there: built lazily in Enter they took ~4 s on a landscape's
                    // first visit, after the arriving client was ready, and it fell from the deck to the floor meanwhile
                    // (landscape 395, 2026-10-17: first visit y 1268.2 = floor, later visits 1287.7 = the dais).
                    if (site != null)
                        PropsOf(site);
                }
                catch (Exception e)
                {
                    log.Error(e, $"Could not build the Spire of landscape {zone}.");
                    site = null;
                }
                sites[zone] = site;
            }
            // The contest-winning build placed beside this Spire (TASKS 3.20), stamped once, after the Spire exists.
            if (site != null)
                LandmarksOfLandmark.Prepare(zone);
            return site;
        }

        private static Site Create(uint zone)
        {
            if (template == null)
                // Without the template only the home continent has a Spire: its two models on the bare ground.
                return zone == Home && Terrain.TerrainHeights.OnGround(X, Z) is Vector3 bare ? new Site { Zone = zone, Position = bare } : null;
            if (zone != Home && Landscapes.Get(zone) == null)
                return null;

            string marker = editsFolder == null ? null : Path.Combine(editsFolder, zone == Home ? Marker : $"spire_base_{zone}.txt");
            (long X, long Y, long Z)? saved = null;
            FillBox? savedDeck = null;
            bool deckDone = false;
            if (marker != null && File.Exists(marker))
            {
                string[] f = File.ReadAllText(marker).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 4 && f[0] == MarkerMerge)
                {
                    var oldCorner = (long.Parse(f[1]), long.Parse(f[2]), long.Parse(f[3]));
                    FillBox? oldDeck = null;
                    if (f.Length == 11 && f[4] == MarkerDeck)
                        oldDeck = new FillBox(long.Parse(f[5]), long.Parse(f[6]), long.Parse(f[7]), long.Parse(f[8]), long.Parse(f[9]), long.Parse(f[10]));
                    Vector3 oldPivot = Local(oldCorner, templateSpire.Position);
                    // Merge on a steep spot floats the downhill side (deck seats to the high ground). Re-pick.
                    bool flatEnough = Survey(zone, oldPivot.X, oldPivot.Z, out _, out float deckSpread, out _)
                        && deckSpread <= MaxDeckSpread;
                    if (!flatEnough)
                    {
                        RevertDeckCarve(zone, oldDeck);
                        File.Move(marker, marker + ".steep-reverted", true);
                        log.Info($"Landscape {zone}: Spire at ({oldPivot.X:F0}, {oldPivot.Z:F0}) deck relief "
                            + $"{deckSpread:F1} m (need ≤ {MaxDeckSpread} m); picking a flatter spot.");
                    }
                    else
                    {
                        saved = oldCorner;
                        if (f.Length >= 5 && f[4] == MarkerDeck)
                        {
                            deckDone = true;
                            savedDeck = oldDeck;
                        }
                    }
                }
                else if (f.Length >= 4 && f[0] == MarkerVoxel)
                {
                    // Pre-merge (2026-10-17 carve + fill): drop the air stamp and embankment, place again merged.
                    FillBox? oldFill = null;
                    if (f.Length == 11 && f[4] == "fill")
                        oldFill = new FillBox(long.Parse(f[5]), long.Parse(f[6]), long.Parse(f[7]), long.Parse(f[8]), long.Parse(f[9]), long.Parse(f[10]));
                    RevertVoxelStamp(zone, (long.Parse(f[1]), long.Parse(f[2]), long.Parse(f[3])), oldFill);
                    File.Move(marker, marker + ".voxel-reverted", true);
                }
                else if (f.Length == 3 && uint.TryParse(f[0], out uint cx) && uint.TryParse(f[1], out uint cy) && uint.TryParse(f[2], out uint cz))
                {
                    RevertOldStamp(zone, (cx, cy, cz));
                    File.Move(marker, marker + ".cell-reverted", true);
                }
            }
            (long X, long Y, long Z) corner;
            FillBox? deck = savedDeck;
            var broadcast = new List<ulong>();
            if (saved is { } known)
            {
                corner = known;
                if (savedDeck is FillBox done)
                    Terrain.EnvironmentServer.Instance.MarkPermanent(CellsOf(done), zone);
            }
            else
            {
                if (SpotFor(zone) is not Vector3 spot)
                {
                    log.Warn($"Landscape {zone}: no ground found for its Spire (no baked heights or no free dry land near the centre).");
                    return null;
                }
                corner = CornerFor(zone, spot);
                Vector3 pivot = Local(corner, templateSpire.Position);
                log.Info($"Spire of landscape {zone}: merge seat at voxel {corner}, pivot {pivot.Y:F1} m, deck "
                    + $"{pivot.Y - DeckBelowPivot:F1} m (ground under deck ≤ {pivot.Y - DeckBelowPivot - Raise:F1} m +{Raise} m), "
                    + $"centre ({spot.X:F0}, {spot.Z:F0}); no air-box stamp.");
            }
            if (!deckDone)
            {
                (deck, List<ulong> cleared) = ClearDeck(zone, corner);
                broadcast.AddRange(cleared);
                if (marker != null)
                {
                    Directory.CreateDirectory(editsFolder);
                    File.WriteAllText(marker, $"{MarkerMerge} {corner.X} {corner.Y} {corner.Z} {MarkerDeck} "
                        + (deck is FillBox b ? $"{b.MinX} {b.MinY} {b.MinZ} {b.MaxX} {b.MaxY} {b.MaxZ}" : "none"));
                }
            }
            var site = new Site { Zone = zone, Corner = corner, Deck = deck, Position = Local(corner, templateSpire.Position) };
            if (broadcast.Count > 0)
            {
                Terrain.ZoneTerrain.Broadcast(broadcast.Distinct().ToList(), zone);
                if (zone != Home)
                {
                    var (min, max) = PlatformBox(site);
                    Terrain.ZoneTerrain.ClearVeinsInSite(zone, min, max);
                }
            }
            return site;
        }

        /// <summary>Marker word since merge placement (2026-10-19): "merge x y z".</summary>
        private const string MarkerMerge = "merge";
        /// <summary>Marker word before the deck-carve box ("deck none" or "deck x0 y0 z0 x1 y1 z1").</summary>
        private const string MarkerDeck = "deck";
        /// <summary>Pre-merge marker word (2026-10-17 carve + fill): still recognised to revert.</summary>
        private const string MarkerVoxel = "voxel";

        /// <summary>A box of global voxels, both ends included (deck carve).</summary>
        private readonly record struct FillBox(long MinX, long MinY, long MinZ, long MaxX, long MaxY, long MaxZ);

        /// <summary>Every level-2 cell a voxel box touches.</summary>
        private static List<ulong> CellsOf(FillBox b)
        {
            int size = Terrain.BlockData.Size;
            var cells = new List<ulong>();
            for (long cz = FloorDiv(b.MinZ, size); cz <= FloorDiv(b.MaxZ, size); cz++)
                for (long cy = FloorDiv(b.MinY, size); cy <= FloorDiv(b.MaxY, size); cy++)
                    for (long cx = FloorDiv(b.MinX, size); cx <= FloorDiv(b.MaxX, size); cx++)
                        cells.Add(Terrain.TerrainStore.CellId(Terrain.TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz));
            return cells;
        }

        private static long FloorDiv(long a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>
        /// Half-width of the walkable deck / arms around the pivot, metres (model AABB ±47.19; arms to ~46 m on the axes)
        /// [P footprint]. Used to seat the deck above ground and to carve only where ground still pokes through.
        /// </summary>
        private const float DeckHalf = 48f;

        /// <summary>
        /// Main deck collision below the Hub pivot, metres (.cdt deck at −2.924; dais −1.537, arms −1.144). Seating and
        /// deck carve use this plane so the walk surface clears the landscape.
        /// </summary>
        private const float DeckBelowPivot = 2.924f;

        /// <summary>Hub model base below pivot, metres (.dme AABB y −20.94) [P].</summary>
        private const float ModelBaseBelow = 20.94f;

        /// <summary>
        /// How far the deck sits over the highest ground under it, metres. P5647: central spires raised slightly so grass
        /// did not overgrow them [P text]; 0.8 m = two voxels [I], same as the pre-merge raise.
        /// </summary>
        private const float Raise = 0.8f;

        /// <summary>
        /// Highest dry ground under the deck footprint around (x, z), or null if any sample is missing / under water.
        /// The deck is seated on this so hills under the walk area do not poke through; ground outside the deck may still
        /// bury legs and ramps (merge).
        /// </summary>
        private static float? DeckReference(uint zone, float x, float z)
        {
            float step = DeckHalf / 4f;
            float high = float.MinValue;
            for (int j = -4; j <= 4; j++)
                for (int i = -4; i <= 4; i++)
                {
                    if (Terrain.TerrainHeights.HeightAt(x + i * step, z + j * step, zone) is not float h || h < DryLand)
                        return null;
                    high = MathF.Max(high, h);
                }
            return high;
        }

        /// <summary>
        /// Air where baked ground still rises through the deck after seating. Only those columns — not a full template
        /// clear — so surrounding slopes keep merging with the ramps and legs. Returns the box written (null: nothing).
        /// </summary>
        private static (FillBox? Box, List<ulong> Cells) ClearDeck(uint zone, (long X, long Y, long Z) corner)
        {
            var env = Terrain.EnvironmentServer.Instance;
            int size = Terrain.BlockData.Size;
            double voxel = Terrain.TerrainEdits.VoxelSize;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            Vector3 pivot = Local(corner, templateSpire.Position);
            float deckY = pivot.Y - DeckBelowPivot;
            long x0 = (long)Math.Floor(((pivot.X - DeckHalf) * 10 + origin.X) / voxel);
            long x1 = (long)Math.Ceiling(((pivot.X + DeckHalf) * 10 + origin.X) / voxel);
            long z0 = (long)Math.Floor(((pivot.Z - DeckHalf) * 10 + origin.Z) / voxel);
            long z1 = (long)Math.Ceiling(((pivot.Z + DeckHalf) * 10 + origin.Z) / voxel);
            // One voxel under the deck mesh so the walk surface is clear.
            long deckLow = (long)Math.Floor((deckY * 10 + origin.Y) / voxel) - 1;
            int w = (int)(x1 - x0 + 1), d = (int)(z1 - z0 + 1);
            var low = new long[w * d];
            var high = new long[w * d];
            long minY = long.MaxValue, maxY = long.MinValue, minX = long.MaxValue, maxX = long.MinValue, minZ = long.MaxValue, maxZ = long.MinValue;
            long columns = 0;
            for (int k = 0; k < d; k++)
                for (int i = 0; i < w; i++)
                {
                    int c = k * w + i;
                    high[c] = long.MinValue;
                    float lx = (float)(((x0 + i + 0.5) * voxel - origin.X) / 10), lz = (float)(((z0 + k + 0.5) * voxel - origin.Z) / 10);
                    if (MathF.Abs(lx - pivot.X) > DeckHalf || MathF.Abs(lz - pivot.Z) > DeckHalf)
                        continue;
                    if (Terrain.TerrainHeights.HeightAt(lx, lz, zone) is not float ground || ground <= deckY + 0.05f)
                        continue;
                    long top = (long)Math.Floor((ground * 10 + origin.Y) / voxel);
                    if (top < deckLow)
                        continue;
                    low[c] = deckLow;
                    high[c] = top;
                    columns++;
                    minY = Math.Min(minY, low[c]);
                    maxY = Math.Max(maxY, top);
                    minX = Math.Min(minX, x0 + i);
                    maxX = Math.Max(maxX, x0 + i);
                    minZ = Math.Min(minZ, z0 + k);
                    maxZ = Math.Max(maxZ, z0 + k);
                }
            var written = new List<ulong>();
            if (columns == 0)
            {
                log.Info($"Spire of landscape {zone}: deck clear (no ground through the walk surface).");
                return (null, written);
            }
            var box = new FillBox(minX, minY, minZ, maxX, maxY, maxZ);
            long voxels = 0;
            byte mid = Terrain.BlockData.CentreOffset;
            var values = new List<Terrain.TerrainEdits.VoxelValue>();
            for (long cz = FloorDiv(minZ, size); cz <= FloorDiv(maxZ, size); cz++)
                for (long cx = FloorDiv(minX, size); cx <= FloorDiv(maxX, size); cx++)
                    for (long cy = FloorDiv(minY, size); cy <= FloorDiv(maxY, size); cy++)
                    {
                        values.Clear();
                        for (int z = 0; z < size; z++)
                            for (int x = 0; x < size; x++)
                            {
                                long gx = cx * size + x, gz = cz * size + z;
                                if (gx < x0 || gx > x1 || gz < z0 || gz > z1)
                                    continue;
                                int c = (int)((gz - z0) * w + (gx - x0));
                                if (high[c] < low[c])
                                    continue;
                                long from = Math.Max(low[c], cy * size), to = Math.Min(high[c], cy * size + size - 1);
                                for (long gy = from; gy <= to; gy++)
                                    values.Add(new Terrain.TerrainEdits.VoxelValue(
                                        Terrain.TerrainEdits.VoxelIndex(x, (int)(gy - cy * size), z), 1, 0, mid, mid, mid));
                            }
                        if (values.Count == 0)
                            continue;
                        ulong id = Terrain.TerrainStore.CellId(Terrain.TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                        env.Write(id, values, zone, 0, Terrain.TerrainEdits.PoiLayer);
                        voxels += values.Count;
                        written.Add(id);
                        if (written.Count % 100 == 0)
                        {
                            env.Flush();
                            env.CompactNow(true);
                        }
                    }
            env.Flush();
            env.MarkPermanent(written, zone);
            env.CompactNow(true);
            log.Info($"Spire of landscape {zone}: cleared {columns} deck columns ({voxels} air voxels, {written.Count} cells) where ground poked through.");
            return (box, written);
        }

        /// <summary>
        /// Template corner for a Hub whose deck sits on <see cref="DeckReference"/> + <see cref="Raise"/> at
        /// <paramref name="spot"/>'s x / z. Pivot = deck + <see cref="DeckBelowPivot"/>; base plane
        /// (<see cref="ModelBaseBelow"/> under the pivot) ends up ~18 m under that ground — legs and lower ramps bury,
        /// players climb from the landscape.
        /// </summary>
        private static (long X, long Y, long Z) CornerFor(uint zone, Vector3 spot)
        {
            double voxel = Terrain.TerrainEdits.VoxelSize;
            float ground = DeckReference(zone, spot.X, spot.Z) ?? spot.Y;
            float pivotY = ground + Raise + DeckBelowPivot;
            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(new Vector3(spot.X, pivotY, spot.Z));
            return ((long)Math.Round((gx - templateSpire.Position.X * 10) / voxel),
                (long)Math.Round((gy - templateSpire.Position.Y * 10) / voxel),
                (long)Math.Round((gz - templateSpire.Position.Z * 10) / voxel));
        }

        /// <summary>
        /// Drops a pre-2026-10-17 Spire stamp (corner on the 16 m cell grid) from the terrain.
        /// </summary>
        private static void RevertOldStamp(uint zone, (uint X, uint Y, uint Z) cell)
        {
            double size = Terrain.TerrainEdits.CellSize;
            int cx = template.Cells.Max(c => c.X) + 1, cy = template.Cells.Max(c => c.Y) + 1, cz = template.Cells.Max(c => c.Z) + 1;
            List<ulong> reverted = Terrain.EnvironmentServer.Instance.RevertBox(cell.X * size, cell.Y * size, cell.Z * size,
                (cell.X + cx) * size, (cell.Y + cy) * size, (cell.Z + cz) * size, zone);
            Terrain.EnvironmentServer.Instance.Flush();
            Terrain.EnvironmentServer.Instance.CompactNow(true);
            Terrain.ZoneTerrain.Broadcast(reverted, zone);
            log.Info($"Landscape {zone}: reverted the old cell-aligned Spire stamp at cell {cell} ({reverted.Count} cells); placing it again.");
        }

        /// <summary>
        /// Drops a 2026-10-17 voxel-aligned air stamp and its fill embankment so the landscape can be merge-placed.
        /// </summary>
        private static void RevertVoxelStamp(uint zone, (long X, long Y, long Z) corner, FillBox? fill)
        {
            double voxel = Terrain.TerrainEdits.VoxelSize;
            var env = Terrain.EnvironmentServer.Instance;
            var reverted = new List<ulong>();
            reverted.AddRange(env.RevertBox(corner.X * voxel, corner.Y * voxel, corner.Z * voxel,
                (corner.X + template.Size.X) * voxel, (corner.Y + template.Size.Y) * voxel, (corner.Z + template.Size.Z) * voxel, zone));
            if (fill is FillBox f)
                reverted.AddRange(env.RevertBox(f.MinX * voxel, f.MinY * voxel, f.MinZ * voxel,
                    (f.MaxX + 1) * voxel, (f.MaxY + 1) * voxel, (f.MaxZ + 1) * voxel, zone));
            env.Flush();
            env.CompactNow(true);
            Terrain.ZoneTerrain.Broadcast(reverted.Distinct().ToList(), zone);
            log.Info($"Landscape {zone}: reverted the old voxel Spire stamp at {corner} ({reverted.Count} cells); merge-placing.");
        }

        /// <summary>Dry land at least this high (local metres; sea level is about 1000), as Landscapes.ArrivalPoint.</summary>
        private const float DryLand = 1005f;
        /// <summary>
        /// Max height spread under the walk footprint (±<see cref="DeckHalf"/>) for a spot, metres (ours). Deck seats to
        /// the high ground there, so every metre of relief is hang under the downhill edge — capped near the deck→base
        /// height (~18 m) so the low side still meets the plinth rather than floating under empty air.
        /// </summary>
        private const float MaxDeckSpread = 17f;
        /// <summary>
        /// Max height spread over the whole 124 m box for a spot, metres (ours). A little looser than
        /// <see cref="MaxDeckSpread"/> so rim terrain outside the walk footprint does not veto an otherwise good seat.
        /// </summary>
        private const float MaxBoxSpread = 22f;
        /// <summary>
        /// Worst deck spread still accepted as a last-resort "flattest" fallback (ours). Same as
        /// <see cref="MaxDeckSpread"/>: above the plinth height the landscape gets no Spire.
        /// </summary>
        private const float MaxDeckSpreadFallback = 17f;
        /// <summary>Ring spacing and reach of the spot search, metres (ours).</summary>
        private const float SearchStep = 20f, SearchReach = 3000f;
        /// <summary>Clear ground kept between a new Spire's platform and any build site, decimetres (ours: 20 m).</summary>
        private const double SiteMargin = 200;

        /// <summary>
        /// Ground relief around (x, z): box spread over the 124 m template, deck spread over ±<see cref="DeckHalf"/>,
        /// and a median under the deck for ranking. Null when any sample is missing or under water.
        /// </summary>
        private static bool Survey(uint zone, float x, float z, out float boxSpread, out float deckSpread, out float reference)
        {
            boxSpread = deckSpread = reference = 0f;
            if (Terrain.TerrainHeights.HeightAt(x, z, zone) is not float centre || centre < DryLand)
                return false;
            float boxHalf = template.Size.X * (float)Terrain.TerrainEdits.VoxelSize / 20f;   // 62 m
            float boxStep = boxHalf / 4f;
            float boxLow = float.MaxValue, boxHigh = float.MinValue;
            float deckLow = float.MaxValue, deckHigh = float.MinValue;
            Span<float> deckSamples = stackalloc float[81];
            int n = 0;
            for (int j = -4; j <= 4; j++)
                for (int i = -4; i <= 4; i++)
                {
                    float sx = x + i * boxStep, sz = z + j * boxStep;
                    if (Terrain.TerrainHeights.HeightAt(sx, sz, zone) is not float h || h < DryLand)
                        return false;
                    boxLow = MathF.Min(boxLow, h);
                    boxHigh = MathF.Max(boxHigh, h);
                    if (MathF.Abs(sx - x) <= DeckHalf && MathF.Abs(sz - z) <= DeckHalf)
                    {
                        deckLow = MathF.Min(deckLow, h);
                        deckHigh = MathF.Max(deckHigh, h);
                        deckSamples[n++] = h;
                    }
                }
            // Denser ring inside the deck (box step is 15.5 m; fill gaps so a narrow ridge is not missed).
            float deckStep = DeckHalf / 4f;
            for (int j = -4; j <= 4; j++)
                for (int i = -4; i <= 4; i++)
                {
                    if (i == 0 && j == 0)
                        continue;
                    float sx = x + i * deckStep, sz = z + j * deckStep;
                    if (MathF.Abs(sx - x) > DeckHalf || MathF.Abs(sz - z) > DeckHalf)
                        continue;
                    if (Terrain.TerrainHeights.HeightAt(sx, sz, zone) is not float h || h < DryLand)
                        return false;
                    deckLow = MathF.Min(deckLow, h);
                    deckHigh = MathF.Max(deckHigh, h);
                    if (n < deckSamples.Length)
                        deckSamples[n++] = h;
                }
            if (n == 0)
                return false;
            deckSamples.Slice(0, n).Sort();
            boxSpread = boxHigh - boxLow;
            deckSpread = deckHigh - deckLow;
            reference = deckSamples[n / 2];
            return true;
        }

        /// <summary>
        /// Where a landscape's Spire stands (local x / z; y = deck median for ranking — seating uses
        /// <see cref="DeckReference"/>): nearest dry point to the centre whose deck relief is ≤
        /// <see cref="MaxDeckSpread"/> and box relief ≤ <see cref="MaxBoxSpread"/>, clear of build sites; else the
        /// flattest deck within <see cref="MaxDeckSpreadFallback"/> out to 3 km. Same rule for home (no longer locked
        /// to (0, 0)). INFERRED (no client data names a spot).
        /// </summary>
        private static Vector3? SpotFor(uint zone)
        {
            if (!Terrain.TerrainHeights.Available(zone) && zone != Home)
                return null;
            if (zone == Home && !Terrain.TerrainHeights.Available(Home)
                && Terrain.TerrainHeights.OnGround(X, Z) is Vector3 bare)
                return bare;
            float half = DeckHalf;
            Vector3? flattest = null;
            float flattestDeck = float.MaxValue;
            for (float radius = 0f; radius <= SearchReach; radius += SearchStep)
            {
                int steps = Math.Max(1, (int)(radius * 2 * MathF.PI / SearchStep));
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * 2 * MathF.PI / steps;
                    float x = X + radius * MathF.Cos(angle), z = Z + radius * MathF.Sin(angle);
                    if (!Survey(zone, x, z, out float boxSpread, out float deckSpread, out float ground))
                        continue;
                    bool ok = deckSpread <= MaxDeckSpread && boxSpread <= MaxBoxSpread;
                    if (!ok && deckSpread >= flattestDeck)
                        continue;
                    var global = Terrain.TerrainEdits.ToGlobal(new Vector3(x, ground, z));
                    double reach = half * 10 + SiteMargin;
                    if (Claims.BoxesIn(zone, (global.X - reach, double.MinValue, global.Z - reach),
                            (global.X + reach, double.MaxValue, global.Z + reach)).Count > 0)
                        continue;
                    if (ok)
                        return new Vector3(x, ground, z);
                    if (deckSpread < flattestDeck)
                    {
                        flattest = new Vector3(x, ground, z);
                        flattestDeck = deckSpread;
                    }
                }
            }
            if (flattest is Vector3 best && flattestDeck <= MaxDeckSpreadFallback)
            {
                log.Warn($"Landscape {zone}: no Spire spot with deck relief ≤ {MaxDeckSpread} m within {SearchReach} m; "
                    + $"using the flattest deck ({flattestDeck:F1} m) at ({best.X:F0}, {best.Z:F0}).");
                return best;
            }
            if (flattest is Vector3 steep)
                log.Warn($"Landscape {zone}: flattest Spire deck still {flattestDeck:F1} m at ({steep.X:F0}, {steep.Z:F0}) "
                    + $"(over {MaxDeckSpreadFallback} m); no Spire.");
            else
                log.Warn($"Landscape {zone}: no dry free ground for a Spire within {SearchReach} m of the centre.");
            return null;
        }

        /// <summary>Drops a previous merge deck-carve so a steeper spot can be abandoned.</summary>
        private static void RevertDeckCarve(uint zone, FillBox? deck)
        {
            if (deck is not FillBox f)
                return;
            double voxel = Terrain.TerrainEdits.VoxelSize;
            List<ulong> reverted = Terrain.EnvironmentServer.Instance.RevertBox(
                f.MinX * voxel, f.MinY * voxel, f.MinZ * voxel,
                (f.MaxX + 1) * voxel, (f.MaxY + 1) * voxel, (f.MaxZ + 1) * voxel, zone);
            Terrain.EnvironmentServer.Instance.Flush();
            Terrain.EnvironmentServer.Instance.CompactNow(true);
            if (reverted.Count > 0)
                Terrain.ZoneTerrain.Broadcast(reverted, zone);
        }

        /// <summary>
        /// Is a point (local metres) on a landscape's Spire platform? Trees and plants are not grown there. Only Spires
        /// already built count (see <see cref="Peek"/>).
        /// </summary>
        public static bool Covers(uint zone, float x, float z)
        {
            if (template == null || Peek(zone) is not { Corner: not null } site)
                return false;
            var (lo, hi) = PlatformBox(site);
            var origin = Terrain.TerrainStore.ZoneOrigin;
            return x >= (lo.X - origin.X) / 10 && x <= (hi.X - origin.X) / 10 && z >= (lo.Z - origin.Z) / 10 && z <= (hi.Z - origin.Z) / 10;
        }

        /// <summary>How far around the platform mining and building are refused (decimetres; ours).</summary>
        private const double NoEditMargin = 100;

        /// <summary>Text 560944 "You are near a Portal Spire. You must move away before you can mine or build."</summary>
        private const uint NearSpireText = 560944;

        private static readonly Dictionary<ulong, DateTime> lastWarned = new();

        /// <summary>
        /// May a player mine or build in this box (global dm)? Not on or right beside a Portal Spire (hotfix 1 Apr 2016,
        /// P4425: mining next to the spire gives clearer feedback; the client's text 560944).
        /// Refused edits tell the player (at most every 3 s).
        /// </summary>
        public static bool MayEdit(ulong playerGuid, uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            if (!Touches(zone, (min.X - NoEditMargin, min.Y - NoEditMargin, min.Z - NoEditMargin),
                    (max.X + NoEditMargin, max.Y + NoEditMargin, max.Z + NoEditMargin)))
                return true;
            DateTime now = DateTime.UtcNow;
            bool tell;
            lock (lastWarned)
            {
                tell = !lastWarned.TryGetValue(playerGuid, out DateTime last) || now - last > TimeSpan.FromSeconds(3);
                if (tell)
                    lastWarned[playerGuid] = now;
            }
            if (tell && PlayerManager.FindByGuid(playerGuid) is WorldSession session)
                SystemMessages.Show(session, NearSpireText);
            return false;
        }

        /// <summary>
        /// Does a box (global dm) touch a landscape's Spire platform (the same area as <see cref="Covers"/>)? Ore veins
        /// keep out of it (<see cref="Terrain.OreVeins.NoVeins"/>). Only Spires already built count: this runs inside
        /// the terrain and vein code, which must never trigger a stamp.
        /// </summary>
        public static bool Touches(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            if (template == null || Peek(zone) is not { Corner: not null } site)
                return false;
            var (lo, hi) = PlatformBox(site);
            return max.X > lo.X && min.X < hi.X && max.Y > lo.Y && min.Y < hi.Y && max.Z > lo.Z && min.Z < hi.Z;
        }

        /// <summary>
        /// Every built Spire platform's box (global dm) with its landscape: the same areas <see cref="Touches"/> checks,
        /// sent to a remote Environment Server as its no-vein boxes (Terrain.RemoteEnvironmentServer; terrain-service.md §5.2).
        /// </summary>
        public static List<(uint Zone, double[] Box)> PlatformBoxes()
        {
            var boxes = new List<(uint, double[])>();
            if (template == null)
                return boxes;
            foreach ((uint zone, Site site) in sites.ToArray())
                if (site is { Corner: not null })
                {
                    var (lo, hi) = PlatformBox(site);
                    boxes.Add((zone, new[] { lo.X, lo.Y, lo.Z, hi.X, hi.Y, hi.Z }));
                }
            return boxes;
        }

        /// <summary>
        /// The platform's box in global dm: the Hub model's footprint (±47.19 m, plus 2 m) from the base plane up through
        /// the crown, grown for any deck-carve box. No trees / ore / digs there (deck-carve cells are permanent).
        /// </summary>
        private static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max) PlatformBox(Site site)
        {
            double voxel = Terrain.TerrainEdits.VoxelSize;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            Vector3 p = site.Position;
            const float half = 47.19f + 2f;
            const float top = 43.74f + 2f;
            (double X, double Y, double Z) min = ((p.X - half) * 10 + origin.X, (p.Y - ModelBaseBelow - 2f) * 10 + origin.Y, (p.Z - half) * 10 + origin.Z);
            (double X, double Y, double Z) max = ((p.X + half) * 10 + origin.X, (p.Y + top) * 10 + origin.Y, (p.Z + half) * 10 + origin.Z);
            if (site.Deck is FillBox f)
            {
                min = (Math.Min(min.X, f.MinX * voxel - 20), Math.Min(min.Y, f.MinY * voxel), Math.Min(min.Z, f.MinZ * voxel - 20));
                max = (Math.Max(max.X, (f.MaxX + 1) * voxel + 20), Math.Max(max.Y, (f.MaxY + 1) * voxel + 20), Math.Max(max.Z, (f.MaxZ + 1) * voxel + 20));
            }
            return (min, max);
        }

        /// <summary>
        /// The box (global dm) a Spire platform of a landscape takes, plus <paramref name="margin"/> dm around, if that
        /// landscape has a Spire (built on demand). Pioneer keeps automatic build sites off it.
        /// </summary>
        public static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max)? PlatformOn(uint zone, double margin = 0)
        {
            if (template == null || Ensure(zone) is not { Corner: not null } site)
                return null;
            var (lo, hi) = PlatformBox(site);
            return ((lo.X - margin, lo.Y - margin, lo.Z - margin), (hi.X + margin, hi.Y + margin, hi.Z + margin));
        }

        /// <summary>A point inside the template (metres from its corner) in local metres.</summary>
        private static Vector3 Local((long X, long Y, long Z) corner, Vector3 inTemplate)
        {
            double voxel = Terrain.TerrainEdits.VoxelSize;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            return new Vector3((float)((corner.X * voxel + inTemplate.X * 10 - origin.X) / 10),
                (float)((corner.Y * voxel + inTemplate.Y * 10 - origin.Y) / 10),
                (float)((corner.Z * voxel + inTemplate.Z * 10 - origin.Z) / 10));
        }

        /// <summary>The props of a Spire (built once per landscape).</summary>
        private static List<PlayerUpdateAddLightweightProp> PropsOf(Site site)
        {
            if (site.Props == null)
                lock (site)
                    site.Props ??= BuildProps(site);
            return site.Props;
        }

        /// <summary>
        /// Sends the Spire's props to a player entering a landscape or arriving after any re-zone (the client deletes its
        /// entities at every ClientBeginZoning) — the Spire built if needed. Their collision is not a matter of the full
        /// conversion (the client asks for it as for every prop; <see cref="SendFullAtOnce"/> pushes it for tests) but of
        /// the model's one-time collision build, re-run by <see cref="ReassertCollision"/> after the arrival
        /// (trace/0695/agent-prop-collision.md, 2026-10-17: arrivals fell through the deck until then).
        /// </summary>
        public static void Enter(WorldSession session, SpirePart part = SpirePart.All)
        {
            if (Ensure(session.ZoneId) is not Site site)
                return;
            List<PlayerUpdateAddLightweightProp> props = PropsOf(site).Where(p => InPart(p, part)).ToList();
            if (props.Count == 0)
                return;
            foreach (PlayerUpdateAddLightweightProp prop in props)
                session.EnqueueMessage(prop);
            if (SendFullAtOnce)
                foreach (PlayerUpdateAddLightweightProp prop in props)
                    session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            // The Replicator, Repair Station and Mailbox are usable; flag them so the client asks for their interaction
            // info (Props.SendInteractionInfo) and sends InteractRequest on a click.
            var flagging = new DynamicInteractionFlagging();
            flagging.Guids.AddRange(props.Select(p => p.Guid).Where(Usable));
            if (flagging.Guids.Count > 0)
                session.EnqueueMessage(flagging);
        }

        /// <summary>
        /// Which of a Spire's props <see cref="Enter"/> and <see cref="ReassertCollision"/> are for. While a loading screen
        /// holds the world streams (PlayerManager "Stream hold") only the deck goes early, so a player arriving there has
        /// collision; the stations, chests and crystal follow when the screen drops. A player arriving on the platform
        /// gets all of them early (they stand among them).
        /// </summary>
        public enum SpirePart { All, Deck, Rest }

        /// <summary>
        /// The deck: the Hub structure the players walk on (the Spires model, and the base model of a Spire without a
        /// template) [P models; the deck collision is the Hub model's, agent-prop-collision.md].
        /// </summary>
        private static bool IsDeck(PlayerUpdateAddLightweightProp prop) => prop.ModelId is SpiresModel or BaseModel;

        private static bool InPart(PlayerUpdateAddLightweightProp prop, SpirePart part)
            => part == SpirePart.All || IsDeck(prop) == (part == SpirePart.Deck);

        /// <summary>
        /// Answers the client's FullCharacterDataRequest for a Spire prop with LightweightToFullProp. Until it has it a
        /// prop stays a lightweight shell: no collision (players walked through the Spire) and no proximity
        /// interaction (the client's reach test uses the collision bounds).
        /// </summary>
        /// <summary>
        /// When, after an arrival, the Spire's props get their position again (ms after the re-zone finished) [I: the
        /// client retries at most once a second, and a slow load can take several seconds]. The 12 s and 24 s retries were
        /// dropped (audit-bandwidth-2026-10-18 R4, ≈0.9 KB per arrival): the deck's bodies were built 2.7-4.4 s after the
        /// zone load (Frida revisit1 / r71) [P], and FinishRezone, which starts this clock, already runs after the
        /// Spire's SpireBodyMs hold, so 6 s after it covers a load ~10 s slower than measured [I].
        /// </summary>
        private static readonly int[] CollisionRetryMs = { 1500, 3000, 6000 };

        /// <summary>Players further than this from the Spire skip the retries (their deck is not under them).</summary>
        private const float CollisionRetryRange = 300f;

        /// <summary>
        /// Gives the Spire's props their collision on the client after an arrival (trace/0695/agent-prop-collision.md)
        /// [P mechanism]. A prop's collision is built once, from its model's first complete update: Actor update
        /// FUN_143d0e060 → listener FUN_1430aabc0, which sets the one-time latch entity +0x4E9D and calls FUN_1430ac390 →
        /// FUN_142b7d510 (PhysX scene add). That build needs the model's current LOD (+0x2B4, from camera distance) to have
        /// its mesh loaded; while the arrival is still loading it can fail and the latch is spent, so the deck stayed
        /// without collision and arriving players fell through it (2026-10-17: every arrival from another landscape).
        /// The build runs again only when the prop is moved: PlayerUpdatePosition 0x7F (handler FUN_142ce6340 sets
        /// "moved" +0x4F00, retried at most once a second). So each prop gets its own unchanged transform a few times
        /// after the arrival — 7 small packets per retry, only for players near the Spire. Unrequested full conversions
        /// do nothing for collision (FUN_142d532f0) [P].
        /// </summary>
        public static void ReassertCollision(WorldSession session, SpirePart part = SpirePart.All)
        {
            uint zone = session.ZoneId;
            if (Peek(zone) is not Site site || site.Props == null)
                return;
            // Only the props this client was sent (the deck alone while a loading screen holds the rest).
            List<PlayerUpdateAddLightweightProp> props = site.Props.Where(p => InPart(p, part)).ToList();
            if (props.Count == 0)
                return;
            foreach (int ms in CollisionRetryMs)
                Teleport.After(session, ms, () =>
                {
                    if (session.ZoneId != zone || session.RezoneTarget != null
                        || Vector3.Distance(session.Position, site.Position) > CollisionRetryRange)
                        return;
                    foreach (PlayerUpdateAddLightweightProp prop in props)
                        session.EnqueueMessage(new PlayerUpdatePosition
                        {
                            TransientId = prop.TransientId,
                            Movement = Creatures.MovementBody(new Vector3(prop.Position.X, prop.Position.Y, prop.Position.Z),
                                new Quaternion(prop.Rotation.X, prop.Rotation.Y, prop.Rotation.Z, prop.Rotation.W), Vector3.Zero)
                        });
                });
        }

        /// <summary>
        /// Push each Spire prop's LightweightToFullProp with the prop, unasked (dev "spirefull on"). Off: the client asks
        /// for it (FullCharacterDataRequest, answered by <see cref="SendFull"/>), as for every other prop. A full conversion
        /// does nothing for collision (FUN_142d532f0; trace/0695/agent-prop-collision.md) [P].
        /// </summary>
        public static bool SendFullAtOnce { get; set; }

        public static bool SendFull(WorldSession session, ulong guid)
        {
            PlayerUpdateAddLightweightProp prop = Peek(session.ZoneId)?.Props?.FirstOrDefault(p => p.Guid == guid);
            if (prop == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            log.Debug($"Spire prop 0x{guid:X} (transient {prop.TransientId}): full data asked by {session.CharacterName}.");
            return true;
        }

        /// <summary>The crafting station a Spire prop works as (the Replicator, the Repair Station), or 0.</summary>
        public static uint StationAt(ulong guid)
            => propDefinitions.TryGetValue(guid, out uint def) ? CraftingStations.StationForProp(def) : 0;

        public static bool IsMailbox(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == MailboxProp;

        public static bool IsBarber(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == BarberProp;

        /// <summary>
        /// The guid of the home Spire's "Portal Spire (Hub)" prop (PropDefs 1871): the warp network's hub node (the Local
        /// Travel map picks nodes by the entity under the cursor), 0 before the Spire's props are built.
        /// </summary>
        public static ulong HubGuid => HubGuidOn(Home);

        /// <summary>A landscape's "Portal Spire (Hub)" prop guid (PropDefs 1871), 0 when it has no Spire.</summary>
        public static ulong HubGuidOn(uint zone)
        {
            if (Ensure(zone) is not Site site)
                return 0;
            return PropsOf(site).Select(p => p.Guid).FirstOrDefault(g => propDefinitions.TryGetValue(g, out uint def) && def == 1871);
        }

        /// <summary>
        /// A Spire's Portal Crystal (PropDefs 575): clicking it is the warp hub's terminal. Only the crystal is usable;
        /// the Spire structure itself (1871, the hub node's entity) stays a plain solid prop.
        /// </summary>
        public static bool IsCrystal(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == 575;

        /// <summary>Can players use this Spire prop?</summary>
        public static bool Usable(ulong guid) => StationAt(guid) != 0 || IsMailbox(guid) || IsBarber(guid) || IsCrystal(guid)
            || IsResupplyChest(guid) || IsFounderChest(guid);

        /// <summary>
        /// The template's props where it puts them; the Spire itself gets index 1 (its guid is the respawn site id).
        /// Without a template, the two Spire models on the ground (home continent only).
        /// </summary>
        private static List<PlayerUpdateAddLightweightProp> BuildProps(Site site)
        {
            if (template == null || site.Corner is not { } corner)
                return new()
                {
                    Prop(site.Zone, 1, SpiresModel, site.Position, Quaternion.Identity, Vector3.One),
                    Prop(site.Zone, 2, BaseModel, site.Position, Quaternion.Identity, Vector3.One)
                };
            var list = new List<PlayerUpdateAddLightweightProp>();
            ulong next = 2;
            ulong spireGuid = GuidOn(site.Zone);
            foreach (Terrain.VoxelTemplate.Prop p in template.Props)
            {
                if (p.ModelId == 0)
                    continue;
                ulong index = p.ModelId == SpiresModel && list.All(x => x.Guid != spireGuid) ? 1 : next++;
                PlayerUpdateAddLightweightProp spawn = Prop(site.Zone, index, p.ModelId, Local(corner, p.Position), p.Rotation, p.Scale);
                propDefinitions[spawn.Guid] = p.PropId;
                if (Usable(spawn.Guid))   // named like a placed station (the hover prompt shows it)
                    spawn.NameId = GameTableManager.Instance.PropDefs.GetEntry(p.PropId)?.NameStringId ?? 0;
                list.Add(spawn);
                if (p.PropId == ResupplyChestProp)
                {
                    // The Founder's Chest beside it (see FounderChestProp).
                    uint model = Props.ModelOf(FounderChestProp);
                    Vector3 at = Local(corner, p.Position) + Vector3.Transform(FounderChestOffset, p.Rotation);
                    // Its own index (not the next in the template's sequence), so the template props' guids don't shift.
                    PlayerUpdateAddLightweightProp founder = Prop(site.Zone, FounderChestIndex, model != 0 ? model : FounderChestModel, at, p.Rotation, p.Scale);
                    propDefinitions[founder.Guid] = FounderChestProp;
                    founder.NameId = GameTableManager.Instance.PropDefs.GetEntry(FounderChestProp)?.NameStringId ?? 0;
                    list.Add(founder);
                }
            }
            log.Info($"Spire of landscape {site.Zone}: {list.Count} template props (prop ids {string.Join(", ", template.Props.Select(p => p.PropId).Distinct().OrderBy(v => v))}).");
            return list;
        }

        /// <summary>A static Spire prop with a fixed guid so every player sees the same entity.</summary>
        private static PlayerUpdateAddLightweightProp Prop(uint zone, ulong index, uint model, Vector3 at, Quaternion rotation, Vector3 scale) => new()
        {
            Guid = GuidFor(zone, index),
            TransientId = PlayerManager.AllocateTransientId(),
            ModelId = model,
            Position = new Vector4(at, 1f),
            Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale = new Vector4(scale, 1f)
        };

        /// <summary>
        /// Travel → Spire: the nearest portal spire, at any time (P4946, 26 Apr 2016): the Spire of the
        /// landscape the player is on, arriving on its platform beside the Portal Crystal. A landscape without one (no
        /// baked ground) sends them to the home continent's, re-zoning there.
        /// </summary>
        public static void TravelTo(WorldSession session)
        {
            if (ArrivalOn(session.ZoneId) is Vector3 here)
            {
                Teleport.To(session, here);
                log.Info($"{session.CharacterName} travels to the Spire of landscape {session.ZoneId} {here}.");
                return;
            }
            if (!session.OnHomeContinent)
            {
                Landscapes.TravelTo(session, Home);
                return;
            }
            log.Warn("The Spire has no ground position; travel cancelled.");
        }
    }
}
