using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.Gateway.Network.Message.Model
{
    /// <summary>
    /// ForcedLogout (opcode 4): StringFixed&lt;64&gt; reason (u32 length + characters; reader FUN_143a49fc0). Was written
    /// as a u32, which only worked because 0 reads as an empty string.
    /// </summary>
    [GatewayMessage(GatewayMessageOpcode.ForceDisconnect, ProtocolVersion.ExternalGatewayApi_3)]
    public class ForceDisconnect : IWritable
    {
        public string Reason { get; set; } = "";

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Reason.Length > 64 ? Reason[..64] : Reason);
        }
    }
}
