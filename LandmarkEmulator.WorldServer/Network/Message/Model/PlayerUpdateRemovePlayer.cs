using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Despawns a character. Version 0 is the plain removal: u16 version then the guid (0.589 client);
    /// version 1 adds a death/effect payload.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateRemovePlayerWire)]
    public class PlayerUpdateRemovePlayer : IWritable
    {
        public ulong Guid { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.WriteBytes(new byte[2]);
            writer.Write(Guid);
        }
    }
}
