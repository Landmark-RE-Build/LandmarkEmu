using System;

namespace LandmarkEmulator.Shared.Network
{
    /// <summary>Edge ↔ zone backend seam (in-process today; gRPC in W6).</summary>
    public interface IZoneSessionChannel
    {
        Guid SessionId { get; }
        void SendTunnelDown(TunnelChannel channel, byte[] payload);
        void SubmitTerrainPlan(TerrainPlan plan);
        void Disconnect(ZoneDisconnectReason reason);
    }

    /// <summary>Zone-side handler for client→zone tunnel payloads.</summary>
    public interface IZoneTunnelSink
    {
        void OnTunnelUp(TunnelChannel channel, byte[] payload);
    }
}
