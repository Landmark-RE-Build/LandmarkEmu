using LandmarkEmulator.Internal.Social;

namespace LandmarkEmulator.Internal.WorldChannel
{
    /// <summary>
    /// The claim-rating messages between a world's world-channel service and its zone
    /// (trace/architecture/world-channel-service.md §3.2, §6.1, §6.5; build step 6; transport: internal-transport.md §3).
    /// <para>
    /// Why the split: the walk-on window's like (Lua Claims.RateClaim, FUN_1435329d0 / 0.695 FUN_1435e4870) sends
    /// RatingPacketAddVote 0xA7/8 on the <b>World</b> channel, category "Claims" then "Contests", and the contest walk-on
    /// asks RatingPacketCanVoteRequest 0xA7/0x18 on World too (FUN_142d1ff50, FUN_142d2ad00 / FUN_142dbecb0,
    /// FUN_142dc8ec0); the client reads VoteReply 0x12 (FUN_142d02880) and CanVoteReply 0x19 (FUN_142d024b0) in its
    /// RatingBase handler whatever channel they came on. So the "Claims" votes belong to the World back end, which keeps
    /// them in its own store (`lmemu_wc_&lt;world&gt;`.claim_vote, §6.1), while the zone knows the claims themselves
    /// (owner, tags, gallery listing) and runs the daily goals and achievements a like counts for.
    /// </para>
    /// <para>
    /// Every message here is <b>ours</b> (live's internal layouts are not known, L48): the client families Claims 0xF9
    /// and RatingBase 0xA7 with a sub from 0x10000 up (spec §6.2), so they can never be taken for a client sub (u8).
    /// They travel as a <see cref="ServicePacket"/> on the world channel's link to the zone (ZONE_INTERNAL_PORT).
    /// Ids 0x10040-0x10042 are the spec's (§6.5); 0x10043 and 0x10044 are additions (internal-messages.md §2).
    /// </para>
    /// </summary>
    public static class RatingMessages
    {
        /// <summary>Claims 0xF9 (the client's family; its subs are u8).</summary>
        public const ushort ClaimsFamily = 0xF9;

        /// <summary>RatingBase 0xA7 (the client's family; its subs are u8).</summary>
        public const ushort RatingFamily = 0xA7;

        public const uint ClaimInfoQuerySub = 0x10040, ClaimInfoReplySub = 0x10041;

        public const uint RatingVoteCountedSub = 0x10042, RatingVoteRefusedSub = 0x10043, RatingClientRequestSub = 0x10044;

        /// <summary><see cref="RatingVoteRefused.Reason"/>: the voter owns the site ("You can't rate your own build site.").</summary>
        public const byte RefusedOwnSite = 1;
        /// <summary><see cref="RatingVoteRefused.Reason"/>: the site is not listed in the gallery, or does not exist (P5630).</summary>
        public const byte RefusedNotListed = 2;
        /// <summary><see cref="RatingVoteRefused.Reason"/>: the voter liked this site before.</summary>
        public const byte RefusedAlreadyRated = 3;

