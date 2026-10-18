using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// KeepAlive (0x63, C→S) {u32 game time}: sent periodically. No reply; any packet keeps the session alive. The game
    /// time is the client's synced clock FUN_142cd2690 (ms) [P]; the server keeps it (WorldSession.ClientClockNow) for
    /// packets that carry a start time on that clock (InstancedGame PropInteraction).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.KeepAlive)]
    public class KeepAlive: IReadable, IWritable
    {
        public uint GameTime { get; set; }

        public void Read(GamePacketReader reader)
        {
            GameTime = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(GameTime);
        }
    }
}
