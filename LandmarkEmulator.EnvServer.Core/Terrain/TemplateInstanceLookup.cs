using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Placed Player Studio designs ("template instances", the world's Game.TemplateInstances) as the client's terrain
    /// knows them: VoxelFarm's VoxelTemplateLookup (AppCommon\VoxelFarmWrapper\VoxelTemplateLookup.cpp), one per layer
    /// of a cell, which says which design instance each voxel belongs to. Voxel Vision's "Templates" list comes from it.
    /// <para>
    /// Client flow (0.695) [P]: the Voxel Vision widget (VoxelVisionWidget.as initialize) calls Lua
    /// VoxelOwnership.RequestAllCellsForClaim(site) (FUN_1435ff2e0). ClientTemplateManager::
    /// PopulateClaimInstanceDataToOwnershipDS (FUN_143538f90) collects the instance guids of the site's cells
    /// (FUN_14310f910 → FUN_143f93160 → FUN_144015f40: layer 1's lookup, FUN_14406bdb0 lists the instances with a voxel
    /// inside the site) and asks TemplatePacketClientGetInstanceDetailsRequest (0xF0 sub 0x12, FUN_1431138e0) for each one
    /// it has no details of; the answer 0x13 (Game.TemplateInstances.SendDetails) fills the "Templates" rows (FUN_1435382e0:
    /// one row per design, one "name #n" row per instance).
    /// </para>
    /// <para>
    /// Where the client gets the lookup [P]: nothing in the client adds an instance (AddTemplateInstance FUN_144068670 is
    /// reached only from FUN_144068560 / FUN_14406d0c0, which nothing calls or points to: live's Environment Server ran
    /// that code). The client reads it from the cell data: CellBlocks decompress (FUN_144049ef0, FUN_144049d20) reads
    /// SerializeBlockData and then, when bytes are left in the decompressed layer, the lookup (FUN_14406dca0): varint n +
    /// n bytes, parsed on first use (FUN_14406dd80): u8 (skipped), u32 count, count × entry (FUN_144067060; its writer
    /// FUN_144066d30, the whole blob FUN_14406c0a0):
    /// <list type="bullet">
    /// <item>u64 instance guid; u8 the voxel flag bits 2-3 its voxels carry (flags &amp; 0xC, 0 for ours; AddTemplateInstance
    /// writes 0);</item>
    /// <item>u16 box min, u16 box max, each a voxel index y + 40·(x + 40·z) inside the cell;</item>
    /// <item>signed varint k (FUN_1440669b0 / FUN_142c0bc30: bit 0 sign, bits 1-2 extra byte count, value &gt;&gt; 3):
    /// |k| - 1 node weights follow; k &lt; 0 = a bit set over the box comes first, one bit per voxel, index
    /// ((z - minZ)·dimX + (x - minX))·dimY + (y - minY), MSB first, (volume + 7) / 8 bytes (FUN_14406dbc0 tests it);</item>
    /// <item>(|k| - 1) × varint node weight (the overlap tie-break of FUN_14406c2b0; none here).</item>
    /// </list>
    /// A voxel belongs to an instance when its flags &amp; 0xC equal the entry's flag and the instance's bit is set
    /// (FUN_14406c2b0 / FUN_1440694f0).
    /// </para>
    /// <para>
    /// [I] Which voxels: live's server kept the paste's own voxels. Here an instance's voxels are those inside its box
    /// that its placer still owns (Voxel Vision's ownership grid, <see cref="VoxelOwnership"/>): voxels later removed
    /// or rebuilt by someone else drop out on their own, and nothing extra has to be stored.
    /// </para>
    /// </summary>
    public static class TemplateInstanceLookup
    {
        /// <summary>One placed design: guid (0x13's instance guid), placer and box (global dm, as build sites).</summary>
        public readonly record struct Instance(ulong Guid, ulong Placer, VoxelBox Box);

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Dictionary<ulong, Instance>> byLandscape = new();

        private static uint Landscape(uint zone) => zone == 0 ? TerrainStore.HomeContinent : zone;

        /// <summary>
        /// Replaces a landscape's placed designs with <paramref name="instances"/>. Returns the level-2 cells whose layer-1
        /// lookup really changed (<see cref="BlobFor"/> before and after, <paramref name="owned"/> as there): the caller bumps
        /// those layers' versions and sends the cells again. Only those: bumping every cell of a box gave cells next to a
        /// new placement a version nobody was told about, so the owner's next brush edit there came back stale
        /// (trace/0695/phaseE4-logic.md F10). With <paramref name="announce"/> false (start-up, a reconnect: the clients'
        /// copies were built from the same records) nothing is returned.
        /// </summary>
        public static List<ulong> Replace(uint zone, IEnumerable<Instance> instances, Func<ulong, ulong, ISet<int>> owned = null,
            bool announce = true)
        {
            var fresh = instances.GroupBy(i => i.Guid).ToDictionary(g => g.Key, g => g.First());
            HashSet<ulong> touched = Touched(zone, fresh);
            Dictionary<ulong, byte[]> before = owned == null ? null : touched.ToDictionary(c => c, c => BlobFor(zone, c, owned));
            lock (sync)
            {
                if (fresh.Count == 0)
                    byLandscape.Remove(Landscape(zone));
                else
                    byLandscape[Landscape(zone)] = fresh;
            }
            if (!announce)
                return new List<ulong>();
            if (before == null)
                return touched.ToList();
            return touched.Where(c => !(BlobFor(zone, c, owned) ?? Array.Empty<byte>()).AsSpan()
                .SequenceEqual(before[c] ?? Array.Empty<byte>())).ToList();
        }

        /// <summary>The level-2 cells of every instance added, removed or moved by replacing the landscape's set with <paramref name="fresh"/>.</summary>
        private static HashSet<ulong> Touched(uint zone, Dictionary<ulong, Instance> fresh)
        {
            var touched = new HashSet<ulong>();
            lock (sync)
            {
                byLandscape.TryGetValue(Landscape(zone), out var old);
                old ??= new Dictionary<ulong, Instance>();
                foreach ((ulong guid, Instance instance) in old)
                    if (!fresh.TryGetValue(guid, out Instance now) || now != instance)
                        touched.UnionWith(CellsOf(instance.Box));
                foreach ((ulong guid, Instance instance) in fresh)
                    if (!old.TryGetValue(guid, out Instance was) || was != instance)
                        touched.UnionWith(CellsOf(instance.Box));
            }
            return touched;
        }

        /// <summary>The placed designs of a landscape (tests, dev).</summary>
        public static List<Instance> Of(uint zone)
        {
            lock (sync)
                return byLandscape.TryGetValue(Landscape(zone), out var map) ? map.Values.ToList() : new List<Instance>();
        }

        /// <summary>The level-2 cells a global dm box touches (voxel centres inside, as <see cref="VoxelBox.HoldsVoxel"/>).</summary>
        public static IEnumerable<ulong> CellsOf(VoxelBox box)
        {
            (long minX, long maxX) = VoxelRange(box.MinX, box.MaxX);
            (long minY, long maxY) = VoxelRange(box.MinY, box.MaxY);
            (long minZ, long maxZ) = VoxelRange(box.MinZ, box.MaxZ);
            if (minX > maxX || minY > maxY || minZ > maxZ)
                yield break;
            for (long cz = minZ / BlockData.Size; cz <= maxZ / BlockData.Size; cz++)
                for (long cx = minX / BlockData.Size; cx <= maxX / BlockData.Size; cx++)
                    for (long cy = minY / BlockData.Size; cy <= maxY / BlockData.Size; cy++)
                        yield return TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
        }

        /// <summary>Is any voxel centre of the level-2 <paramref name="cell"/> inside the box (the test <see cref="CellsOf"/> lists by)?</summary>
        public static bool Touches(VoxelBox box, ulong cell)
        {
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
            static bool Overlaps((long Min, long Max) range, uint c)
                => range.Min <= range.Max && range.Min <= (c + 1L) * BlockData.Size - 1 && range.Max >= (long)c * BlockData.Size;
            return Overlaps(VoxelRange(box.MinX, box.MaxX), cx) && Overlaps(VoxelRange(box.MinY, box.MaxY), cy)
                && Overlaps(VoxelRange(box.MinZ, box.MaxZ), cz);
        }

        /// <summary>
        /// A box as the client's voxel region (0x13's instance region, FUN_1430ff0c0; expanded to cells by FUN_143c18c60):
        /// the level-2 cell holding the first voxel, that voxel's x / y / z inside the cell, and the size in voxels.
        /// </summary>
        public static (ulong Cell, uint X, uint Y, uint Z, uint SizeX, uint SizeY, uint SizeZ) Region(VoxelBox box)
        {
            (long minX, long maxX) = VoxelRange(box.MinX, box.MaxX);
            (long minY, long maxY) = VoxelRange(box.MinY, box.MaxY);
            (long minZ, long maxZ) = VoxelRange(box.MinZ, box.MaxZ);
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(minX / BlockData.Size), (uint)(minY / BlockData.Size), (uint)(minZ / BlockData.Size));
            return (cell, (uint)(minX % BlockData.Size), (uint)(minY % BlockData.Size), (uint)(minZ % BlockData.Size),
                (uint)Math.Max(0, maxX - minX + 1), (uint)Math.Max(0, maxY - minY + 1), (uint)Math.Max(0, maxZ - minZ + 1));
        }

        /// <summary>The global voxel range whose centres lie in [min, max] dm (empty when max &lt; min).</summary>
        private static (long Min, long Max) VoxelRange(double min, double max)
            => ((long)Math.Max(0, Math.Ceiling(min / TerrainEdits.VoxelSize - 0.5)),
                (long)Math.Floor(max / TerrainEdits.VoxelSize - 0.5));

        /// <summary>
        /// The lookup bytes for layer 1 of a cell (what follows SerializeBlockData inside the layer blob: varint n + n
        /// bytes), or null when no placed design has a voxel in it. <paramref name="owned"/> gives a placer's voxels in
        /// the cell (voxel indices; <see cref="VoxelOwnership"/>).
        /// </summary>
        public static byte[] BlobFor(uint zone, ulong cell, Func<ulong, ulong, ISet<int>> owned)
        {
            List<Instance> here;
            lock (sync)
            {
                if (!byLandscape.TryGetValue(Landscape(zone), out var map) || map.Count == 0)
                    return null;
                here = map.Values.Where(i => Touches(i.Box, cell)).ToList();
            }
            var entries = new List<(ulong Guid, int[] Min, int[] Max, bool[] Bits)>();
            foreach (Instance instance in here.OrderBy(i => i.Guid))
            {
                ISet<int> mine = owned(cell, instance.Placer);
                if (mine == null || mine.Count == 0)
                    continue;
                var voxels = mine.Where(i => instance.Box.HoldsVoxel(cell, i)).ToList();
                if (voxels.Count == 0)
                    continue;
                int[] min = { int.MaxValue, int.MaxValue, int.MaxValue }, max = { -1, -1, -1 };
                foreach (int index in voxels)
                {
                    (int x, int y, int z) = XYZ(index);
                    min[0] = Math.Min(min[0], x); min[1] = Math.Min(min[1], y); min[2] = Math.Min(min[2], z);
                    max[0] = Math.Max(max[0], x); max[1] = Math.Max(max[1], y); max[2] = Math.Max(max[2], z);
                }
                int dimX = max[0] - min[0] + 1, dimY = max[1] - min[1] + 1, dimZ = max[2] - min[2] + 1;
                var bits = new bool[dimX * dimY * dimZ];
                foreach (int index in voxels)
                {
                    (int x, int y, int z) = XYZ(index);
                    bits[((z - min[2]) * dimX + (x - min[0])) * dimY + (y - min[1])] = true;
                }
                entries.Add((instance.Guid, min, max, bits));
            }
            if (entries.Count == 0)
                return null;
            var inner = new List<byte> { 0 };
            inner.AddRange(BitConverter.GetBytes((uint)entries.Count));
            foreach ((ulong guid, int[] min, int[] max, bool[] bits) in entries)
            {
                inner.AddRange(BitConverter.GetBytes(guid));
                inner.Add(0);   // flags & 0xC of its voxels
                inner.AddRange(BitConverter.GetBytes((ushort)TerrainEdits.VoxelIndex(min[0], min[1], min[2])));
                inner.AddRange(BitConverter.GetBytes((ushort)TerrainEdits.VoxelIndex(max[0], max[1], max[2])));
                WriteSignedVarint(inner, -1);   // bit set, no node weights
                var packed = new byte[(bits.Length + 7) / 8];
                for (int i = 0; i < bits.Length; i++)
                    if (bits[i])
                        packed[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                inner.AddRange(packed);
            }
            var blob = new List<byte>();
            WriteUnsignedVarint(blob, (uint)inner.Count);
            blob.AddRange(inner);
            return blob.ToArray();
        }

        private static (int X, int Y, int Z) XYZ(int index)
            => (index / BlockData.Size % BlockData.Size, index % BlockData.Size, index / (BlockData.Size * BlockData.Size));

        /// <summary>SoeUtil packed u32 (reader FUN_142c0bd80): value &lt;&lt; 2 | extra byte count, little endian.</summary>
        public static void WriteUnsignedVarint(List<byte> output, uint value)
        {
            if (value >= 1u << 30)
                throw new ArgumentOutOfRangeException(nameof(value));
            uint v = value << 2;
            int extra = v > 0xFFFFFF ? 3 : v > 0xFFFF ? 2 : v > 0xFF ? 1 : 0;
            v |= (uint)extra;
            for (int i = 0; i <= extra; i++)
                output.Add((byte)(v >> (8 * i)));
        }

        /// <summary>
        /// SoeUtil packed signed int (writer FUN_1440669b0, reader FUN_142c0bc30): |value| &lt;&lt; 3 | sign bit 0 |
        /// extra byte count &lt;&lt; 1, little endian.
        /// </summary>
        public static void WriteSignedVarint(List<byte> output, int value)
        {
            uint v = value < 0 ? ((uint)-value << 3) | 1 : (uint)value << 3;
            int extra = (v >> 24) != 0 ? 3 : (v >> 16) != 0 ? 2 : (v >> 8) != 0 ? 1 : 0;
            v |= (uint)extra << 1;
            for (int i = 0; i <= extra; i++)
                output.Add((byte)(v >> (8 * i)));
        }
    }
}
