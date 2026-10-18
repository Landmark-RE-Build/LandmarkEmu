using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Shared.Game;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LandmarkEmulator.Gateway.Network
{
    /// <summary>
    /// The gateway side of a client connection (trace/architecture/gateway-service.md §3.1 "moves to the gateway"):
    /// on top of the SOE session and the gateway login (<see cref="GatewaySession"/>) it does what the gateway does to
    /// every client packet before the zone sees it — the per-signature drop (Abilities DelayedTargetInfo) and the
    /// transport flood limits — and hands on what the zone sends back. Packets pass unchanged both ways: the server
    /// speaks the 0.695 client's opcodes and sub numbers natively (docs/protocol.md §1.6; until 2026-10-17 a header
    /// translation, ClientWire695, sat here). Where the zone is is the subclass's business: the world server's
    /// in-process ClientSession, or the gateway process's link to a zone back end.
    /// </summary>
    public abstract class ZoneGatewaySession : GatewaySession
    {
        /// <summary>Connection ids: with the start time in the high bits, not reused by a restarted process.</summary>
        private static long nextConnection = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        /// <summary>The gateway's id for the connection (internal-transport.md connectionId).</summary>
        public ulong ConnectionId { get; } = (ulong)Interlocked.Increment(ref nextConnection);

        /// <summary>
        /// Client packets waiting for their back end: after the login reply but before the zone had the character, and
        /// (separate gateway) while a channel's back end is away. Guarded by itself: packets arrive on the receive thread,
        /// the queue is released on the tick.
        /// </summary>
        private readonly Queue<(Channel Channel, byte[] Data)> early = new();
        /// <summary>Packets of each channel in <see cref="early"/>: a new one goes behind them, never past them.</summary>
        private readonly int[] earlyPerChannel = new int[4];
        private const int EarlyLimit = 256;

        /// <summary>
        /// Zone packets (not movement) a connection may send: bursts of 300, 100 a second sustained — far above a normal
        /// client (building, a few dozen a second at most). Packets beyond it are dropped unread.
        /// </summary>
        private readonly RateLimiter packetLimiter = new(300, 100);
        /// <summary>Terrain cell requests and build edits (see the flood check in <see cref="OnTunnelData"/>).</summary>
        private readonly RateLimiter terrainLimiter = new(200, 50);
        private readonly RateLimiter packetWarnLimiter = new(1, 1 / 60.0);
        private long droppedPackets;

        protected ZoneGatewaySession(string cryptoKey) : base(cryptoKey)
        {
        }

        /// <summary>A name for log lines (the character once known).</summary>
        protected virtual string DisplayName => "A session";

        /// <summary>True once the zone has the character: packets go straight to it, not into the early queue.</summary>
        protected abstract bool ZoneReady { get; }

        /// <summary>
        /// True when a packet on <paramref name="channel"/> can go to its back end now; otherwise it waits in the early
        /// queue (not movement). By default every channel waits for the zone (<see cref="ZoneReady"/>); the separate
        /// gateway decides per channel (its routing table, gateway-service.md §3.3 / §3.5).
        /// </summary>
        protected virtual bool ChannelReady(Channel channel) => ZoneReady;

        /// <summary>A client packet for the zone: movement as sent, anything else as sent and within the limits.</summary>
        protected abstract void ToZone(Channel channel, byte[] data);

        /// <summary>
        /// The packets held meanwhile go to their back ends, in order; those whose channel is still not ready
        /// (<see cref="ChannelReady"/>) stay queued, in order.
        /// </summary>
        protected void ReleaseEarlyPackets()
        {
            lock (early)
            {
                if (early.Count == 0)
                    return;
                // Oldest first; once a channel's packet has to wait, the later ones of that channel wait too (order).
                Span<bool> waiting = stackalloc bool[4];
                for (int n = early.Count; n > 0 && early.TryDequeue(out var packet); n--)
                {
                    int c = (int)packet.Channel & 3;
                    if (!waiting[c] && ChannelReady(packet.Channel))
                    {
                        earlyPerChannel[c]--;
                        ToZone(packet.Channel, packet.Data);
                    }
                    else
                    {
                        waiting[c] = true;
                        early.Enqueue(packet);
                    }
                }
            }
        }

        /// <summary>Packets waiting for a back end (the separate gateway releases them on its tick).</summary>
        protected bool HasHeldPackets
        {
            get
            {
                lock (early)
                    return early.Count > 0;
            }
        }

        /// <summary>
        /// Keeps a client packet until its channel's back end can take it (dropped when the queue is full; movement is
        /// never kept: it would replay stale positions, and the client sends its position again as soon as it moves).
        /// </summary>
        protected void HoldPacket(Channel channel, byte[] data)
        {
            lock (early)
                HoldLocked(channel, data);
        }

        private void HoldLocked(Channel channel, byte[] data)
        {
            if (channel != Channel.PositionUpdate && early.Count < EarlyLimit)
            {
                early.Enqueue((channel, data));
                earlyPerChannel[(int)channel & 3]++;
            }
        }

        public override void OnTunnelData(Channel channel, byte[] data)
        {
            if (channel != Channel.PositionUpdate)
            {
                // Abilities 0x27 sub 0x17 DelayedTargetInfo: while a power attack charges, the client sends its aim every
                // frame (ClientAbilityManager vtable slot 2 → FUN_1433ad3e0; seen 617 in two seconds of charging 733). The
                // server takes the aim from the cast, so these are dropped before the flood limiter, which they would
                // otherwise exhaust, dropping the requests that matter (the charge's release among them).
                if (data.Length >= 3 && data[0] == 0x27 && data[1] == 0x00 && data[2] == 0x17)
                    return;

                // WorldEditingBase 0x10C, the build cursor (up to ~500 a second while building, 2026-10-13), is exempt:
                // through the limiter it used up the whole budget and terrain cell requests were dropped (the build tool
                // hung). Terrain cell requests (0xEC) and build edits (0xF4) have their own allowance, so a flood of
                // anything else can't drop them.
                bool cursor = data.Length >= 2 && data[0] == 0x0C && data[1] == 0x01;
                bool terrain = data.Length >= 2 && data[1] == 0x00 && (data[0] == 0xEC || data[0] == 0xF4);
                if (!cursor && !(terrain ? terrainLimiter.TryTake() : packetLimiter.TryTake()))
                {
                    long dropped = Interlocked.Increment(ref droppedPackets);
                    if (packetWarnLimiter.TryTake())
                        log.Warn($"{DisplayName} is sending packets too fast; {dropped} dropped so far.");
                    return;
                }
            }

            // Movement goes straight on whenever its channel is ready (never kept). Anything else goes straight on only when
            // nothing of its channel is still waiting, so a channel's packets reach the back end in the client's order.
            lock (early)
            {
                if (ChannelReady(channel) && (channel == Channel.PositionUpdate || earlyPerChannel[(int)channel & 3] == 0))
                    ToZone(channel, data);
                else
                    HoldLocked(channel, data);
            }
        }

        /// <summary>
        /// ChannelIsRoutable (gateway opcode 7, the channel in the first byte): whether the back end behind
        /// <paramref name="channel"/> is there (gateway-service.md §3.5). The client only logs it (NetInfo.log
        /// "RECEIVED=OnChannelIsRoutable channel=%d isRoutable=%d"). The second bool stays false (meaning: L70).
        /// </summary>
        protected void SendRoutable(Channel channel, bool routable)
            => EnqueueGatewayMessage(new Message.Model.ChannelIsRoutable { IsRoutable = routable }, channel);

        /// <summary>
        /// ConnectionIsNotRoutable (gateway opcode 8, no body): no back end can take this connection at all
        /// (gateway-service.md §3.5 item 5). The client only logs it (NetInfo.log "RECEIVED=OnConnectionIsNotRoutable").
        /// </summary>
        protected void SendNotRoutable() => EnqueueGatewayMessage(new Message.Model.ConnectionIsNotRoutable());

        /// <summary>A zone packet for the client (opcode bytes + body, already the client's numbering), sent as given.</summary>
        protected void SendToClient(byte[] packet, Channel channel = Channel.Zone) => PackTunnelPacket(packet, channel);
    }
}
