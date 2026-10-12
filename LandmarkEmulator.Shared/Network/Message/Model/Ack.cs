namespace LandmarkEmulator.Shared.Network.Message.Model
{
    /// <summary>
    /// SOE Acknowledge — release one reliable sequence (wire <c>0x11</c>+channel).
    /// </summary>
    [ProtocolMessage(ProtocolMessageOpcode.Ack, useEncryption: true)]
    public class Ack : IProtocol
    {
        public ushort Sequence { get; set; }

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            if (options.Compression && !options.IsSubpacket)
                reader.ReadByte();
            Sequence = reader.ReadUShort();
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            if (options.Compression && !options.IsSubpacket)
                writer.Write((byte)0);
            writer.Write(Sequence);
        }
    }
}
