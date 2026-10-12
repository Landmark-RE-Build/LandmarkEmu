namespace LandmarkEmulator.Shared.Network
{
    /// <summary>How the edge process reaches zone logic (in-process monolith vs gRPC).</summary>
    public enum EdgeZoneBackendMode
    {
        InProcess = 0,
        Grpc = 1,
    }
}
