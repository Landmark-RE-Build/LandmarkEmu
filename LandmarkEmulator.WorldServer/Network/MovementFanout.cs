using LandmarkEmulator.Internal;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// One movement packet to many players (trace/architecture/gateway-service.md §3.8; internal opcode 0x16
    /// TunnelPacketToExternalConnectionList, live's own message: internal-transport.md). Movement is most of what a zone
    /// sends: each player's PositionUpdate goes to everyone else on the landscape, so with n players the zone used to
    /// send n × (n − 1) tunnel packets a tick, each the same bytes. Now every (mover, update kind) collects its viewers
    /// on the separate gateway between ticks and goes out once with their connection ids in the list; the gateway
    /// copies it to each (ZoneBackEnds: FromZone per destination, in that client's header shapes). Only the newest
    /// movement of each kind is kept, as the per-session queue did (<see cref="WorldSession.QueueMovement"/>).
    /// Viewers whose client is served in this process (no separate gateway) keep the per-session queue: there is no
    /// link to fan out on. Sent after the sessions' own queues each tick (<see cref="GatewayLink"/>): positions for an
    /// entity a viewer hasn't been sent yet are dropped by the client and the next ones come a tick later, so the
    /// order against spawns does not matter. Counted once per packet in OpcodeStats (bytes on the internal link).
    /// <para>
    /// Each mover's kinds go out in body-time order (<see cref="InTimeOrder"/>; OPEN-LEADS L179, agent-leadsB.md §4.5),
    /// here and in the per-session flush: the observer's history push (0.695 FUN_1435a4430) resets an entity's history to
    /// a record older than its newest, so a position-only body (type 2) of time c sent before a full body (type 0) of
    /// time b &lt; c snapped the mover back by a tick's motion. First-queued order allowed that.
    /// </para>
    /// </summary>
    public static class MovementFanout
    {
        private sealed class Pending
        {
            public byte[] Movement;
            public readonly HashSet<ulong> Receivers = new();
        }

        /// <summary>(transient id, update kind = first body byte) → newest movement and its gateway viewers.</summary>
        private static readonly Dictionary<(uint, byte), Pending> pending = new();
        private static readonly object sync = new();

        /// <summary>
        /// Queues another entity's movement for a viewer: into this tick's list when the viewer is on the separate
        /// gateway, else into the viewer's own queue.
        /// </summary>
        public static void Queue(WorldSession viewer, uint transientId, byte[] movement)
        {
            if (viewer.Link is RemoteClientLink remote && !remote.Closed && movement.Length > 0)
            {
                lock (sync)
                {
                    var key = (transientId, movement[0]);
                    if (!pending.TryGetValue(key, out Pending p))
                        pending[key] = p = new Pending();
                    p.Movement = movement;
                    p.Receivers.Add(remote.ConnectionId);
                }
                return;
            }
            viewer.QueueMovement(transientId, movement);
        }

        /// <summary>Sends this tick's lists (one 0x16 per mover and kind). Called once per gateway tick after the sessions flushed.</summary>
        public static void Flush()
        {
            List<KeyValuePair<(uint, byte), Pending>> items;
            lock (sync)
            {
                if (pending.Count == 0)
                    return;
                items = pending.ToList();
                pending.Clear();
            }
            if (!GatewayLink.Connected)
                return;
            byte[] opcode = WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PlayerUpdatePosition);
            bool prepend = ZoneMessageManager.Instance.HasPrependedSize(ZoneMessageOpcode.PlayerUpdatePosition);
            foreach (((uint transientId, _), Pending p) in InTimeOrder(items, p => p.Movement))
            {
                var body = new ZonePacket(ZoneMessageOpcode.PlayerUpdatePosition,
                    new PlayerUpdatePosition { TransientId = transientId, Movement = p.Movement }, prepend);
                var bytes = new byte[opcode.Length + body.Data.Length];
                opcode.CopyTo(bytes, 0);
                body.Data.CopyTo(bytes, opcode.Length);
                OpcodeStats.Sent(bytes);
                GatewayLink.Send(new TunnelToExternalList
                {
                    Channel = GatewayChannel.Zone,
                    Flags = ExternalDestination.Reliable,
                    Destinations = p.Receivers.Select(id => new ExternalDestination(id, GatewayChannel.Zone, ExternalDestination.Reliable)).ToList(),
                    Packet = bytes
                });
            }
        }

        /// <summary>
        /// A movement body's time: the u32 after its type byte (the client's serializer FUN_142c141c0 writes u8 type,
        /// u32 time, u8, then the type's fields; the server's own bodies, Creatures.MovementBody, have the same head) [P].
        /// 0 for a body too short to have one.
        /// </summary>
        public static uint TimeOf(byte[] movement) => movement != null && movement.Length >= 5 ? BitConverter.ToUInt32(movement, 1) : 0u;

        /// <summary>
        /// The pending (mover, kind) bodies in send order: movers in first-queued order, each mover's bodies oldest first
        /// by <see cref="TimeOf"/> (compared as a signed difference from the mover's first body, so a u32 wrap inside one
        /// tick still sorts right; equal times keep their queued order). A newer position-only body then never goes out
        /// before an older full one (see the class notes).
        /// </summary>
        public static List<KeyValuePair<(uint, byte), T>> InTimeOrder<T>(IEnumerable<KeyValuePair<(uint, byte), T>> items, Func<T, byte[]> movement)
            => items.GroupBy(i => i.Key.Item1).SelectMany(mover =>
            {
                uint first = TimeOf(movement(mover.First().Value));
                return mover.OrderBy(i => unchecked((int)(TimeOf(movement(i.Value)) - first)));
            }).ToList();
    }
}
