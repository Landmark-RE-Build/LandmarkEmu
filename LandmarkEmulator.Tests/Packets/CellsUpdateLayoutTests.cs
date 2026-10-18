using LandmarkEmulator.Parser;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The capture tool's CellsUpdate walk (TerrainCel2Packer.ReadCellsUpdateFile) follows the 0.695 client's reader:
    /// FUN_1431464f0 → FUN_143148950 list of VoxelFarm::CellIdVersionFlagsAndData (FUN_143146320: u64 cell, u32 version,
    /// u32 n × layer), each layer FUN_143146cf0 {u32 layer id, u32 layer version, u8 flag, u32 size, data}, then the
    /// transaction list and two bools. No live CellsUpdate capture is kept in the repository, so the packets come from the
    /// server's own writer (TerrainStore.BuildBlocksUpdate) and from a hand-built two-cell body in the client's layout.
    /// </summary>
    public class CellsUpdateLayoutTests
    {
        private static readonly byte[] Opcode = { 0xEC, 0x00, 0x05, 0x00 };

        private static List<TerrainCel2Packer.CellRecord> Walk(byte[] body)
        {
            string file = Path.Combine(Path.GetTempPath(), "lmemu-cellsupdate-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                var bytes = new byte[Opcode.Length + body.Length];
                Opcode.CopyTo(bytes, 0);
                body.CopyTo(bytes, Opcode.Length);
                File.WriteAllBytes(file, bytes);
                return TerrainCel2Packer.ReadCellsUpdateFile(file);
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>The server's block update (version 0) with an empty layer between two data layers.</summary>
        [Fact]
        public void ServerBlocksUpdate_NamedFields()
        {
            byte[] body = TerrainStore.BuildBlocksUpdate(0x2000000400000123ul, new List<(uint, uint, byte[])>
            {
                (0u, 7u, new byte[] { 1, 2, 3 }),
                (1u, 0u, Array.Empty<byte>()),
                (2u, 9u, new byte[] { 4, 5 }),
            });

            List<TerrainCel2Packer.CellRecord> cells = Walk(body);
            TerrainCel2Packer.CellRecord cell = Assert.Single(cells);
            Assert.Equal(0x2000000400000123ul, cell.CellId);
            Assert.Equal(0u, cell.Version);            // block data
            Assert.Equal(3u, cell.SegmentCount);       // layers
            Assert.Equal(0u, cell.FirstLayerId);
            // layer 0: version 7, flag, size 3, data | layer 1 id, version 0, flag, size 0 | layer 2 id, version 9, flag, size 2, data
            Assert.Equal((9 + 3) + (4 + 9) + (4 + 9 + 2), cell.DataBlob.Length);
            Assert.Equal(7u, BitConverter.ToUInt32(cell.DataBlob, 0));
            Assert.Equal(1u, BitConverter.ToUInt32(cell.DataBlob, 12));
            Assert.Equal(2u, BitConverter.ToUInt32(cell.DataBlob, 25));
            Assert.Equal(9u, BitConverter.ToUInt32(cell.DataBlob, 29));
        }

        /// <summary>
        /// Two cells, the first ending in an empty layer (the old walk took 15 bytes for it and lost the second cell),
        /// the second a geometry cell (version 3) with no layer at all.
        /// </summary>
        [Fact]
        public void TwoCells_EmptyLayerAndNoLayer()
        {
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write((ushort)0);        // high half of the sub-opcode
            w.Write(2u);               // cells
            w.Write(0x11ul);
            w.Write(0u);               // version: block data
            w.Write(1u);               // one layer
            w.Write(2u);               // layer id
            w.Write(0u);               // layer version
            w.Write((byte)0);          // flag
            w.Write(0u);               // size
            w.Write(0x22ul);
            w.Write(3u);               // version: geometry
            w.Write(0u);               // no layer
            w.Write(0u);               // transactions
            w.Write(false);
            w.Write(false);
            w.Flush();

            List<TerrainCel2Packer.CellRecord> cells = Walk(stream.ToArray());
            Assert.Equal(2, cells.Count);
            Assert.Equal(0x11ul, cells[0].CellId);
            Assert.Equal(0u, cells[0].Version);
            Assert.Equal(1u, cells[0].SegmentCount);
            Assert.Equal(2u, cells[0].FirstLayerId);
            Assert.Equal(9, cells[0].DataBlob.Length);
            Assert.Equal(0x22ul, cells[1].CellId);
            Assert.Equal(3u, cells[1].Version);
            Assert.Equal(0u, cells[1].SegmentCount);
            Assert.Equal(0u, cells[1].FirstLayerId);
            Assert.Empty(cells[1].DataBlob);
        }

        /// <summary>A CEL2 store written from the records reads back with the same named fields.</summary>
        [Fact]
        public void Cel2_RoundTrip()
        {
            string file = Path.Combine(Path.GetTempPath(), "lmemu-cel2-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                TerrainCel2Packer.WriteCel2(file, new[]
                {
                    new TerrainCel2Packer.CellRecord { CellId = 5, Version = 0, SegmentCount = 2, FirstLayerId = 1, DataBlob = new byte[] { 9, 8 } },
                    new TerrainCel2Packer.CellRecord { CellId = 6, Version = 4, SegmentCount = 0, FirstLayerId = 0, DataBlob = Array.Empty<byte>() },
                });
                Dictionary<ulong, TerrainCel2Packer.CellRecord> read = TerrainCel2Packer.ReadCel2(file);
                Assert.Equal(2, read.Count);
                Assert.Equal(0u, read[5].Version);
                Assert.Equal(2u, read[5].SegmentCount);
                Assert.Equal(1u, read[5].FirstLayerId);
                Assert.Equal(new byte[] { 9, 8 }, read[5].DataBlob);
                Assert.Equal(4u, read[6].Version);
                Assert.Equal(0u, read[6].SegmentCount);
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
