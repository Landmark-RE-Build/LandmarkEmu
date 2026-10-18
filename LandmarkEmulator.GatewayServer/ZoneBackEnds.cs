using LandmarkEmulator.Internal;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace LandmarkEmulator.GatewayServer
{
    /// <summary>
    /// The gateway's internal side (trace/architecture/gateway-service.md §3.3-§3.5). Back ends dial the internal port and
    /// register (RegisterBackEnd 0x20: kind, world, served landscapes, channel mask); the registrations fill the
    /// <see cref="Table"/>, and a closed link takes its registration out again. Nothing is special-cased by kind:
    /// <list type="bullet">
    /// <item>channel 0: the back end that registered channel 0 for the connection's landscape is its <b>zone</b>; the
    /// gateway attaches each logged-in client to it (AttachConnection 0x21 → ConnectionAttached 0x22) and tells it when
    /// the client goes (ConnectionClosed 0x23);</item>
    /// <item>channels 1-3: a service registered for the channel (e.g. the world-channel back end, kind WorldChannel, mask
    /// 1 &lt;&lt; 1) takes it; with none registered the connection's zone does (§3.3);</item>
    /// <item>anything a back end tunnels back (TunnelPacketToExternalConnection 8 / List 0x16, CloseConnection 0x24) is
    /// delivered whichever back end sent it.</item>
    /// </list>
    /// Each <see cref="GatewayClient"/> keeps its own channel → back end answers and refreshes them when the table
    /// changes (<see cref="RoutingTable.Version"/>), so the per-packet path is a field read and a queue write.
    /// When a zone's link drops, the clients attached to it are held (§3.5, <see cref="GatewayClient"/>) and re-attached
    /// to the next back end that registers channel 0 for their landscape (AttachConnection with isReattach).
    /// </summary>
    public static class ZoneBackEnds
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static InternalListener listener;
        private static readonly object sync = new();
        /// <summary>Every logged-in connection (attached, held, or waiting for its first zone).</summary>
        private static readonly Dictionary<ulong, GatewayClient> clients = new();

        /// <summary>The registrations: (world, channel) → back ends.</summary>
        public static RoutingTable Table { get; } = new();

        private static uint world;

        /// <summary>
        /// The world this gateway serves (GatewayServer.json WorldId; 0 = the world of the first back end that registers).
        /// Registrations for another world are not routed.
        /// </summary>
        public static uint World
        {
            get
            {
                lock (sync)
                    return world;
            }
        }

        /// <summary>GatewayServer.json MultiWorld (Docker GATEWAY_MULTI_WORLD): off, on, or auto (default).</summary>
        public enum MultiWorldMode { Off, On, Auto }

        /// <summary>
        /// Cross-world travel (B.10): <see cref="MultiWorldMode.Auto"/> (default) turns it on by itself when a zone of a
        /// second world registers here — a gateway with one world sees no difference, since only zones that dial this
        /// gateway can register and the zones list no other world while there is none. <see cref="MultiWorldMode.Off"/>
        /// refuses other worlds' zones, as before 2026-10-18.
        /// </summary>
        public static MultiWorldMode Mode { get; set; } = MultiWorldMode.Auto;

        /// <summary>Zones of other worlds are routed too (cross-world travel B.10): on, or auto.</summary>
        public static bool MultiWorld => Mode != MultiWorldMode.Off;

        /// <summary>"auto" (or empty / unknown), "true" / "on" / "1", "false" / "off" / "0".</summary>
        public static MultiWorldMode ParseMultiWorld(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "true": case "on": case "1": case "yes": return MultiWorldMode.On;
                case "false": case "off": case "0": case "no": return MultiWorldMode.Off;
                case "": case "auto": return MultiWorldMode.Auto;
                default:
                    log.Warn($"MultiWorld '{value}' is not auto, true or false: auto is used.");
                    return MultiWorldMode.Auto;
            }
        }

        /// <summary>The worlds with a zone registered here now.</summary>
        private static int ZoneWorlds() => Table.All().Where(b => b.Kind == BackEndKind.Zone).Select(b => b.WorldId).Distinct().Count();

        public static void Start(IPAddress address, int port, uint worldId)
        {
            world = worldId;
            listener = new InternalListener(address, port);
            listener.Accepted += OnAccepted;
            listener.Start();
            log.Info($"Gateway internal port {listener.Port}: waiting for back ends{(worldId != 0 ? $" of world {worldId}" : "")}.");
        }

        public static void Stop()
        {
            listener?.Dispose();
            foreach (BackEnd backEnd in Table.All())
                backEnd.Link.Close();
        }

        private static void OnAccepted(InternalLink link)
        {
            link.Received += (l, message) => OnReceived(l, message);
            link.Closed += (l, error) => OnClosed(l, error);
            link.Start();
            log.Info($"Back end connected from {link.Remote}.");
        }

        private static void OnClosed(InternalLink link, Exception error)
        {
            BackEnd gone = Table.Remove(link);
            if (gone != null)
            {
                Lost(gone, error?.Message);
                if (gone.Kind == BackEndKind.Zone)
                    SendWorldsReachable();
            }
        }

        /// <summary>
        /// A back end left the table (its link closed, or a restarted copy replaced it): the clients attached to it are
        /// held (§3.5) and every connection looks at its routes again (a service's channels fall back to the zone).
        /// </summary>
        private static void Lost(BackEnd gone, string why)
        {
            List<GatewayClient> all = Clients();
            List<GatewayClient> held = all.Where(c => c.Zone == gone).ToList();
            if (gone.Takes(GatewayChannel.Zone))
                log.Warn($"Zone back end gone{(why != null ? ": " + why : "")}: {gone}; holding its {held.Count} client(s).");
            else
                log.Warn($"Back end gone{(why != null ? ": " + why : "")}: {gone}; its channels go to the next registered "
                    + "back end, else to each connection's zone.");
            foreach (GatewayClient client in held)
                client.RequestHold();
            foreach (GatewayClient client in all)
                client.RoutesChanged();
        }

        private static void OnReceived(InternalLink link, IInternalMessage message)
        {
            switch (message)
            {
                case RegisterBackEnd register:
                    Register(link, register);
                    break;
                case ConnectionAttached attached:
                    if (Find(attached.ConnectionId) is GatewayClient client)
                    {
                        if (client.Zone?.Link == link)
                            client.Attached(attached.ZoneId);
                        else
                            log.Debug($"ConnectionAttached for {attached.ConnectionId:X} from {link.Remote}, not its zone: ignored.");
                    }
                    break;
                case ZoneChanged changed:
                    Find(changed.ConnectionId)?.ZoneChangedTo(changed.ZoneId);
                    break;
                case TunnelToExternal one:
                    Find(one.Destination.ConnectionId)?.FromZone(one.Packet);
                    break;
                case TunnelToExternalList many:
                    foreach (ExternalDestination d in many.Destinations)
                        Find(d.ConnectionId)?.FromZone(many.Packet);
                    break;
                case CloseConnection close:
                    Find(close.ConnectionId)?.RequestClose();
                    break;
                case HandOver handOver:
                    HandOverConnection(link, handOver);
                    break;
                default:
                    log.Warn($"Back end sent internal opcode {message.Opcode}: not handled.");
                    break;
            }
        }

        /// <summary>
        /// RegisterBackEnd: into the table (replacing a live registration of the same role, which is closed), then every
        /// connection looks at its routes again, and the connections waiting for a zone of a landscape this back end
        /// serves on channel 0 are attached to it.
        /// </summary>
        private static void Register(InternalLink link, RegisterBackEnd register)
        {
            var backEnd = new BackEnd(link, register);
            lock (sync)
            {
                if (world == 0)
                {
                    world = register.WorldId;
                    log.Info($"Serving world {world} (the first back end's).");
                }
                else if (register.WorldId != world && !MultiWorld)
                {
                    log.Warn($"{backEnd} is for world {register.WorldId}, this gateway serves world {world}: not routed "
                        + "(GATEWAY_MULTI_WORLD is false).");
                    return;
                }
                else if (register.WorldId != world && Mode == MultiWorldMode.Auto && register.Kind == BackEndKind.Zone)
                    log.Info($"{backEnd} is for world {register.WorldId} (this gateway's logins go to world {world}): travel "
                        + "between the worlds is on (GATEWAY_MULTI_WORLD auto).");
            }
            foreach (BackEnd old in Table.Add(backEnd))
            {
                // Out of the table already, so its link's Closed finds nothing: its clients are held here.
                Lost(old, "replaced by a new registration of the same role");
                old.Link.Close();
            }
            // "Zone back end registered" is what re/test/socialtest/up.sh waits for.
            log.Info(backEnd.Kind == BackEndKind.Zone ? $"Zone back end registered: {backEnd}." : $"Back end registered: {backEnd}.");

            List<GatewayClient> all = Clients();
            foreach (GatewayClient client in all)
                client.RoutesChanged();
            if (!backEnd.Takes(GatewayChannel.Zone))
                return;
            // Connections held while their zone was away, or waiting since their login for one, are attached to it.
            List<GatewayClient> waiting = all.Where(c => c.WaitingForZone && !c.Disconnected && c.WorldId == backEnd.WorldId
                && backEnd.Serves(c.ZoneId)).ToList();
            int attached = waiting.Count(c => Attach(c));
            if (attached > 0)
                log.Info($"Attaching {attached} waiting connection(s) to it.");
            if (backEnd.Kind == BackEndKind.Zone)
                SendWorldsReachable();
        }

        /// <summary>
        /// WorldsReachable (0x29) to every zone back end: each world with a registered zone, with the client world id
        /// and name text its zone registered (cross-world travel, B.10). Only with <see cref="MultiWorld"/>: a one-world
        /// gateway tells its zones nothing, so they list no other world.
        /// </summary>
        private static void SendWorldsReachable()
        {
            if (!MultiWorld)
                return;
            List<BackEnd> zones = Table.All().Where(b => b.Kind == BackEndKind.Zone).ToList();
            var worlds = zones.Where(b => b.ClientWorldId != 0).GroupBy(b => b.WorldId)
                .Select(g => (g.Key, g.First().ClientWorldId, g.First().WorldNameText)).OrderBy(w => w.Key).ToList();
            var message = new WorldsReachable { Worlds = worlds };
            foreach (BackEnd zone in zones)
                zone.Link.Send(message);
            log.Info($"Worlds reachable: {string.Join(", ", worlds.Select(w => $"{w.Item1} (client id {w.Item2})"))}.");
        }

        /// <summary>
        /// HandOver (0x25) from a zone: the connection goes to the zone of <see cref="HandOver.TargetWorldId"/> (or this
        /// world) serving <see cref="HandOver.TargetZoneId"/>, as a transfer attach (AttachConnection isReattach +
        /// isTransfer; gateway-service.md §3.6). The old zone has saved the character and dropped the session already. With
        /// no such zone registered the connection is held like a lost zone's (ChannelIsRoutable 0) and attached when one
        /// registers, else back to character select after the hold.
        /// </summary>
        private static void HandOverConnection(InternalLink link, HandOver handOver)
        {
            GatewayClient client = Find(handOver.ConnectionId);
            if (client == null || client.Zone?.Link != link)
            {
                log.Debug($"HandOver for {handOver.ConnectionId:X} from {link.Remote}, not its zone: ignored.");
                return;
            }
            uint target = handOver.TargetWorldId != 0 ? handOver.TargetWorldId : client.WorldId;
            if (target != client.WorldId && !MultiWorld)
            {
                log.Warn($"{client.CharacterName}: hand-over to world {target} refused (MultiWorld off); closing.");
                client.RequestClose();
                return;
            }
            client.BeginHandOver(target, handOver.TargetZoneId, handOver.ArriveNear, handOver.JoinGame, handOver.ReturnTo, handOver.ArriveAt,
                handOver.CavernTrip);
            if (Attach(client))
                log.Info($"{client.CharacterName}: handed over to world {target}, landscape {handOver.TargetZoneId}.");
            else
            {
                client.RequestHold();
                log.Warn($"{client.CharacterName}: no zone of world {target} serves landscape {handOver.TargetZoneId}; held.");
            }
        }

        private static DateTime nextStatus;
        private static readonly DateTime started = DateTime.UtcNow;

        /// <summary>
        /// Every <see cref="ServiceStatus.Interval"/> (gateway tick): its connections and back ends to every zone back end,
        /// for the zones' admin monitor page (TASKS B.7).
        /// </summary>
        public static void SendStatusWhenDue()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextStatus)
                return;
            nextStatus = now + ServiceStatus.Interval;
            List<GatewayClient> all = Clients();
            List<BackEnd> backEnds = Table.All();
            var rows = new List<(string, string)>
            {
                ("Up for", $"{(int)(now - started).TotalHours}h {(now - started).Minutes}m"),
                ("World", World.ToString() + Mode switch
                {
                    MultiWorldMode.On => " (multi-world)",
                    MultiWorldMode.Auto => $" (multi-world auto: {ZoneWorlds()} world(s) with zones here)",
                    _ => ""
                }),
                ("Connections", all.Count.ToString()),
                ("Attached", all.Count(c => c.Zone != null && !c.Held).ToString()),
                ("Held (zone away)", all.Count(c => c.Held).ToString()),
                ("Waiting for a zone", all.Count(c => c.WaitingForZone && !c.Held).ToString()),
                ("Back ends", string.Join(", ", backEnds.Select(b => $"{b.Kind} world {b.WorldId}")))
            };
            ServicePacket status = ServiceStatus.Build("Gateway", rows);
            foreach (BackEnd zone in backEnds.Where(b => b.Kind == BackEndKind.Zone))
                zone.Link.Send(status);
        }

        private static List<GatewayClient> Clients()
        {
            lock (sync)
                return clients.Values.ToList();
        }

        private static GatewayClient Find(ulong connectionId)
        {
            lock (sync)
                return clients.TryGetValue(connectionId, out GatewayClient client) ? client : null;
        }

        /// <summary>A client logged in: kept here from now on (until <see cref="Closed"/>), then attached if a zone serves it.</summary>
        public static bool LoggedIn(GatewayClient client)
        {
            lock (sync)
                clients[client.ConnectionId] = client;
            Attach(client);
            // (A registration on another thread may have attached it meanwhile.)
            return client.Zone != null;
        }

        /// <summary>
        /// AttachConnection to the zone back end of the client's landscape (isReattach once the client has been in the
        /// world: a held connection whose zone came back). False when no back end serves channel 0 for it.
        /// </summary>
        public static bool Attach(GatewayClient client)
        {
            BackEnd zone = Table.ZoneFor(client.WorldId, client.ZoneId);
            // Once per zone: a login and a registration on another thread may both try.
            if (zone == null || !client.TryAttachTo(zone))
                return false;
            return zone.Link.Send(new AttachConnection
            {
                ConnectionId = client.ConnectionId, CharacterGuid = client.CharacterGuid, AccountId = client.Account.Id,
                AccountName = client.Account.Username ?? "", ClientBuild = client.ClientBuild ?? "", Ticket = client.LoginTicket ?? "",
                IsRelog = (client.LoginTicket ?? "").StartsWith("R", StringComparison.Ordinal), IsReattach = client.EverAttached,
                ZoneId = client.ZoneId, IsTransfer = client.TakeTransfer(), ArriveNear = client.TakeArriveNear(),
                JoinGame = client.TakeJoinGame(), ReturnTo = client.TakeReturnTo(), ArriveAt = client.TakeArriveAt(),
                CavernTrip = client.TakeCavernTrip()
            });
        }

        /// <summary>
        /// The client went: ConnectionClosed to its zone, which removes the character from the world at once (others get
        /// the despawn). Reason Timeout when the client went silent (no packet for CLIENT_TIMEOUT_SECONDS: a crash, a killed
        /// process, a lost network) or stopped acknowledging. Once per connection.
        /// </summary>
        public static void Closed(GatewayClient client)
        {
            lock (sync)
                if (!clients.Remove(client.ConnectionId))
                    return;
            client.Zone?.Link.Send(new ConnectionClosed
            {
                ConnectionId = client.ConnectionId, Reason = client.TimedOut ? CloseReason.Timeout : CloseReason.Disconnected
            });
        }
    }
}
