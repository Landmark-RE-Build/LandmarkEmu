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
    /// Travel window takes players to (cast 185) and the landscape's hub / graveyard respawn. Built the way the live game
    /// built it: the client's own template CommonData/Voxel/templates/base_portal_spire.otd
    /// (<see cref="Terrain.VoxelTemplate"/>, the only Spire template in either client: the others in that folder are
    /// contest ruins and underground hubs) is stamped into the terrain near the landscape's centre — its voxel platform —
    /// and its props (the Spire pieces, Models.txt 596 Theme_Widgets_Teleporters_PortalSpires01, the Replicator, Repair
    /// Station, Mailbox, The Crimson Parlor, the Portal Crystal) are placed where the template puts them. The stamp is
    /// done once per landscape and saved with the terrain edits (marker TerrainEdits/spire_base.txt for the home
    /// continent, spire_base_&lt;zone&gt;.txt for the others, holding "voxel x y z", the template's corner as a global
    /// voxel); the props are static entities sent on zone-in.
    ///
    /// Placement (OPEN-LEADS L100, trace/0695/agent-leads3-b.md §L100 / S2; fixed 2026-10-17). Both Spire templates hold
    /// no solid voxel [P]: they are a 124 × 68 × 124 m air box over a vertex-only floor (template layer y = 0, its
    /// surface ~0.2 m above the corner), i.e. a clearing that carves everything above the floor and keeps what lies
    /// under it (trace/0695/ruin-stamping.md §1). The Spire model (596) reaches 20.94 m under its pivot [P: .dme header
    /// AABB y −20.94 … +43.74], and its collision mesh (CommonData/Collision/Theme_Widgets_Teleporters_PortalSpires01.cdt,
    /// "CDTA" v1: u32 vertex count, f32 ×3 vertices, u32 triangle count, u16 ×3 indices, then a cooked copy) stands on
    /// that same plane: its legs' and the four diagonal ramps' feet are at pivot −20.94 (ramps reach it 60.5 m out
    /// along the diagonals) [P: measured from the .cdt, 2026-10-17; docs/terrain.md "Collision meshes"]. The template's pivot is 21.24 m over its corner, so the model's
    /// base is on the template floor: the template floor is meant to be the ground. Until 2026-10-17 the server put the
    /// pivot at the ground and rounded the corner to the 16 m cell grid, carving a ~21 m (±8 m) pit with the deck at
    /// about the old ground (in-game, landscape 395: floor 1088.2, ground 1106.9 at the centre, 1125.2 just outside).
    /// Now the floor is put on the ground (<see cref="ReferenceGround"/>) on the 0.4 m voxel grid (VoxelTemplate.StampAt),
    /// 0.8 m higher for P5647 (<see cref="Raise"/>), on the nearest ground level within 8 m across the box (<see cref="SpotFor"/>;
    /// the home continent's stays at (0, 0), a 110 m slope on the 0.695 bake: its floor is on the median ground too); worlds stamped
    /// the old way are reverted and stamped again on start-up / first use (<see cref="RevertOldStamp"/>). Where the ground
    /// lies below the floor, the server fills it up to the floor with an embankment around (<see cref="Fill"/>; ours,
    /// user decision 2026-10-17 "keep centre, fill ground"), so the Spire's feet meet the ground on any slope.
    ///
    /// Sources: wiki "Spire" / "Servers and Landscapes" (trace/systems/world-travel.md: a Spire "near each landscape's
    /// centre"); P5647 (25 May 2016) central spires sit a little higher above the ground; P4946 (26 Apr)
    /// the travel menu can always go to the nearest portal spire (<see cref="TravelTo"/>: the current
    /// landscape's); P4965 (26 Apr) "When traveling to a pioneer landscape you should now correctly arrive at the spire"
    /// (Landscapes.TravelTo arrives at <see cref="ArrivalOn"/>). The client data has no per-landscape Spire position
    /// (no table or file names one; all 29 landscapes share one GEOMETRY_OFFSET in StaticZoneData, so local (0, 0) is
    /// every landscape's centre): the spot is the server's, INFERRED as the centre — the home continent's at (0, 0) as
    /// before, the others at the nearest dry, level ground to the centre that no build site occupies (a site already
    /// placed at a centre before Spires existed keeps its ground). The live "raised slightly higher" offset is not known
    /// exactly (<see cref="Raise"/>, [I]).
    /// </summary>
    public static class Spire
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Where the home continent's Spire stands (local metres, horizontal; the ground height is looked up).</summary>
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
            /// <summary>The ground added under its floor (global voxels), null when none (<see cref="Fill"/>).</summary>
            public FillBox? Fill;
            /// <summary>The Spire's foot (local metres).</summary>
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
            FillBox? savedFill = null;
            bool fillDone = false;
            if (marker != null && File.Exists(marker))
            {
                string[] f = File.ReadAllText(marker).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 4 && f[0] == MarkerVoxel)
                {
                    saved = (long.Parse(f[1]), long.Parse(f[2]), long.Parse(f[3]));
                    // "fill none" or "fill x0 y0 z0 x1 y1 z1": the fill is done. Without the word (the first voxel
                    // markers, effe77a) the fill is still to do.
                    if (f.Length >= 5 && f[4] == MarkerFill)
                    {
                        fillDone = true;
                        if (f.Length == 11)
                            savedFill = new FillBox(long.Parse(f[5]), long.Parse(f[6]), long.Parse(f[7]), long.Parse(f[8]), long.Parse(f[9]), long.Parse(f[10]));
                    }
                }
                else if (f.Length == 3 && uint.TryParse(f[0], out uint cx) && uint.TryParse(f[1], out uint cy) && uint.TryParse(f[2], out uint cz))
                {
                    // Stamped before 2026-10-17: pivot at the ground, corner on the 16 m cell grid (a ~21 m pit). Its cells
                    // go back to the generated ground, the marker is kept aside, and the Spire is placed again below.
                    RevertOldStamp(zone, (cx, cy, cz));
                    File.Move(marker, marker + ".cell-reverted", true);
                }
            }
            (long X, long Y, long Z) corner;
            FillBox? fill = savedFill;
            var broadcast = new List<ulong>();
            if (saved is { } known)
            {
                corner = known;
                // Marked permanent again after a restart (the heal would otherwise regrow them), from the boxes alone.
                Terrain.EnvironmentServer.Instance.MarkPermanent(Terrain.VoxelTemplate.CellsCovering(corner.X, corner.Y, corner.Z, template.Size), zone);
                if (savedFill is FillBox done)
                    Terrain.EnvironmentServer.Instance.MarkPermanent(CellsOf(done), zone);
            }
            else
            {
                if (SpotFor(zone) is not Vector3 spot)
                {
                    log.Warn($"Landscape {zone}: no ground found for its Spire (no baked heights or no free dry land near the centre).");
                    return null;
                }
                corner = CornerFor(spot);
                (List<ulong> cells, List<ulong> changed) = Terrain.EnvironmentServer.Instance.StampTemplateAt(template, corner.X, corner.Y, corner.Z, zone);
                Terrain.EnvironmentServer.Instance.Flush();
                Terrain.EnvironmentServer.Instance.MarkPermanent(cells, zone);
                Terrain.EnvironmentServer.Instance.CompactNow(true);
                broadcast.AddRange(changed);
                Vector3 floor = Local(corner, Vector3.Zero);
                log.Info($"Stamped the Spire base of landscape {zone} at voxel {corner}: floor {floor.Y + FloorSurface:F1} m on ground {spot.Y:F1} m "
                    + $"(+{Raise} m), pivot {floor.Y + templateSpire.Position.Y:F1} m, centre ({spot.X:F0}, {spot.Z:F0}); {Terrain.VoxelTemplate.LastStampReport}.");
            }
            if (!fillDone)
            {
                // After the template: its vertex-only floor layer is made solid where the fill reaches it.
                (fill, List<ulong> filled) = Fill(zone, corner);
                broadcast.AddRange(filled);
                if (marker != null)
                {
                    Directory.CreateDirectory(editsFolder);
                    File.WriteAllText(marker, $"{MarkerVoxel} {corner.X} {corner.Y} {corner.Z} {MarkerFill} "
                        + (fill is FillBox b ? $"{b.MinX} {b.MinY} {b.MinZ} {b.MaxX} {b.MaxY} {b.MaxZ}" : "none"));
                }
            }
            var site = new Site { Zone = zone, Corner = corner, Fill = fill, Position = Local(corner, templateSpire.Position) };
            if (broadcast.Count > 0)
            {
                // Players already on that landscape (a character who logged in there) get the new cells, and veins
                // spawned there before the Spire existed go elsewhere (the home continent's: Load).
                Terrain.ZoneTerrain.Broadcast(broadcast.Distinct().ToList(), zone);
                if (zone != Home)
                {
                    var (min, max) = PlatformBox(site);
                    Terrain.ZoneTerrain.ClearVeinsInSite(zone, min, max);
                }
            }
            return site;
        }

        /// <summary>The marker's word before the fill's box ("fill x0 y0 z0 x1 y1 z1", global voxels, or "fill none").</summary>
        private const string MarkerFill = "fill";

        /// <summary>A box of global voxels, both ends included (the Spire's fill).</summary>
        private readonly record struct FillBox(long MinX, long MinY, long MinZ, long MaxX, long MaxY, long MaxZ);

        /// <summary>Every level-2 cell a fill box touches.</summary>
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
        /// Half-width of the fill's flat top around the pivot, metres: the Spire model's footprint (AABB ±47.19 m; the
        /// ramps' feet at ~±43 m on the diagonals) and a little more, so every foot stands on it [P footprint, I margin].
        /// </summary>
        private const float FillFlatHalf = 48f;

        /// <summary>
        /// The fill's slope outside its flat top, metres down per metre out (1.5, ~56°), and how far out it may reach,
        /// metres (ours). Gentler grades never meet the ground on the home continent's hillside, which falls about as
        /// steeply below the Spire (measured on the 0.695 bake: 1:1 still left a 21.6 m drop 80 m out; 1:1.5 meets the
        /// ground within 55 m everywhere).
        /// </summary>
        private const float FillGrade = 1.5f, FillReach = 60f;

        /// <summary>
        /// Ground added under the Spire where the generated ground lies below the template floor. OURS (user decision
        /// 2026-10-17, "keep centre, fill ground"): live's home terrain and how live seated its Spires are not known. The
        /// template only carves, so on a slope its floor, and the Spire's legs and ramp feet on it, hung over the
        /// downhill ground. This adds solid voxels from the baked ground (TerrainHeights; one voxel into it, so no seam)
        /// up to the floor within <see cref="FillFlatHalf"/> of the pivot, then sloping down at <see cref="FillGrade"/>
        /// (corners rounded) until it meets the ground, at most <see cref="FillReach"/> further out.
        /// - Material: each column takes the local biome's ground material for its top's slope
        ///   (TerrainMaterials.GroundMaterialAt: the biome's flat material on the top, its slope material on the
        ///   embankment), so it matches the generated ground around it.
        /// - Surface: on the flat top it is the template floor's own surface (voxel <c>corner.Y</c>, centred vertex, 0.2 m
        ///   up), which the fill makes solid where it reaches; on the embankment the top voxel's vertex is put at the exact
        ///   height (its y offset), so the slope is smooth, not 0.4 m steps.
        /// - Written once, on the 0.4 m grid, into the layer the template uses (layer 0, through IEnvironmentServer.Write,
        ///   so a remote Environment Server gets it too), marked permanent; its box goes in the marker (restarts mark its
        ///   cells permanent again; a later re-placement reverts it with RevertBox).
        /// Nothing where the ground is at or above the floor. Returns the box written (null: nothing) and its cells.
        /// </summary>
        private static (FillBox? Box, List<ulong> Cells) Fill(uint zone, (long X, long Y, long Z) corner)
        {
            var env = Terrain.EnvironmentServer.Instance;
            int size = Terrain.BlockData.Size;
            double voxel = Terrain.TerrainEdits.VoxelSize;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            Vector3 pivot = Local(corner, templateSpire.Position);
            long top = corner.Y;                                   // the floor's voxel (its surface at the voxel's middle)
            double surface = (top + 0.5) * voxel;                  // global dm
            float reach = FillFlatHalf + FillReach;
            long x0 = (long)Math.Floor(((pivot.X - reach) * 10 + origin.X) / voxel), x1 = (long)Math.Ceiling(((pivot.X + reach) * 10 + origin.X) / voxel);
            long z0 = (long)Math.Floor(((pivot.Z - reach) * 10 + origin.Z) / voxel), z1 = (long)Math.Ceiling(((pivot.Z + reach) * 10 + origin.Z) / voxel);
            int w = (int)(x1 - x0 + 1), d = (int)(z1 - z0 + 1);
            // Per column: lowest and highest voxel (none when high < low), the top voxel's y offset byte, the material.
            var low = new long[w * d];
            var high = new long[w * d];
            var vertex = new byte[w * d];
            var material = new ushort[w * d];
            float upSlope = 1f / MathF.Sqrt(1f + FillGrade * FillGrade);
            long minY = long.MaxValue, maxY = long.MinValue, minX = long.MaxValue, maxX = long.MinValue, minZ = long.MaxValue, maxZ = long.MinValue;
            long columns = 0;
            for (int k = 0; k < d; k++)
                for (int i = 0; i < w; i++)
                {
                    int c = k * w + i;
                    high[c] = long.MinValue;
                    float lx = (float)(((x0 + i + 0.5) * voxel - origin.X) / 10), lz = (float)(((z0 + k + 0.5) * voxel - origin.Z) / 10);
                    float ox = MathF.Max(MathF.Abs(lx - pivot.X) - FillFlatHalf, 0f), oz = MathF.Max(MathF.Abs(lz - pivot.Z) - FillFlatHalf, 0f);
                    float outside = MathF.Sqrt(ox * ox + oz * oz);
                    if (outside > FillReach || Terrain.TerrainHeights.HeightAt(lx, lz, zone) is not float ground)
                        continue;
                    double want = surface - outside * FillGrade * 10;    // the fill's surface here, global dm
                    double groundDm = ground * 10 + origin.Y;
                    if (want <= groundDm + voxel / 2)
                        continue;
                    // Solid samples up to the voxel whose cube holds the surface, its vertex at the surface (VoxelTemplate's
                    // model: sample at grid point i, vertex in the cube i..i + 1; offset byte = floor((u + 1) · 85 + 0.5)).
                    long highest = outside <= 0f ? top : (long)Math.Floor(want / voxel);
                    double u = outside <= 0f ? 0.5 : want / voxel - highest;
                    low[c] = (long)Math.Floor(groundDm / voxel) - 1;
                    high[c] = highest;
                    vertex[c] = (byte)Math.Floor((Math.Clamp(u, -1.0, 2.0) + 1) * 85 + 0.5);
                    material[c] = Terrain.TerrainMaterials.GroundMaterialAt(new Vector3(lx, ground, lz), outside <= 0f ? 1f : upSlope, zone);
                    columns++;
                    minY = Math.Min(minY, low[c]);
                    maxY = Math.Max(maxY, highest);
                    minX = Math.Min(minX, x0 + i);
                    maxX = Math.Max(maxX, x0 + i);
                    minZ = Math.Min(minZ, z0 + k);
                    maxZ = Math.Max(maxZ, z0 + k);
                }
            var written = new List<ulong>();
            if (columns == 0)
            {
                log.Info($"Spire of landscape {zone}: no fill (the ground is nowhere below the floor).");
                return (null, written);
            }
            var box = new FillBox(minX, minY, minZ, maxX, maxY, maxZ);
            long voxels = 0;
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
                                {
                                    byte oy = gy == high[c] ? vertex[c] : Terrain.BlockData.CentreOffset;
                                    values.Add(new Terrain.TerrainEdits.VoxelValue(Terrain.TerrainEdits.VoxelIndex(x, (int)(gy - cy * size), z), 3,
                                        material[c], Terrain.BlockData.CentreOffset, oy, Terrain.BlockData.CentreOffset));
                                }
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
            log.Info($"Spire of landscape {zone}: filled the ground under its floor, {columns} columns, {voxels} voxels "
                + $"({voxels * voxel * voxel * voxel / 1000:F0} m³), {written.Count} cells, box {box}.");
            return (box, written);
        }

        /// <summary>The marker's first word since the voxel-aligned placement (2026-10-17): "voxel x y z" (global voxel).</summary>
        private const string MarkerVoxel = "voxel";

        /// <summary>
        /// Height of the template floor's surface over its corner, metres: layer y = 0 is vertex-only voxels with centred
        /// vertices (0x22; 96,721 = 311² of them), i.e. a surface half a voxel (0.2 m) up [P: agent-leads3-b.md §L100].
        /// </summary>
        private const float FloorSurface = 0.2f;

        /// <summary>
        /// How far the template floor is put over the ground, metres. P5647 (25 May 2016): central spires sit a little
        /// higher above the ground (grass overgrew them) [P text]. The amount is not in any source [I]: 0.8 m =
        /// two voxels, the extra base clearance the pre-11 May template (portalspirebase.otd, Hub at 22.0 m, its props
        /// 0.8 m higher) had over the post-7 Jun one we stamp (Hub at 21.24 m); the template itself went 0.8 m down, so
        /// the raise must have been in live's placement (agent-leads3-b.md §L100). Small enough that the ramps' feet
        /// (pivot −20.94) still meet the carved floor with a step of under a metre.
        /// </summary>
        private const float Raise = 0.8f;

        /// <summary>
        /// The template corner (global voxel) for a Spire whose pivot stands over <paramref name="spot"/> (local metres; its
        /// y the ground reference): x / z = the spot minus the Hub's offset in the template, y = the floor's surface on the
        /// ground plus <see cref="Raise"/>. All on the 0.4 m voxel grid (the vertical error is under 0.2 m, not ±8 m).
        /// </summary>
        private static (long X, long Y, long Z) CornerFor(Vector3 spot)
        {
            double voxel = Terrain.TerrainEdits.VoxelSize;
            (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(new Vector3(spot.X, spot.Y + Raise - FloorSurface, spot.Z));
            return ((long)Math.Round((gx - templateSpire.Position.X * 10) / voxel), (long)Math.Round(gy / voxel),
                (long)Math.Round((gz - templateSpire.Position.Z * 10) / voxel));
        }

        /// <summary>
        /// Drops a pre-2026-10-17 Spire stamp (corner on the 16 m cell grid) from the terrain: every override in the
        /// template's cells goes, so the client shows its generated ground there again (Outposts' re-stamp does the same
        /// with RevertBox). The template has no solids, so the pit cannot be filled back by stamping again; the box is
        /// inside the Spire's no-edit zone (<see cref="MayEdit"/>), so it holds nothing but the stamp. Players on the
        /// landscape get the reverted cells.
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

        /// <summary>Dry land at least this high (local metres; sea level is about 1000), as Landscapes.ArrivalPoint.</summary>
        private const float DryLand = 1005f;
        /// <summary>
        /// Largest ground height spread over the whole 124 m box (9 × 9 samples) for a spot to be taken at once, metres
        /// (ours). Every metre of spread is a wall (ground over the floor, carved by the air box) or a gap (ground under
        /// it: the vertex-only floor keeps what is underneath) of up to that height. The baked landscapes (gameData695)
        /// have such ground for 28 of 37 within 3 km; the others take their flattest spot (8.1-11.5 m; 395: 10.7 m, 320 m out).
        /// </summary>
        private const float MaxSpread = 8f;
        /// <summary>Ring spacing and reach of the spot search, metres (ours).</summary>
        private const float SearchStep = 20f, SearchReach = 3000f;
        /// <summary>Clear ground kept between a new Spire's platform and any build site, decimetres (ours: 20 m).</summary>
        private const double SiteMargin = 200;

        /// <summary>
        /// The ground a Spire's floor is put on, around a pivot x / z (local metres), and how uneven the box is. Samples a
        /// 9 × 9 grid over the template's 124 m box (15.5 m apart); null when any is missing or under water.
        /// Reference = the median of the 7 × 7 samples within ±46.5 m: the Spire model's own footprint (AABB ±47.19 m),
        /// where its collision meets its base plane (leg feet, and the four ramps reaching pivot −20.94 at ~±43 m on the
        /// diagonals) [P: .cdt]. Not the centre alone (one sample, under the open deck, where nothing stands on the
        /// ground) nor the box rim (outside the model): the median of the footprint is the height that the most of
        /// the Spire's feet meet, so on a slope half of them stand on carved ground and half on ground they rest on,
        /// and a bump or hollow under one sample does not move the whole Spire [I, the best fit; live's placement is
        /// not in any source].
        /// </summary>
        private static bool Survey(uint zone, float x, float z, out float spread, out float reference)
        {
            spread = reference = 0f;
            if (Terrain.TerrainHeights.HeightAt(x, z, zone) is not float centre || centre < DryLand)
                return false;
            float half = template.Size.X * (float)Terrain.TerrainEdits.VoxelSize / 20f;   // 62 m
            float step = half / 4f;
            float low = float.MaxValue, high = float.MinValue;
            Span<float> inner = stackalloc float[49];
            int n = 0;
            for (int j = -4; j <= 4; j++)
                for (int i = -4; i <= 4; i++)
                {
                    if (Terrain.TerrainHeights.HeightAt(x + i * step, z + j * step, zone) is not float h || h < DryLand)
                        return false;
                    low = MathF.Min(low, h);
                    high = MathF.Max(high, h);
                    if (Math.Abs(i) <= 3 && Math.Abs(j) <= 3)
                        inner[n++] = h;
                }
            inner.Sort();
            spread = high - low;
            reference = inner[n / 2];
            return true;
        }

        /// <summary>
        /// Where a landscape's Spire pivot stands (local x / z; y = the ground its floor goes on, <see cref="Survey"/>):
        /// the home continent's at (0, 0) as it always has; elsewhere the nearest point to the centre (rings 20 m apart,
        /// up to 3 km) whose whole box is level within <see cref="MaxSpread"/>, dry, and with no build site within 20 m
        /// (Claims.BoxesIn); failing that, the flattest such point found. INFERRED spot (no client data names one). Until
        /// 2026-10-17 only the box's four corners were compared with its centre, which passed slopes of 20 m and more
        /// (landscape 395: 22 m across the box).
        /// </summary>
        private static Vector3? SpotFor(uint zone)
        {
            if (zone == Home)
                return Survey(Home, X, Z, out _, out float home) ? new Vector3(X, home, Z) : Terrain.TerrainHeights.OnGround(X, Z);
            if (!Terrain.TerrainHeights.Available(zone))
                return null;
            float half = template.Size.X * (float)Terrain.TerrainEdits.VoxelSize / 20f;   // half the footprint, metres
            Vector3? flattest = null;
            float flattestSpread = float.MaxValue;
            for (float radius = 0f; radius <= SearchReach; radius += SearchStep)
            {
                int steps = Math.Max(1, (int)(radius * 2 * MathF.PI / SearchStep));
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * 2 * MathF.PI / steps;
                    float x = radius * MathF.Cos(angle), z = radius * MathF.Sin(angle);
                    if (!Survey(zone, x, z, out float spread, out float ground) || (spread > MaxSpread && spread >= flattestSpread))
                        continue;
                    // No build site within SiteMargin of the platform (any height: a site's box may not take the Spire).
                    var global = Terrain.TerrainEdits.ToGlobal(new Vector3(x, ground, z));
                    double reach = half * 10 + SiteMargin;
                    if (Claims.BoxesIn(zone, (global.X - reach, double.MinValue, global.Z - reach),
                            (global.X + reach, double.MaxValue, global.Z + reach)).Count > 0)
                        continue;
                    if (spread <= MaxSpread)
                        return new Vector3(x, ground, z);
                    flattest = new Vector3(x, ground, z);
                    flattestSpread = spread;
                }
            }
            if (flattest is Vector3 best)
                log.Warn($"Landscape {zone}: no Spire spot level within {MaxSpread} m within {SearchReach} m of the centre; "
                    + $"the flattest ({flattestSpread:F1} m) at ({best.X:F0}, {best.Z:F0}).");
            return flattest;
        }

        /// <summary>
        /// Is a point (local metres) on a landscape's Spire platform? Trees and plants are not grown there (the world
        /// generator knows nothing of the stamped template). Only Spires already built count (see <see cref="Peek"/>).
        /// </summary>
        public static bool Covers(uint zone, float x, float z)
        {
            if (template == null || Peek(zone) is not { Corner: not null } site)
                return false;
            // The platform box's footprint (template and fill, 2 m around), in local metres.
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
        /// The platform's box in global dm: the template's footprint plus 2 m around (as <see cref="Covers"/>), from 20 m
        /// under it to 20 m over it (the caverns below keep their ore), grown to take the ground filled under it
        /// (<see cref="Fill"/>, 2 m around and down to its bottom): no trees inside the embankment, no ore, no digging
        /// into it (its cells are permanent, so a dig there would never heal).
        /// </summary>
        private static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max) PlatformBox(Site site)
        {
            double voxel = Terrain.TerrainEdits.VoxelSize;
            var c = site.Corner.Value;
            (double X, double Y, double Z) min = (c.X * voxel - 20, c.Y * voxel - 200, c.Z * voxel - 20);
            (double X, double Y, double Z) max = ((c.X + template.Size.X) * voxel + 20, (c.Y + template.Size.Y) * voxel + 200, (c.Z + template.Size.Z) * voxel + 20);
            if (site.Fill is FillBox f)
            {
                min = (Math.Min(min.X, f.MinX * voxel - 20), Math.Min(min.Y, f.MinY * voxel), Math.Min(min.Z, f.MinZ * voxel - 20));
                max = (Math.Max(max.X, (f.MaxX + 1) * voxel + 20), max.Y, Math.Max(max.Z, (f.MaxZ + 1) * voxel + 20));
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
        public static void Enter(WorldSession session)
        {
            if (Ensure(session.ZoneId) is not Site site)
                return;
            List<PlayerUpdateAddLightweightProp> props = PropsOf(site);
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
        public static void ReassertCollision(WorldSession session)
        {
            uint zone = session.ZoneId;
            if (Peek(zone) is not Site site || site.Props == null)
                return;
            foreach (int ms in CollisionRetryMs)
                Teleport.After(session, ms, () =>
                {
                    if (session.ZoneId != zone || session.RezoneTarget != null
                        || Vector3.Distance(session.Position, site.Position) > CollisionRetryRange)
                        return;
                    foreach (PlayerUpdateAddLightweightProp prop in site.Props)
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
