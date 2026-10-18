using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Storage;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Other Worlds: the landscapes (continents) players can travel to, all read from the client's own data:
    ///   Resources/StaticZoneData.txt   zone id, name text id, continent, origin (GEOMETRY_OFFSET), extents
    ///   CommonData/Voxel/materials/textures/Continent_{BiomeIds,Shape,HeightLayerIds}_&lt;id&gt;.bmp   must exist
    /// The islands are zones 391-416 (two biomes each, e.g. 406 Sleeping Ridge); the Pioneer lands 464-468 have one
    /// biome each (<see cref="PioneerZones"/>; the old 417-421 are not loaded). Test maps (NOTLIVE, Museum, Research,
    /// Seamless, AI stress) are left out.
    ///
    /// Travel window (TeleportBase 0xFC): sub 2 RequestLocations is answered with sub 3 Locations (client reader
    /// FUN_1430c4150, filled into data sources Teleport.Worlds / Teleport.Zones):
    ///   u32 n worlds, each {u32 world id, u32 name text, u32 capacity %, u32 m, m x zone {u32 zone id, u32 name text,
    ///   u32 biome type, u32 total claims, f32 capacity, u32 surface tier, u32 image id}}
    ///   u32 k sites (ServerClaimBasicData, FUN_143007a30): {u64, u64, u64, u32 world, u32, u32 zone, bool, string name,
    ///   3 x u64, 3 x u64, u32, u32}
    /// Choosing one sends sub 4 TeleportRequest {u32 world, u32 zone} (writer FUN_1430c8600).
    ///
    /// The event islands (TASKS 0.T2 / 3.19, trace/0695/event-islands-695.md): zones 460 Halloween Party (continent 436),
    /// 461 Winter Holiday Party (437) and 462 Isle of Judgement (438), 0.695 clients only (only that build ships their
    /// ContinentOceanMap / ContinentSurfaceCache). One of them at a time is listed on a second world, "Events"
    /// [W: forum 58006, 3 Oct 2016: reached from the teleport menu on a new world named Events; 58077, 21 Nov 2016:
    /// Halloween closed at that morning's restart, Winter Holiday open until early January], open for anyone to place a
    /// build
    /// site [W: 57987, 29 Sep 2016], chosen at start-up (WorldServer.json / EVENT_ISLAND: <see cref="ActiveEventZone"/>).
    /// No bitmaps of their own exist: SendZoneDetails names the template island's (<see cref="eventIslandDefs"/>).
    /// The sites left on an island when it closes can be expired as live's were (Landscapes.EventExpiry.cs, setting
    /// EventIslandExpireOnClose, default on = live).
    /// </summary>
    public static partial class Landscapes
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The one world of this server, by default: "Liberation" (text 560912, as in the 406 zone details; the name follows
        /// the server-list name when that is a live one, <see cref="WorldNameText"/>). Id 2: the window selects
        /// the world equal to the client's current world id (client +0x378cc), which the zone details set to 2 (their
        /// WorldId); ClientBeginZoning sends it at +0xa8.
        /// </summary>
        public const uint DefaultWorldId = 2, DefaultWorldNameText = 560912;
        /// <summary>This world's client world id (WorldServer.json ClientWorldId, Docker CLIENT_WORLD_ID; default 2).</summary>
        public static uint WorldId => worldId ??= Configured(c => c.ClientWorldId, DefaultWorldId);
        /// <summary>
        /// This world's name text in game (ClientWorldNameText, Docker CLIENT_WORLD_NAME_TEXT). Unset (0) = as live, where
        /// every world showed its own name in game (the server list's names are the same client texts, 560911-560926):
        /// the server-list name WorldNameId when it is one of those, else <see cref="DefaultWorldNameText"/> 560912
        /// "Liberation" (the live 406 capture's), e.g. for 62147 "Dev Server" or a kept stored name (0).
        /// </summary>
        public static uint WorldNameText => worldNameText ??= Configured(c => c.ClientWorldNameText,
            ConfigurationManager<WorldServerConfiguration>.Instance.Config?.WorldNameId is uint name && IsLiveWorldName(name)
                ? name : DefaultWorldNameText);
        private static uint? worldId, worldNameText;

        /// <summary>The live world names (client texts 560911 Courage ... 560926 Inspiration; WorldServerConfiguration.WorldNameId).</summary>
        public const uint FirstLiveWorldName = 560911, LastLiveWorldName = 560926;

        /// <summary>Is this text id one of the live worlds' names?</summary>
        public static bool IsLiveWorldName(uint text) => text >= FirstLiveWorldName && text <= LastLiveWorldName;

        private static uint Configured(Func<WorldServerConfiguration, uint> read, uint fallback)
        {
            WorldServerConfiguration config = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            uint value = config != null ? read(config) : 0;
            return value != 0 ? value : fallback;
        }

        /// <summary>
        /// Other worlds a player can travel to from the Landscapes window (cross-world travel, TASKS B.10): the worlds whose
        /// zones the same gateway routes (WorldsReachable from a MultiWorld gateway; none without one).
        /// </summary>
        public sealed record OtherWorldInfo(uint ServerWorld, uint ClientWorld, uint NameText);
        private static volatile IReadOnlyList<OtherWorldInfo> otherWorlds = Array.Empty<OtherWorldInfo>();

        /// <summary>The gateway's list of reachable worlds (gateway link thread); this world and clashing ids left out.</summary>
        public static void SetReachableWorlds(IEnumerable<(uint World, uint ClientWorld, uint NameText)> worlds, uint ownServerWorld)
        {
            var list = new List<OtherWorldInfo>();
            foreach ((uint world, uint clientWorld, uint nameText) in worlds)
            {
                if (world == ownServerWorld)
                    continue;
                if (clientWorld == 0 || clientWorld == WorldId || clientWorld == EventsWorldId || list.Any(o => o.ClientWorld == clientWorld))
                {
                    log.Warn($"World {world} uses client world id {clientWorld}, which clashes here: not listed (give each world "
                        + "its own CLIENT_WORLD_ID, never 3).");
                    continue;
                }
                list.Add(new OtherWorldInfo(world, clientWorld, nameText != 0 ? nameText : DefaultWorldNameText));
            }
            otherWorlds = list;
            log.Info(list.Count == 0 ? "No other worlds reachable." : $"Other worlds reachable: {string.Join(", ", list.Select(o => $"{o.ServerWorld} (client id {o.ClientWorld})"))}.");
        }

        /// <summary>The reachable world the client knows as <paramref name="clientWorld"/>, or null.</summary>
        public static OtherWorldInfo OtherWorld(uint clientWorld) => otherWorlds.FirstOrDefault(o => o.ClientWorld == clientWorld);

        /// <summary>Every other world reachable through the gateway (for travel to a friend there).</summary>
        public static IReadOnlyList<OtherWorldInfo> OtherWorlds => otherWorlds;
        /// <summary>
        /// The "Events" world of the Landscapes window. Name: text 567016 "Events" [I: the only client text with that
        /// name; live's world name id is OPEN-LEADS L109]. Id 3 is ours (live's not in the client); it only has to differ
        /// from <see cref="WorldId"/>: the client keeps it as its current world (+0x378cc) on the island.
        /// </summary>
        public const uint EventsWorldId = 3, EventsWorldNameText = 567016;
        public const byte LocationsSub = 3, TeleportRequestSub = 4;
        /// <summary>
        /// Image shown for a landscape's row in the Travel window (VoxelContinentLayers TELEPORT_IMAGE_ID of the islands;
        /// drawn through the zone row's ImageId, FUN_143174ca0). [P names] It is an Images.txt id: 10228 =
        /// back_depthSelector_tier1.dds, 10229 = back_depthSelector_tier2.dds (layer 496 is the only tier-2 layer). So
        /// 10228 is the tier-1 image, consistent with the surface tier 1 sent beside it; tier N = 10227 + N [I rule].
        /// trace/0695/agent-leads3-b.md L14.
        /// </summary>
        private const uint ImageId = 10228;

        public class Zone
        {
            public uint Id;
            public uint Continent;
            public uint NameText;
            public bool Pioneer;
            /// <summary>GEOMETRY_OFFSET: the zone origin in global metres (x10 = decimetres).</summary>
            public Vector3 OriginMetres;
            public Vector3 Extents;
            /// <summary>The world it is listed under: <see cref="WorldId"/>, or <see cref="EventsWorldId"/> for an event island.</summary>
            public uint World = WorldId;
            public bool EventIsland;
            /// <summary>The three continent bitmaps SendZoneDetails names (CommonData/Voxel/materials/textures).</summary>
            public string BiomeBitmap, ShapeBitmap, HeightLayerBitmap;
            /// <summary>
            /// StaticZoneData IS_NEWBIE_ZONE (1 for the Pioneer lands): sent as SendZoneDetails.IsNewbieZone (client Lua
            /// Ui.IsInNewbieZone).
            /// </summary>
            public bool NewbieZone;
        }

        /// <summary>
        /// The event islands: setting keyword, the landscape name shown in the window, and the continents whose bitmaps
        /// SendZoneDetails names (the islands ship none). Evidence (event-islands-695.md §2-3): continents 424-429 and 432
        /// share one Shape and one HeightLayerIds bitmap (the developers' "Museum" template island); the shipped surface
        /// cache of 436 equals 427 / 429's on 99.994 % of texels (their BiomeIds bitmap ee5ba3f32e, deciduous: the only
        /// inputs 427 and 429 share), 437 and 438 equal each other on 99.976 % and differ from every other continent by
        /// at least 6 % (low field) - the template island painted Tundra, which is exactly Continent_BiomeIds_437.bmp
        /// (biomes 51 / 54 / 80 / 81, VoxelWorldSettings 48 "Museum Tundra" of 437 / 438). [I] until a 0.695 client's
        /// bake of these names matches the shipped caches (OPEN-LEADS L13, TESTING-NEEDED).
        /// Names: 572667 "Halloween Party", 572669 "Winter Holiday Party", 572678 "IsleOfJudgement" (StaticZoneData's
        /// NAME_ID is the "... Zone" text next to each) [I].
        /// </summary>
        private static readonly Dictionary<uint, (string Key, uint NameText, uint BiomeOf, uint ShapeOf)> eventIslandDefs = new()
        {
            [460] = ("halloween", 572667, 427, 424),
            [461] = ("winter", 572669, 437, 424),
            [462] = ("judgement", 572678, 437, 424)
        };

        /// <summary>Every event island this install can show (0.695 data present), active or not.</summary>
        private static readonly Dictionary<uint, Zone> eventIslands = new();
        /// <summary>The event island on the Events world this run, or null (none in season / switched off).</summary>
        public static Zone EventIsland { get; private set; }

        /// <summary>
        /// Which event island the Events world holds, from the setting (WorldServer.json EventIsland, Docker EVENT_ISLAND):
        /// "halloween" / "winter" (or "holiday") / "judgement", a zone id 460-462, "off", or "auto" (default) = live's 2016
        /// calendar every year: Halloween 3 Oct - 20 Nov, Winter Holiday 21 Nov - 10 Jan [W: 58006 open 3 Oct; 58077
        /// switched 21 Nov, open until early January; the 10 Jan end matches RareDrops' winter season and is ours]. The
        /// Isle of Judgement
        /// was never announced as open: only by name. Read once at start-up: live switched islands at a restart.
        /// </summary>
        public static uint? ActiveEventZone(string setting, DateTime utcNow)
        {
            string s = (setting ?? "").Trim().ToLowerInvariant();
            if (uint.TryParse(s, out uint id))
                return eventIslandDefs.ContainsKey(id) ? id : null;
            switch (s)
            {
                case "off": case "none": return null;
                case "halloween": return 460;
                case "winter": case "holiday": return 461;
                case "judgement": case "judgment": return 462;
            }
            int day = utcNow.Month * 100 + utcNow.Day;
            if (day >= 1003 && day <= 1120)
                return 460;
            if (day >= 1121 || day <= 110)
                return 461;
            return null;
        }

        /// <summary>
        /// The Pioneer lands: zones 464-468 "Pioneer Island Jungle464" … "Deciduous468" (continents 440-444), whose own
        /// StaticZoneData names are 573314-573318 "Pioneer Jungle / Tundra / Desert / Old Growth / Deciduous" [P]. Their
        /// Shape bitmaps are byte-identical flat plateaus with plain, path and ocean biomes (P4031: the new Pioneer
        /// Landscapes' flat terrain), their underground has ~78,000 isolated cave pieces and no creatures (layers
        /// 489-494:
        /// cave density 50, no encounters; P4300: isolated caverns kept only for automatically placed build sites), and
        /// only HeightLayerIds_440.bmp exists for the set, so all five use it [I]. Before 2026-10-17 the server used zones
        /// 417-421 (internal names "Jungle_Zone" …, hilly islands with no Pioneer caverns) with overridden names; those are
        /// no longer loaded or listed (trace/0695/agent-leads2-d2.md L99; user decision 2026-10-17), and the sites still on
        /// them move to the replacement land at start-up (<see cref="OldPioneerToNew"/>, Pioneer.MoveOldSites).
        /// StaticZoneData IS_NEWBIE_ZONE is 1 for exactly these (and the abandoned 463) [P]; the client parses it into
        /// ZonesStaticDefinition +0x64 (FUN_143d7bdb0) but nothing in the client reads it (the zone table's only readers use
        /// the name and extents) [P], so it was a server rule: the new players' landscapes, as Pioneer is here [I]. The
        /// server also sends it as SendZoneDetails.IsNewbieZone, the client's only source of Lua Ui.IsInNewbieZone [P]
        /// (Zone.NewbieZone).
        /// Ground heights: TerrainHeights464-468.bin come from the client's surface cache (re/tools/csc_heights.py).
        /// </summary>
        public static readonly IReadOnlyDictionary<uint, uint> PioneerZones = new Dictionary<uint, uint>
        {
            [464] = 440, // Pioneer Jungle
            [465] = 441, // Pioneer Tundra
            [466] = 442, // Pioneer Desert
            [467] = 443, // Pioneer Old Growth
            [468] = 444  // Pioneer Deciduous
        };

        /// <summary>
        /// The Pioneer land that replaced each of the old 417-421 lands (same biome): where their sites move at start-up
        /// (Pioneer.MoveOldSites) and where a character saved on one starts (Pioneer.LeaveOldLand).
        /// </summary>
        public static readonly IReadOnlyDictionary<uint, uint> OldPioneerToNew = new Dictionary<uint, uint>
        {
            [417] = 464, [418] = 466, [419] = 465, [420] = 467, [421] = 468
        };

        private const string PioneerHeightLayers = "Continent_HeightLayerIds_440.bmp";

        private static readonly Dictionary<uint, Zone> zones = new();
        /// <summary>The landscapes of the main world (Pioneer lands and islands; never the event island).</summary>
        public static IReadOnlyDictionary<uint, Zone> Zones => zones;
        /// <summary>A landscape players can be on: the main world's, or the event island while it is open.</summary>
        public static Zone Get(uint id) => zones.TryGetValue(id, out Zone z) ? z : EventIsland?.Id == id ? EventIsland : null;
        /// <summary>Also the closed event islands (dev travel, zone details, bitmaps).</summary>
        private static Zone Any(uint id) => Get(id) ?? (eventIslands.TryGetValue(id, out Zone z) ? z : null);
        /// <summary>The world a landscape is listed under (the Landscapes window, zone details, site lists).</summary>
        public static uint WorldOf(uint zone) => Any(zone)?.World ?? WorldId;
        /// <summary>The name text of a world id this server uses.</summary>
        public static uint WorldNameTextOf(uint world) => world == EventsWorldId ? EventsWorldNameText
            : OtherWorld(world)?.NameText ?? WorldNameText;
        /// <summary>The BiomeIds bitmap the client generates a landscape with (TerrainMaterials reads the same one).</summary>
        public static string BiomeBitmapOf(uint zone) => Any(zone)?.BiomeBitmap ?? $"Continent_BiomeIds_{zone}.bmp";

        /// <summary>The client install the landscapes were read from (cave placements etc. are read from it later).</summary>
        public static string GameDirectory { get; private set; }

        /// <summary>StaticZoneData IS_NEWBIE_ZONE (column 14; missing = 0).</summary>
        private static bool NewbieColumn(string[] f) => f.Length > 14 && f[14].Trim() == "1";

        public static void Load(string gameDirectory)
        {
            GameDirectory = gameDirectory;
            string table = Path.Combine(gameDirectory ?? "", "Resources", "StaticZoneData.txt");
            string bitmaps = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "materials", "textures");
            if (!File.Exists(table))
            {
                log.Warn($"No {table}; only the home landscape is available.");
                return;
            }
            float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
            foreach (string line in File.ReadLines(table))
            {
                string[] f = line.Split('^');
                if (f.Length < 14 || !uint.TryParse(f[0], out uint id))
                    continue;
                uint continent = uint.Parse(f[5]);
                if (eventIslandDefs.TryGetValue(id, out var ev))
                {
                    // An event island: the template island's bitmaps, and only with this install's 0.695 surface cache
                    // (a 0.589 install has none for 436-438).
                    var island = new Zone
                    {
                        Id = id,
                        Continent = continent,
                        NameText = ev.NameText,
                        OriginMetres = new Vector3(F(f[8]), F(f[9]), F(f[10])),
                        Extents = new Vector3(F(f[11]), F(f[12]), F(f[13])),
                        World = EventsWorldId,
                        EventIsland = true,
                        NewbieZone = NewbieColumn(f),
                        BiomeBitmap = $"Continent_BiomeIds_{ev.BiomeOf}.bmp",
                        ShapeBitmap = $"Continent_Shape_{ev.ShapeOf}.bmp",
                        HeightLayerBitmap = $"Continent_HeightLayerIds_{ev.ShapeOf}.bmp"
                    };
                    string cache = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "surfaceCache", $"ContinentSurfaceCache{continent}.csc");
                    if (File.Exists(cache) && new[] { island.BiomeBitmap, island.ShapeBitmap, island.HeightLayerBitmap }
                        .All(b => File.Exists(Path.Combine(bitmaps, b))))
                        eventIslands[id] = island;
                    continue;
                }
                bool pioneer = PioneerZones.ContainsKey(id);
                if (!pioneer && (id < 391 || id > 416))
                    continue;
                string heightLayers = pioneer ? PioneerHeightLayers : $"Continent_HeightLayerIds_{continent}.bmp";
                if (f[4].StartsWith("NOTLIVE") || !File.Exists(Path.Combine(bitmaps, heightLayers)))
                    continue;
                zones[id] = new Zone
                {
                    Id = id,
                    Continent = continent,
                    NameText = uint.Parse(f[3]),
                    Pioneer = pioneer,
                    NewbieZone = NewbieColumn(f),
                    OriginMetres = new Vector3(F(f[8]), F(f[9]), F(f[10])),
                    Extents = new Vector3(F(f[11]), F(f[12]), F(f[13])),
                    BiomeBitmap = $"Continent_BiomeIds_{continent}.bmp",
                    ShapeBitmap = $"Continent_Shape_{continent}.bmp",
                    HeightLayerBitmap = heightLayers
                };
            }
            string setting = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.EventIsland;
            EventIsland = ActiveEventZone(setting, DateTime.UtcNow) is uint active ? eventIslands.GetValueOrDefault(active) : null;
            // The event island's ground materials come from the template bitmap it is generated with.
            Terrain.TerrainMaterials.BiomeBitmapOf = BiomeBitmapOf;
            log.Info($"{zones.Count} landscapes ({zones.Values.Count(z => z.Pioneer)} Pioneer); event islands available "
                + $"[{string.Join(", ", eventIslands.Keys)}], Events world: "
                + (EventIsland == null ? $"closed (setting '{setting ?? "auto"}')" : $"zone {EventIsland.Id} (continent {EventIsland.Continent})"));
            // Did the island change since the last run? Then the one that closed may expire its sites (Landscapes.EventExpiry.cs).
            StartEventExpiry();
            SelfCheck();
        }

        /// <summary>
        /// At login: a character saved on an event island that is not open this run (the Events world switched at a
        /// restart) starts on the home continent at its Spire instead; the zone details then name a landscape the server
        /// serves. Live had no such move (players who logged out there could not log back in onto it, forum 58006 #3, 9
        /// Oct 2016), so this part is ours.
        /// </summary>
        public static void LeaveClosedEventIsland(CharacterModel character)
        {
            if (!eventIslands.ContainsKey(character.ZoneId) || Get(character.ZoneId) != null)
                return;
            uint home = Terrain.TerrainStore.HomeContinent, from = character.ZoneId;
            Vector3 at = Spire.ArrivalOn(home) ?? new Vector3(0, 1200, 0);
            character.ZoneId = home;
            (character.PositionX, character.PositionY, character.PositionZ) = (at.X, at.Y, at.Z);
            ulong id = character.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = id };
                c.Character.Attach(model);
                model.ZoneId = home;
                model.PositionX = at.X;
                model.PositionY = at.Y;
                model.PositionZ = at.Z;
                c.MarkModified(model, nameof(model.ZoneId), nameof(model.PositionX), nameof(model.PositionY), nameof(model.PositionZ));
            });
            log.Info($"{character.Name} was on the closed event island {from}: starts at the home Spire.");
        }

        /// <summary>
        /// SendZoneDetails for a continent: the live 406 capture with the landscape's own values swapped in (field
        /// meanings in SendZoneDetails / ContinentDefinition): ZoneName "OpenBetaGeometry_&lt;id&gt;", GeometryId and ZoneId
        /// = the zone id, WorldId / WorldNameId = its world and that world's name text (2 / 560912 in the capture),
        /// IsNewbieZone = StaticZoneData IS_NEWBIE_ZONE, and in the continent block ContinentId, ContinentNameId (the zone's
        /// name text), ContinentName "OpenBetaContinent_&lt;id&gt;", offset and extents (m) and the three bitmap names. For an
        /// event island the continent (436-438, which selects the client's ContinentOceanMap / SurfaceCache and
        /// VoxelContinentWorld row) and the bitmaps (the template island's) differ. GeometryId is the StaticZoneData row id
        /// (the client looks the table up by ID), not the table's GEOMETRY_ID column (456-464 on the islands and Pioneer
        /// lands); no field of the packet carries that column (OPEN-LEADS L13).
        /// </summary>
        public static byte[] ZoneDetails(uint zoneId)
        {
            Zone zone = Any(zoneId);
            bool defaultWorld = WorldId == DefaultWorldId && WorldNameText == DefaultWorldNameText;
            if (zone == null || zone.Id == 406)
            {
                if (defaultWorld)
                    return WorldSession.ZoneDetails;
                // The capture as it is, with this world's id and name (cross-world travel: each world its own id).
                var own = new SendZoneDetails();
                own.Read(new GamePacketReader(WorldSession.ZoneDetails));
                own.WorldId = WorldId;
                own.WorldNameId = WorldNameText;
                return Encode(own);
            }
            var details = new SendZoneDetails();
            details.Read(new GamePacketReader(WorldSession.ZoneDetails));
            uint c = zone.Continent;
            details.ZoneName = $"OpenBetaGeometry_{zone.Id}";
            details.GeometryId = zone.Id;
            details.ZoneId = zone.Id;
            details.WorldId = zone.World;
            details.WorldNameId = WorldNameTextOf(zone.World);
            details.IsNewbieZone = zone.NewbieZone;
            Network.Message.Model.Shared.ContinentDefinition continent = details.Continent;
            continent.ContinentId = c;
            continent.ContinentNameId = zone.NameText;
            continent.ContinentName = $"OpenBetaContinent_{c}";
            continent.ContinentOffset = new double[] { zone.OriginMetres.X, zone.OriginMetres.Y, zone.OriginMetres.Z };
            continent.ContinentExtents = new double[] { zone.Extents.X, zone.Extents.Y, zone.Extents.Z };
            continent.BiomeMap = zone.BiomeBitmap ?? $"Continent_BiomeIds_{c}.bmp";
            continent.ShapeMap = zone.ShapeBitmap ?? $"Continent_Shape_{c}.bmp";
            continent.HeightLayerMap = zone.HeightLayerBitmap ?? $"Continent_HeightLayerIds_{c}.bmp";
            return Encode(details);
        }

        private static byte[] Encode(SendZoneDetails details)
        {
            var output = new List<byte>();
            details.Write(new GamePacketWriter(output));
            return output.ToArray();
        }

        /// <summary>Re-encoding the captured 406 details must give the same bytes, or ZoneDetails would be wrong.</summary>
        private static void SelfCheck()
        {
            var details = new SendZoneDetails();
            details.Read(new GamePacketReader(WorldSession.ZoneDetails));
            byte[] again = Encode(details);
            if (!again.AsSpan().SequenceEqual(WorldSession.ZoneDetails))
                log.Error($"SendZoneDetails round trip differs ({again.Length} vs {WorldSession.ZoneDetails.Length} bytes); "
                    + "travel to other landscapes is unsafe.");
        }

        /// <summary>
        /// Arrival height (local metres) on landscapes without baked ground heights: above all terrain; the player
        /// falls to the ground (0.589 has no fall damage) while the terrain streams in. Do NOT snap them down with a
        /// TeleportToLocation: a long in-place jump makes the client re-centre its terrain inside a frame, which hangs
        /// its renderer (QuerySimpleVisibility, FUN_144009a40) until the watchdog kills it.
        /// </summary>
        private const float ArrivalHeight = 1800f;

        /// <summary>
        /// TeleportRequest (sub 4 {u32 world, u32 zone}) from the Landscapes window: re-zones the player into the
        /// landscape at its Spire (TASKS 6.49; P4965, 26 Apr 2016: travel to a Pioneer landscape arrives at the spire
        /// again). The Spire is built before the re-zone, so its platform is in the terrain the
        /// client streams. A landscape without one (no baked ground) arrives at its centre as before.
        /// </summary>
        /// <param name="anyEventIsland">Dev travel (dev command "landscape"): also the event islands that are closed this
        /// run, e.g. to bake their ground heights.</param>
        public static void TravelTo(WorldSession session, uint zoneId, bool anyEventIsland = false)
        {
            Zone zone = anyEventIsland ? Any(zoneId) : Get(zoneId);
            if (zone == null)
            {
                Chat.System(session, "That landscape isn't available.");
                return;
            }
            // The players of both landscapes are told (PlayerManager.ChangedZone) when the re-zone is actually sent
            // (announceZoneChange): it can wait for a placement to clear or for a re-zone in flight (review 2026-10-18 #3).
            if (Spire.ArrivalOn(zone.Id) is Vector3 spire)
                Teleport.Rezone(session, spire, zone.Id, announceZoneChange: true);
            else if (zone.Id == Terrain.TerrainStore.HomeContinent)
                Teleport.Rezone(session, new Vector3(0, 1200, 0), zone.Id, announceZoneChange: true);
            else
                Teleport.Rezone(session, ArrivalPoint(zone.Id), zone.Id, announceZoneChange: true);
            if (!zone.Pioneer)
                Achievements.Progress(session, Achievements.VisitNonPioneer);
            log.Info($"{session.CharacterName} travels to landscape {zone.Id} (continent {zone.Continent}).");
        }

        /// <summary>Dry land at least this high (local metres; sea level is about 1000).</summary>
        private const float DryLand = 1005f;

        /// <summary>
        /// Where a traveller arrives: with the landscape's baked heights, the nearest gentle dry ground to its centre
        /// (searched outwards in rings, 1 m above it and clear of trees); without them, high above the centre.
        /// </summary>
        public static Vector3 ArrivalPoint(uint zoneId)
        {
            if (!Terrain.TerrainHeights.Has(zoneId))
                return new Vector3(0f, ArrivalHeight, 0f);
            for (float radius = 0f; radius <= 3000f; radius += 40f)
            {
                int steps = Math.Max(1, (int)(radius * 2 * MathF.PI / 40f));
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * 2 * MathF.PI / steps;
                    float x = radius * MathF.Cos(angle), z = radius * MathF.Sin(angle);
                    if (Terrain.TerrainHeights.HeightAt(x, z, zoneId) is float y && y >= DryLand
                        && Terrain.TerrainHeights.Slope(x, z, 2f, zoneId) < 0.4f)
                        return Gathering.ClearOfTrees(new Vector3(x, y + 1f, z), zoneId);
                }
            }
            return new Vector3(0f, ArrivalHeight, 0f);
        }

        /// <summary>
        /// Locations (sub 3): the main world and its landscapes, and while an event island is open the Events world with
        /// that one island (its surface caches ship with the 0.695 client only, event-islands-695.md §1); no build sites
        /// listed yet.
        /// </summary>
        public static void SendLocations(WorldSession session)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(LocationsSub);
            Zone island = EventIsland;
            IReadOnlyList<OtherWorldInfo> others = otherWorlds;
            w.Write((island == null ? 1u : 2u) + (uint)others.Count);
            List<Zone> main = zones.Values.OrderBy(z => z.Pioneer).ThenBy(z => z.Id).ToList();
            WriteWorld(w, WorldId, main);
            // Other worlds on the same gateway, each with the same landscapes (every world ran the same set [I]); choosing
            // one of their landscapes hands the player over (CrossWorld.Travel).
            foreach (OtherWorldInfo other in others)
                WriteWorld(w, other.ClientWorld, main);
            if (island != null)
                WriteWorld(w, EventsWorldId, new List<Zone> { island });
            w.Write(0u);                                    // build sites
            session.EnqueueMessage(ZoneMessageOpcode.TeleportBase, output.ToArray());
        }

        /// <summary>One world location of the Locations list (FUN_1430c2ad0) with its landscapes.</summary>
        private static void WriteWorld(GamePacketWriter w, uint world, List<Zone> landscapes)
        {
            w.Write(world);
            w.Write(WorldNameTextOf(world));
            w.Write(20u);                                   // capacity % (Low)
            w.Write((uint)landscapes.Count);
            foreach (Zone zone in landscapes)
            {
                w.Write(zone.Id);
                w.Write(zone.NameText);
                w.Write(0u);                                // biome type (0: shown under every filter)
                w.Write((uint)Claims.CountOn(zone.Id));
                w.Write(BitConverter.SingleToUInt32Bits(0.2f)); // capacity fraction
                w.Write(1u);                                // surface tier
                w.Write(ImageId);
            }
        }
    }
}
