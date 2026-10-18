using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Internal.WorldChannel;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// Build-site ratings ("likes"; trace/architecture/world-channel-service.md §3.2, §6.1, §6.5; build step 6, §8). The
    /// client sends both rating requests on the World channel:
    /// <list type="bullet">
    /// <item>RatingPacketAddVote 0xA7/8 (Lua Claims.RateClaim FUN_1435329d0 / 0.695 FUN_1435e4870; writer FUN_143534b40):
    /// string category, string item id (the claim id in decimal), string, string, f32 score, u64. Sent twice per like,
    /// "Claims" then "Contests". The "Claims" vote is answered here; "Contests" (and any other category) goes on to the
    /// zone, where the contest votes are still counted (Contests.Vote through LocalCommerceService: the `commerce`
    /// container of §6.3 / §8 step 8 that would take ContestVote does not exist yet).</item>
    /// <item>RatingPacketCanVoteRequest 0xA7/0x18 (the contest walk-on, FUN_142d2ad00 / FUN_142dc8ec0): four strings and a
    /// u64; the first string that parses as a number is the site.</item>
    /// </list>
    /// The rules are the zone's Ratings.cs, unchanged: one like per player per site, never your own site, only sites
    /// listed in the gallery (P5630, 25 May 2016). The votes live in this service's store (<see cref="VoteStore"/>,
    /// `lmemu_wc_&lt;world&gt;`.claim_vote); what only the zone knows — the site's owner, whether it exists and is listed in
    /// the gallery — comes from a <b>ClaimInfoQuery</b> per request, and a counted like goes back as
    /// <b>RatingVoteCounted</b> (the zone runs the daily goal and the achievements, and keeps its kind-11 read copy) or a
    /// refused one as <b>RatingVoteRefused</b> (the zone prints the same chat line as before).
    /// <para>
    /// The packets to the client are byte-identical to Ratings.cs (RatingBase 0xA700 → bytes A7 00, u8 sub; numbered as
    /// in 0.695, sent as given): VoteReply 0x12 (reader FUN_142d02880) {u64 voter, string
    /// item id, bool counted, u64 the site's total} and CanVoteReply 0x19 (reader FUN_142d024b0, handler FUN_142d1f470)
    /// {u64 site, string the site's key as sent, bool may vote, u64 0}, on the Zone destination as the zone's own replies.
    /// One reply to the requester per request, as before: nothing is broadcast (bandwidth rule 8).
    /// </para>
    /// <para>
    /// When the service is away the gateway sends channel 1 to the zone and Ratings.cs answers as before (spec §7); when
    /// this broker is switched off (WorldChannel.json ClaimRatings false) or has no store, the rating packets are handed
    /// on to the zone unchanged. Store calls run on one worker thread, never on a link's receive thread.
    /// </para>
    /// </summary>
    public static class RatingBroker
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte AddVoteSub = 8, VoteReplySub = 0x12, CanVoteSub = 0x18, CanVoteReplySub = 0x19;

        /// <summary>A request whose ClaimInfoReply has not arrived after this long is dropped (the client repeats UI requests).</summary>
        private static readonly TimeSpan PendingLifetime = TimeSpan.FromSeconds(30);

        private sealed class Pending
        {
            public bool CanVote;
            public ulong Connection, Character, ClaimId;
            /// <summary>The item id (AddVote) or site key (CanVote) exactly as the client sent it; the reply echoes it.</summary>
            public string Key = "";
            public DateTime At = DateTime.UtcNow;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Pending> pending = new();
        private static uint nextRequest;

        private static readonly BlockingCollection<Action> work = new();
        private static Thread worker;

        private static Func<IInternalMessage, bool> toGateway, toZone;

        /// <summary>WorldChannel.json ClaimRatings (default true): false hands the rating packets on to the zone.</summary>
        public static bool Enabled { get; set; } = true;

        private static volatile VoteStore store;

        /// <summary>The vote store (set once it is open and imported); null = the votes stay in the zone (the packets are handed on).</summary>
        public static VoteStore Store { get => store; set => store = value; }

        /// <summary>Active: switched on and a store opened.</summary>
        public static bool Active => Enabled && Store != null;

        /// <summary>The two links' send functions (<see cref="ChannelRelay"/>) and the store worker.</summary>
        public static void Start(Func<IInternalMessage, bool> gateway, Func<IInternalMessage, bool> zone)
        {
            toGateway = gateway;
            toZone = zone;
            if (worker != null)
                return;
            worker = new Thread(() =>
            {
                foreach (Action job in work.GetConsumingEnumerable())
                {
                    try
                    {
                        job();
                    }
                    catch (Exception e)
                    {
                        log.Error($"Rating store call failed: {e.Message}");
                    }
                }
            }) { IsBackground = true, Name = "RatingBroker" };
            worker.Start();
        }

        /// <summary>
        /// A channel-1 packet from the gateway: true when it was a rating request this broker took (AddVote "Claims",
        /// CanVoteRequest); false hands it on to the zone.
        /// </summary>
        public static bool TryTake(TunnelFromExternal packet)
        {
            byte[] p = packet.Packet;
            if (!Active || p.Length < 3 || p[0] != 0xA7 || p[1] != 0x00)
                return false;
            switch (p[2])
            {
                case AddVoteSub:
                    return AddVote(packet);
                case CanVoteSub:
                    return CanVote(packet);
                default:
                    // RequestContestsRequest 0x1D and the rest: the zone's (commerce, in-process).
                    return false;
            }
        }

        /// <summary>AddVote (Ratings.HandleAddVote): the "Claims" category is ours.</summary>
        private static bool AddVote(TunnelFromExternal packet)
        {
            string category, itemId;
            try
            {
                var r = new InternalReader(packet.Packet, 3);
                category = r.Str();
                itemId = r.Str();
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable AddVote from connection {packet.ConnectionId:X}: {e.Message}");
                return true;
            }
            if (category != "Claims")
                return false;
            if (!ulong.TryParse(itemId, out ulong claimId) || claimId > uint.MaxValue)
            {
                log.Debug($"Character {packet.CharacterGuid} voted for unknown item '{itemId}' ({category}).");
                return true;
            }
            Ask(new Pending { Connection = packet.ConnectionId, Character = packet.CharacterGuid, ClaimId = claimId, Key = itemId });
            return true;
        }

        /// <summary>CanVoteRequest (Ratings.HandleCanVote): the first of the four strings that parses as a number is the site.</summary>
        private static bool CanVote(TunnelFromExternal packet)
        {
            ulong claimId = 0;
            string key = "";
            try
            {
                var r = new InternalReader(packet.Packet, 3);
                for (int i = 0; i < 4; i++)
                {
                    string text = r.Str();
                    if (ulong.TryParse(text, out ulong id) && claimId == 0)
                    {
                        claimId = id;
                        key = text;
                    }
                }
            }
            catch (Exception)
            {
                // A shorter body than expected: answer for what was found (as Ratings.HandleCanVote).
            }
            var request = new Pending { CanVote = true, Connection = packet.ConnectionId, Character = packet.CharacterGuid, ClaimId = claimId, Key = key };
            if (claimId == 0 || claimId > uint.MaxValue)
            {
                // No site to ask the zone about: "may not" at once, as the zone did.
                SendCanVoteReply(request, false);
                return true;
            }
            Ask(request);
            return true;
        }

        /// <summary>Keeps the request and asks the zone about the site (ClaimInfoQuery).</summary>
        private static void Ask(Pending request)
        {
            uint id;
            lock (sync)
            {
                // Requests the zone never answered (it went away mid-request) are dropped.
                DateTime now = DateTime.UtcNow;
                foreach (uint old in pending.Where(kv => now - kv.Value.At > PendingLifetime).Select(kv => kv.Key).ToList())
                    pending.Remove(old);
                if (++nextRequest == 0)
                    nextRequest = 1;
                id = nextRequest;
                pending[id] = request;
            }
            if (!Zone(new ClaimInfoQuery { RequestId = id, ClaimId = request.ClaimId }))
            {
                lock (sync)
                    pending.Remove(id);
                log.Warn($"Rating request of character {request.Character} for site {request.ClaimId}: the zone is unreachable.");
            }
        }

        /// <summary>A rating message from the zone (ClaimInfoReply). True when it was one.</summary>
        public static bool FromZone(ServicePacket packet)
        {
            switch (RatingMessages.Parse(packet))
            {
                case ClaimInfoReply info:
                    Pending request;
                    lock (sync)
                        if (!pending.Remove(info.RequestId, out request))
                            return true;
                    work.Add(() => Complete(request, info));
                    return true;
                case null:
                    return false;
                default:
                    log.Debug($"Rating message 0x{packet.Family:X}/0x{packet.Sub:X} from the zone: not for the rating broker.");
                    return true;
            }
        }

        /// <summary>The zone's facts about the site arrived (worker thread): decide, store, answer, tell the zone.</summary>
        private static void Complete(Pending request, ClaimInfoReply info)
        {
            VoteStore votes = Store;
            if (votes == null)
                return;
            bool own = info.Owner == request.Character;
            if (request.CanVote)
            {
                bool may = !own && info.Listed && !votes.Has(request.Character, request.ClaimId);
                SendCanVoteReply(request, may);
                return;
            }
            bool counted = !own && info.Listed && votes.Add(request.Character, request.ClaimId);
            ulong total = votes.Total(request.ClaimId);
            SendClient(request.Connection, VoteReplySub, w =>
            {
                w.U64(request.Character);
                ClientString(w, request.Key);
                w.Bool(counted);
                w.U64(total);
            });
            if (counted)
                Zone(new RatingVoteCounted { Voter = request.Character, ClaimId = request.ClaimId, Owner = info.Owner });
            else
                Zone(new RatingVoteRefused
                {
                    Voter = request.Character, ClaimId = request.ClaimId,
                    Reason = own ? RatingMessages.RefusedOwnSite : !info.Listed ? RatingMessages.RefusedNotListed : RatingMessages.RefusedAlreadyRated
                });
            log.Info($"Character {request.Character} rated claim {request.ClaimId} ({(counted ? "counted" : own ? "own site" : !info.Listed ? "not in the gallery" : "already rated")}; total {total}).");
        }

        private static void SendCanVoteReply(Pending request, bool may)
        {
            SendClient(request.Connection, CanVoteReplySub, w =>
            {
                w.U64(request.ClaimId);
                ClientString(w, request.Key);
                w.Bool(may);
                w.U64(0);
            });
        }

        /// <summary>The zone link is gone: requests waiting for its answer are dropped (the client's like does nothing, spec §7).</summary>
        public static void ZoneLost()
        {
            lock (sync)
                pending.Clear();
        }

        private static bool Zone(ISocialMessage message) => toZone?.Invoke(message.ToPacket()) ?? false;

        /// <summary>One rating packet to a client: RatingBase 0xA700 (bytes A7 00), u8 sub, body.</summary>
        private static void SendClient(ulong connection, byte sub, Action<InternalWriter> body)
        {
            var w = new InternalWriter();
            w.U8(0xA7).U8(0x00).U8(sub);
            body(w);
            toGateway?.Invoke(new TunnelToExternal
            {
                Destination = new ExternalDestination(connection, GatewayChannel.Zone, ExternalDestination.Reliable),
                Packet = w.ToArray()
            });
        }

        /// <summary>A client string: u32 byte count, UTF-8 (GamePacketWriter.Write(string)).</summary>
        private static void ClientString(InternalWriter w, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            w.U32((uint)bytes.Length).Bytes(bytes);
        }

        public static void Stop() => work.CompleteAdding();
    }
}
