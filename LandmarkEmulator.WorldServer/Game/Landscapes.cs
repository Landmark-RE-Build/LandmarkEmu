using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
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
    /// The islands are zones 391-416 (two biomes each, e.g. 406 Sleeping Ridge); the Pioneer lands 417-421 have one
    /// biome each. Test maps (NOTLIVE, Museum, Research, Seamless, AI stress) are left out.
    ///
    /// Travel window (TeleportBase 0xFC): sub 2 RequestLocations is answered with sub 3 Locations (client reader
    /// FUN_1430c4150, filled into data sources Teleport.Worlds / Teleport.Zones):
    ///   u32 n worlds, each {u32 world id, u32 name text, u32 capacity %, u32 m, m x zone {u32 zone id, u32 name text,
    ///   u32 biome type, u32 total claims, f32 capacity, u32 surface tier, u32 image id}}
    ///   u32 k sites (ServerClaimBasicData, FUN_143007a30): {u64, u64, u64, u32 world, u32, u32 zone, bool, string name,
    ///   3 x u64, 3 x u64, u32, u32}
    /// Choosing one sends sub 4 TeleportRequest {u32 world, u32 zone} (writer FUN_1430c8600).
    /// </summary>
    public static class Landscapes
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The one world of this server: "Liberation" (text 560912, as in the 406 zone details). Id 2: the window selects
        /// the world equal to the client's current world id (client +0x378cc), which the zone details set to 2 (their
        /// Unknown8); ClientBeginZoning sends it at +0xa8.
        /// </summary>
        public const uint WorldId = 2, WorldNameText = 560912;
        public const byte LocationsSub = 3, TeleportRequestSub = 4;
        /// <summary>Image shown for a landscape (VoxelContinentLayers TELEPORT_IMAGE_ID of the islands).</summary>
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
        }

        /// <summary>Display names of the Pioneer lands (their StaticZoneData names are internal, e.g. "Jungle_Zone").</summary>
        private static readonly Dictionary<uint, uint> pioneerNames = new()
        {
            [417] = 573314, // Pioneer Jungle
            [418] = 573316, // Pioneer Desert
            [419] = 573315, // Pioneer Tundra
            [420] = 573317, // Pioneer Old Growth
            [421] = 573318  // Pioneer Deciduous
        };

        private static readonly Dictionary<uint, Zone> zones = new();
        public static IReadOnlyDictionary<uint, Zone> Zones => zones;
        public static Zone Get(uint id) => zones.TryGetValue(id, out Zone z) ? z : null;

        public static void Load(string gameDirectory)
        {
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
                if (f.Length < 14 || !uint.TryParse(f[0], out uint id) || id < 391 || id > 421)
                    continue;
                uint continent = uint.Parse(f[5]);
                if (f[4].StartsWith("NOTLIVE") || !File.Exists(Path.Combine(bitmaps, $"Continent_HeightLayerIds_{continent}.bmp")))
                    continue;
                bool pioneer = pioneerNames.ContainsKey(id);
                zones[id] = new Zone
                {
                    Id = id,
                    Continent = continent,
                    NameText = pioneer ? pioneerNames[id] : uint.Parse(f[3]),
                    Pioneer = pioneer,
                    OriginMetres = new Vector3(F(f[8]), F(f[9]), F(f[10])),
                    Extents = new Vector3(F(f[11]), F(f[12]), F(f[13]))
                };
            }
            log.Info($"{zones.Count} landscapes ({zones.Values.Count(z => z.Pioneer)} Pioneer).");
            SelfCheck();
        }

        /// <summary>
        /// SendZoneDetails for a continent: the live 406 capture with the continent's id, names, bitmaps, origin and
        /// extents swapped in. Fields (see SendZoneDetails / Sky): geometry name "OpenBetaGeometry_&lt;id&gt;",
        /// Unknown5/Unknown7 = id, Sky.Unknown1 = continent, Sky.Unknown3 = name text, Sky.Unknown4 =
        /// "OpenBetaContinent_&lt;id&gt;", Sky.Unknown7 = origin (m), Sky.Unknown8 = extents (m), the three bitmap names.
        /// </summary>
        public static byte[] ZoneDetails(uint zoneId)
        {
            Zone zone = Get(zoneId);
            if (zone == null || zone.Id == 406)
                return WorldSession.ZoneDetails;
            var details = new SendZoneDetails();
            details.Read(new GamePacketReader(WorldSession.ZoneDetails));
            uint c = zone.Continent;
            details.Unknown0 = $"OpenBetaGeometry_{zone.Id}";
            details.Unknown5 = zone.Id;
            details.Unknown7 = zone.Id;
            Network.Message.Model.Shared.Sky sky = details.SkyData;
            sky.Unknown1 = c;
            sky.Unknown3 = zone.NameText;
            sky.Unknown4 = $"OpenBetaContinent_{c}";
            sky.Unknown7 = new double[] { zone.OriginMetres.X, zone.OriginMetres.Y, zone.OriginMetres.Z };
            sky.Unknown8 = new double[] { zone.Extents.X, zone.Extents.Y, zone.Extents.Z };
            sky.Unknown11 = $"Continent_BiomeIds_{c}.bmp";
            sky.Unknown13 = $"Continent_Shape_{c}.bmp";
            sky.Unknown15 = $"Continent_HeightLayerIds_{c}.bmp";
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
        /// landscape at its centre. The home continent arrives at the Spire.
        /// </summary>
        public static void TravelTo(WorldSession session, uint zoneId)
        {
            Zone zone = Get(zoneId);
            if (zone == null)
            {
                Chat.System(session, "That landscape isn't available.");
                return;
            }
            uint oldZone = session.ZoneId;
            if (zone.Id == Terrain.TerrainStore.HomeContinent)
            {
                Vector3 at = Spire.Arrival ?? new Vector3(0, 1200, 0);
                Teleport.Rezone(session, at, zone.Id);
            }
            else
                Teleport.Rezone(session, ArrivalPoint(zone.Id), zone.Id);
            if (oldZone != zone.Id)
                PlayerManager.ChangedZone(session, oldZone);
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

        /// <summary>Locations (sub 3): the world and its landscapes; no build sites listed yet.</summary>
        public static void SendLocations(WorldSession session)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(LocationsSub);
            w.Write(1u);
            w.Write(WorldId);
            w.Write(WorldNameText);
            w.Write(20u);                                   // capacity % (Low)
            w.Write((uint)zones.Count);
            foreach (Zone zone in zones.Values.OrderBy(z => z.Pioneer).ThenBy(z => z.Id))
            {
                w.Write(zone.Id);
                w.Write(zone.NameText);
                w.Write(0u);                                // biome type (0: shown under every filter)
                w.Write((uint)Claims.CountOn(zone.Id));
                w.Write(BitConverter.SingleToUInt32Bits(0.2f)); // capacity fraction
                w.Write(1u);                                // surface tier
                w.Write(ImageId);
            }
            w.Write(0u);                                    // build sites
            session.EnqueueMessage(ZoneMessageOpcode.TeleportBase, output.ToArray());
        }
    }
}
