using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Login;
using LandmarkEmulator.Shared.Game.Events;
using LandmarkEmulator.Shared.Configuration;
using System;
using System.Collections.Concurrent;

namespace LandmarkEmulator.GatewayServer
{
    /// <summary>
    /// One client connection on the gateway process. The SOE session, gateway login, 0.695 translation and flood limits
    /// are <see cref="ZoneGatewaySession"/>'s; this class connects the result to the zone back end
    /// (<see cref="ZoneBackEnds"/>): AttachConnection after the login, TunnelPacketFromExternalConnection for every
    /// client packet once the zone answered ConnectionAttached (packets before that are held), ConnectionClosed when the
    /// client goes; and it sends what the zone tunnels back. Everything the zone link delivers is queued here and acted
    /// on in the gateway's own tick (<see cref="OnProcessZonePackets"/>), the thread the SOE session belongs to.
    /// <para>
    /// When the zone goes away (a crash, or its container restarted) the connection is <b>held</b> (gateway-service.md
    /// §3.5): the SOE session stays up, the client is told its zone channels are not routable, its packets wait (not its
    /// movement), and when a zone registers again the connection is re-attached (AttachConnection with isReattach); the
    /// zone then rebuilds the client's world with a re-zone. A logout during the hold closes at once; after
    /// <see cref="HoldLimit"/> the client is sent back to character select (CharacterSelectSessionResponse 0xF7 with a
    /// relog session from the login service: it leaves by itself and can press Play again), or, without a login
    /// service, the connection closes.
    /// </para>
    /// <para>
    /// Routing (gateway-service.md §3.3, filled by registration, <see cref="RoutingTable"/>): channel 0 goes to the zone
    /// the connection is attached to; channels 1-3 to a service registered for the channel (channel 1: the
    /// world-channel back end) or else to the zone. Only what really has no back end waits: during a zone hold a
    /// registered world-channel service still takes channel 1. ChannelIsRoutable is sent per channel whenever a channel
    /// changes between routable and not; ConnectionIsNotRoutable when no zone serves the landscape at the login (§3.5
    /// item 5), after which the connection waits for one like a hold.
    /// </para>
    /// <para>
    /// With a login service configured the ticket check goes there (<see cref="Login"/>, §3.7) instead of the
    /// auth database.
    /// </para>
    /// </summary>
    public sealed class GatewayClient : ZoneGatewaySession
    {
        /// <summary>The login service, when configured (ticket checks, relog sessions); null = the database.</summary>
        public static LoginClient Login { get; set; }

        /// <summary>How long a connection waits for its zone to come back (GATEWAY_HOLD_SECONDS, default 120).</summary>
        public static TimeSpan HoldLimit { get; set; } = TimeSpan.FromSeconds(120);

        private readonly ConcurrentQueue<byte[]> fromZone = new();

        // Set on the back ends' link threads.
        private readonly object attachSync = new();
        private volatile BackEnd zone;
        private volatile uint zoneId;
        /// <summary>The world whose zones serve this connection: the gateway's own from login, another after a hand-over.</summary>
        private volatile uint worldId;
        /// <summary>The next attach is a cross-world transfer (HandOver with a target world).</summary>
        private volatile bool transferPending;
        /// <summary>No zone has (or is loading) the character: held, or waiting since the login for a first zone.</summary>
        private volatile bool waiting;
        private volatile bool attachedSeen, closeRequested, holdRequested, routesDirty = true;

        // The gateway's tick (read on the receive thread too).
        private volatile bool attached, everAttached;
        private DateTime? heldSince;
        /// <summary>Sent back to character select after the hold: closed if the client hasn't left by then.</summary>
        private DateTime? closeAfter;
        private bool sentAway;

        /// <summary>
        /// Channel → back end for this connection (index = channel): 0 its zone, 1-3 a service registered for the channel
        /// or else its zone. Refreshed on the tick when the routing table or the connection's zone changed; read per packet.
        /// </summary>
        private readonly BackEnd[] routes = new BackEnd[4];
        private int routesVersion = -1;
        /// <summary>What the client was last told per channel (ChannelIsRoutable); it starts out assuming every channel works.</summary>
        private readonly bool[] told = { true, true, true, true };

