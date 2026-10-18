using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network;
using LandmarkEmulator.Gateway.Network.Message;
using System;
using System.Collections.Concurrent;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// The gateway half of a client connection while the gateway runs inside the world server (the default; with
    /// GATEWAY_INTERNAL_ADDRESS set the gateway is its own process and <see cref="GatewayLink"/> stands in for this).
    /// The SOE session, gateway login, 0.695 translation and flood limits are <see cref="ZoneGatewaySession"/>'s; this
    /// class only hands the result to its <see cref="Zone"/> session in the same process. The zone reaches it only
    /// through <see cref="IClientLink"/>, the seam the out-of-process gateway replaces (gateway-service.md §5).
    /// </summary>
    public sealed class ClientSession : ZoneGatewaySession, IClientLink
    {
        /// <summary>The character's zone session, once the login has loaded it.</summary>
        public WorldSession Zone { get; private set; }

        public ClientSession() : base(WorldServer.EncryptionKey)
        {
        }

        protected override string DisplayName => Zone?.CharacterName ?? base.DisplayName;

        protected override bool ZoneReady => Zone != null;

        /// <summary>The login was accepted and the character loaded: its zone session starts (AttachConnection).</summary>
        public override void OnCharacterLogin(CharacterModel model)
        {
            Zone = new WorldSession(this, Account, ClientBuild, ClientProtocol, LoginTicket);
            ZoneSessions.Add(Zone);
            Zone.OnCharacterLogin(model);
            ReleaseEarlyPackets();
        }

        /// <summary>
        /// The client's packets for the zone, waiting for the world thread. They arrive on the UDP receive thread; handled
        /// there (as before), every zone handler ran beside the world tick — e.g. a finished craft (Crafting.Update →
        /// GiveItem) and the player's own bag request (move, split, salvage) changed the same unlocked inventory lists at
        /// once, which can lose or duplicate items or throw half-way through a change. Queued and run by
        /// <see cref="OnProcessZonePackets"/> on the world thread, in the client's order, as the separate gateway's
        /// packets always were (<see cref="GatewayLink"/>: "game code never sees another thread").
        /// </summary>
        private readonly ConcurrentQueue<(Channel Channel, byte[] Data)> fromClient = new();

        protected override void ToZone(Channel channel, byte[] data) => fromClient.Enqueue((channel, data));

        /// <summary>World thread (this session's tick): the client's queued packets to the zone, then the zone's packets out.</summary>
        protected override void OnProcessZonePackets()
        {
            WorldSession zone = Zone;
            if (zone == null)
                return;
            while (fromClient.TryDequeue(out var packet))
            {
                try
                {
                    zone.FromClient(packet.Channel, packet.Data);
                }
                catch (Exception e)
                {
                    // One bad packet must not stop the rest of this tick's network update (every session's).
                    log.Error(e, $"Client packet from {zone.CharacterName} failed.");
                }
            }
            zone.Flush();
        }

        /// <summary>
        /// The connection ends. The zone session hears of it (<see cref="WorldSession.LinkClosed"/>: the player leaves the
        /// world and is saved) in <see cref="OnRemoved"/> on the world thread, not here: a client's own logout or quit
        /// (its SOE Disconnect) lands here on the UDP receive thread, and the leave clean-up (PlayerManager.Leave and
        /// every module's Forget) then ran beside the world tick. Until the tick removes the session it is Disconnected,
        /// which the zone side already treats as gone (Flush sends nothing, PlayerManager prunes it).
        /// </summary>
        public override void OnDisconnect()
        {
            if (Disconnected)
                return;
            base.OnDisconnect();
        }

        /// <summary>The server closed the connection itself (logout, kick, duplicate login): see <see cref="OnRemoved"/>.</summary>
        private volatile bool closedByServer;

        /// <summary>
        /// The network manager dropped the session (world thread): the zone session's leave. The client's packets still in
        /// <see cref="fromClient"/> are handled first, in order, on this thread: a session that is Disconnected gets no
        /// more Update (NetworkManager.Update sends it straight to removal), so whatever arrived since the last tick —
        /// typically the client's last packets, sent back to back with its SOE Disconnect, such as the Exit Game
        /// preference save — would otherwise be lost (review 2026-10-18b #1). The separate gateway already keeps this
        /// order (<see cref="GatewayLink"/> queues ConnectionClosed behind the connection's packets). Not after a close by
        /// the server: the session was ended on purpose (a kicked older session has already left the world, and its
        /// packets would act on its stale state). NetworkManager.Shutdown calls this too, so a shutdown drains as well.
        /// </summary>
        public override void OnRemoved()
        {
            WorldSession zone = Zone;
            if (zone == null)
                return;
            if (!closedByServer)
            {
                while (fromClient.TryDequeue(out var packet))
                {
                    try
                    {
                        zone.FromClientBeforeClose(packet.Channel, packet.Data);
                    }
                    catch (Exception e)
                    {
                        log.Error(e, $"Client packet from {zone.CharacterName} failed.");
                    }
                }
            }
            fromClient.Clear();
            zone.LinkClosed();
        }

        // ---- IClientLink -------------------------------------------------------------------------------------------------

        bool IClientLink.CanSend => CanProcessPackets;

        bool IClientLink.Closed => Disconnected;

        void IClientLink.Send(byte[] packet) => SendToClient(packet);

        void IClientLink.Close()
        {
            closedByServer = true;
            OnDisconnect();
        }
    }
}
