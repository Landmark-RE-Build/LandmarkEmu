using LandmarkEmulator.Internal.Social;

namespace LandmarkEmulator.Internal.WorldChannel
{
    /// <summary>
    /// The trade broker's messages between a world's world-channel service and its zone
    /// (trace/architecture/world-channel-service.md §6.4, build step 5; transport: internal-transport.md §3).
    /// <para>
    /// Why the split: the client itself sends the trade invitation on the World channel — TradeInitiatePacket 0x3E/1
    /// (FUN_142cb1dc0 / 0.695 FUN_142d4b0c0), TradeInviteReplyPacket 0x3E/3 (FUN_14358fda0, FUN_14358ff50) and
    /// TradeCancelPacket 0x3E/7 (FUN_14358fef0) — but the exchange on the Zone channel: TradeAcceptPacket 0x3E/6,
    /// TradeOfferItemPacket 0x3E/8, TradeCoinCountPacket 0x3E/9 (spec §3.3). So on live a World back end brokered the
    /// invitation while the zone, which holds the inventories, ran the exchange. The client reads every S→C trade sub
    /// in one handler (FUN_14358ee50) whatever channel it came on, so either side may send its part.
    /// </para>
    /// <para>
    /// Every message here is <b>ours</b> (live's internal layouts are not known, L48): the client family TradeBase 0x3E
    /// and a sub from 0x10000 up (spec §6.2), so they can never be taken for a client sub (u8). They travel as a
    /// <see cref="ServicePacket"/> on the world channel's link to the zone (ZONE_INTERNAL_PORT). Ids 0x10030-0x10032 are
    /// the spec's; <see cref="TradeZoneSessionReady"/> 0x10033 is an addition (see its comment).
    /// </para>
    /// </summary>
    public static class TradeMessages
    {
        /// <summary>TradeBase 0x3E (the client's family; its subs are u8).</summary>
        public const ushort TradeFamily = 0x3E;

        public const uint TradeZoneSessionStartSub = 0x10030, TradeZoneSessionEndSub = 0x10031, TradeZoneSessionOpenSub = 0x10032,
            TradeZoneSessionReadySub = 0x10033;

        /// <summary>
        /// Trade ids the world channel hands out start here, so they never meet the ids the zone gives the trades it
        /// brokers itself (in-process mode, or while the world channel is away; those count up from 1). The client keeps
        /// the id as a u32 (TradeInvite / StartSession / every exchange sub).
        /// </summary>
        public const uint FirstBrokerTradeId = 0x4000_0001;

        /// <summary>
        /// <see cref="TradeZoneSessionEnd.Code"/> 0 (ours, not a client TradeMessage code — those start at 1): end the
        /// trade without telling the players (an invitation that was declined — the inviter got TradeRejected 0x3E/0xF —
        /// or one the zone refused before any invitation went out).
        /// </summary>
        public const byte EndSilently = 0;