        /// <summary>The rating message in a service packet, or null when it isn't one of these.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            if (packet.Family != ClaimsFamily && packet.Family != RatingFamily)
                return null;
            var r = new InternalReader(packet.Body);
            return (packet.Family, packet.Sub) switch
            {
                (ClaimsFamily, ClaimInfoQuerySub) => ClaimInfoQuery.Read(r),
                (ClaimsFamily, ClaimInfoReplySub) => ClaimInfoReply.Read(r),
                (RatingFamily, RatingVoteCountedSub) => RatingVoteCounted.Read(r),
                (RatingFamily, RatingVoteRefusedSub) => RatingVoteRefused.Read(r),
                (RatingFamily, RatingClientRequestSub) => RatingClientRequest.Read(r),
                _ => null
            };
        }
    }

    /// <summary>
    /// ClaimInfoQuery (world channel → zone, 0xF9/0x10040; spec §6.5): u32 request, u64 claim. The world channel needs
    /// what only the zone knows about a build site before it answers a vote or a can-vote request: its owner (no vote
    /// for your own site), whether it exists and whether it is listed in the gallery (P5630, 25 May 2016: only builds
    /// listed in the Gallery can be liked; today Commerce.IsOnGallery in the zone, the commerce
    /// service's CommerceGalleryStatus later, §6.3). The spec's body is the claim alone; the request id (ours) pairs the
    /// answer with the waiting client request.
    /// </summary>
    public sealed class ClaimInfoQuery : ISocialMessage
    {
        public ushort Family => RatingMessages.ClaimsFamily;
        public uint Sub => RatingMessages.ClaimInfoQuerySub;
        public uint RequestId { get; init; }
        public ulong ClaimId { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).U64(ClaimId);
        public static ClaimInfoQuery Read(InternalReader r) => new() { RequestId = r.U32(), ClaimId = r.U64() };
    }

    /// <summary>
    /// ClaimInfoReply (zone → world channel, 0xF9/0x10041; spec §6.5): u32 request, u64 claim, u64 owner, u32 zone (the
    /// landscape), string tags, bool exists, bool listed in the gallery. The spec's body plus the request id and the
    /// gallery flag (the like rule, see <see cref="ClaimInfoQuery"/>).
    /// </summary>
    public sealed class ClaimInfoReply : ISocialMessage
    {
        public ushort Family => RatingMessages.ClaimsFamily;
        public uint Sub => RatingMessages.ClaimInfoReplySub;
        public uint RequestId { get; init; }
        public ulong ClaimId { get; init; }
        public ulong Owner { get; init; }
        public uint ZoneId { get; init; }
        public string Tags { get; init; } = "";
        public bool Exists { get; init; }
        public bool Listed { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).U64(ClaimId).U64(Owner).U32(ZoneId).Str(Tags).Bool(Exists).Bool(Listed);
        public static ClaimInfoReply Read(InternalReader r) => new()
        {
            RequestId = r.U32(), ClaimId = r.U64(), Owner = r.U64(), ZoneId = r.U32(), Tags = r.Str(), Exists = r.Bool(), Listed = r.Bool()
        };
    }

    /// <summary>
    /// RatingVoteCounted (world channel → zone, 0xA7/0x10042; spec §6.5): u64 voter, u64 claim, u64 owner. A like was
    /// counted in the world channel's store; the zone does what the in-process Ratings did after counting it: the daily
    /// goal (DailyGoals.LikeGiven), the achievements of the voter (LikeGivenOrReceived, LikedTaggedSite, LikedSite) and of
    /// the owner when online (LikeGivenOrReceived), and it keeps its read copy of the vote (character_unlock kind 11,
    /// which the Showcase gallery count and the zone's own fallback path read).
    /// </summary>
    public sealed class RatingVoteCounted : ISocialMessage
    {
        public ushort Family => RatingMessages.RatingFamily;
        public uint Sub => RatingMessages.RatingVoteCountedSub;
        public ulong Voter { get; init; }
        public ulong ClaimId { get; init; }
        public ulong Owner { get; init; }

        public void Write(InternalWriter w) => w.U64(Voter).U64(ClaimId).U64(Owner);
        public static RatingVoteCounted Read(InternalReader r) => new() { Voter = r.U64(), ClaimId = r.U64(), Owner = r.U64() };
    }

    /// <summary>
    /// RatingVoteRefused (ours, world channel → zone, 0xA7/0x10043; not in the spec): u64 voter, u64 claim, u8 reason
    /// (<see cref="RatingMessages.RefusedOwnSite"/> / NotListed / AlreadyRated). The in-process Ratings prints a system
    /// chat line after a refused like; the zone prints the same line (Chat.System) when this arrives, as the encounter
    /// broker's EncounterNotice does for the duel lines. The client's VoteReply (counted false) is sent by the world
    /// channel itself.
    /// </summary>
    public sealed class RatingVoteRefused : ISocialMessage
    {
        public ushort Family => RatingMessages.RatingFamily;
        public uint Sub => RatingMessages.RatingVoteRefusedSub;
        public ulong Voter { get; init; }
        public ulong ClaimId { get; init; }
        public byte Reason { get; init; }

        public void Write(InternalWriter w) => w.U64(Voter).U64(ClaimId).U8(Reason);
        public static RatingVoteRefused Read(InternalReader r) => new() { Voter = r.U64(), ClaimId = r.U64(), Reason = r.U8() };
    }

    /// <summary>
    /// RatingClientRequest (ours, zone → world channel, 0xA7/0x10044; not in the spec): u64 connection, u64 character,
    /// then a whole client RatingBase packet (bytes A7 00, u8 sub, body). A rating request that starts in the zone — the
    /// dev command <c>ratevote</c>, which plays the walk-on window's like — while a world channel is linked, so the vote
    /// is counted where the client's own votes are. The world channel treats it as a channel-1 packet of that
    /// connection: its rating broker answers it, or (broker off) it goes back to the zone as a tunnelled packet.
    /// </summary>
    public sealed class RatingClientRequest : ISocialMessage
    {
        public ushort Family => RatingMessages.RatingFamily;
        public uint Sub => RatingMessages.RatingClientRequestSub;
        public ulong ConnectionId { get; init; }
        public ulong Character { get; init; }
        public byte[] Packet { get; init; } = System.Array.Empty<byte>();

        public void Write(InternalWriter w) => w.U64(ConnectionId).U64(Character).Bytes(Packet);
        public static RatingClientRequest Read(InternalReader r) => new() { ConnectionId = r.U64(), Character = r.U64(), Packet = r.Rest() };
    }
}
