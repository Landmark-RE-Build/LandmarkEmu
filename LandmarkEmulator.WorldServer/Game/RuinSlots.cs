using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Live's ruin slots and arrival platforms from a continent's underground cave placements
    /// (ContinentCavePlacements[c]_Underground.cpa; client parser FUN_143fa2e20, layout in trace/0695/ruin-stamping.md §5):
    /// <code>
    /// u32 version; u32 n × tile (39 bytes: u32, 3 × f64, u32, u8, u32, u8, u8)
    /// v ≥ 3: u64;  3 ≤ v ≤ 12: u32 n × u64, u32 n × u64, (v > 3) u32 n × u64
    /// v > 4: u32; u32 n × LayerInfo {4 × i32, (v > 8) 2 × i32}
    /// v > 5: u32
    /// v > 6: u32 n × CavernInstanceData {4 × u32, Box, u32 m × Room}       ← the ruin slots (Rooms)
    /// v > 7: u32 n × CaveTileObjectGaps {u32, u32 m × Room}               ← the arrival platforms (kept clear)
    /// v > 10: u32 n × ManuallyPlacedExportedTemplateData {u32 v2, Room(v2), u32 a, u32 b, u32 c}  ← the graveyards
    /// Room = (v &lt; 10: 1 byte | v ≥ 10: u32 k × 3 × i32), Box;  Box = f64 min x, y, z, w, max x, y, z, w (global dm)
    /// </code>
    /// The manual placements (reader FUN_143fa3b70 → Room FUN_143fa3590; fields at record +0x58 / +0x5c / +0x60): v2 =
    /// the file version (it is passed to the Room reader as its version) [P], a = 1..4 a quarter turn (VoxelTemplate's
    /// turn notes) [P by data for its parity], b = 2 or 4 (4 with the graveyard templates 75 / 76) [I: a category], c = the
    /// template id (76 on every island = graveyard_launch.otd by its 83 × 22 × 83-voxel box; in no client table) [I by
    /// size]. The retail client parses them and never uses them (their only collector FUN_143ff7bc0 has no reference,
    /// trace/0695/agent-leads2-d1.md L47), so the server stamps them (Graveyards.Landscape.cs).
    /// </summary>
    public static class RuinSlots
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>A box in global decimetres.</summary>
        public readonly record struct Box(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
        {
            public bool Overlaps(Box o) => MinX < o.MaxX && o.MinX < MaxX && MinY < o.MaxY && o.MinY < MaxY && MinZ < o.MaxZ && o.MinZ < MaxZ;
        }

        /// <summary>One ManuallyPlacedExportedTemplateData record: its box and its a / b / c fields (see the notes above).</summary>
        public readonly record struct ManualTemplate(int Index, Box Box, uint A, uint B, uint C);

        public static (List<(int Cavern, Box Box)> Slots, List<Box> Gaps) Load(string path)
        {
            (var slots, var gaps, _) = Parse(path, manual: false);
            return (slots, gaps);
        }

        /// <summary>The placement file's manually placed templates (empty before version 11 or when unreadable).</summary>
        public static List<ManualTemplate> ManualTemplates(string path) => Parse(path, manual: true).Manual;

        private static (List<(int Cavern, Box Box)> Slots, List<Box> Gaps, List<ManualTemplate> Manual) Parse(string path, bool manual)
        {
            var slots = new List<(int, Box)>();
            var gaps = new List<Box>();
            var templates = new List<ManualTemplate>();
            if (!File.Exists(path))
                return (slots, gaps, templates);
            try
            {
                using var r = new BinaryReader(File.OpenRead(path));
                uint version = r.ReadUInt32();
                uint tiles = r.ReadUInt32();
                r.BaseStream.Seek(tiles * 39L, SeekOrigin.Current);
                if (version >= 3)
                    r.ReadUInt64();
                if (version >= 3 && version <= 12)
                    for (int k = 0; k < (version > 3 ? 3 : 2); k++)
                        r.BaseStream.Seek(r.ReadUInt32() * 8L, SeekOrigin.Current);
                if (version > 4)
                {
                    r.ReadUInt32();
                    uint layers = r.ReadUInt32();
                    r.BaseStream.Seek(layers * (version > 8 ? 24L : 16L), SeekOrigin.Current);
                }
                if (version > 5)
                    r.ReadUInt32();
                if (version > 6)
                {
                    uint caverns = r.ReadUInt32();
                    for (int c = 0; c < caverns; c++)
                    {
                        r.BaseStream.Seek(16, SeekOrigin.Current);
                        ReadBox(r);
                        uint rooms = r.ReadUInt32();
                        for (int m = 0; m < rooms; m++)
                            slots.Add((c, ReadRoom(r, version)));
                    }
                }
                if (version > 7)
                {
                    uint n = r.ReadUInt32();
                    for (int g = 0; g < n; g++)
                    {
                        r.ReadUInt32();
                        uint rooms = r.ReadUInt32();
                        for (int m = 0; m < rooms; m++)
                            gaps.Add(ReadRoom(r, version));
                    }
                }
                if (manual)
                {
                    if (version > 10)
                    {
                        uint n = r.ReadUInt32();
                        for (int i = 0; i < n; i++)
                        {
                            uint recordVersion = r.ReadUInt32();
                            Box box = ReadRoom(r, recordVersion);
                            templates.Add(new ManualTemplate(i, box, r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32()));
                        }
                    }
                }
                else
                    log.Info($"Ruin slots: {slots.Count} in the caverns, {gaps.Count} arrival platform(s) kept clear ({Path.GetFileName(path)}).");
            }
            catch (Exception e)
            {
                log.Warn($"Cave placements {path}: {(manual ? "manual template placements" : "ruin slots")} unreadable ({e.Message}).");
            }
            return (slots, gaps, templates);
        }

        private static Box ReadRoom(BinaryReader r, uint version)
        {
            if (version < 10)
                r.ReadByte();
            else
                r.BaseStream.Seek(r.ReadUInt32() * 12L, SeekOrigin.Current);
            return ReadBox(r);
        }

        private static Box ReadBox(BinaryReader r)
        {
            double minX = r.ReadDouble(), minY = r.ReadDouble(), minZ = r.ReadDouble();
            r.ReadDouble();
            double maxX = r.ReadDouble(), maxY = r.ReadDouble(), maxZ = r.ReadDouble();
            r.ReadDouble();
            return new Box(minX, minY, minZ, maxX, maxY, maxZ);
        }
    }
}
