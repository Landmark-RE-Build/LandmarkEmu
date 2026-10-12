using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// InitializationParameters (S→C) {string environment}: the environment name; the server sends "live".
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.InitializationParameters)]
    public class InitializationParameters : IReadable, IWritable
    {
        public string Environment { get; set; } = "";

        public void Read(GamePacketReader reader)
        {
            Environment = reader.ReadString();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Environment);
        }
    }
}
