using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    /// <summary>
    /// The continent (landscape terrain) settings the client builds its voxel world from: the client's
    /// <c>ContinentDefinition</c> (a DataManagement::BaseDefinition; constructor FUN_143cdf1e0, 0x1a0 bytes), carried at
    /// the end of SendZoneDetails (packet +0x68) and ClientBeginZoning (packet +0xb8). Wire reader FUN_142c1ddf0 (0.695).
    /// Was called "Sky" here with 36 unknown fields; every field is now named [P] by the client's own ContinentDefinition
    /// loaders, which name each struct offset: the INI loader FUN_143cdf940 ("ContinentId", "VoxelSize", …), the data-table
    /// loader FUN_143ce07c0 ("VOXEL_SIZE", "CONTINENT_OFFSET_X", …) and the debug printer FUN_143cdf590
    /// ("ContinentId=%d ContinentVersion=%d …"); the three sub-blocks are named by their printers FUN_143df2620
    /// ("%sBlendCurveQuarterX") and FUN_143df2ff0 ("%sNoiseStep" …). The struct offset of each field is in its comment.
    /// <para>
    /// Where it goes [P]: SendZoneDetails (zone dispatcher FUN_142d1ccf0 case 0x11) → FUN_142cb3860 → FUN_142cb3510 →
    /// FUN_142d5c450: when the ContinentId differs from the running voxel world's (FUN_143cdf500 compares +0x08 only) the
    /// whole definition is copied into the voxel engine (FUN_143f9a0e0 → engine +0x5218, FUN_143f8bcf0) and the engine
    /// derives its cell grid from VoxelSize and the continent bounds (FUN_143ffa3d0); the client then logs "VoxelFarmCore::
    /// LoadSettings ContinentId …". The bounds are precomputed by FUN_143cdf820: (offset ± extents × 0.5) × 10.
    /// VoxelSize is also kept in the game at +0x32010 (read as a double by the build / placement code: part_0087, 0119,
    /// 0121) and ContinentNameId is the zone name of GameData.WorldZoneLocation (SetWorldZoneInfo FUN_142d52ac0).
    /// </para>
    /// <para>
    /// Values: the live 406 capture (WorldSession.ZoneDetails). Game.Landscapes.ZoneDetails swaps in the per-landscape
    /// id, name, offset, extents and bitmaps; the rest are 406's for every landscape, as there is no client table of
    /// continent settings (VoxelContinent*.txt hold layers / worlds only) and no capture of another landscape [I].
    /// </para>
    /// </summary>
    public class ContinentDefinition : IReadable, IWritable
    {
        /// <summary>+0x08 ContinentId (406). The voxel world is rebuilt only when it changes (FUN_143cdf500).</summary>
        public uint ContinentId { get; set; }
        /// <summary>+0x0C ContinentVersion (live 5).</summary>
        public uint ContinentVersion { get; set; }
        /// <summary>
        /// +0x10 ContinentNameId: a client text id (571468 "Sleeping Ridge" in the capture; the landscape's StaticZoneData
        /// NAME_ID). The zone name of GameData.WorldZoneLocation: dispatcher → SetWorldZoneInfo FUN_142d52ac0 param 4.
        /// </summary>
        public uint ContinentNameId { get; set; }
        /// <summary>+0x18 ContinentName ("OpenBetaContinent_406").</summary>
        public string ContinentName { get; set; }
        /// <summary>+0x30 ContinentDescription (live "OpenBeta_Do_NOT_ChangeThisText_16").</summary>
        public string ContinentDescription { get; set; }
        /// <summary>
        /// +0x50 VoxelSize, a double (live 40.0; the INI default is 50). Copied to game +0x32010 and into the voxel
        /// engine, where FUN_143ffa3d0 sizes the cell grid with it. (Was an unnamed u64 here: same 8 bytes.)
        /// </summary>
        public double VoxelSize { get; set; }
        /// <summary>+0x58 ContinentOffsetX/Y/Z in global metres (406: 2558400, 2000, 2556450).</summary>
        public double[] ContinentOffset { get; set; } = new double[3];
        /// <summary>+0x70 ContinentExtentsX/Y/Z in metres (406: 8000, 4000, 8000).</summary>
        public double[] ContinentExtents { get; set; } = new double[3];
        /// <summary>+0x88 UseContinentMap (live true; when false the loaders also clear UseHeightLayerMap).</summary>
        public bool UseContinentMap { get; set; }
        /// <summary>+0x8A FourMaterialBlending (live false).</summary>
        public bool FourMaterialBlending { get; set; }
        /// <summary>+0x90 BiomeMap: the BiomeIds bitmap (CommonData/Voxel/materials/textures).</summary>
        public string BiomeMap { get; set; }
        /// <summary>+0xE0 BiomeMapMetersPerTexel, f32 (live 6.4).</summary>
        public float BiomeMapMetersPerTexel { get; set; }
        /// <summary>+0xA8 ShapeMap: the Shape bitmap.</summary>
        public string ShapeMap { get; set; }
        /// <summary>+0xE4 ShapeMapMetersPerTexel, f32 (live 6.4).</summary>
        public float ShapeMapMetersPerTexel { get; set; }
        /// <summary>+0xC0 HeightLayerMap: the HeightLayerIds bitmap.</summary>
        public string HeightLayerMap { get; set; }
        /// <summary>+0xE8 HeightLayerMapMetersPerTexel, f32 (live 400).</summary>
        public float HeightLayerMapMetersPerTexel { get; set; }
        /// <summary>+0xD8 ContinentMaxClaims (live 10000; INI default 500).</summary>
        public uint ContinentMaxClaims { get; set; }
        /// <summary>+0x178 the "Biome" blend curve (data table BLEND_CURVE_*): BiomeBlendCurveQuarterX, f32 (live 0.25).</summary>
        public float BiomeBlendCurveQuarterX { get; set; }
        /// <summary>+0x17C BiomeBlendCurveQuarterY, f32 (live 0.25).</summary>
        public float BiomeBlendCurveQuarterY { get; set; }
        /// <summary>+0xEC ContinentMaterialBlendFactor, f32 (table BIOME_MATERIAL_BLEND_FACTOR; live 0.25).</summary>
        public float ContinentMaterialBlendFactor { get; set; }
        /// <summary>+0xF0 ContinentMaterialNoiseFactor, f32 (table BIOME_MATERIAL_NOISE_FACTOR; live 0.75).</summary>
        public float ContinentMaterialNoiseFactor { get; set; }
        /// <summary>
        /// +0x180 the "ContinentMaterial" noise block (table BIOME_MATERIAL_*; wire reader FUN_142c1e3c0, printer
        /// FUN_143df2ff0): NoiseStep f32 (live 0.5), NoiseLacunarity f32 (2.0), NoiseOctaves u32 (3), NoiseTile f32 (0.01).
        /// </summary>
        public float ContinentMaterialNoiseStep { get; set; }
        public float ContinentMaterialNoiseLacunarity { get; set; }
        public uint ContinentMaterialNoiseOctaves { get; set; }
        public float ContinentMaterialNoiseTile { get; set; }
        /// <summary>+0xF4 LayerStartDepth, f32 (live 1250).</summary>
        public float LayerStartDepth { get; set; }
        /// <summary>+0xF8 LayerEndDepth, f32 (live 3100).</summary>
        public float LayerEndDepth { get; set; }
        /// <summary>+0xFC OverlandCaveDensityPct, f32 (live 30).</summary>
        public float OverlandCaveDensityPct { get; set; }
        /// <summary>+0x150 OverlandTierIndex (live 1, the INI default).</summary>
        public uint OverlandTierIndex { get; set; }
        /// <summary>+0x100 OceanHeight, a double (live 2999.0). (Was an unnamed u64 here: same 8 bytes.)</summary>
        public double OceanHeight { get; set; }
        /// <summary>+0x108 OceanMaterialId: a VoxelMaterials.txt id (live 474, NAME 562353 "Ocean Water").</summary>
        public uint OceanMaterialId { get; set; }
        /// <summary>+0x48 EntryRequirementExpressionId (table ENTRY_REQ_EXPR_ID; live 0 = none).</summary>
        public uint EntryRequirementExpressionId { get; set; }
        /// <summary>+0xDC DestructionDisabled (table DISABLE_DESTRUCTION; live false).</summary>
        public bool DestructionDisabled { get; set; }
        /// <summary>+0x154 TeleportImageId (table TELEPORT_LIST_IMAGE_ID; live 14005).</summary>
        public uint TeleportImageId { get; set; }
        /// <summary>+0x89 UseHeightLayerMap (live true).</summary>
        public bool UseHeightLayerMap { get; set; }
        /// <summary>
        /// +0x190 the "HeightLayer" blend curve (table HEIGHT_LAYER_*; wire reader FUN_142c3d690):
        /// HeightLayerBlendCurveQuarterX f32 (live 0.25), HeightLayerBlendCurveQuarterY f32 (live 0.1).
        /// </summary>
        public float HeightLayerBlendCurveQuarterX { get; set; }
        public float HeightLayerBlendCurveQuarterY { get; set; }
        /// <summary>+0x158 ProceduralLayerType (table PROCEDURAL_LAYER_TYPE_ID; live 1).</summary>
        public uint ProceduralLayerType { get; set; }
        /// <summary>+0x160 DistanceField (live empty).</summary>
        public string DistanceField { get; set; }

        public void Read(GamePacketReader reader)
        {
            ContinentId = reader.ReadUInt();
            ContinentVersion = reader.ReadUInt();
            ContinentNameId = reader.ReadUInt();
            ContinentName = reader.ReadString();
            ContinentDescription = reader.ReadString();
            VoxelSize = reader.ReadDouble();
            for (int i = 0; i < 3; i++)
                ContinentOffset[i] = reader.ReadDouble();
            for (int i = 0; i < 3; i++)
                ContinentExtents[i] = reader.ReadDouble();
            UseContinentMap = reader.ReadBool();
            FourMaterialBlending = reader.ReadBool();
            BiomeMap = reader.ReadString();
            BiomeMapMetersPerTexel = reader.ReadSingle();
            ShapeMap = reader.ReadString();
            ShapeMapMetersPerTexel = reader.ReadSingle();
            HeightLayerMap = reader.ReadString();
            HeightLayerMapMetersPerTexel = reader.ReadSingle();
            ContinentMaxClaims = reader.ReadUInt();
            BiomeBlendCurveQuarterX = reader.ReadSingle();
            BiomeBlendCurveQuarterY = reader.ReadSingle();
            ContinentMaterialBlendFactor = reader.ReadSingle();
            ContinentMaterialNoiseFactor = reader.ReadSingle();
            ContinentMaterialNoiseStep = reader.ReadSingle();
            ContinentMaterialNoiseLacunarity = reader.ReadSingle();
            ContinentMaterialNoiseOctaves = reader.ReadUInt();
            ContinentMaterialNoiseTile = reader.ReadSingle();
            LayerStartDepth = reader.ReadSingle();
            LayerEndDepth = reader.ReadSingle();
            OverlandCaveDensityPct = reader.ReadSingle();
            OverlandTierIndex = reader.ReadUInt();
            OceanHeight = reader.ReadDouble();
            OceanMaterialId = reader.ReadUInt();
            EntryRequirementExpressionId = reader.ReadUInt();
            DestructionDisabled = reader.ReadBool();
            TeleportImageId = reader.ReadUInt();
            UseHeightLayerMap = reader.ReadBool();
            HeightLayerBlendCurveQuarterX = reader.ReadSingle();
            HeightLayerBlendCurveQuarterY = reader.ReadSingle();
            ProceduralLayerType = reader.ReadUInt();
            DistanceField = reader.ReadString();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ContinentId);
            writer.Write(ContinentVersion);
            writer.Write(ContinentNameId);
            writer.Write(ContinentName);
            writer.Write(ContinentDescription);
            writer.Write(VoxelSize);
            for (int i = 0; i < 3; i++)
                writer.Write(ContinentOffset[i]);
            for (int i = 0; i < 3; i++)
                writer.Write(ContinentExtents[i]);
            writer.Write(UseContinentMap);
            writer.Write(FourMaterialBlending);
            writer.Write(BiomeMap);
            writer.Write(BiomeMapMetersPerTexel);
            writer.Write(ShapeMap);
            writer.Write(ShapeMapMetersPerTexel);
            writer.Write(HeightLayerMap);
            writer.Write(HeightLayerMapMetersPerTexel);
            writer.Write(ContinentMaxClaims);
            writer.Write(BiomeBlendCurveQuarterX);
            writer.Write(BiomeBlendCurveQuarterY);
            writer.Write(ContinentMaterialBlendFactor);
            writer.Write(ContinentMaterialNoiseFactor);
            writer.Write(ContinentMaterialNoiseStep);
            writer.Write(ContinentMaterialNoiseLacunarity);
            writer.Write(ContinentMaterialNoiseOctaves);
            writer.Write(ContinentMaterialNoiseTile);
            writer.Write(LayerStartDepth);
            writer.Write(LayerEndDepth);
            writer.Write(OverlandCaveDensityPct);
            writer.Write(OverlandTierIndex);
            writer.Write(OceanHeight);
            writer.Write(OceanMaterialId);
            writer.Write(EntryRequirementExpressionId);
            writer.Write(DestructionDisabled);
            writer.Write(TeleportImageId);
            writer.Write(UseHeightLayerMap);
            writer.Write(HeightLayerBlendCurveQuarterX);
            writer.Write(HeightLayerBlendCurveQuarterY);
            writer.Write(ProceduralLayerType);
            writer.Write(DistanceField);
        }
    }
}
