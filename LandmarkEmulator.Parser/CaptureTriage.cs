using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model;
using LandmarkEmulator.Shared.Network.Packets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LandmarkEmulator.Parser
{
    public static class CaptureTriage
    {
        private const ushort DefaultAuthPort = 20042;

        public static TriageReport Run(string directory, string outDir, bool enrich = true, CancellationToken token = default)
        {
            directory = Path.GetFullPath(directory);
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);

            var report = new TriageReport { SourceDirectory = directory };
            var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".pcap" or ".pcapng";
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine($"[triage] Scanning {files.Count} capture(s) in {directory} (streaming{(enrich ? ", enrich on" : ", enrich off")})");

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var record = TriageFile(file, directory);
                if (enrich && record.HasAuth && !record.GatewayOnly)
                {
                    Console.WriteLine($"  [enrich] {record.FileName} …");
                    CaptureEnrichment.Enrich(record, file, token);
                }

                report.Files.Add(record);
                var tag = record.Error != null ? "ERROR"
                    : record.LikelyFullLogin ? "FULL"
                    : record.HasAuth ? "AUTH"
                    : record.GatewayOnly ? "GW-ONLY"
                    : "OTHER";
                string server = string.IsNullOrEmpty(record.ServerName) ? "" : $"  server=\"{record.ServerName}\"";
                string loc = string.IsNullOrEmpty(record.WorldLocation) ? "" : $"  loc={record.WorldLocation}";
                Console.WriteLine($"  [{tag}] {record.FileName}  {record.SizeMB:F1} MB  udp={record.UdpPacketCount}  protocols=[{string.Join(", ", record.ProtocolsSeen)}]{server}{loc}");
            }

            report.Files = report.Files
                .OrderByDescending(f => f.LikelyFullLogin)
                .ThenByDescending(f => f.HasAuth)
                .ThenByDescending(f => f.UdpPacketCount)
                .ToList();

            report.LikelyFullLoginCount = report.Files.Count(f => f.LikelyFullLogin);
            report.AuthBearingCount = report.Files.Count(f => f.HasAuth);
            report.GatewayOnlyCount = report.Files.Count(f => f.GatewayOnly);

            var jsonPath = Path.Combine(outDir, "triage.json");
            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            File.WriteAllText(Path.Combine(outDir, "triage.md"), BuildMarkdown(report));

            Console.WriteLine($"[triage] Auth-bearing: {report.AuthBearingCount}, likely full login: {report.LikelyFullLoginCount}, gateway-only: {report.GatewayOnlyCount}");
            Console.WriteLine($"[triage] Wrote {jsonPath}");
            return report;
        }

        public static TriageFileRecord TriageFile(string filePath, string rootDir)
        {
            var info = new FileInfo(filePath);
            var record = new TriageFileRecord
            {
                FileName = info.Name,
                RelativePath = Path.GetRelativePath(rootDir, filePath),
                SizeMB = info.Length / (1024d * 1024d)
            };

            var serverPortCounts = new Dictionary<ushort, long>();
            var protocols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                PcapUdpReader.ForEach(filePath, datagram =>
                {
                    record.UdpPacketCount++;
                    ushort serverPort = PcapUdpReader.ServerPort(datagram.SourcePort, datagram.DestinationPort);
                    serverPortCounts.TryGetValue(serverPort, out long c);
                    serverPortCounts[serverPort] = c + 1;

                    if (datagram.SourcePort == DefaultAuthPort || datagram.DestinationPort == DefaultAuthPort)
                        record.HasAuthPort20042 = true;

                    TryReadSessionProtocol(datagram.Payload, out string protocol);
                    if (!string.IsNullOrEmpty(protocol))
                    {
                        protocols.Add(protocol);
                        if (protocol.StartsWith("LoginUdp", StringComparison.OrdinalIgnoreCase))
                            record.HasLoginUdpSession = true;
                        if (protocol.StartsWith("ExternalGatewayApi", StringComparison.OrdinalIgnoreCase))
                            record.HasExternalGatewaySession = true;
                    }

                    return true;
                });
            }
            catch (Exception ex)
            {
                record.Error = ex.Message;
            }

            record.ProtocolsSeen = protocols.OrderBy(p => p).ToList();
            record.TopServerPorts = serverPortCounts
                .OrderByDescending(kv => kv.Value)
                .Take(8)
                .Select(kv => new PortCount { Port = kv.Key, Count = kv.Value })
                .ToList();

            // Non-auth high-volume server ports imply Gateway/world traffic.
            record.HasNonAuthServerPorts = serverPortCounts.Keys.Any(p => p != DefaultAuthPort && p < 40000);
            return record;
        }

        public static bool TryReadSessionProtocol(byte[] payload, out string protocol)
        {
            protocol = null;
            if (payload == null || payload.Length < 14)
                return false;

            try
            {
                var packet = new ProtocolPacket(payload, new PacketOptions { IsSubpacket = true, Compression = false });
                if (packet.Opcode != ProtocolMessageOpcode.SessionRequest)
                    return false;

                var message = new SessionRequest();
                var reader = new ProtocolPacketReader(packet.Data);
                message.Read(reader, new PacketOptions());
                if (string.IsNullOrWhiteSpace(message.Protocol))
                    return false;

                // Reject binary garbage that happens to look like SessionRequest.
                string p = message.Protocol;
                if (p.Length > 64 || p.Any(c => c < 0x20 || c > 0x7E))
                    return false;
                if (!(p.StartsWith("LoginUdp", StringComparison.Ordinal)
                      || p.StartsWith("ExternalGatewayApi", StringComparison.Ordinal)
                      || p.StartsWith("ExternalLoggingUdp", StringComparison.Ordinal)))
                    return false;

                protocol = p;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildMarkdown(TriageReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Capture triage");
            sb.AppendLine();
            sb.AppendLine($"Generated (UTC): {report.GeneratedUtc:O}");
            sb.AppendLine($"Source: `{report.SourceDirectory}`");
            sb.AppendLine();
            sb.AppendLine($"| Metric | Count |");
            sb.AppendLine($"| --- | ---: |");
            sb.AppendLine($"| Files | {report.Files.Count} |");
            sb.AppendLine($"| Auth-bearing | {report.AuthBearingCount} |");
            sb.AppendLine($"| Likely full login (Auth+Gateway) | {report.LikelyFullLoginCount} |");
            sb.AppendLine($"| Gateway-only (skip decrypt) | {report.GatewayOnlyCount} |");
            sb.AppendLine();
            sb.AppendLine("| File | MB | UDP | Auth | Full | GW-only | Server | Description | Location | Protocols |");
            sb.AppendLine("| --- | ---: | ---: | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var f in report.Files)
            {
                sb.AppendLine(
                    $"| `{f.FileName}` | {f.SizeMB:F1} | {f.UdpPacketCount} | {(f.HasAuth ? "yes" : "")} | {(f.LikelyFullLogin ? "yes" : "")} | {(f.GatewayOnly ? "yes" : "")} | {MdCell(f.ServerName)} | {MdCell(f.ServerDescription)} | {MdCell(f.WorldLocation)} | {MdCell(string.Join(", ", f.ProtocolsSeen))} |");
            }
            return sb.ToString();
        }

        private static string MdCell(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            // Keep markdown tables intact: no raw pipes / newlines in cells.
            return value
                .Replace("|", "/", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        }
    }
}
