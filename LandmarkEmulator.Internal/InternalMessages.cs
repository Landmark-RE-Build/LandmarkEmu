using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.Internal
{
    /// <summary>An internal packet: its first byte is <see cref="Opcode"/>, written by <see cref="Write"/> after it.</summary>
    public interface IInternalMessage
    {
        InternalOpcode Opcode { get; }
        void Write(InternalWriter w);
    }

    /// <summary>Gateway channels as the client numbers them (trace/architecture/channels.md).</summary>
    public enum GatewayChannel : byte
    {
        Zone = 0,
        World = 1,
        PositionUpdate = 2,
        /// <summary>Build-cursor previews (WorldEditing 0x10C); the old name "Voxel" is not supported by the client.</summary>
        Channel3 = 3
    }

    /// <summary>
    /// Gateway::ExternalDestination: 12 bytes per recipient (the client's tracker counts 12 per destination in
    /// TunnelPacketToExternalConnectionList). Field layout ours: connection, channel, flags, two reserved bytes.
    /// </summary>
    public readonly record struct ExternalDestination(ulong ConnectionId, GatewayChannel Channel, byte Flags)
    {
        /// <summary>Flags: the packet must arrive (the client's reliable stream).</summary>
        public const byte Reliable = 1;
        // Flag 2 (ClientShape, "no 0.695 header translation") is gone with the translation (2026-10-17: the server
        // speaks the 0.695 numbering natively, docs/protocol.md §1.6); a gateway ignores it.

        public void Write(InternalWriter w) => w.U64(ConnectionId).U8((byte)Channel).U8(Flags).U16(0);
        public static ExternalDestination Read(InternalReader r)
        {
            var d = new ExternalDestination(r.U64(), (GatewayChannel)r.U8(), r.U8());
            r.U16();
            return d;
        }
    }

    /// <summary>
    /// 7 TunnelPacketFromExternalConnection (gateway → back end), 19-byte header: u8 op, u64 connection, u64 character,
    /// u8 channel, u8 flags (bit 0 reliable, bit 1 replayed after a hold); then the client packet as the client sent it.
    /// </summary>
    public sealed class TunnelFromExternal : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.TunnelFromExternal;
        public ulong ConnectionId { get; init; }
        public ulong CharacterGuid { get; init; }
        public GatewayChannel Channel { get; init; }
        public byte Flags { get; init; }
        public byte[] Packet { get; init; } = Array.Empty<byte>();

        public const byte Replayed = 2;

        public void Write(InternalWriter w) => w.U64(ConnectionId).U64(CharacterGuid).U8((byte)Channel).U8(Flags).Bytes(Packet);
        public static TunnelFromExternal Read(InternalReader r) => new()
        {
            ConnectionId = r.U64(), CharacterGuid = r.U64(), Channel = (GatewayChannel)r.U8(), Flags = r.U8(), Packet = r.Rest()
        };
    }

    /// <summary>8 TunnelPacketToExternalConnection (back end → gateway), 13-byte header: u8 op, destination; then the packet.</summary>
    public sealed class TunnelToExternal : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.TunnelToExternal;
        public ExternalDestination Destination { get; init; }
        public byte[] Packet { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w)
        {
            Destination.Write(w);
            w.Bytes(Packet);
        }

        public static TunnelToExternal Read(InternalReader r) => new() { Destination = ExternalDestination.Read(r), Packet = r.Rest() };
    }

    /// <summary>
    /// 0x16 TunnelPacketToExternalConnectionList (back end → gateway), header 11 + 12 per destination: u8 op, u8 channel,
    /// u8 flags, u32 packet length, u32 n (at offset 7, as the client's tracker reads it), n destinations; then the packet.
    /// One packet for every listed connection (a per-build body is sent as one list per layout).
    /// </summary>
    public sealed class TunnelToExternalList : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.TunnelToExternalList;
        public GatewayChannel Channel { get; init; }
        public byte Flags { get; init; }
        public IReadOnlyList<ExternalDestination> Destinations { get; init; } = Array.Empty<ExternalDestination>();
        public byte[] Packet { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w)
        {
            w.U8((byte)Channel).U8(Flags).U32((uint)Packet.Length).U32((uint)Destinations.Count);
            foreach (ExternalDestination d in Destinations)
                d.Write(w);
            w.Bytes(Packet);
        }

        public static TunnelToExternalList Read(InternalReader r)
        {
            var channel = (GatewayChannel)r.U8();
            byte flags = r.U8();
            uint length = r.U32();
            uint n = r.U32();
            if (n > 100_000)
                throw new InvalidDataException($"{n} destinations refused.");
            var list = new List<ExternalDestination>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(ExternalDestination.Read(r));
            return new() { Channel = channel, Flags = flags, Destinations = list, Packet = r.Bytes((int)length) };
        }
    }

    /// <summary>Back-end kinds a gateway routes to.</summary>
    public enum BackEndKind : byte { Zone = 1, WorldChannel = 2, Social = 3, EnvironmentServer = 4, Commerce = 5 }

    /// <summary>0x20 RegisterBackEnd (ours): a back end dialled the gateway and says what it serves.</summary>
    public sealed class RegisterBackEnd : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.RegisterBackEnd;
        public BackEndKind Kind { get; init; }
        public uint WorldId { get; init; }
        public IReadOnlyList<uint> ZoneIds { get; init; } = Array.Empty<uint>();
        /// <summary>Bit per <see cref="GatewayChannel"/> the back end takes.</summary>
        public byte ChannelMask { get; init; }
        public string Name { get; init; } = "";
        public uint BuildStamp { get; init; }
        /// <summary>
        /// Zones: the world id the client is told for this world (zone details, ClientBeginZoning +0xa8, the Landscapes
        /// window) and its name text — what other worlds' teleport windows list it as (cross-world travel, B.10).
        /// </summary>
        public uint ClientWorldId { get; init; }
        public uint WorldNameText { get; init; }

        public void Write(InternalWriter w)
        {
            w.U8((byte)Kind).U32(WorldId).U32((uint)ZoneIds.Count);
            foreach (uint z in ZoneIds)
                w.U32(z);
            w.U8(ChannelMask).Str(Name).U32(BuildStamp).U32(ClientWorldId).U32(WorldNameText);
        }

        public static RegisterBackEnd Read(InternalReader r)
        {
            var kind = (BackEndKind)r.U8();
            uint world = r.U32();
            uint n = r.U32();
            var zones = new List<uint>();
            for (uint i = 0; i < n && i < 4096; i++)
                zones.Add(r.U32());
            byte mask = r.U8();
            string name = r.Str();
            uint stamp = r.U32();
            // (Older back ends end here.)
            uint clientWorld = r.Remaining >= 8 ? r.U32() : 0, nameText = r.Remaining >= 4 ? r.U32() : 0;
            return new()
            {
                Kind = kind, WorldId = world, ZoneIds = zones, ChannelMask = mask, Name = name, BuildStamp = stamp,
                ClientWorldId = clientWorld, WorldNameText = nameText
            };
        }
    }

    /// <summary>
    /// A spot on a landscape of a world (ours; cross-world travel): u32 world (server id), u32 zone (landscape), f32 x, y, z
    /// (the zone's own coordinates). Zone 0 = none. Written as an optional trailing group of <see cref="HandOver"/> and
    /// <see cref="AttachConnection"/> (Game Finder trips, OPEN-LEADS L31).
    /// </summary>
    public readonly record struct WorldSpot(uint World, uint Zone, float X, float Y, float Z)
    {
        public bool IsSet => Zone != 0;
        public const int Size = 20;

        public void Write(InternalWriter w) => w.U32(World).U32(Zone).F32(X).F32(Y).F32(Z);
        /// <summary>The group, or none when the (older, shorter) message ends before it.</summary>
        public static WorldSpot Read(InternalReader r)
            => r.Remaining >= Size ? new WorldSpot(r.U32(), r.U32(), r.F32(), r.F32(), r.F32()) : default;
    }

    /// <summary>
    /// A Chaos Cavern trip that crosses to another world (ours; OPEN-LEADS L131, P5734: Lost Crystal trips to other
    /// worlds and biomes): the world it lands on picks the cavern itself (its own outposts, lockouts and
    /// graveyards). Written as an optional trailing byte of <see cref="HandOver"/> and <see cref="AttachConnection"/>.
    /// </summary>
    public enum CavernTripKind : byte
    {
        None = 0,
        /// <summary>A Lost Crystal's "Eureka!" trip: to an outpost's cavern there (Outposts.ArriveFromOtherWorld).</summary>
        LostCrystal = 1,
        /// <summary>Travel → An Underground Adventure (or following a group member's): a cavern graveyard there (Caves).</summary>
        UndergroundAdventure = 2
    }

    /// <summary>0x21 AttachConnection (ours, gateway → back end): a client logged in to the gateway (or a back end came back).</summary>
    public sealed class AttachConnection : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.AttachConnection;
        public ulong ConnectionId { get; init; }
        public ulong CharacterGuid { get; init; }
        public ulong AccountId { get; init; }
        public string AccountName { get; init; } = "";
        public string ClientBuild { get; init; } = "";
        public string Ticket { get; init; } = "";
        public bool IsRelog { get; init; }
        public bool IsReattach { get; init; }
        public uint ZoneId { get; init; }
        /// <summary>
        /// The connection comes from another world's zone (HandOver with a target world, cross-world travel B.10): the
        /// character was saved there at its arrival spot, and this zone re-zones the client there at once.
        /// </summary>
        public bool IsTransfer { get; init; }
        /// <summary>
        /// A transfer that travels to a friend (Travel to Friend on another world, TASKS E.13): the zone places the arrival
        /// beside this character if they are there; 0 = the landscape's usual arrival. Optional trailing field.
        /// </summary>
        public ulong ArriveNear { get; init; }
        /// <summary>
        /// A transfer that joins a site game (a Game Finder match on another world, OPEN-LEADS L31): the zone re-zones the
        /// client to that game's build site and joins it (InstancedGames.ArriveToJoin); 0 = none. Optional trailing field.
        /// </summary>
        public ulong JoinGame { get; init; }
        /// <summary>
        /// A transfer for a Game Finder match whose player goes back afterwards (setting FinderReturnHome on the world they
        /// came from): the world, landscape and spot to take them back to when they leave the game. Optional trailing group.
        /// </summary>
        public WorldSpot ReturnTo { get; init; }
        /// <summary>
        /// A transfer that arrives at this spot of the landscape instead of its usual arrival (the way back from a Game
        /// Finder trip). Optional trailing group.
        /// </summary>
        public WorldSpot ArriveAt { get; init; }
        /// <summary>
        /// A transfer that is a Chaos Cavern trip (L131): the zone picks a cavern of its own for the arrival instead of the
        /// landscape's usual arrival. Optional trailing byte (after the two spots, which are therefore always written).
        /// </summary>
        public CavernTripKind CavernTrip { get; init; }

        public void Write(InternalWriter w)
        {
            w.U64(ConnectionId).U64(CharacterGuid).U64(AccountId).Str(AccountName).Str(ClientBuild)
                .Str(Ticket).Bool(IsRelog).Bool(IsReattach).U32(ZoneId).Bool(IsTransfer).U64(ArriveNear).U64(JoinGame);
            ReturnTo.Write(w);
            ArriveAt.Write(w);
            w.U8((byte)CavernTrip);
        }
        public static AttachConnection Read(InternalReader r)
        {
            ulong connection = r.U64(), character = r.U64(), account = r.U64();
            string accountName = r.Str(), build = r.Str(), ticket = r.Str();
            bool relog = r.Bool(), reattach = r.Bool();
            uint zone = r.U32();
            bool transfer = r.Remaining > 0 && r.Bool();
            ulong near = r.Remaining >= 8 ? r.U64() : 0;
            ulong join = r.Remaining >= 8 ? r.U64() : 0;
            WorldSpot returnTo = WorldSpot.Read(r), arriveAt = WorldSpot.Read(r);
            var trip = r.Remaining >= 1 ? (CavernTripKind)r.U8() : CavernTripKind.None;
            return new AttachConnection
            {
                ConnectionId = connection, CharacterGuid = character, AccountId = account, AccountName = accountName,
                ClientBuild = build, Ticket = ticket, IsRelog = relog, IsReattach = reattach, ZoneId = zone,
                IsTransfer = transfer, ArriveNear = near, JoinGame = join, ReturnTo = returnTo, ArriveAt = arriveAt,
                CavernTrip = trip
            };
        }
    }

    /// <summary>0x22 ConnectionAttached (ours, back end → gateway): the zone has the character loaded.</summary>
    public sealed class ConnectionAttached : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.ConnectionAttached;
        public ulong ConnectionId { get; init; }
        public uint ZoneId { get; init; }
        public uint TransientId { get; init; }
        public float OffsetX { get; init; }
        public float OffsetY { get; init; }
        public float OffsetZ { get; init; }
        public bool Admin { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }

        public void Write(InternalWriter w) => w.U64(ConnectionId).U32(ZoneId).U32(TransientId).F32(OffsetX).F32(OffsetY).F32(OffsetZ)
            .Bool(Admin).F32(X).F32(Y).F32(Z);
        public static ConnectionAttached Read(InternalReader r) => new()
        {
            ConnectionId = r.U64(), ZoneId = r.U32(), TransientId = r.U32(), OffsetX = r.F32(), OffsetY = r.F32(), OffsetZ = r.F32(),
            Admin = r.Bool(), X = r.F32(), Y = r.F32(), Z = r.F32()
        };
    }

    /// <summary>Why a client connection ended.</summary>
    public enum CloseReason : byte { Logout = 0, Timeout = 1, Kicked = 2, Disconnected = 3, GatewayStopping = 4, HandedOver = 5 }

    /// <summary>0x23 ConnectionClosed (ours, gateway → back end).</summary>
    public sealed class ConnectionClosed : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.ConnectionClosed;
        public ulong ConnectionId { get; init; }
        public CloseReason Reason { get; init; }

        public void Write(InternalWriter w) => w.U64(ConnectionId).U8((byte)Reason);
        public static ConnectionClosed Read(InternalReader r) => new() { ConnectionId = r.U64(), Reason = (CloseReason)r.U8() };
    }

    /// <summary>0x24 CloseConnection (ours, back end → gateway): close this client (logout, kick, shutdown).</summary>
    public sealed class CloseConnection : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.CloseConnection;
        public ulong ConnectionId { get; init; }
        public uint DelayMs { get; init; }
        public bool SendForcedLogout { get; init; }
        public string Reason { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(ConnectionId).U32(DelayMs).Bool(SendForcedLogout).Str(Reason.Length > 64 ? Reason[..64] : Reason);
        public static CloseConnection Read(InternalReader r) => new() { ConnectionId = r.U64(), DelayMs = r.U32(), SendForcedLogout = r.Bool(), Reason = r.Str() };
    }

    /// <summary>
    /// 0x25 HandOver (ours, back end → gateway): move a connection to the zone process serving another landscape, or —
    /// with <see cref="TargetWorldId"/> — to another world's zone on the same gateway (cross-world travel, B.10). The
    /// sending zone has saved the character at the arrival spot and dropped the session before it sends this.
    /// </summary>
    public sealed class HandOver : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.HandOver;
        public ulong ConnectionId { get; init; }
        public uint TargetZoneId { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public uint StateToken { get; init; }
        /// <summary>The world (server id) the connection goes to; 0 = the same world.</summary>
        public uint TargetWorldId { get; init; }
        /// <summary>Arrive beside this character there (Travel to Friend on another world); 0 = the usual arrival.</summary>
        public ulong ArriveNear { get; init; }
        /// <summary>Join this site game there (a Game Finder match on another world, L31); 0 = none.</summary>
        public ulong JoinGame { get; init; }
        /// <summary>With <see cref="JoinGame"/>: where to take the player back to after the game (FinderReturnHome); unset = stay.</summary>
        public WorldSpot ReturnTo { get; init; }
        /// <summary>Arrive at this spot of the target landscape instead of its usual arrival (back from a Game Finder trip).</summary>
        public WorldSpot ArriveAt { get; init; }
        /// <summary>A Chaos Cavern trip to another world (L131): the target zone picks the cavern. Optional trailing byte.</summary>
        public CavernTripKind CavernTrip { get; init; }

        public void Write(InternalWriter w)
        {
            w.U64(ConnectionId).U32(TargetZoneId).F32(X).F32(Y).F32(Z).U32(StateToken).U32(TargetWorldId).U64(ArriveNear).U64(JoinGame);
            ReturnTo.Write(w);
            ArriveAt.Write(w);
            w.U8((byte)CavernTrip);
        }

        public static HandOver Read(InternalReader r) => new()
        {
            ConnectionId = r.U64(), TargetZoneId = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(), StateToken = r.U32(),
            TargetWorldId = r.Remaining >= 4 ? r.U32() : 0,
            ArriveNear = r.Remaining >= 8 ? r.U64() : 0,
            JoinGame = r.Remaining >= 8 ? r.U64() : 0,
            ReturnTo = WorldSpot.Read(r),
            ArriveAt = WorldSpot.Read(r),
            CavernTrip = r.Remaining >= 1 ? (CavernTripKind)r.U8() : CavernTripKind.None
        };
    }

    /// <summary>
    /// 0x29 WorldsReachable (ours, gateway → zone back ends): every world with a zone registered on this gateway, with
    /// the id and name text its clients are told — the worlds a zone may list in the Landscapes window and hand a
    /// connection to (cross-world travel, B.10). Sent on every change of the list.
    /// </summary>
    public sealed class WorldsReachable : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.WorldsReachable;
        public IReadOnlyList<(uint World, uint ClientWorldId, uint NameText)> Worlds { get; init; } = Array.Empty<(uint, uint, uint)>();

        public void Write(InternalWriter w)
        {
            w.U32((uint)Worlds.Count);
            foreach ((uint world, uint clientWorld, uint nameText) in Worlds)
                w.U32(world).U32(clientWorld).U32(nameText);
        }

        public static WorldsReachable Read(InternalReader r)
        {
            var worlds = new List<(uint, uint, uint)>();
            for (uint n = r.U32(); n > 0 && worlds.Count < 1024; n--)
                worlds.Add((r.U32(), r.U32(), r.U32()));
            return new WorldsReachable { Worlds = worlds };
        }
    }

    /// <summary>0x26 PositionCopy (ours, gateway → back end): the newest movement of a connection (coalesced).</summary>
    public sealed class PositionCopy : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.PositionCopy;
        public ulong ConnectionId { get; init; }
        public byte[] Movement { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w) => w.U64(ConnectionId).Bytes(Movement);
        public static PositionCopy Read(InternalReader r) => new() { ConnectionId = r.U64(), Movement = r.Rest() };
    }

    /// <summary>0x27 ZoneChanged (ours, back end → gateway): the connection is now on another landscape (fan-out interest).</summary>
    public sealed class ZoneChanged : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.ZoneChanged;
        public ulong ConnectionId { get; init; }
        public uint ZoneId { get; init; }
        public float OffsetX { get; init; }
        public float OffsetY { get; init; }
        public float OffsetZ { get; init; }

        public void Write(InternalWriter w) => w.U64(ConnectionId).U32(ZoneId).F32(OffsetX).F32(OffsetY).F32(OffsetZ);
        public static ZoneChanged Read(InternalReader r) => new() { ConnectionId = r.U64(), ZoneId = r.U32(), OffsetX = r.F32(), OffsetY = r.F32(), OffsetZ = r.F32() };
    }

    /// <summary>0x28 Ping (ours, both ways): a link keep-alive; the other side answers with the same timestamp.</summary>
    public sealed class Ping : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.Ping;
        public ulong Timestamp { get; init; }

        public void Write(InternalWriter w) => w.U64(Timestamp);
        public static Ping Read(InternalReader r) => new() { Timestamp = r.U64() };
    }

    /// <summary>
    /// 0x30 ServicePacket (ours): a service-to-service message in the client's family / sub numbering (u16 family, u32
    /// sub, body), e.g. Mail 0xF8 / 0x11 ConsumeAttachmentZoneRequest; each owning service's spec documents the body.
    /// </summary>
    public sealed class ServicePacket : IInternalMessage
    {
        public InternalOpcode Opcode => InternalOpcode.ServicePacket;
        public ushort Family { get; init; }
        public uint Sub { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w) => w.U16(Family).U32(Sub).Bytes(Body);
        public static ServicePacket Read(InternalReader r) => new() { Family = r.U16(), Sub = r.U32(), Body = r.Rest() };
    }

    /// <summary>Serialising and parsing internal packets (without the frame's length prefix).</summary>
    public static class InternalMessages
    {
        public static byte[] Serialise(IInternalMessage message)
        {
            var w = new InternalWriter();
            w.U8((byte)message.Opcode);
            message.Write(w);
            return w.ToArray();
        }

        public static IInternalMessage Parse(byte[] packet)
        {
            if (packet.Length == 0)
                throw new InvalidDataException("Empty internal packet.");
            var r = new InternalReader(packet, 1);
            return (InternalOpcode)packet[0] switch
            {
                InternalOpcode.TunnelFromExternal => TunnelFromExternal.Read(r),
                InternalOpcode.TunnelToExternal => TunnelToExternal.Read(r),
                InternalOpcode.TunnelToExternalList => TunnelToExternalList.Read(r),
                InternalOpcode.RegisterBackEnd => RegisterBackEnd.Read(r),
                InternalOpcode.AttachConnection => AttachConnection.Read(r),
                InternalOpcode.ConnectionAttached => ConnectionAttached.Read(r),
                InternalOpcode.ConnectionClosed => ConnectionClosed.Read(r),
                InternalOpcode.CloseConnection => CloseConnection.Read(r),
                InternalOpcode.HandOver => HandOver.Read(r),
                InternalOpcode.PositionCopy => PositionCopy.Read(r),
                InternalOpcode.ZoneChanged => ZoneChanged.Read(r),
                InternalOpcode.Ping => Ping.Read(r),
                InternalOpcode.WorldsReachable => WorldsReachable.Read(r),
                InternalOpcode.ServicePacket => ServicePacket.Read(r),
                _ => throw new InvalidDataException($"Unknown internal opcode 0x{packet[0]:X2}.")
            };
        }
    }
}
