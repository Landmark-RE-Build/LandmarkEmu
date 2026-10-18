namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// The zone side's way to its client (trace/architecture/gateway-service.md §3, Phase B gateway step 2). A
    /// <see cref="WorldSession"/> holds the game state of one connected character; the connection itself — UDP, SOE,
    /// encryption, transport flood limits — is the gateway's. Today the gateway half is
    /// <see cref="ClientSession"/> in this process; when the gateway becomes its own process this is backed by the
    /// internal link (TunnelPacketToExternalConnection 8 / CloseConnection 0x24) instead, with no change to the zone.
    /// </summary>
    public interface IClientLink
    {
        /// <summary>The gateway's id for the connection (internal-transport.md: connectionId).</summary>
        ulong ConnectionId { get; }

        /// <summary>Packets may be sent now (the client finished its SOE handshake and the connection is open).</summary>
        bool CanSend { get; }

        /// <summary>The connection is gone.</summary>
        bool Closed { get; }

        /// <summary>
        /// One zone packet (opcode bytes + body) for the client, in the 0.695 client's own numbering (the server speaks
        /// it natively, docs/protocol.md §1.6): sent as given.
        /// </summary>
        void Send(byte[] packet);

        /// <summary>Close the connection (logout, kick, duplicate login).</summary>
        void Close();
    }
}
