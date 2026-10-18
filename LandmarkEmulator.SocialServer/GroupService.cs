using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// Groups (parties) shared by every world (TASKS B.9; P4906, 26 Apr 2016: a group member can be removed from
    /// another world; social-service.md §10). The zone's Game/Groups.cs moved here with
    /// the same rules and the same client packets (<see cref="GroupPackets"/>, used by both; GroupsBase 0x2B "same" in
    /// 0.695, coverage-695.md §0x2B):
    /// <list type="bullet">
    /// <item>Invite (1, "/invite Name" or the right-click menu's guid): the named player online on <b>any</b> world;
    /// refused (Invite result line to the inviter) when not found (9), already grouped (14), the inviter's group is full
    /// (5) or an invitation is pending (13); the invitee ignoring the inviter gets nothing and the inviter a line. The
    /// invitee sees the popup (Invite type 1); invitations lapse after 2 minutes (RemoveInvitation 3 / 4 to both).</item>
    /// <item>Join (2): the popup's answer; Accept forms the group (the inviter leads) or joins it (max 4, a party is
    /// four people, 2016 wiki): AddGroup to the newcomer (and to a first-time inviter), AddMember to the
    /// others, Join result 0; Decline: result 16 to the inviter.</item>
    /// <item>Leave (4), Kick (5, leader only), SetGroupOwner (8, leader only), Disband (6, leader only): RemoveMember /
    /// RemoveGroup / SetGroupOwner to every member wherever they are; a group left with one member ends ("too small");
    /// a leader who leaves passes the lead to the next member.</item>
    /// <item>Party chat (channel 8) goes to the group's members on every world (<see cref="ChatAudience"/>).</item>
    /// </list>
    /// What needs the game or positions stays in the zones. Each zone writes the party-frame record of the members it
    /// hosts (GroupMemberRow: health, energy, landscape, position) and the service passes it on as bytes: as is to
    /// members on the same world, with the world field cleared and the position zeroed to members on another world
    /// (<see cref="GroupPackets.HideLocation"/>: the client then shows them not "IsInZone"), so positions never leave
    /// their world. Content offers (the Chaos Cavern group trip, InviteToGroupContent 0x24 / AcceptTeleport 0x26), group
    /// loot and gathering shares stay zone-side for the members on the same world (GroupStateForZone tells the zone who
    /// is in the group).
    /// <para>
    /// Logging out or changing world (ours, follows the presence grace, §5.5): a member who leaves their world stays in
    /// the group for the grace (30 s); the others see them "linkdead" (<see cref="GroupPackets.MarkLinkdead"/>), and when
    /// they enter a world within the grace they get their group again (AddGroup) — a group survives a world change.
    /// When the grace runs out they leave it (reason 3), as a logout did before.
    /// </para>
    /// Groups are kept in memory only (live's were not persistent either as far as traced; a service restart ends them,
    /// and the zones then tell their clients, GroupStateForZone FlagEntry).
    /// </summary>
    public sealed class GroupService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>A party holds four people (2016 wiki, Party page); the client has no size constant.</summary>
        public const int MaxMembers = 4;
        private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(2);

        private sealed class Group
        {
            public uint Id;
            public ulong Leader;
            /// <summary>Character guids in joining order (the first is the next leader when the leader leaves).</summary>
            public readonly List<ulong> Members = new();
        }

        /// <summary>A member's latest party-frame record from the zone that hosts them.</summary>
        private sealed class Row
        {
            public byte[] Record;
            public int LocationOffset;
            /// <summary>The world whose zone wrote it.</summary>
            public uint WorldId;
            /// <summary>The member is in the presence leave grace (between worlds / logging out).</summary>
            public bool Linkdead;
            /// <summary>The last location-hidden copy members on other worlds were sent (to send them only changes).</summary>
            public byte[] LastHidden;
        }

        private readonly SocialHub hub;
        private readonly Dictionary<uint, Group> groups = new();

        /// <summary>Groups that exist now (work loop; the admin page's status).</summary>
        public int GroupCount => groups.Count;
        private readonly Dictionary<ulong, Group> groupOf = new();
        /// <summary>Invitee → (inviter, when).</summary>
        private readonly Dictionary<ulong, (ulong Inviter, DateTime At)> invites = new();
        private readonly Dictionary<ulong, Row> rows = new();
        /// <summary>The connection each member last got the whole group (AddGroup) on: a new connection (world change,
        /// relog) gets it again.</summary>
        private readonly Dictionary<ulong, ulong> groupSentOn = new();
        private uint nextGroupId;

        public GroupService(SocialHub hub) => this.hub = hub;

        /// <summary>A GroupsBase request handed on by the zone (invite, join, leave, kick, set owner, disband).</summary>
        public void Handle(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"Group request 0x{request.Sub:X} for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            byte[] body = request.Body ?? Array.Empty<byte>();
            switch (request.Sub)
            {
                case GroupPackets.InviteSub:
                    Invite(player, body);
                    break;
                case GroupPackets.JoinSub:
                    // Join {u32 1, u32 0, u32 response (1 accept, 2 decline), u32, the invite as received} (writer FUN_142d3d9c0).
                    Answer(player, body.Length >= 12 && BitConverter.ToUInt32(body, 8) == 1);
                    break;
                case GroupPackets.LeaveSub:
                    Remove(player.Guid, GroupPackets.LeftReason, 0);
                    break;
                case GroupPackets.KickSub:
                    // Kick {u32, u32, u64 kicker, u64 kicked, u8}.
                    if (body.Length >= 24)
                        Kick(player, BitConverter.ToUInt64(body, 16));
                    break;
                case GroupPackets.SetOwnerSub:
                    // SetGroupOwner {u32 state, u32 reason, u64 new leader, u32}.
                    if (body.Length >= 16)
                        SetOwner(player, BitConverter.ToUInt64(body, 8));
                    break;
                case GroupPackets.DisbandSub:
                    Disband(player);
                    break;
                default:
                    log.Debug($"Group request 0x{request.Sub:X} from {player}: not handled here.");
                    break;
            }
        }

        // ---- Invitations --------------------------------------------------------------------------------------------

        /// <summary>
        /// Invite {u32, u32, u32, u64, u32, inviter player block, invitee player block, …}: "/invite Name" names the
        /// player, the right-click menu (Groups.GroupInviteByGuid) gives only the guid.
        /// </summary>
        private void Invite(OnlineCharacter inviter, byte[] body)
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
                log.Debug($"Unreadable group invite from {inviter}: {BitConverter.ToString(body)}");
                return;
            }

            OnlineCharacter target = name.Length > 0
                ? hub.Presence.Everyone().FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                : hub.Presence.Find(targetGuid) is { Connected: true } byGuid ? byGuid : null;
            if (target == null)
            {
                InviteResult(inviter, GroupPackets.InviteBadInvitee, targetGuid, name);
                return;
            }
            if (target.Guid == inviter.Guid)
                return;
            if (hub.Ignores.Ignoring(target.Guid, inviter.Guid))
            {
                hub.SendTo(inviter, ClientPackets.SystemLine($"{target.Name} is not accepting invitations from you."));
                log.Info($"{target} ignores {inviter}: group invitation refused.");
                return;
            }
            DateTime now = DateTime.UtcNow;
            uint refusal = uint.MaxValue;
            if (groupOf.ContainsKey(target.Guid))
                refusal = GroupPackets.InviteAlreadyInGroup;
            else if (groupOf.TryGetValue(inviter.Guid, out Group own) && own.Members.Count >= MaxMembers)
                refusal = GroupPackets.InviteGroupFull;
            else if (invites.TryGetValue(target.Guid, out var pending) && now - pending.At <= InviteLifetime)
                refusal = GroupPackets.InviteAlreadyInvited;
            else
                invites[target.Guid] = (inviter.Guid, now);
            if (refusal != uint.MaxValue)
            {
                InviteResult(inviter, refusal, target.Guid, target.Name);
                log.Info($"{inviter} could not invite {target}: invite result {refusal}.");
                return;
            }
            hub.SendTo(target, Packet(GroupPackets.Invitation(inviter.Guid, inviter.Name, target.Guid, target.Name)));
            InviteResult(inviter, GroupPackets.InviteSuccess, target.Guid, target.Name);
            log.Info($"{inviter} (world {inviter.WorldId}) invited {target} (world {target.WorldId}) to a group.");
        }

        /// <summary>The invitee answered the popup: Accept joins (or forms) the inviter's group, anything else declines.</summary>
        private void Answer(OnlineCharacter player, bool accept)
        {
            bool valid = invites.Remove(player.Guid, out var invite) && DateTime.UtcNow - invite.At <= InviteLifetime;
            if (!valid)
            {
                if (accept)
                    JoinResult(player, GroupPackets.JoinBadInvitation, invite.Inviter);
                return;
            }
            OnlineCharacter inviter = hub.Presence.Find(invite.Inviter);
            if (!accept)
            {
                if (inviter is { Connected: true })
                    InviteResult(inviter, GroupPackets.InviteDeclined, player.Guid, player.Name);
                log.Info($"{player} declined {NameOf(invite.Inviter)}'s group invitation.");
                return;
            }
            if (inviter is not { Connected: true } || groupOf.ContainsKey(player.Guid))
                return;

            if (!groupOf.TryGetValue(inviter.Guid, out Group group))
            {
                group = new Group { Id = ++nextGroupId, Leader = inviter.Guid };
                group.Members.Add(inviter.Guid);
                groups[group.Id] = group;
                groupOf[inviter.Guid] = group;
            }
            if (group.Members.Count >= MaxMembers)
            {
                JoinResult(player, GroupPackets.JoinGroupFull, inviter.Guid);
                return;
            }
            List<ulong> existing = group.Members.ToList();
            group.Members.Add(player.Guid);
            groupOf[player.Guid] = group;

            // The newcomer gets the whole group; members already in it get the newcomer (a first-time inviter gets the
            // whole group too, as it is new to them as well).
            SendGroup(player, group);
            foreach (OnlineCharacter member in Connected(existing))
                if (existing.Count == 1)
                    SendGroup(member, group);
                else if (RecordFor(player.Guid, member.WorldId) is byte[] record)
                    hub.SendTo(member, Packet(GroupPackets.AddMember(group.Id, record)));
            JoinResult(player, GroupPackets.JoinSuccess, inviter.Guid);
            PushState(group, joined: existing.Count == 1 ? new[] { player.Guid, existing[0] } : new[] { player.Guid });
            log.Info($"{player} joined group {group.Id} ({group.Members.Count} members, worlds "
                + $"{string.Join("/", Connected(group.Members).Select(m => m.WorldId).Distinct())}).");
        }

        /// <summary>Once a second: invitations nobody answered within 2 minutes lapse (RemoveInvitation 3 to the inviter, 4 to the invitee).</summary>
        public void Tick()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var (invitee, invite) in invites.Where(i => now - i.Value.At > InviteLifetime).ToList())
            {
                invites.Remove(invitee);
                string inviterName = NameOf(invite.Inviter), inviteeName = NameOf(invitee);
                if (hub.Presence.Find(invite.Inviter) is { Connected: true } inviter)
                    hub.SendTo(inviter, Packet(GroupPackets.InviteRecord(GroupPackets.RemoveInvitationSub, 0, GroupPackets.RemoveExpiredTo,
                        invite.Inviter, inviterName, invitee, inviteeName)));
                if (hub.Presence.Find(invitee) is { Connected: true } invited)
                    hub.SendTo(invited, Packet(GroupPackets.InviteRecord(GroupPackets.RemoveInvitationSub, 0, GroupPackets.RemoveExpiredFrom,
                        invite.Inviter, inviterName, invitee, inviteeName)));
                log.Info($"{inviterName}'s group invitation to {inviteeName} expired.");
            }
        }

        // ---- Leaving, kicking, leader, disbanding ----------------------------------------------------------------------

        /// <summary>
        /// Takes a character out of their group (left, kicked, or offline after the grace). The others get RemoveMember
        /// with the reason; a group left with one member is removed (RemoveGroup "too small"); when the leader left, the
        /// next member leads and everyone is told (SetGroupOwner).
        /// </summary>
        private void Remove(ulong character, uint reason, ulong by)
        {
            invites.Remove(character);
            if (!groupOf.Remove(character, out Group group))
                return;
            group.Members.Remove(character);
            groupSentOn.Remove(character);
            bool wasLeader = group.Leader == character;
            if (wasLeader && group.Members.Count > 0)
                group.Leader = group.Members[0];
            List<ulong> rest = group.Members.ToList();
            if (rest.Count <= 1)
            {
                groups.Remove(group.Id);
                foreach (ulong member in rest)
                {
                    groupOf.Remove(member);
                    groupSentOn.Remove(member);
                }
            }
            byte[] removed = Packet(GroupPackets.RemoveMember(character, reason, by));
            if (hub.Presence.Find(character) is { Connected: true } self)
            {
                hub.SendTo(self, removed);
                PushState(self, null, 0);
            }
            List<OnlineCharacter> others = Connected(rest);
            hub.SendTo(others, rest.Count <= 1 ? Packet(GroupPackets.RemoveGroup(group.Id, GroupPackets.TooSmallReason)) : removed);
            if (rest.Count > 1 && wasLeader)
                hub.SendTo(others, Packet(GroupPackets.SetOwner(group.Leader)));
            foreach (OnlineCharacter member in others)
                PushState(member, rest.Count <= 1 ? null : group, 0);
            log.Info($"{NameOf(character)} left group {group.Id} (reason {reason}).");
        }

        /// <summary>Kick: leader only, of another member.</summary>
        private void Kick(OnlineCharacter leader, ulong kicked)
        {
            if (!groupOf.TryGetValue(leader.Guid, out Group group) || group.Leader != leader.Guid
                || kicked == leader.Guid || !group.Members.Contains(kicked))
                return;
            Remove(kicked, GroupPackets.KickedReason, leader.Guid);
        }

        /// <summary>SetGroupOwner: leader only; every member gets state 2 "applied" (the crown moves).</summary>
        private void SetOwner(OnlineCharacter leader, ulong newLeader)
        {
            if (!groupOf.TryGetValue(leader.Guid, out Group group) || group.Leader != leader.Guid || !group.Members.Contains(newLeader))
                return;
            group.Leader = newLeader;
            hub.SendTo(Connected(group.Members), Packet(GroupPackets.SetOwner(newLeader)));
            PushState(group);
            log.Info($"{leader} made {NameOf(newLeader)} leader of group {group.Id}.");
        }

        /// <summary>The leader ends the group for everyone (RemoveGroup "disbanded").</summary>
        private void Disband(OnlineCharacter leader)
        {
            if (!groupOf.TryGetValue(leader.Guid, out Group group) || group.Leader != leader.Guid)
                return;
            groups.Remove(group.Id);
            foreach (ulong member in group.Members)
            {
                groupOf.Remove(member);
                groupSentOn.Remove(member);
            }
            List<OnlineCharacter> members = Connected(group.Members);
            hub.SendTo(members, Packet(GroupPackets.RemoveGroup(group.Id, GroupPackets.DisbandedReason)));
            foreach (OnlineCharacter member in members)
                PushState(member, null, 0);
            log.Info($"{leader} disbanded group {group.Id}.");
        }

        // ---- Party frames -------------------------------------------------------------------------------------------

        /// <summary>
        /// GroupMemberRow: a member's record from their zone. Kept for every character that sent one (an invite / join
        /// sends it before the request); for a grouped member a changed record goes to the group as UpdateMember — as is
        /// to members on the member's world, location hidden (and only when that copy changed) to members elsewhere.
        /// A row from a world the character is no longer connected through (a late row after a world change) is dropped.
        /// </summary>
        public void RowReceived(WorldLink world, GroupMemberRow message)
        {
            if (message.Record.Length == 0 || hub.Presence.Find(message.CharacterGuid) is not { Connected: true } member
                || member.World != world)
                return;
            rows.TryGetValue(message.CharacterGuid, out Row row);
            bool unchanged = row != null && !row.Linkdead && row.WorldId == message.WorldId
                && row.Record.AsSpan().SequenceEqual(message.Record);
            if (row == null)
                rows[message.CharacterGuid] = row = new Row();
            row.Record = message.Record;
            row.LocationOffset = (int)message.LocationOffset;
            row.WorldId = message.WorldId;
            row.Linkdead = false;
            if (!unchanged && groupOf.TryGetValue(message.CharacterGuid, out Group group))
                FanOutRow(message.CharacterGuid, group, row);
        }

        /// <summary>UpdateMember for one member to the whole group (see <see cref="RowReceived"/>).</summary>
        private void FanOutRow(ulong character, Group group, Row row)
        {
            uint visibleOn = VisibleWorld(character, row);
            byte[] hidden = Hidden(row);
            bool hiddenChanged = row.LastHidden == null || !row.LastHidden.AsSpan().SequenceEqual(hidden);
            row.LastHidden = hidden;
            var same = new List<OnlineCharacter>();
            var elsewhere = new List<OnlineCharacter>();
            foreach (OnlineCharacter to in Connected(group.Members))
                (visibleOn != 0 && to.WorldId == visibleOn ? same : elsewhere).Add(to);
            if (same.Count > 0)
                hub.SendTo(same, Packet(GroupPackets.UpdateMember(row.Record)));
            if (elsewhere.Count > 0 && hiddenChanged)
                hub.SendTo(elsewhere, Packet(GroupPackets.UpdateMember(hidden)));
        }

        /// <summary>
        /// The world on which a member's location may be shown: the world whose zone wrote the row, while the member is
        /// connected there; 0 (nowhere) while linkdead or when the row is from the world they just left.
        /// </summary>
        private uint VisibleWorld(ulong character, Row row)
            => !row.Linkdead && hub.Presence.Find(character) is { Connected: true } member && member.WorldId == row.WorldId ? row.WorldId : 0;

        private static byte[] Hidden(Row row)
            => GroupPackets.HideLocation(row.Linkdead ? GroupPackets.MarkLinkdead(row.Record, row.LocationOffset) : row.Record, row.LocationOffset);

        /// <summary>A member's record as a viewer on <paramref name="viewerWorld"/> gets it; null when no zone sent one yet.</summary>
        private byte[] RecordFor(ulong character, uint viewerWorld)
        {
            if (!rows.TryGetValue(character, out Row row))
                return null;
            uint visibleOn = VisibleWorld(character, row);
            return visibleOn != 0 && viewerWorld == visibleOn ? row.Record : Hidden(row);
        }

        /// <summary>AddGroup (the whole group, records as this viewer may see them); remembers the connection it went to.</summary>
        private void SendGroup(OnlineCharacter to, Group group)
        {
            var members = new List<(ulong, byte[])>();
            foreach (ulong member in group.Members)
                if (RecordFor(member, to.WorldId) is byte[] record)
                    members.Add((member, record));
                else
                    log.Warn($"Group {group.Id}: no party-frame record for {NameOf(member)} yet; left out of {to}'s AddGroup.");
            hub.SendTo(to, Packet(GroupPackets.AddGroup(group.Id, group.Leader, members)));
            groupSentOn[to.Guid] = to.ConnectionId;
        }

        // ---- Presence -----------------------------------------------------------------------------------------------

        /// <summary>
        /// A character entered a world (PresenceService bootstrap; also after a world link's or this service's restart):
        /// its world learns its group (GroupStateForZone with FlagEntry, also "none"); on a new connection (world change,
        /// relog within the grace) the client gets its group again; a member who was linkdead is shown connected.
        /// </summary>
        public void Entered(OnlineCharacter character)
        {
            groupOf.TryGetValue(character.Guid, out Group group);
            PushState(character, group, GroupStateForZone.FlagEntry);
            if (group == null)
                return;
            if (!groupSentOn.TryGetValue(character.Guid, out ulong sentOn) || sentOn != character.ConnectionId)
                SendGroup(character, group);
            if (rows.TryGetValue(character.Guid, out Row row) && row.Linkdead)
            {
                row.Linkdead = false;
                FanOutRow(character.Guid, group, row);
            }
        }

        /// <summary>A character's leave grace began (logout, world change, its world's link lost): shown linkdead to the group.</summary>
        public void Linkdead(OnlineCharacter character)
        {
            if (groupOf.TryGetValue(character.Guid, out Group group) && rows.TryGetValue(character.Guid, out Row row) && !row.Linkdead)
            {
                row.Linkdead = true;
                FanOutRow(character.Guid, group, row);
            }
        }

        /// <summary>The grace ran out: the character leaves its group (reason 3, as a logout did) and its row is forgotten.</summary>
        public void WentOffline(ulong character)
        {
            Remove(character, GroupPackets.LeftReason, 0);
            rows.Remove(character);
            groupSentOn.Remove(character);
        }

        /// <summary>Party chat (channel 8): the sender's group members connected on any world (nobody when not grouped).</summary>
        public List<OnlineCharacter> ChatAudience(ulong sender)
            => groupOf.TryGetValue(sender, out Group group) ? Connected(group.Members) : new List<OnlineCharacter>();

        // ---- Helpers ------------------------------------------------------------------------------------------------

        /// <summary>GroupStateForZone to every connected member (after a change of members or leader).</summary>
        private void PushState(Group group, ICollection<ulong> joined = null)
        {
            foreach (OnlineCharacter member in Connected(group.Members))
                PushState(member, group, joined != null && joined.Contains(member.Guid) ? GroupStateForZone.FlagJoined : (byte)0);
        }

        private void PushState(OnlineCharacter character, Group group, byte flags)
        {
            if (character is not { Connected: true })
                return;
            hub.SendService(character.World, new GroupStateForZone
            {
                CharacterGuid = character.Guid,
                GroupId = group?.Id ?? 0,
                Leader = group?.Leader ?? 0,
                Members = group?.Members.ToList() ?? new List<ulong>(),
                Flags = flags
            });
        }

        private List<OnlineCharacter> Connected(IEnumerable<ulong> characters)
            => characters.Select(hub.Presence.Find).Where(c => c is { Connected: true }).ToList();

        private string NameOf(ulong character)
            => character == 0 ? "" : hub.Presence.Find(character)?.Name ?? hub.Store.GetCharacter(character)?.Name ?? "";

        private void InviteResult(OnlineCharacter inviter, uint code, ulong inviteeGuid, string inviteeName)
        {
            hub.SendTo(inviter, Packet(GroupPackets.InviteResult(code, inviter.Guid, inviter.Name, inviteeGuid, inviteeName)));
            log.Debug($"{inviter}: invite result {code} for {inviteeName}.");
        }

        private void JoinResult(OnlineCharacter joiner, uint code, ulong inviter)
        {
            hub.SendTo(joiner, Packet(GroupPackets.JoinResult(code, inviter, NameOf(inviter), joiner.Guid, joiner.Name)));
            log.Debug($"{joiner}: join result {code}.");
        }

        private static byte[] Packet(byte[] body) => GroupPackets.WithOpcode(body);

        /// <summary>Reads a player block and returns the first name.</summary>
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
    }
}
