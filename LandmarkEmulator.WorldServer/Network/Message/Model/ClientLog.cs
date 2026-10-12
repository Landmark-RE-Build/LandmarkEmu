using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientLog (C→S): a log line from the client, written to the server log at debug level.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientLog)]
    public class ClientLog : IReadable
    {
        public string File { get; set; } = "";
        public string Message { get; set; } = "";

        public void Read(GamePacketReader reader)
        {
            File = reader.ReadString();
            Message = reader.ReadString();
        }
    }
}
