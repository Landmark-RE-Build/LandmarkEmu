using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// UiBase 0x32 from the client: a u8 sub-opcode, then its body. Sub 0x17 UIEventTriggeredPacket {u32 event id}
    /// reports UI events (e.g. 0x1A when the inventory opens) for tutorial goals; it needs no reply.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.UiBase)]
    public class UiRequest : SubRequest8
    {
        public const byte EventTriggered = 0x17;
    }
}
