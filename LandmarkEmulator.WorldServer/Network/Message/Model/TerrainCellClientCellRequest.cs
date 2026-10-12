using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Sent by the client when it wants the server's copy of terrain cells (e.g. cells listed in the modified cell manifest).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.TerrainCellClientCellRequest)]
    public class TerrainCellClientCellRequest : IReadable
    {
        public class CellRequest
        {
            public ulong CellId { get; set; }
            public ulong Unknown1 { get; set; }
            public ulong Unknown2 { get; set; }
        }

        public ulong Unknown0 { get; set; }
        public List<CellRequest> Cells { get; } = new();

        public void Read(GamePacketReader reader)
        {
            reader.ReadUShort(); // high half of the 32-bit sub-opcode
            Unknown0 = reader.ReadULong();

            uint count = reader.ReadUInt();
            for (uint i = 0; i < count; i++)
            {
                Cells.Add(new CellRequest
                {
                    CellId   = reader.ReadULong(),
                    Unknown1 = reader.ReadULong(),
                    Unknown2 = reader.ReadULong()
                });
            }
        }
    }
}
