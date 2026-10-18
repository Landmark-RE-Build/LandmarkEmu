using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Activity requests (ActivityBase 0xA0, client → server; layout in <see cref="ActivityRequest"/>). The refresh the
    /// client sends after every membership change (Game.Membership) and after a mini game is answered with the activity
    /// list it gets at login (MiscHandler: the live capture's ListOfActivities, sub 1) [I: what live answered isn't
    /// captured; a refresh asks for the list again]. Joining an activity is logged only (Landmark has no activities to join).
    /// </summary>
    public static class ActivityHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>ActivityPacketActivityListRefreshRequest's kind byte.</summary>
        private const byte RefreshKind = 4;

        /// <summary>The login activity list (MiscHandler, after the prop definitions): ListOfActivities, empty.</summary>
        private static readonly byte[] ActivityList = Convert.FromHexString("01010200000000000000");

        [ZoneMessageHandler(ZoneMessageOpcode.ActivityBase)]
        public static void HandleActivity(WorldSession session, ActivityRequest request)
        {
            if (request.Group == 1 && request.Kind == RefreshKind)
            {
                session.EnqueueMessage(ZoneMessageOpcode.ActivityBase, ActivityList);
                return;
            }
            log.Debug($"{session.CharacterName}: activity request {request.Group}/{request.Kind} ({BitConverter.ToString(request.Body)}) not handled.");
        }
    }
}
