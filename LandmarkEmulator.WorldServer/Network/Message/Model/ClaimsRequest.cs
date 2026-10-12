using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// A claims request from the client: ClaimsBase 0xF9, a u8 sub-opcode, then its body (0x1A PlaceClaimRequest,
    /// 0x1B PlaceClaimConfirm; handled in Game.Claims).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClaimsBase)]
    public class ClaimsRequest : SubRequest8 { }
}
