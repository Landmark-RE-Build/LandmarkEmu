using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.AuthServer.Network.Message.Model.Shared;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;

namespace LandmarkEmulator.Parser
{
    /// <summary>
    /// How the capture tools read a captured message. The packet models' Read is the exact mirror of their Write (the
    /// server's own form, round-tripped by the tests); what only a capture needs lives here:
    /// <list type="bullet">
    /// <item>SendSelfToClient from a live capture has a size prefix and the 0.589-era tail:
    /// <see cref="SendSelfToClient.ReadCapture"/>, not Read.</item>
    /// <item>World names in the log: the text ids' strings appended to Name / Description (what Server.Read used to do).</item>
    /// </list>
    /// </summary>
    public static class CaptureReading
    {
        /// <summary>Reads a captured message body into <paramref name="message"/>, the capture's way.</summary>
        public static void Read(IReadable message, GamePacketReader reader)
        {
            if (message is SendSelfToClient sendSelf)
                sendSelf.ReadCapture(reader);
            else
                message.Read(reader);
            DecorateServers(message);
        }

        /// <summary>ServerListReply worlds: "Name (text of NameId)", "Description (text of DescriptionId)", for the log.</summary>
        public static void DecorateServers(IReadable message)
        {
            if (message is not ServerListReply list || TextManager.Instance == null)
                return;
            foreach (Server server in list.Servers)
            {
                server.Name += $" ({TextManager.Instance.GetTextForId(server.NameId)})";
                server.Description += $" ({TextManager.Instance.GetTextForId(server.DescriptionId)})";
            }
        }
    }
}
