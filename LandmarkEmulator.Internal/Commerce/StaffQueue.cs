using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LandmarkEmulator.Internal.Commerce
{
    /// <summary>
    /// A Player Studio product waiting for staff: the peers rejected it and live's rule sends it to Customer Service
    /// for review (Update Notes 18 Jun 2014, P1210) instead of turning it down. Listed on the world's admin page and
    /// decided there with "psreview &lt;id&gt; approve|reject". <see cref="Reasons"/>: why the peers rejected it, from the
    /// votes' verdict codes (<see cref="RejectReasons.Summary"/>), e.g. "Offensive, option 1 x2; no reason x1".
    /// </summary>
    public sealed record StaffQueueEntry(ulong Product, string Name, string Description, string Tags, string Creator,
        uint Price, uint Approvals, uint Rejections, DateTime Created, string Reasons = "");

    /// <summary>
    /// The reason of a rejecting Player Studio vote. The client sends a verdict code with the vote (Lua
    /// ReviewProduct(approve, type, subtype), 0.695 FUN_1435e9f30 → SubmitProductReview FUN_1432704a0) [P]: approve → 1;
    /// reject with (type 1, subtype 0) → 2, (1, 1) → 3, (2, 0) → 4, (2, 1) → 5, (3, 0) → 6, (3, 1) → 7; anything else → 0.
    /// The review window fills its two reason drop-downs from MarketingData.InGamePurchase.ReviewRejectionTypes /
    /// ReviewRejectionSubTypes (as3 PlayerStudioItemReviewDocumentClass), which nothing in the client fills, so their
    /// names are not in the client [P negative: text_695, CodeStringMappings]. The type names here follow the three
    /// grounds of the developer post (Update Notes 18 Jun 2014, Fairan: indecent or offensive content, or copyright
    /// infringement, the latter by name, object or description) [I]; the subtype is shown as option 1 / 2 (its meaning
    /// is
    /// not known yet, trace/0695/agent-leadsC.md L185).
    /// </summary>
    public static class RejectReasons
    {
        public const uint Approve = 1;

        /// <summary>The reason a verdict code stands for ("no reason" for 0, "code N" for one the client never sends).</summary>
        public static string Label(uint verdict) => verdict switch
        {
            0 => "no reason",
            >= 2 and <= 7 => $"{TypeName((verdict - 2) / 2 + 1)}, option {(verdict - 2) % 2 + 1}",
            _ => $"code {verdict}"
        };

        /// <summary>The client's rejection type 1-3 [I names, see the class notes].</summary>
        public static string TypeName(uint type) => type switch
        {
            1 => "Offensive",
            2 => "Copyright (name)",
            3 => "Copyright (object or description)",
            _ => $"type {type}"
        };

        /// <summary>
        /// The rejecting votes of one product as one line, most frequent reason first: "Offensive, option 1 x2; no reason
        /// x1". Approvals (code 1) are left out; empty when no vote rejected.
        /// </summary>
        public static string Summary(IEnumerable<uint> verdicts)
            => string.Join("; ", (verdicts ?? Enumerable.Empty<uint>()).Where(v => v != Approve).GroupBy(v => v)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => $"{Label(g.Key)} x{g.Count()}"));
    }

    /// <summary>
    /// The staff queue as the commerce service's admin reply carries it (CommerceAdminReply text for the command
    /// "psqueue"): one product per line, fields separated by tabs (id, name, description, tags, creator, price, approvals,
    /// rejections, created as Unix seconds, reasons); tabs and line breaks inside the texts become spaces. A line without
    /// the reasons field (an older commerce service) decodes with no reasons.
    /// </summary>
    public static class StaffQueue
    {
        public const string Command = "psqueue";

        public static string Encode(IEnumerable<StaffQueueEntry> entries)
            => string.Join("\n", entries.Select(e => string.Join("\t", e.Product.ToString(CultureInfo.InvariantCulture),
                Clean(e.Name), Clean(e.Description), Clean(e.Tags), Clean(e.Creator), e.Price.ToString(CultureInfo.InvariantCulture),
                e.Approvals.ToString(CultureInfo.InvariantCulture), e.Rejections.ToString(CultureInfo.InvariantCulture),
                new DateTimeOffset(DateTime.SpecifyKind(e.Created, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                Clean(e.Reasons))));

        public static List<StaffQueueEntry> Decode(string text)
        {
            var list = new List<StaffQueueEntry>();
            foreach (string line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.Split('\t');
                if (f.Length < 9 || !ulong.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
                    continue;
                uint U(string s) => uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0;
                long.TryParse(f[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out long created);
                list.Add(new StaffQueueEntry(id, f[1], f[2], f[3], f[4], U(f[5]), U(f[6]), U(f[7]),
                    DateTimeOffset.FromUnixTimeSeconds(created).UtcDateTime, f.Length > 9 ? f[9] : ""));
            }
            return list;
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
