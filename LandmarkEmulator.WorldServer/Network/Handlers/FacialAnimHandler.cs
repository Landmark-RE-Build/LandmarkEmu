using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>SOEmote / FacialAnim C→S: settings opt-in (<c>0xF3</c>) and face-frame relay (<c>0xF1</c>→<c>0xF2</c>).</summary>
    public static class FacialAnimHandler
    {
        [ZoneMessageHandler(ZoneMessageOpcode.ClientFacialAnimSettings)]
        public static void HandleSettings(WorldSession session, ClientFacialAnimSettings settings)
            => session.SetFacialAnimReceiver(settings.Receiver);

        [ZoneMessageHandler(ZoneMessageOpcode.ClientFacialAnim)]
        public static void HandleClientFacialAnim(WorldSession session, ClientFacialAnim frame)
            => FacialAnim.RelayFromClient(session, frame.QueueWeight, frame.Payload);
    }
}
