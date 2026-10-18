using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// The world server as a zone back end of a gateway in its own process (trace/architecture/gateway-service.md §3,
    /// Phase B gateway step 3), used when GATEWAY_INTERNAL_ADDRESS is set — the world then opens no UDP port. The
    /// world dials the gateway (the stable hub) and registers (RegisterBackEnd 0x20, kind zone); the gateway attaches
    /// each client that logged in (AttachConnection 0x21), tunnels its packets (TunnelPacketFromExternalConnection 7,
    /// as the client sent them and past its flood limits) and reports closed connections (ConnectionClosed 0x23).
    /// The zone answers through <see cref="RemoteClientLink"/>: TunnelPacketToExternalConnection 8 and CloseConnection
    /// 0x24. Everything received is queued and run on the world thread by <see cref="Update"/>, so game code never
    /// sees another thread.
    /// When the link to the gateway drops, every connection it carried counts as closed here (players leave and are
    /// saved); the gateway holds the clients and re-attaches them when a zone registers again (isReattach,
    /// <see cref="WorldSession.OnReattach"/>).
    /// </summary>
    public static class GatewayLink
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static InternalDialer dialer;
        private static readonly object sync = new();
        private static InternalLink link;
        private static uint worldId;

        /// <summary>This world's server id as registered with the gateway (0 before <c>Start</c>).</summary>
        public static uint ServerWorldId => worldId;
        private static readonly ConcurrentQueue<Action> work = new();
        private static readonly Dictionary<ulong, WorldSession> byConnection = new();

        /// <summary>
        /// Connections whose AttachConnection is still loading the account and character (off the world thread), with the
        /// serial of that attach. A ConnectionClosed (or the gateway link dropping) meanwhile takes the entry out, and a
        /// newer attach of the same connection replaces it; the load's completion only makes a session while its own
        /// serial is still the entry. Before this, a client that went during the load (ConnectionClosed found no session
        /// yet and did nothing) got its session made afterwards anyway: a ghost the gateway would never close, kept in
        /// <see cref="byConnection"/> and <see cref="ZoneSessions"/> and flushed every tick for the life of the process.
        /// Guarded by <see cref="sync"/>.
        /// </summary>
        private static readonly Dictionary<ulong, long> attaching = new();
        private static long attachSerial;

        /// <summary>True when this world runs behind a separate gateway process.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>Starts dialing the gateway when <paramref name="address"/> is set; false (in-process gateway) otherwise.</summary>
        public static bool Start(string address, uint world)
        {
            if (string.IsNullOrWhiteSpace(address))
                return false;
            Enabled = true;
            worldId = world;
            dialer = new InternalDialer(address.Trim());
            dialer.Connected += OnConnected;
            dialer.Start();
            log.Info($"Gateway: separate process at {address} (this world opens no UDP port).");
            return true;
        }

        public static void Stop()
        {
            dialer?.Dispose();
            zoneListener?.Dispose();
        }

        private static InternalListener zoneListener;

        /// <summary>
        /// The zone's own internal port (ZONE_INTERNAL_PORT; trace/architecture/world-channel-service.md §6.2): the world's
        /// world-channel service dials it to hand on the channel-1 packets the gateway routed to it
        /// (TunnelPacketFromExternalConnection with channel 1). They take the same path as packets from the gateway.
        /// </summary>
        public static void ListenForServices(int port)
        {
            if (port <= 0)
                return;
            zoneListener = new InternalListener(System.Net.IPAddress.Any, port);
            zoneListener.Accepted += service =>
            {
                service.Received += (l, message) =>
                {
                    switch (message)
                    {
                        case RegisterBackEnd register:
                            log.Info($"{register.Kind} service connected to the zone ({register.Name}).");
                            // Relogin keys the world channel makes start with this zone's tag, so a client back from
                            // character select keeps its cached item list (ItemDefinitionManager.RelogTag).
                            l.Send(Internal.Social.SocialMessages.ToPacket(new Internal.Login.ZoneRelogTag
                            {
                                Tag = Game.Inventory.ItemDefinitionManager.RelogTag
                            }));
                            // The world channel brokers trade invitations (world-channel-service.md §6.4).
                            if (register.Kind == BackEndKind.WorldChannel)
                                Game.WorldChannel.WorldChannelLink.Attach(l);
                            break;
                        case TunnelFromExternal tunnel:
                            OnReceived(tunnel);
                            break;
                        case ServicePacket status when Game.ServiceStatuses.TryTake(status):
                            break;
                        case ServicePacket packet when Internal.Login.LoginMessages.Parse(packet) is Internal.Login.NoteRelogin note:
                            work.Enqueue(() => Game.Inventory.ItemDefinitionManager.NoteRelogin(note.AccountId, note.SessionKey));
                            break;
                        case ServicePacket packet when packet.Family is Internal.WorldChannel.TradeMessages.TradeFamily
                            or Internal.WorldChannel.EncounterMessages.EncounterFamily
                            or Internal.WorldChannel.RatingMessages.ClaimsFamily or Internal.WorldChannel.RatingMessages.RatingFamily:
                            // Trade broker messages (TradeZoneSessionStart / Open / End), encounter broker messages
                            // (EncounterParticipants / Notice) and rating broker messages (ClaimInfoQuery, RatingVoteCounted /
                            // Refused), in order with the tunnelled packets.
                            work.Enqueue(() => Game.WorldChannel.WorldChannelLink.Received(l, packet));
                            break;
                        default:
                            log.Warn($"Service sent internal opcode {message.Opcode} to the zone: not handled.");
                            break;
                    }
                };
                service.Closed += (l, error) =>
                {
                    log.Info($"Service link to the zone from {l.Remote} closed.");
                    // A world channel's brokered trades end with it (TradeCanceled, world-channel-service.md §7).
                    work.Enqueue(() => Game.WorldChannel.WorldChannelLink.Detach(l));
                };
                service.Start();
            };
            zoneListener.Start();
            log.Info($"Zone internal port {zoneListener.Port} (world-channel service).");
        }

        private static void OnConnected(InternalLink newLink)
        {
            newLink.Received += (_, message) => OnReceived(message);
            newLink.Closed += (l, error) =>
            {
                lock (sync)
                    if (link == l)
                        link = null;
                log.Warn($"Gateway link lost{(error != null ? ": " + error.Message : "")}; its connections are closed, reconnecting.");
                work.Enqueue(CloseAll);
            };
            newLink.Send(new RegisterBackEnd
            {
                Kind = BackEndKind.Zone, WorldId = worldId, ZoneIds = Array.Empty<uint>(),
                // Channels 0 Zone, 1 World, 2 PositionUpdate, 3, and no zone ids = every landscape of the world: one world
                // process serves them all today. The gateway's routing table (gateway-service.md §3.3) makes this the
                // channel-0 back end of every connection; channel 1 goes to a world-channel back end instead when one is
                // registered, and back here while none is.
                ChannelMask = 0x0F, Name = Environment.MachineName, BuildStamp = 0,
                // What other worlds' Landscapes windows list this one as (cross-world travel, B.10).
                ClientWorldId = Game.Landscapes.WorldId, WorldNameText = Game.Landscapes.WorldNameText
            });
            lock (sync)
                link = newLink;
            log.Info("Gateway connected; registered as the zone back end.");
        }

        private static void OnReceived(IInternalMessage message)
        {
            switch (message)
            {
                case ServicePacket status when Game.ServiceStatuses.TryTake(status):
                    break;
                case AttachConnection attach:
                    Attach(attach);
                    break;
                case TunnelFromExternal tunnel:
                    work.Enqueue(() =>
                    {
                        if (Find(tunnel.ConnectionId) is WorldSession session)
                            session.FromClient((Channel)tunnel.Channel, tunnel.Packet);
                    });
                    break;
                case WorldsReachable reachable:
                    Game.Landscapes.SetReachableWorlds(reachable.Worlds, worldId);
                    break;
                case ConnectionClosed closed:
                    work.Enqueue(() =>
                    {
                        // An attach still loading for it makes no session now (see attaching).
                        lock (sync)
                            if (attaching.Remove(closed.ConnectionId))
                                log.Info($"Connection {closed.ConnectionId:X} closed by the gateway while its attach was loading.");
                        if (Find(closed.ConnectionId) is WorldSession session)
                        {
                            ((RemoteClientLink)session.Link).MarkClosed();
                            session.LinkClosed();
                            lock (sync)
                                byConnection.Remove(closed.ConnectionId);
                            log.Debug($"Connection {closed.ConnectionId:X} closed by the gateway ({closed.Reason}).");
                        }
                    });
                    break;
                default:
                    log.Warn($"Gateway sent internal opcode {message.Opcode}: not handled.");
                    break;
            }
        }

        private static WorldSession Find(ulong connectionId)
        {
            lock (sync)
                return byConnection.TryGetValue(connectionId, out WorldSession session) ? session : null;
        }

        /// <summary>
        /// AttachConnection: the gateway checked the ticket; the zone loads the account and character (as the in-process
        /// login did) and starts the zone session, then answers ConnectionAttached.
        /// </summary>
        private static void Attach(AttachConnection attach)
        {
            // Link thread, before any later ConnectionClosed of this connection is read (same link, in order).
            long serial = System.Threading.Interlocked.Increment(ref attachSerial);
            lock (sync)
                attaching[attach.ConnectionId] = serial;
            Task.Run(async () =>
            {
                AccountModel account = await DatabaseManager.Instance.AuthDatabase.GetAccountByUsername(attach.AccountName);
                List<CharacterModel> characters = account != null
                    ? await DatabaseManager.Instance.CharacterDatabase.GetCharacters(account.Id) : new List<CharacterModel>();
                CharacterModel character = characters.SingleOrDefault(c => c.Id == attach.CharacterGuid);
                work.Enqueue(() =>
                {
                    lock (sync)
                    {
                        if (!attaching.TryGetValue(attach.ConnectionId, out long current) || current != serial)
                        {
                            log.Info($"Attach for connection {attach.ConnectionId:X} ({attach.AccountName}) dropped: the connection "
                                + "closed, or was attached again, while it loaded.");
                            return;
                        }
                        attaching.Remove(attach.ConnectionId);
                    }
                    var remote = new RemoteClientLink(attach.ConnectionId);
                    if (account == null || account.Id != attach.AccountId || character == null)
                    {
                        log.Error($"Attach for connection {attach.ConnectionId:X}: account {attach.AccountName} / character "
                            + $"{attach.CharacterGuid} not found or not theirs; closing.");
                        remote.Close();
                        return;
                    }
                    var session = new WorldSession(remote, account, attach.ClientBuild, ClientProtocol.LandmarkClientProtocol_709, attach.Ticket);
                    lock (sync)
                        byConnection[attach.ConnectionId] = session;
                    ZoneSessions.Add(session);
                    if (attach.IsReattach)
                        session.OnReattach(character, attach.IsTransfer, attach.ArriveNear, attach.JoinGame, attach.ReturnTo, attach.ArriveAt,
                            attach.CavernTrip);
                    else
                        session.OnCharacterLogin(character);
                    Send(new ConnectionAttached
                    {
                        ConnectionId = attach.ConnectionId, ZoneId = session.ZoneId, TransientId = session.TransientId,
                        OffsetX = WorldSession.RemoteFrameOffset.X, OffsetY = WorldSession.RemoteFrameOffset.Y,
                        OffsetZ = WorldSession.RemoteFrameOffset.Z, X = character.PositionX, Y = character.PositionY, Z = character.PositionZ
                    });
                });
            });
        }

        /// <summary>
        /// ZoneChanged 0x27 (gateway-service.md §3.4): the player is on another landscape now. The gateway keeps each
        /// connection's landscape for its routing table (§3.3: back ends register the landscapes they serve), so a
        /// service registered for some landscapes only gets the connection's channel while it is on one of them. One
        /// small message per re-zone.
        /// </summary>
        public static void SendZoneChanged(WorldSession session)
        {
            if (!Enabled || session.Link is not RemoteClientLink remote || remote.Closed)
                return;
            Send(new ZoneChanged
            {
                ConnectionId = remote.ConnectionId, ZoneId = session.ZoneId, OffsetX = WorldSession.RemoteFrameOffset.X,
                OffsetY = WorldSession.RemoteFrameOffset.Y, OffsetZ = WorldSession.RemoteFrameOffset.Z
            });
        }

        /// <summary>The link is gone: every connection it carried is closed on this side.</summary>
        private static void CloseAll()
        {
            List<WorldSession> sessions;
            lock (sync)
            {
                sessions = byConnection.Values.ToList();
                byConnection.Clear();
                // Attaches still loading belong to the lost link too: the gateway attaches them again when it is back.
                attaching.Clear();
            }
            foreach (WorldSession session in sessions)
            {
                ((RemoteClientLink)session.Link).MarkClosed();
                session.LinkClosed();
            }
            if (sessions.Count > 0)
                log.Warn($"{sessions.Count} connection(s) closed with the gateway link.");
        }

        /// <summary>World thread, every tick: runs what the link queued, then sends each session's queued packets.</summary>
        public static void Update()
        {
            if (!Enabled)
                return;
            while (work.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Error(e, "Gateway work item failed.");
                }
            }
            List<WorldSession> sessions;
            lock (sync)
                sessions = byConnection.Values.ToList();
            foreach (WorldSession session in sessions)
            {
                try
                {
                    session.Flush();
                }
                catch (Exception e)
                {
                    log.Error(e, $"Sending to {session.CharacterName} failed.");
                }
            }
            // This tick's movement, one list packet per mover (gateway-service.md §3.8).
            MovementFanout.Flush();
        }

        /// <summary>
        /// Cross-world travel (TASKS B.10): this zone lets go of the player — what it queued is sent, the session leaves the
        /// world here (PlayerManager.Leave, position and played time saved) — and once <paramref name="saved"/> (the target
        /// landscape and world written to the character) has finished, HandOver with the target world asks the gateway to
        /// attach the connection to that world's zone (a transfer attach: it re-zones the client there). False when this
        /// world isn't behind a separate gateway. <paramref name="arriveNear"/>: arrive beside that character (Travel to
        /// Friend); <paramref name="joinGame"/>: join that site game there (a Game Finder match, L31), and with
        /// <paramref name="returnTo"/> be taken back there after it (FinderReturnHome); <paramref name="arriveAt"/>: arrive
        /// at that spot instead of the landscape's usual arrival (the way back); <paramref name="cavernTrip"/>: a Lost Crystal /
        /// Underground Adventure trip, whose cavern the target world picks (L131).
        /// </summary>
        public static bool HandOverToWorld(WorldSession session, uint targetWorld, uint zoneId, System.Threading.Tasks.Task saved, ulong arriveNear = 0,
            ulong joinGame = 0, WorldSpot returnTo = default, WorldSpot arriveAt = default, CavernTripKind cavernTrip = CavernTripKind.None)
        {
            if (!Enabled || session.Link is not RemoteClientLink remote || remote.Closed)
                return false;
            session.Flush();
            remote.MarkClosed();
            lock (sync)
                byConnection.Remove(remote.ConnectionId);
            session.LinkClosed();
            ulong connection = remote.ConnectionId;
            saved.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    log.Error(t.Exception, $"Saving {session.CharacterName} for the move to world {targetWorld} failed; handing over anyway.");
                Send(new HandOver
                {
                    ConnectionId = connection, TargetZoneId = zoneId, TargetWorldId = targetWorld, ArriveNear = arriveNear, JoinGame = joinGame,
                    ReturnTo = returnTo, ArriveAt = arriveAt, CavernTrip = cavernTrip
                });
            });
            return true;
        }

        internal static bool Connected
        {
            get
            {
                lock (sync)
                    return link is { IsOpen: true };
            }
        }

        internal static bool Send(IInternalMessage message)
        {
            lock (sync)
                return link != null && link.Send(message);
        }
    }

    /// <summary>A client connection on the separate gateway, as its zone session sees it.</summary>
    public sealed class RemoteClientLink : IClientLink
    {
        private bool closed;

        public RemoteClientLink(ulong connectionId) => ConnectionId = connectionId;

        public ulong ConnectionId { get; }

        public bool CanSend => !closed && GatewayLink.Connected;

        public bool Closed => closed;

        internal void MarkClosed() => closed = true;

        public void Send(byte[] packet)
        {
            if (closed)
                return;
            GatewayLink.Send(new TunnelToExternal
            {
                Destination = new ExternalDestination(ConnectionId, GatewayChannel.Zone, ExternalDestination.Reliable),
                Packet = packet
            });
        }

        /// <summary>CloseConnection; the gateway answers ConnectionClosed, which ends the zone session.</summary>
        public void Close()
        {
            if (closed)
                return;
            if (!GatewayLink.Send(new CloseConnection { ConnectionId = ConnectionId, Reason = "zone" }))
                closed = true;
        }
    }
}
