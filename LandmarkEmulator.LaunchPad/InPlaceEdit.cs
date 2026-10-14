using System.IO;
using System.Runtime.InteropServices;

namespace LandmarkEmulator.LaunchPad
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

        /// <summary>The edit that puts the original back: the bytes each range overwrites (within the old length), then the old length.</summary>
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

        public static Edit Read(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            if (!r.ReadBytes(4).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Not an in-place edit.");
            long sizeBefore = (long)r.ReadUInt64();
            uint crcBefore = r.ReadUInt32();
            long sizeAfter = (long)r.ReadUInt64();
            uint crcAfter = r.ReadUInt32();
            int n = r.ReadInt32();
            var ranges = new List<Range>(n);
            for (int i = 0; i < n; i++)
            {
                long offset = (long)r.ReadUInt64();
                int length = r.ReadInt32();
                if (offset < 0 || length < 0 || offset + length > Math.Max(sizeBefore, sizeAfter))
                    throw new InvalidDataException("The edit is damaged.");
                ranges.Add(new Range(offset, r.ReadBytes(length)));
            }
            return new Edit { SizeBefore = sizeBefore, CrcBefore = crcBefore, SizeAfter = sizeAfter, CrcAfter = crcAfter, Ranges = ranges };
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
