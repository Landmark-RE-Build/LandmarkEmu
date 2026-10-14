using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player groups (in memory; a group ends when its last member leaves). GroupsBase 0x2B, u8 sub, then per packet
    /// (client dispatcher FUN_142d4c840, one reader per packet):
    /// - 1 Invite {u32 result, u32, u32, invite} (FUN_142d45ad0). Client → server: "/invite Name" (result 1).
    ///   Server → client with result 1 shows "X invites you to a group" (FUN_142d4da90).
    ///   invite (FUN_142d3eb10): u64, u32 raid flag, inviter player, invitee player, u32.
    ///   player (FUN_142d3fdc0): u64 guid, name {3 × u32, first, last}, u8.
    /// - 2 Join {u32 result, u32, u32, u32, invite} (FUN_142d45bf0): the invitee's answer.
    /// - 0x11 AddGroup {u32, group} (FUN_142d4ca90): group (FUN_142d3e830): key {u32 id, u64, u8}, u8, string,
    ///   u32, u32 n + n × {u64 guid, member}, u32, bool.
    /// - 0x12 AddMember {u32, u32 group id, member} (FUN_142d45590). member (FUN_142d3ebe0): player, 5 × u32,
    ///   3 × u32, 2 bools, u32, u64, 4 × u32, 3 × u32, 4 × u32, u64, 2 × u32, bool, u32 n + n × 3 × u32.
    /// Packets go only to the members of the group concerned.
    /// </summary>
    public static class Groups
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte InviteSub = 1, JoinSub = 2, LeaveSub = 4, KickSub = 5, DisbandSub = 6, SetOwnerSub = 8,
            AddGroupSub = 0x11, AddMemberSub = 0x12, UpdateMemberSub = 0x13, RemoveGroupSub = 0x15, RemoveMemberSub = 0x16,
            RemoveInvitationSub = 0x17;

        /// <summary>
        /// Invite packet "type" (+0x10, FUN_142d4da90): 1 = an invitation for this player (the popup); any other value
        /// (but 4) = a result line for the inviter, shown with the client's own text for the code at +0x18
        /// (FUN_143b5f4a0: UI.GroupPacketInvite* table) and the invite's two names as [*source*] / [*target*].
        /// </summary>
        private const uint InviteTypeIncoming = 1, InviteTypeResult = 2;
        /// <summary>
        /// Invite result codes (index into the client's table): 0 "[target] has been invited to your party.",
        /// 5 "... because it is full.", 9 "[target] was not found. Couldn't send party invitation.", 13 "[target] already
        /// has a pending party invitation.", 14 "[target] is already in a party.", 16 "[target] declined your party
        /// invitation." (UI.GroupPacketJoinDeclined).
        /// </summary>
        private const uint InviteSuccess = 0, InviteGroupFull = 5, InviteBadInvitee = 9, InviteAlreadyInvited = 13,
            InviteAlreadyInGroup = 14, InviteDeclined = 16;
        /// <summary>
        /// RemoveInvitation (sub 0x17, FUN_142d4ea30) codes (FUN_143b5f570): 3 "Your party invitation to [target] has
        /// expired." (the inviter), 4 "Your party invitation from [source] has expired." (the invitee, whose pending
        /// invite — matched by its key — is dropped).
        /// </summary>
        private const uint RemoveExpiredTo = 3, RemoveExpiredFrom = 4;
        /// <summary>
        /// Join results to the joiner (Join sub 2 {u8 sub, u32 type, u32 code, u32 response, u32, invite}, reader
        /// FUN_142d45bf0, handler FUN_142d4df30, which always prints the code's text from FUN_143b5f4d0's table with the
        /// inviter as [*source*]): 0 "You have joined the party.", 2 "Party invitation is no longer valid.", 13 "Could not
        /// join because the party is full.".
        /// </summary>
        private const uint JoinSuccess = 0, JoinBadInvitation = 2, JoinGroupFull = 13;
        /// <summary>
        /// "A party is a group of four people" (2016 wiki, Party page; forum posts speak of "a party of 4"). The client has
        /// no size constant of its own (the party frame list grows with its data source), so this is the server's rule.
        /// </summary>
        public const int MaxMembers = 4;
        private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(2);

        private class Group
        {
            public uint Id;
            public ulong Leader;
            public readonly List<WorldSession> Members = new();
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Group> groups = new();
        private static readonly Dictionary<WorldSession, Group> groupOf = new();
        /// <summary>Invitee → (inviter, when).</summary>
        private static readonly Dictionary<WorldSession, (WorldSession Inviter, DateTime At)> invites = new();
        private static uint nextGroupId;

        /// <summary>The members of the player's group (empty when not in one).</summary>
        public static List<WorldSession> MembersOf(WorldSession session)
        {
            lock (sync)
                return groupOf.TryGetValue(session, out Group group) ? group.Members.ToList() : new List<WorldSession>();
        }

        /// <summary>The player's party members on the same landscape within <paramref name="range"/> metres (not the player).</summary>
        public static List<WorldSession> NearbyMembers(WorldSession session, float range)
            => MembersOf(session).Where(m => m != session && !m.Disconnected && m.ZoneId == session.ZoneId
                && System.Numerics.Vector3.Distance(m.Position, session.Position) <= range).ToList();

        /// <summary>
        /// Routes a GroupsBase request: invite (1), join, leave, kick (5), promote (8, SetGroupOwner) and disband (6).
        /// </summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            switch (sub)
            {
                case InviteSub:
                    HandleInvite(session, body);
                    break;
                case JoinSub:
                    HandleJoin(session, body);
                    break;
                case LeaveSub:
                    log.Debug($"Group leave from {session.CharacterName}: {BitConverter.ToString(body)}");
                    Leave(session);
                    break;
                case KickSub:
                    if (body.Length >= 24)
                        Kick(session, BitConverter.ToUInt64(body, 16));
                    break;
                case SetOwnerSub:
                    if (body.Length >= 16)
                        SetOwner(session, BitConverter.ToUInt64(body, 8));
                    break;
                case DisbandSub:
                    log.Debug($"Group disband from {session.CharacterName}: {BitConverter.ToString(body)}");
                    Disband(session);
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Groups", sub);
                    log.Debug($"Group packet 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body)}");
                    break;
            }
        }

        /// <summary>
        /// Invite (sub 1, "/invite Name" or "/party invite Name"): finds the named player online, refuses when they are
        /// already grouped or are the inviter, and shows them the invite popup (sub 1 with result 1). The answer comes back
        /// as a Join (see <see cref="HandleJoin"/>).
        /// </summary>
        private static void HandleInvite(WorldSession session, byte[] body)
        {
            string name;
            ulong targetGuid;
            try
            {
                var reader = new GamePacketReader(body);
                reader.ReadUInt();
                reader.ReadUInt();
                reader.ReadUInt();
                reader.ReadULong();
                reader.ReadUInt();
                ReadPlayer(reader, out _);
                name = ReadPlayer(reader, out targetGuid).Trim();
            }
            catch (Exception)
            {
                log.Debug($"Unreadable group invite from {session.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }

            // "/invite Name" names the player; the right-click menu (Groups.GroupInviteByGuid) gives only the guid.
            WorldSession target = name.Length > 0
                ? PlayerManager.Snapshot().FirstOrDefault(p => string.Equals(p.CharacterName, name, StringComparison.OrdinalIgnoreCase))
                : PlayerManager.FindByGuid(targetGuid);
            if (target == null)
            {
                InviteResult(session, InviteBadInvitee, targetGuid, name);
                return;
            }
            if (target == session || Ignores.Refuses(target, session))
                return;
            uint refusal = uint.MaxValue;
            lock (sync)
            {
                if (groupOf.ContainsKey(target))
                    refusal = InviteAlreadyInGroup;
                else if (groupOf.TryGetValue(session, out Group own) && own.Members.Count >= MaxMembers)
                    refusal = InviteGroupFull;
                else if (invites.TryGetValue(target, out var pending) && DateTime.UtcNow - pending.At <= InviteLifetime)
                    refusal = InviteAlreadyInvited;
                else
                    invites[target] = (session, DateTime.UtcNow);
            }
            if (refusal != uint.MaxValue)
            {
                InviteResult(session, refusal, target.PlayerGuid, target.CharacterName);
                log.Info($"{session.CharacterName} could not invite {target.CharacterName}: invite result {refusal}.");
                return;
            }
            target.EnqueueMessage(ZoneMessageOpcode.GroupsBase, BuildInvite(InviteSub, InviteTypeIncoming, session, target));
            InviteResult(session, InviteSuccess, target.PlayerGuid, target.CharacterName);
            log.Info($"{session.CharacterName} invited {target.CharacterName} to a group.");
        }

        /// <summary>
        /// Join (sub 2): the invitee's Accept / Decline on the invite popup (Game Action Queue GaqItemGroupInvite →
        /// FUN_143350700 → FUN_142d535a0, writer FUN_142d3d9c0). After the u8 sub: u32 1 (header, FUN_142b79760),
        /// u32 0, u32 response (1 accept, 2 decline), u32, the invite as received. The client sends it only when the
        /// stored invite is valid (see <see cref="BuildInvite"/>).
        /// </summary>
        private static void HandleJoin(WorldSession session, byte[] body)
        {
            log.Debug($"Group join from {session.CharacterName}: {BitConverter.ToString(body)}");
            uint response = body.Length >= 12 ? BitConverter.ToUInt32(body, 8) : 0;
            Answer(session, response == 1 ? AcceptChoice : 0);
        }

        /// <summary>The answer <see cref="Answer"/> takes as Accept.</summary>
        public const uint AcceptChoice = 4;

        /// <summary>The invitee answered the invite popup: Accept joins, anything else declines.</summary>
        public static void Answer(WorldSession session, uint choice)
        {
            log.Debug($"{session.CharacterName} answered a popup: {choice}.");
            WorldSession inviter;
            lock (sync)
            {
                bool valid = invites.Remove(session, out var invite) && DateTime.UtcNow - invite.At <= InviteLifetime;
                inviter = invite.Inviter;
                if (!valid)
                {
                    if (choice == AcceptChoice)
                        JoinResult(session, JoinBadInvitation, inviter);
                    return;
                }
            }
            if (choice != AcceptChoice)
            {
                InviteResult(inviter, InviteDeclined, session.PlayerGuid, session.CharacterName);
                log.Info($"{session.CharacterName} declined {inviter.CharacterName}'s group invitation.");
                return;
            }
            if (inviter.Disconnected || PlayerManager.FindByGuid(inviter.PlayerGuid) != inviter)
                return;

            Group group;
            List<WorldSession> existing;
            lock (sync)
            {
                if (groupOf.ContainsKey(session))
                    return;
                if (!groupOf.TryGetValue(inviter, out group))
                {
                    group = new Group { Id = ++nextGroupId, Leader = inviter.PlayerGuid };
                    group.Members.Add(inviter);
                    groups[group.Id] = group;
                    groupOf[inviter] = group;
                }
                if (group.Members.Count >= MaxMembers)
                {
                    JoinResult(session, JoinGroupFull, inviter);
                    return;
                }
                existing = group.Members.ToList();
                group.Members.Add(session);
                groupOf[session] = group;
            }

            // The newcomer gets the whole group; members already in it get the newcomer (a first-time inviter gets
            // the whole group too, as it is new to them as well).
            session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, BuildAddGroup(group));
            byte[] added = BuildAddMember(group, session);
            foreach (WorldSession member in existing)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, existing.Count == 1 ? BuildAddGroup(group) : added);
            JoinResult(session, JoinSuccess, inviter);
            Achievements.GroupJoined(session);
            if (existing.Count == 1)
                Achievements.GroupJoined(existing[0]);
            log.Info($"{session.CharacterName} joined group {group.Id} ({group.Members.Count} members).");
        }

        private const uint LeftReason = 3, KickedReason = 4, TooSmallReason = 3, DisbandedReason = 4;

        /// <summary>Leaving (or logging out): everyone is told; a group left with one member ends.</summary>
        public static void Leave(WorldSession session) => Remove(session, LeftReason, 0);

        /// <summary>
        /// Takes a player out of their group (left, kicked or disconnected). The others get RemoveMember with the reason; a
        /// group left with one member is removed (RemoveGroup, "too small"); when the leader left, the next member is promoted
        /// and everyone is told (SetGroupOwner).
        /// </summary>
        private static void Remove(WorldSession session, uint reason, ulong by)
        {
            bool wasLeader;
            Group group;
            List<WorldSession> rest;
            lock (sync)
            {
                invites.Remove(session);
                if (!groupOf.Remove(session, out group))
                    return;
                group.Members.Remove(session);
                wasLeader = group.Leader == session.PlayerGuid;
                if (wasLeader && group.Members.Count > 0)
                    group.Leader = group.Members[0].PlayerGuid;
                rest = group.Members.ToList();
                if (rest.Count <= 1)
                {
                    groups.Remove(group.Id);
                    foreach (WorldSession member in rest)
                        groupOf.Remove(member);
                }
            }
            byte[] removed = BuildRemoveMember(session, reason, by);
            session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, removed);
            byte[] note = rest.Count <= 1 ? BuildRemoveGroup(group, TooSmallReason) : removed;
            foreach (WorldSession member in rest)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, note);
            if (rest.Count > 1 && wasLeader)
            {
                byte[] owner = Build(w => { w.Write(SetOwnerSub); w.Write(2u); w.Write(0u); w.Write(group.Leader); w.Write(0u); });
                foreach (WorldSession member in rest)
                    member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, owner);
            }
            log.Info($"{session.CharacterName} left group {group.Id} (reason {reason}).");
        }

        /// <summary>Kick {u32, u32, u64 kicker, u64 kicked, u8} ("/party kick Name"): leader only.</summary>
        private static void Kick(WorldSession session, ulong kicked)
        {
            WorldSession target;
            lock (sync)
            {
                if (!groupOf.TryGetValue(session, out Group group) || group.Leader != session.PlayerGuid)
                    return;
                target = group.Members.FirstOrDefault(m => m.PlayerGuid == kicked && m != session);
            }
            if (target != null)
                Remove(target, KickedReason, session.PlayerGuid);
        }

        /// <summary>
        /// SetGroupOwner (8, reader FUN_142d46230): u32 state (1 request, 2 applied, 3 error + u32 reason), u32 reason,
        /// u64 new leader, u32. The client applies state 2 to its current group (moves the crown).
        /// </summary>
        private static void SetOwner(WorldSession session, ulong newLeader)
        {
            List<WorldSession> members;
            lock (sync)
            {
                if (!groupOf.TryGetValue(session, out Group group) || group.Leader != session.PlayerGuid
                    || group.Members.All(m => m.PlayerGuid != newLeader))
                    return;
                group.Leader = newLeader;
                members = group.Members.ToList();
            }
            byte[] note = Build(w =>
            {
                w.Write(SetOwnerSub);
                w.Write(2u);
                w.Write(0u);
                w.Write(newLeader);
                w.Write(0u);
            });
            foreach (WorldSession member in members)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, note);
        }

        /// <summary>The leader ends the group for everyone.</summary>
        private static void Disband(WorldSession session)
        {
            List<WorldSession> members;
            Group group;
            lock (sync)
            {
                if (!groupOf.TryGetValue(session, out group) || group.Leader != session.PlayerGuid)
                    return;
                members = group.Members.ToList();
                groups.Remove(group.Id);
                foreach (WorldSession member in members)
                    groupOf.Remove(member);
            }
            byte[] note = BuildRemoveGroup(group, DisbandedReason);
            foreach (WorldSession member in members)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, note);
            log.Info($"{session.CharacterName} disbanded group {group.Id}.");
        }

        /// <summary>
        /// An Invite packet as shown to the invitee: sub, result, then the invite (FUN_142d3eb10, kept by the client at
        /// its group manager +0x593a0): u64 key, u32 raid flag, inviter and invitee player blocks, u32. The popup's
        /// Accept / Decline only sends a Join back when that invite looks valid (FUN_143350700 / FUN_142d535a0): a set
        /// key (not the client's "no group" constant), both guids set, each player block's last byte 1-3, raid flag 0.
        /// The key is the inviter's guid (unique per inviter; the Join echoes it).
        /// </summary>
        /// <summary>A Join result for the joiner (see <see cref="JoinSuccess"/>); the inviter's name is [*source*].</summary>
        private static void JoinResult(WorldSession joiner, uint code, WorldSession inviter)
        {
            joiner.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Build(w =>
            {
                w.Write(JoinSub);
                w.Write(InviteTypeResult);
                w.Write(code);
                w.Write(code == JoinSuccess ? 1u : 0u);   // response: accepted
                w.Write(0u);
                w.Write(0u);
                w.Write(inviter?.PlayerGuid ?? 0ul);
                w.Write(0u);
                WritePlayer(w, inviter?.PlayerGuid ?? 0, inviter?.CharacterName ?? "", 1);
                WritePlayer(w, joiner.PlayerGuid, joiner.CharacterName, 1);
                w.Write(0u);
            }));
            log.Debug($"{joiner.CharacterName}: join result {code}.");
        }

        /// <summary>
        /// A result line for an inviter (Invite type 2, the code at +0x18): the client shows its own text for the code
        /// with the invitee's name as [*target*].
        /// </summary>
        private static void InviteResult(WorldSession inviter, uint code, ulong inviteeGuid, string inviteeName)
        {
            inviter.EnqueueMessage(ZoneMessageOpcode.GroupsBase, BuildInviteRecord(InviteSub, InviteTypeResult, code,
                inviter.PlayerGuid, inviter.CharacterName, inviteeGuid, inviteeName));
            log.Debug($"{inviter.CharacterName}: invite result {code} for {inviteeName}.");
        }

        /// <summary>
        /// An Invite / RemoveInvitation packet: sub, type, code, then the invite record (key = the inviter's guid, raid
        /// flag 0, inviter and invitee player blocks, u32).
        /// </summary>
        private static byte[] BuildInviteRecord(byte sub, uint type, uint code, ulong inviterGuid, string inviterName,
            ulong inviteeGuid, string inviteeName)
        {
            return Build(w =>
            {
                w.Write(sub);
                w.Write(type);
                w.Write(code);
                w.Write(0u);
                w.Write(inviterGuid);
                w.Write(0u);
                WritePlayer(w, inviterGuid, inviterName, 1);
                WritePlayer(w, inviteeGuid, inviteeName, 1);
                w.Write(0u);
            });
        }

        /// <summary>
        /// Invitations nobody answered within <see cref="InviteLifetime"/> lapse: RemoveInvitation tells the inviter
        /// ("Your party invitation to [X] has expired.") and the invitee ("... from [X] has expired.", which also drops
        /// the pending popup). Called from <see cref="Update"/>.
        /// </summary>
        private static void ExpireInvites(DateTime now)
        {
            List<(WorldSession Invitee, WorldSession Inviter)> lapsed;
            lock (sync)
            {
                lapsed = invites.Where(i => now - i.Value.At > InviteLifetime).Select(i => (i.Key, i.Value.Inviter)).ToList();
                foreach (var (invitee, _) in lapsed)
                    invites.Remove(invitee);
            }
            foreach (var (invitee, inviter) in lapsed)
            {
                if (!inviter.Disconnected)
                    inviter.EnqueueMessage(ZoneMessageOpcode.GroupsBase, BuildInviteRecord(RemoveInvitationSub, 0, RemoveExpiredTo,
                        inviter.PlayerGuid, inviter.CharacterName, invitee.PlayerGuid, invitee.CharacterName));
                if (!invitee.Disconnected)
                    invitee.EnqueueMessage(ZoneMessageOpcode.GroupsBase, BuildInviteRecord(RemoveInvitationSub, 0, RemoveExpiredFrom,
                        inviter.PlayerGuid, inviter.CharacterName, invitee.PlayerGuid, invitee.CharacterName));
                log.Info($"{inviter.CharacterName}'s group invitation to {invitee.CharacterName} expired.");
            }
        }

        private static byte[] BuildInvite(byte sub, uint result, WorldSession inviter, WorldSession invitee)
        {
            return Build(w =>
            {
                w.Write(sub);
                w.Write(result);
                w.Write(0u);
                w.Write(0u);
                w.Write(inviter.PlayerGuid);
                w.Write(0u);
                WritePlayer(w, inviter, 1);
                WritePlayer(w, invitee, 1);
                w.Write(0u);
            });
        }

        /// <summary>
        /// AddGroup (0x11): the whole group (id, leader, members) for a player who joined or formed it; opens the Party window.
        /// </summary>
        private static byte[] BuildAddGroup(Group group)
        {
            List<WorldSession> members;
            lock (sync)
                members = group.Members.ToList();
            return Build(w =>
            {
                w.Write(AddGroupSub);
                w.Write(0u);
                w.Write(group.Id);
                w.Write(group.Leader);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write("");
                w.Write(0u);
                w.Write((uint)members.Count);
                foreach (WorldSession member in members)
                {
                    w.Write(member.PlayerGuid);
                    WriteMember(w, member);
                }
                w.Write(0u);
                w.Write(false);
            });
        }

        /// <summary>
        /// AddMember (0x12): one new member for players already in the group.
        /// </summary>
        private static byte[] BuildAddMember(Group group, WorldSession member) => Build(w =>
        {
            w.Write(AddMemberSub);
            w.Write(0u);
            w.Write(group.Id);
            WriteMember(w, member);
        });

        /// <summary>RemoveGroup (0x15, FUN_142d4e890): u32, u32 reason, u32 group id. Reasons: 0 success,
        /// 1 error, 2 invalid, 3 too small, 4 disbanded.</summary>
        private static byte[] BuildRemoveGroup(Group group, uint reason) => Build(w =>
        {
            w.Write(RemoveGroupSub);
            w.Write(0u);
            w.Write(reason);
            w.Write(group.Id);
        });

        /// <summary>RemoveMember (0x16, FUN_142d4edd0): u32, u32 reason, u64 member guid (+ u64, bool when kicked).
        /// Reasons: 0 success, 1 error, 2 invalid, 3 left, 4 kicked, 5 faction changed, 6 swapped. The member
        /// themself gets it too (the client drops its whole group then).</summary>
        private static byte[] BuildRemoveMember(WorldSession member, uint reason, ulong by = 0) => Build(w =>
        {
            w.Write(RemoveMemberSub);
            w.Write(0u);
            w.Write(reason);
            w.Write(member.PlayerGuid);
            if (reason == KickedReason)
            {
                w.Write(by);
                w.Write(false);
            }
        });

        /// <summary>
        /// Writes a group member record: the player block followed by status fields (zone, health and so on) sent as defaults.
        /// </summary>
        /// <summary>
        /// A member record (reader FUN_142d3ebe0; the party frame's row filler FUN_142d56350 / FUN_142d560a0): player,
        /// 5 + 3 × u32, bool, bool, u32, u64 link id (+0xf8; anything but 0 = "IsLinkdead"), u32 world (+0x100), u32 zone
        /// (+0x104, its name is the row's ZoneName), u32 instance (+0x108; same world and instance as the viewer →
        /// "IsInZone", "Distance" from the position), u32, f32 × 3 position (+0x110), 4 × u32, u64, u32, u32, bool knocked
        /// out (+0x140), then the resources the frame's bars show: u32 n × {u32 type (1 health, 2 energy / mana,
        /// 11 armor), u32 current, u32 max}.
        /// </summary>
        private static void WriteMember(GamePacketWriter w, WorldSession member)
        {
            WritePlayer(w, member);
            for (int i = 0; i < 5 + 3; i++)
                w.Write(0u);
            w.Write(true);
            w.Write(false);
            w.Write(0u);
            w.Write(0ul);                                  // link id: 0 = connected
            w.Write(1u);                                   // world
            w.Write(member.ZoneId);                        // zone
            w.Write(member.ZoneId);                        // instance
            w.Write(0u);
            var at = member.Position;
            w.Write(at.X);
            w.Write(at.Y);
            w.Write(at.Z);
            for (int i = 0; i < 4; i++)
                w.Write(0u);
            w.Write(0ul);
            w.Write(0u);
            w.Write(0u);
            w.Write(Health.IsDead(member));                // knocked out
            w.Write(2u);
            w.Write(1u);
            w.Write(Health.Current(member));
            w.Write(Health.MaxHealth);
            w.Write(2u);
            w.Write(WeaponAttacks.EnergyOf(member));
            w.Write(CharacterResources.EnergyMax);
        }

        /// <summary>What a member's row shows, to send an update only when it changed.</summary>
        private static (uint Hp, uint Energy, uint Zone, int X, int Z, bool Dead) RowOf(WorldSession m)
            => (Health.Current(m), WeaponAttacks.EnergyOf(m) / 10, m.ZoneId, (int)(m.Position.X / 5), (int)(m.Position.Z / 5), Health.IsDead(m));

        private static readonly Dictionary<WorldSession, (uint, uint, uint, int, int, bool)> lastRows = new();
        private static DateTime nextRowTick;

        /// <summary>
        /// Once a second: a member whose health, energy (in steps of 10), zone, position (5 m) or knocked-out state changed
        /// goes to the whole group again as UpdateMember (0x13, FUN_142d4fb50: u32, member — found by its guid), which the
        /// party frame shows (bars, distance, zone).
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextRowTick)
                return;
            nextRowTick = now.AddSeconds(1);
            ExpireInvites(now);
            List<Group> all;
            lock (sync)
                all = groups.Values.ToList();
            foreach (Group group in all)
            {
                List<WorldSession> members;
                lock (sync)
                    members = group.Members.ToList();
                foreach (WorldSession m in members.Where(m => !m.Disconnected))
                {
                    var row = RowOf(m);
                    lock (sync)
                    {
                        if (lastRows.TryGetValue(m, out var last) && last == row)
                            continue;
                        lastRows[m] = row;
                    }
                    byte[] update = Build(w =>
                    {
                        w.Write(UpdateMemberSub);
                        w.Write(0u);
                        WriteMember(w, m);
                    });
                    foreach (WorldSession to in members.Where(x => !x.Disconnected))
                        to.EnqueueMessage(ZoneMessageOpcode.GroupsBase, update);
                }
            }
            lock (sync)
                foreach (WorldSession gone in lastRows.Keys.Where(s => s.Disconnected || !groupOf.ContainsKey(s)).ToList())
                    lastRows.Remove(gone);
        }

        /// <summary>
        /// Writes a player block {u64 guid, name block, u8}.
        /// </summary>
        private static void WritePlayer(GamePacketWriter w, WorldSession player, byte kind = 0)
            => WritePlayer(w, player.PlayerGuid, player.CharacterName, kind);

        private static void WritePlayer(GamePacketWriter w, ulong guid, string name, byte kind)
        {
            w.Write(guid);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(name ?? "");
            w.Write("");
            w.Write(kind);
        }

        /// <summary>Reads a player record and returns the first name.</summary>
        private static string ReadPlayer(GamePacketReader reader, out ulong guid)
        {
            guid = reader.ReadULong();
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadUInt();
            string first = reader.ReadString();
            reader.ReadString();
            reader.ReadByte();
            return first ?? "";
        }

        /// <summary>
        /// Collects what a writer callback writes into a byte array.
        /// </summary>
        private static byte[] Build(Action<GamePacketWriter> write)
        {
            var output = new List<byte>();
            write(new GamePacketWriter(output));
            return output.ToArray();
        }
    }
}
