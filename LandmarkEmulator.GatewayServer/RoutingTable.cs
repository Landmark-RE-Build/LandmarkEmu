using LandmarkEmulator.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.GatewayServer
{
    /// <summary>
    /// One back end that dialled the gateway and registered (RegisterBackEnd 0x20, trace/architecture/gateway-service.md
    /// §3.4): what it is (<see cref="Kind"/>), which world, which landscapes (<see cref="ZoneIds"/>, empty = every
    /// landscape of the world) and which client channels it takes (<see cref="ChannelMask"/>, bit per channel).
    /// </summary>
    public sealed class BackEnd
    {
        private static long nextSequence;

        public BackEnd(InternalLink link, RegisterBackEnd register)
        {
            Link = link;
            Kind = register.Kind;
            WorldId = register.WorldId;
            ClientWorldId = register.ClientWorldId;
            WorldNameText = register.WorldNameText;
            ZoneIds = new HashSet<uint>(register.ZoneIds);
            ChannelMask = register.ChannelMask;
            Name = register.Name ?? "";
            Sequence = Interlocked.Increment(ref nextSequence);
        }

        public InternalLink Link { get; }
        public BackEndKind Kind { get; }
        public uint WorldId { get; }
        /// <summary>Zones: the world id its clients are told and the world's name text (cross-world travel, B.10).</summary>
        public uint ClientWorldId { get; }
        public uint WorldNameText { get; }
        /// <summary>The landscapes it serves; empty = all of its world's.</summary>
        public IReadOnlySet<uint> ZoneIds { get; }
        public byte ChannelMask { get; }
        public string Name { get; }
        /// <summary>Registration order: of two equal candidates the newer one wins.</summary>
        public long Sequence { get; }

        /// <summary>Its link is still open (it may close on another thread at any time).</summary>
        public bool Alive => Link.IsOpen;

        public bool Takes(GatewayChannel channel) => (ChannelMask & (1 << (int)channel)) != 0;

        public bool Serves(uint zoneId) => ZoneIds.Count == 0 || ZoneIds.Contains(zoneId);

        /// <summary>Same role as <paramref name="other"/> (a restarted copy of it): it replaces the old registration.</summary>
        public bool SameRole(BackEnd other) => Kind == other.Kind && WorldId == other.WorldId && ChannelMask == other.ChannelMask
            && ZoneIds.SetEquals(other.ZoneIds);

        public override string ToString()
        {
            string zones = ZoneIds.Count == 0 ? "every landscape" : $"landscapes {string.Join(",", ZoneIds.OrderBy(z => z))}";
            var channels = Enumerable.Range(0, 4).Where(c => (ChannelMask & (1 << c)) != 0).Select(c => c.ToString());
            return $"{Kind} '{Name}' (world {WorldId}, {zones}, channels {string.Join("/", channels)}) from {Link.Remote}";
        }
    }

    /// <summary>
    /// The gateway's routing table (gateway-service.md §3.3), filled only from registrations: (world, channel) → the back
    /// ends that registered for that channel of that world, removed when their link closes. A connection asks it
    /// <see cref="ZoneFor"/> (channel 0: the zone process holding its landscape, the one it is attached to) and
    /// <see cref="ServiceFor"/> (channels 1-3: a dedicated service such as the world-channel back end, when one is
    /// registered for the channel; the connection falls back to its zone otherwise). Connections cache the answers and
    /// look again only when <see cref="Version"/> changes, so the per-packet path takes no lock.
    /// </summary>
    public sealed class RoutingTable
    {
        private readonly object sync = new();
        private readonly Dictionary<(uint World, GatewayChannel Channel), List<BackEnd>> byChannel = new();
        private readonly List<BackEnd> all = new();
        private int version;

        /// <summary>Changes whenever a back end is added or removed.</summary>
        public int Version => Volatile.Read(ref version);

        /// <summary>
        /// Adds a registration. A live back end with the same role (kind, world, landscapes, channels) is replaced: it is
        /// a restarted copy whose old link hasn't been seen to close yet; the replaced ones are returned for closing.
        /// </summary>
        public List<BackEnd> Add(BackEnd backEnd)
        {
            List<BackEnd> replaced;
            lock (sync)
            {
                replaced = all.Where(b => b.Link != backEnd.Link && b.SameRole(backEnd)).ToList();
                foreach (BackEnd old in replaced)
                    RemoveLocked(old);
                all.Add(backEnd);
                for (int c = 0; c < 4; c++)
                {
                    var channel = (GatewayChannel)c;
                    if (!backEnd.Takes(channel))
                        continue;
                    if (!byChannel.TryGetValue((backEnd.WorldId, channel), out List<BackEnd> list))
                        byChannel[(backEnd.WorldId, channel)] = list = new List<BackEnd>();
                    list.Add(backEnd);
                }
                Interlocked.Increment(ref version);
            }
            return replaced;
        }

        /// <summary>Removes the back end registered on <paramref name="link"/>; null when none was.</summary>
        public BackEnd Remove(InternalLink link)
        {
            lock (sync)
            {
                BackEnd backEnd = all.FirstOrDefault(b => b.Link == link);
                if (backEnd != null)
                {
                    RemoveLocked(backEnd);
                    Interlocked.Increment(ref version);
                }
                return backEnd;
            }
        }

        private void RemoveLocked(BackEnd backEnd)
        {
            all.Remove(backEnd);
            foreach (List<BackEnd> list in byChannel.Values)
                list.Remove(backEnd);
        }

        /// <summary>
        /// Channel 0: the zone back end for <paramref name="zoneId"/> of <paramref name="world"/> — one that registered
        /// channel 0 and serves that landscape, a back end naming the landscape before a catch-all one, the newest first.
        /// </summary>
        public BackEnd ZoneFor(uint world, uint zoneId)
        {
            lock (sync)
                return Best(world, GatewayChannel.Zone, zoneId, b => true);
        }

        /// <summary>
        /// Channels 1-3: a service (any kind but a zone) registered for <paramref name="channel"/> of
        /// <paramref name="world"/> serving <paramref name="zoneId"/>, or null: the connection's own zone takes the
        /// channel then (§3.3: channel 1 to the zone until a world-channel back end exists; channels 2 and 3 to the zone).
        /// A zone back end's channel bits for 1-3 only say it can take them for its own connections.
        /// </summary>
        public BackEnd ServiceFor(uint world, GatewayChannel channel, uint zoneId)
        {
            lock (sync)
                return Best(world, channel, zoneId, b => b.Kind != BackEndKind.Zone);
        }

        private BackEnd Best(uint world, GatewayChannel channel, uint zoneId, Func<BackEnd, bool> filter)
        {
            if (!byChannel.TryGetValue((world, channel), out List<BackEnd> list))
                return null;
            BackEnd best = null;
            foreach (BackEnd b in list)
            {
                if (!b.Alive || !filter(b) || !b.Serves(zoneId))
                    continue;
                if (best == null || Better(b, best, zoneId))
                    best = b;
            }
            return best;
        }

        /// <summary>Naming the landscape beats serving every landscape; then the newer registration.</summary>
        private static bool Better(BackEnd a, BackEnd b, uint zoneId)
        {
            bool specificA = a.ZoneIds.Contains(zoneId), specificB = b.ZoneIds.Contains(zoneId);
            return specificA != specificB ? specificA : a.Sequence > b.Sequence;
        }

        /// <summary>Every registration (for logs).</summary>
        public List<BackEnd> All()
        {
            lock (sync)
                return all.ToList();
        }
    }
}
