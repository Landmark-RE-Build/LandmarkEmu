namespace LandmarkEmulator.Gateway.Network
{
    /// <summary>
    /// The client build a connection logs in with (Gateway LoginRequest.ClientBuild, e.g. "0.589.1.128895" for the
    /// Steam client, "0.695.…" for the final build).
    /// </summary>
    /// <remarks>
    /// The server speaks the 0.695 client only, its opcodes and sub numbers natively (docs/protocol.md §1.6): the 0.589
    /// client numbers four families differently (Claims 0xF9, Mail 0xF8, InGamePurchase 0x45, InstancedGame 0x10D) and
    /// reads PlayerUpdate 0x26 with a one-byte sub, so it is refused at the gateway login rather than served packets it
    /// would misread. Until 2026-10-17 a header translation (ClientWire695 / ZoneOpcodeTranslator) let one server serve
    /// both; 0.589 is served by the frozen client-0.589 branch.
    /// </remarks>
    public static class ClientBuilds
    {
        /// <summary>The 0.589 (Steam) client, which this server does not serve.</summary>
        public static bool Is589(string clientBuild) => clientBuild?.StartsWith("0.589.") ?? false;
    }
}
