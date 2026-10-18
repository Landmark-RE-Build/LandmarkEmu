using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// Groups (parties) across worlds between the zones and the shared social service (TASKS B.9; patch note P4906,
    /// 26 Apr 2016: a group member can be removed from another world;
    /// trace/architecture/social-service.md §10). Family GroupsBase 0x2B, the client's own family for groups:
    /// <list type="bullet">
    /// <item><b>Client requests handed on</b> (<see cref="ClientRequestForward"/>, sub = the client's u8 sub, body after it
    /// unchanged): Invite 1, Join 2, Leave 4, Kick 5, Disband 6, SetGroupOwner 8. The client sends them on the Zone
    /// channel (channels-589.tsv / channels-695.tsv: GroupPacketInvite / Join / Leave / Kick / Disband /
    /// SetGroupOwner all Zone), so, like mail, the zone receives them and hands them on.</item>
    /// <item><see cref="GroupMemberRow"/> 0x2B / 0x10001 (ours), zone → social: a member's party-frame record.</item>
    /// <item><see cref="GroupStateForZone"/> 0x2B / 0x10002 (ours), social → zone: which group a character hosted by the
    /// zone is in.</item>
    /// </list>
    /// Live's server-side group messages are not named in the client's id table (L48), so both bodies are ours; next
    /// free sub 0x10003 (internal-messages.md §2).
    /// </summary>
    public static class GroupMessages
    {
        /// <summary>GroupsBase (the client's group family).</summary>
        public const ushort GroupFamily = 0x2B;
        public const uint MemberRowSub = 0x10001, StateForZoneSub = 0x10002;

        /// <summary>The client subs a zone hands on (GroupPackets: Invite 1, Join 2, Leave 4, Kick 5, Disband 6, SetGroupOwner 8).</summary>
        public static bool IsForwardedSub(uint sub) => sub is 1 or 2 or 4 or 5 or 6 or 8;

        /// <summary>The group message in a service packet of family 0x2B, or null.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            return packet.Sub switch
            {
                MemberRowSub => GroupMemberRow.Read(r),
                StateForZoneSub => GroupStateForZone.Read(r),
                _ when IsForwardedSub(packet.Sub) => ClientRequestForward.Read(packet.Sub, r, GroupFamily),
                _ => null
            };
        }
    }

    /// <summary>
    /// GroupMemberRow (ours, 0x2B / 0x10001, zone → social): u64 characterGuid, u32 worldId (the zone's world), u32 zoneId,
    /// u32 locationOffset, u32 n + n bytes record. The record is the client's group member record exactly as the zone's
    /// party frames wrote it before the split (Groups.MemberRecord: player block, status, location, resources; reader
    /// FUN_142d3ebe0); locationOffset is where its location block starts (GroupPackets.LocationWorldOffset…), which the
    /// service needs to hide the position from players on other worlds and to mark a member "linkdead". Sent once a
    /// second at most and only when the row changed (health, energy in steps of 10, landscape, position in 5 m steps,
    /// knocked out), for grouped characters, and right before every Invite / Join the zone hands on (so a new group
    /// has every member's row).
    /// </summary>
    public sealed class GroupMemberRow : ISocialMessage
    {
        public ushort Family => GroupMessages.GroupFamily;
        public uint Sub => GroupMessages.MemberRowSub;
        public ulong CharacterGuid { get; init; }
        public uint WorldId { get; init; }
        public uint ZoneId { get; init; }
        public uint LocationOffset { get; init; }
        public byte[] Record { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w) => w.U64(CharacterGuid).U32(WorldId).U32(ZoneId).U32(LocationOffset)
            .U32((uint)Record.Length).Bytes(Record);

        public static GroupMemberRow Read(InternalReader r)
        {
            ulong character = r.U64();
            uint world = r.U32(), zone = r.U32(), offset = r.U32(), n = r.U32();
            SocialMessages.CheckCount(n, 4096, "member record bytes");
            return new() { CharacterGuid = character, WorldId = world, ZoneId = zone, LocationOffset = offset, Record = r.Bytes((int)n) };
        }
    }

    /// <summary>
    /// GroupStateForZone (ours, 0x2B / 0x10002, social → zone): u64 characterGuid, u32 groupId (0 = not in a group),
    /// u64 leader, u32 n × u64 members (every member, on every world), u8 flags. Sent to the world hosting the character
    /// whenever its group changes (formed, joined, left, kicked, leader, disbanded) and on every world entry. The zone
    /// keeps it for what it does itself with the members it hosts: party chat while the service is unreachable, the
    /// party-frame rows it publishes, the "grouped" goals, group loot / gathering shares in range, the Chaos Cavern group
    /// trip (content invite 0x24 / accept 0x26), the Outposts crystal choice per group.
    /// </summary>
    public sealed class GroupStateForZone : ISocialMessage
    {
        /// <summary>Flags: the character joined or formed a group just now (the zone runs the "join a group" goal).</summary>
        public const byte FlagJoined = 1;
        /// <summary>Flags: sent on world entry (a service restart forgets every group: a zone holding a group the
        /// service no longer has tells the client it is gone).</summary>
        public const byte FlagEntry = 2;

        public ushort Family => GroupMessages.GroupFamily;
        public uint Sub => GroupMessages.StateForZoneSub;
        public ulong CharacterGuid { get; init; }
        public uint GroupId { get; init; }
        public ulong Leader { get; init; }
        public IReadOnlyList<ulong> Members { get; init; } = Array.Empty<ulong>();
        public byte Flags { get; init; }

        public void Write(InternalWriter w)
        {
            w.U64(CharacterGuid).U32(GroupId).U64(Leader).U32((uint)Members.Count);
            foreach (ulong member in Members)
                w.U64(member);
            w.U8(Flags);
        }

        public static GroupStateForZone Read(InternalReader r)
        {
            ulong character = r.U64();
            uint group = r.U32();
            ulong leader = r.U64();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 64, "group members");
            var members = new List<ulong>((int)n);
            for (uint i = 0; i < n; i++)
                members.Add(r.U64());
            return new() { CharacterGuid = character, GroupId = group, Leader = leader, Members = members, Flags = r.U8() };
        }
    }
}
