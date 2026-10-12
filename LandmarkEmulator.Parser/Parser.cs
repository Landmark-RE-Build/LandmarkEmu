using LandmarkEmulator.AuthServer.Network.Message;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.Parser
{
    public class Parser
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public enum Protocol
        {
            TCP = 6,
            UDP = 17,
            Unknown = -1
        }

        public enum GamePacketType
        {
            Auth,
            Gateway,
            Zone
        }

        public static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("[Landmark RE:Build Packet Parser]");

            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            ParseCliOptions options;
            try
            {
                options = ParseArgs(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                PrintUsage();
                return 1;
            }

            if (options.Mode is not "pack-terrain" and not "merge-cel2")
                InitManagers();

            var sw = Stopwatch.StartNew();
            int exit = options.Mode switch
            {
                "triage" => RunTriage(options),
                "parse" => RunParse(options),
                "timeline" => RunTimeline(options),
                "pack-terrain" => RunPackTerrain(options),
                "merge-cel2" => RunMergeCel2(options),
                _ => FailUnknownMode(options.Mode)
            };

            Console.WriteLine($"Finished in {sw.Elapsed:hh\\:mm\\:ss}");
            return exit;
        }

        private static int FailUnknownMode(string mode)
        {
            Console.Error.WriteLine($"Unknown mode: {mode}");
            PrintUsage();
            return 1;
        }

        private static void InitManagers()
        {
            // String tables are optional for packet RE; skip when gameData is absent.
            string gameData = Path.Combine(Directory.GetCurrentDirectory(), "gameData", "en_us_data.dat");
            if (File.Exists(gameData) || File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "en_us_data.dat")))
            {
                try { TextManager.Instance.Initialise(); }
                catch (Exception ex)
                {
                    log.Warn(ex, "TextManager init failed — continuing without string tables");
                    Console.WriteLine("[warn] TextManager unavailable; continuing without string tables");
                }
            }
            else
            {
                Console.WriteLine("[info] gameData not found; skipping TextManager");
            }

            MessageManager.Instance.Initialise();
            TunnelDataManager.Instance.Initialise();
            AuthMessageManager.Instance.Initialise();
            GatewayMessageManager.Instance.Initialise();
            ZoneMessageManager.Instance.Initialise();
        }

        private static int RunPackTerrain(ParseCliOptions options)
        {
            string binsDir = options.InputPath;
            string packetDumps = options.OutDir
                ?? Path.Combine(Directory.GetCurrentDirectory(),
                    "LandmarkEmulator.WorldServer", "Resources", "PacketDumps");

            try
            {
                var result = TerrainCel2Packer.PackFromParsedBins(binsDir, packetDumps);
                Console.WriteLine($"[pack-terrain] CEL2 {result.UniqueCells} cells ({result.CellsExtracted} entries / {result.CellsUpdateFiles} files, multiSeg={result.MultiSegCells})");
                Console.WriteLine($"[pack-terrain] BlobStore: {result.BlobStorePath} ({result.BlobStoreBytes:N0} B)");
                Console.WriteLine($"[pack-terrain] Manifest:  {result.ManifestPath} ({result.ManifestBytes:N0} B)");

                // Spawn from sibling SendSelf bin when present (for PROVENANCE.md).
                float x = 0, y = 0, z = 0;
                string spawnNote = "unknown";
                string sendSelf = Directory.GetFiles(binsDir, "*SendSelfToClient*0x00000F00*.bin")
                    .Concat(Directory.GetFiles(binsDir, "SendSelf.bin"))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (sendSelf != null)
                {
                    byte[] raw = File.ReadAllBytes(sendSelf);
                    byte[] payload = raw;
                    if (raw.Length > 4 && raw[0] == 0x00 && raw[1] == 0x00 && raw[2] == 0x0F && raw[3] == 0x00)
                        payload = raw.AsSpan(4).ToArray();
                    else if (raw.Length > 2 && raw[0] == 0x0F && raw[1] == 0x00)
                        payload = raw.AsSpan(2).ToArray();

                    if (CaptureEnrichment.TryReadSendSelfSpawn(payload, out x, out y, out z, out _, out string name))
                    {
                        spawnNote = $"({x:F2}, {y:F2}, {z:F2}) name={name}";
                        File.WriteAllBytes(Path.Combine(packetDumps, "SendSelf.bin"), payload);
                        Console.WriteLine($"[pack-terrain] SendSelf.bin refreshed; spawn {spawnNote}");
                    }
                }

                string stem = new DirectoryInfo(binsDir).Parent?.Name ?? "capture";
                if (stem.EndsWith("_session", StringComparison.OrdinalIgnoreCase) == false
                    && binsDir.Contains("bins", StringComparison.OrdinalIgnoreCase))
                {
                    // binsDir = .../<stem>/bins → stem is parent name
                }

                string provenance = TerrainCel2Packer.FormatProvenance(stem, result, x, y, z);
                File.WriteAllText(Path.Combine(packetDumps, "PROVENANCE.md"), provenance);
                Console.WriteLine($"[pack-terrain] Wrote PROVENANCE.md (spawn {spawnNote})");
                Console.WriteLine("[pack-terrain] Reminder: set ZoneSession SendSelf.Position to this spawn.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[pack-terrain] FAILED: {ex.Message}");
                return 1;
            }
        }

        private static int RunMergeCel2(ParseCliOptions options)
        {
            string missIdsPath = options.InputPath;
            string packetDumps = options.OutDir
                ?? Path.Combine(Directory.GetCurrentDirectory(),
                    "LandmarkEmulator.WorldServer", "Resources", "PacketDumps");
            string cel2Path = options.Cel2Path ?? Path.Combine(packetDumps, "TerrainCellBlobStore.bin");
            string primaryBins = options.PrimaryBinsDir
                ?? Path.Combine(Directory.GetCurrentDirectory(),
                    "NetworkCaptures", "_parsed", "Ube.eElKEtdDlpmP_session", "bins");
            string fallbackBins = options.FallbackBinsDir
                ?? Path.Combine(Directory.GetCurrentDirectory(),
                    "NetworkCaptures", "_parsed", "5JeosOMXzw5P1f2S_session", "bins");
            string ticket = options.Ticket ?? "E-UBE-CEL2-REPACK";
            string scanRoot = options.ScanAllCaptures
                ? Path.Combine(Directory.GetCurrentDirectory(), "NetworkCaptures", "_parsed")
                : null;

            try
            {
                var result = TerrainCel2Packer.MergeCel2(
                    cel2Path, missIdsPath, primaryBins, fallbackBins, packetDumps, scanRoot);

                Console.WriteLine($"[merge-cel2] CEL2 before={result.ExistingCells} after={result.TotalCells} ({result.BlobStoreBytes:N0} B)");
                Console.WriteLine($"[merge-cel2] Miss IDs={result.MissIdsRequested} alreadyPresent={result.AlreadyPresent} "
                    + $"+primary={result.ResolvedPrimary} +fallback={result.ResolvedFallback} +raws={result.ResolvedPacketDumpsRaw} "
                    + $"+scanAll={result.ResolvedScanAll} +template={result.ResolvedTemplate} stillMissing={result.StillMissing}");
                Console.WriteLine($"[merge-cel2] Wrote {result.BlobStorePath}");

                if (result.StillMissing > 0)
                {
                    string unresolvedPath = Path.Combine(packetDumps, "cel2-merge-unresolved.txt");
                    File.WriteAllLines(unresolvedPath,
                        result.UnresolvedIds.Select(id => $"0x{id:X16}"));
                    Console.WriteLine($"[merge-cel2] WARN: {result.StillMissing} IDs lack capture blobs → {unresolvedPath}");
                }

                string provenancePath = Path.Combine(packetDumps, "PROVENANCE.md");
                if (File.Exists(provenancePath))
                {
                    TerrainCel2Packer.AppendMergeProvenance(
                        provenancePath, result, missIdsPath, primaryBins, fallbackBins, ticket);
                    Console.WriteLine($"[merge-cel2] Appended merge note to PROVENANCE.md ({ticket})");
                }

                return result.StillMissing > 0 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[merge-cel2] FAILED: {ex.Message}");
                return 1;
            }
        }

        private static int RunTriage(ParseCliOptions options)
        {
            string dir = options.InputPath;
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Directory not found: {dir}");
                return 1;
            }

            string outDir = options.OutDir ?? Path.Combine(dir, "_index");
            CaptureTriage.Run(dir, outDir, enrich: !options.NoEnrich);
            return 0;
        }

        private static int RunTimeline(ParseCliOptions options)
        {
            var files = ResolveCaptureFiles(options.InputPath);
            if (files.Count == 0)
            {
                Console.Error.WriteLine($"No .pcap/.pcapng found at: {options.InputPath}");
                return 1;
            }

            if (files.Count > 1 && string.IsNullOrEmpty(options.TimelineMarkdownPath))
                Console.WriteLine($"[timeline] {files.Count} files — writing per-file CSV/MD under --out");

            string baseOut = options.OutDir
                ?? Path.Combine(
                    Directory.Exists(options.InputPath)
                        ? options.InputPath
                        : Path.GetDirectoryName(Path.GetFullPath(options.InputPath))!,
                    "_parsed");
            Directory.CreateDirectory(baseOut);

            foreach (var file in files)
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                if (stem.EndsWith(".pcap", StringComparison.OrdinalIgnoreCase))
                    stem = Path.GetFileNameWithoutExtension(stem);
                string outDir = Path.Combine(baseOut, SanitizeDirName(stem) + "_timeline");
                Directory.CreateDirectory(outDir);

                var parser = new CaptureSessionParser(file, outDir, options.ToSessionOptions(timelineOnly: true));
                var summary = parser.Parse(CancellationToken.None);
                if (!summary.GotEncryptionKey)
                    Console.WriteLine($"[timeline] WARN {Path.GetFileName(file)}: no CharacterLoginReply key — zone decrypt likely empty");

                WriteTimelineMarkdown(options, files.Count, file, stem, outDir, parser.Timeline);
            }

            return 0;
        }

        private static void WriteTimelineMarkdown(
            ParseCliOptions options,
            int fileCount,
            string file,
            string stem,
            string outDir,
            IReadOnlyList<ZoneTimelineEntry> timeline)
        {
            string mdPath = options.TimelineMarkdownPath;
            if (string.IsNullOrEmpty(mdPath))
                mdPath = Path.Combine(outDir, "zone-entry-timeline.md");
            else if (fileCount > 1)
                mdPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mdPath))!,
                    SanitizeDirName(stem) + "_" + Path.GetFileName(mdPath));

            ZoneTimelineMarkdown.Write(mdPath, Path.GetFileName(file), timeline, options.TimelineSeconds);
            Console.WriteLine($"[timeline] CSV: {Path.Combine(outDir, "zone-timeline.csv")}");
            Console.WriteLine($"[timeline] MD:  {mdPath}");
        }

        private static int RunParse(ParseCliOptions options)
        {
            var files = ResolveCaptureFiles(options.InputPath);
            if (files.Count == 0)
            {
                Console.Error.WriteLine($"No .pcap/.pcapng found at: {options.InputPath}");
                return 1;
            }

            TriageReport triage = null;
            string triagePath = options.TriagePath;
            if (string.IsNullOrEmpty(triagePath))
            {
                string sibling = Path.Combine(
                    Directory.Exists(options.InputPath) ? options.InputPath : Path.GetDirectoryName(Path.GetFullPath(options.InputPath))!,
                    "_index", "triage.json");
                if (File.Exists(sibling))
                    triagePath = sibling;
            }

            if (!string.IsNullOrEmpty(triagePath) && File.Exists(triagePath))
            {
                triage = JsonConvert.DeserializeObject<TriageReport>(File.ReadAllText(triagePath));
                Console.WriteLine($"[parse] Loaded triage from {triagePath}");
            }

            string baseOut = options.OutDir
                ?? Path.Combine(
                    Directory.Exists(options.InputPath)
                        ? options.InputPath
                        : Path.GetDirectoryName(Path.GetFullPath(options.InputPath))!,
                    "_parsed");

            Directory.CreateDirectory(baseOut);
            var summaries = new List<ParseSummary>();
            int parsed = 0;
            int skipped = 0;

            foreach (var file in files)
            {
                var triageRec = triage?.Files?.FirstOrDefault(f =>
                    string.Equals(f.FileName, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));

                if (!options.Force && triageRec != null && triageRec.GatewayOnly)
                {
                    var skip = new ParseSummary
                    {
                        FileName = Path.GetFileName(file),
                        Skipped = true,
                        SkipReason = "gatewayOnly (no Auth in triage; use --force to override)"
                    };
                    summaries.Add(skip);
                    skipped++;
                    Console.WriteLine($"[parse] SKIP {skip.FileName}: {skip.SkipReason}");
                    continue;
                }

                if (!options.Force && triageRec != null && !triageRec.HasAuth && triage != null)
                {
                    var skip = new ParseSummary
                    {
                        FileName = Path.GetFileName(file),
                        Skipped = true,
                        SkipReason = "no Auth in triage (use --force to override)"
                    };
                    summaries.Add(skip);
                    skipped++;
                    Console.WriteLine($"[parse] SKIP {skip.FileName}: {skip.SkipReason}");
                    continue;
                }

                // Without triage, skip files whose names strongly suggest gateway-only when not --force.
                if (!options.Force && triage == null && LooksGatewayOnlyName(Path.GetFileName(file)))
                {
                    // Still allow; triage is authoritative. Name heuristic only warns.
                    Console.WriteLine($"[parse] WARN {Path.GetFileName(file)}: name suggests Gateway-only; prefer running triage first");
                }

                string stem = Path.GetFileNameWithoutExtension(file);
                // Collapse double extension like .pcap.pcapng
                if (stem.EndsWith(".pcap", StringComparison.OrdinalIgnoreCase))
                    stem = Path.GetFileNameWithoutExtension(stem);

                string outDir = Path.Combine(baseOut, SanitizeDirName(stem));
                var sessionOpts = options.ToSessionOptions(timelineOnly: false);
                var parser = new CaptureSessionParser(file, outDir, sessionOpts);
                var summary = parser.Parse(CancellationToken.None);
                summaries.Add(summary);
                parsed++;

                if (options.WithTimeline && parser.Timeline.Count > 0)
                    WriteTimelineMarkdown(options, files.Count, file, stem, outDir, parser.Timeline);
            }

            string indexDir = Path.Combine(baseOut, "_index");
            Directory.CreateDirectory(indexDir);
            CaptureSessionParser.WriteOpcodeIndex(summaries, Path.Combine(indexDir, "opcode-index.csv"));
            File.WriteAllText(Path.Combine(indexDir, "parse-summaries.json"),
                JsonConvert.SerializeObject(summaries, Formatting.Indented));

            Console.WriteLine($"[parse] Parsed {parsed}, skipped {skipped}. Index: {Path.Combine(indexDir, "opcode-index.csv")}");

            return 0;
        }

        private static bool LooksGatewayOnlyName(string fileName)
        {
            string n = fileName.ToLowerInvariant();
            return n.Contains("externalgateway") || n.Contains("_ega") || n.Contains("ega.");
        }

        private static string SanitizeDirName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static List<string> ResolveCaptureFiles(string path)
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path))
                return new List<string> { path };

            if (!Directory.Exists(path))
                return new List<string>();

            return Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".pcap" or ".pcapng";
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ParseCliOptions ParseArgs(string[] args)
        {
            var options = new ParseCliOptions
            {
                Mode = args[0].ToLowerInvariant()
            };

            if (args.Length < 2)
                throw new ArgumentException($"{options.Mode} requires an input path");

            options.InputPath = args[1].Trim().Trim('"', '\'');

            // pack-terrain | merge-cel2 <path> [packetDumpsDir] [--out <dir>]
            int flagStart = 2;
            if ((options.Mode is "pack-terrain" or "merge-cel2") && args.Length >= 3 && !args[2].StartsWith('-'))
            {
                options.OutDir = args[2];
                flagStart = 3;
            }

            for (int i = flagStart; i < args.Length; i++)
            {
                string a = args[i];
                if (a is "--out" or "-o")
                {
                    options.OutDir = RequireValue(args, ref i, a);
                }
                else if (a is "--cel2")
                {
                    options.Cel2Path = RequireValue(args, ref i, a);
                }
                else if (a is "--primary-bins")
                {
                    options.PrimaryBinsDir = RequireValue(args, ref i, a);
                }
                else if (a is "--fallback-bins")
                {
                    options.FallbackBinsDir = RequireValue(args, ref i, a);
                }
                else if (a is "--ticket")
                {
                    options.Ticket = RequireValue(args, ref i, a);
                }
                else if (a is "--scan-all")
                {
                    options.ScanAllCaptures = true;
                }
                else if (a is "--opcodes" or "--dump-opcodes")
                {
                    string list = RequireValue(args, ref i, a);
                    AddOpcodes(options.OpcodeFilter, list);
                }
                else if (a is "--force")
                {
                    options.Force = true;
                }
                else if (a is "--from-triage")
                {
                    options.TriagePath = RequireValue(args, ref i, a);
                }
                else if (a is "--quiet")
                {
                    options.QuietProgress = true;
                }
                else if (a is "--no-enrich")
                {
                    options.NoEnrich = true;
                }
                else if (a is "--summary-only" or "--no-packet-log")
                {
                    options.SummaryOnly = true;
                }
                else if (a is "--with-timeline")
                {
                    options.WithTimeline = true;
                }
                else if (a is "--dump-all-ec")
                {
                    options.DumpAllEc = true;
                }
                else if (a is "--no-dump-all-ec")
                {
                    options.DumpAllEc = false;
                }
                else if (a is "--dump-large")
                {
                    string v = RequireValue(args, ref i, a);
                    if (!int.TryParse(v, out int min) || min < 0)
                        throw new ArgumentException($"Invalid --dump-large: {v}");
                    options.DumpLargeMinBytes = min == 0 ? null : min;
                }
                else if (a is "--no-dump-anchors")
                {
                    options.DumpAnchors = false;
                }
                else if (a is "--seconds")
                {
                    string v = RequireValue(args, ref i, a);
                    if (!double.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double secs) || secs <= 0)
                        throw new ArgumentException($"Invalid --seconds: {v}");
                    options.TimelineSeconds = secs;
                }
                else if (a is "--markdown" or "--md")
                {
                    options.TimelineMarkdownPath = RequireValue(args, ref i, a);
                }
                else
                {
                    throw new ArgumentException($"Unknown argument: {a}");
                }
            }

            return options;
        }

        private static void AddOpcodes(HashSet<uint> filter, string list)
        {
            foreach (var part in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // PowerShell may evaluate 0xEC… as a signed Int32 before argv reaches us
                // (e.g. 0xEC000A00 → "-335541760"). Accept hex, unsigned, or unchecked signed.
                string p = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
                uint opcode;
                if (uint.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out opcode)
                    || uint.TryParse(part, System.Globalization.NumberStyles.Integer, null, out opcode)
                    || (long.TryParse(part, System.Globalization.NumberStyles.Integer, null, out long signed)
                        && (opcode = unchecked((uint)signed)) == opcode))
                {
                    filter.Add(opcode);
                    continue;
                }
                throw new ArgumentException($"Invalid opcode: {part}");
            }
        }

        private static string RequireValue(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException($"{flag} requires a value");
            i++;
            return args[i];
        }

        private static bool IsHelp(string arg)
            => arg is "-h" or "--help" or "help" or "/?";

        private static void PrintUsage()
        {
            Console.WriteLine(@"
Usage:
  triage       <captures-dir> [--out <dir>] [--no-enrich]
  parse        <file-or-dir>  [--out <dir>] [--opcodes|--dump-opcodes 0xED00,0xF900,...]
                              [--summary-only] [--with-timeline] [--seconds 60] [--markdown <path>]
                              [--dump-all-ec|--no-dump-all-ec] [--dump-large N] [--no-dump-anchors]
                              [--force] [--from-triage <triage.json>] [--quiet]
  timeline     <file-or-dir>  [--out <dir>] [--seconds 60] [--markdown <path>] [--quiet]
  pack-terrain <bins-dir> [packet-dumps-dir] [--out <packet-dumps-dir>]
  merge-cel2   <miss-ids-file> [packet-dumps-dir] [--cel2 <path>] [--primary-bins <dir>] [--fallback-bins <dir>] [--ticket <name>]

Examples:
  # Fast RE: dump listed opcodes only, no PacketParse.txt, include timing CSV
  dotnet run --project LandmarkEmulator.Parser -- parse capture.pcapng --opcodes 0xED00,0xF900 --summary-only --with-timeline

  # Terrain / CEL2 opcode dumps
  dotnet run --project LandmarkEmulator.Parser -- parse NetworkCaptures --opcodes 0xEC000100,0xEC000500,0xEC000A00 --dump-all-ec

  # Pack CEL2 + manifest into PacketDumps from a green-control parse
  dotnet run --project LandmarkEmulator.Parser -- pack-terrain NetworkCaptures/_parsed/5JeosOMXzw5P1f2S_session/bins

  # Merge miss cellIds into existing CEL2 (Ube primary, 5Jeos fallback)
  dotnet run --project LandmarkEmulator.Parser -- merge-cel2 KeptLogs/E-UBE-CEL2-GAP-DUMP-1788254720/cel2-miss-ids.txt

  dotnet run --project LandmarkEmulator.Parser -- triage NetworkCaptures --no-enrich
  dotnet run --project LandmarkEmulator.Parser -- timeline capture.pcapng --seconds 60 --markdown AgentShare/ZoneEntryTimeline.md

Notes:
  - --opcodes / --dump-opcodes: dump bins + verbose JSON for listed opcodes only.
  - With a filter, --dump-all-ec and --dump-large default OFF (opt in). Without a filter,
    dump all 0xEC* and packets >5000 bytes (legacy). Anchors (SendSelf, manifest) dump by default.
  - --summary-only / --no-packet-log: skip PacketParse.txt (summary.json + bins + dumps-index.csv).
  - --with-timeline: also write zone-timeline.csv (+ markdown) in one decrypt pass.
  - dumps-index.csv includes rel_sendself_ms / rel_clientready_ms for each bin.
  - Run triage first. parse skips gateway-only / no-Auth unless --force. --no-enrich skips Auth decrypt.
  - pack-terrain: builds TerrainCellBlobStore.bin (CEL2) from CellsUpdate bins; copies manifest/raws;
    refreshes SendSelf.bin + PROVENANCE.md. Prefer green-control 5JeosOMXzw5P1f2S (see docs/packet-dumps-terrain.md).
  - merge-cel2: adds capture blobs for listed miss cellIds into existing CEL2; does not touch manifest/spawn.
");
        }
    }
}
