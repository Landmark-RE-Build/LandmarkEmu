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
        /// <summary>
        /// The UDP protocol version (live: 3) [P]: the client's UdpLibrary reads it only when the reply is longer than
        /// 0x11 bytes and stores it at UdpConnection +0x204 (0.695 FUN_142b69770 packet type 2; the same slot the
        /// SessionRequest's version fills on the accepting side, type 1); nothing else reads +0x204. The server echoes the
        /// client's SessionRequest version.
        /// </summary>
        public uint ProtocolVersion { get; set; } = 3;

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            SessionId          = reader.ReadUInt();
            CRCSeed            = reader.ReadUInt();
            CRCLength          = reader.ReadByte();
            EncryptionMethod   = reader.ReadByte();
            EncryptionMethod2  = reader.ReadByte();
            UdpLength          = reader.ReadUInt();
            ProtocolVersion    = reader.ReadUInt();
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            writer.Write(SessionId);
            writer.Write(CRCSeed);
            writer.Write(CRCLength);
            writer.Write(EncryptionMethod);
            writer.Write(EncryptionMethod2);
            writer.Write(UdpLength);
            writer.Write(ProtocolVersion);
        }
    }
}
