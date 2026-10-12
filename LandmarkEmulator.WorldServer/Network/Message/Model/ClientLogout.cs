using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PacketClientLogout (0x14, empty), sent by BaseClient FUN_142c24730 when it leaves the zone (logout to character
    /// select, quit). The client then waits up to 2 s for the server to close the connection.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientLogout)]
    public class ClientLogout : IReadable
    {
        public void Read(GamePacketReader reader)
        {
        }
    }
}
