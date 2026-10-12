namespace LandmarkEmulator.Shared.Network.Message.Model
{
    [ProtocolMessage(ProtocolMessageOpcode.SessionRequest, useEncryption: false)]
    public class SessionRequest : IProtocol
    {
        /// <summary>Wire u32be protocol version (Live: 3). Historically misnamed CRCLength.</summary>
        public uint ProtocolVersion { get; set; }
        public uint SessionId { get; set; }
        public uint UdpLength { get; set; }
        public string Protocol { get; set; }

        public void Read(ProtocolPacketReader reader, PacketOptions options)
        {
            ProtocolVersion = reader.ReadUInt();
            SessionId       = reader.ReadUInt();
            UdpLength       = reader.ReadUInt();
            Protocol        = reader.ReadNullTerminatedString();
        }

        public void Write(ProtocolPacketWriter writer, PacketOptions options)
        {
            throw new System.NotImplementedException();
        }
    }
}
