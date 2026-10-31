using NLog;
using System;
using System.Runtime.InteropServices;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The version the game client gives a layer of a cell: CRC32 of its own compressed layer (CompressBlockData,
    /// FUN_144049a00): the bytes A1 B2 C3 D4, the raw length as a big-endian u32, then classic zlib (compress2, level 6)
    /// of the raw layer; 0 becomes 1 and 0x7FFFFFFF becomes 0x7FFFFFFE; a layer with no flagged voxel has version 0
    /// (FUN_14404aa60) [P]. Reproduced exactly from the server's own layer bytes for five logged cells (trace/0695
    /// stale-terrain review 2026-10-31); .NET's ZLibStream (zlib-ng) and SharpZipLib produce other bytes, so the classic
    /// native zlib is loaded (libz.so.1 in the Linux images, zlib1.dll on Windows). Without it <see cref="Version"/> is null.
    /// </summary>
    /// <remarks>
    /// Why the server needs it: the server stamps a layer it writes itself (heal, undo / redo, Delete / Cut, paste) with
    /// a counter, while the client re-stamps the same content with this CRC on its next local commit — so the builder's
    /// next edit carried a base the server did not know and was refused as stale, and a section of a brush stroke
    /// reverted (player report 2026-10-31, TerrainEdits.HoldsClientCopy).
    /// </remarks>
    public static class ClientLayerCodec
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Compress2(byte[] dest, ref CULong destLength, byte[] source, CULong sourceLength, int level);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate CULong CompressBound(CULong sourceLength);

        private static readonly Lazy<(Compress2 Compress, CompressBound Bound)?> zlib = new(LoadZlib);

        private static (Compress2, CompressBound)? LoadZlib()
        {
            foreach (string name in new[] { "libz.so.1", "libz.so", "zlib1.dll", "zlib.dll", "libz.dylib" })
                if (NativeLibrary.TryLoad(name, out IntPtr handle)
                    && NativeLibrary.TryGetExport(handle, "compress2", out IntPtr compress)
                    && NativeLibrary.TryGetExport(handle, "compressBound", out IntPtr bound))
                    return (Marshal.GetDelegateForFunctionPointer<Compress2>(compress), Marshal.GetDelegateForFunctionPointer<CompressBound>(bound));
            log.Warn("Classic zlib (libz.so.1 / zlib1.dll) not found: a build edit based on the client's own version of a "
                + "server-written layer is accepted only when the layer is empty.");
            return null;
        }

        /// <summary>Is classic zlib available (else <see cref="Version"/> returns null)?</summary>
        public static bool Available => zlib.Value != null;

        /// <summary>The client's version of a layer from its raw bytes (block data, plus the placed-design lookup in layer 1); null without zlib.</summary>
        public static uint? Version(byte[] raw)
        {
            if (zlib.Value is not (Compress2 compress, CompressBound bound))
                return null;
            byte[] deflated = new byte[(int)bound(new CULong((nuint)raw.Length)).Value];
            CULong length = new((nuint)deflated.Length);
            if (compress(deflated, ref length, raw, new CULong((nuint)raw.Length), 6) != 0)
                return null;
            byte[] packed = new byte[8 + (int)length.Value];
            packed[0] = 0xA1; packed[1] = 0xB2; packed[2] = 0xC3; packed[3] = 0xD4;
            packed[4] = (byte)(raw.Length >> 24); packed[5] = (byte)(raw.Length >> 16); packed[6] = (byte)(raw.Length >> 8); packed[7] = (byte)raw.Length;
            Array.Copy(deflated, 0, packed, 8, (int)length.Value);
            uint crc = Crc32(packed, packed.Length);
            return crc == 0 ? 1u : crc == 0x7FFF_FFFF ? 0x7FFF_FFFEu : crc;
        }

        private static readonly uint[] table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        /// <summary>Standard CRC-32 (IEEE, reflected, init and final xor 0xFFFFFFFF) of the first <paramref name="count"/> bytes.</summary>
        public static uint Crc32(byte[] data, int count)
        {
            uint crc = 0xFFFF_FFFF;
            for (int i = 0; i < count; i++)
                crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFF_FFFF;
        }
    }
}
