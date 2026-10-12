using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>ChatBase 0x13 from the client: u16 sub-opcode (1 = PacketChat), then its body (Game.Chat).</summary>
    [ZoneMessage(ZoneMessageOpcode.ChatBase)]
    public class ChatRequest : SubRequest16 { }
}
