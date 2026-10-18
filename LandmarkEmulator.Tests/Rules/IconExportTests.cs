using LandmarkEmulator.WorldServer.Game;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The icon export (Game/IconExport.cs): DXT decoding as the client's icons need it, and a PNG that unpacks back to the
    /// same pixels. (Checked against the Pillow-based research export on 4,937 real icons: identical pixels.)
    /// </summary>
    public class IconExportTests
    {
        private static byte[] Dds(string fourCc, int width, int height, byte[] blocks)
        {
            byte[] dds = new byte[128 + blocks.Length];
            Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
            BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(4), 124);
            BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(12), height);
            BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(16), width);
            BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(80), 0x4);
            Encoding.ASCII.GetBytes(fourCc).CopyTo(dds, 84);
            blocks.CopyTo(dds, 128);
            return dds;
        }

        [Fact]
        public void Dxt1_FourColourAndTransparentBlocks()
        {
            // Block 1: c0 = white > c1 = black, indices 0,1,2,3 on the first row. Block 2: c0 <= c1, index 3 = transparent.
            byte[] blocks =
            {
                0xFF, 0xFF, 0x00, 0x00, 0b11100100, 0, 0, 0,
                0x00, 0x00, 0xFF, 0xFF, 0b11100100, 0, 0, 0,
            };
            (int w, int h, byte[] rgba) = IconExport.DecodeDds(Dds("DXT1", 8, 4, blocks));
            Assert.Equal(8, w);
            Assert.Equal(4, h);
            byte[] Pixel(int x, int y) => rgba.Skip((y * w + x) * 4).Take(4).ToArray();
            Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(0, 0));
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, Pixel(1, 0));
            Assert.Equal(new byte[] { 170, 170, 170, 255 }, Pixel(2, 0));
            Assert.Equal(new byte[] { 85, 85, 85, 255 }, Pixel(3, 0));
            Assert.Equal(new byte[] { 127, 127, 127, 255 }, Pixel(6, 0));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(7, 0));
        }

        [Fact]
        public void Dxt5_InterpolatesAlpha()
        {
            // a0 = 255 > a1 = 0: index 2 = (6*255 + 0) / 7 = 218. Pixel 0 takes index 0, pixel 1 index 1, pixel 2 index 2.
            byte[] block = { 255, 0, 0b10001000, 0, 0, 0, 0, 0, 0x00, 0xF8, 0x00, 0xF8, 0, 0, 0, 0 };
            (_, _, byte[] rgba) = IconExport.DecodeDds(Dds("DXT5", 4, 4, block));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba[0..4]);
            Assert.Equal(0, rgba[7]);
            Assert.Equal(218, rgba[11]);
        }

        [Fact]
        public void Png_UnpacksToTheSamePixels()
        {
            var random = new Random(7);
            byte[] rgba = new byte[5 * 3 * 4];
            random.NextBytes(rgba);
            byte[] png = IconExport.EncodePng(5, 3, rgba);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[0..8]);
            Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
            int idat = 8 + 25;
            Assert.Equal("IDAT", Encoding.ASCII.GetString(png, idat + 4, 4));
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(idat));
            using var z = new ZLibStream(new MemoryStream(png, idat + 8, length), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            z.CopyTo(raw);
            byte[] f = raw.ToArray();
            int stride = 5 * 4;
            byte[] back = new byte[rgba.Length];
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= 4 ? back[y * stride + x - 4] : 0, up = y > 0 ? back[(y - 1) * stride + x] : 0;
                    int upLeft = x >= 4 && y > 0 ? back[(y - 1) * stride + x - 4] : 0;
                    int p = left + up - upLeft;
                    int paeth = Math.Abs(p - left) <= Math.Abs(p - up) && Math.Abs(p - left) <= Math.Abs(p - upLeft) ? left
                        : Math.Abs(p - up) <= Math.Abs(p - upLeft) ? up : upLeft;
                    int pred = f[y * (stride + 1)] switch { 1 => left, 2 => up, 3 => (left + up) / 2, 4 => paeth, _ => 0 };
                    back[y * stride + x] = (byte)(f[y * (stride + 1) + 1 + x] + pred);
                }
            Assert.Equal(rgba, back);
        }

        [Fact]
        public void Png_IsKeptAsItIs()
        {
            byte[] png = IconExport.EncodePng(1, 1, new byte[] { 1, 2, 3, 4 });
            Assert.Same(png, IconExport.ToPng(png));
            Assert.Throws<InvalidDataException>(() => IconExport.ToPng(new byte[200]));
        }
    }
}
