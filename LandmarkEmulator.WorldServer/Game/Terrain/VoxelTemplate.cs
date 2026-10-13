using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// A voxel template from the client's CommonData/Voxel/templates/*.otd (read-only): prefab structures such as the
    /// Spire's base (base_portal_spire.otd), castles, floating islands, building platforms. Format (reader FUN_143470510,
    /// version 6), little-endian:
    /// - u8 version;
    /// - u32 n × cell {u64 cell id (level 2, x/y/z relative to the template), u64 the same id, u32 type (4), u32 length,
    ///   blob} (FUN_143471930); a type 2 or 3 cell has one more u32. The blob is the block codec (A1 B2 C3 D4, BE size,
    ///   zlib) around an 8000-byte changed mask (MSB first) + SerializeBlockData format 2: the same as an ApplyDiff cell;
    /// - u32 ×3 size in voxels (FUN_143471540);
    /// - u32 n × {u16 material, u32} (FUN_143471460);
    /// - varint n (FUN_14346f8f0: bit 0 sign, bits 1-2 extra byte count, value &gt;&gt; 3) × prop {u64 id, u32 prop
    ///   definition, u32 model, f32 ×3 position (metres from the template's corner), f32 ×4 rotation, f32 ×3 scale,
    ///   u64, u64, u32, varint n × 16 bytes, varint n × 20 bytes {u32, u32, u64 linked prop, u32}};
    /// - version-dependent trailers (not needed).
    /// </summary>
    public sealed class VoxelTemplate
    {
        public sealed record Cell(int X, int Y, int Z, byte[] Changed, BlockData Blocks);

        /// <summary>The client's template folder (GameDirectory/CommonData/Voxel/templates), set at start-up.</summary>
        public static string Folder { get; set; } = "";

        public sealed record Prop(ulong Id, uint PropId, uint ModelId, Vector3 Position, Quaternion Rotation, Vector3 Scale);

        public List<Cell> Cells { get; } = new();
        /// <summary>The cell section exactly as stored (u32 n + cell records): the block list of a design (Templates).</summary>
        public byte[] RawCells { get; private set; }
        /// <summary>Solid voxels per material ({u16 material, u32 count}), as a design's material map.</summary>
        public List<(ushort Material, uint Count)> Materials { get; } = new();
        public List<Prop> Props { get; } = new();
        public (uint X, uint Y, uint Z) Size { get; private set; }

        public static VoxelTemplate Load(string path)
        {
            var template = new VoxelTemplate();
            using var reader = new BinaryReader(File.OpenRead(path));
            byte version = reader.ReadByte();
            long cellsStart = reader.BaseStream.Position;
            uint cells = reader.ReadUInt32();
            for (uint i = 0; i < cells; i++)
            {
                ulong id = reader.ReadUInt64();
                reader.ReadUInt64();
                uint type = reader.ReadUInt32();
                byte[] blob = reader.ReadBytes(reader.ReadInt32());
                if (type is 2 or 3)
                    reader.ReadUInt32();
                byte[] raw = TerrainStore.Unpack(blob, 0, blob.Length);
                byte[] changed = raw[..(BlockData.Count / 8)];
                BlockData blocks = BlockData.Deserialize(raw, BlockData.Count / 8);
                (_, uint x, uint y, uint z) = TerrainStore.Decode(id);
                template.Cells.Add(new Cell((int)x, (int)y, (int)z, changed, blocks));
            }
            long cellsEnd = reader.BaseStream.Position;
            reader.BaseStream.Position = cellsStart;
            template.RawCells = reader.ReadBytes((int)(cellsEnd - cellsStart));
            template.Size = (reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            uint materials = reader.ReadUInt32();
            for (uint i = 0; i < materials; i++)
                template.Materials.Add((reader.ReadUInt16(), reader.ReadUInt32()));
            int props = ReadVarint(reader);
            for (int i = 0; i < props; i++)
            {
                ulong id = reader.ReadUInt64();
                uint propId = reader.ReadUInt32(), model = reader.ReadUInt32();
                var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var scale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadUInt32();
                // Version 2 (the cavern ruins / Outposts, most older templates) has one more byte here: measured 23
                // bytes from the scale to the next prop instead of 22 (re/tools/otd.py parses every template with it).
                if (version < 3)
                    reader.ReadByte();
                reader.ReadBytes(ReadVarint(reader) * 16);
                reader.ReadBytes(ReadVarint(reader) * (version < 3 ? 24 : 20));
                template.Props.Add(new Prop(id, propId, model, position, rotation, scale));
            }
            return template;
        }

        /// <summary>FUN_14346f8f0: low bit = sign, bits 1-2 = extra bytes (little-endian), value = all bits &gt;&gt; 3.</summary>
        private static int ReadVarint(BinaryReader reader)
        {
            byte first = reader.ReadByte();
            uint value = first;
            int extra = (first >> 1) & 3;
            for (int i = 0; i < extra; i++)
                value |= (uint)reader.ReadByte() << (8 * (i + 1));
            int result = (int)(value >> 3);
            return (first & 1) != 0 ? -result : result;
        }

        /// <summary>
        /// Writes the template into the terrain with its corner at a level-2 cell (global cell coordinates): every cell's
        /// changed voxels replace the overlay there. Returns the cells changed (to broadcast).
        /// </summary>
        public List<ulong> Stamp(uint cellX, uint cellY, uint cellZ, uint zone)
        {
            var changed = new List<ulong>();
            foreach (Cell cell in Cells)
            {
                ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)(cellX + cell.X), (uint)(cellY + cell.Y), (uint)(cellZ + cell.Z));
                if (TerrainEdits.ApplyDiff(id, cell.Changed, cell.Blocks, (_, _, _) => true, out int voxels, zone, 0, TerrainEdits.PoiLayer) && voxels > 0)
                    changed.Add(id);
            }
            return changed;
        }
    }
}
