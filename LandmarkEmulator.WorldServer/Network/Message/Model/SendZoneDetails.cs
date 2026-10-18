using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// SendZoneDetails 0x2E (S→C): the zone the client is in and its continent settings. 0.695 reader FUN_142c246b0
    /// (constructor FUN_142c65d40); handled by the zone dispatcher FUN_142d1ccf0 case 0x11, which stores every field as
    /// listed below [P]. Sent at zone entry and after every ClientBeginZoning (Game.Teleport); the values are the live
    /// 406 capture's (WorldSession.ZoneDetails) with each landscape's own swapped in (Game.Landscapes.ZoneDetails).
    /// Struct offsets are the client packet's.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.SendZoneDetails)]
    public class SendZoneDetails : IReadable, IWritable
    {
        /// <summary>
        /// +0x10 the zone (geometry) name, "OpenBetaGeometry_406": FUN_142d52d70 sets the client's zone name, which
        /// ClientBeginZoning compares with its own to tell an in-place re-zone.
        /// </summary>
        public string ZoneName { get; set; }
        /// <summary>
        /// +0x28 zone type, FUN_142d531c0 (game +0x31fa8; Lua "ZoneType"): 0 TileStatic, 1 TileSeamless, 2 RuntimeSeamless,
        /// 3 Mesh, 4 HeightfieldLod, 5 BlockTerrain (live 5; type 5 builds the voxel world, FUN_142cb3860). The
        /// constructor's 0x7FFFFFFF marks "partial" details: then only the world id / name are applied ("RECEIVED=Partial
        /// ZoneDetails").
        /// </summary>
        public uint ZoneType { get; set; }
        /// <summary>
        /// +0x2C sets all eleven flags of the client's EncounterDataCommon (game +0x32860, FUN_143bca710 writes
        /// +0x10..+0x1a): +0x1a is Lua Ui.IsInCombat (FUN_1435cc280 → FUN_142d05410) and, while set, holds the client's
        /// queued Lua UI calls (FUN_142d20c60) [P]. Live false.
        /// </summary>
        public bool EncounterFlags { get; set; }
        /// <summary>
        /// +0x30 the sky definition file, "Sky.xml": copied to game +0x32b88 and loaded by BaseClient::InitSkyDefinition
        /// (FUN_142ce93d0) when not empty [P].
        /// </summary>
        public string SkyDefinition { get; set; }
        /// <summary>
        /// +0x48 zone mode, game +0x32868 [P]: 3 = a starting zone (HUD:SetIsInStartingZone true, FUN_142d4f410; the
        /// client then loads only SmallUiAssets.txt and defers UiAssets.txt); any non-zero value holds the queued Lua UI
        /// calls (FUN_142d20c60) and FUN_142ca6c60. No client name for the field [I name]. Live 0.
        /// </summary>
        public uint ZoneMode { get; set; }
        /// <summary>
        /// +0x4C game +0x31fac, the key the client's own offline zone setup reads as "GeometryId" (FUN_142d12360). The
        /// client looks it up in its StaticZoneData table by row ID (FUN_142ccbe00: the zone extents in FUN_1435fdb70,
        /// the world map's fog of war HudWorldMap::UpdateFogOfWar FUN_1434cae60) [P], so it is the zone id (live 406),
        /// not the table's GEOMETRY_ID column (which differs on the Pioneer and event islands) [I strong: the table is
        /// keyed by ID, e.g. FUN_142cd4bc0 names friends' zones through it].
        /// </summary>
        public uint GeometryId { get; set; }
        /// <summary>
        /// +0x50 game +0x31fb0 (FUN_142d53250): only formatted ("%lld", FUN_143c01a90) into a string that is thrown away
        /// (FUN_143c01af0 returns at once); client stores at game +0x31fb0, never read [P]. Live 0xFFFFFFFFFFFFFFFF.
        /// </summary>
        public ulong Slot50 { get; set; }
        /// <summary>
        /// +0x58 the current zone id, game +0x378d0: the Landscapes (Travel) window and the friend / guild location lists
        /// compare their entries' zone with it (trace/0695/agent-travel.md; also the location list of FUN_142db9560) [P]. Live 406.
        /// </summary>
        public uint ZoneId { get; set; }
        /// <summary>+0x5C the current world id, game +0x378cc (m_worldId of SetWorldZoneInfo) [P]. Live 2.</summary>
        public uint WorldId { get; set; }
        /// <summary>
        /// +0x60 the world's name text (SetWorldZoneInfo worldNameId → GameData.WorldZoneLocation "World"; "Unnamed World"
        /// when the text is empty) [P]. Live 560912 "Liberation".
        /// </summary>
        public uint WorldNameId { get; set; }
        /// <summary>+0x68 the continent settings.</summary>
        public ContinentDefinition Continent { get; set; } = new();
        /// <summary>
        /// +0x200 game +0x31fb8, Lua Ui.IsInNewbieZone (FUN_1435d3520) [P]. In 0.589 the Travel widget disabled "An
        /// Underground Adventure" while it was true ("There are no underground adventures available in your current
        /// World"); the 0.695 TeleportWidget no longer calls it (re/out/agent_ui/uicalls.txt "GONE"), and nothing else
        /// reads it. Sent as StaticZoneData IS_NEWBIE_ZONE of the landscape (1 for the Pioneer lands 463-468) [I: the
        /// table column no client code reads, and this flag the client takes only from here]; live 406 sent false.
        /// Was the unnamed "Trailing" byte.
        /// </summary>
        public bool IsNewbieZone { get; set; }

        public void Read(GamePacketReader reader)
        {
            ZoneName = reader.ReadString();
            ZoneType = reader.ReadUInt();
            EncounterFlags = reader.ReadBool();
            SkyDefinition = reader.ReadString();
            ZoneMode = reader.ReadUInt();
            GeometryId = reader.ReadUInt();
            Slot50 = reader.ReadULong();
            ZoneId = reader.ReadUInt();
            WorldId = reader.ReadUInt();
            WorldNameId = reader.ReadUInt();
            Continent.Read(reader);
            IsNewbieZone = reader.BytesRemaining > 0 && reader.ReadBool();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ZoneName);
            writer.Write(ZoneType);
            writer.Write(EncounterFlags);
            writer.Write(SkyDefinition);
            writer.Write(ZoneMode);
            writer.Write(GeometryId);
            writer.Write(Slot50);
            writer.Write(ZoneId);
            writer.Write(WorldId);
            writer.Write(WorldNameId);
            Continent.Write(writer);
            writer.Write(IsNewbieZone);
        }
    }
}
