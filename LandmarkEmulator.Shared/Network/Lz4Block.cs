using System;
using System.IO;

namespace LandmarkEmulator.Shared.Network
{
    /// <summary>
    /// Minimal LZ4 block decoder (the format each item definition is compressed with).
    /// </summary>
    public static class Lz4Block
    {
        /// <summary>
        /// Decodes one LZ4 block into a buffer of the known decompressed size.
        /// </summary>
        public static byte[] Decode(ReadOnlySpan<byte> src, int outputSize)
        {
            var dst = new byte[outputSize];
            int s = 0, d = 0;
            while (s < src.Length)
            {
                int token = src[s++];
                int literals = token >> 4;
                if (literals == 15)
                {
                    int b;
                    do { b = src[s++]; literals += b; } while (b == 255);
                }
                src.Slice(s, literals).CopyTo(dst.AsSpan(d));
                s += literals;
                d += literals;
                if (s >= src.Length)
                    break;

                int matchOffset = src[s] | (src[s + 1] << 8);
                s += 2;
                int matchLength = token & 15;
                if (matchLength == 15)
                {
                    int b;
                    do { b = src[s++]; matchLength += b; } while (b == 255);
                }
                matchLength += 4;
                for (int k = 0; k < matchLength; k++, d++)
                    dst[d] = dst[d - matchOffset];
            }
            if (d != outputSize)
                throw new InvalidDataException($"LZ4 block decoded to {d} bytes, expected {outputSize}.");
            return dst;
        }
    }
}
