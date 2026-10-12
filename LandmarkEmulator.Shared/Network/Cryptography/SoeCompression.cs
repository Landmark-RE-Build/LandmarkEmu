using System;
using System.IO;
using System.IO.Compression;

namespace LandmarkEmulator.Shared.Network.Cryptography
{
    /// <summary>
    /// LoginUdp SessionReply method 1 (<c>UdpCompressionHandler</c>): after opcode (and CRC strip),
    /// body is <c>u8 flag</c> + raw or zlib payload. Gateway method 0 has no flag.
    /// See <c>AgentShare/SoeProtocol.md</c> § Compression.
    /// </summary>
    public static class SoeCompression
    {
        /// <summary>
        /// Strip the compression flag and inflate when <paramref name="flag"/> is 1.
        /// When compression is disabled, returns <paramref name="body"/> unchanged.
        /// </summary>
        public static byte[] UnwrapBody(byte[] body, bool compressionEnabled)
        {
            if (!compressionEnabled || body == null || body.Length == 0)
                return body;

            byte flag = body[0];
            byte[] rest = new byte[body.Length - 1];
            if (rest.Length > 0)
                Buffer.BlockCopy(body, 1, rest, 0, rest.Length);

            return flag switch
            {
                0 => rest,
                1 => InflateZlib(rest),
                _ => rest
            };
        }

        /// <summary>
        /// Smallest body worth trying to compress; below this zlib's own overhead wins.
        /// </summary>
        private const int MinCompressLength = 64;

        /// <summary>
        /// Builds an outgoing body for a compression-enabled session: flag 1 + zlib when that is smaller, otherwise
        /// flag 0 + the raw bytes (the client's UdpCompressionHandler accepts both).
        /// </summary>
        /// <summary>Bytes handed to <see cref="WrapBody"/> and bytes it produced, for the stats log.</summary>
        public static long BytesIn, BytesOut;

        public static byte[] WrapBody(byte[] body)
        {
            byte[] wrapped = Wrap(body);
            System.Threading.Interlocked.Add(ref BytesIn, body.Length + 1);
            System.Threading.Interlocked.Add(ref BytesOut, wrapped.Length);
            return wrapped;
        }

        private static byte[] Wrap(byte[] body)
        {
            if (body.Length >= MinCompressLength)
            {
                // The client's UdpCompressionHandler (FUN_143952200) inflates with its block codec FUN_1439f5950:
                // A1 B2 C3 D4, big-endian u32 raw size, then the zlib stream — not bare zlib.
                using var output = new MemoryStream();
                output.WriteByte(1);
                output.Write(Magic);
                output.WriteByte((byte)(body.Length >> 24));
                output.WriteByte((byte)(body.Length >> 16));
                output.WriteByte((byte)(body.Length >> 8));
                output.WriteByte((byte)body.Length);
                using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
                    zlib.Write(body, 0, body.Length);
                if (output.Length < body.Length + 1)
                    return output.ToArray();
            }
            var raw = new byte[body.Length + 1];
            Buffer.BlockCopy(body, 0, raw, 1, body.Length);
            return raw;
        }

        private static readonly byte[] Magic = { 0xA1, 0xB2, 0xC3, 0xD4 };

        public static byte[] InflateZlib(byte[] compressed)
        {
            if (compressed == null || compressed.Length == 0)
                return Array.Empty<byte>();
            // Client-compressed bodies use the same block codec (FUN_1439f5840): skip magic + size.
            if (compressed.Length > 8 && compressed[0] == 0xA1 && compressed[1] == 0xB2 && compressed[2] == 0xC3
                && compressed[3] == 0xD4)
                compressed = compressed.AsSpan(8).ToArray();

            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
    }
}
