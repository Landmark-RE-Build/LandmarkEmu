using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Clock sync, NTP style. All fields are 64-bit integers, not doubles (0.589 client).
    /// </summary>
    /// <remarks>
    /// The client measures the round trip from <see cref="ClientSendTime"/> and sets its network clock to
    /// <see cref="ServerTime"/> (milliseconds) plus that. Movement packets are stamped with this clock and other clients
    /// play them back by it, so every client must get the same server clock.
    /// </remarks>
    [ZoneMessage(ZoneMessageOpcode.Synchronization)]
    public class Synchronization : IReadable, IWritable
    {
        public ulong ClientSendTime { get; set; }
        public ulong Time2 { get; set; }
        /// <summary>
        /// Client wall clock (Unix seconds).
        /// </summary>
        public ulong ClientTime { get; set; }
        public ulong ServerTime { get; set; }
        public ulong ServerTime2 { get; set; }
        public ulong Time3 { get; set; }
        public bool Unknown0 { get; set; }

        public void Read(GamePacketReader reader)
        {
            ClientSendTime = reader.ReadULong();
            Time2 = reader.ReadULong();
            ClientTime = reader.ReadULong();
            ServerTime = reader.ReadULong();
            ServerTime2 = reader.ReadULong();
            Time3 = reader.ReadULong();
            Unknown0 = reader.ReadBool();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ClientSendTime);
            writer.Write(Time2);
            writer.Write(ClientTime);
            writer.Write(ServerTime);
            writer.Write(ServerTime2);
            writer.Write(Time3);
            writer.Write(Unknown0);
        }
    }
}
