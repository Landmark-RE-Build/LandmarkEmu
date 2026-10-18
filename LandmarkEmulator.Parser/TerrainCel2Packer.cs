using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.Parser
{
    /// <summary>
    /// Builds <c>TerrainCellBlobStore.bin</c> (CEL2) and copies manifest/raws from parsed
    /// <c>0xEC000500</c> / <c>0xEC000A00</c> capture bins. See <c>docs/terrain.md</c> ("CellsUpdate entries").
    /// </summary>
    public static class TerrainCel2Packer
    {
        public const uint Cel2Magic = 0x324C4543; // "CEL2"

        /// <summary>
        /// One cell of a TerrainCell 5 CellsUpdate, as the 0.695 client reads it [P]: reader FUN_1431464f0 → list
        /// FUN_143148950 of <c>VoxelFarm::CellIdVersionFlagsAndData</c> (FUN_143146320: u64 cell, u32 version, u32 n ×
        /// layer), each layer FUN_143146cf0: u32 layer id, u32 layer version, u8 flag, u32 size + size bytes.
        /// </summary>
        public sealed class CellRecord
        {
            public ulong CellId { get; init; }
            /// <summary>
            /// The cell version, entry +0x08 of CellIdVersionFlagsAndData (FUN_143146320) [P]. HandleCellsUpdate
            /// (FUN_144016a90) takes 0 as block data (voxels the client meshes, FUN_144016150) and any other value as
            /// finished geometry (FUN_1440164c0).
            /// </summary>
            public uint Version { get; init; }
            /// <summary>The number of layers that follow (FUN_143146320's layer list count) [P].</summary>
            public uint SegmentCount { get; init; }
            /// <summary>
            /// The first layer's u32 layer id (FUN_143146cf0 +0x00; 0-3 = Poi / Permanent / Timed Persistent /
            /// Transient) [P]. Was named "Flags" here: the same 4 bytes. 0 when the cell carries no layer.
            /// </summary>
            public uint FirstLayerId { get; init; }
            /// <summary>
            /// Everything after <see cref="FirstLayerId"/>: the first layer's version, flag, size and data, then each
            /// further layer whole (its u32 layer id first).
            /// </summary>
            public byte[] DataBlob { get; init; }
        }

        public sealed class PackResult
        {
            public int CellsUpdateFiles { get; init; }
            public int CellsExtracted { get; init; }
            public int UniqueCells { get; init; }
            public int MultiSegCells { get; init; }
            public string BlobStorePath { get; init; }
            public string ManifestPath { get; init; }
            public long BlobStoreBytes { get; init; }
            public long ManifestBytes { get; init; }
        }

        public sealed class MergeResult
        {
            public int ExistingCells { get; init; }
            public int MissIdsRequested { get; init; }
            public int AlreadyPresent { get; init; }
            public int ResolvedPrimary { get; init; }
            public int ResolvedFallback { get; init; }
            public int ResolvedPacketDumpsRaw { get; init; }
            public int ResolvedScanAll { get; init; }
            public int ResolvedTemplate { get; init; }
            public int StillMissing { get; init; }
            public int TotalCells { get; init; }
            public long BlobStoreBytes { get; init; }
            public string BlobStorePath { get; init; }
            public IReadOnlyList<ulong> UnresolvedIds { get; init; }
        }

        /// <summary>
        /// Pack all <c>*CellsUpdate*0xEC000500*.bin</c> under <paramref name="binsDir"/> into CEL2,
        /// copy the zone-in <c>0xEC000A00</c> (or <c>TerrainCellManifest.bin</c>) to PacketDumps,
        /// and refresh raw CellsUpdate reference bins + donor template.
        /// </summary>
        public static PackResult PackFromParsedBins(string binsDir, string packetDumpsDir)
        {
            if (!Directory.Exists(binsDir))
                throw new DirectoryNotFoundException(binsDir);
            Directory.CreateDirectory(packetDumpsDir);

            var cells = new Dictionary<ulong, CellRecord>();
            int files = 0;
            int extracted = 0;
            var cellsUpdateFiles = Directory.GetFiles(binsDir, "*CellsUpdate*0xEC000500*.bin")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (cellsUpdateFiles.Count == 0)
                cellsUpdateFiles = Directory.GetFiles(binsDir, "*0xEC000500*.bin")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var file in cellsUpdateFiles)
            {
                var list = ReadCellsUpdateFile(file);
                files++;
                extracted += list.Count;
                foreach (var cell in list)
                    cells[cell.CellId] = cell; // last write wins
            }

            string blobPath = Path.Combine(packetDumpsDir, "TerrainCellBlobStore.bin");
            WriteCel2(blobPath, cells.Values.OrderBy(c => c.CellId));

            string manifestSrc = FindManifestBin(binsDir)
                ?? throw new FileNotFoundException($"No TerrainCellManifest / 0xEC000A00 bin in {binsDir}");
            string manifestDst = Path.Combine(packetDumpsDir, "TerrainCellManifest.bin");
            File.Copy(manifestSrc, manifestDst, overwrite: true);

            // Reference raws (full wire bins including opcode) — first two multi-cell updates by size.
            var bySize = cellsUpdateFiles
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.Length)
                .Take(2)
                .ToList();
            if (bySize.Count > 0)
                File.Copy(bySize[0].FullName, Path.Combine(packetDumpsDir, "TerrainCellCellsUpdateRaw0015.bin"), true);
            if (bySize.Count > 1)
                File.Copy(bySize[1].FullName, Path.Combine(packetDumpsDir, "TerrainCellCellsUpdateRaw0016.bin"), true);

            // Donor template = DataBlob of smallest non-empty data cell (zlib-shaped).
            var donor = cells.Values
                .Where(c => c.DataBlob != null && c.DataBlob.Length >= 16)
                .OrderBy(c => c.DataBlob.Length)
                .FirstOrDefault();
            if (donor != null)
                File.WriteAllBytes(Path.Combine(packetDumpsDir, "TerrainCellCellsUpdateCellTemplate.bin"), donor.DataBlob);

            int multi = cells.Values.Count(c => c.SegmentCount > 1);
            return new PackResult
            {
                CellsUpdateFiles = files,
                CellsExtracted = extracted,
                UniqueCells = cells.Count,
                MultiSegCells = multi,
                BlobStorePath = blobPath,
                ManifestPath = manifestDst,
                BlobStoreBytes = new FileInfo(blobPath).Length,
                ManifestBytes = new FileInfo(manifestDst).Length
            };
        }

        public static List<CellRecord> ReadCellsUpdateFile(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int pos = 0;
            if (data.Length >= 4 && data[0] == 0xEC && data[1] == 0x00 && data[2] == 0x05 && data[3] == 0x00)
                pos = 4;

            if (pos + 6 > data.Length)
                throw new InvalidDataException($"{path}: truncated header");

            // u16 (the high half of the 32-bit sub-opcode), u32 cell count (FUN_1431464f0 → FUN_143148950).
            _ = BitConverter.ToUInt16(data, pos);
            uint count = BitConverter.ToUInt32(data, pos + 2);
            pos += 6;
            if (count > (data.Length - pos) / 16)
                throw new InvalidDataException($"{path}: cell count {count} beyond the data");

            var list = new List<CellRecord>((int)count);
            for (int i = 0; i < count; i++)
            {
                if (pos + 16 > data.Length)
                    throw new InvalidDataException($"{path}: eof at cell header {i}/{count} pos={pos}");

                ulong id = BitConverter.ToUInt64(data, pos);
                uint version = BitConverter.ToUInt32(data, pos + 8);
                uint layerCount = BitConverter.ToUInt32(data, pos + 12);
                pos += 16;

                uint firstLayerId = 0;
                if (layerCount > 0)
                {
                    if (pos + 4 > data.Length)
                        throw new InvalidDataException($"{path}: eof at first layer id cell={i} pos={pos}");
                    firstLayerId = BitConverter.ToUInt32(data, pos);
                    pos += 4;
                }

                int bodyStart = pos;
                for (uint s = 0; s < layerCount; s++)
                {
                    if (s > 0)
                    {
                        if (pos + 4 > data.Length)
                            throw new InvalidDataException($"{path}: eof layer id cell={i} layer={s}");
                        pos += 4; // the next layer's u32 layer id
                    }

                    pos += MeasureSegmentBody(data, pos, path, i);
                }

                int bodyLen = pos - bodyStart;
                var blob = new byte[bodyLen];
                Buffer.BlockCopy(data, bodyStart, blob, 0, bodyLen);
                list.Add(new CellRecord
                {
                    CellId = id,
                    Version = version,
                    SegmentCount = layerCount,
                    FirstLayerId = firstLayerId,
                    DataBlob = blob
                });
            }

            return list;
        }

        /// <summary>
        /// Length of one layer after its u32 layer id (FUN_143146cf0) [P]: <c>u32 layer version, u8 flag, u32 size,
        /// size bytes</c> = 9 + size, so an empty layer is 9 bytes (this read 15 for a layer with version 0 and size 0,
        /// which only lined up when it was the packet's last layer: the 6 bytes after the last cell are the packet
        /// tail, u32 transaction count 0 and two bools, FUN_143148b80 / +0x48 / +0x49). The layer version is the CRC32
        /// of the packed layer (docs/terrain.md).
        /// </summary>
        internal static int MeasureSegmentBody(byte[] data, int pos, string path, int cellIndex)
        {
            if (pos + 9 > data.Length)
                throw new InvalidDataException($"{path}: eof layer hdr cell={cellIndex} pos={pos}");

            uint size = BitConverter.ToUInt32(data, pos + 5);
            if (size > 16 * 1024 * 1024)
                throw new InvalidDataException($"{path}: bad data size {size} cell={cellIndex} pos={pos}");
            int total = 9 + (int)size;
            if (pos + total > data.Length)
                throw new InvalidDataException($"{path}: overrun data body cell={cellIndex} need={total}");
            return total;
        }

        public static void WriteCel2(string path, IEnumerable<CellRecord> cells)
        {
            var list = cells.ToList();
            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            bw.Write(Cel2Magic);
            bw.Write((uint)list.Count);
            foreach (var c in list)
            {
                bw.Write(c.CellId);
                bw.Write(c.Version);
                bw.Write(c.SegmentCount);
                bw.Write(c.FirstLayerId);
                var blob = c.DataBlob ?? Array.Empty<byte>();
                bw.Write(blob.Length);
                bw.Write(blob);
            }
        }

        public static Dictionary<ulong, CellRecord> ReadCel2(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            var cells = new Dictionary<ulong, CellRecord>();
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            uint magic = br.ReadUInt32();
            if (magic != Cel2Magic)
                throw new InvalidDataException($"{path}: bad magic 0x{magic:X8} (expected CEL2)");

            uint count = br.ReadUInt32();
            for (uint i = 0; i < count; i++)
            {
                ulong id = br.ReadUInt64();
                uint version = br.ReadUInt32();
                uint segmentCount = br.ReadUInt32();
                uint firstLayerId = br.ReadUInt32();
                int len = br.ReadInt32();
                if (len < 0 || len > 16 * 1024 * 1024)
                    throw new InvalidDataException($"{path}: bad blob len {len} at cell {i}");
                byte[] blob = br.ReadBytes(len);
                if (blob.Length != len)
                    throw new InvalidDataException($"{path}: truncated blob at cell {i}");

                cells[id] = new CellRecord
                {
                    CellId = id,
                    Version = version,
                    SegmentCount = segmentCount,
                    FirstLayerId = firstLayerId,
                    DataBlob = blob
                };
            }

            return cells;
        }

        public static HashSet<ulong> ParseMissIds(string missIdsPath)
        {
            var ids = new HashSet<ulong>();
            foreach (string raw in File.ReadAllLines(missIdsPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                string hex = line.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? line[2..] : line;
                if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong id))
                    throw new FormatException($"Bad cellId line in {missIdsPath}: {raw}");
                ids.Add(id);
            }

            return ids;
        }

        /// <summary>
        /// Index all CellsUpdate cells under <paramref name="binsDir"/> (last write wins per cellId).
        /// </summary>
        public static Dictionary<ulong, CellRecord> IndexCellsFromBins(string binsDir)
        {
            if (!Directory.Exists(binsDir))
                throw new DirectoryNotFoundException(binsDir);

            var files = Directory.GetFiles(binsDir, "*CellsUpdate*0xEC000500*.bin")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
                files = Directory.GetFiles(binsDir, "*0xEC000500*.bin")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            return IndexCellsFromBinFiles(files);
        }

        /// <summary>Index CellsUpdate records from explicit bin paths (last write wins).</summary>
        public static Dictionary<ulong, CellRecord> IndexCellsFromBinFiles(IEnumerable<string> binFiles)
        {
            var cells = new Dictionary<ulong, CellRecord>();
            foreach (string file in binFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file))
                    continue;
                try
                {
                    foreach (CellRecord cell in ReadCellsUpdateFile(file))
                        cells[cell.CellId] = cell;
                }
                catch (InvalidDataException)
                {
                    // skip non-CellsUpdate bins
                }
            }

            return cells;
        }

        static CellRecord LoadDonorTemplateRecord(string packetDumpsDir, Dictionary<ulong, CellRecord> existing)
        {
            string templatePath = Path.Combine(packetDumpsDir, "TerrainCellCellsUpdateCellTemplate.bin");
            byte[] blob = File.Exists(templatePath) ? File.ReadAllBytes(templatePath) : null;
            if (blob == null || blob.Length == 0)
            {
                blob = existing.Values
                    .Where(c => c.DataBlob != null && c.DataBlob.Length >= 16)
                    .OrderBy(c => c.DataBlob.Length)
                    .Select(c => c.DataBlob)
                    .FirstOrDefault();
            }

            if (blob == null || blob.Length == 0)
                return null;

            CellRecord meta = existing.Values
                .FirstOrDefault(c => c.DataBlob != null && c.DataBlob.Length >= 16)
                ?? existing.Values.FirstOrDefault();

            return new CellRecord
            {
                CellId = 0,
                Version = meta?.Version ?? 0,
                SegmentCount = meta?.SegmentCount ?? 1,
                FirstLayerId = meta?.FirstLayerId ?? 2,
                DataBlob = blob
            };
        }

        static IEnumerable<string> PacketDumpsRawBins(string packetDumpsDir)
        {
            if (string.IsNullOrEmpty(packetDumpsDir) || !Directory.Exists(packetDumpsDir))
                yield break;

            foreach (string name in new[]
                     {
                         "TerrainCellCellsUpdateRaw0015.bin",
                         "TerrainCellCellsUpdateRaw0016.bin"
                     })
            {
                string path = Path.Combine(packetDumpsDir, name);
                if (File.Exists(path))
                    yield return path;
            }
        }

        /// <summary>
        /// Merge capture blobs for <paramref name="missIds"/> into an existing CEL2 store.
        /// Searches <paramref name="primaryBinsDir"/> first, then <paramref name="fallbackBinsDir"/>.
        /// When <paramref name="scanAllCapturesRoot"/> is set, remaining IDs are searched under
        /// <c>*/bins</c> session folders (skipping primary/fallback paths).
        /// Does not touch manifest/spawn/raws; donor template is never used as a CEL2 entry.
        /// </summary>
        public static MergeResult MergeCel2(
            string cel2Path,
            string missIdsPath,
            string primaryBinsDir,
            string fallbackBinsDir,
            string outputDir = null,
            string scanAllCapturesRoot = null)
        {
            outputDir ??= Path.GetDirectoryName(cel2Path)
                ?? throw new ArgumentException("Cannot infer output dir from cel2Path");

            var existing = ReadCel2(cel2Path);
            int cellsBeforeMerge = existing.Count;
            var missIds = ParseMissIds(missIdsPath);
            var primary = IndexCellsFromBins(primaryBinsDir);
            Dictionary<ulong, CellRecord> fallback = null;
            if (!string.IsNullOrEmpty(fallbackBinsDir) && Directory.Exists(fallbackBinsDir)
                && !string.Equals(Path.GetFullPath(fallbackBinsDir), Path.GetFullPath(primaryBinsDir),
                    StringComparison.OrdinalIgnoreCase))
            {
                fallback = IndexCellsFromBins(fallbackBinsDir);
            }

            int alreadyPresent = 0;
            int resolvedPrimary = 0;
            int resolvedFallback = 0;
            int resolvedPacketDumpsRaw = 0;
            int resolvedScanAll = 0;
            int resolvedTemplate = 0;
            var unresolved = new List<ulong>();
            var pending = new List<ulong>();

            foreach (ulong id in missIds.OrderBy(x => x))
            {
                if (existing.ContainsKey(id))
                {
                    alreadyPresent++;
                    continue;
                }

                if (primary.TryGetValue(id, out CellRecord rec))
                {
                    existing[id] = rec;
                    resolvedPrimary++;
                    continue;
                }

                if (fallback != null && fallback.TryGetValue(id, out rec))
                {
                    existing[id] = rec;
                    resolvedFallback++;
                    continue;
                }

                pending.Add(id);
            }

            if (pending.Count > 0)
            {
                var rawIndex = IndexCellsFromBinFiles(PacketDumpsRawBins(outputDir));
                if (rawIndex.Count > 0)
                {
                    var stillPending = new List<ulong>();
                    foreach (ulong id in pending)
                    {
                        if (rawIndex.TryGetValue(id, out CellRecord rec))
                        {
                            existing[id] = rec;
                            resolvedPacketDumpsRaw++;
                        }
                        else
                        {
                            stillPending.Add(id);
                        }
                    }

                    pending = stillPending;
                }
            }

            if (pending.Count > 0 && !string.IsNullOrEmpty(scanAllCapturesRoot) && Directory.Exists(scanAllCapturesRoot))
            {
                string primaryFull = Path.GetFullPath(primaryBinsDir);
                string fallbackFull = string.IsNullOrEmpty(fallbackBinsDir) ? null : Path.GetFullPath(fallbackBinsDir);
                var scanIndices = new List<(string name, Dictionary<ulong, CellRecord> index)>();

                foreach (string sessionDir in Directory.GetDirectories(scanAllCapturesRoot)
                             .Where(d => !Path.GetFileName(d).StartsWith("_", StringComparison.Ordinal))
                             .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string bins = Path.Combine(sessionDir, "bins");
                    if (!Directory.Exists(bins))
                        continue;
                    string binsFull = Path.GetFullPath(bins);
                    if (string.Equals(binsFull, primaryFull, StringComparison.OrdinalIgnoreCase)
                        || (fallbackFull != null && string.Equals(binsFull, fallbackFull, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    try
                    {
                        scanIndices.Add((Path.GetFileName(sessionDir), IndexCellsFromBins(bins)));
                    }
                    catch (IOException)
                    {
                        // skip unreadable session bins
                    }
                    catch (InvalidDataException)
                    {
                        // skip corrupt bins
                    }
                }

                var stillPending = new List<ulong>();
                foreach (ulong id in pending)
                {
                    CellRecord rec = null;
                    foreach (var (_, index) in scanIndices)
                    {
                        if (index.TryGetValue(id, out rec))
                            break;
                    }

                    if (rec != null)
                    {
                        existing[id] = rec;
                        resolvedScanAll++;
                    }
                    else
                    {
                        stillPending.Add(id);
                    }
                }

                pending = stillPending;
            }

            if (pending.Count > 0)
            {
                CellRecord template = LoadDonorTemplateRecord(outputDir, existing);
                if (template != null)
                {
                    foreach (ulong id in pending)
                    {
                        existing[id] = new CellRecord
                        {
                            CellId = id,
                            Version = template.Version,
                            SegmentCount = template.SegmentCount,
                            FirstLayerId = template.FirstLayerId,
                            DataBlob = template.DataBlob
                        };
                        resolvedTemplate++;
                    }

                    pending = new List<ulong>();
                }
            }

            unresolved.AddRange(pending);

            string outPath = Path.Combine(outputDir, "TerrainCellBlobStore.bin");
            WriteCel2(outPath, existing.Values.OrderBy(c => c.CellId));
            long bytes = new FileInfo(outPath).Length;

            return new MergeResult
            {
                ExistingCells = cellsBeforeMerge,
                MissIdsRequested = missIds.Count,
                AlreadyPresent = alreadyPresent,
                ResolvedPrimary = resolvedPrimary,
                ResolvedFallback = resolvedFallback,
                ResolvedPacketDumpsRaw = resolvedPacketDumpsRaw,
                ResolvedScanAll = resolvedScanAll,
                ResolvedTemplate = resolvedTemplate,
                StillMissing = unresolved.Count,
                TotalCells = existing.Count,
                BlobStoreBytes = bytes,
                BlobStorePath = outPath,
                UnresolvedIds = unresolved
            };
        }

        public static void AppendMergeProvenance(string provenancePath, MergeResult result, string missIdsPath,
            string primaryBinsDir, string fallbackBinsDir, string ticket)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"## CEL2 merge — {ticket} ({DateTime.UtcNow:yyyy-MM-dd HH:mm}Z)");
            sb.AppendLine();
            sb.AppendLine($"- **Miss IDs file:** `{missIdsPath}` ({result.MissIdsRequested} requested)");
            sb.AppendLine($"- **Primary bins:** `{primaryBinsDir}` → +{result.ResolvedPrimary} cells");
            if (!string.IsNullOrEmpty(fallbackBinsDir))
                sb.AppendLine($"- **Fallback bins:** `{fallbackBinsDir}` → +{result.ResolvedFallback} cells");
            if (result.ResolvedPacketDumpsRaw > 0)
                sb.AppendLine($"- **PacketDumps raws:** +{result.ResolvedPacketDumpsRaw} cells");
            if (result.ResolvedScanAll > 0)
                sb.AppendLine($"- **Scan-all captures:** +{result.ResolvedScanAll} cells");
            if (result.ResolvedTemplate > 0)
                sb.AppendLine($"- **Donor template (no capture blob):** +{result.ResolvedTemplate} cells");
            sb.AppendLine($"- **Already in CEL2:** {result.AlreadyPresent}");
            sb.AppendLine($"- **Still missing (no capture blob):** {result.StillMissing}");
            sb.AppendLine($"- **CEL2 total:** {result.TotalCells} cells ({result.BlobStoreBytes:N0} bytes)");
            if (result.UnresolvedIds.Count > 0)
            {
                sb.AppendLine($"- **Unresolved sample:** {string.Join(", ", result.UnresolvedIds.Take(8).Select(id => $"0x{id:X16}"))}"
                    + (result.UnresolvedIds.Count > 8 ? $" … (+{result.UnresolvedIds.Count - 8} more)" : ""));
            }

            sb.AppendLine();
            sb.AppendLine("Rebuild merge:");
            sb.AppendLine("```powershell");
            sb.AppendLine($"dotnet run --project LandmarkEmulator.Parser -c Release -- merge-cel2 {missIdsPath}");
            sb.AppendLine("```");

            File.AppendAllText(provenancePath, sb.ToString());
        }

        static string FindManifestBin(string binsDir)
        {
            string named = Path.Combine(binsDir, "TerrainCellManifest.bin");
            if (File.Exists(named) && new FileInfo(named).Length > 64)
                return named;

            return Directory.GetFiles(binsDir, "*ModifiedCellManifest*0xEC000A00*.bin")
                .Concat(Directory.GetFiles(binsDir, "*0xEC000A00*.bin"))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }

        public static string FormatProvenance(
            string captureStem,
            PackResult result,
            float spawnX, float spawnY, float spawnZ)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PacketDumps terrain provenance");
            sb.AppendLine();
            sb.AppendLine($"- **Green control capture:** `{captureStem}`");
            sb.AppendLine($"- **Packed (UTC):** {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z");
            sb.AppendLine($"- **CEL2 cells:** {result.UniqueCells} unique (from {result.CellsExtracted} entries in {result.CellsUpdateFiles} CellsUpdate files; multiSeg={result.MultiSegCells})");
            sb.AppendLine($"- **TerrainCellBlobStore.bin:** {result.BlobStoreBytes:N0} bytes");
            sb.AppendLine($"- **TerrainCellManifest.bin:** {result.ManifestBytes:N0} bytes (`0xEC000A00` wire dump, 4-byte opcode prefix kept)");
            sb.AppendLine($"- **Spawn (`SendSelf.Position`):** `({spawnX:F2}, {spawnY:F2}, {spawnZ:F2})` — must match this capture");
            sb.AppendLine();
            sb.AppendLine("Rebuild:");
            sb.AppendLine("```powershell");
            sb.AppendLine($"dotnet run --project LandmarkEmulator.Parser -c Release -- pack-terrain NetworkCaptures/_parsed/{captureStem}/bins LandmarkEmulator.WorldServer/Resources/PacketDumps");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("See [`docs/packet-dumps-terrain.md`](../../../docs/packet-dumps-terrain.md).");
            return sb.ToString();
        }
    }
}
