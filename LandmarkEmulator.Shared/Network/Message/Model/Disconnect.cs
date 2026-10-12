namespace LandmarkEmulator.Shared.Network.Message.Model
{
    [ProtocolMessage(ProtocolMessageOpcode.Disconnect, useEncryption: true)]
    public class Disconnect : IProtocol
    {
        public uint SessionId { get; set; }
        /// <summary>Live sample often uses 6; 0 is a generic default.</summary>
        public ushort Reason { get; set; }

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            // Auth (compression advertised) may prefix a flag byte; Gateway does not.
            if (options.Compression && !options.IsSubpacket && reader.BytesRemaining > 0)
                reader.ReadByte();

            SessionId = reader.ReadUInt();
            if (reader.BytesRemaining >= 2)
                Reason = reader.ReadUShort();
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            if (options.Compression && !options.IsSubpacket)
                writer.Write((byte)0);

            writer.Write(SessionId);
            writer.Write(Reason);
        }
    }
}
