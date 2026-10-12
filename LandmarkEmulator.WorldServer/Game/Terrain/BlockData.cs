using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// A cell's voxels (VoxelFarm CBlockData): 40 × 40 × 40 voxels, each with flags (bit 0 = has a material,
    /// bit 1 = has a surface offset), a u16 material and a 3-byte offset inside the voxel (0.5 = centre).
    /// Clients build the cell mesh from these themselves, which is how edits reach every player
    /// (CellsUpdate entry with version 0 → CellBlocks, client FUN_143f52470).
    /// </summary>
    public class BlockData
    {
        public const int Size = 40;
        public const int Count = Size * Size * Size;
        /// <summary>The offset byte for 0.5 (client DAT_14552de88).</summary>
        public const byte CentreOffset = 128;

        public byte[] Flags { get; } = new byte[Count];
        public ushort[] Materials { get; } = new ushort[Count];
        public byte[] Offsets { get; } = new byte[Count * 3];

        public static int Index(int x, int y, int z) => x + Size * (y + Size * z);

        /// <summary>
        /// Makes a voxel solid with a material and a centred vertex (flags: material + offset).
        /// </summary>
        public void SetSolid(int index, ushort material)
        {
            Flags[index] = 3;
            Materials[index] = material;
            Offsets[index * 3] = Offsets[index * 3 + 1] = Offsets[index * 3 + 2] = CentreOffset;
        }

        /// <summary>
        /// SerializeBlockData (client FUN_143f86db0), format 2: u32 2, 8000-byte occupancy bitmask (MSB first), u32 n +
        /// flags of occupied voxels, bool palette, then either u32 n + u16 materials or (u32 n + u16 palette,
        /// u32 n + u8 indices), then u32 n + 3-byte offsets. Flag extras: 0x40 material 0, 0x80 palette index 1
        /// (not stored), 0x20 centre offset (not stored).
        /// </summary>
        public byte[] Serialize()
        {
            var mask = new byte[Count / 8];
            var flags = new List<byte>();
            var palette = new List<ushort>();
            var paletteIndex = new Dictionary<ushort, int>();
            var indices = new List<byte>();
            var allMaterials = new List<ushort>();
            var offsets = new List<byte>();
            for (int i = 0; i < Count; i++)
            {
                byte flag = Flags[i];
                if (flag == 0)
                    continue;
                mask[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                if ((flag & 1) != 0)
                {
                    ushort material = Materials[i];
                    if (material == 0)
                        flag |= 0x40;
                    else
                    {
                        allMaterials.Add(material);
                        if (!paletteIndex.TryGetValue(material, out int index))
                        {
                            palette.Add(material);
                            paletteIndex[material] = index = palette.Count;
                        }
                        if (index == 1)
                            flag |= 0x80;
                        else
                            indices.Add((byte)index);
                    }
                }
                if ((flag & 2) != 0)
                {
                    byte a = Offsets[i * 3], b = Offsets[i * 3 + 1], c = Offsets[i * 3 + 2];
                    if (a == CentreOffset && b == CentreOffset && c == CentreOffset)
                        flag |= 0x20;
                    else
                    {
                        offsets.Add(a);
                        offsets.Add(b);
                        offsets.Add(c);
                    }
                }
                flags.Add(flag);
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(2u);
            writer.Write(mask);
            writer.Write(flags.Count);
            writer.Write(flags.ToArray());
            bool usePalette = palette.Count < 0x100;
            writer.Write(usePalette);
            if (usePalette)
            {
                writer.Write(palette.Count);
                foreach (ushort material in palette)
                    writer.Write(material);
                writer.Write(indices.Count);
                writer.Write(indices.ToArray());
            }
            else
            {
                writer.Write(allMaterials.Count);
                foreach (ushort material in allMaterials)
                    writer.Write(material);
            }
            writer.Write(offsets.Count);
            writer.Write(offsets.ToArray());
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Reads SerializeBlockData format 2 (the inverse of <see cref="Serialize"/>) starting at <paramref name="offset"/>.
        /// Voxels not in the occupancy mask are left unset (flag 0).
        /// </summary>
        public static BlockData Deserialize(byte[] data, int offset = 0)
        {
            var blocks = new BlockData();
            using var reader = new BinaryReader(new MemoryStream(data, offset, data.Length - offset));
            if (reader.ReadUInt32() != 2)
                throw new InvalidDataException("Block data is not format 2.");
            byte[] mask = reader.ReadBytes(Count / 8);
            byte[] flags = reader.ReadBytes(reader.ReadInt32());
            bool usePalette = reader.ReadBoolean();
            ushort[] palette = null, materials;
            byte[] indices = null;
            if (usePalette)
            {
                palette = new ushort[reader.ReadInt32()];
                for (int i = 0; i < palette.Length; i++)
                    palette[i] = reader.ReadUInt16();
                indices = reader.ReadBytes(reader.ReadInt32());
                materials = null;
            }
            else
            {
                materials = new ushort[reader.ReadInt32()];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = reader.ReadUInt16();
            }
            byte[] offsets = reader.ReadBytes(reader.ReadInt32());
            int f = 0, m = 0, o = 0;
            for (int i = 0; i < Count; i++)
            {
                if ((mask[i >> 3] & (1 << (7 - (i & 7)))) == 0)
                    continue;
                byte flag = flags[f++];
                // A vertex without a material is never kept (the client's mesher hangs on them).
                blocks.Flags[i] = (flag & 1) != 0 ? (byte)(flag & 3) : (byte)0;
                if ((flag & 1) != 0)
                {
                    if ((flag & 0x40) != 0)
                        blocks.Materials[i] = 0;
                    else if (usePalette)
                        blocks.Materials[i] = (flag & 0x80) != 0 ? palette[0] : palette[indices[m++] - 1];
                    else
                        blocks.Materials[i] = materials[m++];
                }
                if ((flag & 2) != 0 && (flag & 0x20) == 0)
                {
                    blocks.Offsets[i * 3] = offsets[o++];
                    blocks.Offsets[i * 3 + 1] = offsets[o++];
                    blocks.Offsets[i * 3 + 2] = offsets[o++];
                }
                else
                    blocks.Offsets[i * 3] = blocks.Offsets[i * 3 + 1] = blocks.Offsets[i * 3 + 2] = CentreOffset;
            }
            return blocks;
        }

        /// <summary>Compact storage form for the server's own files: zlib of flags, materials, offsets.</summary>
        public byte[] Save() => Save(System.IO.Compression.CompressionLevel.Optimal);

        /// <summary><see cref="Save"/> at a chosen compression level (Fastest for the in-memory compact copies).</summary>
        public byte[] Save(System.IO.Compression.CompressionLevel level)
        {
            using var output = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(output, level, true))
            {
                zlib.Write(Flags);
                zlib.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Materials.AsSpan()));
                zlib.Write(Offsets);
            }
            return output.ToArray();
        }

        /// <summary>
        /// Restores a cell saved by <see cref="Save"/> (zlib of the flag, material and offset arrays).
        /// </summary>
        public void Load(byte[] saved)
        {
            using var input = new System.IO.Compression.ZLibStream(new MemoryStream(saved), System.IO.Compression.CompressionMode.Decompress);
            input.ReadExactly(Flags);
            input.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(Materials.AsSpan()));
            input.ReadExactly(Offsets);
        }

        /// <summary>A CellsUpdate block layer blob: the client codec (A1B2C3D4, BE size, zlib) around Serialize().</summary>
        public byte[] ToLayerBlob() => TerrainStore.Pack(Serialize());

        /// <summary>A copy holding only the carved voxels (material 0); null when there are none.</summary>
        public BlockData AirOnly()
        {
            BlockData air = null;
            for (int i = 0; i < Count; i++)
            {
                if ((Flags[i] & 1) == 0 || Materials[i] != 0)
                    continue;
                air ??= new BlockData();
                air.Flags[i] = Flags[i];
                air.Offsets[i * 3] = Offsets[i * 3];
                air.Offsets[i * 3 + 1] = Offsets[i * 3 + 1];
                air.Offsets[i * 3 + 2] = Offsets[i * 3 + 2];
            }
            return air;
        }
    }
}