        public GatewayClient() : base(ConfigurationManager<GatewayServerConfiguration>.Instance.Config.EncryptionKey)
        {
        }

        /// <summary>The character this connection logged in as (0 before the login).</summary>
        public ulong CharacterGuid { get; private set; }
        public string CharacterName { get; private set; } = "";
        /// <summary>The landscape the character is on (login, ConnectionAttached, ZoneChanged): picks its zone back end.</summary>
        public uint ZoneId => zoneId;
        /// <summary>The world whose zones serve this connection (cross-world travel, B.10).</summary>
        public uint WorldId => worldId;

        /// <summary>The zone back end the connection is attached (or being attached) to; null before the first.</summary>
        public BackEnd Zone => zone;

        /// <summary>The character has been in the world through this connection: a new attach is a re-attach.</summary>
        public bool EverAttached => everAttached;

        /// <summary>Waiting for a zone back end (its zone went away, or none served its landscape at the login).</summary>
        public bool WaitingForZone => waiting;

        /// <summary>Waiting for its zone to come back (or for a first one).</summary>
        public bool Held => heldSince != null || holdRequested;

        protected override string DisplayName => CharacterName.Length > 0 ? CharacterName : base.DisplayName;

        protected override bool ZoneReady => attached && !holdRequested && heldSince == null;

        /// <summary>The login check through the login service when one is configured (the database otherwise).</summary>
        protected override void CheckLogin(string ticket, ulong characterId, Action<AccountModel, CharacterModel> done)
        {
            if (Login == null)
            {
                base.CheckLogin(ticket, characterId, done);
                return;
            }
            EnqueueEvent(new TaskGenericEvent<TicketConsumeReply>(Login.Consume(ticket, characterId, ZoneBackEnds.World), reply =>
            {
                if (reply == null)
                    done(null, null);
                else
                    done(new AccountModel { Id = reply.AccountId, Username = reply.AccountName },
                        new CharacterModel { Id = reply.CharacterGuid, Name = reply.CharacterName, ZoneId = reply.ZoneId });
            }));
        }

        /// <summary>
        /// The ticket and character were accepted: the zone back end of the character's landscape is asked to attach the
        /// connection. With none registered the client is told ConnectionIsNotRoutable (gateway-service.md §3.5 item 5)
        /// and the connection waits for one, as long as a hold lasts (<see cref="HoldLimit"/>).
        /// </summary>
        public override void OnCharacterLogin(CharacterModel model)
        {
            CharacterGuid = model.Id;
            CharacterName = model.Name ?? "";
            zoneId = model.ZoneId;
            worldId = ZoneBackEnds.World;
            waiting = true;
            if (!ZoneBackEnds.LoggedIn(this))
            {
                heldSince = DateTime.UtcNow;
                SendNotRoutable();
                // Once a zone has it, the client hears ChannelIsRoutable 1 for its channels (RefreshRoutes).
                Array.Fill(told, false);
                log.Warn($"{CharacterName}: no back end serves landscape {zoneId} on channel 0; ConnectionIsNotRoutable, "
                    + $"waiting up to {HoldLimit.TotalSeconds:0} s for one to register.");
            }
        }

        /// <summary>
        /// Attaching to <paramref name="target"/> (link or tick thread): once, and only while no zone has the connection.
        /// </summary>
        public bool TryAttachTo(BackEnd target)
        {
            lock (attachSync)
            {
                if (!waiting || Disconnected)
                    return false;
                zone = target;
                waiting = false;
                attachedSeen = false;
                routesDirty = true;
                return true;
            }
        }

