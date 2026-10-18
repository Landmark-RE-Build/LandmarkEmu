using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientBeginZoning 0x23 (S→C): starts loading a zone (Game.Teleport.RezoneNow). 0.695 reader FUN_142c23310; handled
    /// by the zone dispatcher FUN_142d1ccf0 case 8, which stores every field as listed below and then raises
    /// GameEvents:OnPlayerBeginZoning (FUN_142d60340) [P]. Struct offsets are the client packet's. Several fields share
    /// their client slot with SendZoneDetails (which the server sends right after, so the two must agree).
    /// The names before 2026-10-18 were partly wrong (WorldId / WorldNameId / ZoneId / ZoneNameId were four slots off);
    /// the wire is unchanged.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientBeginZoning)]
    public class ClientBeginZoning : IReadable, IWritable
    {
        /// <summary>
        /// The client's movement version byte (+0x98 here): what ClientUpdate MovementVersion (0x29/0x26) sets at login.
        /// </summary>
        public const byte LiveMovementVersion = 1;

        /// <summary>+0x10 zone name (as SendZoneDetails.ZoneName; same name and world = an in-place re-zone).</summary>
        public string ZoneName { get; set; }
        /// <summary>+0x50 zone type (as SendZoneDetails.ZoneType), FUN_142d531c0.</summary>
        public uint ZoneType { get; set; }
        /// <summary>+0x60 the player's arrival position (local metres).</summary>
        public float[] Position { get; set; } = new float[4];
        /// <summary>+0x70 the player actor's orientation, a real quaternion.</summary>
        public float[] Rotation { get; set; } = new float[4] { 0f, 0f, 0f, 1f };
        /// <summary>
        /// +0x80 sky file name: never read [P] — case 8 resets the client's sky definition (game +0x32b88) to the
        /// constant "sky.xml" (DAT_1446b3290) and the real one comes with SendZoneDetails.SkyDefinition.
        /// </summary>
        public string SkyXml { get; set; }
        /// <summary>
        /// +0x98 the client's movement version, game +0x32560 (FUN_142d60340 param 7) [P]: the same byte ClientUpdate
        /// MovementVersion sets (ClientUpdate handler FUN_142cd9980 case 0x25) and TeleportToLocation +0x40 sets (FUN_142d59f30); the
        /// client's movement writer (FUN_142ca4ce0) copies it into the version byte of every movement body it sends, and
        /// other clients drop non-full updates whose version differs from the one they hold (FUN_142ce6340). The server
        /// keeps every player at 1 (login MovementVersion, AddLightweightPc +0x12d), so it sends 1 here too; it sent 0
        /// until 2026-10-18, which put the player on version 0 after every re-zone until its next full update.
        /// </summary>
        public byte MovementVersion { get; set; } = LiveMovementVersion;
        /// <summary>+0x9C zone mode, game +0x32868 (as SendZoneDetails.ZoneMode; 3 = starting zone). Live 0.</summary>
        public uint ZoneMode { get; set; }
        /// <summary>
        /// +0xA0 an f32 passed to the ActorManager (game +0x34098, vtable +0x90 = FUN_1430f5da0), which keeps
        /// max(value, 1500) at ActorManager +0x6ce678 (its constructor's default is 1500, so 0 means the default). Read
        /// back only through vtable +0x88 (FUN_1430eca00), whose callers are not found yet [P store; meaning open]. 0.
        /// </summary>
        public float SlotA0 { get; set; }
        /// <summary>+0xA4 the StaticZoneData zone id, game +0x31fac (as SendZoneDetails.GeometryId).</summary>
        public uint GeometryId { get; set; }
        /// <summary>
        /// +0xA8 the world id, game +0x378cc (as SendZoneDetails.WorldId): a different world than the client's makes it
        /// change world (Game.CrossWorld). Was named ZoneNameId here.
        /// </summary>
        public uint WorldId { get; set; }
        /// <summary>+0xAC the zone id, game +0x378d0 (as SendZoneDetails.ZoneId). Was unnamed (slot 8).</summary>
        public uint ZoneId { get; set; }
        /// <summary>
        /// +0xB0 the world's name text, SetWorldZoneInfo worldNameId (as SendZoneDetails.WorldNameId; 0 shows "Unnamed
        /// World" until the zone details arrive). Was unnamed (slot 9); sent since 2026-10-18.
        /// </summary>
        public uint WorldNameId { get; set; }
        /// <summary>
        /// +0xB4 a flag on the ActorManager: vtable +0x78 (FUN_1430f5ce0) is called with (value == 1) and stores it at
        /// ActorManager +0x6ce68c; the client clears it (with 0) whenever an encounter ends (FUN_142ca6d90, FUN_142ca8b50,
        /// FUN_142d06480). Read back only through vtable +0x80 (FUN_1430ef390), callers not found yet [P store; meaning
        /// open]. False.
        /// </summary>
        public bool SlotB4 { get; set; }
        /// <summary>
        /// +0xB5 WaitForZoneReadyPacket, game +0x31e0f [P: named by the client's own "WaitForWorldReady Status: …
        /// WaitForZoneReadyPacket=%d" log in FUN_142d5e590]: while set the client keeps waiting for a zone-ready packet
        /// (which clears it, FUN_142d10020) before the world counts as ready. False: the server sends none.
        /// </summary>
        public bool WaitForZoneReadyPacket { get; set; }

        /// <summary>+0xB8 the continent settings (as SendZoneDetails.Continent).</summary>
        public ContinentDefinition Continent { get; set; } = new();

        public void Read(GamePacketReader reader)
        {
            ZoneName = reader.ReadString();
            ZoneType = reader.ReadUInt();
            for (int i = 0; i < 4; i++)
                Position[i] = reader.ReadSingle();
            for (int i = 0; i < 4; i++)
                Rotation[i] = reader.ReadSingle();
            SkyXml = reader.ReadString();
            MovementVersion = reader.ReadByte();
            ZoneMode = reader.ReadUInt();
            SlotA0 = reader.ReadSingle();
            GeometryId = reader.ReadUInt();
            WorldId = reader.ReadUInt();
            ZoneId = reader.ReadUInt();
            WorldNameId = reader.ReadUInt();
            SlotB4 = reader.ReadBool();
            WaitForZoneReadyPacket = reader.ReadBool();

            Continent.Read(reader);
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ZoneName);
            writer.Write(ZoneType);
            for (int i = 0; i < 4; i++)
                writer.Write(Position[i]);
            for (int i = 0; i < 4; i++)
                writer.Write(Rotation[i]);
            writer.Write(SkyXml);
            writer.Write(MovementVersion);
            writer.Write(ZoneMode);
            writer.Write(SlotA0);
            writer.Write(GeometryId);
            writer.Write(WorldId);
            writer.Write(ZoneId);
            writer.Write(WorldNameId);
            writer.Write(SlotB4);
            writer.Write(WaitForZoneReadyPacket);

            Continent.Write(writer);
        }
    }
}
