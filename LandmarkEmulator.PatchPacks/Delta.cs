
using System.IO.Compression;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// Binary deltas: a changed game file stored as the differences from its Steam 0.589 version, so the update carries
    /// only what changed (a changed .pack is ~98% the same as before). A delta only rebuilds the new file from the exact
    /// old file it was made from, so it is useless without a genuine copy of the game.
    ///
    /// Format ("LMD1"): header {magic, u64 old size, u32 old CRC32, u64 new size, u32 new CRC32}, then operations until
    /// the new file is complete:
    /// <list type="bullet">
    /// <item>'C' copy: varint old offset (signed, from the end of the previous copy/add), varint length — bytes as in the old file.</item>
    /// <item>'A' add: same fields, then length bytes d — new byte = old byte + d (mod 256). For code where addresses
    /// moved (bsdiff's idea): the d bytes are mostly 0 and compress to almost nothing.</item>
    /// <item>'L' literal: varint length, then the bytes.</item>
    /// </list>
    /// The pack's .zip compresses the result.
    /// </summary>
    public static class Delta
    {
        private const int Window = 32;            // seed length: a match is found once 32 bytes agree
        private const ulong Prime = 1099511628211; // rolling-hash base

        private static readonly byte[] Magic = "LMD1"u8.ToArray();

        /// <summary>Makes the delta that turns <paramref name="old"/> into <paramref name="now"/>.</summary>
        public static byte[] Create(byte[] old, byte[] now)
        {
            // Index the old file: rolling hash of each 32-byte block at every 16-byte step -> its offset.
            var index = new Dictionary<ulong, int>(old.Length / 16 + 1);
            ulong pow = 1;
            for (int i = 0; i < Window; i++)
                pow *= Prime;
            for (int at = 0; at + Window <= old.Length; at += 16)
                index.TryAdd(Hash(old, at), at);

            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            w.Write(Magic);
            w.Write((ulong)old.Length);
            w.Write(Crc(old));
            w.Write((ulong)now.Length);
            w.Write(Crc(now));

            long lastOldEnd = 0;           // where the previous copy/add ended in the old file
            int pending = 0;               // start of new bytes not written yet
            int pos = 0;
            ulong h = now.Length >= Window ? Hash(now, 0) : 0;
            while (pos + Window <= now.Length)
            {
                if (index.TryGetValue(h, out int oldAt) && Same(old, oldAt, now, pos, Window))
                {
                    // Extend the match both ways.
                    int start = pos, oldStart = oldAt;
                    while (start > pending && oldStart > 0 && old[oldStart - 1] == now[start - 1])
                    {
                        start--;
                        oldStart--;
                    }
                    int end = pos + Window, oldEnd = oldAt + Window;
                    while (end < now.Length && oldEnd < old.Length && old[oldEnd] == now[end])
                    {
                        end++;
                        oldEnd++;
                    }
                    // The bytes before the match: an add against the old bytes just before it (same displacement)
                    // when they are mostly the same, otherwise literal.
                    WriteGap(w, old, now, pending, start, oldStart - (start - pending), ref lastOldEnd);
                    WriteOp(w, (byte)'C', oldStart, end - start, ref lastOldEnd);
                    pending = pos = end;
                    if (pos + Window <= now.Length)
                        h = Hash(now, pos);
                    continue;
                }
                // Roll the hash one byte on.
                if (pos + Window < now.Length)
                    h = h * Prime + now[pos + Window] - now[pos] * pow;
                pos++;
            }
            WriteGap(w, old, now, pending, now.Length, lastOldEnd, ref lastOldEnd);
            return output.ToArray();
        }

        /// <summary>The new bytes [from, to): an add against old[oldFrom..] if at least half the bytes agree, else literal.</summary>
        private static void WriteGap(BinaryWriter w, byte[] old, byte[] now, int from, int to, long oldFrom, ref long lastOldEnd)
        {
            int length = to - from;
            if (length <= 0)
                return;
            if (oldFrom >= 0 && oldFrom + length <= old.Length)
            {
                int same = 0;
                for (int i = 0; i < length; i++)
                    if (old[oldFrom + i] == now[from + i])
                        same++;
                if (same * 2 >= length && length >= 8)
                {
                    WriteOp(w, (byte)'A', oldFrom, length, ref lastOldEnd);
                    for (int i = 0; i < length; i++)
                        w.Write((byte)(now[from + i] - old[oldFrom + i]));
                    return;
                }
            }
            w.Write((byte)'L');
            WriteVarint(w, (ulong)length);
            w.Write(now, from, length);
        }

        private static void WriteOp(BinaryWriter w, byte op, long oldAt, int length, ref long lastOldEnd)
        {
            w.Write(op);
            long rel = oldAt - lastOldEnd;
            WriteVarint(w, (ulong)((rel << 1) ^ (rel >> 63)));   // zigzag: small moves either way stay small
            WriteVarint(w, (ulong)length);
            lastOldEnd = oldAt + length;
        }

        /// <summary>
        /// Rebuilds the new file from the old file and a delta, into <paramref name="output"/>. Throws if the old file isn't the
        /// one the delta was made from (size, CRC32), or the result isn't the new file (size, CRC32).
        /// </summary>
        public static void Apply(byte[] old, byte[] delta, Stream output)
        {
            var (oldSize, oldCrc, newSize, newCrc) = ReadHeader(delta);
            var r = new BinaryReader(new MemoryStream(delta));
            r.BaseStream.Position = HeaderSize;
            if (old.Length != oldSize || Crc(old) != oldCrc)
                throw new InvalidDataException("This patch is for a different version of the file.");
            try
            {
                ApplyOps(old, r, delta.Length, output, newSize, newCrc);
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("The patch is damaged (cut short).");
            }
        }

        private const int HeaderSize = 4 + 8 + 4 + 8 + 4;

        /// <summary>A delta's header: the old file's size and CRC32, and the new file's.</summary>
        public static (long OldSize, uint OldCrc, long NewSize, uint NewCrc) ReadHeader(byte[] delta)
        {
            if (delta.Length < HeaderSize || !delta.AsSpan(0, 4).SequenceEqual(Magic))
                throw new InvalidDataException("Not a delta.");
            long oldSize = BitConverter.ToInt64(delta, 4), newSize = BitConverter.ToInt64(delta, 16);
            if (oldSize < 0 || newSize < 0)
                throw new InvalidDataException("The patch is damaged.");
            return (oldSize, BitConverter.ToUInt32(delta, 12), newSize, BitConverter.ToUInt32(delta, 24));
        }

        /// <summary>
        /// The operations, written to <paramref name="output"/>. Never writes past the new size the header gives (a hostile
        /// delta that copies the old file over and over stops there), and never reads a literal longer than the delta.
        /// </summary>
        private static void ApplyOps(byte[] old, BinaryReader r, long deltaLength, Stream output, long newSize, uint newCrc)
        {
            var crc = new System.IO.Hashing.Crc32();
            long written = 0, lastOldEnd = 0;
            byte[] buffer = new byte[1 << 16];
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                byte op = r.ReadByte();
                if (op == 'L')
                {
                    ulong literal = ReadVarint(r);
                    if (literal > (ulong)(deltaLength - r.BaseStream.Position) || (long)literal > newSize - written)
                        throw new InvalidDataException("The patch is damaged.");
                    int length = (int)literal;
                    byte[] bytes = r.ReadBytes(length);
                    output.Write(bytes);
                    crc.Append(bytes);
                    written += length;
                    continue;
                }
                ulong z = ReadVarint(r);
                long oldAt = lastOldEnd + ((long)(z >> 1) ^ -(long)(z & 1));
                ulong length64 = ReadVarint(r);
                if (length64 > (ulong)old.Length || (long)length64 > newSize - written || (op != 'C' && op != 'A'))
                    throw new InvalidDataException("The patch is damaged.");
                int count = (int)length64;
                if (oldAt < 0 || oldAt > old.Length - count)
                    throw new InvalidDataException("The patch is damaged.");
                lastOldEnd = oldAt + count;
                for (int done = 0; done < count;)
                {
                    int n = Math.Min(buffer.Length, count - done);
                    Array.Copy(old, oldAt + done, buffer, 0, n);
                    if (op == 'A')
                    {
                        byte[] d = r.ReadBytes(n);
                        if (d.Length != n)
                            throw new InvalidDataException("The patch is damaged (cut short).");
                        for (int i = 0; i < n; i++)
                            buffer[i] += d[i];
                    }
                    else if (op != 'C')
                        throw new InvalidDataException("The patch is damaged.");
                    output.Write(buffer, 0, n);
                    crc.Append(buffer.AsSpan(0, n));
                    done += n;
                }
                written += count;
            }
            if (written != newSize || BitConverter.ToUInt32(crc.GetCurrentHash()) != newCrc)
                throw new InvalidDataException("The patched file came out wrong (size or CRC32).");
        }

        private static ulong Hash(byte[] data, int at)
        {
            ulong h = 0;
            for (int i = 0; i < Window; i++)
                h = h * Prime + data[at + i];
            return h;
        }

        private static bool Same(byte[] a, int ai, byte[] b, int bi, int n)
            => ai + n <= a.Length && bi + n <= b.Length && a.AsSpan(ai, n).SequenceEqual(b.AsSpan(bi, n));

        public static uint Crc(byte[] data) => System.IO.Hashing.Crc32.HashToUInt32(data);

        private static void WriteVarint(BinaryWriter w, ulong v)
        {
            while (v >= 0x80)
            {
                w.Write((byte)(v | 0x80));
                v >>= 7;
            }
            w.Write((byte)v);
        }

        private static ulong ReadVarint(BinaryReader r)
        {
            ulong v = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (shift > 63)
                    throw new InvalidDataException("The patch is damaged.");
                byte b = r.ReadByte();
                v |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return v;
            }
        }

        /// <summary>Size of <paramref name="data"/> after Deflate (as a pack's .zip would store it).</summary>
        public static long Deflated(byte[] data)
        {
            using var counter = new MemoryStream();
            using (var z = new DeflateStream(counter, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(data);
            return counter.Length;
        }
    }
}
