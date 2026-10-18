using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.Gateway.Network.Message.Model
{
    /// <summary>
    /// ChannelIsRoutable (opcode 7, the channel in the first byte): bool isRoutable, bool (meaning not found yet, L70).
    /// The client only logs it (FUN_142d582a0, NetInfo.log "RECEIVED=OnChannelIsRoutable channel=%d isRoutable=%d"),
    /// which makes it a cheap marker in tests of back-end restarts.
    /// </summary>
    [GatewayMessage(GatewayMessageOpcode.ChannelIsRoutable, ProtocolVersion.ExternalGatewayApi_3)]
    public class ChannelIsRoutable : IWritable
    {
        public bool IsRoutable { get; set; }
        public bool Second { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(IsRoutable);
            writer.Write(Second);
        }
    }

    /// <summary>ConnectionIsNotRoutable (opcode 8, no body); the client only logs it (FUN_142d58300).</summary>
    [GatewayMessage(GatewayMessageOpcode.ConnectionIsNotRoutable, ProtocolVersion.ExternalGatewayApi_3)]
    public class ConnectionIsNotRoutable : IWritable
    {
        public void Write(GamePacketWriter writer)
        {
        }
    }

    /// <summary>
    /// A gateway slash command (opcode 9): the client's ClientCommandPacket with its zone header (1E 00 43 00, string
    /// name, u32 n, n × string). Kept whole for the gateway's command handler.
    /// </summary>
    [GatewayMessage(GatewayMessageOpcode.GatewayCommand, ProtocolVersion.ExternalGatewayApi_3)]
    public class GatewayCommand : IReadable
    {
        public byte[] Packet { get; private set; }

        public void Read(GamePacketReader reader)
        {
            Packet = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
