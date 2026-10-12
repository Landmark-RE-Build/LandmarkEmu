namespace LandmarkEmulator.Shared.Network.Message
{
    public class PacketOptions
    {
        /// <summary>
        /// When true, contextual SOE packets carry a leading compression-flag byte
        /// (SessionReply advertised non-zero enc/compression method — Auth LoginUdp).
        /// Gateway ExternalGatewayApi_3 leaves this false.
        /// </summary>
        public bool Compression { get; set; }
        public bool IsSubpacket { get; set; }
        public bool WasOutOfOrder { get; set; }
        public ushort Sequence { get; set; }
        public bool IsPriority { get; set; } = false;
    }
}
