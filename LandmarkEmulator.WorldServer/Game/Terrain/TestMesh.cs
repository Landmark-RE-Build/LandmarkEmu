using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Hand-made cell geometry for probing the client's mesh format (dev command testcell).
    /// CellGeometry (client CellGeometry::Decompress FUN_143f1cde0): 2 × (u8 present + CellMesh), bounds block,
    /// AABB. CellMesh: u32 n + n × 3 u16 positions, u32 n + n × 3 u8 normals, 4 × (u32 n + n × u32).
    /// </summary>
    public static class TestMesh
    {
        /// <summary>Vertex coordinates are IntToFloat&lt;u16, 32767&gt;: real client meshes use roughly 33000-59000.</summary>
        public const ushort PositionZero = 32767;

        /// <summary>
        /// A one-quad test mesh in CellMesh format (developer "testcell" command): two triangles at a height, with the given
        /// per-vertex arrays.
        /// </summary>
        public static byte[] FlatQuad(ushort height, ushort extent, uint[][] arrays, ushort origin = 0)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)1);
            var positions = new (ushort, ushort, ushort)[]
            {
                (origin, height, origin), ((ushort)(origin + extent), height, origin),
                ((ushort)(origin + extent), height, (ushort)(origin + extent)), (origin, height, (ushort)(origin + extent))
            };
            writer.Write((uint)positions.Length);
            foreach ((ushort x, ushort y, ushort z) in positions)
            {
                writer.Write(x);
                writer.Write(y);
                writer.Write(z);
            }
            writer.Write((uint)positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                writer.Write((byte)127);
                writer.Write((byte)254);
                writer.Write((byte)127);
            }
            foreach (uint[] array in arrays)
            {
                writer.Write((uint)array.Length);
                foreach (uint value in array)
                    writer.Write(value);
            }
            writer.Write((byte)0);   // second mesh absent
            writer.Write((byte)0);   // bounds: none
            // AABB as 2 × vec4 in cell units (0..1 = the cell); the client places it (FUN_14360d9d0:
            // local = (index × size − origin) × 0.1 + value × size × 0.1).
            float lo = ToCell(origin), hi = ToCell((ushort)(origin + extent)), top = ToCell(height);
            foreach (float f in new[] { lo, top, lo, 1f, hi, top, hi, 1f })
                writer.Write(f);
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>A vertex coordinate in cell units: IntToFloat&lt;u16, 32767.5, scale 0.8&gt;.</summary>
        public static float ToCell(ushort v) => (v - 32767.5f) / (32767.5f * 0.8f);

        public static uint[][] DefaultArrays() => Arrays(0);

        /// <summary>
        /// The four u32 arrays. Variant 8 is the real layout, read from meshes the client's own mesher (FUN_14360db80)
        /// builds: per vertex a0 = blend (secondary material &lt;&lt; 8 | weight; 0xff00 = none), a1 = material id
        /// (secondary &lt;&lt; 16 | primary), a2 = flags (0; triangles whose corners are all 0xff are dropped), then
        /// a3 = triangle indices. The others are earlier guesses.
        /// </summary>
        public static uint[][] Arrays(int variant)
        {
            uint[] tris = { 0, 1, 2, 0, 2, 3 };
            uint[] trisCw = { 0, 2, 1, 0, 3, 2 };
            uint[] four = { 1, 1, 1, 1 };
            uint[] two = { 1, 1 };
            uint[] none = new uint[0];
            return variant switch
            {
                1 => new[] { tris, two, none, none },
                2 => new[] { four, tris, none, none },
                3 => new[] { tris, none, none, none },
                4 => new[] { trisCw, four, none, none },
                5 => new[] { tris, tris, tris, tris },
                6 => new[] { none, none, tris, four },
                7 => new[] { tris, four, four, four },
                8 => new[] { new uint[] { 0xff00, 0xff00, 0xff00, 0xff00 }, new uint[] { 390, 390, 390, 390 },
                    new uint[] { 0, 0, 0, 0 }, tris },
                10 => new[] { new uint[] { 0xff00, 0xff00, 0xff00, 0xff00 }, new uint[] { 390, 390, 390, 390 },
                    new uint[] { 0, 0, 0, 0 }, new uint[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 } },
                9 => new[] { new uint[] { 0xff00, 0xff00, 0xff00, 0xff00 }, new uint[] { 390, 390, 390, 390 },
                    new uint[] { 0, 0, 0, 0 }, trisCw },
                _ => new[] { tris, four, none, none }
            };
        }
    }
}
