using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdate 0x3B Respawn from the death window (client FUN_143387xxx): u32 respawn type (10 revive here,
    /// 11/12 nearest safe point / hub), then a location id and a value we don't need.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateRespawnWire)]
    public class PlayerUpdateRespawn : IReadable
    {
        public uint Type { get; set; }

        public void Read(GamePacketReader reader)
        {
            Type = reader.ReadUInt();
            reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
