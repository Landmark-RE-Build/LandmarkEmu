using LandmarkEmulator.AuthServer.Network.Message.Model.Shared;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    /// <summary>
    /// ServerUpdate (0x0F): one world's entry again, the same layout as a ServerListReply row (client
    /// PacketServerUpdate, reader FUN_14399c690 + bool AllowedAccess). The client replaces that row and character select
    /// re-checks the worlds (EVENT_ON_SERVERS_LIST_UPDATE, CharacterSelection.lua OnServersListUpdate).
    /// </summary>
    [AuthMessage(AuthMessageOpcode.ServerUpdate, ProtocolVersion.LOGIN_ALL)]
    public class ServerUpdate : IWritable
    {
        public Server Server { get; set; } = new();

        public void Write(GamePacketWriter writer) => Server.Write(writer);
    }
}