        /// <summary>The trade message in a service packet, or null when it isn't one of these.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            if (packet.Family != TradeFamily)
                return null;
            var r = new InternalReader(packet.Body);
            return packet.Sub switch
            {
                TradeZoneSessionStartSub => TradeZoneSessionStart.Read(r),
                TradeZoneSessionEndSub => TradeZoneSessionEnd.Read(r),
                TradeZoneSessionOpenSub => TradeZoneSessionOpen.Read(r),
                TradeZoneSessionReadySub => TradeZoneSessionReady.Read(r),
                _ => null
            };
        }
    }

    /// <summary>
    /// TradeZoneSessionStart (world channel → zone, 0x3E/0x10030; spec §6.4 step 1): u32 trade, u64 connection A, u64
    /// character A, u64 connection B, u64 character B. A (the inviter) sent TradeInitiate on the World channel naming B;
    /// the zone checks what only it knows today — B online on the same landscape, B not ignoring A, neither already in a
    /// trade (the same checks as the in-process Trades.Initiate) — and answers <see cref="TradeZoneSessionReady"/> or
    /// <see cref="TradeZoneSessionEnd"/> (code 0). Connection B is 0 while the world channel has no presence table of
    /// its own (the gateway does not send it connection events yet); the zone fills it in the reply.
    /// </summary>
    public sealed class TradeZoneSessionStart : ISocialMessage
    {
        public ushort Family => TradeMessages.TradeFamily;
        public uint Sub => TradeMessages.TradeZoneSessionStartSub;
        public uint TradeId { get; init; }
        public ulong ConnectionA { get; init; }
        public ulong CharacterA { get; init; }
        public ulong ConnectionB { get; init; }
        public ulong CharacterB { get; init; }

        public void Write(InternalWriter w) => w.U32(TradeId).U64(ConnectionA).U64(CharacterA).U64(ConnectionB).U64(CharacterB);
        public static TradeZoneSessionStart Read(InternalReader r) => new()
        {
            TradeId = r.U32(), ConnectionA = r.U64(), CharacterA = r.U64(), ConnectionB = r.U64(), CharacterB = r.U64()
        };
    }

    /// <summary>
    /// TradeZoneSessionEnd (both ways, 0x3E/0x10031; spec §6.4 steps 2, 4, 5): u32 trade, u8 code (a client
    /// TradeMessage code, FUN_14358ee50 case 0xE: 1 TradeCanceled, 2 PartnerDisconnected, 3 / 4 offer invalid,
    /// 6 TradeCompleted, 8 / 9 instanced zone, 10 inventory full; or <see cref="TradeMessages.EndSilently"/>).
    /// <list type="bullet">
    /// <item>World channel → zone: the invitation was declined (code 0: the zone forgets the trade quietly) or a player
    /// sent TradeCancel (code 1: the zone sends TradeMessage 1 and EndSession 0x3E/5 to both, as the in-process
    /// Cancel did).</item>
    /// <item>Zone → world channel: the zone ended the trade (completed, failed, a player left the landscape / the world /
    /// entered an instanced game, or refused it at start with code 0); the world channel frees the pair.</item>
    /// </list>
    /// </summary>
    public sealed class TradeZoneSessionEnd : ISocialMessage
    {
        public ushort Family => TradeMessages.TradeFamily;
        public uint Sub => TradeMessages.TradeZoneSessionEndSub;
        public uint TradeId { get; init; }
        public byte Code { get; init; }

        public void Write(InternalWriter w) => w.U32(TradeId).U8(Code);
        public static TradeZoneSessionEnd Read(InternalReader r) => new() { TradeId = r.U32(), Code = r.U8() };
    }

    /// <summary>
    /// TradeZoneSessionOpen (world channel → zone, 0x3E/0x10032; spec §6.4 step 2): u32 trade. B accepted the
    /// invitation; the zone opens the exchange. The world channel sends it <b>before</b> StartSession 0x3E/4 goes to
    /// the two clients, so an OfferItem / CoinCount / Accept on the Zone channel never reaches a zone that does not
    /// know the trade is open (spec §9 "Split requests and ordering").
    /// </summary>
    public sealed class TradeZoneSessionOpen : ISocialMessage
    {
        public ushort Family => TradeMessages.TradeFamily;
        public uint Sub => TradeMessages.TradeZoneSessionOpenSub;
        public uint TradeId { get; init; }

        public void Write(InternalWriter w) => w.U32(TradeId);
        public static TradeZoneSessionOpen Read(InternalReader r) => new() { TradeId = r.U32() };
    }

    /// <summary>
    /// TradeZoneSessionReady (ours, zone → world channel, 0x3E/0x10033; not in the spec): u32 trade, u64 connection A,
    /// u64 character A, string name A, u64 connection B, u64 character B, string name B. The zone accepted a
    /// <see cref="TradeZoneSessionStart"/> and holds the trade; the world channel now sends TradeInvite 0x3E/2 to B.
    /// <para>
    /// Added because the spec's step 1 has the world channel check presence (gateway connection events) and the
    /// ignore list (#12 IgnoreCheck) itself, and neither reaches it yet: the gateway sends connection events only to the
    /// zone, and #12 has no ignore query (the zone keeps the ignore lists social pushes it, IgnoreListSync). So the
    /// zone checks, and its answer carries what the invitation and StartSession need — the connections and the names
    /// for the client's character identity (FUN_142b8c3c0). When the world channel gets its own presence and ignore
    /// data this reply can shrink to an acknowledgement.
    /// </para>
    /// </summary>
    public sealed class TradeZoneSessionReady : ISocialMessage
    {
        public ushort Family => TradeMessages.TradeFamily;
        public uint Sub => TradeMessages.TradeZoneSessionReadySub;
        public uint TradeId { get; init; }
        public ulong ConnectionA { get; init; }
        public ulong CharacterA { get; init; }
        public string NameA { get; init; } = "";
        public ulong ConnectionB { get; init; }
        public ulong CharacterB { get; init; }
        public string NameB { get; init; } = "";

        public void Write(InternalWriter w) => w.U32(TradeId).U64(ConnectionA).U64(CharacterA).Str(NameA).U64(ConnectionB).U64(CharacterB).Str(NameB);
        public static TradeZoneSessionReady Read(InternalReader r) => new()
        {
            TradeId = r.U32(), ConnectionA = r.U64(), CharacterA = r.U64(), NameA = r.Str(), ConnectionB = r.U64(), CharacterB = r.U64(), NameB = r.Str()
        };
    }
}
