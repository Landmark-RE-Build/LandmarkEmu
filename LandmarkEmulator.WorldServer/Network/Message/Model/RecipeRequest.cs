using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// A crafting request from the client: RecipeBase 0x42, a u32 sub-opcode, then its body. Sub 1 StartRequest:
    /// u32 recipe id, u32 count, u64.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.RecipeBase)]
    public class RecipeRequest : SubRequest32
    {
        public const uint Start = 0x01;
    }
}
