using LandmarkEmulator.Internal.WorldChannel;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Ratings (RatingBase 0xA7, u8 sub). Visitors vote for a build site from its walk-on window (Lua Claims.RateClaim,
    /// FUN_1435329d0), which sends AddVote (sub 8) twice: category "Claims" then "Contests", both for the site's id.
    /// AddVote body (writer FUN_143534b40): string category, string item id (the claim id in decimal), string, string,
    /// f32 score (1.0), u64. The answer is VoteReply (sub 0x12, reader FUN_142d02880): u64, string item id, bool
    /// success, u64 the item's vote total (the client stores it on its list entry for that item).
    /// One vote per player per site, kept as <see cref="Unlocks.ClaimVote"/> rows (value = claim id), and only for sites
    /// listed in the gallery (P5630, 25 May 2016: "It is only possible to Like a build that is listed in the Gallery";
    /// the 0.695 walk-on window shows the Like button only for tagged sites — WalkOnText CanVote is "0" while the claim's
    /// tag string is empty, part_0069.c 19884 — and the server checks the listing itself, <see cref="Commerce.IsOnGallery"/>
    /// through the commerce seam, WorldChannelServices.Commerce.IsOnGallery: the zone's cache of the commerce service's
    /// CommerceGalleryStatus when COMMERCE_ADDRESS is set).
    /// Contest votes (category "Contests") count for the site's entries in contests that are voting
    /// (<see cref="Contests.Vote"/>, or the commerce service) and get their own VoteReply with the contest total, sent by
    /// the commerce back end (ICommerceService.ContestVote).
    /// <para>
    /// Build step 6 (world-channel-service.md §6.5, 2026-10-16): with the world-channel container running, the "Claims"
    /// votes and CanVoteRequest never reach the zone — LandmarkEmulator.WorldChannel.RatingBroker answers them from its own
    /// store (`lmemu_wc_&lt;world&gt;`.claim_vote) with the same packets, asks the zone about the site
    /// (<see cref="ClaimInfo"/>) and reports the outcome (<see cref="BrokerCounted"/> / <see cref="BrokerRefused"/>). The
    /// handlers below are the in-process path and the fallback while that service is away (spec §7), unchanged. The zone
    /// keeps its kind-11 rows as a read copy (written for the service's votes too), which <see cref="Total"/> (the
    /// Showcase gallery) and this fallback read.
    /// </para>
    /// </summary>
    public static class Ratings
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte AddVote = 8, VoteReply = 0x12;

        public static void HandleAddVote(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            reader.ReadByte();                     // sub
            string category = reader.ReadString();
            string itemId = reader.ReadString();
            if (!ulong.TryParse(itemId, out ulong claimId) || claimId > uint.MaxValue)
            {
                log.Debug($"{session.CharacterName} voted for unknown item '{itemId}' ({category}).");
                return;
            }
            if (category == "Contests")
            {
                // Counted by the commerce back end, which sends the VoteReply with the site's contest total (in-process
                // Contests.Vote, or the commerce service; spec ContestVote).
                WorldChannelServices.Commerce.ContestVote(session, itemId, claimId);
                return;
            }
            if (category != "Claims")
                return;
            bool own = Claims.OwnerOf(claimId) == session.PlayerGuid;
            bool listed = WorldChannelServices.Commerce.IsOnGallery(claimId);
            bool counted = !own && listed && Unlocks.Add(session, Unlocks.ClaimVote, (uint)claimId);
            if (counted)
                LikeCounted(session, claimId, Claims.OwnerOf(claimId));
            ulong total = Total(claimId);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(VoteReply);
            writer.Write(session.PlayerGuid);
            writer.Write(itemId);
            writer.Write(counted);
            writer.Write(total);
            session.EnqueueMessage(ZoneMessageOpcode.RatingBase, output.ToArray());
            if (!counted)
                Chat.System(session, RefusedText(own ? RatingMessages.RefusedOwnSite
                    : !listed ? RatingMessages.RefusedNotListed : RatingMessages.RefusedAlreadyRated));
            log.Info($"{session.CharacterName} rated claim {claimId} ({(counted ? "counted" : own ? "own site" : !listed ? "not in the gallery" : "already rated")}; total {total}).");
        }

        /// <summary>
        /// RatingBase 0x18 CanVoteRequest (the walk-on window of a contest site, FUN_142d2ad00): four strings (category,
        /// item id = the site id, ...) and a u64. Answered with 0x19 CanVoteReply (reader FUN_142d024b0, handler
        /// FUN_142d1f470 → claim entry +0x360 / +0x368, the walk-on's CanVote / TimeCanVote): u64 site, bool may vote, u64
        /// time until a vote is possible (0). May vote = not their own site, listed in the gallery and not voted yet.
        /// </summary>
        public static void HandleCanVote(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            reader.ReadByte();                     // sub
            ulong claimId = 0;
            string key = "";
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    string text = reader.ReadString();
                    if (ulong.TryParse(text, out ulong id) && claimId == 0)
                    {
                        claimId = id;
                        key = text;
                    }
                }
            }
            catch (Exception)
            {
                // A shorter body than expected: answer for what was found.
            }
            bool may = claimId != 0 && claimId <= uint.MaxValue && Claims.OwnerOf(claimId) != session.PlayerGuid
                && WorldChannelServices.Commerce.IsOnGallery(claimId) && !Unlocks.Has(session, Unlocks.ClaimVote, (uint)claimId);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            // CanVoteReply (reader FUN_142d024b0, handler FUN_142d1f470 → FUN_142d32fc0): u8 sub, u64, string = the
            // build site's key as the request sent it (parsed back to the u64 the client's site map is keyed by),
            // bool may vote, u64 (kept by the client when voting is refused). Without the string the client's reader
            // ran past the end and the whole reply was dropped (found by the s2cprobe reader check, 2026-10-12).
            writer.Write((byte)0x19);
            writer.Write(claimId);
            writer.Write(key);
            writer.Write(may);
            writer.Write(0ul);
            session.EnqueueMessage(ZoneMessageOpcode.RatingBase, output.ToArray());
        }

        /// <summary>What a counted like is worth: the daily goal and the achievements of the voter, and of the owner when online.</summary>
        private static void LikeCounted(WorldSession voter, ulong claimId, ulong ownerGuid)
        {
            DailyGoals.LikeGiven(voter);
            Achievements.LikeGivenOrReceived(voter);
            Achievements.LikedTaggedSite(voter, Claims.Get(claimId)?.Tags);
            Achievements.LikedSite(voter, claimId);   // Getting Around: Like a gallery site (1307)
            if (PlayerManager.FindByGuid(ownerGuid) is WorldSession owner)
                Achievements.LikeGivenOrReceived(owner);
        }

        /// <summary>The system chat line after a refused like (<see cref="RatingMessages"/> Refused* reasons).</summary>
        private static string RefusedText(byte reason) => reason switch
        {
            RatingMessages.RefusedOwnSite => "You can't rate your own build site.",
            RatingMessages.RefusedNotListed => "Only build sites listed in the gallery can be liked.",
            _ => "You have already rated this build site."
        };

        // ---- the world-channel service's rating broker (build step 6; WorldChannelLink) ---------------------------------

        /// <summary>
        /// ClaimInfoQuery (spec §6.5): what the world channel needs to judge a like or a can-vote request — the site's
        /// owner (0 when it doesn't exist), landscape, tags, whether it exists and whether it is listed in the gallery.
        /// </summary>
        public static ClaimInfoReply ClaimInfo(uint requestId, ulong claimId)
        {
            var claim = Claims.Get(claimId);
            return new ClaimInfoReply
            {
                RequestId = requestId, ClaimId = claimId, Owner = claim?.OwnerGuid ?? 0, ZoneId = claim?.ZoneId ?? 0,
                Tags = claim?.Tags ?? "", Exists = claim != null, Listed = WorldChannelServices.Commerce.IsOnGallery(claimId)
            };
        }

        /// <summary>
        /// RatingVoteCounted (spec §6.5): the world channel counted a like. The zone keeps its read copy (kind 11, also for
        /// an offline voter) and, with the voter online, runs what the in-process path did after counting.
        /// </summary>
        public static void BrokerCounted(ulong voterGuid, ulong claimId, ulong ownerGuid)
        {
            if (claimId <= uint.MaxValue)
                Unlocks.Add(voterGuid, Unlocks.ClaimVote, (uint)claimId);
            if (PlayerManager.FindByGuid(voterGuid) is WorldSession voter)
                LikeCounted(voter, claimId, ownerGuid);
            log.Info($"Character {voterGuid} rated claim {claimId} (counted by the world channel).");
        }

        /// <summary>RatingVoteRefused (ours): the world channel refused a like; the voter gets the in-process chat line.</summary>
        public static void BrokerRefused(ulong voterGuid, ulong claimId, byte reason)
        {
            if (PlayerManager.FindByGuid(voterGuid) is WorldSession voter)
                Chat.System(voter, RefusedText(reason));
        }

        /// <summary>How many players rated a build site.</summary>
        public static ulong Total(ulong claimId)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            uint value = (uint)claimId;
            return (ulong)context.CharacterUnlock.Count(u => u.Kind == Unlocks.ClaimVote && u.Value == value);
        }
    }
}
