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
            for (int i = 0; i < 3; i++)
                writer.Write(0u);
            writer.Write(0u);                   // attachments
            writer.Write("");
            writer.Write("");
            writer.Write("");
            for (int i = 0; i < 3 + 3 + 3 + 4; i++)
                writer.Write(0u);
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
            writer.Write((byte)0);
            writer.Write((byte)0);
            for (int i = 0; i < 3; i++)
                writer.Write(0u);
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
