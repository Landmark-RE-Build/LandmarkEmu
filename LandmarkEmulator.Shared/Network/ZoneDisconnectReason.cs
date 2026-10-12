namespace LandmarkEmulator.Shared.Network
{
    public enum ZoneDisconnectReason : uint
    {
        Normal              = 0,
        UnacknowledgedTimeout = 6
    }
}
