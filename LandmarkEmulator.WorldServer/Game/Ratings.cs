using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
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
    /// One vote per player per site, kept as <see cref="Unlocks.ClaimVote"/> rows (value = claim id).
    /// Contest votes (category "Contests") count for the site's entries in contests that are voting
    /// (<see cref="Contests.Vote"/>) and get their own VoteReply with the contest total.
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
                (bool contestCounted, ulong contestTotal) = Contests.Vote(session, claimId);
                var reply = new List<byte>();
                var rw = new GamePacketWriter(reply);
                rw.Write(VoteReply);
                rw.Write(session.PlayerGuid);
                rw.Write(itemId);
                rw.Write(contestCounted);
                rw.Write(contestTotal);
                session.EnqueueMessage(ZoneMessageOpcode.RatingBase, reply.ToArray());
                log.Info($"{session.CharacterName} contest vote for site {claimId}: {(contestCounted ? "counted" : "not counted")} (total {contestTotal}).");
                return;
            }
            if (category != "Claims")
                return;
            bool own = Claims.OwnerOf(claimId) == session.PlayerGuid;
            bool counted = !own && Unlocks.Add(session, Unlocks.ClaimVote, (uint)claimId);
            if (counted)
                DailyGoals.LikeGiven(session);
            ulong total = Total(claimId);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(VoteReply);
            writer.Write(session.PlayerGuid);
            writer.Write(itemId);
            writer.Write(counted);
            writer.Write(total);
            session.EnqueueMessage(ZoneMessageOpcode.RatingBase, output.ToArray());
            if (own)
                Chat.System(session, "You can't rate your own build site.");
            else if (!counted)
                Chat.System(session, "You have already rated this build site.");
            log.Info($"{session.CharacterName} rated claim {claimId} ({(counted ? "counted" : own ? "own site" : "already rated")}; total {total}).");
        }

        /// <summary>
        /// RatingBase 0x18 CanVoteRequest (the walk-on window of a contest site, FUN_142d2ad00): four strings (category,
        /// item id = the site id, ...) and a u64. Answered with 0x19 CanVoteReply (reader FUN_142d024b0, handler
        /// FUN_142d1f470 → claim entry +0x360 / +0x368, the walk-on's CanVote / TimeCanVote): u64 site, bool may vote, u64
        /// time until a vote is possible (0). May vote = not their own site and not voted yet.
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
                && !Unlocks.Has(session, Unlocks.ClaimVote, (uint)claimId);
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

        /// <summary>How many players rated a build site.</summary>
        public static ulong Total(ulong claimId)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            uint value = (uint)claimId;
            return (ulong)context.CharacterUnlock.Count(u => u.Kind == Unlocks.ClaimVote && u.Value == value);
        }
    }
}
