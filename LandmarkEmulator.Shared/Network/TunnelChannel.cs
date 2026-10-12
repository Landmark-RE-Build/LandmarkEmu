namespace LandmarkEmulator.Shared.Network
{
    /// <summary>Gateway tunnel multiplex index (ExternalGatewayApi_3).</summary>
    public enum TunnelChannel
    {
        Zone           = 0,
        World          = 1,
        PositionUpdate = 2,
        Voxel          = 3
    }
}
