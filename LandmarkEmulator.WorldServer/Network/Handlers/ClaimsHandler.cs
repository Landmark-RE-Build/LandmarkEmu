using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Build-site requests (ClaimsBase 0xF9, u8 sub). All logic is in <see cref="Game.Claims.HandleRequest"/>.
    /// </summary>
    public static class ClaimsHandler
    {
        /// <summary>
        /// Forwards the sub-opcode and body to the claims system.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClaimsBase)]
        public static void HandleClaims(WorldSession session, ClaimsRequest request)
            => Game.Claims.HandleRequest(session, request.SubOpcode, request.Body);
    }
}
