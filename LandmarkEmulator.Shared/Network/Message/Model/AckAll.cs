namespace LandmarkEmulator.Shared.Network.Message.Model
{
    /// <summary>
    /// SOE AcknowledgeAll — release every reliable sequence through this one (wire <c>0x15</c>+channel).
    /// </summary>
    [ProtocolMessage(ProtocolMessageOpcode.AckAll, useEncryption: true)]
    public class AckAll : IProtocol
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
