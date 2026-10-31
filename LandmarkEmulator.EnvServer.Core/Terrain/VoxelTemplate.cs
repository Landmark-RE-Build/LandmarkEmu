using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Template material for air enclosed by the build (see <see cref="Stamp"/>).</summary>
        public const ushort InteriorAir = 0xFFFC;

        public sealed record Prop(ulong Id, uint PropId, uint ModelId, Vector3 Position, Quaternion Rotation, Vector3 Scale);

        public List<Cell> Cells { get; } = new();
        /// <summary>The cell section exactly as stored (u32 n + cell records): the block list of a design (Templates).</summary>
        public byte[] RawCells { get; private set; }
        /// <summary>Solid voxels per material ({u16 material, u32 count}), as a design's material map.</summary>
        public List<(ushort Material, uint Count)> Materials { get; } = new();
        public List<Prop> Props { get; } = new();
        public (uint X, uint Y, uint Z) Size { get; private set; }

        /// <summary>A template's size in voxels without reading its cells (they take ~0.4 MB each once decoded).</summary>
        public static (uint X, uint Y, uint Z) SizeOf(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            reader.ReadByte();
            uint cells = reader.ReadUInt32();
            for (uint i = 0; i < cells; i++)
            {
                reader.BaseStream.Seek(16, SeekOrigin.Current);
                uint type = reader.ReadUInt32();
                reader.BaseStream.Seek(reader.ReadInt32() + (type is 2 or 3 ? 4 : 0), SeekOrigin.Current);
            }
            return (reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        }

        /// <summary>
        /// The template's cell positions (x, y, z in cells from its corner) without decoding a cell: what a stamp covers,
        /// for marking a stamped template's cells permanent again after a restart (Outposts on the other landscapes).
        /// </summary>
        public static List<(int X, int Y, int Z)> CellPositions(string path)
        {
            var cells = new List<(int, int, int)>();
            using var reader = new BinaryReader(File.OpenRead(path));
            reader.ReadByte();
            uint n = reader.ReadUInt32();
            for (uint i = 0; i < n; i++)
            {
                ulong id = reader.ReadUInt64();
                reader.BaseStream.Seek(8, SeekOrigin.Current);
                uint type = reader.ReadUInt32();
                reader.BaseStream.Seek(reader.ReadInt32() + (type is 2 or 3 ? 4 : 0), SeekOrigin.Current);
                (_, uint x, uint y, uint z) = TerrainStore.Decode(id);
                cells.Add(((int)x, (int)y, (int)z));
            }
            return cells;
        }

        public static VoxelTemplate Load(string path)
        {
            using var stream = File.OpenRead(path);
            VoxelTemplate template = Load(stream);
            template.Source = path;
            return template;
        }

        /// <summary>
        /// The file the template was read from (null when read from a stream, e.g. repaired bytes). A remote Environment
        /// Server is sent this name and reads the template from its own copy of the game's templates folder, so a ruin's
        /// decoded cells (up to ~0.6 GB) never cross the network (terrain-service.md §5.5).
        /// </summary>
        public string Source { get; private set; }

        /// <summary>
        /// The template's cells and size as a minimal .otd (version 6, the cell section as read, the size, no materials,
        /// no props) that <see cref="Load(Stream, bool)"/> reads back: how a template without a <see cref="Source"/> file
        /// travels to a remote Environment Server. Null when the cells were not kept (read without cells).
        /// </summary>
        public byte[] CellsAsOtd()
        {
            if (RawCells == null || RawCells.Length == 0)
                return null;
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((byte)6);
                writer.Write(RawCells);
                writer.Write(Size.X);
                writer.Write(Size.Y);
                writer.Write(Size.Z);
                writer.Write(0u);         // no materials
                writer.Write((byte)0);    // varint 0: no props
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Reads a template from a stream (a file, or bytes a caller repaired first: Landmarks of Landmark). Without
        /// <paramref name="withCells"/> the cell blobs are skipped, not decoded (size, materials and props only: a decoded
        /// cell takes ~0.4 MB).
        /// </summary>
        public static VoxelTemplate Load(Stream stream, bool withCells = true)
        {
            var template = new VoxelTemplate();
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
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
                if (!withCells)
                    continue;
                byte[] raw = TerrainStore.Unpack(blob, 0, blob.Length);
                byte[] changed = raw[..(BlockData.Count / 8)];
                BlockData blocks = BlockData.Deserialize(raw, BlockData.Count / 8);
                (_, uint x, uint y, uint z) = TerrainStore.Decode(id);
                template.Cells.Add(new Cell((int)x, (int)y, (int)z, changed, blocks));
            }
            long cellsEnd = reader.BaseStream.Position;
            reader.BaseStream.Position = cellsStart;
            template.RawCells = withCells ? reader.ReadBytes((int)(cellsEnd - cellsStart)) : Array.Empty<byte>();
            reader.BaseStream.Position = cellsEnd;
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

        /// <summary>
        /// A copy of the template with only the solid voxels <paramref name="solid"/> keeps: it is given each solid voxel's
        /// position (voxels from the template's corner) and stored material and returns the material to write, or null (or
        /// 0) to leave the voxel out. Template air, interior air and vertex-only voxels are left out (a stamp of the copy
        /// never carves the ground). Flags and vertex offsets are kept. The cell section is rebuilt in the stored format
        /// (u32 n × {u64 cell, u64 cell, u32 type 4, u32 length, packed changed mask + SerializeBlockData format 2}, as
        /// SiteDesigns writes a design), so <see cref="CellsAsOtd"/> carries the change to a remote Environment Server; the
        /// copy has no <see cref="Source"/> (a remote service must not read the unchanged file), the same size, the new
        /// material counts and no props. Used for the Pioneer slate slab (Pioneer.Slab, OPEN-LEADS L180).
        /// </summary>
        public VoxelTemplate SolidOnly(Func<int, int, int, ushort, ushort?> solid)
        {
            int size = BlockData.Size;
            var copy = new VoxelTemplate { Size = Size };
            var counts = new SortedDictionary<ushort, uint>();
            using var section = new MemoryStream();
            using (var writer = new BinaryWriter(section, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(0u);   // the cell count, written at the end
                uint written = 0;
                foreach (Cell cell in Cells)
                {
                    var changed = new byte[BlockData.Count / 8];
                    BlockData blocks = null;
                    for (int i = 0; i < BlockData.Count; i++)
                    {
                        if ((cell.Changed[i >> 3] & (0x80 >> (i & 7))) == 0)
                            continue;
                        byte flags = cell.Blocks.Flags[i];
                        ushort material = cell.Blocks.Materials[i];
                        if ((flags & 1) == 0 || material == 0 || material == InteriorAir)
                            continue;
                        // Voxel index order inside a cell: y + 40 · (x + 40 · z) (TerrainEdits.VoxelIndex).
                        int y = i % size, x = i / size % size, z = i / (size * size);
                        if (solid(cell.X * size + x, cell.Y * size + y, cell.Z * size + z, material) is not ushort to || to == 0)
                            continue;
                        blocks ??= new BlockData();
                        changed[i >> 3] |= (byte)(0x80 >> (i & 7));
                        blocks.Flags[i] = flags;
                        blocks.Materials[i] = to;
                        Array.Copy(cell.Blocks.Offsets, i * 3, blocks.Offsets, i * 3, 3);
                        counts[to] = counts.GetValueOrDefault(to) + 1;
                    }
                    if (blocks == null)
                        continue;
                    copy.Cells.Add(new Cell(cell.X, cell.Y, cell.Z, changed, blocks));
                    byte[] packed = TerrainStore.Pack(changed.Concat(blocks.Serialize()).ToArray());
                    ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cell.X, (uint)cell.Y, (uint)cell.Z);
                    writer.Write(id);
                    writer.Write(id);
                    writer.Write(4u);
                    writer.Write(packed.Length);
                    writer.Write(packed);
                    written++;
                }
                writer.Seek(0, SeekOrigin.Begin);
                writer.Write(written);
            }
            copy.RawCells = section.ToArray();
            foreach ((ushort material, uint count) in counts)
                copy.Materials.Add((material, count));
            return copy;
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
                // Material 0xFFFC is "interior empty space": the client marks air enclosed by a copied build with it
                // (FUN_143f9a150 / FUN_143f9c330) and its paste writes it as air (FUN_142a7eeb0, "Paste Interior Air").
                // Written as air here, so the overlay never holds 65532 (trace/0695/ruin-stamping.md §1).
                for (int i = 0; i < BlockData.Count; i++)
                    if (cell.Blocks.Materials[i] == InteriorAir && (cell.Blocks.Flags[i] & 1) != 0)
                        cell.Blocks.Materials[i] = 0;
                ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)(cellX + cell.X), (uint)(cellY + cell.Y), (uint)(cellZ + cell.Z));
                if (TerrainEdits.ApplyDiff(id, cell.Changed, cell.Blocks, (_, _, _) => true, out int voxels, zone, 0, TerrainEdits.PoiLayer) && voxels > 0)
                    changed.Add(id);
            }
            return changed;
        }

        /// <summary>
        /// Writes the template with its corner at a global voxel (0.4 m grid, not cell-aligned; live's ruin slots are
        /// voxel-aligned, trace/0695/ruin-stamping.md §5): every changed template voxel goes to the cell and index of its
        /// global voxel (flags, material with interior air as air, offsets), then one ApplyDiff per destination cell.
        /// Air that falls on the generated world's cave air is left out (it changes nothing, and a ruin's box is mostly
        /// air inside a cavern: writing it would store thousands of cells). Voxels are gathered sparsely per destination
        /// cell and each cell is built only when it is applied, with the overlays packed every 200 cells, so a large ruin
        /// never holds its cells expanded at once. Returns every destination cell (to mark permanent) and the changed ones.
        /// </summary>
        public (List<ulong> Cells, List<ulong> Changed) StampAt(long voxelX, long voxelY, long voxelZ, uint zone)
        {
            int size = BlockData.Size;
            double voxel = TerrainEdits.VoxelSize;
            var source = Cells.ToDictionary(c => (c.X, c.Y, c.Z));
            long skippedAir = 0, kept = 0;
            var cells = new List<ulong>();
            var changed = new List<ulong>();
            // Destination cells covering the template's cells, walked one at a time: memory stays at one cell.
            var targets = new SortedSet<(long, long, long)>();
            foreach (Cell c in Cells)
                for (int d = 0; d < 8; d++)
                    targets.Add((FloorDiv(voxelX + (long)c.X * size + ((d & 1) != 0 ? size - 1 : 0), size),
                        FloorDiv(voxelY + (long)c.Y * size + ((d & 2) != 0 ? size - 1 : 0), size),
                        FloorDiv(voxelZ + (long)c.Z * size + ((d & 4) != 0 ? size - 1 : 0), size)));
            int applied = 0;
            var mask = new byte[BlockData.Count / 8];
            foreach ((long cx, long cy, long cz) in targets)
            {
                Array.Clear(mask);
                BlockData blocks = null;
                for (int z = 0; z < size; z++)
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            // The template voxel at this global voxel.
                            long tx = cx * size + x - voxelX, ty = cy * size + y - voxelY, tz = cz * size + z - voxelZ;
                            if (tx < 0 || ty < 0 || tz < 0)
                                continue;
                            if (!source.TryGetValue(((int)(tx / size), (int)(ty / size), (int)(tz / size)), out Cell cell))
                                continue;
                            // Voxel index order inside a cell is y + 40 · (x + 40 · z) (docs/terrain.md "Cells";
                            // TerrainEdits.VoxelIndex). BlockData.Index is x-fastest, which mixed x and y inside each cell
                            // whenever the corner wasn't cell-aligned on x / y (found 2026-10-16 measuring the Landmarks
                            // of Landmark templates: only the y-fastest order keeps their builds inside their 70 m box).
                            int i = TerrainEdits.VoxelIndex((int)(tx % size), (int)(ty % size), (int)(tz % size));
                            if ((cell.Changed[i >> 3] & (0x80 >> (i & 7))) == 0)
                                continue;
                            byte flags = cell.Blocks.Flags[i];
                            ushort material = cell.Blocks.Materials[i] == InteriorAir ? (ushort)0 : cell.Blocks.Materials[i];
                            bool air = (flags & 1) != 0 && material == 0;   // air (template air decodes as flags 3, material 0)
                            long gx = cx * size + x, gy = cy * size + y, gz = cz * size + z;
                            if (air && CavePieces.IsAir((gx + 0.5) * voxel, (gy + 0.5) * voxel, (gz + 0.5) * voxel, zone))
                            {
                                skippedAir++;
                                continue;
                            }
                            kept++;
                            blocks ??= new BlockData();
                            int j = TerrainEdits.VoxelIndex(x, y, z);
                            mask[j >> 3] |= (byte)(0x80 >> (j & 7));
                            blocks.Flags[j] = flags;
                            blocks.Materials[j] = material;
                            Array.Copy(cell.Blocks.Offsets, i * 3, blocks.Offsets, j * 3, 3);
                        }
                if (blocks == null)
                    continue;
                ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                cells.Add(id);
                if (TerrainEdits.ApplyDiff(id, mask, blocks, (_, _, _) => true, out int voxels, zone, 0, TerrainEdits.PoiLayer) && voxels > 0)
                    changed.Add(id);
                if (++applied % 200 == 0)
                {
                    TerrainEdits.Flush();
                    TerrainEdits.CompactNow(true);
                }
            }
            LastStampReport = $"{kept} voxel(s) written, {skippedAir} air on cave air skipped, {cells.Count} cell(s)";
            return (cells, changed);
        }

        /// <summary>The last <see cref="StampAt"/>'s counts (for the log).</summary>
        public static string LastStampReport { get; set; } = "";

        // ---- Quarter turns (cavern graveyards: the .cpa ManuallyPlacedExportedTemplateData placements) ---------------
        //
        // A placement record {u32 record version, Room, u32 a, u32 b, u32 c} carries a = 1..4 (trace/0695/agent-leads2-d1.md
        // L47). [P by data] a is a quarter turn about +y: in every v12 file (old continents) the placement box of a
        // non-square template keeps the template's own x / z extents when a is even and swaps them when a is odd —
        // underground_portal_node.otd (template 61, 20 × 42 × 21 voxels): 1,530 of 1,560 boxes; template 75 (graveyard01's
        // 64 × 59 footprint): 1,528 of 1,556; templates 60 and the old 76 split the same way by parity — and a is spread
        // evenly over 1-4 at every spot (scratchpad gs/par.py, rot12.py). [I] a = the number of quarter turns, 4 = a full
        // turn (turns = a mod 4), in the cave pieces' own sense (CavePieces: rotation 1 takes world (jx, jz) from source
        // (−jz, jx), i.e. a source (x, z) goes to (z, −x): +90° about +y as System.Numerics turns a vector). Parity is all the
        // data can prove: the placement tool chose a at random (a is uniform at every spot, whatever the piece), so which odd
        // value is +90° and which −90° is not recoverable from the files.
        //
        // The box is turned about its middle, so the placement box keeps its corner: box minimum = template corner + 1
        // voxel (the vertex-only border, ruin-stamping.md §5), the explicit voxels 1..Size stay inside the box. Voxel model
        // [I strong]: a voxel index i carries a sample (solid / air, material) at grid point i and a surface vertex in the
        // cube from point i to point i + 1 (docs/terrain.md "Offset byte": u is measured from the voxel's corner, 0.5 =
        // 128 = the client's centre DAT_14552de88 (0.589; 0.695 DAT_14560e128); the templates' vertex-only border at
        // index 0 of x / y / z supplies the
        // cubes in front of the first explicit samples). Mirroring an axis of size S therefore takes sample i to S + 1 − i,
        // but the vertex of cube i to cube S − i with its fraction u to 1 − u (offset byte b to 255 − b, the exact inverse
        // of the codec's floor((u + 1) · 85 + 0.5); the centre 128 is kept as the centre). Material and vertex of one turned
        // voxel come from neighbouring source voxels. Unturned stamps (the outpost ruins, Landmarks of Landmark) take the
        // old path unchanged.

        /// <summary>The template's size after <paramref name="turns"/> quarter turns (x and z swap on odd turns).</summary>
        public (uint X, uint Y, uint Z) TurnedSize(int turns) => (turns & 1) != 0 ? (Size.Z, Size.Y, Size.X) : Size;

        /// <summary>The size a template file has after a turn, without decoding its cells.</summary>
        public static (uint X, uint Y, uint Z) TurnedSize((uint X, uint Y, uint Z) size, int turns) => (turns & 1) != 0 ? (size.Z, size.Y, size.X) : size;

        /// <summary>
        /// Where a point of the template (x / z in voxels from its corner, as a prop's position / 0.4 m) lies after
        /// <paramref name="turns"/> quarter turns, from the same corner (see the notes above).
        /// </summary>
        public (double X, double Z) TurnPoint(double x, double z, int turns)
        {
            double ex = Size.X + 1, ez = Size.Z + 1;
            return (turns & 3) switch
            {
                1 => (z, ex - x),
                2 => (ex - x, ez - z),
                3 => (ez - z, x),
                _ => (x, z)
            };
        }

        /// <summary>The rotation of <paramref name="turns"/> quarter turns about +y (applied after a prop's own rotation).</summary>
        public static Quaternion Turn(int turns) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (turns & 3) * MathF.PI / 2f);

        private Dictionary<(int, int, int), Cell> cellIndex;
        private Dictionary<(int, int, int), Cell> CellIndex => cellIndex ??= Cells.ToDictionary(c => (c.X, c.Y, c.Z));

        /// <summary>The source sample and source vertex voxels of a turned template's voxel (dest x / z from the corner).</summary>
        private (long Sx, long Sz, long Vx, long Vz) SourceOf(long dx, long dz, int turns) => (turns & 3) switch
        {
            1 => (Size.X + 1 - dz, dx, Size.X - dz, dx),
            2 => (Size.X + 1 - dx, Size.Z + 1 - dz, Size.X - dx, Size.Z - dz),
            3 => (dz, Size.Z + 1 - dx, dz, Size.Z - dx),
            _ => (dx, dz, dx, dz)
        };

        /// <summary>The template voxel at x / y / z (from its corner) when it is in a cell and marked changed.</summary>
        private bool TryVoxel(long x, long y, long z, out Cell cell, out int index)
        {
            int size = BlockData.Size;
            cell = null;
            index = 0;
            if (x < 0 || y < 0 || z < 0 || !CellIndex.TryGetValue(((int)(x / size), (int)(y / size), (int)(z / size)), out cell))
                return false;
            index = TerrainEdits.VoxelIndex((int)(x % size), (int)(y % size), (int)(z % size));
            return (cell.Changed[index >> 3] & (0x80 >> (index & 7))) != 0;
        }

        /// <summary>
        /// The turned template's sample at x / y / z from its corner: 1 solid, 0 air (template air or interior air), −1 not
        /// written there (the generated world stays).
        /// </summary>
        public int SampleAt(long dx, long dy, long dz, int turns)
        {
            (long sx, long sz, _, _) = SourceOf(dx, dz, turns);
            if (!TryVoxel(sx, dy, sz, out Cell cell, out int i) || (cell.Blocks.Flags[i] & 1) == 0)
                return -1;
            return cell.Blocks.Materials[i] is 0 or InteriorAir ? 0 : 1;
        }

        /// <summary>A vertex offset byte along a mirrored axis: u → 1 − u (255 − b), the centre kept.</summary>
        private static byte Mirror(byte b) => b == BlockData.CentreOffset ? b : (byte)(255 - b);

        /// <summary>
        /// <see cref="StampAt(long, long, long, uint)"/> with the template turned by <paramref name="turns"/> quarter turns
        /// about +y in its own box (corner unchanged; see the notes above). 0 is the plain stamp.
        /// </summary>
        public (List<ulong> Cells, List<ulong> Changed) StampAt(long voxelX, long voxelY, long voxelZ, uint zone, int turns)
        {
            turns &= 3;
            if (turns == 0)
                return StampAt(voxelX, voxelY, voxelZ, zone);
            int size = BlockData.Size;
            double voxel = TerrainEdits.VoxelSize;
            (uint tx, _, uint tz) = TurnedSize(turns);
            // Destination voxels from the corner: 0..Size + 1 on a mirrored axis (the vertex cubes 0..Size and samples
            // 1..Size land on 0..Size + 1), 0..Size on y (never turned).
            long ex = tx + 1, ey = Size.Y, ez = tz + 1;
            long skippedAir = 0, kept = 0;
            var cells = new List<ulong>();
            var changed = new List<ulong>();
            var mask = new byte[BlockData.Count / 8];
            int applied = 0;
            for (long cz = FloorDiv(voxelZ, size); cz <= FloorDiv(voxelZ + ez, size); cz++)
                for (long cy = FloorDiv(voxelY, size); cy <= FloorDiv(voxelY + ey, size); cy++)
                    for (long cx = FloorDiv(voxelX, size); cx <= FloorDiv(voxelX + ex, size); cx++)
                    {
                        Array.Clear(mask);
                        BlockData blocks = null;
                        for (int z = 0; z < size; z++)
                            for (int y = 0; y < size; y++)
                                for (int x = 0; x < size; x++)
                                {
                                    long dx = cx * size + x - voxelX, dy = cy * size + y - voxelY, dz = cz * size + z - voxelZ;
                                    if (dx < 0 || dy < 0 || dz < 0 || dx > ex || dy > ey || dz > ez)
                                        continue;
                                    (long sx, long sz, long vx, long vz) = SourceOf(dx, dz, turns);
                                    bool hasSample = TryVoxel(sx, dy, sz, out Cell sc, out int si);
                                    bool hasVertex = TryVoxel(vx, dy, vz, out Cell vc, out int vi);
                                    byte flags = (byte)((hasSample ? sc.Blocks.Flags[si] & 1 : 0) | (hasVertex ? vc.Blocks.Flags[vi] & 2 : 0));
                                    if (flags == 0)
                                        continue;
                                    ushort material = (flags & 1) == 0 ? (ushort)0
                                        : sc.Blocks.Materials[si] == InteriorAir ? (ushort)0 : sc.Blocks.Materials[si];
                                    long gx = cx * size + x, gy = cy * size + y, gz = cz * size + z;
                                    // As the plain stamp: air on the generated world's cave air changes nothing.
                                    if ((flags & 1) != 0 && material == 0
                                        && CavePieces.IsAir((gx + 0.5) * voxel, (gy + 0.5) * voxel, (gz + 0.5) * voxel, zone))
                                    {
                                        skippedAir++;
                                        continue;
                                    }
                                    kept++;
                                    blocks ??= new BlockData();
                                    int j = TerrainEdits.VoxelIndex(x, y, z);
                                    mask[j >> 3] |= (byte)(0x80 >> (j & 7));
                                    blocks.Flags[j] = flags;
                                    blocks.Materials[j] = material;
                                    if ((flags & 2) != 0)
                                    {
                                        byte ox = vc.Blocks.Offsets[vi * 3], oy = vc.Blocks.Offsets[vi * 3 + 1], oz = vc.Blocks.Offsets[vi * 3 + 2];
                                        (ox, oz) = turns switch { 1 => (oz, Mirror(ox)), 2 => (Mirror(ox), Mirror(oz)), _ => (Mirror(oz), ox) };
                                        blocks.Offsets[j * 3] = ox;
                                        blocks.Offsets[j * 3 + 1] = oy;
                                        blocks.Offsets[j * 3 + 2] = oz;
                                    }
                                    else
                                        blocks.Offsets[j * 3] = blocks.Offsets[j * 3 + 1] = blocks.Offsets[j * 3 + 2] = BlockData.CentreOffset;
                                }
                        if (blocks == null)
                            continue;
                        ulong id = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                        cells.Add(id);
                        if (TerrainEdits.ApplyDiff(id, mask, blocks, (_, _, _) => true, out int voxels, zone, 0, TerrainEdits.PoiLayer) && voxels > 0)
                            changed.Add(id);
                        if (++applied % 200 == 0)
                        {
                            TerrainEdits.Flush();
                            TerrainEdits.CompactNow(true);
                        }
                    }
            LastStampReport = $"{kept} voxel(s) written (turned {turns * 90}°), {skippedAir} air on cave air skipped, {cells.Count} cell(s)";
            return (cells, changed);
        }

        /// <summary>
        /// The level-2 cells a stamp with its corner at a global voxel can touch (the turned box and its border), without
        /// decoding the template: what a restart marks permanent again.
        /// </summary>
        public static List<ulong> CellsCovering(long voxelX, long voxelY, long voxelZ, (uint X, uint Y, uint Z) turnedSize)
        {
            int size = BlockData.Size;
            var cells = new List<ulong>();
            for (long cz = FloorDiv(voxelZ, size); cz <= FloorDiv(voxelZ + turnedSize.Z + 1, size); cz++)
                for (long cy = FloorDiv(voxelY, size); cy <= FloorDiv(voxelY + turnedSize.Y + 1, size); cy++)
                    for (long cx = FloorDiv(voxelX, size); cx <= FloorDiv(voxelX + turnedSize.X + 1, size); cx++)
                        cells.Add(TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz));
            return cells;
        }

        private static long FloorDiv(long a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
