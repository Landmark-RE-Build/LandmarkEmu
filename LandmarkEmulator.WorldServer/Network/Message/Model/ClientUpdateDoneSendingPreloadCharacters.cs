using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientUpdate DoneSendingPreloadCharacters (S→C, empty): the characters the client should know before entering
    /// have all been sent.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateDoneSendingPreloadCharacters)]
    public class ClientUpdateDoneSendingPreloadCharacters : IWritable
    {
        public void Write(GamePacketWriter writer)
        {
            writer.Write((byte)0);
        }
    }
}
