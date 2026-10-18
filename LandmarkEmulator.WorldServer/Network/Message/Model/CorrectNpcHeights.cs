using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PacketCorrectNpcHeights (0x114, C→S, both builds; 0.695 sender FUN_1433278c0, a callback of the client's batched
    /// actor queries, posted through FUN_1433229e0): u32 game time, Array&lt;u64 character guid&gt;, Array&lt;f32 height&gt;.
    /// One entry per NPC actor (entity type 0x10) whose movement component reports a pending height correction; the
    /// height comes from the actor's physics result (+0x34). The tools' catalogue listed it as S→C: it is client-built.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.CorrectNpcHeights)]
    public class CorrectNpcHeights : IReadable
    {
        public uint GameTime { get; private set; }
        public List<ulong> Guids { get; } = new();
        public List<float> Heights { get; } = new();

        public void Read(GamePacketReader reader)
        {
            GameTime = reader.ReadUInt();
            uint n = reader.ReadUInt();
            for (uint i = 0; i < n && reader.BytesRemaining >= 8; i++)
                Guids.Add(reader.ReadULong());
            uint m = reader.ReadUInt();
            for (uint i = 0; i < m && reader.BytesRemaining >= 4; i++)
                Heights.Add(reader.ReadSingle());
        }
    }
}
