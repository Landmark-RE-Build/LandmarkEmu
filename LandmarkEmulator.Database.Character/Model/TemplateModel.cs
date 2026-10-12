using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A saved design (template): the voxels of a Select-tool box, kept for its owner to place again.
    /// </summary>
    public class TemplateModel
    {
        /// <summary>Also the design's TemplateGUID on the wire.</summary>
        public ulong Id { get; set; }
        public ulong OwnerGuid { get; set; }
        public string AuthorName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public uint SizeX { get; set; }
        public uint SizeY { get; set; }
        public uint SizeZ { get; set; }
        /// <summary>Solid voxels per material: "material:count" entries separated by ';'.</summary>
        public string Materials { get; set; } = "";
        /// <summary>The block list exactly as the client sent it (u32 n × {u64, u64, u32 type, u32 len + packed block, ...}).</summary>
        public byte[] Blocks { get; set; } = Array.Empty<byte>();
        public DateTime CreatedAt { get; set; }
    }
}
