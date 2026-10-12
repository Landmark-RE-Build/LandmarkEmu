namespace LandmarkEmulator.WorldServer.Network.Message
{
    /// <summary>
    /// Client build detection for packets whose layout differs between the 0.589 (Steam) and 0.695 clients.
    /// </summary>
    /// <remarks>
    /// Zone opcodes are the same in both builds: every packet the 0.589 zone dispatcher names
    /// via its jump table has the same base id as <see cref="ZoneMessageOpcode"/>. The order of the cPacketId*
    /// strings in Landmark64.exe is not the enum order and must not be used to derive ids.
    /// </remarks>
    public static class ZoneOpcodeTranslator
    {
        public static bool Uses589Opcodes(string clientBuild) => clientBuild?.StartsWith("0.589.") ?? false;
    }
}
