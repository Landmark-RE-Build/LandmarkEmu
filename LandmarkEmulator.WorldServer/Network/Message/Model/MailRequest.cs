using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// MailBase 0xF8 from the client: a u32 sub-opcode, then its body. Sub 7 PacketMailInboxListRequest (Open Inbox;
    /// 0.695 number, 0.589 6: Game/Mail.cs).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.MailBase)]
    public class MailRequest : SubRequest32
    {
        public const uint InboxListRequest = 7;
    }
}
