using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build contests. The client's Contest window (ContestWindow.swf, Build Site Management) lists the open contests
    /// (data source GameData.Contests: Name, ContestId) and enters the selected site with a description and up to three
    /// screenshots (Lua EnterContest).
    ///
    /// - RatingBase 0x1D RequestContestsRequest → 0x1E RequestContestsReply {u64 player, u32 n, n × entry}; entry
    ///   (FUN_142d02390): string key, string name, u32 contest id, u32 state (1 = taking entries: only those are listed),
    ///   u64 submissions start, u64 voting start, u64 judging start (the client's debug line prints them as dates).
    /// - ClaimsBase 0x41 SubmitContestEntry (FUN_142d30a60 → FUN_142d07b90): u64 site, string contest, u32 n × byte
    ///   arrays (screenshots), u8 → 0x18 ContestEntryResults {bool ok, string entry (site) name, string contest name}:
    ///   the client shows ContestSubmission.Success / Fail.
    ///
    /// Contests are defined by the server's operators in Contests/contests.json (created with one open contest, the
    /// "Community Showcase"); entries are kept in Contests/entries.json and screenshots in Contests/shots. Votes come
    /// through the claim rating (<see cref="Ratings"/>, category "Contests").
    ///
    /// Gallery rule (client text 576478 Claims.Error.CannotEnterCompetition, "You must publish your Build Site to the
    /// gallery before submitting to a competition."): the 0.695 Claims.EnterContest native (FUN_1435e5e70) looks the
    /// site's listing up in the account store first and shows that message when there is none; with a listing it sends
    /// ProductUpdate (contest tag + the entry's description, "enter contest" byte) and then SubmitContestEntry.
    /// <see cref="Submit"/> checks the listing again and takes the description handed over by <see cref="Commerce"/>.
    /// Leave Competition (Claims.RemoveFromContest) only sends ProductUpdate with the "leave contest" byte →
    /// <see cref="Withdraw"/>. trace/0695/gallery-695.md §4.
    /// </summary>
    public static class Contests
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new();

        /// <summary>Contest tag words start with this (the client refuses them in site tags, Claims.Error.TagsAreInvalid).</summary>
        public const string TagPrefix = "contest_";
        /// <summary>Entry descriptions from EnterContest's ProductUpdate, waiting for the SubmitContestEntry that follows.</summary>
        private static readonly Dictionary<ulong, string> pendingDescriptions = new();

        public class Contest
        {
            public uint Id { get; set; }
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            /// <summary>
            /// 1 = taking entries, 2 = voting, 3 = judging, 4 = closed. 4 (set by the operators) closes a contest for good;
            /// otherwise the phase follows the dates (<see cref="Phase"/>).
            /// </summary>
            public uint State { get; set; } = 1;
            public long SubmissionsStart { get; set; }
            public long VotingStart { get; set; }
            public long JudgingStart { get; set; }
        }

        public class Entry
        {
            public uint ContestId { get; set; }
            public ulong ClaimId { get; set; }
            public ulong OwnerGuid { get; set; }
            public string Owner { get; set; } = "";
            public string Site { get; set; } = "";
            /// <summary>The entry's description (the Contest window's text, sent in EnterContest's ProductUpdate).</summary>
            public string Description { get; set; } = "";
            public int Screenshots { get; set; }
            public DateTime Submitted { get; set; }
            /// <summary>Players who voted for this entry (one vote each, never the owner).</summary>
            public List<ulong> Voters { get; set; } = new();
        }

        public const uint TakingEntries = 1, Voting = 2, Judging = 3, Closed = 4;

        /// <summary>
        /// A contest's phase now: closed when the operators set 4; else by its dates — entries from SubmissionsStart,
        /// voting from VotingStart, judging from JudgingStart (the client lists the dates with each contest).
        /// </summary>
        public static uint Phase(Contest c)
        {
            if (c.State == Closed)
                return Closed;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now >= c.JudgingStart && c.JudgingStart > 0 ? Judging
                : now >= c.VotingStart && c.VotingStart > 0 ? Voting
                : TakingEntries;
        }

        /// <summary>
        /// A contest vote (the walk-on window's rating sends category "Contests" with the site's id; <see cref="Ratings"/>):
        /// counted once per player for each entry of that site in a contest that is voting. Returns whether it counted
        /// and the site's contest vote total.
        /// </summary>
        public static (bool Counted, ulong Total) Vote(WorldSession session, ulong claimId)
        {
            EnsureLoaded();
            lock (sync)
            {
                var open = entries.Where(e => e.ClaimId == claimId
                    && contests.FirstOrDefault(c => c.Id == e.ContestId) is { } c && Phase(c) == Voting).ToList();
                bool counted = false;
                foreach (Entry e in open)
                {
                    if (e.OwnerGuid == session.PlayerGuid || e.Voters.Contains(session.PlayerGuid))
                        continue;
                    e.Voters.Add(session.PlayerGuid);
                    counted = true;
                }
                if (counted)
                    Save();
                ulong total = (ulong)entries.Where(e => e.ClaimId == claimId).Sum(e => e.Voters.Count);
                return (counted, total);
            }
        }

        private static List<Contest> contests;
        private static List<Entry> entries;
        private static string Folder => Path.Combine(AppContext.BaseDirectory, "Contests");
        private static readonly JsonSerializerOptions json = new() { WriteIndented = true };

        private static void EnsureLoaded()
        {
            if (contests != null)
                return;
            lock (sync)
            {
                if (contests != null)
                    return;
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder, "contests.json");
                if (!File.Exists(file))
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    File.WriteAllText(file, JsonSerializer.Serialize(new List<Contest>
                    {
                        new() { Id = 1, Key = "contest_community_showcase", Name = "Community Showcase", State = 1,
                            SubmissionsStart = now, VotingStart = now + 14 * 86400, JudgingStart = now + 21 * 86400 }
                    }, json));
                }
                try
                {
                    contests = JsonSerializer.Deserialize<List<Contest>>(File.ReadAllText(file)) ?? new();
                    string entryFile = Path.Combine(Folder, "entries.json");
                    entries = File.Exists(entryFile) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(entryFile)) ?? new() : new();
                }
                catch (Exception e)
                {
                    log.Error(e, "Could not read Contests/contests.json or entries.json.");
                    contests ??= new();
                    entries ??= new();
                }
                log.Info($"Contests: {contests.Count} defined, {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}.");
            }
        }

        /// <summary>RatingBase 0x1D: the contest list (Contest window drop-down).</summary>
        public static void SendList(WorldSession session)
        {
            EnsureLoaded();
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)0x1E);
            w.Write(session.PlayerGuid);
            List<Contest> list;
            lock (sync)
                list = contests.ToList();
            w.Write((uint)list.Count);
            foreach (Contest c in list)
            {
                w.Write(c.Key);
                w.Write(c.Name);
                w.Write(c.Id);
                w.Write(Phase(c));
                w.Write((ulong)c.SubmissionsStart);
                w.Write((ulong)c.VotingStart);
                w.Write((ulong)c.JudgingStart);
            }
            session.EnqueueMessage(ZoneMessageOpcode.RatingBase, output.ToArray());
        }

        /// <summary>ClaimsBase 0x41 SubmitContestEntry: the owner enters a site into an open contest.</summary>
        public static void Submit(WorldSession session, byte[] body)
        {
            EnsureLoaded();
            var reader = new GamePacketReader(body);
            ulong claimId = reader.ReadULong();
            string contestName = reader.ReadString();
            var shots = new List<byte[]>();
            try
            {
                uint n = reader.ReadUInt();
                for (int i = 0; i < n && i < 8; i++)
                    shots.Add(reader.ReadBytes(reader.ReadUInt()));
            }
            catch (Exception)
            {
                // Screenshots are optional; a short body just has none.
            }
            ClaimModel site = Claims.Get(claimId);
            bool allowed = session.ContestLimiter.TryTake();
            Contest contest;
            string description;
            lock (sync)
            {
                contest = contests.FirstOrDefault(c => string.Equals(c.Name, contestName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Key, contestName, StringComparison.OrdinalIgnoreCase));
                pendingDescriptions.Remove(claimId, out description);
            }
            // Only a site listed in the gallery may enter (text 576478); the client checks its own listing cache first.
            bool listed = Commerce.IsOnGallery(claimId);
            bool ok = allowed && site != null && site.OwnerGuid == session.PlayerGuid && listed && contest != null && Phase(contest) == TakingEntries;
            if (ok)
            {
                lock (sync)
                {
                    entries.RemoveAll(e => e.ContestId == contest.Id && e.ClaimId == claimId);
                    entries.Add(new Entry
                    {
                        ContestId = contest.Id, ClaimId = claimId, OwnerGuid = session.PlayerGuid, Owner = session.CharacterName ?? "",
                        Site = site.Name ?? "", Description = description ?? "", Screenshots = shots.Count, Submitted = DateTime.UtcNow
                    });
                    Save();
                }
                string shotFolder = Path.Combine(Folder, "shots");
                Directory.CreateDirectory(shotFolder);
                for (int i = 0; i < shots.Count; i++)
                    File.WriteAllBytes(Path.Combine(shotFolder, $"{contest.Id}_{claimId}_{i}.bin"), shots[i]);
            }
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)0x18);
            w.Write(ok);
            w.Write(site?.Name ?? "");
            w.Write(contest?.Name ?? contestName);
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
            log.Info($"{session.CharacterName} entered site {claimId} into \"{contestName}\": {(ok ? $"ok ({shots.Count} screenshot(s))" : !allowed ? "refused (too many entries too fast)" : !listed ? "refused (not published to the gallery)" : "refused")}.");
        }

        /// <summary>
        /// EnterContest's ProductUpdate carried the entry's description (Commerce.UpdateProduct, "enter contest" byte):
        /// kept for the SubmitContestEntry that follows it.
        /// </summary>
        public static void EntryDescription(ulong claimId, string description)
        {
            lock (sync)
                pendingDescriptions[claimId] = description ?? "";
        }

        /// <summary>
        /// Leave Competition (Claims.RemoveFromContest's ProductUpdate, "leave contest" byte), or the site was taken off
        /// the gallery: the owner's entries for the site are withdrawn from contests still taking entries or voting.
        /// </summary>
        public static void Withdraw(WorldSession session, ulong claimId)
        {
            EnsureLoaded();
            int removed;
            lock (sync)
            {
                removed = entries.RemoveAll(e => e.ClaimId == claimId && e.OwnerGuid == session.PlayerGuid
                    && contests.FirstOrDefault(c => c.Id == e.ContestId) is { } c && Phase(c) is TakingEntries or Voting);
                pendingDescriptions.Remove(claimId);
                if (removed > 0)
                    Save();
            }
            if (removed > 0)
                log.Info($"{session.CharacterName} withdrew site {claimId} from {removed} contest(s).");
        }

        /// <summary>The names of the contests a site is entered in (for its gallery entry).</summary>
        public static List<string> NamesOf(ulong claimId)
        {
            EnsureLoaded();
            lock (sync)
                return entries.Where(e => e.ClaimId == claimId)
                    .Select(e => contests.FirstOrDefault(c => c.Id == e.ContestId)?.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        }

        private static void Save()
            => File.WriteAllText(Path.Combine(Folder, "entries.json"), JsonSerializer.Serialize(entries, json));

        /// <summary>Dev "contestphase": shifts a contest's dates so <see cref="Phase"/> gives that phase now (saved).</summary>
        public static string DevSetPhase(uint id, string phase)
        {
            EnsureLoaded();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), week = 7 * 86400;
            lock (sync)
            {
                Contest c = contests.FirstOrDefault(x => x.Id == id);
                if (c == null)
                    return $"no contest {id}";
                (c.SubmissionsStart, c.VotingStart, c.JudgingStart) = phase switch
                {
                    "voting" => (now - 2 * week, now - 60, now + week),
                    "judging" => (now - 3 * week, now - 2 * week, now - 60),
                    _ => (now - 60, now + 2 * week, now + 3 * week)
                };
                if (c.State == Closed)
                    c.State = TakingEntries;
                File.WriteAllText(Path.Combine(Folder, "contests.json"), JsonSerializer.Serialize(contests, json));
                return $"contest {id} now in phase {Phase(c)}";
            }
        }

        /// <summary>Dev "contests": the contests and their entries.</summary>
        public static string Describe()
        {
            EnsureLoaded();
            lock (sync)
                return string.Join("; ", contests.Select(c => $"{c.Id} {c.Name} (phase {Phase(c)}): "
                    + string.Join(", ", entries.Where(e => e.ContestId == c.Id).Select(e => $"{e.Site} by {e.Owner} ({e.Voters.Count} votes)"))));
        }
    }
}
