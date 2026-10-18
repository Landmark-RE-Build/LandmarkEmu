using LandmarkEmulator.Internal.Commerce;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// Build contests in the commerce service (were Game/Contests.cs in each world; world-channel-service.md §5.1: one
    /// Competition tab for everyone, entries from several worlds). The client side is unchanged:
    /// <list type="bullet">
    /// <item>RatingBase 0x1D RequestContestsRequest → 0x1E RequestContestsReply (the Contest window's drop-down, data source
    /// GameData.Contests).</item>
    /// <item>ClaimsBase 0x41 SubmitContestEntry → 0x18 ContestEntryResults: the zone checks the site (exists, the caller's)
    /// and the entry limiter and hands it on (<see cref="CommerceContestEntrySubmit"/>); only a site listed in the gallery
    /// may enter (text 576478 Claims.Error.CannotEnterCompetition), into a contest taking entries.</item>
    /// <item>AddVote "Contests" (the walk-on window's like, sent after the "Claims" one) → VoteReply with the site's
    /// contest total: one vote per player for each entry of that site in a contest that is voting, never the owner's.</item>
    /// <item>Leave Competition (ProductUpdate "leave" byte) and taking the site off the gallery withdraw the owner's
    /// entries from contests still taking entries or voting.</item>
    /// </list>
    /// Contests are defined by the operators (table contest; created with one open contest, the "Community Showcase", like
    /// the in-process Contests/contests.json); a world's own Contests/ files are imported once (<see cref="ImportFile"/>).
    /// Screenshots are kept in the data folder (Contests/shots/&lt;contest&gt;_&lt;world&gt;_&lt;site&gt;_&lt;n&gt;.bin).
    /// </summary>
    public sealed class ContestService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const uint TakingEntries = 1, Voting = 2, Judging = 3, Closed = 4;

        private readonly CommerceHub hub;
        private CommerceStore Store => hub.Store;
        /// <summary>Entry descriptions from EnterContest's ProductUpdate, waiting for the SubmitContestEntry that follows it.</summary>
        private readonly Dictionary<(uint World, ulong Claim), string> pendingDescriptions = new();
        /// <summary>Contests/ files of a world arriving for the import, until its CommerceImportDone.</summary>
        private readonly Dictionary<uint, Dictionary<string, byte[]>> importing = new();

        private string ShotFolder => Path.Combine(hub.DataPath, "Contests", "shots");

        public ContestService(CommerceHub hub) => this.hub = hub;

        /// <summary>
        /// A contest's phase now: closed when the operators set 4; else by its dates — entries from SubmissionsStart,
        /// voting from VotingStart, judging from JudgingStart (Contests.Phase).
        /// </summary>
        public static uint Phase(ContestRow c)
        {
            if (c.State == Closed)
                return Closed;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now >= c.JudgingStart && c.JudgingStart > 0 ? Judging
                : now >= c.VotingStart && c.VotingStart > 0 ? Voting
                : TakingEntries;
        }

        /// <summary>The contests, with the default one created when there are none (as Contests.EnsureLoaded).</summary>
        private List<ContestRow> Contests()
        {
            List<ContestRow> contests = Store.Contests();
            if (contests.Count > 0)
                return contests;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var first = new ContestRow
            {
                Id = 1, Key = "contest_community_showcase", Name = "Community Showcase", State = TakingEntries,
                SubmissionsStart = now, VotingStart = now + 14 * 86400, JudgingStart = now + 21 * 86400
            };
            Store.SaveContest(first, replace: false);
            log.Info("Contests: none defined, created the Community Showcase.");
            return Store.Contests();
        }

        /// <summary>RatingBase 0x1D: the contest list.</summary>
        public void SendList(WorldLink world, CommerceCaller caller)
            => hub.SendClient(world, caller, ClientPackets.ContestList(caller.Character, Contests(), Phase));

        /// <summary>
        /// A contest vote (AddVote "Contests"; body ours: string item id, u64 site — the site on the voter's world): counted
        /// once per player for each entry of that site in a contest that is voting; the reply carries the site's contest
        /// vote total (Contests.Vote + Ratings.HandleAddVote's reply).
        /// </summary>
        public void Vote(WorldLink world, CommerceClientRequest request)
        {
            CommerceCaller caller = request.Caller;
            string itemId;
            ulong claim;
            try
            {
                var r = new Internal.InternalReader(request.Body);
                itemId = r.Str();
                claim = r.U64();
            }
            catch (InvalidDataException)
            {
                return;
            }
            Dictionary<uint, ContestRow> contests = Contests().ToDictionary(c => c.Id);
            bool counted = false;
            foreach (EntryRow e in Store.Entries().Where(e => e.WorldId == caller.World && e.ClaimId == claim))
            {
                if (!contests.TryGetValue(e.ContestId, out ContestRow c) || Phase(c) != Voting)
                    continue;
                bool own = e.OwnerGuid == caller.Character && hub.RealmOf(e.WorldId) == world.Realm;
                if (own || Store.HasVoted(e.ContestId, e.WorldId, e.ClaimId, world.Realm, caller.Character))
                    continue;
                Store.AddVote(e.ContestId, e.WorldId, e.ClaimId, caller.World, caller.Character);
                counted = true;
            }
            ulong total = (ulong)Store.VoteCounts().Where(v => v.Key.World == caller.World && v.Key.Claim == claim).Sum(v => v.Value);
            hub.SendClient(world, caller, ClientPackets.VoteReply(caller.Character, itemId, counted, total));
            log.Info($"{caller.Name} (world {caller.World}) contest vote for site {claim}: {(counted ? "counted" : "not counted")} (total {total}).");
        }

        /// <summary>ClaimsBase 0x41 SubmitContestEntry, checked by the zone: the owner enters a listed site into an open contest.</summary>
        public void Submit(WorldLink world, CommerceContestEntrySubmit submit)
        {
            CommerceCaller caller = submit.Caller;
            ContestRow contest = Contests().FirstOrDefault(c => string.Equals(c.Name, submit.Contest, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Key, submit.Contest, StringComparison.OrdinalIgnoreCase));
            pendingDescriptions.Remove((caller.World, submit.Site), out string description);
            // Only a site listed in the gallery may enter (text 576478); the client checks its own listing cache first.
            bool listed = Store.Listing(caller.World, submit.Site) != null;
            bool ok = submit.Allowed && submit.OwnSite && listed && contest != null && Phase(contest) == TakingEntries;
            if (ok)
            {
                Store.SaveEntry(new EntryRow
                {
                    ContestId = contest.Id, WorldId = caller.World, ClaimId = submit.Site, OwnerGuid = caller.Character, OwnerName = caller.Name ?? "",
                    SiteName = submit.SiteName ?? "", Description = description ?? "", Screenshots = (uint)submit.Screenshots.Count,
                    SubmittedAt = DateTime.UtcNow
                });
                Directory.CreateDirectory(ShotFolder);
                for (int i = 0; i < submit.Screenshots.Count; i++)
                    File.WriteAllBytes(Path.Combine(ShotFolder, $"{contest.Id}_{caller.World}_{submit.Site}_{i}.bin"), submit.Screenshots[i]);
                hub.SendGalleryStatus(caller.World, submit.Site);
            }
            hub.SendClient(world, caller, ClientPackets.ContestEntryResults(ok, submit.SiteName, contest?.Name ?? submit.Contest));
            log.Info($"{caller.Name} (world {caller.World}) entered site {submit.Site} into \"{submit.Contest}\": " +
                (ok ? $"ok ({submit.Screenshots.Count} screenshot(s))" : !submit.Allowed ? "refused (too many entries too fast)"
                    : !listed ? "refused (not published to the gallery)" : "refused") + ".");
        }

        /// <summary>EnterContest's ProductUpdate carried the entry's description: kept for the SubmitContestEntry that follows it.</summary>
        public void EntryDescription(uint world, ulong claim, string description) => pendingDescriptions[(world, claim)] = description ?? "";

        /// <summary>
        /// Leave Competition, or the site was taken off the gallery: the owner's entries for the site leave the contests
        /// still taking entries or voting (Contests.Withdraw).
        /// </summary>
        public void Withdraw(uint world, ulong claim, CommerceCaller owner, bool sendStatus)
        {
            Dictionary<uint, ContestRow> contests = Contests().ToDictionary(c => c.Id);
            int removed = 0;
            string realm = hub.RealmOf(owner.World);
            foreach (EntryRow e in Store.Entries().Where(e => e.WorldId == world && e.ClaimId == claim && e.OwnerGuid == owner.Character
                && hub.RealmOf(e.WorldId) == realm))
            {
                if (contests.TryGetValue(e.ContestId, out ContestRow c) && Phase(c) is TakingEntries or Voting)
                {
                    Store.RemoveEntry(e.ContestId, e.WorldId, e.ClaimId);
                    removed++;
                }
            }
            pendingDescriptions.Remove((world, claim));
            if (removed > 0)
                log.Info($"{owner.Name} withdrew site {claim} (world {world}) from {removed} contest(s).");
            if (sendStatus && removed > 0)
                hub.SendGalleryStatus(world, claim);
        }

        /// <summary>The names of the contests a site is entered in (its gallery entry; Contests.NamesOf).</summary>
        public List<string> NamesOf(uint world, ulong claim, List<ContestRow> contests = null, List<EntryRow> entries = null)
        {
            contests ??= Store.Contests();
            entries ??= Store.Entries();
            return entries.Where(e => e.WorldId == world && e.ClaimId == claim)
                .Select(e => contests.FirstOrDefault(c => c.Id == e.ContestId)?.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        }

        /// <summary>Every site of a world with contest entries (for the market snapshot).</summary>
        public List<ulong> SitesWithEntries(uint world) => Store.Entries().Where(e => e.WorldId == world).Select(e => e.ClaimId).Distinct().ToList();

        /// <summary>"contestphase &lt;id&gt; &lt;voting|judging|entries&gt;": shifts a contest's dates so it is in that phase now (Contests.DevSetPhase).</summary>
        public (bool Ok, string Text) SetPhase(string arguments)
        {
            string[] a = (arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length < 2 || !uint.TryParse(a[0], out uint id))
                return (false, "contestphase <id> <voting|judging|entries>");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), week = 7 * 86400;
            ContestRow c = Contests().FirstOrDefault(x => x.Id == id);
            if (c == null)
                return (false, $"no contest {id}");
            (c.SubmissionsStart, c.VotingStart, c.JudgingStart) = a[1] switch
            {
                "voting" => (now - 2 * week, now - 60, now + week),
                "judging" => (now - 3 * week, now - 2 * week, now - 60),
                _ => (now - 60, now + 2 * week, now + 3 * week)
            };
            if (c.State == Closed)
                c.State = TakingEntries;
            Store.SaveContest(c);
            return (true, $"contest {id} now in phase {Phase(c)}");
        }

        /// <summary>"contests": the contests and their entries (Contests.Describe), with each entry's world.</summary>
        public string Describe()
        {
            List<ContestRow> contests = Contests();
            List<EntryRow> entries = Store.Entries();
            Dictionary<(uint, uint, ulong), int> votes = Store.VoteCounts();
            return string.Join("; ", contests.Select(c => $"{c.Id} {c.Name} (phase {Phase(c)}): "
                + string.Join(", ", entries.Where(e => e.ContestId == c.Id).Select(e =>
                    $"{e.SiteName} by {e.OwnerName} on world {e.WorldId} ({votes.GetValueOrDefault((e.ContestId, e.WorldId, e.ClaimId))} votes)"))));
        }

        // ---- the one-time import of a world's Contests/ files ----------------------------------------------------------

        /// <summary>One file of a world's Contests/ folder (contests.json, entries.json, shots/…), kept until the import is done.</summary>
        public void ImportFile(uint world, CommerceImportFile file)
        {
            if (!importing.TryGetValue(world, out Dictionary<string, byte[]> files))
                importing[world] = files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            files[file.Name.Replace('\\', '/')] = file.Data;
        }

        private sealed class OldContest
        {
            public uint Id { get; set; }
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public uint State { get; set; } = 1;
            public long SubmissionsStart { get; set; }
            public long VotingStart { get; set; }
            public long JudgingStart { get; set; }
        }

        private sealed class OldEntry
        {
            public uint ContestId { get; set; }
            public ulong ClaimId { get; set; }
            public ulong OwnerGuid { get; set; }
            public string Owner { get; set; } = "";
            public string Site { get; set; } = "";
            public string Description { get; set; } = "";
            public int Screenshots { get; set; }
            public DateTime Submitted { get; set; }
            public List<ulong> Voters { get; set; } = new();
        }

        /// <summary>
        /// The world sent all its Contests/ files: contests.json → contest (the first world's file replaces the definitions
        /// with the same id — it was made from the same default — later worlds only add missing ones), entries.json →
        /// contest_entry and contest_vote with this world's id, shots/&lt;contest&gt;_&lt;site&gt;_&lt;n&gt;.bin →
        /// Contests/shots/&lt;contest&gt;_&lt;world&gt;_&lt;site&gt;_&lt;n&gt;.bin. Marked done for the world.
        /// </summary>
        public string ImportDone(uint world)
        {
            importing.Remove(world, out Dictionary<string, byte[]> files);
            files ??= new Dictionary<string, byte[]>();
            bool first = !Store.IsImported("contests");
            int contests = 0, entries = 0, votes = 0, shots = 0;
            try
            {
                if (files.TryGetValue("contests.json", out byte[] contestFile))
                    foreach (OldContest c in JsonSerializer.Deserialize<List<OldContest>>(contestFile) ?? new())
                    {
                        Store.SaveContest(new ContestRow
                        {
                            Id = c.Id, Key = c.Key ?? "", Name = c.Name ?? "", State = c.State, SubmissionsStart = c.SubmissionsStart,
                            VotingStart = c.VotingStart, JudgingStart = c.JudgingStart
                        }, replace: first);
                        contests++;
                    }
                if (files.TryGetValue("entries.json", out byte[] entryFile))
                    foreach (OldEntry e in JsonSerializer.Deserialize<List<OldEntry>>(entryFile) ?? new())
                    {
                        Store.SaveEntry(new EntryRow
                        {
                            ContestId = e.ContestId, WorldId = world, ClaimId = e.ClaimId, OwnerGuid = e.OwnerGuid, OwnerName = e.Owner ?? "",
                            SiteName = e.Site ?? "", Description = e.Description ?? "", Screenshots = (uint)Math.Max(0, e.Screenshots),
                            SubmittedAt = DateTime.SpecifyKind(e.Submitted, DateTimeKind.Utc)
                        });
                        entries++;
                        foreach (ulong voter in e.Voters.Distinct())
                        {
                            Store.AddVote(e.ContestId, world, e.ClaimId, world, voter);
                            votes++;
                        }
                    }
            }
            catch (Exception e) when (e is JsonException or NotSupportedException)
            {
                log.Error($"Contest import from world {world}: unreadable file ({e.Message}); the import stays open for the next link.");
                return null;
            }
            Directory.CreateDirectory(ShotFolder);
            foreach ((string name, byte[] data) in files.Where(f => f.Key.StartsWith("shots/", StringComparison.OrdinalIgnoreCase)))
            {
                // shots/<contest>_<site>_<n>.bin → <contest>_<world>_<site>_<n>.bin
                string[] parts = Path.GetFileNameWithoutExtension(name).Split('_');
                if (parts.Length != 3 || !uint.TryParse(parts[0], out uint contest) || !ulong.TryParse(parts[1], out ulong site)
                    || !int.TryParse(parts[2], out int n))
                    continue;
                File.WriteAllBytes(Path.Combine(ShotFolder, $"{contest}_{world}_{site}_{n}.bin"), data);
                shots++;
            }
            string note = $"world {world}: {contests} contest(s), {entries} entr(ies), {votes} vote(s), {shots} screenshot(s)";
            Store.MarkImported($"contests:{world}", note);
            if (first)
                Store.MarkImported("contests", $"definitions from world {world}");
            return note;
        }
    }
}
