namespace LandmarkEmulator.Shared.Network.Message.Model
{
    /// <summary>
    /// Keepalive only. Client receive path skips CRC/decrypt/dispatch; trailing bytes are ignored.
    /// </summary>
    [ProtocolMessage(ProtocolMessageOpcode.Ping, useEncryption: false)]
    public class Ping : IProtocol
    {
        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
        }
    }
}
