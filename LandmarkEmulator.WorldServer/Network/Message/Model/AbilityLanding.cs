using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// AbilityLanding (Abilities 0x27, sub 0x1D; C→S on the Zone channel): sent by FUN_143322830 when the character lands
    /// after an ability that set its landing flag (+0x4c38) and timer (+0x4c3c): {u64, u64} (header + u64 +0x18, u64
    /// +0x20; ctor part_0098 ~32791; trace/0695/agent-leads2-d1.md L7). No live reply is known; read and logged.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityAbilityLanding)]
    public class AbilityLanding : IReadable
    {
        public ulong First { get; private set; }
        public ulong Second { get; private set; }

        public void Read(GamePacketReader reader)
        {
            if (reader.BytesRemaining >= 8)
                First = reader.ReadULong();
            if (reader.BytesRemaining >= 8)
                Second = reader.ReadULong();
        }
    }
}
