using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Tells the client its copy of these terrain cells is current, completing any pending cell requests for them
    /// (0.589 client: TerrainCellPacketUpToDateCells).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.TerrainCellUpToDateCells)]
    public class TerrainCellUpToDateCells : IWritable
    {
        public List<ulong> CellIds { get; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.WriteBytes(new byte[2]); // high half of the 32-bit sub-opcode (Write(ushort) emits 4 bytes)
            writer.Write((uint)CellIds.Count);
            foreach (ulong cellId in CellIds)
                writer.Write(cellId);
        }
    }
}
