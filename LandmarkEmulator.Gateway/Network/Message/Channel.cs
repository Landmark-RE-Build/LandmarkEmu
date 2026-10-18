namespace LandmarkEmulator.Gateway.Network.Message
{
    /// <summary>
    /// The gateway channel in the top three bits of a tunnel packet's first byte (trace/architecture/channels.md). The
    /// client sends each message on the channel of its back end; it reads every channel except 2 the same way.
    /// </summary>
    public enum Channel
    {
        Zone           = 0,
        World          = 1,
        PositionUpdate = 2,
        /// <summary>Build-cursor and paste previews (all eight WorldEditing 0x10C messages); once called "Voxel", which the
        /// client doesn't support: terrain cell requests go on channel 0.</summary>
        Channel3       = 3
    }
}
