using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Leaving the zone: logout to character select (a fresh login session) and closing the connection when the client
    /// says it is leaving. See docs/protocol.md, "Session lifecycle".
    /// </summary>
    public class CharacterSelectHandler
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The client is leaving the zone: close the connection at once. The client waits up to 2 s for this; when the
        /// server never closes it, the client's own disconnect fires after it has moved on to reconnecting to the
        /// login server, its "disconnected" handler starts another logout, and the client shuts down instead of
        /// returning to character select.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientLogout)]
        public static void HandleClientLogout(WorldSession session, ClientLogout logout)
        {
            log.Info($"{session.CharacterName ?? "A client"} logged out of the zone.");
            session.OnDisconnect();
        }

        /// <summary>
        /// Logging out to character select (Lua Ui.Logout): the world-channel back end issues a fresh login session
        /// (<see cref="Game.WorldChannel.IWorldChannelService.CharacterSelectSession"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.CharacterSelectSessionRequest)]
        public static void HandleCharacterSelectSessionRequest(WorldSession session, CharacterSelectSessionRequest request)
            => Game.WorldChannel.WorldChannelServices.WorldChannel.CharacterSelectSession(session);
    }
}
