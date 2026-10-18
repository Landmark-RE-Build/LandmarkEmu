using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// TerrainCell 1 ClientCellRequest (C→S): the client wants the server's copy of terrain cells (e.g. cells listed in
    /// the modified cell manifest). 0.695 builder FUN_143157700 (TerrainCellPacketClientCellRequest, list of
    /// VoxelFarm::CellIdAndVersions), writer FUN_143149190 → FUN_143147500 / FUN_143145310 [P]:
    /// u16 (high half of the sub), u64 context, u32 n × {u64 cell, 4 × u32 layer versions}.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.TerrainCellClientCellRequest)]
    public class TerrainCellClientCellRequest : IReadable
    {
        public class CellRequest
        {
            public ulong CellId { get; set; }
            /// <summary>
            /// The versions of the cell's four layers the client holds (0 = none; FUN_143145310 writes 4 × u32). Was read as
            /// two unnamed u64s here: the same 16 bytes.
            /// </summary>
            public uint[] LayerVersions { get; } = new uint[4];
        }

        /// <summary>
        /// The u64 every TerrainCell request header carries (packet +0x18): the client copies it from DAT_145453e88 (0.695;
        /// DAT_1453743d8 in 0.589), a .bss global no code writes, so it is always 0 [P]. Unused by the server (as
        /// <see cref="TerrainCellPendingRequestTimeout.Context"/>).
        /// </summary>
        public ulong Context { get; set; }
        public List<CellRequest> Cells { get; } = new();

        public void Read(GamePacketReader reader)
        {
            reader.ReadUShort(); // high half of the 32-bit sub-opcode
            Context = reader.ReadULong();

            uint count = reader.ReadUInt();
            for (uint i = 0; i < count; i++)
            {
                var cell = new CellRequest { CellId = reader.ReadULong() };
                for (int layer = 0; layer < cell.LayerVersions.Length; layer++)
                    cell.LayerVersions[layer] = reader.ReadUInt();
                Cells.Add(cell);
            }
        }
    }
}
