using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientFinishedLoading (C→S, empty): the loading screen is gone and the world is shown.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientFinishedLoading)]
    public class ClientFinishedLoading : IReadable
    {
        public void Read(GamePacketReader reader)
        {
        }
    }
}
