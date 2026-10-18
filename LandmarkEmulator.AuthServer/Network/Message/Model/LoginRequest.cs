using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    /// <summary>
    /// Login::ExternalPackets::PacketLoginRequest: six strings after the opcode byte (writer 0.695 FUN_143a54110; the
    /// builder FUN_143a5bcd0 is the same code in 0.589). The first, second and fourth to sixth come from the client's
    /// credentials (StringFixed&lt;32&gt;, StringFixed&lt;2048&gt;, then three StringFixed&lt;32&gt;), the third is
    /// the locale (StringFixed&lt;5&gt;, e.g. "en_us" from a 0.695 client). Only the session id is used.
    /// </summary>
    /// <remarks>
    /// Read as string, string, u32, u32 before 2026-10-15: a non-empty locale then left bytes over (the "13 bytes
    /// remaining" of the 0.695 test: the locale's last letter plus three empty strings; trace/0695/OPEN-LEADS.md L9).
    /// The names after the session id follow other ForgeLight login servers and are not proven from this client.
    /// </remarks>
    [AuthMessage(AuthMessageOpcode.LoginRequest, ProtocolVersion.LOGIN_ALL)]
    public class LoginRequest : IReadable
    {
        // Field names proven by the client's own packet printer FUN_143a57510: "Session" +0x10, "Fingerprint" +0x50,
        // "Locale" +0x90, "ThirdPartyAuthTicket" +0xb8, "ThirdPartyUserId" +0xf8, "ThirdPartyId" +0x138 (OPEN-LEADS L9).
        public string SessionId { get; private set; }
        public string SystemFingerPrint { get; private set; }
        public string Locale { get; private set; }
        public string ThirdPartyAuthTicket { get; private set; }
        public string ThirdPartyUserId { get; private set; }
        public string ThirdPartyId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SessionId            = reader.ReadString();
            SystemFingerPrint    = reader.ReadString();
            Locale               = reader.ReadString();
            ThirdPartyAuthTicket = reader.ReadString();
            ThirdPartyUserId     = reader.ReadString();
            ThirdPartyId         = reader.ReadString();
        }
    }
}
