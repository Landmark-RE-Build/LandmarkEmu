using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ZoneDoneSendingInitialData (S→C, empty): the initial zone data is complete.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ZoneDoneSendingInitialData)]
    public class ZoneDoneSendingInitialData : IWritable
    {
        public void Write(GamePacketWriter writer)
        {
            
        }
    }
}
