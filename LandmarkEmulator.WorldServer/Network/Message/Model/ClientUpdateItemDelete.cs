using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Removes an item (by guid) from the player's inventory view.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateItemDelete)]
    public class ClientUpdateItemDelete : IWritable
    {
        public ulong Guid { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
        }
    }
}
