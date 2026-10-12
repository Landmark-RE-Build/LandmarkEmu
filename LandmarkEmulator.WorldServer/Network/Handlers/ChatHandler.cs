using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Chat (0x13). Sub 1 is a chat message; <see cref="Game.Chat"/> fills in the real sender and relays it by channel.
    /// </summary>
    public static class ChatHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Passes chat messages (sub 1) to <see cref="Game.Chat.Handle"/>; other subs are only logged.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ChatBase)]
        public static void HandleChat(WorldSession session, ChatRequest request)
        {
            if (request.SubOpcode == 1)
                Game.Chat.Handle(session, request.Body);
            else if (request.SubOpcode == Game.ChatRooms.JoinRequest || request.SubOpcode == Game.ChatRooms.LeaveRequest)
                Game.ChatRooms.Handle(session, (ushort)request.SubOpcode, request.Body);
            else
                log.Debug($"Unhandled chat request {request.SubOpcode}.");
        }
    }
}
