using LandmarkEmulator.Database.Social.Model;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// Guilds for every world (social-service.md §5.1 item 4, §6 step 5), moved out of the world server's Game/Guilds.cs
    /// with the same client behaviour; now members on any world see each other online and in the roster (Location
    /// "world:zone"), as live's shared guilds did. Client side (traced, see Game/Guilds.cs):
    /// GuildBase 0x83 (wire bytes 83 00, u16 sub; client dispatcher FUN_142e539f0):
    /// <list type="bullet">
    /// <item>client → server, handed on by the zone as ClientRequestForward (family 0x83): 1 Create {string}, 2 Disband,
    /// 4 Invite {u64 guild, u64, string name}, 5 InviteReply {u64, u8 accept}, 0x0A ChangeMemberRank, 0x0B Motd {u64,
    /// string}, 0x0C Kick {u64 guild, u64 member, string}, 0x0D Quit, 0x0E UpdateRank, 0x0F Description, 0x10 Mute, 0x11
    /// AppDataField.</item>
    /// <item>server → client: 2 Disband, 0x0A ChangeMemberRank, 0x0B Motd, 0x0E UpdateRank, 0x13 GuildData, 0x15 AddMember,
    /// 0x16 RemoveMember, 0x17 UpdateInvitation, 0x18 MemberOnlineStatus, 0x19 GuildNotification, and (OPEN-LEADS L174)
    /// 0x12 GuildDataFull (empty, at entry for a player in no guild), 0x14 GuildInvitations (at entry), 0x1B
    /// GuildRemoveLogUpdate (News rows the cap drops). The invitee's popup is queued by the client itself from an
    /// invitation of type 1 addressed to it (accepting sends InviteReply), as WorldServer Game/Guilds.cs.</item>
    /// </list>
    /// Online means: counted online by presence (connected, or in its leave grace on a world change), on any world.
    /// The zone keeps what it needs for guild chat (while chat lines still go through the zone) from
    /// SocialStateForZone (guild id / name, officer-chat and mute bits), pushed whenever they change; the "join a guild"
    /// goal runs in the zone on GuildJoined. Mutes are kept while the service runs, as before.
    /// </summary>
    public sealed class GuildService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly byte[] GuildBase = { 0x83, 0x00 };

        public const ushort CreateSub = 1, DisbandSub = 2, InviteSub = 4, InviteReplySub = 5, InviteRevokeSub = 6,
            ChangeMemberRankSub = 0x0A,
            MotdSub = 0x0B, KickSub = 0x0C, QuitSub = 0x0D, UpdateRankSub = 0x0E, DescriptionSub = 0x0F, MuteSub = 0x10,
            AppDataFieldSub = 0x11, DataFullSub = 0x12, DataSub = 0x13, InvitationsSub = 0x14, AddMemberSub = 0x15,
            RemoveMemberSub = 0x16, UpdateInvitationSub = 0x17, OnlineStatusSub = 0x18, NotificationSub = 0x19, RemoveLogSub = 0x1B;
        public const uint LeaderRank = 1, MemberRank = 2, OfficerRank = 3;
        /// <summary>Chat channels (ChatChannels.txt rows 6 / 7, wire ids 5 / 6): guild chat and officer chat.</summary>
        public const ushort ChatChannel = 5, OfficerChatChannel = 6;
        public const string JoinOfficerChat = "JoinOfficerChat", MuteGuildChat = "MuteGuildChat", MuteOfficerChat = "MuteOfficerChat";
        /// <summary>Client constant Guild.MaxMembers.</summary>
        private const int MaxMembers = 250;
        private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(5);

        private const byte NotifyGeneralFail = 0, NotifyCreated = 1, NotifyCreateFailed = 2, NotifyNameTaken = 3,
            NotifyInvited = 4, NotifyPlayerNotFound = 5, NotifyPlayerInGuild = 7, NotifyAlreadyInvited = 8,
            NotifyAlreadyInGuild = 13, NotifyChangeRankFailed = 14;

        private static readonly string[] OfficerPermissions =
            { "CreateGuildInvite", "ViewGuildInvites", "KickFromGuild", "ChangeMemberRank", "UpdateMotd", "ViewGuildLog",
              JoinOfficerChat, MuteGuildChat };

        public static readonly string[] AllPermissions =
        {
            "DisbandGuild", "CreateGuildInvite", "ReplyGuildPetition", "RevokeGuildInvite", "ViewGuildInvites",
            "TransferGuildLeadership", "ChangeMemberRank", "KickFromGuild", "ViewGuildLog", "ModifyGuildLog",
            "ModifyRanks", "UpdateMotd", "UpdateDesc", "RenameGuild", JoinOfficerChat, MuteGuildChat, MuteOfficerChat,
        };

        /// <summary>A guild rank: id (members carry it), level (1 highest), name and permission names.</summary>
        public sealed class Rank
        {
            public uint Id { get; set; }
            public uint Level { get; set; }
            public string Name { get; set; }
            public List<string> Permissions { get; set; } = new();
        }

        private sealed class Guild
        {
            public SocialGuildModel Model;
            public List<Rank> Ranks = DefaultRanks();
            /// <summary>The News tab's rows, oldest first (<see cref="AddLog"/>).</summary>
            public List<LogEntry> Log = new();
            /// <summary>Character guid → (name, rank).</summary>
            public readonly Dictionary<ulong, (string Name, uint Rank)> Members = new();
        }

        private readonly SocialHub hub;
        private readonly Dictionary<ulong, Guild> guilds = new();
        private readonly Dictionary<ulong, ulong> guildOf = new();
        /// <summary>Invitee guid → (guild, inviter name, inviter guid, when).</summary>
        private readonly Dictionary<ulong, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At)> invites = new();

        /// <summary>
        /// UpdateInvitation states (client FUN_142e56100; as WorldServer Game/Guilds.cs): 0 adds the invitation to the
        /// player's list; 1 removes it with "…has been revoked." (9405), 2 silently, 3 with "You have declined…" (9415)
        /// for the invitee, "[target] has declined…" (9407) for anyone else.
        /// </summary>
        private const uint InvitationOpen = 0, InvitationRevoked = 1, InvitationClosed = 2, InvitationDeclined = 3;
        /// <summary>(guild, channel, member) → muted until (UTC; MaxValue = until unmuted).</summary>
        private readonly Dictionary<(ulong Guild, ushort Channel, ulong Member), DateTime> mutes = new();
        private ulong nextInviteId = 1;

        public GuildService(SocialHub hub) => this.hub = hub;

        /// <summary>Loads every guild and membership (at start).</summary>
        public void Load()
        {
            foreach (SocialGuildModel model in hub.Store.GetGuilds())
                guilds[model.Id] = new Guild { Model = model, Ranks = ParseRanks(model.Ranks), Log = ParseLog(model.Log) };
            IReadOnlyList<SocialGuildMemberModel> members = hub.Store.GetGuildMembers();
            var names = hub.Store.GetCharacters(members.Select(m => m.CharacterGuid).ToList()).ToDictionary(c => c.Guid, c => c.Name);
            foreach (SocialGuildMemberModel member in members)
                if (guilds.TryGetValue(member.GuildId, out Guild guild))
                {
                    guild.Members[member.CharacterGuid] = (names.GetValueOrDefault(member.CharacterGuid, "?"), member.RankId);
                    guildOf[member.CharacterGuid] = member.GuildId;
                }
            log.Info($"Loaded {guilds.Count} guild(s), {guildOf.Count} member(s).");
        }

        private static List<Rank> DefaultRanks() => new()
        {
            new Rank { Id = LeaderRank, Level = 1, Name = "Leader", Permissions = AllPermissions.ToList() },
            new Rank { Id = OfficerRank, Level = 2, Name = "Officer", Permissions = OfficerPermissions.ToList() },
            new Rank { Id = MemberRank, Level = 3, Name = "Member", Permissions = new() { "ViewGuildInvites" } },
        };

        private static List<Rank> ParseRanks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return DefaultRanks();
            try
            {
                var ranks = System.Text.Json.JsonSerializer.Deserialize<List<Rank>>(json);
                if (ranks == null || !ranks.Any(r => r.Id == LeaderRank) || !ranks.Any(r => r.Id == MemberRank))
                    return DefaultRanks();
                foreach (Rank rank in ranks)
                    if (rank.Id == LeaderRank)
                        rank.Permissions = AllPermissions.ToList();
                    else if (rank.Id == OfficerRank && !rank.Permissions.Any(x => x is JoinOfficerChat or MuteGuildChat or MuteOfficerChat))
                        rank.Permissions.AddRange(new[] { JoinOfficerChat, MuteGuildChat });
                return ranks;
            }
            catch (System.Text.Json.JsonException)
            {
                return DefaultRanks();
            }
        }

        // ---- What the zone needs ---------------------------------------------------------------------------------------

        /// <summary>The guild fields of SocialStateForZone for a character: guild id, flags, guild name.</summary>
        public (ulong GuildId, uint Flags, string Name) ZoneState(ulong character)
        {
            Guild guild = GuildOf(character);
            if (guild == null)
                return (0, 0, "");
            uint flags = 0;
            if (Has(guild, character, JoinOfficerChat))
                flags |= SocialStateForZone.JoinOfficerChat;
            if (IsMuted(guild.Model.Id, ChatChannel, character))
                flags |= SocialStateForZone.GuildChatMuted;
            if (IsMuted(guild.Model.Id, OfficerChatChannel, character))
                flags |= SocialStateForZone.OfficerChatMuted;
            return (guild.Model.Id, flags, guild.Model.Name);
        }

        private bool IsMuted(ulong guild, ushort channel, ulong member)
        {
            if (!mutes.TryGetValue((guild, channel, member), out DateTime until))
                return false;
            if (until > DateTime.UtcNow)
                return true;
            mutes.Remove((guild, channel, member));
            return false;
        }

        /// <summary>
        /// Who hears a guild (5) or officer (6) line from <paramref name="sender"/>: the guild's members online on any
        /// world (officer chat: those whose rank has JoinOfficerChat, and only when the sender's has it). Empty with
        /// <paramref name="muted"/> set when the sender is muted on that channel.
        /// </summary>
        public List<OnlineCharacter> ChatAudience(ulong sender, ushort channel, out bool muted)
        {
            muted = false;
            Guild guild = GuildOf(sender);
            if (guild == null)
                return new List<OnlineCharacter>();
            if (IsMuted(guild.Model.Id, channel, sender))
            {
                muted = true;
                return new List<OnlineCharacter>();
            }
            if (channel == OfficerChatChannel)
                return Has(guild, sender, JoinOfficerChat)
                    ? Online(guild.Members.Keys.Where(m => Has(guild, m, JoinOfficerChat)))
                    : new List<OnlineCharacter>();
            return Online(guild.Members.Keys);
        }

        /// <summary>The zone state of these characters changed (membership, rank, mutes): push it to their worlds.</summary>
        private void PushZoneState(IEnumerable<ulong> characters)
        {
            foreach (ulong character in characters)
                hub.Friends.PushStateForZone(character);
        }

        // ---- Presence ----------------------------------------------------------------------------------------------

        /// <summary>
        /// A character entered a world (presence bootstrap): an invitation still open shows again; a member gets the
        /// guild and its ranks; <paramref name="announce"/> (it wasn't online before) tells guildmates it came online.
        /// </summary>
        public void Entered(OnlineCharacter character, bool announce)
        {
            // The open invitations (always, so a relog in the same client drops stale ones): the client queues the popup
            // again for one still valid. A player in no guild gets an empty GuildDataFull, which empties the client's
            // guild list (a guild lost while at character select would otherwise stay in the window).
            var open = new List<(Guild Guild, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) Invite)>();
            if (invites.TryGetValue(character.Guid, out var pending) && DateTime.UtcNow - pending.At <= InviteLifetime
                && guilds.TryGetValue(pending.Guild, out Guild invitedTo) && !guildOf.ContainsKey(character.Guid))
                open.Add((invitedTo, pending));
            Guild guild = GuildOf(character.Guid);
            if (guild == null)
                hub.SendTo(character, EmptyDataFull());
            hub.SendTo(character, BuildInvitations(open, character.Guid, character.Name));
            if (guild == null)
                return;
            guild.Members[character.Guid] = (character.Name, guild.Members[character.Guid].Rank);
            SendGuild(character, guild);
            if (announce)
                SendToMembers(guild, OnlineStatus(guild, character.Guid, true), character.Guid);
        }

        /// <summary>A character is offline (its presence grace ran out): guildmates see it.</summary>
        public void WentOffline(ulong character)
        {
            Guild guild = GuildOf(character);
            if (guild != null)
                SendToMembers(guild, OnlineStatus(guild, character, false), character);
        }

        // ---- Requests ---------------------------------------------------------------------------------------------------

        /// <summary>A GuildBase request handed on by the zone.</summary>
        public void Handle(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"Guild request 0x{request.Sub:X2} for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            byte[] body = request.Body;
            try
            {
                var reader = new GamePacketReader(body);
                switch ((ushort)request.Sub)
                {
                    case CreateSub:
                        Create(player, reader.ReadString()?.Trim() ?? "");
                        break;
                    case InviteSub:
                        if (GuildInvites.TryRead(body, out GuildInvites.Request invite))
                            Invite(player, invite);
                        else
                            log.Debug($"Unreadable guild invite from {player}: {BitConverter.ToString(body)}");
                        break;
                    case InviteReplySub:
                        Reply(player, body);
                        break;
                    case InviteRevokeSub:
                        Revoke(player, reader.ReadULong());
                        break;
                    case MotdSub:
                        reader.ReadULong();
                        // Shown in members' chat windows: typed rich-text tokens made harmless (L54).
                        SetMotd(player, LandmarkEmulator.Shared.Game.RichTextTokens.Neutralize(reader.ReadString() ?? ""));
                        break;
                    case QuitSub:
                        Remove(player.Guid, player.Name);
                        break;
                    case KickSub:
                        if (GuildKicks.TryRead(body, out GuildKicks.Request kick))
                            Kick(player, kick);
                        else
                            log.Debug($"Unreadable guild kick from {player}: {BitConverter.ToString(body)}");
                        break;
                    case ChangeMemberRankSub:
                        ChangeRank(player, body);
                        break;
                    case DisbandSub:
                        Disband(player);
                        break;
                    case UpdateRankSub:
                        UpdateRank(player, body);
                        break;
                    case DescriptionSub:
                        SetDescription(player, body);
                        break;
                    case AppDataFieldSub:
                        SetAppData(player, body);
                        break;
                    case MuteSub:
                        Mute(player, body);
                        break;
                    default:
                        log.Debug($"Guild packet 0x{request.Sub:X2} from {player}: {BitConverter.ToString(body)}");
                        break;
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                log.Debug($"Unreadable guild packet 0x{request.Sub:X2} from {player}: {e.Message} {BitConverter.ToString(body)}");
            }
        }

        /// <summary>Creates a guild with the player as leader (3-32 letters, digits, spaces or apostrophes; unique).</summary>
        private void Create(OnlineCharacter player, string name)
        {
            if (name.Length < 3 || name.Length > 32 || name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '\''))
            {
                Notify(player, NotifyCreateFailed, name);
                return;
            }
            if (guildOf.ContainsKey(player.Guid))
            {
                Notify(player, NotifyAlreadyInGuild, name);
                return;
            }
            if (guilds.Values.Any(g => string.Equals(g.Model.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Notify(player, NotifyNameTaken, name);
                return;
            }
            // One service numbers every guild: no per-world ranges any more.
            ulong id = guilds.Keys.DefaultIfEmpty(0ul).Max() + 1;
            var guild = new Guild
            {
                Model = new SocialGuildModel { Id = id, Name = name, LeaderGuid = player.Guid, CreatedAt = DateTime.UtcNow }
            };
            guild.Members[player.Guid] = (player.Name, LeaderRank);
            guilds[id] = guild;
            guildOf[player.Guid] = id;
            SaveGuild(guild);
            hub.Store.SetGuildMember(player.Guid, id, LeaderRank);
            SendGuild(player, guild);
            Notify(player, NotifyCreated, name);
            Joined(player, id);
            log.Info($"{player} created guild {id} '{name}'.");
        }

        /// <summary>The zone's side of joining (the guild goal) and the new zone state.</summary>
        private void Joined(OnlineCharacter player, ulong guildId)
        {
            hub.SendService(player.World, new GuildJoined { CharacterGuid = player.Guid, GuildId = guildId });
            PushZoneState(new[] { player.Guid });
        }

        /// <summary>
        /// Invites a player online on any world (needs CreateGuildInvite). The invitee is found by guid when the request
        /// carries one (the right-click menu's Guild.Invite: {guild, target guid, ""}) and by name otherwise (the Guild
        /// window: {guild, 0, typed name}; Internal/Social/GuildInvites.cs).
        /// </summary>
        private void Invite(OnlineCharacter player, GuildInvites.Request request)
        {
            Guild guild = GuildOf(player.Guid);
            if (guild == null || !Has(guild, player.Guid, "CreateGuildInvite"))
            {
                log.Debug($"{player}: guild invite ({request.Describe()}) without a guild or CreateGuildInvite: ignored.");
                return;
            }
            SocialCharacterModel row = GuildInvites.Resolve(request, hub.Store.GetCharacter, hub.Store.FindCharacterByName);
            OnlineCharacter target = row != null ? hub.Presence.Find(row.Guid) : null;
            if (target is not { Connected: true })
            {
                Notify(player, NotifyPlayerNotFound, guild.Model.Name, GuildInvites.ShownName(request, row?.Name));
                log.Info($"{player}: guild invite ({request.Describe()}): {(row == null ? "no such character" : $"{row.Name} is not online")}.");
                return;
            }
            if (guildOf.ContainsKey(target.Guid))
            {
                Notify(player, NotifyPlayerInGuild, guild.Model.Name, target.Name);
                return;
            }
            if (hub.Store.GetIgnores(target.Guid).ContainsKey(player.Guid))
            {
                hub.SendTo(player, ClientPackets.SystemLine($"{target.Name} is not accepting invitations from you."));
                log.Info($"{target} ignores {player}: guild invitation refused.");
                return;
            }
            if (guild.Members.Count >= MaxMembers)
            {
                Notify(player, NotifyGeneralFail, guild.Model.Name, target.Name);
                return;
            }
            if (invites.TryGetValue(target.Guid, out var pending) && pending.Guild == guild.Model.Id
                && DateTime.UtcNow - pending.At <= InviteLifetime)
            {
                Notify(player, NotifyAlreadyInvited, guild.Model.Name, target.Name);
                return;
            }
            var invite = (nextInviteId++, guild.Model.Id, player.Name, player.Guid, DateTime.UtcNow);
            invites[target.Guid] = invite;
            // Both ends get the invitation (the client's list keeps sent ones too; revoke names it by its id).
            byte[] invitation = BuildInvitation(guild, invite, target.Guid, target.Name, InvitationOpen);
            // The invitee's client queues the Accept / Decline popup itself (type 1 to the player, FUN_142ef4e00).
            hub.SendTo(target, invitation);
            hub.SendTo(player, invitation);
            Notify(player, NotifyInvited, guild.Model.Name, target.Name);
            log.Info($"{player} invited {target} to guild {guild.Model.Id}.");
        }

        /// <summary>
        /// InviteReply (5): {u64 invitation, u8 accept}. The invitation leaves both lists: state 2 on accept, state 3 on
        /// decline (the client's own declined texts).
        /// </summary>
        private void Reply(OnlineCharacter player, byte[] body)
        {
            bool accept = body.Length >= 9 && body[8] != 0;
            if (!invites.Remove(player.Guid, out var invite) || DateTime.UtcNow - invite.At > InviteLifetime
                || !guilds.TryGetValue(invite.Guild, out Guild guild) || guildOf.ContainsKey(player.Guid))
                return;
            byte[] closed = BuildInvitation(guild, invite, player.Guid, player.Name, accept ? InvitationClosed : InvitationDeclined);
            hub.SendTo(player, closed);
            if (hub.Presence.Find(invite.FromGuid) is { Connected: true } inviter)
                hub.SendTo(inviter, closed);
            if (!accept)
                return;
            guild.Members[player.Guid] = (player.Name, MemberRank);
            guildOf[player.Guid] = guild.Model.Id;
            hub.Store.SetGuildMember(player.Guid, guild.Model.Id, MemberRank);
            SendGuild(player, guild);
            SendToMembers(guild, BuildAddMember(guild, player.Guid, player.Name, MemberRank), player.Guid);
            AddLog(guild, LogJoined, player.Name, "");
            Joined(player, guild.Model.Id);
            log.Info($"{player} joined guild {guild.Model.Id}.");
        }

        /// <summary>
        /// GuildInviteRevoke (6) {u64 invitation id} ("/guild_debug revoke invite &lt;id&gt;", Lua Guild.InviteRevoke): needs
        /// RevokeGuildInvite in the invitation's guild; the invitee, inviter and revoker get state 1.
        /// </summary>
        private void Revoke(OnlineCharacter player, ulong invitationId)
        {
            Guild guild = GuildOf(player.Guid);
            if (guild == null || !Has(guild, player.Guid, "RevokeGuildInvite"))
                return;
            ulong invitee = 0;
            (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite = default;
            foreach (var (who, open) in invites)
                if (open.Id == invitationId && open.Guild == guild.Model.Id)
                    (invitee, invite) = (who, open);
            if (invitee == 0)
                return;
            invites.Remove(invitee);
            OnlineCharacter target = hub.Presence.Find(invitee);
            byte[] revoked = BuildInvitation(guild, invite, invitee, target?.Name ?? "", InvitationRevoked);
            foreach (ulong guid in new[] { invitee, invite.FromGuid, player.Guid }.Distinct())
                if (hub.Presence.Find(guid) is { Connected: true } online)
                    hub.SendTo(online, revoked);
            log.Info($"{player} revoked guild {guild.Model.Id}'s invitation {invitationId}.");
        }

        /// <summary>
        /// GuildUpdateRank (0x0E): u64 guild, the edited rank {u32 level, string name, u32, string, u32, u32 n × permission,
        /// u32, u32, u32 n × {…}, string, u32 rank id (0 = new)}, u32 action (1 set, 2 remove). Needs ModifyRanks.
        /// </summary>
        private void UpdateRank(OnlineCharacter player, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            uint level = r.ReadUInt();
            string name = (r.ReadString() ?? "").Trim();
            r.ReadUInt();
            r.ReadString();
            r.ReadUInt();
            uint count = r.ReadUInt();
            var permissions = new List<string>();
            for (uint i = 0; i < count && i < 64; i++)
                permissions.Add(r.ReadString());
            r.ReadUInt();
            r.ReadUInt();
            if (r.ReadUInt() != 0)
            {
                log.Debug($"Guild rank update from {player} with extra entries: {BitConverter.ToString(body)}");
                return;
            }
            r.ReadString();
            uint rankId = r.ReadUInt();
            uint action = r.ReadUInt();
            Guild guild = GuildOf(player.Guid);
            if (guild == null || guild.Model.Id != guildId || !Has(guild, player.Guid, "ModifyRanks"))
                return;
            Rank existing = guild.Ranks.FirstOrDefault(x => x.Id == rankId);
            // A member below the leader who was given ModifyRanks edits only the ranks below their own, keeps the ranks
            // they set below their own, and grants only permissions they have [I: live's rule is not traced yet]. Before,
            // such an officer could give their own rank DisbandGuild, TransferGuildLeadership or KickFromGuild and
            // level 2, or strip the ranks above them.
            uint editorRank = guild.Members[player.Guid].Rank;
            if (editorRank != LeaderRank)
            {
                uint own = Level(guild, editorRank);
                if (existing != null && Level(guild, existing.Id) <= own)
                {
                    log.Info($"{player} may not change guild {guild.Model.Id}'s rank {existing.Id} (not below their own).");
                    return;
                }
                level = Math.Max(level, own + 1);
                permissions = permissions.Where(p => Has(guild, player.Guid, p)).ToList();
            }
            var moved = new List<ulong>();
            Rank changed;
            bool created = false;
            if (action == 2)
            {
                if (existing == null || rankId == LeaderRank || rankId == MemberRank)
                    return;
                guild.Ranks.Remove(existing);
                foreach (var m in guild.Members.Where(m => m.Value.Rank == rankId).ToList())
                {
                    guild.Members[m.Key] = (m.Value.Name, MemberRank);
                    moved.Add(m.Key);
                }
                changed = existing;
            }
            else
            {
                if (name.Length == 0 || name.Length > 32
                    || guild.Ranks.Any(x => x.Id != rankId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return;
                var allowed = permissions.Where(AllPermissions.Contains).Distinct().ToList();
                if (existing == null)
                {
                    if (guild.Ranks.Count >= 10)
                        return;
                    existing = new Rank { Id = Math.Max(4u, guild.Ranks.Max(x => x.Id) + 1) };
                    guild.Ranks.Add(existing);
                    created = true;
                }
                existing.Name = name;
                existing.Level = existing.Id == LeaderRank ? 1u : Math.Clamp(level, 2u, 20u);
                existing.Permissions = existing.Id == LeaderRank ? AllPermissions.ToList() : allowed;
                changed = existing;
            }
            SaveGuild(guild);
            foreach (ulong m in moved)
                hub.Store.SetGuildMember(m, guild.Model.Id, MemberRank);
            SendToMembers(guild, BuildRank(guild.Model.Id, changed.Id, changed.Level, changed.Name, changed.Permissions.ToArray(),
                action == 2 ? 2u : 1u));
            if (moved.Count > 0)
                ResendGuild(guild);
            AddLog(guild, action == 2 ? LogDeleteRank : created ? LogCreateRank : LogUpdateRank, player.Name, "", changed.Id);
            // Officer chat follows the rank's permissions.
            PushZoneState(guild.Members.Keys);
            log.Info($"{player} {(action == 2 ? "removed" : "set")} guild {guild.Model.Id} rank {changed.Id} '{changed.Name}' "
                + $"(level {changed.Level}, {changed.Permissions.Count} permissions).");
        }

        /// <summary>GuildDescriptionUpdate (0x0F): u64 guild, three strings, the text in the one that isn't empty. Needs UpdateDesc.</summary>
        private void SetDescription(OnlineCharacter player, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            string text = LandmarkEmulator.Shared.Game.RichTextTokens.Neutralize(
                new[] { r.ReadString(), r.ReadString(), r.ReadString() }.LastOrDefault(x => !string.IsNullOrEmpty(x)) ?? "");
            if (text.Length > 1000)
                text = text.Substring(0, 1000);
            Guild guild = GuildOf(player.Guid);
            if (guild == null || guild.Model.Id != guildId || !Has(guild, player.Guid, "UpdateDesc"))
                return;
            guild.Model.Description = text;
            SaveGuild(guild);
            ResendGuild(guild);
            log.Info($"{player} set guild {guildId}'s description.");
        }

        /// <summary>GuildUpdateAppDataField (0x11): u64 guild, the GuildGameData XML. Needs UpdateDesc.</summary>
        private void SetAppData(OnlineCharacter player, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            string xml = r.ReadString() ?? "";
            if (xml.Length > 2000 || !xml.TrimStart().StartsWith("<GuildGameData"))
                return;
            Guild guild = GuildOf(player.Guid);
            if (guild == null || guild.Model.Id != guildId || !Has(guild, player.Guid, "UpdateDesc"))
                return;
            guild.Model.AppData = xml;
            SaveGuild(guild);
            ResendGuild(guild);
            log.Info($"{player} set guild {guildId}'s app data ({xml.Length} chars).");
        }

        /// <summary>Message of the day (needs UpdateMotd; at most 500 characters).</summary>
        private void SetMotd(OnlineCharacter player, string motd)
        {
            motd = motd.Length > 500 ? motd.Substring(0, 500) : motd;
            Guild guild = GuildOf(player.Guid);
            if (guild == null || !Has(guild, player.Guid, "UpdateMotd"))
                return;
            guild.Model.Motd = motd;
            SaveGuild(guild);
            ulong id = guild.Model.Id;
            SendToMembers(guild, Build(MotdSub, w =>
            {
                w.Write(id);
                w.Write(motd);
                w.Write(player.Name);
                w.Write(0ul);
            }));
        }

        /// <summary>
        /// A character was deleted (TASKS 6.42): it leaves its guild as with /guildquit (P4624: deleted characters
        /// leave friend and guild lists; P5311, 11 May 2016: officer and guild chat no longer outlive a deleted and
        /// recreated character — the membership, and so the zone's guild
        /// chat state, goes with it). The leader's leaving passes the lead on; the last member ends the guild. The
        /// RemoveMember the others get carries the name the member had (P5312: Guild News keeps a deleted player's
        /// name: this service keeps no guild news yet, so there is no stored entry to keep).
        /// </summary>
        public void CharacterDeleted(ulong characterId, string name)
        {
            Guild guild = GuildOf(characterId);
            if (guild == null)
                return;
            string known = guild.Members.TryGetValue(characterId, out var member) && !string.IsNullOrEmpty(member.Name) ? member.Name : name;
            Remove(characterId, known);
            log.Info($"Deleted character {known} ({characterId:X}) left guild {guild.Model.Id}.");
        }

        /// <summary>A member leaves (quit or kick). The leader's leaving passes the lead on; the last member ends the guild.</summary>
        private void Remove(ulong characterId, string name, string kickedBy = null)
        {
            Guild guild = GuildOf(characterId);
            if (guild == null)
                return;
            guild.Members.Remove(characterId);
            guildOf.Remove(characterId);
            bool ended = guild.Members.Count == 0;
            ulong newLeader = 0;
            ulong guildId = guild.Model.Id;
            if (ended)
            {
                guilds.Remove(guildId);
                hub.Store.DeleteGuild(guildId);
            }
            else
            {
                hub.Store.RemoveGuildMember(characterId);
                if (guild.Model.LeaderGuid == characterId)
                {
                    newLeader = guild.Members.Keys.First();
                    guild.Model.LeaderGuid = newLeader;
                    guild.Members[newLeader] = (guild.Members[newLeader].Name, LeaderRank);
                    SaveGuild(guild);
                    hub.Store.SetGuildMember(newLeader, guildId, LeaderRank);
                }
            }
            // RemoveMember about themselves takes the guild off the leaver's client ("You have been removed from the
            // Guild.", guild chat channels closed).
            byte[] removed = Build(RemoveMemberSub, w =>
            {
                // {u64 guild, u64 member, string name} (reader FUN_142ee5650, must be consumed exactly) [P]; a stray
                // string of the member id made the client drop it until the 2026-10-17 wire audit (audit-wire-c.md #1).
                w.Write(guildId);
                w.Write(characterId);
                w.Write(name);
            });
            if (hub.Presence.Find(characterId) is { Connected: true } leaver)
                hub.SendTo(leaver, removed);
            if (!ended)
            {
                SendToMembers(guild, removed);
                if (kickedBy != null)
                    AddLog(guild, LogKicked, kickedBy, name);
                else
                    AddLog(guild, LogQuit, name, "");
                if (newLeader != 0)
                {
                    AddLog(guild, LogNewLeader, "", guild.Members.TryGetValue(newLeader, out var leader) ? leader.Name : "");
                    ResendGuild(guild);
                }
            }
            PushZoneState(new[] { characterId, newLeader }.Where(id => id != 0));
            log.Info($"{name} left guild {guildId}{(ended ? " (guild ended)" : "")}.");
        }

        /// <summary>Disbands the guild (needs DisbandGuild): every online member gets Disband (sub 2).</summary>
        private void Disband(OnlineCharacter player)
        {
            Guild guild = GuildOf(player.Guid);
            if (guild == null || !Has(guild, player.Guid, "DisbandGuild"))
                return;
            List<ulong> members = guild.Members.Keys.ToList();
            foreach (ulong member in members)
                guildOf.Remove(member);
            guilds.Remove(guild.Model.Id);
            hub.Store.DeleteGuild(guild.Model.Id);
            ulong guildId = guild.Model.Id;
            hub.SendTo(Online(members), Build(DisbandSub, w => w.Write(guildId)));
            PushZoneState(members);
            log.Info($"{player} disbanded guild {guildId}.");
        }

        /// <summary>
        /// GuildKick (0x0C): needs KickFromGuild and a target of a lower rank; the target leaves as with /guildquit. The
        /// roster menu names the member by guid, /guildkick Name by name (<see cref="GuildKicks"/>).
        /// </summary>
        private void Kick(OnlineCharacter player, GuildKicks.Request request)
        {
            Guild guild = GuildOf(player.Guid);
            ulong target = guild == null ? 0 : GuildKicks.Resolve(request, guild.Members.Select(m => KeyValuePair.Create(m.Key, m.Value.Name)));
            log.Debug($"{player}: guild kick of {request.Describe()} → 0x{target:X}.");
            if (guild == null || target == 0 || target == player.Guid || !Has(guild, player.Guid, "KickFromGuild")
                || !guild.Members.TryGetValue(target, out var member)
                || Level(guild, member.Rank) <= Level(guild, guild.Members[player.Guid].Rank))
            {
                Notify(player, NotifyGeneralFail, guild?.Model.Name ?? "");
                return;
            }
            Remove(target, member.Name, kickedBy: player.Name);
            log.Info($"{player} removed {member.Name} from guild {guild.Model.Id}.");
        }

        /// <summary>
        /// GuildChangeMemberRank (0x0A): u64 guild, u64 member, string, u64, string, u32, string, u8, u8, u32. Needs
        /// ChangeMemberRank and a member below the changer; a named rank of ours is used, else one step Member ↔ Officer.
        /// </summary>
        private void ChangeRank(OnlineCharacter player, byte[] body)
        {
            var r = new GamePacketReader(body);
            r.ReadULong();
            ulong target = r.ReadULong();
            r.ReadString();
            r.ReadULong();
            string a = r.ReadString();
            r.ReadUInt();
            string b = r.ReadString();
            Guild guild = GuildOf(player.Guid);
            if (guild == null || !Has(guild, player.Guid, "ChangeMemberRank")
                || !guild.Members.TryGetValue(target, out var member)
                || Level(guild, member.Rank) <= Level(guild, guild.Members[player.Guid].Rank))
            {
                Notify(player, NotifyChangeRankFailed, guild?.Model.Name ?? "");
                return;
            }
            uint? named = new[] { a, b }
                .Select(n => guild.Ranks.FirstOrDefault(x => string.Equals(x.Name, n?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? RankByName(n))
                .FirstOrDefault(x => x != null && x != member.Rank);
            uint newRank = named ?? (member.Rank == MemberRank ? OfficerRank : MemberRank);
            if (GuildRankChanges.IsLeadershipTransfer(newRank, guild.Members[player.Guid].Rank, guild.Model.LeaderGuid == player.Guid, LeaderRank))
            {
                TransferLeadership(player, guild, target);
                return;
            }
            if (newRank == LeaderRank || (Level(guild, newRank) <= Level(guild, guild.Members[player.Guid].Rank)
                && guild.Members[player.Guid].Rank != LeaderRank))
            {
                Notify(player, NotifyChangeRankFailed, guild.Model.Name);
                return;
            }
            uint oldRank = member.Rank;
            guild.Members[target] = (member.Name, newRank);
            hub.Store.SetGuildMember(target, guild.Model.Id, newRank);
            // Level 1 is the top: a smaller level is a promotion.
            AddLog(guild, Level(guild, newRank) < Level(guild, oldRank) ? LogPromoted : LogDemoted, player.Name, member.Name, 0, newRank);
            SendToMembers(guild, Build(ChangeMemberRankSub, w =>
            {
                w.Write(guild.Model.Id);
                w.Write(target);
                w.Write(member.Name);
                w.Write(player.Guid);
                w.Write(player.Name);
                w.Write(newRank);
                // The packet's tail (0.695 reader FUN_142ee4550: string +0x68, bool +0x80, bool +0x81, u32 +0x84): the
                // client drops a body that stops short, so without it no member's roster moved until a relog
                // (phaseE3-wire-b.md F1). Values as the client's own copy (FUN_142ef91f0); +0x81 picks the message
                // variant [I].
                w.Write("");
                w.Write(false);
                w.Write(false);
                w.Write(0u);
            }));
            PushZoneState(new[] { target });
            log.Info($"{player} set {member.Name}'s guild rank to {guild.Ranks.FirstOrDefault(x => x.Id == newRank)?.Name ?? newRank.ToString()}.");
        }

        /// <summary>
        /// The leader hands the guild on (<see cref="GuildRankChanges"/>): the member becomes leader, the old leader takes
        /// the member's former rank [I], both rank changes go to the guild, the log gets Guild.NewLeader, and everyone gets
        /// the guild again (its leader field).
        /// </summary>
        private void TransferLeadership(OnlineCharacter player, Guild guild, ulong target)
        {
            var member = guild.Members[target];
            uint formerRank = GuildRankChanges.FormerLeaderRank(member.Rank, LeaderRank, MemberRank);
            guild.Members[target] = (member.Name, LeaderRank);
            guild.Members[player.Guid] = (guild.Members[player.Guid].Name, formerRank);
            guild.Model.LeaderGuid = target;
            hub.Store.SetGuildMember(target, guild.Model.Id, LeaderRank);
            hub.Store.SetGuildMember(player.Guid, guild.Model.Id, formerRank);
            foreach ((ulong who, string name, uint rank) in new[] { (target, member.Name, LeaderRank), (player.Guid, player.Name, formerRank) })
                SendToMembers(guild, Build(ChangeMemberRankSub, w =>
                {
                    w.Write(guild.Model.Id);
                    w.Write(who);
                    w.Write(name);
                    w.Write(player.Guid);
                    w.Write(player.Name);
                    w.Write(rank);
                    // The packet's tail (0.695 reader FUN_142ee4550: string +0x68, bool +0x80, bool +0x81, u32 +0x84):
                    // the client drops a body that stops short, so without it no member's roster moved until a relog
                    // (phaseE3-wire-b.md F1). Values as the client's own copy (FUN_142ef91f0); +0x81 picks the message
                    // variant [I].
                    w.Write("");
                    w.Write(false);
                    w.Write(false);
                    w.Write(0u);
                }));
            AddLog(guild, LogNewLeader, "", member.Name);
            SaveGuild(guild);
            ResendGuild(guild);
            PushZoneState(new[] { target, player.Guid });
            log.Info($"{player} handed the leadership of guild {guild.Model.Id} to {member.Name}.");
        }

        /// <summary>
        /// GuildRequestMutePlayer (0x10): {u64 guild, u32 channel (5 / 6), u32 minutes (-1 = until unmuted), string member,
        /// string requester, u8 mute}. Needs MuteGuildChat / MuteOfficerChat and a member below the requester.
        /// </summary>
        private void Mute(OnlineCharacter player, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            uint channel = r.ReadUInt();
            uint minutes = r.ReadUInt();
            string name = (r.ReadString() ?? "").Trim();
            r.ReadString();
            bool mute = r.ReadByte() != 0;
            string permission = channel == ChatChannel ? MuteGuildChat : channel == OfficerChatChannel ? MuteOfficerChat : null;
            Guild guild = GuildOf(player.Guid);
            if (guild == null || guild.Model.Id != guildId || permission == null || !Has(guild, player.Guid, permission))
                return;
            ulong target = guild.Members.FirstOrDefault(m => string.Equals(m.Value.Name, name, StringComparison.OrdinalIgnoreCase)).Key;
            if (target == 0 || target == player.Guid
                || Level(guild, guild.Members[player.Guid].Rank) >= Level(guild, guild.Members[target].Rank))
            {
                hub.SendTo(player, ClientPackets.SystemLine(target == 0 ? $"{name} is not in your guild." : "You cannot mute that member."));
                return;
            }
            var key = (guildId, (ushort)channel, target);
            if (mute)
                mutes[key] = minutes == uint.MaxValue || minutes == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(minutes);
            else
                mutes.Remove(key);
            string where = channel == ChatChannel ? "guild chat" : "officer chat";
            hub.SendTo(player, ClientPackets.SystemLine(mute ? $"{name} is muted in {where}." : $"{name} is no longer muted in {where}."));
            if (hub.Presence.Find(target) is { Connected: true } muted)
                hub.SendTo(muted, ClientPackets.SystemLine(mute ? $"You have been muted in {where}." : $"You are no longer muted in {where}."));
            PushZoneState(new[] { target });
            log.Info($"{player} {(mute ? "muted" : "unmuted")} {name} in guild {guildId} channel {channel}"
                + (mute && minutes != uint.MaxValue && minutes != 0 ? $" for {minutes} min." : "."));
        }

        // ---- Helpers -------------------------------------------------------------------------------------------------

        private static uint? RankByName(string name) => name?.Trim().ToLowerInvariant() switch
        {
            "leader" => LeaderRank,
            "officer" => OfficerRank,
            "member" => MemberRank,
            _ => null
        };

        private Guild GuildOf(ulong character)
            => guildOf.TryGetValue(character, out ulong id) && guilds.TryGetValue(id, out Guild guild) ? guild : null;

        private static uint Level(Guild guild, uint rank) => guild.Ranks.FirstOrDefault(r => r.Id == rank)?.Level ?? 99u;

        private static bool Has(Guild guild, ulong character, string permission)
            => guild.Members.TryGetValue(character, out var member)
                && (member.Rank == LeaderRank || permission == "ViewGuildInvites"
                    || guild.Ranks.FirstOrDefault(r => r.Id == member.Rank)?.Permissions.Contains(permission) == true);

        // ---- News (guild log; as WorldServer Game/Guilds.cs) ----------------------------------------------------------

        /// <summary>
        /// One News row: u32 id, string source, string target, string type, u64 time (Unix seconds), u32 rank, u32 new
        /// rank, string comment, string app data (client entry reader FUN_142e45fe0; texts by type, FUN_142e5c9a0).
        /// </summary>
        private sealed class LogEntry
        {
            public uint Id { get; set; }
            public string Type { get; set; } = "";
            public string Source { get; set; } = "";
            public string Target { get; set; } = "";
            public long Time { get; set; }
            public uint Rank { get; set; }
            public uint NewRank { get; set; }
        }

        private const string LogJoined = "Guild.Joined", LogQuit = "Guild.Quit", LogKicked = "Guild.Kicked",
            LogPromoted = "Guild.Promoted", LogDemoted = "Guild.Demoted", LogNewLeader = "Guild.NewLeader",
            LogCreateRank = "CreateRank", LogUpdateRank = "UpdateRank", LogDeleteRank = "DeleteRank";
        /// <summary>GuildAddLogUpdate (sub 0x1A): a row, then u64 guild.</summary>
        private const ushort AddLogSub = 0x1A;
        /// <summary>Rows kept per guild [I].</summary>
        private const int MaxLog = 100;

        private static List<LogEntry> ParseLog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<LogEntry>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<LogEntry>>(json) ?? new List<LogEntry>();
            }
            catch (System.Text.Json.JsonException)
            {
                return new List<LogEntry>();
            }
        }

        private static void WriteLogEntry(GamePacketWriter w, LogEntry entry)
        {
            w.Write(entry.Id);
            w.Write(entry.Source ?? "");
            w.Write(entry.Target ?? "");
            w.Write(entry.Type ?? "");
            w.Write((ulong)entry.Time);
            w.Write(entry.Rank);
            w.Write(entry.NewRank);
            w.Write("");
            w.Write("");
        }

        /// <summary>Adds a News row: kept (newest <see cref="MaxLog"/>), saved with the guild, sent to the online members.</summary>
        private void AddLog(Guild guild, string type, string source, string target, uint rank = 0, uint newRank = 0)
        {
            var entry = new LogEntry
            {
                Id = guild.Log.Count == 0 ? 1 : guild.Log.Max(e => e.Id) + 1,
                Type = type, Source = source ?? "", Target = target ?? "",
                Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Rank = rank, NewRank = newRank
            };
            guild.Log.Add(entry);
            List<uint> dropped = null;
            if (guild.Log.Count > MaxLog)
            {
                dropped = guild.Log.Take(guild.Log.Count - MaxLog).Select(e => e.Id).ToList();
                guild.Log.RemoveRange(0, guild.Log.Count - MaxLog);
            }
            SaveGuild(guild);
            ulong id = guild.Model.Id;
            // The rows the cap dropped leave the online members' News tab too (GuildRemoveLogUpdate 0x1B).
            if (dropped is { Count: > 0 })
                SendToMembers(guild, BuildRemoveLog(id, dropped));
            SendToMembers(guild, Build(AddLogSub, w =>
            {
                WriteLogEntry(w, entry);
                w.Write(id);
            }));
        }

        private void SaveGuild(Guild guild)
        {
            guild.Model.Ranks = System.Text.Json.JsonSerializer.Serialize(guild.Ranks);
            guild.Model.Log = System.Text.Json.JsonSerializer.Serialize(guild.Log);
            hub.Store.SaveGuild(guild.Model);
        }

        /// <summary>The connected characters among these guids.</summary>
        private List<OnlineCharacter> Online(IEnumerable<ulong> guids)
            => guids.Select(hub.Presence.Find).Where(c => c is { Connected: true }).ToList();

        /// <summary>A GuildBase packet to every connected member (optionally all but one), one frame per world.</summary>
        private void SendToMembers(Guild guild, byte[] packet, ulong except = 0)
            => hub.SendTo(Online(guild.Members.Keys.Where(m => m != except)), packet);

        private void ResendGuild(Guild guild)
        {
            foreach (OnlineCharacter member in Online(guild.Members.Keys))
                SendGuild(member, guild);
        }

        /// <summary>The whole guild and its ranks, to one member.</summary>
        private void SendGuild(OnlineCharacter character, Guild guild)
        {
            hub.SendTo(character, BuildData(guild));
            foreach (Rank r in guild.Ranks.ToList())
                hub.SendTo(character, BuildRank(guild.Model.Id, r.Id, r.Level, r.Name, r.Permissions.ToArray()));
        }

        /// <summary>GuildData (0x13): core, members (online flags), no invitations, the News rows, names. Applied only when consumed exactly.</summary>
        private byte[] BuildData(Guild guild)
        {
            var members = guild.Members.Select(m => (Id: m.Key, m.Value.Name, m.Value.Rank)).ToList();
            var rows = hub.Store.GetCharacters(members.Select(m => m.Id).ToList()).ToDictionary(c => c.Guid);
            SocialGuildModel model = guild.Model;
            return Build(DataSub, w =>
            {
                w.Write(model.Id);
                w.Write(model.Name);
                // Core +0xa8, the alias = the member's own nameplate tag (client FUN_142eedd60): the guild name, as the
                // client's Guild.Rename sends it (zone Game/Social/GuildTags.cs, OPEN-LEADS L196 OL-1).
                w.Write(model.Name);
                for (int i = 0; i < 4; i++)
                    w.Write("");
                w.Write(model.Description ?? "");
                w.Write(model.LeaderGuid);
                w.Write(model.AppData ?? "");
                w.Write(0ul);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(model.Motd ?? "");
                w.Write("");
                w.Write(0ul);
                w.Write((uint)members.Count);
                foreach (var member in members)
                    WriteMember(w, member.Id, member.Rank, rows.GetValueOrDefault(member.Id));
                w.Write(0u);                           // invitations (list reader FUN_142e48340)
                w.Write((uint)guild.Log.Count);        // News (list reader FUN_142e47de0): u32 key, then the row
                foreach (LogEntry entry in guild.Log)
                {
                    w.Write(entry.Id);
                    WriteLogEntry(w, entry);
                }
                w.Write(0ul);
                w.Write(0u);
                w.Write(false);
                w.Write(false);
                w.Write("");
                w.Write(0u);
                w.Write((uint)members.Count);
                foreach (var member in members)
                {
                    w.Write(member.Id);
                    w.Write(member.Name);
                }
            });
        }

        /// <summary>A member record: guid as a decimal string (the client parses it), guid, rank, u64, game data, online, u64.</summary>
        private void WriteMember(GamePacketWriter w, ulong id, uint rank, SocialCharacterModel row)
        {
            OnlineCharacter presence = hub.Presence.Find(id);
            w.Write(id.ToString());
            w.Write(id);
            w.Write(rank);
            w.Write(0ul);
            w.Write(MemberGameData(presence, row));
            w.Write(presence != null);
            w.Write(0ul);
        }

        /// <summary>
        /// The roster's "game data" (client parser FUN_143b92760, an XML element): LastZone / LastWorld are NAME TEXT ids
        /// (Location "world:zone"; 0 shows "UnknownWorld:UnknownZone"), LastOnline unix seconds. Online members report
        /// where they are now (any world), others where they left.
        /// </summary>
        private static string MemberGameData(OnlineCharacter presence, SocialCharacterModel row)
        {
            uint zoneName, worldName;
            long lastOnline;
            if (presence != null)
            {
                zoneName = presence.ZoneNameText;
                worldName = presence.WorldNameText;
                lastOnline = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            else
            {
                zoneName = row?.LastZoneNameText ?? 0;
                worldName = row?.LastWorldNameText ?? 0;
                lastOnline = row?.LastOnline is DateTime t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0;
            }
            if (zoneName == 0)
                worldName = 0;
            return $"<MemberGameData LastOnline=\"{lastOnline}\" BattleRank=\"0\" LastZone=\"{zoneName}\" LastWorld=\"{worldName}\" LastContinent=\"0\"/>";
        }

        private byte[] BuildAddMember(Guild guild, ulong id, string name, uint rank) => Build(AddMemberSub, w =>
        {
            w.Write(guild.Model.Id);
            WriteMember(w, id, rank, hub.Store.GetCharacter(id));
            w.Write(name);
            w.Write(0ul);
            w.Write(0ul);
            w.Write("");
        });

        private static byte[] OnlineStatus(Guild guild, ulong id, bool online) => Build(OnlineStatusSub, w =>
        {
            w.Write(guild.Model.Id);
            w.Write(id);
            w.Write(online);
        });

        /// <summary>
        /// UpdateInvitation (0x17; 0.695 handler FUN_142ef4e00): the invitation (<see cref="WriteInvitation"/>), string
        /// inviter name, string invitee name, u32 state (as WorldServer Game/Guilds.cs). State 0 with type 1 to the player
        /// queues the client's GAQGuildInvite popup.
        /// </summary>
        private byte[] BuildInvitation(Guild guild, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite,
            ulong toGuid, string toName, uint state) => Build(UpdateInvitationSub, w =>
        {
            WriteInvitation(w, guild, invite, toGuid);
            w.Write(invite.From);
            w.Write(toName);
            w.Write(state);
        });

        /// <summary>
        /// One invitation (0.695 reader FUN_142ee4ee0) [P]: u64 id, u64 guild, string guild name, string inviter id, string
        /// invitee id (decimal), u64 time (unix seconds, the popup's date), string (the invitee id), u32 type (1 = invite:
        /// the client queues the popup), u32 0 (as WorldServer Game/Guilds.cs).
        /// </summary>
        private static void WriteInvitation(GamePacketWriter w, Guild guild, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite,
            ulong toGuid)
        {
            w.Write(invite.Id);
            w.Write(guild.Model.Id);
            w.Write(guild.Model.Name);
            w.Write(invite.FromGuid.ToString());
            w.Write(toGuid.ToString());
            w.Write((ulong)Math.Max(0, new DateTimeOffset(DateTime.SpecifyKind(invite.At, DateTimeKind.Utc)).ToUnixTimeSeconds()));
            w.Write(toGuid.ToString());
            w.Write(1u);
            w.Write(0u);
        }

        /// <summary>
        /// GuildInvitations (0x14; reader FUN_142ee5040, handler FUN_142ef25f0) [P]: u32 n × invitation, then the names
        /// list u32 n × {u64 guid, string name}. Replaces the player's invitation list.
        /// </summary>
        private static byte[] BuildInvitations(List<(Guild Guild, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) Invite)> open,
            ulong toGuid, string toName) => Build(InvitationsSub, w =>
        {
            w.Write((uint)open.Count);
            var names = new Dictionary<ulong, string>();
            foreach (var (guild, invite) in open)
            {
                WriteInvitation(w, guild, invite, toGuid);
                names[invite.FromGuid] = invite.From ?? "";
                names[toGuid] = toName ?? "";
            }
            w.Write((uint)names.Count);
            foreach ((ulong guid, string name) in names)
            {
                w.Write(guid);
                w.Write(name);
            }
        });

        /// <summary>GuildDataFull (0x12; reader FUN_142ee4820) [P] with no guild: u32 0 guilds, u32 0 names. Empties the client's guild list.</summary>
        private static byte[] EmptyDataFull() => Build(DataFullSub, w =>
        {
            w.Write(0u);
            w.Write(0u);
        });

        /// <summary>GuildRemoveLogUpdate (0x1B; reader FUN_142ee5580) [P]: u32 n × u32 row id, u64 guild.</summary>
        private static byte[] BuildRemoveLog(ulong guildId, IReadOnlyCollection<uint> rowIds) => Build(RemoveLogSub, w =>
        {
            w.Write((uint)rowIds.Count);
            foreach (uint row in rowIds)
                w.Write(row);
            w.Write(guildId);
        });

        /// <summary>GuildUpdateRank (0x0E): u64 guild, rank {u32 level, string name, u32, string, u32, permissions, u32, u32, u32 0, string, u32 id}, u32 action.</summary>
        private static byte[] BuildRank(ulong guildId, uint rankId, uint level, string name, string[] permissions, uint action = 1)
            => Build(UpdateRankSub, w =>
            {
                w.Write(guildId);
                w.Write(level);
                w.Write(name);
                w.Write(0u);
                w.Write("");
                w.Write(0u);
                w.Write((uint)permissions.Length);
                foreach (string permission in permissions)
                    w.Write(permission);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write("");
                w.Write(rankId);
                w.Write(action);
            });

        /// <summary>GuildNotification (0x19): {string guild, name block player, u8 code}; the client writes its own line for the code.</summary>
        private void Notify(OnlineCharacter player, byte code, string guild = "", string playerName = "")
        {
            hub.SendTo(player, Build(NotificationSub, w =>
            {
                w.Write(guild ?? "");
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(playerName ?? "");
                w.Write("");
                w.Write(code);
            }));
            log.Debug($"{player}: guild notification {code} ('{guild}', '{playerName}').");
        }

        /// <summary>A GuildBase packet: wire bytes 83 00, u16 sub, body.</summary>
        private static byte[] Build(ushort sub, Action<GamePacketWriter> write)
        {
            var output = new List<byte>(GuildBase);
            var w = new GamePacketWriter(output);
            w.WriteUShort(sub);
            write(w);
            return output.ToArray();
        }
    }
}
