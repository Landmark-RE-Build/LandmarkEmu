using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The live zone-entry captures decode, field by field, to the values the named models document (0.695 readers
    /// FUN_142c246b0 / FUN_142c1ddf0 / FUN_142c236c0), and re-encode to the same bytes.
    /// </summary>
    public class ZoneSettingsTests
    {
        [Fact]
        public void ZoneDetailsCapture_NamedFields()
        {
            (SendZoneDetails d, uint left) = Wire.Read<SendZoneDetails>(WorldSession.ZoneDetails);
            Assert.Equal(0u, left);
            Assert.Equal("OpenBetaGeometry_406", d.ZoneName);
            Assert.Equal(5u, d.ZoneType);               // BlockTerrain
            Assert.False(d.EncounterFlags);
            Assert.Equal("Sky.xml", d.SkyDefinition);
            Assert.Equal(0u, d.ZoneMode);
            Assert.Equal(406u, d.GeometryId);
            Assert.Equal(ulong.MaxValue, d.Slot50);
            Assert.Equal(406u, d.ZoneId);
            Assert.Equal(2u, d.WorldId);
            Assert.Equal(560912u, d.WorldNameId);
            Assert.False(d.IsNewbieZone);

            var c = d.Continent;
            Assert.Equal(406u, c.ContinentId);
            Assert.Equal(5u, c.ContinentVersion);
            Assert.Equal(571468u, c.ContinentNameId);
            Assert.Equal("OpenBetaContinent_406", c.ContinentName);
            Assert.Equal(40.0, c.VoxelSize);
            Assert.Equal(new[] { 2558400.0, 2000.0, 2556450.0 }, c.ContinentOffset);
            Assert.Equal(new[] { 8000.0, 4000.0, 8000.0 }, c.ContinentExtents);
            Assert.True(c.UseContinentMap);
            Assert.True(c.UseHeightLayerMap);
            Assert.Equal("Continent_BiomeIds_406.bmp", c.BiomeMap);
            Assert.Equal(6.4f, c.BiomeMapMetersPerTexel);
            Assert.Equal(400f, c.HeightLayerMapMetersPerTexel);
            Assert.Equal(10000u, c.ContinentMaxClaims);
            Assert.Equal(3u, c.ContinentMaterialNoiseOctaves);
            Assert.Equal(1250f, c.LayerStartDepth);
            Assert.Equal(3100f, c.LayerEndDepth);
            Assert.Equal(30f, c.OverlandCaveDensityPct);
            Assert.Equal(2999.0, c.OceanHeight);
            Assert.Equal(474u, c.OceanMaterialId);
            Assert.Equal(14005u, c.TeleportImageId);
            Assert.Equal(1u, c.ProceduralLayerType);
            Assert.Equal("", c.DistanceField);

            Assert.Equal(WorldSession.ZoneDetails, Wire.Write(d));
        }

        [Fact]
        public void ClientGameSettingsCapture_NamedFields()
        {
            (ClientGameSettings s, uint left) = Wire.Read<ClientGameSettings>(ClientGameSettings.LiveBody);
            Assert.Equal(0u, left);
            Assert.Equal(37, ClientGameSettings.LiveBody.Length);
            Assert.Equal(10u, s.DefaultInteractionRange);
            Assert.False(s.MovementCheck);
            Assert.Equal(1f, s.GameTimeScalar);
            Assert.Equal(0u, s.SharedEquipmentRecord);
            Assert.Equal(1u, s.SharedExperienceRecord);
            Assert.Equal(12f, s.ImpactSpeedThreshold);
            Assert.Equal(110f, s.ImpactScale);
            Assert.Equal(ClientGameSettings.LiveBody, Wire.Write(s));
        }

        /// <summary>
        /// ClientBeginZoning: str, u32, 4 × f32, 4 × f32, str, u8 movement version (1, as the login MovementVersion),
        /// u32 zone mode, f32, u32 geometry id, u32 world, u32 zone, u32 world name, bool, bool, continent.
        /// </summary>
        [Fact]
        public void BeginZoning_CarriesMovementVersion1()
        {
            var zoning = new ClientBeginZoning
            {
                ZoneName = "Z", ZoneType = 5, SkyXml = "", GeometryId = 406, WorldId = 2, ZoneId = 406, WorldNameId = 560912
            };
            zoning.Continent.ContinentName = zoning.Continent.ContinentDescription = zoning.Continent.BiomeMap
                = zoning.Continent.ShapeMap = zoning.Continent.HeightLayerMap = zoning.Continent.DistanceField = "";
            byte[] body = Wire.Write(zoning);
            byte[] head = new Layout().U32(1).U8((byte)'Z').U32(5)
                .F32(0).F32(0).F32(0).F32(0).F32(0).F32(0).F32(0).F32(1)
                .U32(0).U8(1).U32(0).F32(0).U32(406).U32(2).U32(406).U32(560912).Bool(false).Bool(false).ToArray();
            Assert.Equal(head, body[..head.Length]);
        }
    }
}