        /// <summary>
        /// HandOver from its zone (link thread): the connection leaves that zone (which dropped the session) for the zone of
        /// <paramref name="world"/> serving <paramref name="landscape"/>; the next attach is a transfer. What the old zone
        /// queued is still sent (its last packets before the hand-over).
        /// </summary>
        public void BeginHandOver(uint world, uint landscape, ulong arriveNear = 0, ulong joinGame = 0, WorldSpot returnTo = default,
            WorldSpot arriveAt = default, CavernTripKind cavernTrip = CavernTripKind.None)
        {
            lock (attachSync)
            {
                transferPending = world != worldId;
                arriveNearPending = transferPending ? arriveNear : 0;
                joinGamePending = transferPending ? joinGame : 0;
                returnToPending = transferPending ? returnTo : default;
                arriveAtPending = transferPending ? arriveAt : default;
                cavernTripPending = transferPending ? cavernTrip : CavernTripKind.None;
                worldId = world;
                zoneId = landscape;
                zone = null;
                waiting = true;
                attachedSeen = false;
                attached = false;
                routesDirty = true;
            }
        }

        /// <summary>The transfer's "arrive beside this character" (Travel to Friend on another world), read once.</summary>
        private ulong arriveNearPending;

        /// <summary>The character the next transfer arrives beside (0 = none); read once.</summary>
        public ulong TakeArriveNear()
        {
            ulong near = arriveNearPending;
            arriveNearPending = 0;
            return near;
        }

        /// <summary>The transfer's site game to join (a Game Finder match on another world, L31), read once.</summary>
        private ulong joinGamePending;

        /// <summary>The site game the next transfer joins (0 = none); read once.</summary>
        public ulong TakeJoinGame()
        {
            ulong game = joinGamePending;
            joinGamePending = 0;
            return game;
        }

        /// <summary>The transfer's way back after its Game Finder game, and its arrival spot (back from one); L31.</summary>
        private WorldSpot returnToPending, arriveAtPending;

        /// <summary>Where the next transfer's player goes back to after the game (unset = stays); read once.</summary>
        public WorldSpot TakeReturnTo()
        {
            WorldSpot spot = returnToPending;
            returnToPending = default;
            return spot;
        }

        /// <summary>The spot the next transfer arrives at instead of the landscape's usual arrival (unset = usual); read once.</summary>
        public WorldSpot TakeArriveAt()
        {
            WorldSpot spot = arriveAtPending;
            arriveAtPending = default;
            return spot;
        }

        /// <summary>The transfer's Chaos Cavern trip (a Lost Crystal / Underground Adventure trip to another world, L131).</summary>
        private CavernTripKind cavernTripPending;

        /// <summary>The Chaos Cavern trip the next transfer arrives on (None = an ordinary arrival); read once.</summary>
        public CavernTripKind TakeCavernTrip()
        {
            CavernTripKind trip = cavernTripPending;
            cavernTripPending = CavernTripKind.None;
            return trip;
        }

        /// <summary>Whether the attach being sent is a cross-world transfer (read once).</summary>
        public bool TakeTransfer()
        {
            bool transfer = transferPending;
            transferPending = false;
            return transfer;
        }

        /// <summary>ConnectionAttached from its zone (link thread): the zone has the character, on landscape <paramref name="landscape"/>.</summary>
        public void Attached(uint landscape)
        {
            if (landscape != 0)
                zoneId = landscape;
            attachedSeen = true;
        }

        /// <summary>ZoneChanged from its zone (link thread): the character is on another landscape now.</summary>
        public void ZoneChangedTo(uint landscape)
        {
            zoneId = landscape;
            routesDirty = true;
        }

        /// <summary>The routing table changed (link thread): routes are looked at again on the next tick.</summary>
        public void RoutesChanged() => routesDirty = true;

        /// <summary>A packet a back end tunnelled for this client (link thread).</summary>
        public void FromZone(byte[] packet) => fromZone.Enqueue(packet);

        /// <summary>CloseConnection from a back end (link thread): closed on the next tick.</summary>
        public void RequestClose() => closeRequested = true;

        /// <summary>
        /// Its zone's link went away (link thread): held from the next tick. What the old zone queued but never got sent
        /// is of no use to the new one's client view.
        /// </summary>
        public void RequestHold()
        {
            lock (attachSync)
            {
                attachedSeen = false;
                holdRequested = true;
                waiting = true;
                routesDirty = true;
            }
            fromZone.Clear();
        }

