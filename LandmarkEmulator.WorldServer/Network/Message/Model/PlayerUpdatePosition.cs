using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Moves another character: its transient id, then a movement body in the same format the client sends on the
    /// PositionUpdate channel, then 4 floats the client defaults to 1 (0.589 client).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdatePosition)]
    public class PlayerUpdatePosition : IWritable
    {
        public uint TransientId { get; set; }
        public byte[] Movement { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.WriteUIntWith2BitLength((int)TransientId);
            writer.WriteBytes(Movement);
            for (int i = 0; i < 4; i++)
                writer.Write(1f);
        }
    }
}
