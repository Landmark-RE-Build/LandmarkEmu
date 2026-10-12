using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.IO;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player reports, saved next to the server for its operators:
    /// - Bug report window (Lua Ui.OnBugReportUiSubmit → FUN_14306f260): BugSubmission 0x55 sub 1 AddBug, header u16
    ///   base + u16 sub (FUN_14306db00), then u32, string, u32, string — the last string is a TinyXML document
    ///   &lt;BugSubmission Category=".." BugDescription=".."/&gt; plus what the client adds about itself (FUN_14306e820). The
    ///   client shows "Your bug has been submitted!" on its own.
    /// - Report a build site (Ui.OnClaimReportUiSubmit → FUN_14306f5e0, only inside a claim): ClaimReportSubmission
    ///   0x56 sub 1 AddReport, the same way with a &lt;ClaimReport ReportDescription=".."/&gt; document.
    /// Files: Reports/bugs and Reports/sites, one XML file each, headed by what the server knows (character, landscape,
    /// position, the build site there and its owner). At most one report a minute per player (two at once).
    /// </summary>
    public static class Reports
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const int MaxReportBytes = 64 * 1024;

        [ZoneMessageHandler(ZoneMessageOpcode.BugSubmissionBase)]
        public static void HandleBug(WorldSession session, BugSubmissionRequest request)
            => Save(session, "bugs", request.SubOpcode, request.Body);

        [ZoneMessageHandler(ZoneMessageOpcode.ClaimReportSubmissionBase)]
        public static void HandleClaimReport(WorldSession session, SiteReportRequest request)
            => Save(session, "sites", request.SubOpcode, request.Body);

        /// <summary>The report's XML: the last length-prefixed string in the body that starts with '&lt;'.</summary>
        private static string FindXml(byte[] body)
        {
            string found = null;
            for (int i = 0; i + 4 < body.Length; i++)
            {
                int length = BitConverter.ToInt32(body, i);
                if (length <= 0 || length > body.Length - i - 4 || body[i + 4] != (byte)'<')
                    continue;
                found = Encoding.UTF8.GetString(body, i + 4, Math.Min(length, MaxReportBytes));
                i += 3 + length;
            }
            return found;
        }

        private static void Save(WorldSession session, string kind, ushort sub, byte[] body)
        {
            if (sub != 1)
            {
                log.Info($"{session.CharacterName}: report base sub {sub}: {body.Length} byte(s).");
                return;
            }
            if (!session.ReportLimiter.TryTake())
            {
                log.Info($"{session.CharacterName}: report refused (too many too fast).");
                return;
            }
            string xml = FindXml(body) ?? Convert.ToHexString(body.AsSpan(0, Math.Min(body.Length, MaxReportBytes)));
            ulong? site = Claims.ClaimAt(session.ZoneId, session.Position);
            var claim = site is ulong id ? Claims.Get(id) : null;
            var header = new StringBuilder()
                .AppendLine($"<!-- {kind} report, {DateTime.UtcNow:u}")
                .AppendLine($"     character: {session.CharacterName} (0x{session.PlayerGuid:X}), landscape {session.ZoneId}, position {session.Position}")
                .AppendLine(site is ulong s ? $"     build site: {s} \"{claim?.Name}\" owned by 0x{claim?.OwnerGuid:X}" : "     build site: none")
                .AppendLine("-->");
            try
            {
                string folder = Path.Combine(AppContext.BaseDirectory, "Reports", kind);
                Directory.CreateDirectory(folder);
                string name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{Safe(session.CharacterName)}.xml";
                File.WriteAllText(Path.Combine(folder, name), header + xml.Replace("--", "- -", StringComparison.Ordinal) + Environment.NewLine);
                log.Info($"{session.CharacterName} sent a {(kind == "bugs" ? "bug" : "build site")} report (Reports/{kind}/{name}).");
            }
            catch (Exception e)
            {
                log.Error(e, "Could not save a player report.");
            }
        }

        private static string Safe(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name ?? "player")
                b.Append(char.IsLetterOrDigit(c) ? c : '_');
            return b.Length > 0 ? b.ToString() : "player";
        }
    }
}
