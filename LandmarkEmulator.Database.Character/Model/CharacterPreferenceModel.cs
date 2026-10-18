using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A character's client preferences as the client last sent them (Game.Preferences): the XML document of
    /// PreferencePacketClientXml (zone opcode 0x03), kept byte for byte and sent back in BasePreferencePacket at the next
    /// login. One row per character and preference kind; the 0.695 client only uses kind 1 (its whole &lt;Root&gt; document:
    /// chat window positions and sizes, action bar lock).
    /// </summary>
    public class CharacterPreferenceModel
    {
        public ulong CharacterId { get; set; }
        /// <summary>The packet's kind field (1: the client's &lt;Root&gt; preference document; the only one it applies).</summary>
        public uint Kind { get; set; }
        /// <summary>The XML exactly as received (no terminator; the client writes and reads a length-prefixed string).</summary>
        public byte[] Xml { get; set; } = Array.Empty<byte>();
        public DateTime UpdatedAt { get; set; }
    }
}
