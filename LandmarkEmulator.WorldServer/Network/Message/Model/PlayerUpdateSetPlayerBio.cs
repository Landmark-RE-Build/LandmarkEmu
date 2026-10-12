using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdate SetPlayerBio: the player's profile text. Answered with an empty bio.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateSetPlayerBio)]
    public class PlayerUpdateSetPlayerBio : IReadable, IWritable
    {
        public void Read(GamePacketReader reader)
        {
        }

        public void Write(GamePacketWriter writer)
        {

        }
    }
}
