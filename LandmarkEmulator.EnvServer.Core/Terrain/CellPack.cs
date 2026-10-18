using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// One landscape's edited cells in a single append-only file, <c>cells.pack</c>, beside (or instead of) the old one
    /// file per cell (<c>&lt;key&gt;.cell</c>). Over a Docker bind mount on Windows every file open costs milliseconds:
    /// 45,000 cell files (the home continent and one island with their Chaos Cavern outposts) took ~5.5 minutes to load
    /// (2026-10-17), and each further island adds ~23,000. One file reads in one pass.
    /// <para>
    /// Layout: header "LMCP" + u32 format 1; then records {u64 key (cell id | layer &lt;&lt; 56), u32 version, i32 length
    /// (−1 = the cell was emptied and dropped), i64 save time (Unix seconds; what a cell file's write time was), length
    /// bytes = the packed cell (BlockData.Save)}. Records are only ever
    /// appended (a save appends the cell's new state); at load the last record of a key wins. A record cut short by a
    /// crash ends the file: it is ignored and cut off. When the file holds more than twice its live bytes (+ 32 MB) it is
    /// rewritten with only the live records, written to a temporary file first and moved into place.
    /// </para>
    /// </summary>
    public sealed class CellPack : IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const string FileName = "cells.pack";
        private static readonly byte[] Magic = { (byte)'L', (byte)'M', (byte)'C', (byte)'P', 1, 0, 0, 0 };
        private const int RecordHeader = 8 + 4 + 4 + 8;

        /// <summary>Where a cell's latest record is: its version, and the packed bytes' offset and length.</summary>
        public readonly record struct Entry(uint Version, long Offset, int Length, DateTime Saved);

        private readonly string path;
        private readonly object io = new();
        private FileStream stream;
        /// <summary>Where a failed append that could not be cut back at once must be cut before the next one (-1 = none).</summary>
        private long cutTo = -1;

        public CellPack(string folder) => path = Path.Combine(folder, FileName);

        public static bool ExistsIn(string folder) => File.Exists(Path.Combine(folder, FileName));

        private FileStream Stream
        {
            get
            {
                if (stream == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
                    if (stream.Length == 0)
                    {
                        stream.Write(Magic);
                        stream.Flush(true);
                    }
                }
                return stream;
            }
        }

        /// <summary>
        /// Reads the whole file: the latest entry of every live cell. <paramref name="onCell"/> sees each live record's
        /// bytes as it passes (for checks at load); a cut-off tail is dropped; a file holding mostly dead records is
        /// rewritten (compaction).
        /// </summary>
        public Dictionary<ulong, Entry> Load(Action<ulong, uint, byte[]> onCell = null)
        {
            var entries = new Dictionary<ulong, Entry>();
            lock (io)
            {
                FileStream s = Stream;
                s.Position = 0;
                var head = new byte[Magic.Length];
                if (s.Read(head, 0, head.Length) != head.Length || !head.AsSpan().SequenceEqual(Magic))
                    throw new InvalidDataException($"{path} is not a cell pack");
                long good = s.Position, dead = 0;
                var header = new byte[RecordHeader];
                var scratch = new byte[1 << 16];
                // Not disposed: that would close the pack's own stream.
                var reader = new BufferedStream(s, 1 << 20);
                {
                    while (true)
                    {
                        if (reader.Read(header, 0, RecordHeader) != RecordHeader)
                            break;
                        ulong key = BitConverter.ToUInt64(header, 0);
                        uint version = BitConverter.ToUInt32(header, 8);
                        int length = BitConverter.ToInt32(header, 12);
                        DateTime saved = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(BitConverter.ToInt64(header, 16), 0, 253402300799)).UtcDateTime;
                        long at = good + RecordHeader;
                        byte[] data = null;
                        if (length > 0)
                        {
                            // Only a caller that looks at the bytes gets its own copy; otherwise one scratch buffer.
                            data = onCell != null ? new byte[length] : (scratch.Length >= length ? scratch : scratch = new byte[length]);
                            int got = 0, n;
                            while (got < length && (n = reader.Read(data, got, length - got)) > 0)
                                got += n;
                            if (got != length)
                                break;
                        }
                        if (entries.TryGetValue(key, out Entry old))
                            dead += RecordHeader + Math.Max(0, old.Length);
                        if (length < 0)
                        {
                            entries.Remove(key);
                            dead += RecordHeader;
                        }
                        else
                        {
                            entries[key] = new Entry(version, at, length, saved);
                            onCell?.Invoke(key, version, data);
                        }
                        good = at + Math.Max(0, length);
                    }
                }
                if (s.Length > good)
                {
                    log.Warn($"{path}: {s.Length - good} byte(s) of a cut-off record at the end dropped.");
                    s.SetLength(good);
                }
                long live = good - Magic.Length - dead;
                if (dead > live + (32L << 20))
                    entries = Compact(entries);
            }
            return entries;
        }

        /// <summary>The packed bytes of one record.</summary>
        public byte[] Read(Entry entry)
        {
            lock (io)
            {
                FileStream s = Stream;
                var data = new byte[entry.Length];
                s.Position = entry.Offset;
                int read = 0;
                while (read < data.Length)
                {
                    int n = s.Read(data, read, data.Length - read);
                    if (n <= 0)
                        throw new EndOfStreamException($"{path}: record at {entry.Offset} cut short");
                    read += n;
                }
                return data;
            }
        }

        /// <summary>
        /// Appends records (data null = the cell was dropped) and flushes them to the disk; returns each live record's
        /// entry in the same order (default for dropped ones).
        /// </summary>
        public Entry[] Append(IReadOnlyList<(ulong Key, uint Version, byte[] Data, DateTime Saved)> records)
        {
            var result = new Entry[records.Count];
            lock (io)
            {
                FileStream s = Stream;
                // An earlier failed append that could not be cut back is cut now, before anything goes after it.
                if (cutTo >= 0)
                {
                    s.SetLength(cutTo);
                    cutTo = -1;
                }
                s.Position = s.Length;
                using var buffer = new MemoryStream();
                long start = s.Position;
                for (int i = 0; i < records.Count; i++)
                {
                    (ulong key, uint version, byte[] data, DateTime saved) = records[i];
                    buffer.Write(BitConverter.GetBytes(key));
                    buffer.Write(BitConverter.GetBytes(version));
                    buffer.Write(BitConverter.GetBytes(data == null ? -1 : data.Length));
                    buffer.Write(BitConverter.GetBytes(new DateTimeOffset(DateTime.SpecifyKind(saved, DateTimeKind.Utc)).ToUnixTimeSeconds()));
                    if (data != null)
                    {
                        result[i] = new Entry(version, start + buffer.Position, data.Length, saved);
                        buffer.Write(data);
                    }
                }
                buffer.Position = 0;
                try
                {
                    buffer.CopyTo(s);
                    s.Flush(true);
                }
                catch (Exception) when (TryCutBack(s, start))
                {
                    // Never reached (the filter returns false): the half-written records are cut off, then it rethrows.
                }
            }
            return result;
        }

        /// <summary>
        /// A failed append (disk full, I/O error) may have written part of its records. Left in place, the next append
        /// would go after that partial record, and at load the partial record's length would swallow the records behind
        /// it: Load stops at the first record it can't read and cuts the file there, so every later save of the landscape
        /// would be lost. So the file is cut back to where the append started (best effort) and the error goes on to the
        /// caller, which keeps the cells to save again (TerrainEdits.Flush). Always false (an exception filter that only
        /// cleans up).
        /// </summary>
        private bool TryCutBack(FileStream s, long start)
        {
            try
            {
                s.SetLength(start);
                s.Flush(true);
            }
            catch (Exception e)
            {
                log.Error($"{path}: could not cut a failed append back to {start} ({e.Message}); the next save cuts it first.");
                cutTo = start;
                try
                {
                    s.Dispose();
                }
                catch (Exception)
                {
                }
                stream = null;
            }
            return false;
        }

        /// <summary>Rewrites the file with only the live records (temporary file first, then moved into place).</summary>
        private Dictionary<ulong, Entry> Compact(Dictionary<ulong, Entry> entries)
        {
            string temporary = path + ".compacting";
            var moved = new Dictionary<ulong, Entry>(entries.Count);
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                output.Write(Magic);
                foreach ((ulong key, Entry entry) in entries)
                {
                    byte[] data = new byte[entry.Length];
                    stream.Position = entry.Offset;
                    stream.ReadExactly(data);
                    output.Write(BitConverter.GetBytes(key));
                    output.Write(BitConverter.GetBytes(entry.Version));
                    output.Write(BitConverter.GetBytes(data.Length));
                    output.Write(BitConverter.GetBytes(new DateTimeOffset(DateTime.SpecifyKind(entry.Saved, DateTimeKind.Utc)).ToUnixTimeSeconds()));
                    moved[key] = new Entry(entry.Version, output.Position, data.Length, entry.Saved);
                    output.Write(data);
                }
                output.Flush(true);
            }
            long before = stream.Length;
            stream.Dispose();
            stream = null;
            File.Move(temporary, path, overwrite: true);
            log.Info($"{path}: compacted {before / 1024} KB → {new FileInfo(path).Length / 1024} KB ({moved.Count} cells).");
            return moved;
        }

        public void Dispose()
        {
            lock (io)
            {
                stream?.Dispose();
                stream = null;
            }
        }
    }
}
