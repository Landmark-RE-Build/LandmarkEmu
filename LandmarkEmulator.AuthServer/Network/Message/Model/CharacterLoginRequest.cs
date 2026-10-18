using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    [AuthMessage(AuthMessageOpcode.CharacterLoginRequest, ProtocolVersion.LOGIN_ALL)]
    public class CharacterLoginRequest : IReadable
    {
        public ulong CharacterId { get; set; }
        public ulong ServerId { get; set; }
        /// <summary>
        /// Byte count of the payload that follows (locale string, locale id, gateway id) [P]: the request builder
        /// FUN_1430b3cb0 serializes those three into an Array&lt;u8&gt;, FUN_143a5b9b0 sends {u64 character, u64 server,
        /// payload}, and the packet writer FUN_143a53e90 writes the array as u32 count + bytes (FUN_142da2e50).
        /// </summary>
        public uint PayloadLength { get; set; }
        public string Locale { get; set; }
        /// <summary>The client's locale id (u32 after the locale string; FUN_1430b3cb0 / 0.589 FUN_14300d920).</summary>
        public uint LocaleId { get; set; }
        /// <summary>
        /// GatewayId (u32, default 0): which gateway the client asks for (trace/0695/architecture-leads.md L6, OPEN-LEADS
        /// L50). [P] It is a launch argument, <c>GatewayId=N</c> on the command line, as well as a top-level
        /// ClientConfig.ini / LocalConfig.ini key: the builder of this request (0.695 part_0084:2357) reads it from the
        /// client's main config object with no section, the same place as the launcher's SessionId / Ticket / Server /
        /// StationId / CasSessionId keys; that object (FUN_142ce9580) parses the command line's space-separated
        /// key=value words (FUN_142b539e0), loads ClientConfig.ini, applies the command line again (so it wins over the
        /// file), then merges LocalConfig.ini (trace/0695/agent-leads3-a.md L50). No live launcher argument we have sets
        /// it, so live players sent 0 [I]. One gateway serves a world here, so a non-zero value is only logged.
        /// </summary>
        public uint GatewayId { get; set; }

        public void Read(GamePacketReader reader)
        {
            CharacterId = reader.ReadULong();
            ServerId = reader.ReadULong();
            PayloadLength = reader.ReadUInt();
            Locale = reader.ReadString();
            LocaleId = reader.ReadUInt();
            GatewayId = reader.ReadUInt();
        }
    }
}
