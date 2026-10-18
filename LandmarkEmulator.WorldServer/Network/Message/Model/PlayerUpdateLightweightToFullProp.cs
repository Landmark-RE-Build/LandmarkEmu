using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Turns a lightweight prop into a full entity (PlayerUpdate 0x63, reader FUN_142bb2ae0, handler FUN_142c459f0).
    /// The client asks for it (FullCharacterDataRequest) after AddLightweightProp, and its prop placement session
    /// (FUN_142f1ab30) can only grab a prop that is full. Body = the LightweightToFullNpc base (FUN_142b8f230) with
    /// everything empty, then string, u32, byte array. Found by transient id, which must match the spawn.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateLightweightToFullPropWire)]
    public class PlayerUpdateLightweightToFullProp : IWritable
    {
        public uint TransientId { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.WriteUIntWith2BitLength((int)TransientId);
            // The shared base is applied over the entity's defaults unconditionally in 0.695 (FUN_142d532f0), so those
            // defaults are sent (trace/0695/agent-leads2-a.md L85) [P]: chat-bubble colours, expected speed 8.0, +0xb8 100,
            // +0x248 2, +0x250 / +0x254 unset; disposition 2 (the client default, friendly) [I].
            writer.Write(0xFFFFFFu);
            writer.Write(0xD4E2F0u);
            writer.Write(1u);
            writer.Write(0u);                   // attachments
            writer.Write("");
            writer.Write("");
            writer.Write("");
            writer.Write(2u);                   // +0x90 disposition (default 2)
            for (int i = 0; i < 2 + 3 + 3; i++) // +0x94 .. +0xb0
                writer.Write(0u);
            writer.Write(System.BitConverter.SingleToUInt32Bits(8f));   // +0xb4 expected speed
            writer.Write(100u);                 // +0xb8 (default 100)
            writer.Write(0u);                   // +0xbc
            writer.Write(0u);                   // +0xc0
            // FUN_142b84f30: u32, string, string, u32, string.
            writer.Write(0u);
            writer.Write("");
            writer.Write("");
            writer.Write(0u);
            writer.Write("");
            for (int i = 0; i < 4 + 2; i++)
                writer.Write(0u);
            writer.Write(0ul);
            writer.Write(0u);
            writer.Write((byte)0);              // Target: none
            writer.Write(0u);                   // character variables
            writer.Write(0u);
            writer.Write(0u);
            for (int i = 0; i < 4 + 9; i++)
                writer.Write(0u);
            writer.Write((byte)0);              // +0x244
            writer.Write((byte)2);              // +0x248 (default 2)
            writer.Write(0u);                   // +0x24c
            writer.Write(uint.MaxValue);        // +0x250 unset
            writer.Write(uint.MaxValue);        // +0x254 unset
            writer.Write(0u);                   // list (0xd8-byte entries)
            writer.Write(0ul);
            writer.Write(0u);
            writer.Write(0u);                   // effects
            writer.Write(0u);                   // byte array
            for (int i = 0; i < 4; i++)
                writer.Write(0u);
            writer.Write(0u);                   // u32 list
            // Prop part.
            writer.Write("");
            writer.Write(0u);
            writer.Write(0u);                   // byte array
        }
    }
}
