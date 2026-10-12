using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// TerrainCell 2 ClientPendingRequestTimeout (C→S, client FUN_1430aca10): a cell request got no answer within the
    /// client's 3 seconds ("VoxelClient - Removing pending cell callback for sticking around longer than 3 seconds").
    /// u16 (high half of the sub), u64 (the same value the cell request carries, DAT_1453743d8), u64 cell.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.TerrainCellPendingRequestTimeout)]
    public class TerrainCellPendingRequestTimeout : IReadable
    {
        public ulong Context { get; private set; }
        public ulong CellId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            reader.ReadUShort();
            Context = reader.ReadULong();
            CellId = reader.ReadULong();
        }
    }
}
