using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientIsReady (C→S, empty): SendSelf is loaded; the server sends the rest of the zone (MiscHandler.ClientIsReady).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientIsReady)]
    public class ClientIsReady : IReadable
    {
        public void Read(GamePacketReader reader)
        {
        }
    }
}
