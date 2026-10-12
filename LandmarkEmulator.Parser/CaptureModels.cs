using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Parser
{
    public class TriageReport
    {
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
        public string SourceDirectory { get; set; }
        public List<TriageFileRecord> Files { get; set; } = new();
        public int LikelyFullLoginCount { get; set; }
        public int AuthBearingCount { get; set; }
        public int GatewayOnlyCount { get; set; }
    }

    public class TriageFileRecord
    {
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public double SizeMB { get; set; }
        public long UdpPacketCount { get; set; }
        public bool HasAuthPort20042 { get; set; }
        public bool HasLoginUdpSession { get; set; }
        public bool HasExternalGatewaySession { get; set; }
        public bool HasAuth => HasAuthPort20042 || HasLoginUdpSession;
        public bool HasGateway => HasExternalGatewaySession || HasNonAuthServerPorts;
        public bool HasNonAuthServerPorts { get; set; }
        public bool LikelyFullLogin => HasAuth && HasGateway;
        public bool GatewayOnly => !HasAuth && HasGateway;
        public List<string> ProtocolsSeen { get; set; } = new();
        public List<PortCount> TopServerPorts { get; set; } = new();

        /// <summary>Server selected at CharacterLogin (from ServerListReply + CharacterLoginReply).</summary>
        public ulong? ServerId { get; set; }
        public string ServerName { get; set; }
        public string ServerDescription { get; set; }
        public string ServerAddress { get; set; }
        public string CharacterName { get; set; }

        /// <summary>Spawn / enter-world position from SendSelfToClient (Unknown3).</summary>
        public float? WorldX { get; set; }
        public float? WorldY { get; set; }
        public float? WorldZ { get; set; }
        public float? WorldW { get; set; }
        public string WorldLocation =>
            WorldX.HasValue && WorldY.HasValue && WorldZ.HasValue
                ? $"{WorldX.Value:F2}, {WorldY.Value:F2}, {WorldZ.Value:F2}"
                : null;

        public string Error { get; set; }
    }

    public class PortCount
    {
        public ushort Port { get; set; }
        public long Count { get; set; }
    }

    public class OpcodeStat
    {
        public string Layer { get; set; }
        public string Name { get; set; }
        public string OpcodeHex { get; set; }
        public long Count { get; set; }
        public long ClientCount { get; set; }
        public long ServerCount { get; set; }
        public int MinSize { get; set; } = int.MaxValue;
        public int MaxSize { get; set; }
        public long TotalSize { get; set; }
        public double AvgSize => Count == 0 ? 0 : (double)TotalSize / Count;

        public void Observe(int size, bool isClient)
        {
            Count++;
            if (isClient) ClientCount++;
            else ServerCount++;
            if (size < MinSize) MinSize = size;
            if (size > MaxSize) MaxSize = size;
            TotalSize += size;
        }
    }

    public class ParseSummary
    {
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public bool GotEncryptionKey { get; set; }
        public bool Skipped { get; set; }
        public string SkipReason { get; set; }
        public string Error { get; set; }
        public Dictionary<string, OpcodeStat> Opcodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int TerrainFamilyDumps { get; set; }
        public List<string> DumpedBins { get; set; } = new();
    }

    public class ParseCliOptions
    {
        public string Mode { get; set; }
        public string InputPath { get; set; }
        public string OutDir { get; set; }
        public HashSet<uint> OpcodeFilter { get; set; } = new();
        public bool Force { get; set; }
        public string TriagePath { get; set; }
        public bool QuietProgress { get; set; }
        /// <summary>When true, triage skips Auth decrypt enrich (server name / spawn loc).</summary>
        public bool NoEnrich { get; set; }
        /// <summary>Seconds after first SendSelfToClient (or first zone msg) to keep in timeline export.</summary>
        public double TimelineSeconds { get; set; } = 60;
        /// <summary>Optional markdown path for timeline reference output.</summary>
        public string TimelineMarkdownPath { get; set; }

        /// <summary>Skip PacketParse.txt (summary.json + bins + optional timeline only).</summary>
        public bool SummaryOnly { get; set; }
        /// <summary>Also write zone-timeline.csv (+ optional markdown) during parse.</summary>
        public bool WithTimeline { get; set; }
        /// <summary>
        /// Dump all 0xEC* terrain-family bins. Null = auto (on when no --opcodes filter, off when filtered).
        /// </summary>
        public bool? DumpAllEc { get; set; }
        /// <summary>
        /// Dump zone packets larger than this many bytes. Null = auto (5000 when no filter, disabled when filtered).
        /// </summary>
        public int? DumpLargeMinBytes { get; set; }
        /// <summary>Always dump SendSelf + TerrainCellManifest anchors (default true).</summary>
        public bool DumpAnchors { get; set; } = true;

        /// <summary>merge-cel2: existing TerrainCellBlobStore.bin (default under PacketDumps).</summary>
        public string Cel2Path { get; set; }
        /// <summary>merge-cel2: primary parsed bins (Ube).</summary>
        public string PrimaryBinsDir { get; set; }
        /// <summary>merge-cel2: fallback parsed bins (5Jeos).</summary>
        public string FallbackBinsDir { get; set; }
        /// <summary>merge-cel2: ticket label for PROVENANCE.md.</summary>
        public string Ticket { get; set; }
        /// <summary>merge-cel2: scan all NetworkCaptures/_parsed session bins for still-missing IDs.</summary>
        public bool ScanAllCaptures { get; set; }

        public SessionParseOptions ToSessionOptions(bool timelineOnly = false)
        {
            bool hasFilter = OpcodeFilter.Count > 0;
            return new SessionParseOptions
            {
                OpcodeFilter = OpcodeFilter,
                QuietProgress = QuietProgress,
                TimelineOnly = timelineOnly,
                WriteTimeline = timelineOnly || WithTimeline,
                SummaryOnly = SummaryOnly || timelineOnly,
                DumpAllEc = DumpAllEc ?? !hasFilter,
                DumpLargeMinBytes = DumpLargeMinBytes ?? (hasFilter ? null : 5000),
                DumpAnchors = DumpAnchors && !timelineOnly
            };
        }
    }

    /// <summary>Per-capture parse behavior (resolved CLI defaults).</summary>
    public class SessionParseOptions
    {
        public HashSet<uint> OpcodeFilter { get; set; } = new();
        public bool QuietProgress { get; set; }
        public bool TimelineOnly { get; set; }
        public bool WriteTimeline { get; set; }
        public bool SummaryOnly { get; set; }
        public bool DumpAllEc { get; set; }
        public int? DumpLargeMinBytes { get; set; }
        public bool DumpAnchors { get; set; } = true;
    }

    public class DumpIndexEntry
    {
        public int DumpIndex { get; set; }
        public string FileName { get; set; }
        public string Direction { get; set; }
        public string OpcodeHex { get; set; }
        public string Name { get; set; }
        public int PacketLength { get; set; }
        public double AbsoluteSeconds { get; set; }
        public double RelSendSelfMs { get; set; }
        public double RelClientReadyMs { get; set; }
        public double RelFirstZoneMs { get; set; }
    }

    public class ZoneTimelineEntry
    {
        public int Seq { get; set; }
        public int FrameIndex { get; set; }
        public double AbsoluteSeconds { get; set; }
        public double RelSendSelfMs { get; set; }
        public double RelClientReadyMs { get; set; }
        public double RelFirstZoneMs { get; set; }
        public string Direction { get; set; }
        public string OpcodeHex { get; set; }
        public string Name { get; set; }
        public int PayloadLength { get; set; }
        public int PacketLength { get; set; }
    }
}
