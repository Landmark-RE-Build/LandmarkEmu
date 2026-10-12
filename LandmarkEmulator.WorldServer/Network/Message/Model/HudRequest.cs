using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// HudManagerBase 0xDC from the client: a u8 sub-opcode, then its body. Sub 1 HudPackets::PacketRadialMenuRequest
    /// {u64 target guid, u32 request type} asks the server which right-click menu buttons to show.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.HudManagerBase)]
    public class HudRequest : SubRequest8
    {
        public const byte RadialMenuRequest = 1;
    }
}
