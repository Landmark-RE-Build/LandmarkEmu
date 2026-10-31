using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Placed Player Studio designs in the client's terrain (Voxel Vision's "Templates", OPEN-LEADS NL2): the
    /// VoxelTemplateLookup bytes after layer 1's block data and the 0xF0/0x13 details, read back the way the 0.695 client
    /// reads them (FUN_14406dca0 → FUN_14406dd80 → FUN_144067400 → FUN_144067060; FUN_14310bbf0 case 0x11).
    /// </summary>
    public class TemplateInstanceLookupTests
    {
        private const uint Zone = 934;   // a landscape no other test uses

        /// <summary>SoeUtil packed u32 as FUN_142c0bd80 reads it.</summary>
        private static uint ReadUnsigned(BinaryReader r)
        {
            byte first = r.ReadByte();
            uint v = first;
            for (int i = 1; i <= (first & 3); i++)
                v |= (uint)r.ReadByte() << (8 * i);
            return v >> 2;
        }

        /// <summary>SoeUtil packed signed int as FUN_142c0bc30 reads it.</summary>
        private static int ReadSigned(BinaryReader r)
        {
            byte first = r.ReadByte();
            uint v = first;
            for (int i = 1; i <= ((first >> 1) & 3); i++)
                v |= (uint)r.ReadByte() << (8 * i);
            int magnitude = (int)(v >> 3);
            return (v & 1) != 0 ? -magnitude : magnitude;
        }

        private sealed record Entry(ulong Guid, byte Flag, (int X, int Y, int Z) Min, (int X, int Y, int Z) Max, bool[] Bits, uint[] Weights)
        {
            public bool Holds(int x, int y, int z)
            {
                if (x < Min.X || x > Max.X || y < Min.Y || y > Max.Y || z < Min.Z || z > Max.Z || Bits == null)
                    return false;
                int dimX = Max.X - Min.X + 1, dimY = Max.Y - Min.Y + 1;
                return Bits[((z - Min.Z) * dimX + (x - Min.X)) * dimY + (y - Min.Y)];
            }
        }

        private static (int X, int Y, int Z) Unpack(ushort v) => (v % 1600 / 40, v % 40, v / 1600);

        /// <summary>The lookup as the client parses it, with every byte used.</summary>
        private static List<Entry> Parse(byte[] blob)
        {
            using var r = new BinaryReader(new MemoryStream(blob));
            uint n = ReadUnsigned(r);
            Assert.Equal(blob.Length - r.BaseStream.Position, n);
            r.ReadByte();   // FUN_14406dd80 skips one byte
            uint count = r.ReadUInt32();
            var entries = new List<Entry>();
            for (uint i = 0; i < count; i++)
            {
                ulong guid = r.ReadUInt64();
                byte flag = r.ReadByte();
                var min = Unpack(r.ReadUInt16());
                var max = Unpack(r.ReadUInt16());
                int k = ReadSigned(r);
                bool[] bits = null;
                if (k < 0)
                {
                    int volume = (max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
                    byte[] packed = r.ReadBytes((volume + 7) / 8);
                    bits = Enumerable.Range(0, volume).Select(b => (packed[b >> 3] & (1 << (7 - (b & 7)))) != 0).ToArray();
                }
                var weights = new uint[Math.Abs(k) - 1];
                for (int w = 0; w < weights.Length; w++)
                    weights[w] = ReadUnsigned(r);
                entries.Add(new Entry(guid, flag, min, max, bits, weights));
            }
            Assert.Equal(r.BaseStream.Length, r.BaseStream.Position);
            return entries;
        }

        [Fact]
        public void Varints_MatchTheClientCodec()
        {
            foreach (uint value in new uint[] { 0, 5, 63, 64, 300, 16383, 16384, 1 << 22, (1 << 30) - 1 })
            {
                var bytes = new List<byte>();
                TemplateInstanceLookup.WriteUnsignedVarint(bytes, value);
                Assert.Equal(value, ReadUnsigned(new BinaryReader(new MemoryStream(bytes.ToArray()))));
            }
            foreach (int value in new[] { 0, 1, -1, 31, -32, 40, -4000, 1 << 20, -(1 << 25) })
            {
                var bytes = new List<byte>();
                TemplateInstanceLookup.WriteSignedVarint(bytes, value);
                Assert.Equal(value, ReadSigned(new BinaryReader(new MemoryStream(bytes.ToArray()))));
            }
            var minusOne = new List<byte>();
            TemplateInstanceLookup.WriteSignedVarint(minusOne, -1);
            Assert.Equal(new byte[] { 0x09 }, minusOne.ToArray());   // FUN_1440669b0: 1 · 8 | sign, one byte
        }

        [Fact]
        public void Lookup_HoldsThePlacersVoxelsInsideTheBox()
        {
            // A box over voxels 2..5 (x), 0..3 (y), 1..2 (z) of cell (100, 10, 200), in global dm.
            const uint cx = 100, cy = 10, cz = 200;
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, cx, cy, cz);
            double v = TerrainEdits.VoxelSize;
            var box = new VoxelBox((cx * 40 + 2) * v, (cy * 40 + 0) * v, (cz * 40 + 1) * v,
                (cx * 40 + 6) * v - 0.01, (cy * 40 + 4) * v - 0.01, (cz * 40 + 3) * v - 0.01);
            const ulong placer = 77, other = 88, guid = 0x7C00_0000_0000_1234;
            TemplateInstanceLookup.Replace(Zone, new[] { new TemplateInstanceLookup.Instance(guid, placer, box) });
            try
            {
                // The placer owns three voxels in the box and one outside it; someone else owns one in the box.
                var mine = new HashSet<int> { TerrainEdits.VoxelIndex(2, 0, 1), TerrainEdits.VoxelIndex(5, 3, 2), TerrainEdits.VoxelIndex(3, 1, 1),
                    TerrainEdits.VoxelIndex(20, 20, 20) };
                byte[] blob = TemplateInstanceLookup.BlobFor(Zone, cell, (c, owner) => owner == placer ? mine : new HashSet<int>());
                Entry entry = Assert.Single(Parse(blob));
                Assert.Equal(guid, entry.Guid);
                Assert.Equal(0, entry.Flag);
                Assert.Equal((2, 0, 1), entry.Min);
                Assert.Equal((5, 3, 2), entry.Max);
                Assert.Empty(entry.Weights);
                Assert.True(entry.Holds(2, 0, 1));
                Assert.True(entry.Holds(5, 3, 2));
                Assert.True(entry.Holds(3, 1, 1));
                Assert.False(entry.Holds(4, 1, 1));
                Assert.False(entry.Holds(20, 20, 20));
                Assert.Equal(3, entry.Bits.Count(b => b));
                // No voxel of the placer's left: no lookup at all (the layer blob stays plain block data).
                Assert.Null(TemplateInstanceLookup.BlobFor(Zone, cell, (c, owner) => owner == other ? mine : null));
                // A cell the box does not reach.
                Assert.Null(TemplateInstanceLookup.BlobFor(Zone, TerrainStore.CellId(TerrainEdits.Level, cx + 1, cy, cz), (c, owner) => mine));
            }
            finally
            {
                TemplateInstanceLookup.Replace(Zone, Array.Empty<TemplateInstanceLookup.Instance>());
            }
        }

        [Fact]
        public void Replace_ReturnsTheCellsOfChangedInstancesOnly()
        {
            double v = TerrainEdits.VoxelSize;
            // Two cells wide in x (voxels 30..49 of row 0).
            var wide = new VoxelBox(30 * v, 0, 0, 50 * v - 0.01, 4 * v, 4 * v);
            var a = new TemplateInstanceLookup.Instance(1, 5, wide);
            var b = new TemplateInstanceLookup.Instance(2, 5, new VoxelBox(0, 0, 0, 4 * v, 4 * v, 4 * v));
            try
            {
                List<ulong> first = TemplateInstanceLookup.Replace(Zone, new[] { a });
                Assert.Equal(new List<ulong> { TerrainStore.CellId(2, 0, 0, 0), TerrainStore.CellId(2, 1, 0, 0) }.OrderBy(c => c).ToList(), first.OrderBy(c => c).ToList());
                Assert.True(TemplateInstanceLookup.Touches(wide, TerrainStore.CellId(2, 1, 0, 0)));
                Assert.False(TemplateInstanceLookup.Touches(wide, TerrainStore.CellId(2, 2, 0, 0)));
                // Unchanged a, new b: only b's cell.
                List<ulong> second = TemplateInstanceLookup.Replace(Zone, new[] { a, b });
                Assert.Equal(new List<ulong> { TerrainStore.CellId(2, 0, 0, 0) }, second);
                Assert.Equal(2, TemplateInstanceLookup.Of(Zone).Count);
                // Both gone.
                Assert.Equal(2, TemplateInstanceLookup.Replace(Zone, Array.Empty<TemplateInstanceLookup.Instance>()).Count);
                Assert.Empty(TemplateInstanceLookup.Of(Zone));
            }
            finally
            {
                TemplateInstanceLookup.Replace(Zone, Array.Empty<TemplateInstanceLookup.Instance>());
            }
        }

        [Fact]
        public void Replace_WithOwnership_ReturnsOnlyCellsWhoseLookupChanged()
        {
            double v = TerrainEdits.VoxelSize;
            // Two cells wide; the placer owns voxels in the first cell only.
            var wide = new VoxelBox(30 * v, 0, 0, 50 * v - 0.01, 4 * v, 4 * v);
            ulong first = TerrainStore.CellId(2, 0, 0, 0);
            var mine = new HashSet<int> { TerrainEdits.VoxelIndex(35, 1, 1) };
            ISet<int> Owned(ulong cell, ulong placer) => cell == first && placer == 5 ? mine : null;
            var instance = new TemplateInstanceLookup.Instance(1, 5, wide);
            try
            {
                Assert.Equal(new List<ulong> { first }, TemplateInstanceLookup.Replace(Zone, new[] { instance }, Owned));
                // The same set again, or at start-up: nothing to announce.
                Assert.Empty(TemplateInstanceLookup.Replace(Zone, new[] { instance }, Owned));
                Assert.Empty(TemplateInstanceLookup.Replace(Zone, Array.Empty<TemplateInstanceLookup.Instance>(), Owned, announce: false));
                Assert.Empty(TemplateInstanceLookup.Of(Zone));
            }
            finally
            {
                TemplateInstanceLookup.Replace(Zone, Array.Empty<TemplateInstanceLookup.Instance>());
            }
        }

        [Fact]
        public void Region_IsTheFirstVoxelsCellOffsetAndSize()
        {
            double v = TerrainEdits.VoxelSize;
            // Voxels 45..84 in x (cell 1, offset 5, 40 wide), 2..3 in y, 0..0 in z.
            var box = new VoxelBox(45 * v, 2 * v, 0, 85 * v - 0.01, 4 * v - 0.01, 1 * v - 0.01);
            (ulong cell, uint x, uint y, uint z, uint sx, uint sy, uint sz) = TemplateInstanceLookup.Region(box);
            Assert.Equal(TerrainStore.CellId(2, 1, 0, 0), cell);
            Assert.Equal((5u, 2u, 0u), (x, y, z));
            Assert.Equal((40u, 2u, 1u), (sx, sy, sz));
        }

        [Fact]
        public void Details_FollowTheClientReader()
        {
            double v = TerrainEdits.VoxelSize;
            var row = new SiteTemplateInstanceModel
            {
                ZoneId = Zone, SiteGuid = 9, TemplateGuid = 0xABCDEF, PlacedBy = 0x1234,
                MinX = 0, MinY = 0, MinZ = 0, MaxX = 50 * v - 0.01, MaxY = 4 * v, MaxZ = 4 * v,
                PlacedAt = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc)
            };
            // TemplateBasicData stands in as just its first field, the template guid (the real one reads the design store).
            byte[] body = TemplateInstances.DetailsBody(row, w => w.Write(row.TemplateGuid));
            using var r = new BinaryReader(new MemoryStream(body));
            Assert.Equal(TemplateInstances.DetailsResponseSub, r.ReadByte());
            r.ReadUInt64();                                         // +0x18
            Assert.Equal(TemplateInstances.GuidOf(row), r.ReadUInt64());   // instance guid
            Assert.Equal(0xABCDEFul, r.ReadUInt64());               // template guid (FUN_1431109c0's key)
            Assert.Equal(0x1234ul, r.ReadUInt64());
            Assert.Equal(TerrainStore.CellId(2, 0, 0, 0), r.ReadUInt64());   // region (FUN_1430ff0c0)
            Assert.Equal(new uint[] { 0, 0, 0, 50, 4, 4 }, Enumerable.Range(0, 6).Select(_ => r.ReadUInt32()).ToArray());
            r.ReadUInt32();
            Assert.Equal((ulong)new DateTimeOffset(row.PlacedAt).ToUnixTimeSeconds(), r.ReadUInt64());
            Assert.Equal(0u, r.ReadUInt32());                       // string ""
            Assert.Equal(0, r.ReadByte());                          // bool
            // TemplateBasicData starts with the template guid.
            Assert.Equal(0xABCDEFul, r.ReadUInt64());
            // The cell list and the empty prop list end the packet.
            r.BaseStream.Position = body.Length - 4 - 8 * 2 - 4;
            Assert.Equal(2u, r.ReadUInt32());
            Assert.Equal(TerrainStore.CellId(2, 0, 0, 0), r.ReadUInt64());
            Assert.Equal(TerrainStore.CellId(2, 1, 0, 0), r.ReadUInt64());
            Assert.Equal(0u, r.ReadUInt32());
            Assert.Equal(TemplateInstances.GuidOf(row), TemplateInstances.GuidOf(row));
            Assert.Equal(0x7C, (int)(TemplateInstances.GuidOf(row) >> 56));
        }
    }
}
