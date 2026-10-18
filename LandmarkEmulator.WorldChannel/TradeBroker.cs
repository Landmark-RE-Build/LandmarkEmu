using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Internal.WorldChannel;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// The trade broker (trace/architecture/world-channel-service.md §6.4; build step 5, §8). The client sends the
    /// invitation part of a trade on the World channel — TradeInitiatePacket 0x3E/1 {u64 target} (Lua
    /// InitiateTradeByGuid, FUN_142cb1dc0 / 0.695 FUN_142d4b0c0), TradeInviteReplyPacket 0x3E/3 {u32 trade, bool}
    /// (FUN_14358fda0 decline, FUN_14358ff50) and TradeCancelPacket 0x3E/7 {u32 trade} (FUN_14358fef0) — and the
    /// exchange (Accept 6, OfferItem 8, CoinCount 9) on the Zone channel (spec §3.3). This class answers the first
    /// three here and leaves the exchange to the zone (Trades.cs there), which holds the inventories.
    /// <para>The flow (messages in <see cref="TradeMessages"/>, all on the link to the zone):</para>
    /// <list type="number">
    /// <item>Initiate from A → not trading with yourself, neither side already in a trade here → a new trade id
    /// (from <see cref="TradeMessages.FirstBrokerTradeId"/>) → <b>TradeZoneSessionStart</b>. The zone runs the checks
    /// whose data only it has today (B online on A's landscape, B not ignoring A, neither in a zone trade) and answers
    /// <b>TradeZoneSessionReady</b> (with the two connections and names) or <b>TradeZoneSessionEnd</b> code 0. On Ready
    /// this sends TradeInvite 0x3E/2 {identity of A, u32 trade} to B. (The spec has the world channel check presence
    /// and the ignore list itself, §6.4 step 1; it gets neither yet — see TradeZoneSessionReady.)</item>
    /// <item>InviteReply from B: decline → TradeRejected 0x3E/0xF {identity of B} to A and TradeZoneSessionEnd code 0;
    /// accept → <b>TradeZoneSessionOpen</b> first, then StartSession 0x3E/4 {u64 partner, identity of partner, u32
    /// trade} to both, so the zone knows the trade is open before any Zone-channel offer can reach it (§9).</item>
    /// <item>Cancel from either (at any point) → TradeZoneSessionEnd code 1 TradeCanceled; the zone tells both
    /// (TradeMessage 1, EndSession 0x3E/5).</item>
    /// <item>The zone ends a trade (completed 6, partner left 2, landscape change 1, instanced zone 8 / 9, offer
    /// invalid 3 / 4, bag full 10) → TradeZoneSessionEnd from the zone frees the pair here.</item>
    /// </list>
    /// <para>
    /// Trade ids this broker did not hand out (a trade the zone started itself while this service was away, when the
    /// gateway routes channel 1 to the zone) are passed on to the zone unchanged, so such a trade can still be
    /// answered or canceled. When the zone link drops every brokered trade is forgotten here; the zone ends them on
    /// its side with TradeCanceled (spec §7: "the zone keeps a TradeZoneSession only while WC confirms it").
    /// Trades are memory only (spec §6.1; live lost them on a restart too).
    /// </para>
    /// <para>
    /// The packets this sends to clients are written in the 0.695 client's numbering, sent as given (docs/protocol.md §1.6) (TradeBase 0x3E00 → bytes 3E 00, u8 sub;
    /// the gateway passes every back end's packets unchanged, internal-transport.md §2). They
    /// go on the Zone channel destination as the zone's own trade packets did (the client reads them in one handler).
    /// </para>
    /// </summary>
    public static class TradeBroker
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte InitiateSub = 1, InviteSub = 2, InviteReplySub = 3, StartSessionSub = 4, CancelSub = 7, RejectedSub = 15;
        /// <summary>TradeMessage code 1 TradeCanceled (FUN_14358ee50 case 0xE).</summary>
        private const byte Canceled = 1;

        private enum State
        {
            /// <summary>TradeZoneSessionStart sent; waiting for the zone's Ready / End.</summary>
            Starting,
            /// <summary>TradeInvite sent to B; waiting for B's InviteReply.</summary>
            Invited,
            /// <summary>Both in the trade window; the zone runs the exchange.</summary>
            Open
        }

        private sealed class Trade
        {
            public uint Id;
            public State State;
            public ulong ConnectionA, CharacterA, ConnectionB, CharacterB;
            public string NameA = "", NameB = "";
            public bool Has(ulong character) => CharacterA == character || CharacterB == character;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Trade> trades = new();
        private static uint nextId = TradeMessages.FirstBrokerTradeId - 1;

        /// <summary>WorldChannel.json TradeBroker (default true): false hands the trade packets on to the zone as in step 2.</summary>
        public static bool Enabled { get; set; } = true;

        private static Func<IInternalMessage, bool> toGateway, toZone;

        /// <summary>The two links' send functions (<see cref="ChannelRelay"/>).</summary>
        public static void Start(Func<IInternalMessage, bool> gateway, Func<IInternalMessage, bool> zone)
        {
            toGateway = gateway;
            toZone = zone;
        }

        /// <summary>
        /// A channel-1 packet from the gateway: true when it was a trade request this broker took (Initiate, or
        /// InviteReply / Cancel of a trade it handed out); false hands it on to the zone.
        /// </summary>
        public static bool TryTake(TunnelFromExternal packet)
        {
            byte[] p = packet.Packet;
            if (!Enabled || p.Length < 3 || p[0] != 0x3E || p[1] != 0x00)
                return false;
            try
            {
                var r = new InternalReader(p, 3);
                switch (p[2])
                {
                    case InitiateSub:
                        return Initiate(packet, r.U64());
                    case InviteReplySub:
                    {
                        uint id = r.U32();
                        return Reply(packet, id, r.Bool());
                    }
                    case CancelSub:
                        return Cancel(packet, r.U32());
                    default:
                        // The exchange subs come on Zone; if one arrives here it is the zone's.
                        return false;
                }
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable trade packet 0x3E/{p[2]} from connection {packet.ConnectionId:X}: {e.Message}");
                return true;
            }
        }

        /// <summary>TradeInitiate (spec §6.4 step 1).</summary>
        private static bool Initiate(TunnelFromExternal packet, ulong target)
        {
            ulong me = packet.CharacterGuid;
            Trade trade;
            lock (sync)
            {
                // "TradeYourself" is refused by the client already; the rest as the in-process Trades.Initiate.
                if (target == 0 || target == me || trades.Values.Any(t => t.Has(me) || t.Has(target)))
                {
                    log.Info($"Character {me} can't trade with 0x{target:X} now (busy).");
                    return true;
                }
                trade = new Trade
                {
                    Id = NextId(), State = State.Starting, ConnectionA = packet.ConnectionId, CharacterA = me, CharacterB = target
                };
                trades[trade.Id] = trade;
            }
            if (!Zone(new TradeZoneSessionStart
            {
                TradeId = trade.Id, ConnectionA = trade.ConnectionA, CharacterA = me, ConnectionB = 0, CharacterB = target
            }))
            {
                lock (sync)
                    trades.Remove(trade.Id);
                log.Warn($"Trade invitation from {me}: the zone is unreachable.");
            }
            return true;
        }

        /// <summary>TradeInviteReply (spec §6.4 step 2). Not one of ours → false (the zone's).</summary>
        private static bool Reply(TunnelFromExternal packet, uint id, bool accept)
        {
            Trade trade;
            lock (sync)
            {
                if (!trades.TryGetValue(id, out trade))
                    return false;
                // Only the invited player, and only once (the in-process Trades.Reply ignores the rest the same way).
                if (trade.State != State.Invited || trade.CharacterB != packet.CharacterGuid)
                    return true;
                if (accept)
                    trade.State = State.Open;
                else
                    trades.Remove(id);
                // B's connection as the gateway knows it now (the same unless B relogged, which ends the trade anyway).
                trade.ConnectionB = packet.ConnectionId;
            }
            if (!accept)
            {
                Client(trade.ConnectionA, RejectedSub, w => Identity(w, trade.NameB));
                Zone(new TradeZoneSessionEnd { TradeId = id, Code = TradeMessages.EndSilently });
                log.Info($"{trade.NameB} declined trade {id} with {trade.NameA}.");
                return true;
            }
            // The zone first: StartSession lets the clients send Zone-channel offers at once.
            if (!Zone(new TradeZoneSessionOpen { TradeId = id }))
            {
                lock (sync)
                    trades.Remove(id);
                log.Warn($"Trade {id}: the zone is unreachable; not opened.");
                return true;
            }
            Client(trade.ConnectionA, StartSessionSub, w => { w.U64(trade.CharacterB); Identity(w, trade.NameB); w.U32(id); });
            Client(trade.ConnectionB, StartSessionSub, w => { w.U64(trade.CharacterA); Identity(w, trade.NameA); w.U32(id); });
            log.Info($"Trade {id} started: {trade.NameA} ↔ {trade.NameB}.");
            return true;
        }

        /// <summary>TradeCancel (spec §6.4 step 4): the zone tells both. Not one of ours → false (the zone's).</summary>
        private static bool Cancel(TunnelFromExternal packet, uint id)
        {
            lock (sync)
            {
                if (!trades.TryGetValue(id, out Trade trade))
                    return false;
                if (!trade.Has(packet.CharacterGuid))
                    return true;
                trades.Remove(id);
            }
            Zone(new TradeZoneSessionEnd { TradeId = id, Code = Canceled });
            log.Info($"Trade {id} canceled by character {packet.CharacterGuid}.");
            return true;
        }

        /// <summary>A trade message from the zone (Ready / End). True when it was one.</summary>
        public static bool FromZone(ServicePacket packet)
        {
            switch (TradeMessages.Parse(packet))
            {
                case TradeZoneSessionReady ready:
                    Ready(ready);
                    return true;
                case TradeZoneSessionEnd end:
                    lock (sync)
                        trades.Remove(end.TradeId);
                    log.Debug($"Trade {end.TradeId} ended by the zone (code {end.Code}).");
                    return true;
                case null:
                    return false;
                default:
                    log.Debug($"Trade message 0x{packet.Sub:X} from the zone: not for the world channel.");
                    return true;
            }
        }

        /// <summary>The zone holds the trade: invite B (spec §6.4 step 1, "then TradeInvite 0x3E/2 to B").</summary>
        private static void Ready(TradeZoneSessionReady ready)
        {
            Trade trade;
            lock (sync)
            {
                if (!trades.TryGetValue(ready.TradeId, out trade) || trade.State != State.Starting)
                {
                    // Canceled meanwhile (the zone already got that End, after its Ready) — nothing to do.
                    return;
                }
                trade.State = State.Invited;
                trade.ConnectionA = ready.ConnectionA;
                trade.ConnectionB = ready.ConnectionB;
                trade.NameA = ready.NameA;
                trade.NameB = ready.NameB;
            }
            Client(trade.ConnectionB, InviteSub, w => { Identity(w, trade.NameA); w.U32(trade.Id); });
            log.Info($"{trade.NameA} invited {trade.NameB} to trade ({trade.Id}).");
        }

        /// <summary>The zone link is gone: the zone ends its brokered trades itself (TradeCanceled); forget them here.</summary>
        public static void ZoneLost()
        {
            int n;
            lock (sync)
            {
                n = trades.Count;
                trades.Clear();
            }
            if (n > 0)
                log.Warn($"Zone link lost: {n} brokered trade(s) forgotten (the zone cancels them).");
        }

        private static uint NextId()
        {
            // Never 0, never below the broker's range (the zone's own trades count up from 1) and below 2^31 (the zone
            // keeps trade ids as int).
            if (++nextId < TradeMessages.FirstBrokerTradeId || nextId > int.MaxValue)
                nextId = TradeMessages.FirstBrokerTradeId;
            return nextId;
        }

        private static bool Zone(ISocialMessage message) => toZone?.Invoke(message.ToPacket()) ?? false;

        /// <summary>One trade packet to a client: TradeBase 0x3E00 (bytes 3E 00), u8 sub, body.</summary>
        private static void Client(ulong connection, byte sub, Action<InternalWriter> body)
        {
            var w = new InternalWriter();
            w.U8(0x3E).U8(0x00).U8(sub);
            body(w);
            toGateway?.Invoke(new TunnelToExternal
            {
                Destination = new ExternalDestination(connection, GatewayChannel.Zone, ExternalDestination.Reliable),
                Packet = w.ToArray()
            });
        }

        /// <summary>
        /// The character identity as the client reads it (FUN_142b8c3c0): u32 ×3, first name, last name (strings u32
        /// length + bytes) — the zone's SendSelfToClient.CharacterIdentity with the name as the first name and the
        /// three u32 at 0, as Trades.Identity writes it.
        /// </summary>
        private static void Identity(InternalWriter w, string firstName)
        {
            w.U32(0).U32(0).U32(0);
            ClientString(w, firstName);
            ClientString(w, "");
        }

        /// <summary>A client string: u32 byte count, UTF-8 (GamePacketWriter.Write(string)).</summary>
        private static void ClientString(InternalWriter w, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            w.U32((uint)bytes.Length).Bytes(bytes);
        }

        /// <summary>Brokered trades now (for the log).</summary>
        public static int Count
        {
            get
            {
                lock (sync)
                    return trades.Count;
            }
        }
    }
}
