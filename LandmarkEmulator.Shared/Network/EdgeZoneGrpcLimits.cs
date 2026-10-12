namespace LandmarkEmulator.Shared.Network
{
    /// <summary>
    /// Zone↔edge gRPC message size for <c>Attach</c>. Default 4 MiB is too small for
    /// <c>TerrainCellModifiedCellManifest</c> (~8.5 MB dump) and large <c>TerrainPlan</c> payloads.
    /// </summary>
    public static class EdgeZoneGrpcLimits
    {
        public const int MaxMessageBytes = 64 * 1024 * 1024;
    }
}
