
using System.Runtime.InteropServices;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>
    /// In-place edits: a changed game file stored as just the byte ranges that differ at the same positions, plus any data
    /// added at the end. Daybreak's asset packs change this way (~98% of each stays where it is, new assets go on the end),
    /// so the update writes a few hundred KB into each pack instead of rewriting it.
    ///
    /// Format ("LME1"): {magic, u64 size before, u32 CRC32 before, u64 size after, u32 CRC32 after, u32 n,
    /// n × {u64 offset, u32 length, bytes}}. Applying writes each range at its offset, then sets the file's length.
    /// The same format undoes an edit: the original bytes of each range (within the old length) and the old length
    /// (<see cref="Undo"/>), saved before the file is touched.
    /// </summary>
    public static class InPlaceEdit
    {
        private static readonly byte[] Magic = "LME1"u8.ToArray();

        /// <summary>Two differing runs closer than this are written as one range (fewer, slightly longer ranges).</summary>
        private const int Join = 64;

        public sealed record Range(long Offset, byte[] Bytes);

        public sealed class Edit
        {
            public long SizeBefore { get; init; }
            public uint CrcBefore { get; init; }
            public long SizeAfter { get; init; }
            public uint CrcAfter { get; init; }
            public List<Range> Ranges { get; init; } = new();
            /// <summary>Bytes the ranges carry (what the pack stores, before compression).</summary>
            public long Payload => Ranges.Sum(r => (long)r.Bytes.Length);
        }

        /// <summary>The edit that turns <paramref name="old"/> into <paramref name="now"/> in place.</summary>
        public static Edit Create(byte[] old, byte[] now)
        {
            var ranges = new List<Range>();
            int common = Math.Min(old.Length, now.Length);
            int i = 0;
            while (i < common)
            {
                if (old[i] == now[i])
                {
                    i++;
                    continue;
                }
                int start = i, end = i + 1, same = 0;
                for (int j = i + 1; j < common && same < Join; j++)
                {
                    if (old[j] == now[j])
                        same++;
                    else
                    {
                        same = 0;
                        end = j + 1;
                    }
                }
                ranges.Add(new Range(start, now[start..end]));
                i = end;
            }
            if (now.Length > old.Length)
                ranges.Add(new Range(old.Length, now[old.Length..]));
            return new Edit
            {
                SizeBefore = old.Length, CrcBefore = Delta.Crc(old), SizeAfter = now.Length, CrcAfter = Delta.Crc(now), Ranges = ranges
            };
        }

        /// <summary>
        /// The edit that puts the original back: the bytes each range overwrites (within the old length), the bytes a
        /// shorter result cuts off the end, then the old length.
        /// </summary>
        public static Edit Undo(Edit edit, Stream original)
        {
            var ranges = new List<Range>();
            foreach (Range r in edit.Ranges)
            {
                long end = Math.Min(r.Offset + r.Bytes.Length, edit.SizeBefore);
                if (r.Offset >= end)
                    continue;
                byte[] bytes = new byte[end - r.Offset];
                original.Position = r.Offset;
                original.ReadExactly(bytes);
                ranges.Add(new Range(r.Offset, bytes));
            }
            // A shorter result truncates the file: keep the bytes beyond the new length too, or the undo would grow the file
            // back with zeros (found by re/test/test copies.sh: IgRules.txt, IgRuleParameters.txt and a ruin template shrink
            // in 0.695). The edit's ranges stop at the new length then, so this range doesn't overlap them.
            if (edit.SizeAfter < edit.SizeBefore)
            {
                byte[] tail = new byte[edit.SizeBefore - edit.SizeAfter];
                original.Position = edit.SizeAfter;
                original.ReadExactly(tail);
                ranges.Add(new Range(edit.SizeAfter, tail));
            }
            return new Edit
            {
                SizeBefore = edit.SizeAfter, CrcBefore = edit.CrcAfter, SizeAfter = edit.SizeBefore, CrcAfter = edit.CrcBefore, Ranges = ranges
            };
        }

        /// <summary>
        /// Applies the edit to an open file in place, then flushes it to disk. The caller checks the result (CRC32): the file's
        /// size must be <see cref="Edit.SizeBefore"/> to start with.
        /// </summary>
        public static void Apply(Edit edit, FileStream file)
        {
            if (file.Length != edit.SizeBefore)
                throw new InvalidDataException($"The file is {file.Length:N0} bytes, not the {edit.SizeBefore:N0} this edit is for.");
            foreach (Range r in edit.Ranges)
            {
                file.Position = r.Offset;
                file.Write(r.Bytes);
            }
            file.SetLength(edit.SizeAfter);
            file.Flush(true);
        }

        public static byte[] Write(Edit edit)
        {
            var output = new MemoryStream();
            var w = new BinaryWriter(output);
            w.Write(Magic);
            w.Write((ulong)edit.SizeBefore);
            w.Write(edit.CrcBefore);
            w.Write((ulong)edit.SizeAfter);
            w.Write(edit.CrcAfter);
            w.Write(edit.Ranges.Count);
            foreach (Range r in edit.Ranges)
            {
                w.Write((ulong)r.Offset);
                w.Write(r.Bytes.Length);
                w.Write(r.Bytes);
            }
            return output.ToArray();
        }

        /// <summary>
        /// Reads an edit. Its numbers are checked against the data itself (a range can't claim more bytes than the edit
        /// holds, or lie past the larger of the two sizes), so a damaged or hostile edit fails here instead of allocating or
        /// writing what it claims.
        /// </summary>
        public static Edit Read(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            try
            {
                if (!r.ReadBytes(4).AsSpan().SequenceEqual(Magic))
                    throw new InvalidDataException("Not an in-place edit.");
                long sizeBefore = (long)r.ReadUInt64();
                uint crcBefore = r.ReadUInt32();
                long sizeAfter = (long)r.ReadUInt64();
                uint crcAfter = r.ReadUInt32();
                int n = r.ReadInt32();
                if (sizeBefore < 0 || sizeAfter < 0 || n < 0 || n > (data.Length - r.BaseStream.Position) / 12)
                    throw new InvalidDataException("The edit is damaged.");
                var ranges = new List<Range>(n);
                for (int i = 0; i < n; i++)
                {
                    long offset = (long)r.ReadUInt64();
                    int length = r.ReadInt32();
                    if (offset < 0 || length < 0 || length > data.Length - r.BaseStream.Position
                        || offset > Math.Max(sizeBefore, sizeAfter) - length)
                        throw new InvalidDataException("The edit is damaged.");
                    ranges.Add(new Range(offset, r.ReadBytes(length)));
                }
                if (r.BaseStream.Position != data.Length)
                    throw new InvalidDataException("The edit is damaged (data after its last range).");
                return new Edit { SizeBefore = sizeBefore, CrcBefore = crcBefore, SizeAfter = sizeAfter, CrcAfter = crcAfter, Ranges = ranges };
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("The edit is damaged (cut short).");
            }
        }

        /// <summary>
        /// Throws unless the ranges are in order and don't overlap (as <see cref="Create"/> makes them): then the result is
        /// fully determined by the base file and the ranges, whatever order they are written in, so its SHA-256 can be
        /// worked out before the file is touched (<see cref="ResultSha256"/>).
        /// </summary>
        public static void CheckOrdered(Edit edit)
        {
            long end = 0;
            foreach (Range r in edit.Ranges)
            {
                if (r.Offset < end)
                    throw new InvalidDataException("The edit is damaged (its ranges overlap or are out of order).");
                end = r.Offset + r.Bytes.Length;
            }
        }

        /// <summary>
        /// The SHA-256 the file would have after the edit, worked out by reading <paramref name="original"/> (positioned
        /// anywhere; read from the start) with the edit's ranges laid over it, without writing anything. Bytes past the old
        /// end that no range covers come out as zeros, as <see cref="Apply"/>'s SetLength leaves them. The ranges must be in
        /// order (<see cref="CheckOrdered"/>).
        /// </summary>
        public static string ResultSha256(Edit edit, Stream original)
        {
            CheckOrdered(edit);
            if (original.Length != edit.SizeBefore)
                throw new InvalidDataException($"The file is {original.Length:N0} bytes, not the {edit.SizeBefore:N0} this edit is for.");
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] buffer = new byte[1 << 20];
            long pos = 0;
            original.Position = 0;
            // The original's bytes from pos up to (not including) end; zeros past its old length.
            void Base(long end)
            {
                while (pos < end)
                {
                    int n = (int)Math.Min(buffer.Length, end - pos);
                    if (pos < edit.SizeBefore)
                    {
                        n = (int)Math.Min(n, edit.SizeBefore - pos);
                        original.Position = pos;
                        original.ReadExactly(buffer, 0, n);
                    }
                    else
                        Array.Clear(buffer, 0, n);
                    sha.AppendData(buffer, 0, n);
                    pos += n;
                }
            }
            foreach (Range r in edit.Ranges)
            {
                if (r.Offset >= edit.SizeAfter)
                    break;
                Base(r.Offset);
                int keep = (int)Math.Min(r.Bytes.Length, edit.SizeAfter - r.Offset);
                sha.AppendData(r.Bytes, 0, keep);
                pos = r.Offset + keep;
            }
            Base(edit.SizeAfter);
            return Convert.ToHexString(sha.GetHashAndReset());
        }

        // ---- Hard links: a file shared with another install must not be edited in place --------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint Attributes;
            public long CreationTime, LastAccessTime, LastWriteTime;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out ByHandleFileInformation info);

        /// <summary>How many names the file has on disk (more than 1: a hard link, shared with another folder).</summary>
        public static uint Links(FileStream file)
            => GetFileInformationByHandle(file.SafeFileHandle, out ByHandleFileInformation info) ? info.Links : 1;
    }
}
