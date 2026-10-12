using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ActionManagerBase 0x9A from the client: one sub-opcode byte, then its body. Sub 2 asks to set an action bar
    /// slot (same body as ActionManagerUpdateSlot: u32 bar, u32 slot, u32 type, u64 value), e.g. right-clicking a
    /// tool in the bag puts it on the bar as type 2 (item guid).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ActionManagerBase)]
    public class ActionManagerRequest : SubRequest8
    {
        public const byte ClearSlot = 1, SetSlot = 2;
    }
}
