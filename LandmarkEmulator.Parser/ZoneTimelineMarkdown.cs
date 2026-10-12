using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.Parser
{
    /// <summary>
    /// Builds a human-readable zone-entry timeline markdown from <see cref="ZoneTimelineEntry"/> rows.
    /// </summary>
    public static class ZoneTimelineMarkdown
    {
        private static readonly HashSet<string> HighVolumeOpcodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "PlayerUpdateAddFeatherweightObject",
            "PlayerUpdateAddLightweightProp",
            "PlayerUpdateAddLightweightNpc",
            "PlayerUpdateLightweightToFullProp",
            "PlayerUpdateFullCharacterDataRequest",
            "PlayerUpdateInteractionInfoRequest",
            "TerrainCellModifiedCellManifestUpdate",
            "TerrainCellClientCellRequest",
            "TerrainCellCellsUpdate",
            "ClientLog",
            "Unknown",
            "UnknownNoise"
        };

        public static void Write(
            string path,
            string captureFileName,
            IReadOnlyList<ZoneTimelineEntry> allEntries,
            double windowSecondsAfterSendSelf = 60.0)
        {
            var sendSelf = allEntries.FirstOrDefault(e =>
                e.Name.Equals("SendSelfToClient", StringComparison.OrdinalIgnoreCase));
            double t0 = sendSelf?.AbsoluteSeconds
                ?? allEntries.FirstOrDefault()?.AbsoluteSeconds
                ?? 0;

            var window = allEntries
                .Where(e => e.AbsoluteSeconds >= t0 - 0.05 && e.AbsoluteSeconds <= t0 + windowSecondsAfterSendSelf)
                .OrderBy(e => e.Seq)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# Official capture zone-entry timeline");
            sb.AppendLine();
            sb.AppendLine("Reference for comparing emulator S→C grouping/order and approximate timings.");
            sb.AppendLine();
            sb.AppendLine("## Source");
            sb.AppendLine();
            sb.AppendLine($"| Field | Value |");
            sb.AppendLine($"|-------|-------|");
            sb.AppendLine($"| Capture | `{captureFileName}` |");
            sb.AppendLine($"| Parsed | `{DateTime.UtcNow:yyyy-MM-dd HH:mm}Z` |");
            sb.AppendLine($"| Window | first `SendSelfToClient` through +{windowSecondsAfterSendSelf:0}s |");
            sb.AppendLine($"| t=0 | first `SendSelfToClient` (S→C) |");
            sb.AppendLine($"| Also listed | `rel_ready` = ms vs first `ClientIsReady` |");
            sb.AppendLine($"| Zone msgs in window | {window.Count} |");
            sb.AppendLine($"| Total zone msgs in capture | {allEntries.Count} |");
            sb.AppendLine();
            sb.AppendLine("**Timing note:** timestamps are pcap/pcapng packet times of the UDP frame that *completed* SOE reassembly for that zone message (last fragment). Payload length is zone body after opcode bytes.");
            sb.AppendLine();
            sb.AppendLine("**Grouping note:** high-volume opcodes (featherweights, B00, cell request/reply spam, ClientLog) are collapsed into burst rows. Bootstrap catalogs and gate packets are listed individually.");
            sb.AppendLine();
            sb.AppendLine("**Unknown vs UnknownNoise:** `Unknown` = family-shaped prefix not in `ZoneMessageOpcode` (likely catalog gap). `UnknownNoise` = fails Landmark family-shape heuristic (`& 0xFF00FF == 0` / `0x26|0x27|0x29|0xEC`) — treat as ARC4/demux framing garbage until the parser stream is proven green.");
            sb.AppendLine();

            AppendAnchorTable(sb, window);
            AppendPhaseSummary(sb, window);
            AppendHybridTimeline(sb, window);
            AppendOpcodeHistogram(sb, window);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, sb.ToString());
        }

        private static void AppendAnchorTable(StringBuilder sb, List<ZoneTimelineEntry> window)
        {
            sb.AppendLine("## Anchors");
            sb.AppendLine();
            sb.AppendLine("| Event | Dir | Opcode | Payload | t vs SendSelf | t vs ClientIsReady |");
            sb.AppendLine("|-------|-----|--------|---------|---------------|--------------------|");

            string[] anchors =
            {
                "SendZoneDetails",
                "CommandBase",
                "SendSelfToClient",
                "ClientIsReady",
                "AchievementBase",
                "PropBase",
                "ZoneDoneSendingInitialData",
                "ZoneIsReady",
                "TerrainCellModifiedCellManifest",
                "TerrainCellClientCellRequest",
                "TerrainCellCellsUpdate",
                "ClaimsBase",
                "TemplateBase",
                "PlayerUpdateAddFeatherweightObject",
                "PlayerUpdateAddLightweightProp"
            };

            foreach (var name in anchors)
            {
                var e = window.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (e == null)
                    continue;
                sb.AppendLine($"| {e.Name} (first) | {e.Direction} | `{e.OpcodeHex}` | {e.PayloadLength} B | {FmtMs(e.RelSendSelfMs)} | {FmtMs(e.RelClientReadyMs)} |");
            }
            sb.AppendLine();
        }

        private static void AppendPhaseSummary(StringBuilder sb, List<ZoneTimelineEntry> window)
        {
            sb.AppendLine("## Phases (approximate)");
            sb.AppendLine();

            var phases = new (string Title, Func<ZoneTimelineEntry, bool> Pred)[]
            {
                ("A. Pre-SendSelf catalogs", e => e.RelSendSelfMs < -0.5),
                ("B. SendSelf → ClientIsReady", e => e.RelSendSelfMs >= -0.5 && e.RelClientReadyMs < 0),
                ("C. ClientIsReady → ZoneDone / early catalogs", e => e.RelClientReadyMs >= 0 && e.RelClientReadyMs < 2000),
                ("D. Terrain cold start (A00 / first 100 / first 500)", e =>
                    e.Name.Contains("TerrainCell", StringComparison.OrdinalIgnoreCase)
                    && e.RelClientReadyMs >= 0 && e.RelClientReadyMs < 15000),
                ("E. World stream (featherweight / lightweight)", e =>
                    e.Name.Contains("Featherweight", StringComparison.OrdinalIgnoreCase)
                    || e.Name.Contains("Lightweight", StringComparison.OrdinalIgnoreCase)),
                ("F. Remainder of window", e => e.RelSendSelfMs >= 15000)
            };

            sb.AppendLine("| Phase | First t(SendSelf) | Last t(SendSelf) | Msg count | Dominant opcodes |");
            sb.AppendLine("|-------|-------------------|------------------|-----------|------------------|");
            foreach (var (title, pred) in phases)
            {
                var rows = window.Where(pred).ToList();
                if (rows.Count == 0)
                {
                    sb.AppendLine($"| {title} | — | — | 0 | — |");
                    continue;
                }
                string top = string.Join(", ",
                    rows.GroupBy(r => r.Name)
                        .OrderByDescending(g => g.Count())
                        .Take(4)
                        .Select(g => $"{g.Key}×{g.Count()}"));
                sb.AppendLine($"| {title} | {FmtMs(rows.First().RelSendSelfMs)} | {FmtMs(rows.Last().RelSendSelfMs)} | {rows.Count} | {top} |");
            }
            sb.AppendLine();
        }

        private static void AppendHybridTimeline(StringBuilder sb, List<ZoneTimelineEntry> window)
        {
            sb.AppendLine("## Timeline (hybrid)");
            sb.AppendLine();
            sb.AppendLine("| t(SendSelf) | t(Ready) | Dir | Opcode | Name | Payload | Notes |");
            sb.AppendLine("|-------------|----------|-----|--------|------|---------|-------|");

            int i = 0;
            while (i < window.Count)
            {
                var cur = window[i];
                bool collapse = HighVolumeOpcodes.Contains(cur.Name)
                    || cur.Name.StartsWith("TerrainCell", StringComparison.OrdinalIgnoreCase)
                       && cur.Name is not "TerrainCellModifiedCellManifest";

                // Always show first occurrence of bootstrap-ish names individually even if high-volume.
                bool firstOfKind = window.Take(i).All(x => !x.Name.Equals(cur.Name, StringComparison.OrdinalIgnoreCase));
                if (firstOfKind && (
                    cur.Name.Contains("Manifest", StringComparison.OrdinalIgnoreCase)
                    || cur.Name.Contains("CellRequest", StringComparison.OrdinalIgnoreCase)
                    || cur.Name.Contains("CellsUpdate", StringComparison.OrdinalIgnoreCase)
                    || cur.Name.Contains("Featherweight", StringComparison.OrdinalIgnoreCase)
                    || cur.Name.Contains("LightweightProp", StringComparison.OrdinalIgnoreCase)))
                {
                    collapse = false;
                }

                if (!collapse)
                {
                    sb.AppendLine($"| {FmtMs(cur.RelSendSelfMs)} | {FmtMs(cur.RelClientReadyMs)} | {cur.Direction} | `{cur.OpcodeHex}` | {cur.Name} | {cur.PayloadLength} B | |");
                    i++;
                    continue;
                }

                int j = i;
                int minLen = cur.PayloadLength, maxLen = cur.PayloadLength;
                long sumLen = 0;
                while (j < window.Count
                       && window[j].Name == cur.Name
                       && window[j].Direction == cur.Direction
                       && window[j].OpcodeHex == cur.OpcodeHex)
                {
                    minLen = Math.Min(minLen, window[j].PayloadLength);
                    maxLen = Math.Max(maxLen, window[j].PayloadLength);
                    sumLen += window[j].PayloadLength;
                    j++;
                }

                int count = j - i;
                var last = window[j - 1];
                string sizeNote = minLen == maxLen
                    ? $"{minLen} B ×{count}"
                    : $"{minLen}–{maxLen} B ×{count} (sum {sumLen} B)";
                string span = count == 1
                    ? ""
                    : $"burst {FmtMs(cur.RelSendSelfMs)} → {FmtMs(last.RelSendSelfMs)}";
                sb.AppendLine($"| {FmtMs(cur.RelSendSelfMs)} | {FmtMs(cur.RelClientReadyMs)} | {cur.Direction} | `{cur.OpcodeHex}` | {cur.Name} | {sizeNote} | {span} |");
                i = j;
            }
            sb.AppendLine();
        }

        private static void AppendOpcodeHistogram(StringBuilder sb, List<ZoneTimelineEntry> window)
        {
            sb.AppendLine("## Opcode counts in window");
            sb.AppendLine();
            sb.AppendLine("| Count | Dir mix (S/C) | Opcode | Name | Payload min–max |");
            sb.AppendLine("|------:|---------------|--------|------|-----------------|");
            foreach (var g in window.GroupBy(e => (e.OpcodeHex, e.Name))
                         .OrderByDescending(g => g.Count())
                         .Take(40))
            {
                int s = g.Count(x => x.Direction == "S");
                int c = g.Count(x => x.Direction == "C");
                sb.AppendLine($"| {g.Count()} | {s}/{c} | `{g.Key.OpcodeHex}` | {g.Key.Name} | {g.Min(x => x.PayloadLength)}–{g.Max(x => x.PayloadLength)} B |");
            }
            sb.AppendLine();
        }

        private static string FmtMs(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms))
                return "—";
            if (Math.Abs(ms) < 10)
                return ms.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " ms";
            if (Math.Abs(ms) < 1000)
                return ms.ToString("+0;-0", CultureInfo.InvariantCulture) + " ms";
            return (ms / 1000.0).ToString("+0.000;-0.000", CultureInfo.InvariantCulture) + " s";
        }
    }
}