        public override void OnTunnelData(Channel channel, byte[] data)
        {
            // ClientLogout 0x14 while the zone is away: nothing can answer it, and the client closes the connection after
            // it anyway (protocol.md §6): close now.
            if (Held && channel != Channel.PositionUpdate && data.Length >= 2 && data[0] == 0x14 && data[1] == 0x00)
            {
                log.Info($"{DisplayName} logged out while the zone was away; closed.");
                OnDisconnect();
                return;
            }
            // CharacterSelectSessionRequest 0xF6 (logout to character select, channel 1) while nothing can take channel 1
            // (zone held and no world-channel service): the gateway answers it with a relog session from the login
            // service. The client quits 60 s after sending 0xF6 without an answer (FUN_142d57e60 deadline +0x324a0;
            // run state 0x15 "Timed out while waiting for a relogin session.", agent-leads-sweep.md L61), and a held
            // packet would wait up to HoldLimit (120 s) for the zone.
            if (channel == Channel.World && data.Length >= 2 && data[0] == 0xF6 && data[1] == 0x00 && everAttached
                && !ChannelReady(channel) && Login != null && Account != null && !sentAway)
            {
                AnswerReloginHere(channel, data);
                return;
            }
            base.OnTunnelData(channel, data);
        }

        /// <summary>
        /// Answers a held logout to character select from the gateway: CharacterSelectSessionResponse 0xF7 {bool ok,
        /// string session} with the login service's relog session (as after <see cref="HoldLimit"/>); the client then
        /// leaves by itself. Without a session the request is held as before, for a zone or world-channel service that
        /// comes back in time.
        /// </summary>
        private void AnswerReloginHere(Channel channel, byte[] data)
        {
            sentAway = true;
            log.Info($"{DisplayName}: logout to character select while channel 1 has no back end; answered by the gateway.");
            EnqueueEvent(new TaskGenericEvent<RelogSessionReply>(Login.Relog(Account.Id), reply =>
            {
                if (reply == null)
                {
                    sentAway = false;
                    log.Warn($"{DisplayName}: no relog session from the login service; the logout waits for a back end.");
                    base.OnTunnelData(channel, data);
                    return;
                }
                string key = reply.SessionKey;
                var body = new System.Collections.Generic.List<byte> { 0xF7, 0x00, 1 };
                body.AddRange(BitConverter.GetBytes(key.Length));
                body.AddRange(System.Text.Encoding.ASCII.GetBytes(key));
                SendToClient(body.ToArray());
                closeAfter = DateTime.UtcNow.AddSeconds(30);
            }));
        }

        /// <summary>
        /// Whether a packet on <paramref name="channel"/> can go now: never before the zone first had the character (the
        /// world-channel service hands most of channel 1 on to the zone, which would not know the connection yet); then
        /// when the channel's back end is there: the zone only while it has the character (not held), a service of its
        /// own (e.g. channel 1 to the world-channel back end) even while the zone is away (§3.5 item 1).
        /// </summary>
        protected override bool ChannelReady(Channel channel) => everAttached && Routable((int)channel & 3);

        private bool Routable(int channel)
        {
            BackEnd target = routes[channel];
            return target != null && target.Alive && (target != zone || ZoneReady);
        }

        protected override void ToZone(Channel channel, byte[] data)
        {
            BackEnd target = routes[(int)channel & 3];
            bool sent = target != null && target.Link.Send(new TunnelFromExternal
            {
                ConnectionId = ConnectionId, CharacterGuid = CharacterGuid, Channel = (GatewayChannel)channel,
                Flags = 1, Packet = data
            });
            // Its back end closed just now: kept until the routes have caught up.
            if (!sent)
                HoldPacket(channel, data);
        }

