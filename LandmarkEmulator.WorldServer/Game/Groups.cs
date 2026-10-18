using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Game;
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
    /// Player groups. GroupsBase 0x2B, u8 sub, then per packet (client dispatcher FUN_142d4c840 / 0.695 FUN_142deaa10,
    /// one reader per packet, all "same" in 0.695: coverage-695.md §0x2B; layouts in <see cref="GroupPackets"/>, shared
    /// with the social service so both paths send the same bytes):
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
    /// <para>
    /// Two ways to run (TASKS B.9, social-service.md §10):
    /// </para>
    /// <list type="bullet">
    /// <item><b>In-process</b> (no SOCIAL_ADDRESS): the groups of this world live here, in memory; a group ends when its
    /// last member leaves; a logout leaves the group at once. Unchanged from before the social service.</item>
    /// <item><b>With the social service</b> (<see cref="Social.SocialServices.Groups"/> set): groups are shared by every
    /// world (P4906: "You no longer have to be on the same world to remove a group member from your group"). The
    /// client's invite / join / leave / kick / disband / set-owner requests go to the service, which answers the clients
    /// itself; this zone keeps what needs the game or positions: it writes the party-frame record of its grouped players
    /// (health, energy, landscape, position: <see cref="MemberRecord"/>) and publishes it on change; the Chaos Cavern
    /// group trip (content invite 0x24 / accept 0x26), group loot and gathering shares, and the Outposts crystal choice
    /// use the members on this world only (<see cref="MembersOf"/>).</item>
    /// </list>
    /// </summary>
    public static class Groups
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte InviteSub = GroupPackets.InviteSub, JoinSub = GroupPackets.JoinSub, LeaveSub = GroupPackets.LeaveSub,
            KickSub = GroupPackets.KickSub, DisbandSub = GroupPackets.DisbandSub, SetOwnerSub = GroupPackets.SetOwnerSub,
            AddGroupSub = GroupPackets.AddGroupSub, AddMemberSub = GroupPackets.AddMemberSub, UpdateMemberSub = GroupPackets.UpdateMemberSub,
            RemoveGroupSub = GroupPackets.RemoveGroupSub, RemoveMemberSub = GroupPackets.RemoveMemberSub,
            RemoveInvitationSub = GroupPackets.RemoveInvitationSub, InviteToGroupContentSub = GroupPackets.InviteToGroupContentSub,
            AcceptTeleportSub = GroupPackets.AcceptTeleportSub;

        /// <summary>
        /// Invite packet "type" (+0x10, FUN_142d4da90): 1 = an invitation for this player (the popup); any other value
        /// (but 4) = a result line for the inviter, shown with the client's own text for the code at +0x18
        /// (FUN_143b5f4a0: UI.GroupPacketInvite* table) and the invite's two names as [*source*] / [*target*].
        /// </summary>
        private const uint InviteTypeIncoming = GroupPackets.InviteTypeIncoming, InviteTypeResult = GroupPackets.InviteTypeResult;
        /// <summary>
        /// Invite result codes (index into the client's table): 0 "[target] has been invited to your party.",
        /// 5 "... because it is full.", 9 "[target] was not found. Couldn't send party invitation.", 13 "[target] already
        /// has a pending party invitation.", 14 "[target] is already in a party.", 16 "[target] declined your party
        /// invitation." (UI.GroupPacketJoinDeclined).
        /// </summary>
        private const uint InviteSuccess = GroupPackets.InviteSuccess, InviteGroupFull = GroupPackets.InviteGroupFull,
            InviteBadInvitee = GroupPackets.InviteBadInvitee, InviteAlreadyInvited = GroupPackets.InviteAlreadyInvited,
            InviteAlreadyInGroup = GroupPackets.InviteAlreadyInGroup, InviteDeclined = GroupPackets.InviteDeclined;
        /// <summary>
        /// RemoveInvitation (sub 0x17, FUN_142d4ea30) codes (FUN_143b5f570): 3 "Your party invitation to [target] has
        /// expired." (the inviter), 4 "Your party invitation from [source] has expired." (the invitee, whose pending
        /// invite — matched by its key — is dropped).
        /// </summary>
        private const uint RemoveExpiredTo = GroupPackets.RemoveExpiredTo, RemoveExpiredFrom = GroupPackets.RemoveExpiredFrom;
        /// <summary>
        /// Join results to the joiner (Join sub 2 {u8 sub, u32 type, u32 code, u32 response, u32, invite}, reader
        /// FUN_142d45bf0, handler FUN_142d4df30, which always prints the code's text from FUN_143b5f4d0's table with the
        /// inviter as [*source*]): 0 "You have joined the party.", 2 "Party invitation is no longer valid.", 13 "Could not
        /// join because the party is full.".
        /// </summary>
        private const uint JoinSuccess = GroupPackets.JoinSuccess, JoinBadInvitation = GroupPackets.JoinBadInvitation,
            JoinGroupFull = GroupPackets.JoinGroupFull;
        /// <summary>
        /// A party holds four people (2016 wiki, Party page; forum posts say the same). The client has
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

        /// <summary>
        /// The members of the player's group (empty when not in one). With the social service: the members on this
        /// world (the player included) — everything the zone does with them needs them here (party chat, which reaches
        /// every world, goes through the service while it is reachable).
        /// </summary>
        public static List<WorldSession> MembersOf(WorldSession session)
        {
            if (Social.SocialServices.Groups is { } service)
                return service.Of(session.PlayerGuid) is { } view
                    ? view.Members.Select(PlayerManager.FindByGuid).Where(m => m != null).ToList()
                    : new List<WorldSession>();
            lock (sync)
                return groupOf.TryGetValue(session, out Group group) ? group.Members.ToList() : new List<WorldSession>();
        }

        /// <summary>Is the player in a group with at least one other member (on any world, with the social service)?
        /// For the "grouped" goals.</summary>
        public static bool IsGrouped(WorldSession session)
        {
            if (Social.SocialServices.Groups is { } service)
                return service.Of(session.PlayerGuid) is { } view && view.Members.Count > 1;
            return MembersOf(session).Count > 1;
        }

        /// <summary>The player's party members on the same landscape within <paramref name="range"/> metres (not the player).</summary>
        public static List<WorldSession> NearbyMembers(WorldSession session, float range)
            => MembersOf(session).Where(m => m != session && !m.Disconnected && m.ZoneId == session.ZoneId
                && System.Numerics.Vector3.Distance(m.Position, session.Position) <= range).ToList();

        /// <summary>
        /// Routes a GroupsBase request: invite (1), join, leave, kick (5), promote (8, SetGroupOwner) and disband (6).
        /// With the social service those six go to it (<see cref="ForwardToService"/>); the Chaos Cavern trip's accept
        /// (0x26) stays here.
        /// </summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            if (Social.SocialServices.Groups is { } service && GroupMessages.IsForwardedSub(sub))
            {
                ForwardToService(service, session, sub, body);
                return;
            }
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
                case AcceptTeleportSub:
                    // GroupPacketAcceptTeleport (0.695 sender FUN_142de7a90, the "join them?" dialog's Yes; No sends
                    // nothing): {u32 1, u64 inviter, u64 own guid, bool}.
                    if (body.Length >= 12)
                        Caves.AcceptGroupTrip(session, BitConverter.ToUInt64(body, 4));
                    break;
                case DisbandSub:
                    log.Debug($"Group disband from {session.CharacterName}: {BitConverter.ToString(body)}");
                    Disband(session);
                    break;
                case AutoGroupSub:
                case SetGroupFlagsSub:
                case SetGroupDescriptionSub:
                case FindLookingForMoreSub:
                case GetGroupInfoSub:
                case JoinLookingForMoreSub:
                    HandleLookingForMore(session, sub, body);
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Groups", sub);
                    log.Debug($"Group packet 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body)}");
                    break;
            }
        }

        /// <summary>
        /// With the social service: the request goes to it unchanged (ClientRequestForward 0x2B / the client's sub). An
        /// invite or a join carries the requester's member record first, so the service has every member's party-frame
        /// row when the group forms. While the service is unreachable the player gets a line (ours; no client text for a
        /// group service outage found) and nothing changes.
        /// </summary>
        private static void ForwardToService(Social.IGroupService service, WorldSession session, byte sub, byte[] body)
        {
            byte[] record = null;
            int locationOffset = 0;
            if (sub == InviteSub || sub == JoinSub)
                record = MemberRecord(session, out locationOffset);
            if (service.Request(session, sub, body, record, locationOffset))
                return;
            Chat.System(session, "Groups are unavailable right now; please try again in a moment.");
            log.Warn($"Social service unreachable: group request 0x{sub:X2} from {session.CharacterName} dropped.");
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
            if (target == session || Social.SocialServices.IgnoreCache.Refuses(target, session))
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
            target.EnqueueMessage(ZoneMessageOpcode.GroupsBase,
                GroupPackets.Invitation(session.PlayerGuid, session.CharacterName, target.PlayerGuid, target.CharacterName));
            InviteResult(session, InviteSuccess, target.PlayerGuid, target.CharacterName);
            log.Info($"{session.CharacterName} invited {target.CharacterName} to a group.");
        }

        /// <summary>
        /// Join (sub 2): the invitee's Accept / Decline on the invite popup (Game Action Queue GaqItemGroupInvite →
        /// FUN_143350700 → FUN_142d535a0, writer FUN_142d3d9c0). After the u8 sub: u32 1 (header, FUN_142b79760),
        /// u32 0, u32 response (1 accept, 2 decline), u32, the invite as received. The client sends it only when the
        /// stored invite is valid (see <see cref="GroupPackets.Invitation"/>).
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
            byte[] added = GroupPackets.AddMember(group.Id, MemberRecord(session, out _));
            foreach (WorldSession member in existing)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, existing.Count == 1 ? BuildAddGroup(group) : added);
            JoinResult(session, JoinSuccess, inviter);
            Achievements.GroupJoined(session);
            if (existing.Count == 1)
                Achievements.GroupJoined(existing[0]);
            log.Info($"{session.CharacterName} joined group {group.Id} ({group.Members.Count} members).");
        }

        private const uint LeftReason = GroupPackets.LeftReason, KickedReason = GroupPackets.KickedReason,
            TooSmallReason = GroupPackets.TooSmallReason, DisbandedReason = GroupPackets.DisbandedReason;

        /// <summary>
        /// Leaving (or logging out): everyone is told; a group left with one member ends. With the social service this is
        /// only the logout (PlayerManager.Leave): the service keeps the member in the group for its leave grace (a world
        /// change keeps the group) and takes them out when it runs out; here only the party-frame bookkeeping goes.
        /// </summary>
        public static void Leave(WorldSession session)
        {
            if (Social.SocialServices.Groups != null)
            {
                lock (sync)
                    lastRows.Remove(session);
                return;
            }
            Remove(session, LeftReason, 0);
        }

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
            byte[] removed = GroupPackets.RemoveMember(session.PlayerGuid, reason, by);
            session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, removed);
            byte[] note = rest.Count <= 1 ? GroupPackets.RemoveGroup(group.Id, TooSmallReason) : removed;
            foreach (WorldSession member in rest)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, note);
            if (rest.Count > 1 && wasLeader)
            {
                byte[] owner = GroupPackets.SetOwner(group.Leader);
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
            byte[] note = GroupPackets.SetOwner(newLeader);
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
            byte[] note = GroupPackets.RemoveGroup(group.Id, DisbandedReason);
            foreach (WorldSession member in members)
                member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, note);
            log.Info($"{session.CharacterName} disbanded group {group.Id}.");
        }

        /// <summary>A Join result for the joiner (see <see cref="JoinSuccess"/>); the inviter's name is [*source*].</summary>
        private static void JoinResult(WorldSession joiner, uint code, WorldSession inviter)
        {
            joiner.EnqueueMessage(ZoneMessageOpcode.GroupsBase, GroupPackets.JoinResult(code, inviter?.PlayerGuid ?? 0ul,
                inviter?.CharacterName ?? "", joiner.PlayerGuid, joiner.CharacterName));
            log.Debug($"{joiner.CharacterName}: join result {code}.");
        }

        /// <summary>
        /// A result line for an inviter (Invite type 2, the code at +0x18): the client shows its own text for the code
        /// with the invitee's name as [*target*].
        /// </summary>
        private static void InviteResult(WorldSession inviter, uint code, ulong inviteeGuid, string inviteeName)
        {
            inviter.EnqueueMessage(ZoneMessageOpcode.GroupsBase,
                GroupPackets.InviteResult(code, inviter.PlayerGuid, inviter.CharacterName, inviteeGuid, inviteeName));
            log.Debug($"{inviter.CharacterName}: invite result {code} for {inviteeName}.");
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
                    inviter.EnqueueMessage(ZoneMessageOpcode.GroupsBase, GroupPackets.InviteRecord(RemoveInvitationSub, 0, RemoveExpiredTo,
                        inviter.PlayerGuid, inviter.CharacterName, invitee.PlayerGuid, invitee.CharacterName));
                if (!invitee.Disconnected)
                    invitee.EnqueueMessage(ZoneMessageOpcode.GroupsBase, GroupPackets.InviteRecord(RemoveInvitationSub, 0, RemoveExpiredFrom,
                        inviter.PlayerGuid, inviter.CharacterName, invitee.PlayerGuid, invitee.CharacterName));
                log.Info($"{inviter.CharacterName}'s group invitation to {invitee.CharacterName} expired.");
            }
        }

        /// <summary>
        /// AddGroup (0x11): the whole group (id, leader, members) for a player who joined or formed it; opens the Party window.
        /// </summary>
        private static byte[] BuildAddGroup(Group group)
        {
            List<WorldSession> members;
            lock (sync)
                members = group.Members.ToList();
            return GroupPackets.AddGroup(group.Id, group.Leader, members.Select(m => (m.PlayerGuid, MemberRecord(m, out _))).ToList());
        }

        /// <summary>
        /// A member record (reader FUN_142d3ebe0; the party frame's row fillers 0.589 FUN_142d56350 / FUN_142d560a0 =
        /// 0.695 FUN_142df4460 / FUN_142df41b0): player, 5 + 3 × u32, bool, bool, u32, u64 link id (+0xf8; compared with
        /// DAT_14543cd00, a .bss global with no writer = 0, so anything but 0 = "IsLinkdead"), u32 world (+0x100), u32
        /// zone name text (+0x104: looked up in the text table DAT_145431178 vtable +0x10 for the row's "ZoneName",
        /// "InvalidZone" when absent), u32 instance (+0x108; +0x100 and +0x108 both equal to the viewer's own record →
        /// "IsInZone", and "Distance" from the position, FUN_142df41b0), u32 world name text (+0x10c: the same text lookup
        /// for "WorldNameId", "InvalidWorld" when absent, FUN_142df4460), f32 × 3 position (+0x110), 4 × u32, u64, u32,
        /// u32, bool knocked out (+0x140), then the resources the frame's bars show: u32 n × {u32 type (1 health, 2 energy
        /// / mana, 11 armor), u32 current, u32 max}. The status fields are sent as defaults (agent-leads-sweep.md L122)
        /// [P]. Before 2026-10-17 the landscape id went in +0x104 (the frame showed "InvalidZone") and 0 in +0x10c.
        /// <paramref name="locationOffset"/> = where the world field starts (the social service hides the location from
        /// members on other worlds there, <see cref="GroupPackets.HideLocation"/>).
        /// </summary>
        /// <summary>
        /// The group bonus range (m): the party frame's "InGroupBonusRange" is distance² &lt; R² with R from a client setting
        /// (DAT_145431198 +0x3f0), default DAT_144c41210 = 200.0 (FUN_142df41b0) [P]. Server rules that share something
        /// with nearby group members should use this so they agree with what the frame shows.
        /// </summary>
        public const float BonusRange = 200f;

        /// <summary>Are two group members within <see cref="BonusRange"/> of each other on the same landscape (the frame's rule)?</summary>
        public static bool InBonusRange(WorldSession a, WorldSession b)
            => a != null && b != null && a.ZoneId == b.ZoneId && System.Numerics.Vector3.DistanceSquared(a.Position, b.Position) < BonusRange * BonusRange;

        internal static byte[] MemberRecord(WorldSession member, out int locationOffset)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            GroupPackets.WritePlayer(w, member.PlayerGuid, member.CharacterName, 0);
            for (int i = 0; i < 5 + 3; i++)
                w.Write(0u);
            w.Write(true);
            w.Write(false);
            w.Write(0u);
            w.Write(0ul);                                  // link id: 0 = connected
            locationOffset = output.Count;
            w.Write(1u);                                   // world (+0x100; with +0x108 decides "IsInZone")
            w.Write(Landscapes.Get(member.ZoneId)?.NameText ?? 0u);   // zone name text (+0x104, "ZoneName")
            w.Write(member.ZoneId);                        // instance (+0x108)
            w.Write(Landscapes.WorldNameText);             // world name text (+0x10c, "WorldNameId")
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
            w.Write(Health.Max(member));
            w.Write(2u);
            w.Write(WeaponAttacks.EnergyOf(member));
            w.Write(CharacterResources.EnergyMax);
            return output.ToArray();
        }

        /// <summary>What a member's row shows, to send an update only when it changed.</summary>
        private static (uint Hp, uint Energy, uint Zone, int X, int Z, bool Dead) RowOf(WorldSession m)
            => (Health.Current(m), WeaponAttacks.EnergyOf(m) / 10, m.ZoneId, (int)(m.Position.X / 5), (int)(m.Position.Z / 5), Health.IsDead(m));

        private static readonly Dictionary<WorldSession, (uint, uint, uint, int, int, bool)> lastRows = new();
        private static DateTime nextRowTick;

        /// <summary>
        /// Once a second: a member whose health, energy (in steps of 10), zone, position (5 m) or knocked-out state changed
        /// goes to the whole group again as UpdateMember (0x13, FUN_142d4fb50: u32, member — found by its guid), which the
        /// party frame shows (bars, distance, zone). With the social service the changed record goes to the service
        /// instead (<see cref="PublishRows"/>), which sends it to the members on every world.
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextRowTick)
                return;
            nextRowTick = now.AddSeconds(1);
            if (Social.SocialServices.Groups is { } service)
            {
                PublishRows(service);
                return;
            }
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
                    byte[] update = GroupPackets.UpdateMember(MemberRecord(m, out _));
                    foreach (WorldSession to in members.Where(x => !x.Disconnected))
                        to.EnqueueMessage(ZoneMessageOpcode.GroupsBase, update);
                }
            }
            lock (sync)
                foreach (WorldSession gone in lastRows.Keys.Where(s => s.Disconnected || !groupOf.ContainsKey(s)).ToList())
                    lastRows.Remove(gone);
        }

        /// <summary>
        /// With the social service, once a second: the record of every grouped player on this world whose row changed
        /// (same test as in-process) goes to the service (GroupMemberRow). Nothing while the service is unreachable (the
        /// rows are sent once it is back, as the remembered rows are only updated by a send that went out).
        /// </summary>
        private static void PublishRows(Social.IGroupService service)
        {
            if (!service.IsConnected)
                return;
            var grouped = new HashSet<WorldSession>();
            foreach (ulong character in service.GroupedHere())
            {
                WorldSession m = PlayerManager.FindByGuid(character);
                if (m == null || m.Disconnected)
                    continue;
                grouped.Add(m);
                var row = RowOf(m);
                lock (sync)
                    if (lastRows.TryGetValue(m, out var last) && last == row)
                        continue;
                byte[] record = MemberRecord(m, out int locationOffset);
                if (service.PublishRow(m, record, locationOffset))
                    lock (sync)
                        lastRows[m] = row;
            }
            lock (sync)
                foreach (WorldSession gone in lastRows.Keys.Where(s => s.Disconnected || !grouped.Contains(s)).ToList())
                    lastRows.Remove(gone);
        }

        /// <summary>
        /// The social service described a character's group (GroupStateForZone; world thread, from RemoteGroups): the
        /// next row check sends a fresh record; "joined" runs the "join a group" goal (as Achievements.GroupJoined did
        /// for the joiner and a first-time inviter); on world entry, a group this zone still had for the character but
        /// the service doesn't (the service restarted) is ended for the client too — RemoveGroup reason 2 "invalid" (ours).
        /// </summary>
        internal static void ServiceState(ulong character, uint previousGroup, uint groupId, byte flags)
        {
            WorldSession session = PlayerManager.FindByGuid(character);
            if (session == null)
                return;
            lock (sync)
                lastRows.Remove(session);
            if ((flags & GroupStateForZone.FlagEntry) != 0 && groupId == 0 && previousGroup != 0)
            {
                session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, GroupPackets.RemoveGroup(previousGroup, GroupPackets.InvalidGroupReason));
                log.Info($"{session.CharacterName}: group {previousGroup} is gone from the social service; the client was told.");
            }
            if ((flags & GroupStateForZone.FlagJoined) != 0)
                Achievements.GroupJoined(session);
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
        /// The player's group id, or null when not in one (with the social service: the service's group id, shared by the
        /// members on every world).
        /// </summary>
        public static uint? IdOf(WorldSession session)
        {
            if (Social.SocialServices.Groups is { } service)
                return service.Of(session.PlayerGuid)?.Id;
            lock (sync)
                return groupOf.TryGetValue(session, out Group group) ? group.Id : null;
        }

        /// <summary>
        /// GroupPacketInviteToGroupContent (sub 0x24; 0.695 handler FUN_142debfa0, reader FUN_142ddd7c0): {u8 sub, u32 2,
        /// u64 inviter, u32, u32, u32, 4 × u32}. The client asks "[*player*] is venturing into the Chaos Caverns for an
        /// epic adventure. Are you brave enough to join them?" (GAQ.GroupMemberContent, 574353) when the inviter is in its
        /// group; Yes answers with AcceptTeleport (sub 0x26). The three u32 and the four after them are not read by the
        /// dialog: 0 and (0, 0, 0, 1) (inferred). Zone-side in both modes: only members on this world are offered the
        /// trip (Caves.OfferGroupTrip uses <see cref="MembersOf"/>), since the trip is a teleport on this world.
        /// </summary>
        public static void SendContentInvite(WorldSession member, WorldSession inviter)
        {
            member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Build(w =>
            {
                w.Write(InviteToGroupContentSub);
                w.Write(2u);
                w.Write(inviter.PlayerGuid);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(1f);
            }));
        }

        /// <summary>
        /// The hidden "/party" sub-commands (0.695 command table 0x1446c3c48, FUN_142dd8450; no shipped UI calls their Lua
        /// natives) and the auto-party request (trace/0695/agent-l140.md §2, B3) [P for every layout below].
        /// </summary>
        public const byte AutoGroupSub = 3, SetGroupFlagsSub = 7, SetGroupDescriptionSub = 9, FindLookingForMoreSub = 0x0D,
            GetGroupInfoSub = 0x0E, JoinLookingForMoreSub = 0x10;

        /// <summary>The "mode" u32 after the sub in subs 3 / 7 / 9: 1 request (client → server), 2 applied (the client
        /// stores the value in its current group record), 3 a result message ("OnGroupSystemMessage" with the code's text).</summary>
        private const uint ModeApplied = 2, ModeMessage = 3;

        /// <summary>AutoGroup codes (FUN_143c1c810, UI.GroupPacketAutoGroup*): 0 Success, 1 ErrorFail, 2 ErrorBadPlayerId,
        /// 3 PlayerAlreadyInGroup, 4 ErrorWaitListed (also sets the client's wait-listed flag +0x1f9), 5 ErrorNoAvailableGroup.</summary>
        private const uint AutoGroupAlreadyInGroup = 3, AutoGroupNoAvailableGroup = 5;

        /// <summary>SetGroupFlags / SetGroupDescription codes (FUN_143c1c9b0 / FUN_143c1c990): 0 Success, 1 ErrorFail,
        /// 2 ErrorBadPlayerGroup, 3 ErrorPlayerNotGroupOwner.</summary>
        private const uint LfmSuccess = 0, LfmBadPlayerGroup = 2, LfmNotGroupOwner = 3;

        /// <summary>GetGroupInfo codes (FUN_143c1c850): 0 Success, 1 ErrorFail, 2 ErrorGroupNotFound, 3 ErrorGroupNotLookingForMore.</summary>
        private const uint GetGroupSuccess = 0, GetGroupNotFound = 2;

        /// <summary>JoinLookingForMore codes (FUN_143c1c8c0): 0 Success, 1 Fail, 2 AlreadyInGroup, 3 GroupNotFound, 4 WrongFaction.</summary>
        private const uint JoinLfmAlreadyInGroup = 2, JoinLfmGroupNotFound = 3;

        /// <summary>
        /// Answers the looking-for-more family minimally — live's party finder behind these was never shipped in
        /// Landmark's UI (no source describes it [I]), so nobody is matched; each answer is the client's own result text:
        /// <list type="bullet">
        /// <item>3 AutoGroup ("/party autoparty"; Lua Groups.GroupAutoGroup): reply {u32 3, u32 code, u32 0, u32 flags 0}
        /// (reader/handler FUN_142deb5f0: flags bit 2 / 3 would add guid lists): code 3 when grouped, else 5 "no available
        /// group". Never 4, which leaves the client wait-listed.</item>
        /// <item>7 SetGroupFlags {u32 1, u32 0, u8 flags} (writer FUN_142ddbfd0; "/party partyautoon|off, lfmon|off,
        /// lfmoptions"): the client sends it only with a current group record. Reply {u32 mode, u32 code, u8 flags}
        /// (FUN_142ded840): the leader's flags go to every member in mode 2 (stored in the group record +0x20), then mode 3
        /// code 0 to the leader ("…flags successfully set."); others get mode 3 code 3, or 2 when not grouped.</item>
        /// <item>9 SetGroupDescription {u32 1, u32 0, string} ("/party lfmoptions"): reply {u32 mode, u32 code, string}
        /// (reader FUN_142de4320, handler FUN_142ded700; mode 2 stores the text in the group record +0x28), same rules.</item>
        /// <item>0xD FindLookingForMoreGroup ("/party lfmsearch"): its reply 0x18 is read and dropped (FUN_142deb9a0), so
        /// nothing is sent.</item>
        /// <item>0xE GetGroupInfo {u32 1, u32 0, u32 group id} (writer FUN_142ddba90; "/party getpartyinfo N"): reply
        /// {u32 0, u32 code} (FUN_142deba20, always a system message): 0 for the player's own group, else 2 not found.
        /// (The GetGroupInfoReply 0x19 is read and dropped, FUN_142debaf0.)</item>
        /// <item>0x10 JoinLookingForMore {u32 1, u32 0, u32 group id} ("/party lfmjoin N"): reply {u32 0, u32 code}
        /// (FUN_142dec540): 2 already in a party, else 3 group not found.</item>
        /// </list>
        /// Zone-side in both modes (these subs are not forwarded, GroupMessages.IsForwardedSub); with the social service the
        /// group (leader, members) comes from its view and mode-2 updates reach the members on this world only. Nothing is
        /// stored server-side: the flags and description live only in the members' clients.
        /// </summary>
        private static void HandleLookingForMore(WorldSession session, byte sub, byte[] body)
        {
            uint groupId;
            ulong leader;
            List<WorldSession> members;
            if (Social.SocialServices.Groups is { } service)
            {
                var view = service.Of(session.PlayerGuid);
                groupId = view?.Id ?? 0;
                leader = view?.Leader ?? 0;
                members = view == null ? new List<WorldSession>()
                    : view.Members.Select(PlayerManager.FindByGuid).Where(m => m != null && !m.Disconnected).ToList();
            }
            else
                lock (sync)
                {
                    groupOf.TryGetValue(session, out Group group);
                    groupId = group?.Id ?? 0;
                    leader = group?.Leader ?? 0;
                    members = group?.Members.Where(m => !m.Disconnected).ToList() ?? new List<WorldSession>();
                }
            bool grouped = groupId != 0;
            bool isLeader = grouped && leader == session.PlayerGuid;
            switch (sub)
            {
                case AutoGroupSub:
                {
                    uint code = grouped ? AutoGroupAlreadyInGroup : AutoGroupNoAvailableGroup;
                    session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Build(w =>
                    {
                        w.Write(AutoGroupSub);
                        w.Write(ModeMessage);
                        w.Write(code);
                        w.Write(0u);
                        w.Write(0u);
                    }));
                    log.Info($"{session.CharacterName}: auto-party request answered with code {code}.");
                    break;
                }
                case SetGroupFlagsSub:
                {
                    byte flags = body.Length >= 9 ? body[8] : (byte)0;
                    byte[] Reply(uint mode, uint code, byte f) => Build(w =>
                    {
                        w.Write(SetGroupFlagsSub);
                        w.Write(mode);
                        w.Write(code);
                        w.Write(f);
                    });
                    if (isLeader)
                    {
                        byte[] applied = Reply(ModeApplied, LfmSuccess, flags);
                        foreach (WorldSession member in members)
                            member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, applied);
                        session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Reply(ModeMessage, LfmSuccess, flags));
                    }
                    else
                        session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Reply(ModeMessage, grouped ? LfmNotGroupOwner : LfmBadPlayerGroup, 0));
                    log.Info($"{session.CharacterName}: party flags 0x{flags:X2} {(isLeader ? "set" : "refused")}.");
                    break;
                }
                case SetGroupDescriptionSub:
                {
                    string description = "";
                    try
                    {
                        var reader = new GamePacketReader(body);
                        reader.ReadUInt();
                        reader.ReadUInt();
                        description = reader.ReadString();
                    }
                    catch (Exception)
                    {
                        log.Debug($"Unreadable party description from {session.CharacterName}: {BitConverter.ToString(body)}");
                    }
                    if (description.Length > 127)
                        description = description[..127];   // the client's StringFixed<128>
                    byte[] Reply(uint mode, uint code, string text) => Build(w =>
                    {
                        w.Write(SetGroupDescriptionSub);
                        w.Write(mode);
                        w.Write(code);
                        w.Write(text);
                    });
                    if (isLeader)
                    {
                        byte[] applied = Reply(ModeApplied, LfmSuccess, description);
                        foreach (WorldSession member in members)
                            member.EnqueueMessage(ZoneMessageOpcode.GroupsBase, applied);
                        session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Reply(ModeMessage, LfmSuccess, ""));
                    }
                    else
                        session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Reply(ModeMessage, grouped ? LfmNotGroupOwner : LfmBadPlayerGroup, ""));
                    log.Info($"{session.CharacterName}: party description {(isLeader ? "set" : "refused")}.");
                    break;
                }
                case FindLookingForMoreSub:
                    log.Debug($"{session.CharacterName}: /party lfmsearch {BitConverter.ToString(body)} (no reply: the client drops it).");
                    break;
                case GetGroupInfoSub:
                {
                    uint asked = body.Length >= 12 ? BitConverter.ToUInt32(body, 8) : 0;
                    uint code = grouped && asked == groupId ? GetGroupSuccess : GetGroupNotFound;
                    session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Build(w =>
                    {
                        w.Write(GetGroupInfoSub);
                        w.Write(0u);
                        w.Write(code);
                    }));
                    log.Info($"{session.CharacterName}: party info for {asked} answered with code {code}.");
                    break;
                }
                case JoinLookingForMoreSub:
                {
                    uint code = grouped ? JoinLfmAlreadyInGroup : JoinLfmGroupNotFound;
                    session.EnqueueMessage(ZoneMessageOpcode.GroupsBase, Build(w =>
                    {
                        w.Write(JoinLookingForMoreSub);
                        w.Write(0u);
                        w.Write(code);
                    }));
                    log.Info($"{session.CharacterName}: /party lfmjoin answered with code {code}.");
                    break;
                }
            }
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
