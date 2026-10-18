using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Internal.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The zone's end of its link with the world's world-channel service (trace/architecture/world-channel-service.md
    /// §6.2, §6.4): the service dials the zone's internal port (ZONE_INTERNAL_PORT, <see cref="GatewayLink.ListenForServices"/>)
    /// and registers as kind WorldChannel. Besides the channel-1 packets it hands on (TunnelPacketFromExternalConnection,
    /// served like the gateway's), it sends the trade broker's ServicePackets (<see cref="TradeMessages"/>), which this
    /// class turns into calls on <see cref="Trades"/> and answers, and the encounter broker's (<see cref="EncounterMessages"/>,
    /// spec §6.5, build step 7), which it turns into calls on <see cref="Encounters"/>; the zone's own encounter
    /// decisions go back through <see cref="Send"/> / <see cref="SendTo"/>. The rating broker's (<see cref="RatingMessages"/>,
    /// §6.5, build step 6) ask about a build site (ClaimInfoQuery, answered from <see cref="Ratings.ClaimInfo"/>) and report
    /// a like it counted or refused (<see cref="Ratings.BrokerCounted"/>, <see cref="Ratings.BrokerRefused"/>).
    /// <para>
    /// Threading: <see cref="Attach"/> runs on the link's thread (it only records the link); <see cref="Received"/> and
    /// <see cref="Detach"/> are queued by GatewayLink and run on the world thread like every other zone packet, in the
    /// order they arrived — so a TradeZoneSessionOpen is always applied before any channel-1 packet the service sent
    /// after it.
    /// </para>
    /// <para>
    /// Each registration gets a new <b>broker generation</b> (kept in the link's Tag). A trade the service started is
    /// marked with it, so when that link goes, exactly its trades are canceled (spec §7: "on WC loss the zone ends open
    /// trades with code 1 TradeCanceled"), even if a restarted service has registered meanwhile. Generation 0 = a trade
    /// the zone brokered itself (in-process mode: no world-channel service, or the gateway sent channel 1 to the zone).
    /// </para>
    /// </summary>
    public static class WorldChannelLink
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly object sync = new();
        private static InternalLink current;
        private static int lastGeneration;

        /// <summary>A world-channel service registered on the zone's internal port (link thread).</summary>
        public static void Attach(InternalLink link)
        {
            int generation = Interlocked.Increment(ref lastGeneration);
            link.Tag = generation;
            lock (sync)
                current = link;
            log.Info($"World-channel service linked (trade broker generation {generation}).");
        }

        /// <summary>A service link closed (world thread): if it was a world channel, its brokered trades and encounters end.</summary>
        public static void Detach(InternalLink link)
        {
            if (link.Tag is not int generation)
                return;
            lock (sync)
                if (current == link)
                    current = null;
            Trades.BrokerLost(generation);
            Encounters.BrokerLost(generation);
        }

        /// <summary>A world-channel service is linked now (the encounter lifecycle runs there; spec §7 otherwise).</summary>
        public static bool IsLinked
        {
            get
            {
                lock (sync)
                    return current != null;
            }
        }

        /// <summary>A message to the linked world-channel service (false when there is none or its link closed).</summary>
        public static bool Send(ISocialMessage message)
        {
            InternalLink link;
            lock (sync)
                link = current;
            return link != null && link.Send(message.ToPacket());
        }

        /// <summary>A message to the world-channel service of exactly <paramref name="generation"/> (the one that holds the state in question).</summary>
        public static bool SendTo(int generation, ISocialMessage message)
        {
            InternalLink link;
            lock (sync)
                link = current;
            return link != null && link.Tag is int g && g == generation && link.Send(message.ToPacket());
        }

        /// <summary>
        /// A ServicePacket from a service link (world thread): true when it was a trade broker message (§6.4), an
        /// encounter broker message or a rating broker message (§6.5).
        /// </summary>
        public static bool Received(InternalLink link, ServicePacket packet)
        {
            ISocialMessage message = TradeMessages.Parse(packet) ?? EncounterMessages.Parse(packet) ?? RatingMessages.Parse(packet);
            if (message == null)
                return false;
            int generation = link.Tag is int g ? g : 0;
            if (generation == 0)
            {
                log.Warn($"World-channel message 0x{packet.Family:X}/0x{packet.Sub:X} from a service that did not register as the world channel: ignored.");
                return true;
            }
            switch (message)
            {
                case EncounterParticipants participants:
                    Encounters.BrokerParticipants(generation, participants.EncounterId, participants.Kind, participants.State, participants.Characters);
                    break;
                case EncounterNotice notice:
                    Encounters.Notice(notice.Character, notice.Text);
                    break;
                case ClaimInfoQuery query:
                    Reply(link, Ratings.ClaimInfo(query.RequestId, query.ClaimId));
                    break;
                case RatingVoteCounted counted:
                    Ratings.BrokerCounted(counted.Voter, counted.ClaimId, counted.Owner);
                    break;
                case RatingVoteRefused refused:
                    Ratings.BrokerRefused(refused.Voter, refused.ClaimId, refused.Reason);
                    break;
                case TradeZoneSessionStart start:
                    (WorldSession a, WorldSession b) = Trades.BrokerStart(generation, (int)start.TradeId, start.CharacterA, start.CharacterB);
                    if (a == null)
                        Reply(link, new TradeZoneSessionEnd { TradeId = start.TradeId, Code = TradeMessages.EndSilently });
                    else
                        Reply(link, new TradeZoneSessionReady
                        {
                            TradeId = start.TradeId,
                            ConnectionA = a.Link.ConnectionId, CharacterA = a.PlayerGuid, NameA = a.CharacterName ?? "",
                            ConnectionB = b.Link.ConnectionId, CharacterB = b.PlayerGuid, NameB = b.CharacterName ?? ""
                        });
                    break;
                case TradeZoneSessionOpen open:
                    Trades.BrokerOpen(generation, (int)open.TradeId);
                    break;
                case TradeZoneSessionEnd end:
                    Trades.BrokerEnd(generation, (int)end.TradeId, end.Code);
                    break;
                default:
                    log.Debug($"World-channel message 0x{packet.Family:X}/0x{packet.Sub:X} is not for the zone.");
                    break;
            }
            return true;
        }

        /// <summary>
        /// The zone ended a trade the world channel of <paramref name="generation"/> brokered: TradeZoneSessionEnd so it
        /// frees the pair (spec §6.4 step 5). Nothing to send when that service is gone (its table went with it).
        /// </summary>
        public static void TradeEnded(int generation, int tradeId, byte code)
        {
            InternalLink link;
            lock (sync)
                link = current;
            if (link != null && link.Tag is int g && g == generation)
                Reply(link, new TradeZoneSessionEnd { TradeId = (uint)tradeId, Code = code });
        }

        private static void Reply(InternalLink link, ISocialMessage message)
        {
            if (!link.Send(message.ToPacket()))
                log.Debug($"World-channel link closed; message 0x{message.Family:X}/0x{message.Sub:X} not sent.");
        }
    }
}
