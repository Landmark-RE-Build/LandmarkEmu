using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Voice 0xB9 (cPacketIdVoiceBase, 0.695 FUN_1434ba530: u16 base then u8 sub, the base writer FUN_1430cac30) from the
    /// client. Its only C→S packet is sub 6 VoiceRequestCustomChannel (0.695 FUN_1430d3640 → writer FUN_1430cb3f0; 0.589
    /// FUN_143025040): {string room name, string password} [P: two strings after the header, FUN_142d4b510 ×2], sent when the
    /// player asks for a custom voice room (Lua <c>Voice.RequestCustomRoom(roomName[, password])</c>, FUN_1435fa200, or the
    /// voice console command "join &lt;Name&gt; [Password]", FUN_1430d1b00). The client keeps no pending state for it (no
    /// flag, no timer: the writer only sends), so not answering never hangs anything. Handled by
    /// <see cref="Handlers.VoiceHandler"/>.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.VoiceBase)]
    public class VoiceRequest : IReadable
    {
        public const byte RequestCustomChannel = 6;

        public byte SubOpcode { get; set; }
        /// <summary>Sub 6: the room the player asked for, and its password (empty when none).</summary>
        public string RoomName { get; set; } = "";
        public string Password { get; set; } = "";
        /// <summary>Any other sub's body, kept for the log.</summary>
        public byte[] Body { get; set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadByte();
            if (SubOpcode == RequestCustomChannel && reader.BytesRemaining >= 8)
            {
                RoomName = reader.ReadString();
                Password = reader.ReadString();
            }
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
