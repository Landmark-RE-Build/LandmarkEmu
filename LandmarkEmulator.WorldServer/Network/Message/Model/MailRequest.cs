using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// MailBase 0xF8 from the client: a u32 sub-opcode, then its body. Sub 6 PacketMailInboxListRequest (Open Inbox).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.MailBase)]
    public class MailRequest : SubRequest32
    {
        public const uint InboxListRequest = 6;
    }
}
