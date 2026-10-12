namespace LandmarkEmulator.Shared.Network
{
    /// <summary>Reliable-output ack window exposed by the SOE transport (edge-owned in W2).</summary>
    public interface ISoeOutputTelemetry
    {
        bool HasQueuedDataPackets { get; }
        int OutputUnackedCount { get; }
        ushort? OutputHighestSent { get; }
        ushort? OutputNextSequence { get; }
        ushort? OutputLastAck { get; }
        int OutputAckPingMs { get; }
        bool IsOutputAckedThrough(ushort sequence);
    }
}
