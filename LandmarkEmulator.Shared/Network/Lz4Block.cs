using System;
using System.IO;

namespace LandmarkEmulator.Shared.Network
{
    /// <summary>
    /// Minimal LZ4 block decoder (the format each item definition is compressed with), and an encoder that writes one
    /// literal-only sequence (a valid LZ4 block: the last sequence of any block is literals only).
    /// </summary>
    public static class Lz4Block
    {
        /// <summary>Encodes data as a single literal run: token (length nibble 15 when ≥ 15), extra length bytes, the bytes.</summary>
        public static byte[] EncodeLiterals(ReadOnlySpan<byte> data)
        {
            var output = new MemoryStream(data.Length + data.Length / 255 + 2);
            int length = data.Length;
            output.WriteByte((byte)(Math.Min(length, 15) << 4));
            if (length >= 15)
            {
                int rest = length - 15;
                while (rest >= 255)
                {
                    output.WriteByte(255);
                    rest -= 255;
                }
                output.WriteByte((byte)rest);
            }
            output.Write(data);
            return output.ToArray();
        }

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
