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
        /// <summary>
        /// +0x40 the client's answer to a server Synchronization: the 0.695 handler FUN_142ce7090 applies a server one
        /// (FUN_143bf9610) and sends it back with +0x40 = 1, while its own five-second request (FUN_142cd4e60) has 0 [P].
        /// The server answers only requests (false) and sends false.
        /// </summary>
        public bool IsReply { get; set; }

        public void Read(GamePacketReader reader)
        {
            ClientSendTime = reader.ReadULong();
            Time2 = reader.ReadULong();
            ClientTime = reader.ReadULong();
            ServerTime = reader.ReadULong();
            ServerTime2 = reader.ReadULong();
            Time3 = reader.ReadULong();
            IsReply = reader.ReadBool();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(ClientSendTime);
            writer.Write(Time2);
            writer.Write(ClientTime);
            writer.Write(ServerTime);
            writer.Write(ServerTime2);
            writer.Write(Time3);
            writer.Write(IsReply);
        }
    }
}