        /// <summary>
        /// Tick: the routes from the routing table (channel 0 = the zone; 1-3 a registered service, else the zone), and
        /// ChannelIsRoutable to the client for every channel that changed between routable and not (channels 0, 1, 3;
        /// channel 2, movement, is never held and was never reported). Before the first attach the client has been told
        /// ConnectionIsNotRoutable instead, if anything.
        /// </summary>
        private void RefreshRoutes()
        {
            int version = ZoneBackEnds.Table.Version;
            if (!routesDirty && version == routesVersion)
                return;
            routesDirty = false;
            routesVersion = version;
            BackEnd z = zone;
            uint world = worldId;
            uint landscape = zoneId;
            routes[0] = z;
            for (int c = 1; c < 4; c++)
            {
                BackEnd target = ZoneBackEnds.Table.ServiceFor(world, (GatewayChannel)c, landscape) ?? z;
                if (target != routes[c] && everAttached)
                    log.Debug($"{DisplayName}: channel {c} -> {(target == null ? "nothing" : target == z ? "its zone" : target.ToString())}.");
                routes[c] = target;
            }
            if (!everAttached)
                return;
            foreach (int c in RoutableReported)
            {
                bool routable = Routable(c);
                if (routable == told[c])
                    continue;
                told[c] = routable;
                SendRoutable((Channel)c, routable);
                log.Info($"{DisplayName}: ChannelIsRoutable channel={c} isRoutable={(routable ? 1 : 0)}.");
            }
        }

        /// <summary>The channels ChannelIsRoutable is sent for: 0 Zone, 1 World, 3 (not 2, movement: never held).</summary>
        private static readonly int[] RoutableReported = { 0, 1, 3 };

        protected override void OnProcessZonePackets()
        {
            if (closeRequested)
            {
                OnDisconnect();
                return;
            }
            if (holdRequested)
            {
                holdRequested = false;
                attached = false;
                heldSince ??= DateTime.UtcNow;
                routesDirty = true;
                log.Info($"{DisplayName}: zone gone, connection held (up to {HoldLimit.TotalSeconds:0} s).");
            }
            if (heldSince is DateTime since && DateTime.UtcNow - since > HoldLimit && !sentAway)
            {
                sentAway = true;
                if (!everAttached)
                {
                    log.Warn($"{DisplayName}: no zone back end for landscape {zoneId} registered within {HoldLimit.TotalSeconds:0} s; closing.");
                    OnDisconnect();
                    return;
                }
                if (Login != null)
                {
                    log.Warn($"{DisplayName}: the zone did not come back within {HoldLimit.TotalSeconds:0} s; back to character select.");
                    EnqueueEvent(new TaskGenericEvent<RelogSessionReply>(Login.Relog(Account.Id), reply =>
                    {
                        if (reply == null)
                        {
                            OnDisconnect();
                            return;
                        }
                        string key = reply.SessionKey;
                        // CharacterSelectSessionResponse 0xF7 {bool success, string session}: the client takes it in any
                        // run state, disconnects by itself and logs in to the login server with the session (Shutdown.cs).
                        var body = new System.Collections.Generic.List<byte> { 0xF7, 0x00, 1 };
                        body.AddRange(BitConverter.GetBytes(key.Length));
                        body.AddRange(System.Text.Encoding.ASCII.GetBytes(key));
                        SendToClient(body.ToArray());
                        closeAfter = DateTime.UtcNow.AddSeconds(30);
                    }));
                }
                else
                {
                    log.Warn($"{DisplayName}: the zone did not come back within {HoldLimit.TotalSeconds:0} s; closing.");
                    OnDisconnect();
                    return;
                }
            }
            if (closeAfter is DateTime last && DateTime.UtcNow > last)
            {
                OnDisconnect();
                return;
            }
            if (attachedSeen && !attached)
            {
                bool first = !everAttached;
                attached = true;
                everAttached = true;
                routesDirty = true;
                if (heldSince != null)
                {
                    heldSince = null;
                    log.Info(first ? $"{DisplayName}: attached to {zone} after waiting for it." : $"{DisplayName}: re-attached to {zone}.");
                }
            }
            RefreshRoutes();
            if (HasHeldPackets)
                ReleaseEarlyPackets();
            while (CanProcessPackets && fromZone.TryDequeue(out byte[] packet))
                SendToClient(packet);
        }

        public override void OnDisconnect()
        {
            if (Disconnected)
                return;
            base.OnDisconnect();
            ZoneBackEnds.Closed(this);
        }

        /// <summary>
        /// The network manager dropped the session: the zone is told too if no OnDisconnect did (ZoneBackEnds.Closed runs
        /// once per connection), so no close path can leave the character standing in the zone.
        /// </summary>
        public override void OnRemoved() => ZoneBackEnds.Closed(this);
    }
}
