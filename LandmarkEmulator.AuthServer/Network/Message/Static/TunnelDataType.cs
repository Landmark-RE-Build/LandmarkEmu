namespace LandmarkEmulator.AuthServer.Network.Message.Static
{
    public enum TunnelDataType
    {
        NameValidationRequest = 0x01,
        NameValidationReply   = 0x02,
        /// <summary>Login queue place (Model/TunnelData/QueueStatus).</summary>
        QueueStatus           = 0x03,
        /// <summary>A queued character may log in now (the client re-sends its CharacterLoginRequest).</summary>
        ServerQueue           = 0x04,
        /// <summary>The player left the login queue (dialog Cancel, Lua CharacterSelect.CancelQueue): {u64}.</summary>
        QueueCancel           = 0x05,
        LoginInit             = 0x06,
        /// <summary>
        /// Server → client, one string that the client reads and drops (FUN_1430b5370 case 7 → FUN_1430acec0 →
        /// FUN_1430b2d50 → FUN_142ce7050, an empty function) [P]; name ours [I]. Model/TunnelData/LoginMessage.
        /// </summary>
        LoginMessage          = 0x07,
        /// <summary>The account's build sites per world, for the "Claims" count of the world list (Model/TunnelData/ClaimData).</summary>
        ClaimData             = 0x09,
        /// <summary>The tint definitions (TintDefinitionManagerData; Model/TunnelData/ArtData).</summary>
        ArtData               = 0x0A
    }
}
