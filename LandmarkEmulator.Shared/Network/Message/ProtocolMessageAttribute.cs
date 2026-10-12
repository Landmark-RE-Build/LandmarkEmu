using System;

namespace LandmarkEmulator.Shared.Network.Message
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ProtocolMessageAttribute : Attribute
    {
        public ProtocolMessageOpcode Opcode { get; }

        /// <summary>
        /// Historically named UseEncryption. When true, the session layer appends/verifies
        /// a trailing CRC (crcBytes) after the session is established. Cleartext opcodes
        /// (SessionRequest/Reply, Ping) set this false.
        /// </summary>
        public bool UseEncryption { get; }

        public ProtocolMessageAttribute(ProtocolMessageOpcode opcode, bool useEncryption)
        {
            Opcode = opcode;
            UseEncryption = useEncryption;
        }
    }
}
