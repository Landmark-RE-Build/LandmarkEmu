using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// A prop request from the client: PropBase 0xED, a u8 sub-opcode, then its body (handled per sub-opcode).
    /// Sub 0x12 PlacePropById = u32 prop id (from the build palette); 3 Save = the placement result.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PropBase)]
    public class PropRequest : SubRequest8
    {
        public const byte Save = 0x03;
        public const byte PickUp = 0x05;
        public const byte Delete = 0x07;
        public const byte PlacePropById = 0x12;
    }
}
