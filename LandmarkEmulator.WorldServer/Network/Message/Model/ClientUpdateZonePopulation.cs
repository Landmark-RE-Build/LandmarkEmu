using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientUpdate ZonePopulation (S→C): exactly four u32 (reader FUN_142c49410, 0.589 the same; leftover bytes make
    /// the client drop the packet) [P]. The old model wrote a count + byte list + three u32, which only matched while the
    /// list was empty (trace/0695/audit-wire-d.md). Meanings unknown; all 0 as before.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateZonePopulation)]
    public class ClientUpdateZonePopulation : IReadable, IWritable
    {
        public uint[] Values { get; set; } = new uint[4];

        public void Read(GamePacketReader reader)
        {
            for (int i = 0; i < 4; i++)
                Values[i] = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            for (int i = 0; i < 4; i++)
                writer.Write(i < Values.Length ? Values[i] : 0u);
        }
    }
}
