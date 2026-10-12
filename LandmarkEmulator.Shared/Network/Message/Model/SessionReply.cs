namespace LandmarkEmulator.Shared.Network.Message.Model
{
    /// <summary>
    /// Cleartext session negotiation (no CRC). Wire: sessionId, crcSeed, crcBytes,
    /// encryptMethod, encryptMethod2, udpLength, protocolVersion.
    /// </summary>
    [ProtocolMessage(ProtocolMessageOpcode.SessionReply, useEncryption: false)]
    public class SessionReply : IProtocol
    {
        public uint SessionId { get; set; }
        public uint CRCSeed { get; set; }
        public byte CRCLength { get; set; }
        /// <summary>SOE body transform: 0=memcpy, 1=UdpCompressionHandler (LoginUdp).</summary>
        public byte EncryptionMethod { get; set; }
        /// <summary>Second transform (Live: always 0 in sampled replies).</summary>
        public byte EncryptionMethod2 { get; set; }
        public uint UdpLength { get; set; }
        /// <summary>Protocol version echo (Live: 3).</summary>
        public uint Unknown0 { get; set; } = 3;

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            SessionId          = reader.ReadUInt();
            CRCSeed            = reader.ReadUInt();
            CRCLength          = reader.ReadByte();
            EncryptionMethod   = reader.ReadByte();
            EncryptionMethod2  = reader.ReadByte();
            UdpLength          = reader.ReadUInt();
            Unknown0           = reader.ReadUInt();
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            writer.Write(SessionId);
            writer.Write(CRCSeed);
            writer.Write(CRCLength);
            writer.Write(EncryptionMethod);
            writer.Write(EncryptionMethod2);
            writer.Write(UdpLength);
            writer.Write(Unknown0);
        }
    }
}
