using LandmarkEmulator.Shared.Network;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Game
{
    /// <summary>
    /// The GroupsBase (0x2B, u8 sub) client packets the server sends, shared by the zone's in-process groups
    /// (WorldServer Game/Groups.cs) and the shared social service's groups across worlds (SocialServer GroupService.cs,
    /// trace/architecture/social-service.md §10), so both paths write the same bytes. Each method returns the body after
    /// the opcode, starting with the u8 sub: the zone queues it with ZoneMessageOpcode.GroupsBase, the service puts the
    /// wire bytes 2B 00 in front (<see cref="Opcode"/>) and tunnels it through the player's world.
    /// <para>
    /// Client side (0.589 dispatcher FUN_142d4c840 / 0.695 FUN_142deaa10; all 24 cases and every reader "same" in 0.695,
    /// trace/0695/coverage-695.md §0x2B, so one layout serves both builds):
    /// </para>
    /// <list type="bullet">
    /// <item>1 Invite (reader FUN_142d45ad0; handler FUN_142d4da90): {u32 type, u32 code, u32, invite}. Type 1 = an
    /// invitation for this player (the popup); any other type (but 4) = a result line for the inviter with the client's
    /// text for the code (FUN_143b5f4a0, UI.GroupPacketInvite*). invite (FUN_142d3eb10): u64 key, u32 raid flag,
    /// inviter player block, invitee player block, u32.</item>
    /// <item>2 Join (FUN_142d45bf0, handler FUN_142d4df30): {u32 type, u32 code, u32 response, u32, invite}; the client
    /// prints the code's text (FUN_143b5f4d0) with the inviter as [*source*].</item>
    /// <item>8 SetGroupOwner (FUN_142d46230): {u32 state (2 = applied), u32 reason, u64 new leader, u32}.</item>
    /// <item>0x11 AddGroup (FUN_142d4ca90): {u32, group} — group (FUN_142d3e830): u32 id, u64 leader, u8, u8, string,
    /// u32, u32 n × {u64 guid, member record}, u32, bool.</item>
    /// <item>0x12 AddMember (FUN_142d45590): {u32, u32 group id, member record}.</item>
    /// <item>0x13 UpdateMember (FUN_142d4fb50): {u32, member record} — the record is found by its guid.</item>
    /// <item>0x15 RemoveGroup (FUN_142d4e890): {u32, u32 reason, u32 group id}; reasons 0 success, 1 error, 2 invalid,
    /// 3 too small, 4 disbanded.</item>
    /// <item>0x16 RemoveMember (FUN_142d4edd0): {u32, u32 reason, u64 member} (+ u64 by, bool when kicked); reasons
    /// 0 success, 1 error, 2 invalid, 3 left, 4 kicked, 5 faction changed, 6 swapped.</item>
    /// <item>0x17 RemoveInvitation (FUN_142d4ea30): {u32 type, u32 code, u32, invite}; codes (FUN_143b5f570) 3 "Your
    /// party invitation to [target] has expired.", 4 "Your party invitation from [source] has expired.".</item>
    /// </list>
    /// The member record (reader FUN_142d3ebe0) carries the player's status (health, energy, landscape, position) and is
    /// written only by the zone that hosts the player (Groups.MemberRecord); the service passes it on as bytes and only
    /// hides the location of a member on another world (<see cref="HideLocation"/>) or marks one who is between worlds
    /// (<see cref="MarkLinkdead"/>).
    /// </summary>
    public static class GroupPackets
    {
        /// <summary>The wire bytes of GroupsBase (ZoneMessageOpcode.GroupsBase 0x2B00).</summary>
        public static readonly byte[] Opcode = { 0x2B, 0x00 };

        public const byte InviteSub = 1, JoinSub = 2, LeaveSub = 4, KickSub = 5, DisbandSub = 6, SetOwnerSub = 8,
            AddGroupSub = 0x11, AddMemberSub = 0x12, UpdateMemberSub = 0x13, RemoveGroupSub = 0x15, RemoveMemberSub = 0x16,
            RemoveInvitationSub = 0x17, InviteToGroupContentSub = 0x24, AcceptTeleportSub = 0x26;

        /// <summary>Invite / RemoveInvitation type: 1 = an invitation for this player (popup), 2 = a result line.</summary>
        public const uint InviteTypeIncoming = 1, InviteTypeResult = 2;

        /// <summary>
        /// Invite result codes (index into the client's table): 0 "[target] has been invited to your party.",
        /// 5 "... because it is full.", 9 "[target] was not found. Couldn't send party invitation.", 13 "[target] already
        /// has a pending party invitation.", 14 "[target] is already in a party.", 16 "[target] declined your party
        /// invitation." (UI.GroupPacketJoinDeclined).
        /// </summary>
        public const uint InviteSuccess = 0, InviteGroupFull = 5, InviteBadInvitee = 9, InviteAlreadyInvited = 13,
            InviteAlreadyInGroup = 14, InviteDeclined = 16;

        /// <summary>RemoveInvitation codes: 3 to the inviter, 4 to the invitee (drops the pending popup).</summary>
        public const uint RemoveExpiredTo = 3, RemoveExpiredFrom = 4;

        /// <summary>Join results: 0 "You have joined the party.", 2 "Party invitation is no longer valid.", 13 "Could not join because the party is full.".</summary>
        public const uint JoinSuccess = 0, JoinBadInvitation = 2, JoinGroupFull = 13;

        /// <summary>RemoveMember reasons (3 left, 4 kicked) and RemoveGroup reasons (2 invalid, 3 too small, 4 disbanded).</summary>
        public const uint LeftReason = 3, KickedReason = 4, InvalidGroupReason = 2, TooSmallReason = 3, DisbandedReason = 4;

        /// <summary>
        /// Where the location block starts inside a member record, relative to its first byte, is given by the writer
        /// (Groups.MemberRecord returns it). From there: u32 world (+0x100 in the client's member; "IsInZone" needs the
        /// same world and instance as the viewer's own record, FUN_142d560a0), u32 zone (+0x104, its name is the row's
        /// ZoneName, FUN_142d56350), u32 instance (+0x108), u32 (+0x10c, read as a world id for "WorldNameId"), f32 × 3
        /// position (+0x110, "Distance" / "InGroupBonusRange"). The u64 link id (+0xf8, non-zero = "IsLinkdead")
        /// is the 8 bytes just before it.
        /// </summary>
        public const int LocationWorldOffset = 0, LocationPositionOffset = 16, LocationLength = 28, LinkIdBeforeLocation = 8;

        /// <summary>An Invite (sub 1) or RemoveInvitation (0x17) packet: sub, type, code, then the invite record (key = the inviter's guid).</summary>
        public static byte[] InviteRecord(byte sub, uint type, uint code, ulong inviterGuid, string inviterName,
            ulong inviteeGuid, string inviteeName) => Build(w =>
        {
            w.Write(sub);
            // RemoveInvitation carries only the code before the record (reader FUN_142de4220: sub, u32 +0x18, record
            // +0x20); Invite carries type and code [P]. The extra type broke the record's key until the 2026-10-17 wire
            // audit (audit-wire-c.md #3): expired invite popups were never cleared.
            if (sub != RemoveInvitationSub)
                w.Write(type);
            w.Write(code);
            w.Write(0u);
            w.Write(inviterGuid);
            w.Write(0u);
            WritePlayer(w, inviterGuid, inviterName, 1);
            WritePlayer(w, inviteeGuid, inviteeName, 1);
            w.Write(0u);
        });

        /// <summary>The popup for an invited player (Invite type 1). The popup's Accept / Decline sends a Join back only
        /// when this invite looks valid (FUN_143350700 / FUN_142d535a0): key set, both guids set, each player block's last
        /// byte 1-3, raid flag 0.</summary>
        public static byte[] Invitation(ulong inviterGuid, string inviterName, ulong inviteeGuid, string inviteeName)
            => InviteRecord(InviteSub, InviteTypeIncoming, 0, inviterGuid, inviterName, inviteeGuid, inviteeName);

        /// <summary>A result line for an inviter (Invite type 2): the client's text for the code with the invitee as [*target*].</summary>
        public static byte[] InviteResult(uint code, ulong inviterGuid, string inviterName, ulong inviteeGuid, string inviteeName)
            => InviteRecord(InviteSub, InviteTypeResult, code, inviterGuid, inviterName, inviteeGuid, inviteeName);

        /// <summary>A Join result for the joiner (response 1 = accepted on success); the inviter's name is [*source*].</summary>
        public static byte[] JoinResult(uint code, ulong inviterGuid, string inviterName, ulong joinerGuid, string joinerName) => Build(w =>
        {
            w.Write(JoinSub);
            w.Write(InviteTypeResult);
            w.Write(code);
            w.Write(code == JoinSuccess ? 1u : 0u);
            // Then the invite record {u32 0, u64 key, …} (reader FUN_142de3e50: sub, three u32, record +0x28) [P]; one
            // u32 too many before it until the 2026-10-17 wire audit (audit-wire-c.md #4).
            w.Write(0u);
            w.Write(inviterGuid);
            w.Write(0u);
            WritePlayer(w, inviterGuid, inviterName ?? "", 1);
            WritePlayer(w, joinerGuid, joinerName, 1);
            w.Write(0u);
        });

        /// <summary>AddGroup (0x11): the whole group (id, leader, members with their records); opens the Party window.</summary>
        public static byte[] AddGroup(uint groupId, ulong leader, IReadOnlyList<(ulong Guid, byte[] Record)> members) => Build(w =>
        {
            w.Write(AddGroupSub);
            w.Write(0u);
            w.Write(groupId);
            w.Write(leader);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write("");
            w.Write(0u);
            w.Write((uint)members.Count);
            foreach ((ulong guid, byte[] record) in members)
            {
                w.Write(guid);
                w.WriteBytes(record);
            }
            w.Write(0u);
            w.Write(false);
        });

        /// <summary>AddMember (0x12): one new member for players already in the group.</summary>
        public static byte[] AddMember(uint groupId, byte[] record) => Build(w =>
        {
            w.Write(AddMemberSub);
            w.Write(0u);
            w.Write(groupId);
            w.WriteBytes(record);
        });

        /// <summary>UpdateMember (0x13): a member's row again (bars, distance, zone).</summary>
        public static byte[] UpdateMember(byte[] record) => Build(w =>
        {
            w.Write(UpdateMemberSub);
            w.Write(0u);
            w.WriteBytes(record);
        });

        /// <summary>RemoveGroup (0x15): u32, u32 reason, u32 group id.</summary>
        public static byte[] RemoveGroup(uint groupId, uint reason) => Build(w =>
        {
            w.Write(RemoveGroupSub);
            w.Write(0u);
            w.Write(reason);
            w.Write(groupId);
        });

        /// <summary>RemoveMember (0x16): u32, u32 reason, u64 member (+ u64 by, bool when kicked). The member themself
        /// gets it too (the client drops its whole group then).</summary>
        public static byte[] RemoveMember(ulong member, uint reason, ulong by = 0) => Build(w =>
        {
            w.Write(RemoveMemberSub);
            w.Write(0u);
            w.Write(reason);
            w.Write(member);
            if (reason == KickedReason)
            {
                w.Write(by);
                w.Write(false);
            }
        });

        /// <summary>SetGroupOwner (8) state 2 "applied": the client moves the crown to the new leader.</summary>
        public static byte[] SetOwner(ulong newLeader) => Build(w =>
        {
            w.Write(SetOwnerSub);
            w.Write(2u);
            w.Write(0u);
            w.Write(newLeader);
            w.Write(0u);
        });

        /// <summary>A player block {u64 guid, name block {3 × u32, string first, string last}, u8 kind}.</summary>
        public static void WritePlayer(GamePacketWriter w, ulong guid, string name, byte kind)
        {
            w.Write(guid);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(name ?? "");
            w.Write("");
            w.Write(kind);
        }

        /// <summary>
        /// A copy of a member record for a viewer on <b>another world</b> (ours; the social service's groups across
        /// worlds): world 0 (the zone always writes 1, so the viewer's own record never matches and the client shows the
        /// member as not "IsInZone", with no "Distance" and outside the bonus range, FUN_142d560a0) and the position
        /// zeroed, so positions only ever reach players on the same world. The landscape (ZoneName) stays.
        /// The record is returned unchanged when <paramref name="locationOffset"/> doesn't fit it.
        /// </summary>
        public static byte[] HideLocation(byte[] record, int locationOffset)
        {
            byte[] copy = (byte[])record.Clone();
            if (locationOffset < 0 || locationOffset + LocationLength > copy.Length)
                return copy;
            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(locationOffset + LocationWorldOffset), 0u);
            copy.AsSpan(locationOffset + LocationPositionOffset, 12).Clear();
            return copy;
        }

        /// <summary>
        /// A copy of a member record with a non-zero link id (ours): the party frame shows the member as "IsLinkdead"
        /// (FUN_142d56350) while they are between worlds or logging out (the social service's leave grace).
        /// </summary>
        public static byte[] MarkLinkdead(byte[] record, int locationOffset)
        {
            byte[] copy = (byte[])record.Clone();
            if (locationOffset < LinkIdBeforeLocation || locationOffset > copy.Length)
                return copy;
            BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(locationOffset - LinkIdBeforeLocation), 1ul);
            return copy;
        }

        /// <summary>The whole client packet (wire opcode bytes + body), as the social service tunnels it.</summary>
        public static byte[] WithOpcode(byte[] body)
        {
            var packet = new byte[Opcode.Length + body.Length];
            Opcode.CopyTo(packet, 0);
            body.CopyTo(packet, Opcode.Length);
            return packet;
        }

        private static byte[] Build(Action<GamePacketWriter> write)
        {
            var output = new List<byte>();
            write(new GamePacketWriter(output));
            return output.ToArray();
        }
    }
}
