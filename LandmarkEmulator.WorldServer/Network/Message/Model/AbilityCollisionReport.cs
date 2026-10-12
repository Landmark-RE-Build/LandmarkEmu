using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// AbilityCollisionReport (Abilities 0x27, sub 0x18; C→S, built in the character controller's contact handling
    /// FUN_142dd0960 / FUN_142dd1610): the local character touched other entities (a story prop, a creature) while an
    /// ability that reports collisions was running. {u64 reporter, u32 n + n × u64 entity, u32 n + n × vec4 contact
    /// point}. The server does its own hit checks, so this is read and ignored.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCollisionReport)]
    public class AbilityCollisionReport : IReadable
    {
        public ulong Reporter { get; private set; }
        public List<ulong> Entities { get; } = new();
        public List<Vector4> Points { get; } = new();

        public void Read(GamePacketReader reader)
        {
            Reporter = reader.ReadULong();
            uint count = reader.ReadUInt();
            for (uint i = 0; i < count && reader.BytesRemaining >= 8; i++)
                Entities.Add(reader.ReadULong());
            count = reader.ReadUInt();
            for (uint i = 0; i < count && reader.BytesRemaining >= 16; i++)
                Points.Add(new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
        }
    }
}
