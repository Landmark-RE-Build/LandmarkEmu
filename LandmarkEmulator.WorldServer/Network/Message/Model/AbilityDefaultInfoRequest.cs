using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Abilities default-info request (C→S, empty). Answered with an empty list.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityDefaultInfoRequest)]
    public class AbilityDefaultInfoRequest : IReadable
    {
        public void Read(GamePacketReader reader)
        {
            
        }
    }
}
