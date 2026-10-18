using LandmarkEmulator.Internal;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// The world-channel service's two links (world-channel-service.md §6.2): it dials the world's gateway and registers
    /// as the back end of channel 1 (RegisterBackEnd kind WorldChannel), and dials the world's zone process. Every
    /// channel-1 packet the gateway routes here (TunnelPacketFromExternalConnection) goes on to the zone unchanged, which
    /// answers the client through the gateway itself — except what ends here: the sinks (<see cref="Sinks"/>, §8 step 3),
    /// logout to character select (<see cref="Logout"/>, step 4), the trade invitation (<see cref="TradeBroker"/>,
    /// §6.4, step 5), the build-site ratings (<see cref="RatingBroker"/>, §6.5, step 6) and the encounter lifecycle
    /// (<see cref="EncounterBroker"/>, §6.5, step 7). While the zone link is
    /// down the packets are dropped (the client repeats its world-channel requests).
    /// </summary>
    public static class ChannelRelay
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static InternalDialer gateway, zone;
        private static readonly object sync = new();
        private static InternalLink zoneLink, gatewayLink;
        private static long relayed, dropped;
        private static System.Threading.Timer statusTimer;
        private static readonly DateTime started = DateTime.UtcNow;

        private static void SendStatus()
        {
            DateTime now = DateTime.UtcNow;
            bool toGateway;
            lock (sync)
                toGateway = gatewayLink != null;
            ToZone(ServiceStatus.Build("World channel", new List<(string, string)>
            {
                ("Up for", $"{(int)(now - started).TotalHours}h {(now - started).Minutes}m"),
                ("Gateway link", toGateway ? "up" : "down"),
                ("Packets relayed to the zone", Interlocked.Read(ref relayed).ToString()),
                ("Packets dropped (zone away)", Interlocked.Read(ref dropped).ToString()),
                ("Trades open", TradeBroker.Count.ToString())
            }));
        }

        public static void Start(WorldChannelConfiguration config)
        {
            // Its state to the zone every ServiceStatus.Interval, for the world's admin monitor page (TASKS B.7).
            statusTimer = new System.Threading.Timer(_ => SendStatus(), null, ServiceStatus.Interval, ServiceStatus.Interval);
            TradeBroker.Enabled = config.TradeBroker;
            TradeBroker.Start(ToGateway, ToZone);
            EncounterBroker.Enabled = config.EncounterBroker;
            EncounterBroker.Start(ToGateway, ToZone);
            RatingBroker.Enabled = config.ClaimRatings;
            RatingBroker.Start(ToGateway, ToZone);
            zone = new InternalDialer(config.ZoneInternalAddress);
            zone.Connected += link =>
            {
                link.Closed += (l, error) =>
                {
                    lock (sync)
                        if (zoneLink == l)
                            zoneLink = null;
                    // The zone cancels the trades and ends the encounters this service brokered when it loses the link (spec §7).
                    TradeBroker.ZoneLost();
                    EncounterBroker.ZoneLost();
                    RatingBroker.ZoneLost();
                    log.Warn($"Zone link lost{(error != null ? ": " + error.Message : "")}; reconnecting.");
                };
                link.Received += (_, message) => FromZone(message);
                link.Send(Register(config));
                lock (sync)
                    zoneLink = link;
                log.Info("Zone connected.");
            };
            zone.Start();

            gateway = new InternalDialer(config.GatewayInternalAddress);
            gateway.Connected += link =>
            {
                link.Received += (_, message) => FromGateway(message);
                link.Closed += (l, error) =>
                {
                    lock (sync)
                        if (gatewayLink == l)
                            gatewayLink = null;
                    log.Warn($"Gateway link lost{(error != null ? ": " + error.Message : "")}; reconnecting.");
                };
                link.Send(Register(config));
                lock (sync)
                    gatewayLink = link;
                log.Info("Gateway connected; registered as the channel-1 back end.");
            };
            gateway.Start();
            log.Info($"World channel for world {config.WorldId}: gateway {config.GatewayInternalAddress}, zone {config.ZoneInternalAddress}.");
        }

        private static RegisterBackEnd Register(WorldChannelConfiguration config) => new()
        {
            Kind = BackEndKind.WorldChannel, WorldId = config.WorldId, ZoneIds = Array.Empty<uint>(),
            ChannelMask = 1 << (int)GatewayChannel.World, Name = Environment.MachineName, BuildStamp = 0
        };

        private static void FromGateway(IInternalMessage message)
        {
            if (message is not TunnelFromExternal packet)
            {
                log.Debug($"Gateway sent internal opcode {message.Opcode}: not used by the world channel.");
                return;
            }
            FromClient(packet);
        }

        /// <summary>A channel-1 packet of a client: from the gateway, or a rating request the zone made for one (RatingClientRequest).</summary>
        private static void FromClient(TunnelFromExternal packet)
        {
            // Messages that end here (Sinks) are not handed on.
            if (Sinks.TryTake(packet))
                return;
            // Logout to character select (0xF6) is answered here when the login service and the zone's relog tag are known
            // (falling back to the zone when the login service gives nothing). Otherwise the zone answers it; and when the
            // zone link is down too, the login service alone (without the tag), or "not ok" — never silence: the client
            // quits 60 s after sending it without an answer (Logout, agent-leads-sweep.md L61).
            if (packet.Packet.Length >= 2 && packet.Packet[0] == 0xF6 && packet.Packet[1] == 0x00)
            {
                if (Logout.CanHandle)
                {
                    Logout.Request(packet, ToGateway, ToZone, HandOn);
                    return;
                }
                if (HandOn(packet))
                    return;
                if (Logout.Login != null)
                    Logout.Request(packet, ToGateway, ToZone, _ => false);
                else
                {
                    log.Warn($"Logout to character select from connection {packet.ConnectionId:X}: no zone and no login service; \"not ok\".");
                    Logout.Answer(packet.ConnectionId, null, ToGateway);
                }
                return;
            }
            // Trade invitations (0x3E subs 1 / 3 / 7) of the trades brokered here; the exchange subs come on Zone.
            if (TradeBroker.TryTake(packet))
                return;
            // Encounter requests (0x2C subs 0x67 / 0x69 / 0x6C / 0x6D / 0x7C) of the encounters and challenges held here (§6.5).
            if (EncounterBroker.TryTake(packet))
                return;
            // Build-site likes (0xA7 AddVote "Claims") and the can-vote query (0xA7 0x18), from this service's store (§6.5).
            if (RatingBroker.TryTake(packet))
                return;
            if (!HandOn(packet) && Interlocked.Increment(ref dropped) % 100 == 1)
                log.Warn($"Zone unreachable: {dropped} channel-1 packet(s) dropped so far.");
        }

        /// <summary>Hands a client's channel-1 packet on to the zone unchanged; false when the zone link is down.</summary>
        private static bool HandOn(TunnelFromExternal packet)
        {
            bool sent;
            lock (sync)
                sent = zoneLink != null && zoneLink.Send(packet);
            if (sent)
            {
                long n = Interlocked.Increment(ref relayed);
                if (n == 1 || n % 1000 == 0)
                    log.Info($"Channel 1: {n} packet(s) handed on to the zone (latest: {packet.Packet.Length} B from connection {packet.ConnectionId:X}).");
            }
            return sent;
        }

        /// <summary>
        /// From the zone: its relog tag (sent when this service connects), the trade broker's answers (§6.4), the encounter
        /// broker's and the rating broker's (§6.5), and rating requests the zone made for a client (the ratevote dev
        /// command), which are served like that client's channel-1 packets.
        /// </summary>
        private static void FromZone(IInternalMessage message)
        {
            if (message is not ServicePacket packet)
                return;
            if (Internal.Login.LoginMessages.Parse(packet) is Internal.Login.ZoneRelogTag tag)
            {
                Logout.RelogTag = tag.Tag;
                log.Info($"Zone relog tag {tag.Tag}.");
            }
            else if (Internal.WorldChannel.RatingMessages.Parse(packet) is Internal.WorldChannel.RatingClientRequest request)
                FromClient(new TunnelFromExternal
                {
                    ConnectionId = request.ConnectionId, CharacterGuid = request.Character, Channel = GatewayChannel.World, Packet = request.Packet
                });
            else if (!TradeBroker.FromZone(packet) && !EncounterBroker.FromZone(packet) && !RatingBroker.FromZone(packet))
                log.Debug($"Zone sent service packet 0x{packet.Family:X}/0x{packet.Sub:X}: not used by the world channel.");
        }

        public static bool ToGateway(IInternalMessage message)
        {
            lock (sync)
                return gatewayLink != null && gatewayLink.Send(message);
        }

        public static bool ToZone(IInternalMessage message)
        {
            lock (sync)
                return zoneLink != null && zoneLink.Send(message);
        }

        /// <summary>Counters for the log: packets handed on and dropped.</summary>
        public static (long Relayed, long Dropped) Counts => (Interlocked.Read(ref relayed), Interlocked.Read(ref dropped));

        public static void Stop()
        {
            RatingBroker.Stop();
            gateway?.Dispose();
            zone?.Dispose();
        }
    }
}
