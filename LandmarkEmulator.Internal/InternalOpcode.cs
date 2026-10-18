namespace LandmarkEmulator.Internal
{
    /// <summary>
    /// The first byte of every internal packet between the Docker services (trace/architecture/internal-transport.md,
    /// gateway-service.md §3.4). 7, 8 and 0x16 are live's: the client's packet tracker (0.695 FUN_143c7a9c0) reads a
    /// one-byte internal opcode and knows these three with header sizes 19, 13 and 11 + 12 per destination (count at
    /// offset 7). Which of 7 / 8 is "from" and which "to" the client is an inference. The rest are ours.
    /// </summary>
    public enum InternalOpcode : byte
    {
        /// <summary>Gateway::InternalPackets::PacketTunnelPacketFromExternalConnection: a client packet for a back end.</summary>
        TunnelFromExternal = 0x07,
        /// <summary>Gateway::InternalPackets::PacketTunnelPacketToExternalConnection: a packet for one client.</summary>
        TunnelToExternal = 0x08,
        /// <summary>Gateway::InternalPackets::PacketTunnelPacketToExternalConnectionList: one packet for many clients.</summary>
        TunnelToExternalList = 0x16,

        // Ours (gateway-service.md §3.4).
        RegisterBackEnd = 0x20,
        AttachConnection = 0x21,
        ConnectionAttached = 0x22,
        ConnectionClosed = 0x23,
        CloseConnection = 0x24,
        HandOver = 0x25,
        PositionCopy = 0x26,
        ZoneChanged = 0x27,
        Ping = 0x28,
        /// <summary>Gateway → zone back ends: the worlds whose zones this gateway routes (cross-world travel, B.10).</summary>
        WorldsReachable = 0x29,
        /// <summary>A service-to-service message: u16 family, u32 sub, body (the client's family / sub ids and names where
        /// one exists, internal-transport.md §3).</summary>
        ServicePacket = 0x30
    }
}
