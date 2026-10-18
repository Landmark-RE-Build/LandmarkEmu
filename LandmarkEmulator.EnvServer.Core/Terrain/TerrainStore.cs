using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Terrain cells the server has changed from the procedurally generated world. Everything else the client
    /// generates itself, so only edited cells are stored, listed in the modified-cell manifest and sent.
    /// <para>Cell ids (VoxelFarm): level in bits 60-63, x in bits 36-53, y × 64 in bits 18-35, z in bits 0-17; a
    /// level-L cell spans 40 × 2^L world units (decimetres). Each cell has up to 4 layers, each a compressed
    /// CellGeometry blob with its own version.</para>
    /// </summary>
    public static class TerrainStore
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public class Layer
        {
            public uint Version { get; set; }
            public byte Flag { get; set; }
            public byte[] Blob { get; set; }
        }

        public class Cell
        {
            public uint Version { get; set; }
            public Dictionary<uint, Layer> Layers { get; } = new();
        }

        private static readonly object sync = new();
        private static readonly Dictionary<ulong, Cell> cells = new();

        /// <summary>
        /// Builds a VoxelFarm cell id: level &lt;&lt; 60 | x &lt;&lt; 36 | (y · 64) &lt;&lt; 18 | z.
        /// </summary>
        public static ulong CellId(uint level, uint x, uint y, uint z)
            => ((ulong)level << 60) | ((ulong)x << 36) | ((ulong)(y << 6) << 18) | z;

        /// <summary>
        /// Global origin of the zone in world units (dm): client local metres = (global - origin) / 10. Read from the
        /// client (VoxelFarm world +0x5a08) for the starting zone; TODO take it from zone data.
        /// </summary>
        /// <summary>The continent the server's terrain, trees, caves and sites belong to (WorldServer.json Continent).</summary>
        public const uint HomeContinent = 406;

        public static readonly (double X, double Y, double Z) ZoneOrigin = (25584000, 20000, 25564500);

        /// <summary>The global corner (dm) of a cell, from its id (inverse of <see cref="CellId"/>).</summary>
        public static (double X, double Y, double Z) CellOrigin(ulong id)
        {
            uint level = (uint)(id >> 60);
            double size = CellSize(level);
            ulong x = (id >> 36) & 0xFFFFFF, y = ((id >> 18) & 0x3FFFF) >> 6, z = id & 0x3FFFF;
            return (x * size, y * size, z * size);
        }

        /// <summary>Size of a level-L cell in world units (dm).</summary>
        public static double CellSize(uint level) => 40.0 * (1u << (int)level);

        /// <summary>
        /// A cell's box in client-local metres ((index × size − origin) × 0.1). CellGeometry's own AABB is in cell
        /// units; the client applies this placement itself (FUN_14360d9d0).
        /// </summary>
        public static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) LocalBounds(ulong id)
        {
            (uint level, uint x, uint y, uint z) = Decode(id);
            double size = CellSize(level);
            var min = new System.Numerics.Vector3((float)((x * size - ZoneOrigin.X) * 0.1),
                (float)((y * size - ZoneOrigin.Y) * 0.1), (float)((z * size - ZoneOrigin.Z) * 0.1));
            float edge = (float)(size * 0.1);
            return (min, min + new System.Numerics.Vector3(edge));
        }

        /// <summary>
        /// Splits a cell id into level and cell coordinates (the inverse of <see cref="CellId"/>).
        /// </summary>
        public static (uint Level, uint X, uint Y, uint Z) Decode(ulong id)
            => ((uint)(id >> 60), (uint)((id >> 36) & 0x3FFFF), (uint)(((id >> 18) & 0x3FFFF) >> 6), (uint)(id & 0x3FFFF));

        /// <summary>
        /// CellsUpdate body carrying BLOCK data: version 0 tells the client the layers are CellBlocks (voxels it meshes
        /// itself), not CellGeometry. Works for live updates (FUN_143f52470 → set-0 cache → re-bake).
        /// </summary>
        public static byte[] BuildBlocksUpdate(ulong cellId, uint layer, uint layerVersion, byte[] blob)
            => BuildBlocksUpdate(cellId, layerVersion, new[] { (layer, blob) });

        /// <summary>CellsUpdate with several block layers of one cell, each with its own version.</summary>
        public static byte[] BuildBlocksUpdate(ulong cellId, IReadOnlyList<(uint Layer, uint Version, byte[] Blob)> layers)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteBytes(new byte[2]);
            writer.Write(1u);
            writer.Write(cellId);
            writer.Write(0u);
            writer.Write((uint)layers.Count);
            foreach ((uint layer, uint version, byte[] blob) in layers)
            {
                writer.Write(layer);
                writer.Write(version);
                writer.Write((byte)0);
                writer.Write((uint)blob.Length);
                writer.WriteBytes(blob);
            }
            writer.Write(0u);
            writer.Write(false);
            writer.Write(false);
            return output.ToArray();
        }

        /// <summary>CellsUpdate with several block layers of one cell (all at the same layer version).</summary>
        public static byte[] BuildBlocksUpdate(ulong cellId, uint layerVersion, IReadOnlyList<(uint Layer, byte[] Blob)> layers)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteBytes(new byte[2]);
            writer.Write(1u);
            writer.Write(cellId);
            writer.Write(0u);
            writer.Write((uint)layers.Count);
            foreach ((uint layer, byte[] blob) in layers)
            {
                writer.Write(layer);
                writer.Write(layerVersion);
                writer.Write((byte)0);
                writer.Write((uint)blob.Length);
                writer.WriteBytes(blob);
            }
            writer.Write(0u);
            writer.Write(false);
            writer.Write(false);
            return output.ToArray();
        }

        /// <summary>Stores a layer of a cell, bumping its versions so clients take the new data.</summary>
        public static Cell SetLayer(ulong cellId, uint layer, byte[] blob)
        {
            lock (sync)
            {
                if (!cells.TryGetValue(cellId, out Cell cell))
                    cells[cellId] = cell = new Cell();
                cell.Version++;
                cell.Layers.TryGetValue(layer, out Layer old);
                cell.Layers[layer] = new Layer { Version = (old?.Version ?? 0) + 1, Blob = blob };
                return cell;
            }
        }

        /// <summary>
        /// A stored (mesh-format) cell, or null.
        /// </summary>
        public static Cell Get(ulong cellId)
        {
            lock (sync)
                return cells.TryGetValue(cellId, out Cell cell) ? cell : null;
        }

        /// <summary>
        /// Body of TerrainCell 0x0A ModifiedCellManifest after its opcode (client FUN_143ee8810): u16 pad, then a
        /// compressed block holding, per layer 0-3, u32 count + count × (u64 cell, u32 layer version, u8 flag).
        /// </summary>
        /// <param name="zone">The landscape. The stored cells (captured from the live servers) belong to the home
        /// continent; player edits (TerrainEdits) are kept per landscape. A landscape without either still gets an
        /// empty manifest: it starts the client's terrain engine.</param>
        public static byte[] BuildManifest(uint zone) => BuildManifest(zone, TerrainEdits.ManifestEntries);

        /// <summary>
        /// <see cref="BuildManifest(uint)"/> with the player edits' entries (cell, layer version) per (landscape, layer) from
        /// <paramref name="editEntries"/>: the remote Environment Server's zone side builds the manifest from its version
        /// index this way (terrain-service.md §5.3), without asking the service.
        /// </summary>
        public static byte[] BuildManifest(uint zone, Func<uint, uint, List<(ulong Cell, uint Version)>> editEntries)
        {
            var raw = new List<byte>();
            var writer = new GamePacketWriter(raw);
            lock (sync)
            {
                for (uint layer = 0; layer < 4; layer++)
                {
                    bool home = zone == HomeContinent;
                    var entries = home ? cells.Where(c => c.Value.Layers.ContainsKey(layer)).ToList()
                        : new List<KeyValuePair<ulong, Cell>>();
                    // Player voxel edits (TerrainEdits) are listed under their layer too, so clients request them.
                    var edits = layer < TerrainEdits.LayerCount
                        ? editEntries(zone, layer).Where(e => !home || !cells.ContainsKey(e.Cell)).ToList()
                        : new List<(ulong Cell, uint Version)>();
                    writer.Write((uint)(entries.Count + edits.Count));
                    foreach ((ulong id, Cell cell) in entries)
                    {
                        writer.Write(id);
                        writer.Write(cell.Layers[layer].Version);
                        writer.Write(cell.Layers[layer].Flag);
                    }
                    foreach ((ulong id, uint version) in edits)
                    {
                        writer.Write(id);
                        writer.Write(version);
                        writer.Write((byte)0);
                    }
                }
            }
            byte[] packed = Pack(raw.ToArray());
            var output = new List<byte> { 0, 0 };
            output.AddRange(BitConverter.GetBytes(packed.Length));
            output.AddRange(packed);
            return output.ToArray();
        }

        /// <summary>
        /// TerrainCell 5 CellsUpdate body after its opcode: u16 pad, list{u64 cell, u32 version, list{u32 layer,
        /// u32 layer version, u8 flag, u32 length + blob}}, list{u64 transaction}, bool, bool.
        /// </summary>
        public static byte[] BuildCellsUpdate(IEnumerable<ulong> cellIds, IReadOnlyCollection<ulong> transactions = null)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteBytes(new byte[2]);
            var found = new List<(ulong, Cell)>();
            lock (sync)
                foreach (ulong id in cellIds)
                    if (cells.TryGetValue(id, out Cell cell))
                        found.Add((id, cell));
            writer.Write((uint)found.Count);
            foreach ((ulong id, Cell cell) in found)
            {
                writer.Write(id);
                writer.Write(cell.Version);
                writer.Write((uint)cell.Layers.Count);
                foreach ((uint layerId, Layer layer) in cell.Layers)
                {
                    writer.Write(layerId);
                    writer.Write(layer.Version);
                    writer.Write(layer.Flag);
                    writer.Write((uint)layer.Blob.Length);
                    writer.WriteBytes(layer.Blob);
                }
            }
            // Block-event transactions this update resolves (client links them only when exactly one cell is sent).
            writer.Write((uint)(transactions?.Count ?? 0));
            if (transactions != null)
                foreach (ulong transaction in transactions)
                    writer.Write(transaction);
            writer.Write(false);
            writer.Write(false);
            return output.ToArray();
        }

        private static long nextTransaction;

        /// <summary>
        /// TerrainCell 0xC ZoneProcessingComplete: u16 pad, u64 transaction — completes the client's pending block
        /// event (VoxelClientBlockEventCoordinator::ZoneProcessingComplete, FUN_143608490).
        /// </summary>
        public static byte[] BuildProcessingComplete(ulong transaction)
        {
            var output = new List<byte> { 0, 0 };
            output.AddRange(BitConverter.GetBytes(transaction));
            return output.ToArray();
        }

        public static ulong NewTransaction() => (ulong)System.Threading.Interlocked.Increment(ref nextTransaction);

        /// <summary>
        /// TerrainCell 7 CellBlockDamage (reader FUN_14309bb00): u16 pad, list{u64 cell, list{u16 block, u8 health %}},
        /// u64 block-event transaction. The client keeps the event (FUN_143608230) until a CellsUpdate names the same
        /// transaction.
        /// </summary>
        public static byte[] BuildBlockDamage(IReadOnlyCollection<(ulong Cell, IReadOnlyCollection<(ushort Block, byte Health)> Blocks)> cells,
            ulong transaction)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteBytes(new byte[2]);
            writer.Write((uint)cells.Count);
            foreach ((ulong cell, var blocks) in cells)
            {
                writer.Write(cell);
                writer.Write((uint)blocks.Count);
                foreach ((ushort block, byte health) in blocks)
                {
                    writer.WriteUShort(block);
                    writer.Write(health);
                }
            }
            writer.Write(transaction);
            return output.ToArray();
        }

        /// <summary>Undoes <see cref="Pack"/>: checks the A1B2C3D4 magic and inflates the zlib stream.</summary>
        public static byte[] Unpack(byte[] packed, int offset, int length)
        {
            if (length < 8 || packed[offset] != 0xA1 || packed[offset + 1] != 0xB2 || packed[offset + 2] != 0xC3 || packed[offset + 3] != 0xD4)
                throw new InvalidDataException("Not a packed block.");
            int size = packed[offset + 4] << 24 | packed[offset + 5] << 16 | packed[offset + 6] << 8 | packed[offset + 7];
            if (size < 0 || size > 1 << 22)
                throw new InvalidDataException("Packed block too large.");
            using var zlib = new ZLibStream(new MemoryStream(packed, offset + 8, length - 8), CompressionMode.Decompress);
            var raw = new byte[size];
            zlib.ReadExactly(raw);
            return raw;
        }

        /// <summary>
        /// The client's block codec (FUN_1439f5840/5950): A1 B2 C3 D4, big-endian u32 raw size, zlib stream.
        /// </summary>
        public static byte[] Pack(byte[] raw)
        {
            using var output = new MemoryStream();
            output.Write(new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 });
            output.WriteByte((byte)(raw.Length >> 24));
            output.WriteByte((byte)(raw.Length >> 16));
            output.WriteByte((byte)(raw.Length >> 8));
            output.WriteByte((byte)raw.Length);
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            return output.ToArray();
        }

        /// <summary>A CellGeometry layer blob: u32 raw size + packed geometry.</summary>
        public static byte[] LayerBlob(byte[] geometry)
        {
            byte[] packed = Pack(geometry);
            var blob = new byte[4 + packed.Length];
            BitConverter.GetBytes(geometry.Length).CopyTo(blob, 0);
            packed.CopyTo(blob, 4);
            return blob;
        }

        // The ModifiedCellManifest / ModifiedCellManifestUpdate senders are the zone's (ZoneTerrain.SendManifest,
        // SendManifestEntry, SendManifestUpdate): the zone is the only sender of terrain packets to a player.

        public static int Count
        {
            get { lock (sync) return cells.Count; }
        }
    }
}
