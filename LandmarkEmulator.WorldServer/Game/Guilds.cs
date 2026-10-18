using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
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
    /// Guilds (stored in guild / guild_member). The client shows a guild in its guild window (N key), a Guild
    /// section in the Friends window, a Guild chat tab (chat channel 5) and its MOTD. Driven by slash commands
    /// ("/guild_debug create 'Name'", "/guildinvite Name", "/guild_debug accept|decline invite …", "/guildmotd text",
    /// "/guildquit", "/guild_debug disband").
    /// GuildBase 0x83, u16 sub (client dispatcher FUN_142e539f0):
    /// - client → server: 1 Create {string name}; 4 Invite {u64 guild, u64, string name}; 5 InviteReply;
    ///   0x0B Motd {u64 guild, string motd, …}; 0x0D Quit {u64 guild}.
    /// - server → client: 2 Disband {u64 guild}; 0x0B Motd {u64 guild, string motd, string by, u64};
    ///   0x0E UpdateRank {u64 guild, rank, u32 action}; 0x13 GuildData {guild, names}; 0x15 AddMember {u64 guild,
    ///   member, string, u64, u64, string}; 0x16 RemoveMember {u64 guild, string, u64, string};
    ///   0x17 UpdateInvitation {invitation, string from, string to, u32 state (0 new → "GAQGuildInvite" popup)};
    ///   0x18 MemberOnlineStatus {u64 guild, u64 member, u8 online}; at world entry 0x14 GuildInvitations (the player's
    ///   open invitations; empty for most) and, for a player in no guild, an empty 0x12 GuildDataFull; 0x1B
    ///   GuildRemoveLogUpdate when the News cap drops old rows (OPEN-LEADS L174).
    /// guild (FUN_142e459c0): core {u64 id, string name, 6 strings, u64 leader, string, u64, 3 × u32,
    /// string motd, string, u64} (FUN_142e45dc0), members u32 n × member, u32 n × invitation, u32 n × {u32 key, log},
    /// u64, u32 n × {…}, bool, {bool, string, u32 n × string}. member (FUN_142e463f0): {string guid in decimal (the
    /// client parses it, FUN_142b72df0), u64 guid, u32 rank id, u64, string, u8, u64}. names (FUN_142e48d10):
    /// u32 n × {u64 guid, string name}. invitation (FUN_142e46150): {u64 id, u64 guild, string guild name, string,
    /// string, u64, string invitee guid (decimal), u32, u32}.
    /// The invitee's Accept / Decline popup (Game Action Queue item GaqItemGuildInvite "GAQGuildInvite", kind 6) is
    /// queued by the client itself from an invitation of type 1 addressed to it (UpdateInvitation state 0 or
    /// GuildInvitations; FUN_142ef4e00 / FUN_142ef25f0 → FUN_1433da9d0), once per invitation id. Until 2026-10-18 the server
    /// sent type 0 plus a Notifications 0x01 ListNotifications kind-6 entry instead; that entry's "subject" (+0x180) is
    /// the invitation id (looked up in the invitation list, FUN_142ef0e10, for the guild and inviter names) [P], not the
    /// guild id it carried, so that popup showed no guild name. Notifications is the client's restore path for queue
    /// entries it saved at shutdown; it is no longer used for guilds.
    /// Ranks carry permission names the client checks itself (FUN_1439960b0); the server checks the same.
    /// Packets go only to the guild's online members.
    /// Part of the social service: callers outside it use <see cref="Social.SocialServices"/> (LocalSocialService runs
    /// this code; social-service.md §6 step 1).
    /// </summary>
    public static class Guilds
    {
        /// <summary>Size of each world's guild id range when worlds share the character database.</summary>
        private const ulong GuildIdRange = 0x100000ul;

        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort CreateSub = 1, DisbandSub = 2, InviteSub = 4, InviteReplySub = 5, MotdSub = 0x0B,
            QuitSub = 0x0D, UpdateRankSub = 0x0E, DataFullSub = 0x12, DataSub = 0x13, InvitationsSub = 0x14, AddMemberSub = 0x15,
            RemoveMemberSub = 0x16, UpdateInvitationSub = 0x17, OnlineStatusSub = 0x18, InviteRevokeSub = 6, RemoveLogSub = 0x1B;

        /// <summary>
        /// UpdateInvitation states (client FUN_142e56100): 0 adds / updates the invitation in the player's list; 1, 2 and 3
        /// remove it — 1 with "The invitation to join [guild] has been revoked." (UI.GuildInvitationRevoked, text 9405),
        /// 2 silently, 3 with "You have declined the invitation to join [guild]." (9415) when the invitee field is the
        /// player, else "[target] has declined the invitation to join the Guild." (9407).
        /// </summary>
        private const uint InvitationOpen = 0, InvitationRevoked = 1, InvitationClosed = 2, InvitationDeclined = 3;
        public const uint LeaderRank = 1, MemberRank = 2, OfficerRank = 3;
        public const ushort ChangeMemberRankSub = 0x0A, KickSub = 0x0C, DescriptionSub = 0x0F, MuteSub = 0x10, AppDataFieldSub = 0x11;

        /// <summary>
        /// Rank order (level: 1 highest) and what each may do besides viewing invites. The leader has every permission;
        /// officers invite, kick and promote / demote members and set the message of the day.
        /// </summary>
        private static uint LevelOf(uint rank) => rank == LeaderRank ? 1u : rank == OfficerRank ? 2u : 3u;

        /// <summary>A rank's level in this guild (1 highest; an unknown rank counts as the lowest).</summary>
        private static uint Level(Guild guild, uint rank) => guild.Ranks.FirstOrDefault(r => r.Id == rank)?.Level ?? 99u;
        private static readonly string[] OfficerPermissions =
            { "CreateGuildInvite", "ViewGuildInvites", "KickFromGuild", "ChangeMemberRank", "UpdateMotd", "ViewGuildLog",
              JoinOfficerChat, MuteGuildChat };
        /// <summary>Chat channels (ChatChannels.txt rows 6 / 7, wire ids 5 / 6): guild chat and officer chat.</summary>
        public const ushort ChatChannel = 5, OfficerChatChannel = 6;
        /// <summary>
        /// Rank permissions the client checks but its rank editor never lists (string table at 0x145218798, next to
        /// Guild.MaxMembers): JoinOfficerChat makes the client join chat channel 6 (FUN_142e5c120, on every guild /
        /// rank update), MuteGuildChat / MuteOfficerChat allow "/guild mute_member" for channel 5 / 6 (FUN_142e5ae70).
        /// </summary>
        public const string JoinOfficerChat = "JoinOfficerChat", MuteGuildChat = "MuteGuildChat", MuteOfficerChat = "MuteOfficerChat";
        /// <summary>Client constant Guild.MaxMembers (250; the 2016 wiki's guild page says the same).</summary>
        private const int MaxMembers = 250;
        private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(5);

        /// <summary>Permission names a rank can hold (client checks them by name, FUN_1439960b0).</summary>
        /// <summary>The guild a character is in (0 = none).</summary>
        public static ulong GuildIdOf(ulong character)
        {
            EnsureLoaded();
            lock (sync)
                return guildOf.GetValueOrDefault(character);
        }

        /// <summary>The id of the guild with this name (case-insensitive; 0 = none).</summary>
        public static ulong GuildIdByName(string name)
        {
            name = name?.Trim() ?? "";
            lock (sync)
            {
                EnsureLoaded();
                return guilds.Values.FirstOrDefault(g => string.Equals(g.Model.Name, name, StringComparison.OrdinalIgnoreCase))?.Model.Id ?? 0;
            }
        }

        /// <summary>A guild's name (null when there is no such guild).</summary>
        public static string GuildName(ulong guildId)
        {
            lock (sync)
            {
                EnsureLoaded();
                return guilds.TryGetValue(guildId, out Guild guild) ? guild.Model.Name : null;
            }
        }

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

        /// <summary>The three ranks every guild starts with (Leader 1, Officer 3, Member 2).</summary>
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
                // Guilds saved before the chat permissions existed: the leader holds every permission, the stock
                // Officer rank gets officer chat (it never had any of the three, and the rank editor can't add them).
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

        private class Guild
        {
            public GuildModel Model;
            public List<Rank> Ranks = DefaultRanks();
            /// <summary>The News tab's rows, oldest first (<see cref="AddLog"/>).</summary>
            public List<LogEntry> Log = new();
            /// <summary>Character id → (name, rank).</summary>
            public readonly Dictionary<ulong, (string Name, uint Rank)> Members = new();
        }

        private static readonly object sync = new();
        private static Dictionary<ulong, Guild> guilds;
        private static readonly Dictionary<ulong, ulong> guildOf = new();
        /// <summary>Invitee character id → (invitation id, guild id, inviter name and id, when).</summary>
        private static readonly Dictionary<ulong, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At)> invites = new();
        private static ulong nextInviteId = 1;

        /// <summary>
        /// Loads all guilds and their members from the database the first time guilds are needed.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (guilds != null)
                return;
            var database = DatabaseManager.Instance.CharacterDatabase;
            var loaded = database.GetGuilds().ToDictionary(g => g.Id, g => new Guild { Model = g, Ranks = ParseRanks(g.Ranks), Log = ParseLog(g.Log) });
            var members = database.GetGuildMembers();
            var names = database.GetCharacterNames(members.Select(m => m.CharacterId));
            foreach (GuildMemberModel member in members)
                if (loaded.TryGetValue(member.GuildId, out Guild guild))
                {
                    guild.Members[member.CharacterId] = (names.GetValueOrDefault(member.CharacterId, "?"), member.RankId);
                    guildOf[member.CharacterId] = member.GuildId;
                }
            guilds = loaded;
            log.Info($"Loaded {guilds.Count} guild(s).");
        }

        /// <summary>
        /// World entry: the player's open invitations (GuildInvitations 0x14, always, so a relog in the same client drops
        /// stale ones); a member gets their guild, online guildmates see them come online; a player in no guild gets an
        /// empty GuildDataFull 0x12, which empties the client's guild list (a guild left or lost while at character
        /// select would otherwise stay in the window).
        /// </summary>
        public static void Enter(WorldSession session)
        {
            Guild guild;
            byte[] invitations;
            lock (sync)
            {
                EnsureLoaded();
                // An invitation still open from before a relog comes back in the list, and the client queues its popup
                // again (FUN_142ef25f0: type 1 and invitee = the player -> GAQGuildInvite, FUN_1433da9d0). The client
                // also hands its unanswered Game Action Queue entries to the server when its UI shuts down
                // (Notifications ListNotifications, FUN_1433302e0 from GuiOnShutdown FUN_142c3f3c0); the server keeps
                // the invitation itself instead.
                var open = new List<(ulong GuildId, string GuildName, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) Invite)>();
                if (invites.TryGetValue(session.PlayerGuid, out var pending) && DateTime.UtcNow - pending.At <= InviteLifetime
                    && guilds.TryGetValue(pending.Guild, out Guild invitedTo) && !guildOf.ContainsKey(session.PlayerGuid))
                    open.Add((invitedTo.Model.Id, invitedTo.Model.Name, pending));
                invitations = BuildInvitations(open, session.PlayerGuid, session.CharacterName);
                guild = GuildOf(session.PlayerGuid);
                // Picks up a rename since the roster was loaded.
                if (guild != null)
                    guild.Members[session.PlayerGuid] = (session.CharacterName, guild.Members[session.PlayerGuid].Rank);
            }
            if (guild == null)
            {
                session.EnqueueMessage(ZoneMessageOpcode.GuildBase, EmptyDataFull());
                session.EnqueueMessage(ZoneMessageOpcode.GuildBase, invitations);
                return;
            }
            session.EnqueueMessage(ZoneMessageOpcode.GuildBase, invitations);
            SendGuild(session, guild);
            SendToMembers(guild, OnlineStatus(guild, session.PlayerGuid, true), session);
        }

        /// <summary>
        /// A member went offline: their guild's online members see the status change.
        /// </summary>
        public static void Leave(WorldSession session)
        {
            Guild guild;
            lock (sync)
            {
                if (guilds == null)
                    return;
                guild = GuildOf(session.PlayerGuid);
                if (guild == null)
                    return;
            }
            SendToMembers(guild, OnlineStatus(guild, session.PlayerGuid, false), session);
        }

        /// <summary>
        /// Officer chat (channel 6): the online members of the sender's guild whose rank has JoinOfficerChat - the same
        /// members whose client joined the channel. Nobody when the sender can't use it.
        /// </summary>
        public static List<WorldSession> OfficersOf(WorldSession session)
        {
            Guild guild;
            lock (sync)
            {
                EnsureLoaded();
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || !Has(guild, session.PlayerGuid, JoinOfficerChat))
                    return new List<WorldSession>();
            }
            List<WorldSession> online = OnlineMembers(guild);
            lock (sync)
                return online.Where(s => Has(guild, s.PlayerGuid, JoinOfficerChat)).ToList();
        }

        /// <summary>(guild, channel, member) -> muted until (UTC; MaxValue = until unmuted). Kept while the server runs.</summary>
        private static readonly Dictionary<(ulong Guild, ushort Channel, ulong Member), DateTime> mutes = new();

        /// <summary>Is this member muted on guild channel 5 / 6? (Chat drops their lines.)</summary>
        public static bool IsMuted(WorldSession session, ushort channel)
        {
            lock (sync)
            {
                ulong guild = guildOf.GetValueOrDefault(session.PlayerGuid);
                if (guild == 0 || !mutes.TryGetValue((guild, channel, session.PlayerGuid), out DateTime until))
                    return false;
                if (until > DateTime.UtcNow)
                    return true;
                mutes.Remove((guild, channel, session.PlayerGuid));
                return false;
            }
        }

        /// <summary>
        /// GuildRequestMutePlayer (0x10; "/guild_debug mute_member &lt;guild id&gt; &lt;member guid&gt; [guildchat] [minutes]"
        /// and unmute_member — both ids are parsed as decimal numbers (FUN_1432dc490 / FUN_142d04f30), "guildchat" picks
        /// channel 5 and anything else channel 6; chat command FUN_1432fbb50 -> FUN_142e5ae70, serializer FUN_142e44450): {u64 guild, u32 channel (5 guild /
        /// 6 officer), u32 duration (-1 = until unmuted), string member name, string requester name, u8 mute}. The client
        /// sends it only when the requester's rank has MuteGuildChat / MuteOfficerChat and is above the member's rank
        /// (else "Guild.InsufficientRank"); the server checks the same. The client has no reply text for it, so the
        /// result is a plain system line.
        /// </summary>
        private static void Mute(WorldSession session, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            uint channel = r.ReadUInt();
            uint minutes = r.ReadUInt();
            string name = (r.ReadString() ?? "").Trim();
            r.ReadString();
            bool mute = r.ReadByte() != 0;
            string permission = channel == ChatChannel ? MuteGuildChat : channel == OfficerChatChannel ? MuteOfficerChat : null;
            ulong target;
            lock (sync)
            {
                Guild guild = GuildOf(session.PlayerGuid);
                if (guild == null || guild.Model.Id != guildId || permission == null || !Has(guild, session.PlayerGuid, permission))
                    return;
                target = guild.Members.FirstOrDefault(m => string.Equals(m.Value.Name, name, StringComparison.OrdinalIgnoreCase)).Key;
                if (target == 0 || target == session.PlayerGuid
                    || Level(guild, guild.Members[session.PlayerGuid].Rank) >= Level(guild, guild.Members[target].Rank))
                {
                    Chat.System(session, target == 0 ? $"{name} is not in your guild." : "You cannot mute that member.");
                    return;
                }
                var key = (guildId, (ushort)channel, target);
                if (mute)
                    mutes[key] = minutes == uint.MaxValue || minutes == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(minutes);
                else
                    mutes.Remove(key);
            }
            string where = channel == ChatChannel ? "guild chat" : "officer chat";
            Chat.System(session, mute ? $"{name} is muted in {where}." : $"{name} is no longer muted in {where}.");
            WorldSession muted = PlayerManager.FindByGuid(target);
            if (muted != null)
                Chat.System(muted, mute ? $"You have been muted in {where}." : $"You are no longer muted in {where}.");
            log.Info($"{session.CharacterName} {(mute ? "muted" : "unmuted")} {name} in guild {guildId} channel {channel}"
                + (mute && minutes != uint.MaxValue && minutes != 0 ? $" for {minutes} min." : "."));
        }

        /// <summary>The online members of the player's guild (guild chat).</summary>
        public static List<WorldSession> MembersOf(WorldSession session)
        {
            Guild guild;
            lock (sync)
            {
                EnsureLoaded();
                guild = GuildOf(session.PlayerGuid);
            }
            return guild == null ? new List<WorldSession>() : OnlineMembers(guild);
        }

        /// <summary>
        /// Routes a GuildBase request (u16 sub): 1 Create {string}, 4 Invite {u64 guild, u64, string name}, 5 InviteReply
        /// {u64, u8 accept}, 0x0B Motd {u64 guild, string}, 0x0D Quit {u64 guild}. Disband comes as a debug command.
        /// </summary>
        public static void Handle(WorldSession session, ushort sub, byte[] body)
        {
            lock (sync)
                EnsureLoaded();
            try
            {
                var reader = new GamePacketReader(body);
                switch (sub)
                {
                    case CreateSub:
                        Create(session, reader.ReadString()?.Trim() ?? "");
                        break;
                    case InviteSub:
                        reader.ReadULong();
                        reader.ReadULong();
                        Invite(session, reader.ReadString()?.Trim() ?? "");
                        break;
                    case InviteReplySub:
                        log.Debug($"Guild invite reply from {session.CharacterName}: {BitConverter.ToString(body)}");
                        Reply(session, body);
                        break;
                    case InviteRevokeSub:
                        Revoke(session, reader.ReadULong());
                        break;
                    case MotdSub:
                        reader.ReadULong();
                        // Shown in members' chat windows: typed rich-text tokens made harmless (L54).
                        SetMotd(session, Shared.Game.RichTextTokens.Neutralize(reader.ReadString() ?? ""));
                        break;
                    case QuitSub:
                        Remove(session.PlayerGuid, session.CharacterName);
                        break;
                    case KickSub:
                        reader.ReadULong();
                        Kick(session, reader.ReadULong());
                        break;
                    case ChangeMemberRankSub:
                        ChangeRank(session, body);
                        break;
                    case DisbandSub:
                        Disband(session);
                        break;
                    case UpdateRankSub:
                        UpdateRank(session, body);
                        break;
                    case DescriptionSub:
                        SetDescription(session, body);
                        break;
                    case AppDataFieldSub:
                        SetAppData(session, body);
                        break;
                    case MuteSub:
                        Mute(session, body);
                        break;
                    default:
                        Network.OpcodeStats.Unanswered("Guilds 0x83", sub);
                        log.Debug($"Guild packet 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body)}");
                        break;
                }
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable guild packet 0x{sub:X2} from {session.CharacterName}: {e.Message} {BitConverter.ToString(body)}");
            }
        }

        /// <summary>
        /// Creates a guild with the player as leader (name rules: 3-32 letters, digits, spaces or apostrophes; unique; the
        /// player must not be in a guild), saves it and sends the guild data.
        /// </summary>
        private static void Create(WorldSession session, string name)
        {
            if (name.Length < 3 || name.Length > 32 || name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '\''))
            {
                Notify(session, NotifyCreateFailed, name);
                return;
            }
            Guild guild;
            lock (sync)
            {
                if (guildOf.ContainsKey(session.PlayerGuid))
                {
                    Notify(session, NotifyAlreadyInGuild, name);
                    return;
                }
                if (guilds.Values.Any(g => string.Equals(g.Model.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Notify(session, NotifyNameTaken, name);
                    return;
                }
                // Worlds sharing the character database each number new guilds in their own range (world n: n × 2^20
                // up to the next), so two running worlds never pick the same id.
                ulong low = DatabaseManager.Instance.CharacterDatabase.SeparateWorld
                    ? GuildIdRange * Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId : 0;
                ulong id = Math.Max(low, guilds.Keys.Where(k => k >= low && k < low + (low == 0 ? ulong.MaxValue : GuildIdRange))
                    .DefaultIfEmpty(0ul).Max()) + 1;
                guild = new Guild { Model = new GuildModel { Id = id, Name = name, Motd = "", LeaderId = session.PlayerGuid, CreateTime = DateTime.UtcNow } };
                guild.Members[session.PlayerGuid] = (session.CharacterName, LeaderRank);
                guilds[id] = guild;
                guildOf[session.PlayerGuid] = id;
            }
            GuildModel model = guild.Model;
            ulong characterId = session.PlayerGuid;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.Guild.Add(new GuildModel { Id = model.Id, Name = model.Name, Motd = model.Motd, LeaderId = model.LeaderId, CreateTime = model.CreateTime });
                c.GuildMember.Add(new GuildMemberModel { CharacterId = characterId, GuildId = model.Id, RankId = LeaderRank });
            });
            SendGuild(session, guild);
            Notify(session, NotifyCreated, model.Name);
            Achievements.GuildJoined(session);
            log.Info($"{session.CharacterName} created guild {model.Id} '{model.Name}'.");
        }

        /// <summary>
        /// Invites an online player (needs the inviter's CreateGuildInvite permission): the target gets UpdateInvitation,
        /// and accepts with "/guild_debug accept invite" (or the InviteReply packet).
        /// </summary>
        private static void Invite(WorldSession session, string name)
        {
            WorldSession target = PlayerManager.Snapshot()
                .FirstOrDefault(p => string.Equals(p.CharacterName, name, StringComparison.OrdinalIgnoreCase));
            Guild guild;
            (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || !Has(guild, session.PlayerGuid, "CreateGuildInvite"))
                    return;
                if (target == null)
                {
                    Notify(session, NotifyPlayerNotFound, guild.Model.Name, name);
                    return;
                }
                if (guildOf.ContainsKey(target.PlayerGuid))
                {
                    Notify(session, NotifyPlayerInGuild, guild.Model.Name, target.CharacterName);
                    return;
                }
                if (Ignores.Refuses(target, session))
                    return;
                if (guild.Members.Count >= MaxMembers)
                {
                    Notify(session, NotifyGeneralFail, guild.Model.Name, target.CharacterName);
                    return;
                }
                if (invites.TryGetValue(target.PlayerGuid, out var pending) && pending.Guild == guild.Model.Id
                    && DateTime.UtcNow - pending.At <= InviteLifetime)
                {
                    Notify(session, NotifyAlreadyInvited, guild.Model.Name, target.CharacterName);
                    return;
                }
                invite = (nextInviteId++, guild.Model.Id, session.CharacterName, session.PlayerGuid, DateTime.UtcNow);
                invites[target.PlayerGuid] = invite;
            }
            // Both ends get the invitation: the client's list keeps sent ones too (its "[target] has declined" branch is
            // for an invitation whose invitee isn't the player), and /guild_debug revoke invite names it by its id.
            // The invitee's client queues the Accept / Decline popup itself (invitation type 1 to the player:
            // FUN_142ef4e00 -> GAQGuildInvite with the guild's and the inviter's names).
            byte[] invitation = BuildInvitation(guild, invite, target.PlayerGuid, target.CharacterName, InvitationOpen);
            target.EnqueueMessage(ZoneMessageOpcode.GuildBase, invitation);
            session.EnqueueMessage(ZoneMessageOpcode.GuildBase, invitation);
            Notify(session, NotifyInvited, guild.Model.Name, target.CharacterName);
            log.Info($"{session.CharacterName} invited {target.CharacterName} to guild {guild.Model.Id}.");
        }

        /// <summary>
        /// InviteReply (5): accept or decline. The invitation leaves both lists: state 2 (silently) on accept, state 3 on
        /// decline (the invitee sees "You have declined…", the inviter "[target] has declined…").
        /// </summary>
        private static void Reply(WorldSession session, byte[] body)
        {
            bool accept = ReplyAccepts(body);
            Guild guild;
            (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite;
            lock (sync)
            {
                if (!invites.Remove(session.PlayerGuid, out invite) || DateTime.UtcNow - invite.At > InviteLifetime
                    || !guilds.TryGetValue(invite.Guild, out guild) || guildOf.ContainsKey(session.PlayerGuid))
                    return;
                if (accept)
                {
                    guild.Members[session.PlayerGuid] = (session.CharacterName, MemberRank);
                    guildOf[session.PlayerGuid] = guild.Model.Id;
                }
            }
            byte[] closed = BuildInvitation(guild, invite, session.PlayerGuid, session.CharacterName,
                accept ? InvitationClosed : InvitationDeclined);
            session.EnqueueMessage(ZoneMessageOpcode.GuildBase, closed);
            PlayerManager.FindByGuid(invite.FromGuid)?.EnqueueMessage(ZoneMessageOpcode.GuildBase, closed);
            if (!accept)
                return;
            ulong characterId = session.PlayerGuid, guildId = guild.Model.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
                c.GuildMember.Add(new GuildMemberModel { CharacterId = characterId, GuildId = guildId, RankId = MemberRank }));
            SendGuild(session, guild);
            SendToMembers(guild, BuildAddMember(guild, session.PlayerGuid, session.CharacterName, MemberRank), session);
            AddLog(guild, LogJoined, session.CharacterName, "");
            Achievements.GuildJoined(session);
            log.Info($"{session.CharacterName} joined guild {guildId}.");
        }

        /// <summary>InviteReply body: u64 invitation, u8 accept ("/guild_debug accept invite …" = 1, decline = 0).</summary>
        private static bool ReplyAccepts(byte[] body) => body.Length >= 9 && body[8] != 0;

        /// <summary>
        /// GuildInviteRevoke (6) {u64 invitation id} — "/guild_debug revoke invite &lt;id&gt;" (client FUN_1432fbb50; the
        /// id is typed as a decimal number, like accept's) or Lua Guild.InviteRevoke (FUN_143507b00; no 0.589 / 0.695 UI
        /// calls it). Needs RevokeGuildInvite in the invitation's guild. The invitation goes away for the invitee, the
        /// inviter and the revoker with state 1 ("The invitation to join [guild] has been revoked.").
        /// </summary>
        private static void Revoke(WorldSession session, ulong invitationId)
        {
            Guild guild;
            ulong invitee;
            (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || !Has(guild, session.PlayerGuid, "RevokeGuildInvite"))
                    return;
                invitee = 0;
                invite = default;
                foreach (var (who, open) in invites)
                    if (open.Id == invitationId && open.Guild == guild.Model.Id)
                        (invitee, invite) = (who, open);
                if (invitee == 0)
                    return;
                invites.Remove(invitee);
            }
            WorldSession target = PlayerManager.FindByGuid(invitee);
            byte[] revoked = BuildInvitation(guild, invite, invitee, target?.CharacterName ?? "", InvitationRevoked);
            foreach (ulong guid in new[] { invitee, invite.FromGuid, session.PlayerGuid }.Distinct())
                PlayerManager.FindByGuid(guid)?.EnqueueMessage(ZoneMessageOpcode.GuildBase, revoked);
            log.Info($"{session.CharacterName} revoked guild {guild.Model.Id}'s invitation {invitationId}.");
        }

        /// <summary>
        /// Sets the message of the day (needs UpdateMotd; at most 500 characters), saves it and sends it to online members.
        /// </summary>
        /// <summary>
        /// GuildUpdateRank from the guild window (0x0E; Lua Guild.RankAdd / RankSetName / RankAddPermission /
        /// RankRemovePermission / RankToggleAllPermissions / RankRemove, FUN_143508990 …): u64 guild, the edited rank as
        /// the client now has it {u32 level, string name, u32, string, u32, u32 n × permission, u32, u32, u32 n × {…},
        /// string, u32 rank id (0 = new)}, u32 action (1 set, 2 remove). Needs ModifyRanks. The leader's rank keeps every
        /// permission; the Leader and Member ranks can't be removed; members of a removed rank become Members. Every
        /// online member gets the rank back (action 1, or 2 when removed).
        /// </summary>
        private static void UpdateRank(WorldSession session, byte[] body)
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
            uint skip = r.ReadUInt();
            if (skip != 0)
            {
                log.Debug($"Guild rank update from {session.CharacterName} with {skip} extra entries: {BitConverter.ToString(body)}");
                return;
            }
            r.ReadString();
            uint rankId = r.ReadUInt();
            uint action = r.ReadUInt();
            Guild guild;
            Rank changed;
            bool created = false;
            var moved = new List<ulong>();
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || guild.Model.Id != guildId || !Has(guild, session.PlayerGuid, "ModifyRanks"))
                    return;
                Rank existing = guild.Ranks.FirstOrDefault(x => x.Id == rankId);
                // A member below the leader who was given ModifyRanks edits only the ranks below their own, keeps the ranks
                // they set below their own, and grants only permissions they have [I: live's rule is not traced yet]. Before,
                // such an officer could give their own rank DisbandGuild, TransferGuildLeadership or KickFromGuild and
                // level 2, or strip the ranks above them.
                uint editorRank = guild.Members[session.PlayerGuid].Rank;
                if (editorRank != LeaderRank)
                {
                    uint own = Level(guild, editorRank);
                    if (existing != null && Level(guild, existing.Id) <= own)
                    {
                        log.Info($"{session.CharacterName} may not change guild {guild.Model.Id}'s rank {existing.Id} (not below their own).");
                        return;
                    }
                    level = Math.Max(level, own + 1);
                    permissions = permissions.Where(p => Has(guild, session.PlayerGuid, p)).ToList();
                }
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
                    if (name.Length == 0 || name.Length > 32 || guild.Ranks.Any(x => x.Id != rankId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
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
                    // The leader stays at the top with every permission; other ranks sit below it.
                    existing.Level = existing.Id == LeaderRank ? 1u : Math.Clamp(level, 2u, 20u);
                    existing.Permissions = existing.Id == LeaderRank ? AllPermissions.ToList() : allowed;
                    changed = existing;
                }
            }
            SaveRanks(guild);
            foreach (ulong m in moved)
                DatabaseManager.Instance.CharacterDatabase.Save(c =>
                {
                    var model = new GuildMemberModel { CharacterId = m, GuildId = guild.Model.Id };
                    c.GuildMember.Attach(model);
                    model.RankId = MemberRank;
                    c.MarkModified(model, nameof(model.RankId));
                });
            SendToMembers(guild, BuildRank(guild.Model.Id, changed.Id, changed.Level, changed.Name, changed.Permissions.ToArray(), action == 2 ? 2u : 1u));
            if (moved.Count > 0)
                ResendGuild(guild);
            AddLog(guild, action == 2 ? LogDeleteRank : created ? LogCreateRank : LogUpdateRank, session.CharacterName, "", changed.Id);
            log.Info($"{session.CharacterName} {(action == 2 ? "removed" : "set")} guild {guild.Model.Id} rank {changed.Id} '{changed.Name}' "
                + $"(level {changed.Level}, {changed.Permissions.Count} permissions).");
        }

        /// <summary>
        /// The whole guild again to every online member: GuildData resets the client's guild (its rank definitions too),
        /// so the ranks follow it — without them the window's own permission checks fail ("Guild.InsufficientRank").
        /// </summary>
        private static void ResendGuild(Guild guild)
        {
            foreach (WorldSession member in OnlineMembers(guild))
                SendGuild(member, guild);
        }

        private static void SaveRanks(Guild guild)
        {
            string json;
            lock (sync)
                json = System.Text.Json.JsonSerializer.Serialize(guild.Ranks);
            guild.Model.Ranks = json;
            ulong id = guild.Model.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new GuildModel { Id = id };
                c.Guild.Attach(model);
                model.Ranks = json;
                c.MarkModified(model, nameof(model.Ranks));
            });
        }

        /// <summary>
        /// GuildDescriptionUpdate (0x0F; Lua Guild.SetDescription FUN_143506e40, also RankSetDescription FUN_143509470,
        /// which in 0.589 sends the same packet with no rank in it): u64 guild, three strings, the text in the one that
        /// isn't empty. Needs UpdateDesc; the guild's members get the guild again (Description is in its core).
        /// </summary>
        private static void SetDescription(WorldSession session, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            string text = Shared.Game.RichTextTokens.Neutralize(
                new[] { r.ReadString(), r.ReadString(), r.ReadString() }.LastOrDefault(x => !string.IsNullOrEmpty(x)) ?? "");
            if (text.Length > 1000)
                text = text.Substring(0, 1000);
            Guild guild;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || guild.Model.Id != guildId || !Has(guild, session.PlayerGuid, "UpdateDesc"))
                    return;
                guild.Model.Description = text;
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new GuildModel { Id = guildId };
                c.Guild.Attach(model);
                model.Description = text;
                c.MarkModified(model, nameof(model.Description));
            });
            ResendGuild(guild);
            log.Info($"{session.CharacterName} set guild {guildId}'s description.");
        }

        /// <summary>
        /// GuildUpdateAppDataField (0x11; Lua Guild.SetTag FUN_143506b50): u64 guild, string — the whole GuildGameData XML (GuildEmblem, RecruitZinger, RecruitDescription, RatingReq, OpenToApps, GuildKeywords
        /// = the tag). Needs UpdateDesc; stored as is and sent back in the guild core.
        /// </summary>
        private static void SetAppData(WorldSession session, byte[] body)
        {
            var r = new GamePacketReader(body);
            ulong guildId = r.ReadULong();
            string xml = r.ReadString() ?? "";
            if (xml.Length > 2000 || !xml.TrimStart().StartsWith("<GuildGameData"))
                return;
            Guild guild;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || guild.Model.Id != guildId || !Has(guild, session.PlayerGuid, "UpdateDesc"))
                    return;
                guild.Model.AppData = xml;
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new GuildModel { Id = guildId };
                c.Guild.Attach(model);
                model.AppData = xml;
                c.MarkModified(model, nameof(model.AppData));
            });
            ResendGuild(guild);
            log.Info($"{session.CharacterName} set guild {guildId}'s app data ({xml.Length} chars).");
        }


        private static void SetMotd(WorldSession session, string motd)
        {
            motd = motd.Length > 500 ? motd.Substring(0, 500) : motd;
            Guild guild;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || !Has(guild, session.PlayerGuid, "UpdateMotd"))
                    return;
                guild.Model.Motd = motd;
            }
            ulong id = guild.Model.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new GuildModel { Id = id };
                c.Guild.Attach(model);
                model.Motd = motd;
                c.MarkModified(model, nameof(model.Motd));
            });
            SendToMembers(guild, Build(MotdSub, w =>
            {
                w.Write(id);
                w.Write(motd);
                w.Write(session.CharacterName);
                w.Write(0ul);
            }));
        }

        /// <summary>
        /// A character was deleted (TASKS 6.42; P4624: deleted characters leave friend and guild lists, P5311 no guild
        /// / officer chat after deleting and recreating): it leaves its guild as with
        /// /guildquit, under the name it had (P5312 asks guild news to keep deleted players' names; there is no guild
        /// news yet). Idempotent.
        /// </summary>
        public static void CharacterDeleted(ulong characterId, string name)
        {
            string known;
            lock (sync)
            {
                if (GuildOf(characterId) is not Guild guild)
                    return;
                known = guild.Members.TryGetValue(characterId, out var member) && !string.IsNullOrEmpty(member.Name) ? member.Name : name;
            }
            Remove(characterId, known ?? "");
        }

        /// <summary>A member leaves (quit). The leader's leaving passes the lead on; the last member ends the guild.</summary>
        private static void Remove(ulong characterId, string name, string kickedBy = null)
        {
            Guild guild;
            bool ended;
            ulong newLeader = 0;
            lock (sync)
            {
                guild = GuildOf(characterId);
                if (guild == null)
                    return;
                guild.Members.Remove(characterId);
                guildOf.Remove(characterId);
                ended = guild.Members.Count == 0;
                if (ended)
                    guilds.Remove(guild.Model.Id);
                else if (guild.Model.LeaderId == characterId)
                {
                    newLeader = guild.Members.Keys.First();
                    guild.Model.LeaderId = newLeader;
                    guild.Members[newLeader] = (guild.Members[newLeader].Name, LeaderRank);
                }
            }
            ulong guildId = guild.Model.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.GuildMember.Remove(new GuildMemberModel { CharacterId = characterId });
                if (ended)
                    c.Guild.Remove(new GuildModel { Id = guildId });
                else if (newLeader != 0)
                {
                    var model = new GuildModel { Id = guildId };
                    c.Guild.Attach(model);
                    model.LeaderId = newLeader;
                    c.MarkModified(model, nameof(model.LeaderId));
                    var member = new GuildMemberModel { CharacterId = newLeader };
                    c.GuildMember.Attach(member);
                    member.RankId = LeaderRank;
                    c.MarkModified(member, nameof(member.RankId));
                }
            });
            // RemoveMember about themselves takes the guild off the leaver's client ("You have been removed from the
            // Guild.", guild chat channels closed; FUN_142e55c.. self branch). Disband would have said "[guild] has been
            // disbanded." to someone who only left.
            byte[] removed = Build(RemoveMemberSub, w =>
            {
                // {u64 guild, u64 member, string name} (reader FUN_142ee5650, must be consumed exactly) [P]; a stray
                // string of the member id made the client drop it until the 2026-10-17 wire audit (audit-wire-c.md #1).
                w.Write(guildId);
                w.Write(characterId);
                w.Write(name);
            });
            if (PlayerManager.FindByGuid(characterId) is WorldSession leaver)
                leaver.EnqueueMessage(ZoneMessageOpcode.GuildBase, removed);
            if (!ended)
            {
                SendToMembers(guild, removed);
                if (kickedBy != null)
                    AddLog(guild, LogKicked, kickedBy, name);
                else
                    AddLog(guild, LogQuit, name, "");
                if (newLeader != 0)
                {
                    string leaderName;
                    lock (sync)
                        leaderName = guild.Members.TryGetValue(newLeader, out var leader) ? leader.Name : "";
                    AddLog(guild, LogNewLeader, "", leaderName);
                    foreach (WorldSession member in OnlineMembers(guild))
                        SendGuild(member, guild);
                }
            }
            log.Info($"{name} left guild {guildId}{(ended ? " (guild ended)" : "")}.");
        }

        /// <summary>
        /// Disbands the guild (leader only): removes it from the database and tells every online member (Disband, sub 2).
        /// </summary>
        private static void Disband(WorldSession session)
        {
            Guild guild;
            List<ulong> members;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || !Has(guild, session.PlayerGuid, "DisbandGuild"))
                    return;
                members = guild.Members.Keys.ToList();
                foreach (ulong member in members)
                    guildOf.Remove(member);
                guilds.Remove(guild.Model.Id);
            }
            List<WorldSession> online = members.Select(PlayerManager.FindByGuid).Where(s => s != null).ToList();
            ulong guildId = guild.Model.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                foreach (ulong member in members)
                    c.GuildMember.Remove(new GuildMemberModel { CharacterId = member });
                c.Guild.Remove(new GuildModel { Id = guildId });
            });
            byte[] packet = Build(DisbandSub, w => w.Write(guildId));
            foreach (WorldSession member in online)
                member.EnqueueMessage(ZoneMessageOpcode.GuildBase, packet);
            log.Info($"{session.CharacterName} disbanded guild {guildId}.");
        }

        /// <summary>
        /// The guild a character belongs to, or null.
        /// </summary>
        private static Guild GuildOf(ulong characterId)
            => guildOf.TryGetValue(characterId, out ulong id) && guilds.TryGetValue(id, out Guild guild) ? guild : null;

        /// <summary>
        /// GuildKick (0x0C; Lua Guild.Kick from the roster's right-click menu, FUN_143508640; writer FUN_142e44120):
        /// u64 guild, u64 member, string. Needs KickFromGuild and a target of a lower rank; the target leaves as with
        /// /guildquit (RemoveMember to the guild, Disband to the target).
        /// </summary>
        private static void Kick(WorldSession session, ulong target)
        {
            Guild guild;
            string name;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || target == session.PlayerGuid || !Has(guild, session.PlayerGuid, "KickFromGuild")
                    || !guild.Members.TryGetValue(target, out var member)
                    || Level(guild, member.Rank) <= Level(guild, guild.Members[session.PlayerGuid].Rank))
                {
                    Notify(session, NotifyGeneralFail, guild?.Model.Name ?? "");
                    return;
                }
                name = member.Name;
            }
            // The removed player's client says "You have been removed from the Guild." itself (RemoveMember about them).
            Remove(target, name, kickedBy: session.CharacterName);
            log.Info($"{session.CharacterName} removed {name} from guild {guild.Model.Id}.");
        }

        /// <summary>
        /// GuildChangeMemberRank (0x0A; Lua Guild.PromoteMember / DemoteMember → ChangeMemberRank FUN_143507e30, writer
        /// FUN_142e442b0): u64 guild, u64 member, string, u64, string (arg 3), u32, string (arg 4), u8, u8, u32. Needs
        /// ChangeMemberRank and a member below the changer. A rank named in the strings is used when it is one of ours;
        /// otherwise the member moves one step (Member ↔ Officer). The guild gets ChangeMemberRank back (reader
        /// FUN_142e457c0: u64 guild, u64 member, string, u64 by, string by name, u32 rank) and the roster updates.
        /// </summary>
        private static void ChangeRank(WorldSession session, byte[] body)
        {
            var r = new GamePacketReader(body);
            r.ReadULong();
            ulong target = r.ReadULong();
            r.ReadString(); r.ReadULong();
            string a = r.ReadString();
            r.ReadUInt();
            string b = r.ReadString();
            log.Info($"{session.CharacterName}: guild rank change for 0x{target:X} ('{a}', '{b}').");
            Guild guild;
            uint newRank, oldRank = 0;
            string name;
            lock (sync)
            {
                guild = GuildOf(session.PlayerGuid);
                if (guild == null || !Has(guild, session.PlayerGuid, "ChangeMemberRank")
                    || !guild.Members.TryGetValue(target, out var member)
                    || Level(guild, member.Rank) <= Level(guild, guild.Members[session.PlayerGuid].Rank))
                {
                    Notify(session, NotifyChangeRankFailed, guild?.Model.Name ?? "");
                    return;
                }
                uint? named = new[] { a, b }.Select(n => guild.Ranks.FirstOrDefault(r => string.Equals(r.Name, n?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id ?? RankByName(n))
                    .FirstOrDefault(x => x != null && x != member.Rank);
                newRank = named ?? (member.Rank == MemberRank ? OfficerRank : MemberRank);
                // Nobody becomes leader this way, nor reaches the changer's own rank unless they lead.
                if (newRank == LeaderRank || (Level(guild, newRank) <= Level(guild, guild.Members[session.PlayerGuid].Rank)
                    && guild.Members[session.PlayerGuid].Rank != LeaderRank))
                {
                    Notify(session, NotifyChangeRankFailed, guild.Model.Name);
                    return;
                }
                name = member.Name;
                oldRank = member.Rank;
                guild.Members[target] = (member.Name, newRank);
            }
            uint rank = newRank;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new GuildMemberModel { CharacterId = target };
                c.GuildMember.Attach(model);
                model.RankId = rank;
                c.MarkModified(model, nameof(model.RankId));
            });
            SendToMembers(guild, Build(ChangeMemberRankSub, w =>
            {
                w.Write(guild.Model.Id);
                w.Write(target);
                w.Write(name);
                w.Write(session.PlayerGuid);
                w.Write(session.CharacterName ?? "");
                w.Write(rank);
            }));
            // Level 1 is the top: a smaller level is a promotion.
            bool promoted;
            lock (sync)
                promoted = Level(guild, rank) < Level(guild, oldRank);
            AddLog(guild, promoted ? LogPromoted : LogDemoted, session.CharacterName, name, 0, rank);
            log.Info($"{session.CharacterName} set {name}'s guild rank to {guild.Ranks.FirstOrDefault(r => r.Id == rank)?.Name ?? rank.ToString()}.");
        }

        private static uint? RankByName(string name) => name?.Trim().ToLowerInvariant() switch
        {
            "leader" => LeaderRank,
            "officer" => OfficerRank,
            "member" => MemberRank,
            _ => null
        };

        /// <summary>
        /// Does a member hold a rank permission? Only two ranks exist so far: the leader has them all, members can only view
        /// invites.
        /// </summary>
        private static bool Has(Guild guild, ulong characterId, string permission)
            => guild.Members.TryGetValue(characterId, out var member)
                && (member.Rank == LeaderRank || permission == "ViewGuildInvites"
                    || guild.Ranks.FirstOrDefault(r => r.Id == member.Rank)?.Permissions.Contains(permission) == true);

        /// <summary>
        /// The guild's members who are online now.
        /// </summary>
        private static List<WorldSession> OnlineMembers(Guild guild)
        {
            List<ulong> ids;
            lock (sync)
                ids = guild.Members.Keys.ToList();
            return ids.Select(PlayerManager.FindByGuid).Where(s => s != null).ToList();
        }

        /// <summary>
        /// Sends a GuildBase packet to every online member (optionally all but one).
        /// </summary>
        private static void SendToMembers(Guild guild, byte[] packet, WorldSession except = null)
        {
            foreach (WorldSession member in OnlineMembers(guild))
                if (member != except)
                    member.EnqueueMessage(ZoneMessageOpcode.GuildBase, packet);
        }

        /// <summary>The whole guild and its two ranks, to one member.</summary>
        private static void SendGuild(WorldSession session, Guild guild)
        {
            session.EnqueueMessage(ZoneMessageOpcode.GuildBase, BuildData(guild));
            List<Rank> ranks;
            lock (sync)
                ranks = guild.Ranks.ToList();
            foreach (Rank r in ranks)
                session.EnqueueMessage(ZoneMessageOpcode.GuildBase, BuildRank(guild.Model.Id, r.Id, r.Level, r.Name, r.Permissions.ToArray()));
        }

        /// <summary>
        /// GuildData (sub 0x13): the guild core, its members (online flags), no invitations or log, and the name list. The
        /// client applies it only when consumed exactly.
        /// </summary>
        private static byte[] BuildData(Guild guild)
        {
            List<(ulong Id, string Name, uint Rank)> members;
            GuildModel model;
            List<LogEntry> news;
            lock (sync)
            {
                members = guild.Members.Select(m => (m.Key, m.Value.Name, m.Value.Rank)).ToList();
                model = guild.Model;
                news = guild.Log.ToList();
            }
            var online = PlayerManager.Snapshot().Select(s => s.PlayerGuid).ToHashSet();
            return Build(DataSub, w =>
            {
                w.Write(model.Id);
                w.Write(model.Name);
                for (int i = 0; i < 5; i++)
                    w.Write("");
                w.Write(model.Description ?? "");     // core +0x108: the window's Description (FUN_142e5c6f0)
                w.Write(model.LeaderId);
                w.Write(model.AppData ?? "");         // core +0x168: GuildGameData XML (keywords / tag, FUN_143b92050)
                w.Write(0ul);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(model.Motd ?? "");
                w.Write("");
                w.Write(0ul);
                w.Write((uint)members.Count);
                foreach (var member in members)
                    WriteMember(w, member.Id, member.Rank, online.Contains(member.Id));
                w.Write(0u);                           // invitations (list reader FUN_142e48340)
                w.Write((uint)news.Count);             // News (list reader FUN_142e47de0): u32 key, then the row
                foreach (LogEntry entry in news)
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

        /// <summary>
        /// Writes a member record. Its first field is the guid as a decimal string, which the client parses.
        /// </summary>
        private static void WriteMember(GamePacketWriter w, ulong id, uint rank, bool online)
        {
            w.Write(id.ToString());
            w.Write(id);
            w.Write(rank);
            w.Write(0ul);
            w.Write(MemberGameData(id));
            w.Write(online);
            w.Write(0ul);
        }

        /// <summary>
        /// The member's "game data" the roster shows (client parser FUN_143b92760, an XML element): LastZone and
        /// LastWorld are NAME TEXT ids (the Location column reads "world:zone"; 0 shows "UnknownWorld:UnknownZone"),
        /// LastOnline unix seconds. Online members report their current landscape, others the one they logged out on.
        /// </summary>
        private static string MemberGameData(ulong id)
        {
            WorldSession session = PlayerManager.Snapshot().FirstOrDefault(p => p.PlayerGuid == id);
            uint zone;
            long lastOnline;
            if (session != null)
            {
                zone = session.ZoneId;
                lastOnline = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            else
            {
                var model = DatabaseManager.Instance.CharacterDatabase.GetCharacterById(id).GetAwaiter().GetResult();
                zone = model?.ZoneId ?? 0;
                lastOnline = model?.LastOnline is DateTime t
                    ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0;
            }
            uint zoneName = Landscapes.Get(zone)?.NameText ?? 0;
            uint worldName = zoneName != 0 ? Landscapes.WorldNameText : 0;
            return $"<MemberGameData LastOnline=\"{lastOnline}\" BattleRank=\"0\" LastZone=\"{zoneName}\" LastWorld=\"{worldName}\" LastContinent=\"0\"/>";
        }

        /// <summary>
        /// AddMember (sub 0x15): a new member for the online members' lists.
        /// </summary>
        private static byte[] BuildAddMember(Guild guild, ulong id, string name, uint rank) => Build(AddMemberSub, w =>
        {
            w.Write(guild.Model.Id);
            WriteMember(w, id, rank, true);
            w.Write(name);
            w.Write(0ul);
            w.Write(0ul);
            w.Write("");
        });

        /// <summary>
        /// MemberOnlineStatus (sub 0x18 {u64 guild, u64 member, u8 online}).
        /// </summary>
        private static byte[] OnlineStatus(Guild guild, ulong id, bool online) => Build(OnlineStatusSub, w =>
        {
            w.Write(guild.Model.Id);
            w.Write(id);
            w.Write(online);
        });

        /// <summary>
        /// UpdateInvitation (sub 0x17; 0.589 reader FUN_142e46b40, 0.695 handler FUN_142ef4e00): the invitation
        /// (<see cref="WriteInvitation"/>), string inviter name, string invitee name (the texts' [*source*] / [*target*]),
        /// u32 state (<see cref="InvitationOpen"/> …). State 0 adds / updates the client's entry and, when its type is 1 and
        /// the invitee is the player, queues the GAQGuildInvite popup (FUN_1433da9d0 -> FUN_1433da9e0, once per invitation id).
        /// </summary>
        private static byte[] BuildInvitation(Guild guild, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite,
            ulong toGuid, string toName, uint state) => Build(UpdateInvitationSub, w =>
        {
            WriteInvitation(w, guild.Model.Id, guild.Model.Name, invite, toGuid);
            w.Write(invite.From);
            w.Write(toName);
            w.Write(state);
        });

        /// <summary>
        /// One invitation (Guild::GuildInvitationDetails; 0.695 reader FUN_142ee4ee0) [P]: u64 invitation id (+0x0, the
        /// client's list key and the popup's id), u64 guild (+0x8), string guild name (+0x10), string inviter id (+0xb0),
        /// string invitee id (+0xf0; decimal, parsed with FUN_142c0c650 and compared with the player's own guid), u64 time
        /// (+0x130; the popup's date line, from _time64 seconds, FUN_1433dc980), string (+0x138; written as the invitee id,
        /// not read by the paths traced), u32 type (+0x150: 1 = an invitation to join, the only type the client queues a
        /// popup for), u32 (+0x154, 0).
        /// </summary>
        private static void WriteInvitation(GamePacketWriter w, ulong guildId, string guildName,
            (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) invite, ulong toGuid)
        {
            w.Write(invite.Id);
            w.Write(guildId);
            w.Write(guildName ?? "");
            w.Write(invite.FromGuid.ToString());
            w.Write(toGuid.ToString());
            w.Write((ulong)Math.Max(0, new DateTimeOffset(DateTime.SpecifyKind(invite.At, DateTimeKind.Utc)).ToUnixTimeSeconds()));
            w.Write(toGuid.ToString());
            w.Write(InvitationTypeInvite);
            w.Write(0u);
        }

        /// <summary>Invitation type (+0x150): an invitation to join (petitions, the client's other kind, are not built).</summary>
        private const uint InvitationTypeInvite = 1;

        /// <summary>
        /// GuildInvitations (sub 0x14; reader FUN_142ee5040, handler FUN_142ef25f0) [P]: u32 n × invitation
        /// (<see cref="WriteInvitation"/>), then the names list (FUN_142ee7aa0: u32 n × {u64 guid, string name}) the client
        /// fills each entry's inviter / invitee names from. The handler empties the player's invitation list first, then
        /// queues the popup for each type-1 invitation to the player. Applied only when consumed exactly.
        /// </summary>
        internal static byte[] BuildInvitations(List<(ulong GuildId, string GuildName, (ulong Id, ulong Guild, string From, ulong FromGuid, DateTime At) Invite)> open,
            ulong toGuid, string toName) => Build(InvitationsSub, w =>
        {
            w.Write((uint)open.Count);
            var names = new Dictionary<ulong, string>();
            foreach (var (guildId, guildName, invite) in open)
            {
                WriteInvitation(w, guildId, guildName, invite, toGuid);
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

        /// <summary>
        /// GuildDataFull (sub 0x12; reader FUN_142ee4820, handler FUN_142ef1be0) [P]: u32 n × guild (the same element
        /// reader FUN_142ee4750 as GuildData's), then the names list. The handler empties the client's guild list first
        /// (FUN_142ef8830, as GuildData 0x13 does) and adds the n guilds; with n = 0 it only empties it.
        /// </summary>
        public static byte[] EmptyDataFull() => Build(DataFullSub, w =>
        {
            w.Write(0u);
            w.Write(0u);
        });

        /// <summary>
        /// GuildUpdateRank (0x0E, FUN_142e46c20): u64 guild id, rank {u32 level, string name, u32, string, u32,
        /// u32 n × permission, u32, u32, u32 n × {…}, string, u32 rank id}, u32 action (2 removes).
        /// </summary>
        private static byte[] BuildRank(ulong guildId, uint rankId, uint level, string name, string[] permissions, uint action = 1) => Build(UpdateRankSub, w =>
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

        /// <summary>
        /// Writes a GuildBase packet: the u16 sub-opcode, then the body.
        /// </summary>
        /// <summary>
        /// GuildNotification (sub 0x19, reader FUN_142e46710, handler FUN_142e54ae0): {string guild, name block player,
        /// u8 code}; the client shows its own line for the code (table at 0x1451356d0): 0 general failure, 1 "Guild
        /// '[guild]' created successfully.", 2 "Failed to create the Guild, [guild].", 3 "... Name already taken.", 4 "You
        /// have invited '[player]' to your Guild.", 5 "Guild invite failed.  Player not found.", 7 "[player] is already
        /// in a Guild.", 8 "[player] has already been invited to the Guild.", 13 "Error joining Guild.  You are already in
        /// a Guild.", 14 "You cannot change the member to that rank.".
        /// </summary>
        private const ushort NotificationSub = 0x19;
        private const byte NotifyGeneralFail = 0, NotifyCreated = 1, NotifyCreateFailed = 2, NotifyNameTaken = 3,
            NotifyInvited = 4, NotifyPlayerNotFound = 5, NotifyPlayerInGuild = 7, NotifyAlreadyInvited = 8,
            NotifyAlreadyInGuild = 13, NotifyChangeRankFailed = 14;

        private static void Notify(WorldSession session, byte code, string guild = "", string player = "")
        {
            session.EnqueueMessage(ZoneMessageOpcode.GuildBase, Build(NotificationSub, w =>
            {
                w.Write(guild ?? "");
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(player ?? "");
                w.Write("");
                w.Write(code);
            }));
            log.Debug($"{session.CharacterName}: guild notification {code} ('{guild}', '{player}').");
        }

        // ---- News (guild log) -----------------------------------------------------------------------------------------

        /// <summary>
        /// One row of the guild window's News tab (GameData.Character.GuildLogEntries; 0.589 / 0.695 NewsView). Wire
        /// (entry reader FUN_142e45fe0): u32 id, string source, string target, string type, u64 time (Unix seconds, shown
        /// in local time, FUN_142ab5cc0), u32 rank, u32 new rank, string comment, string app data. The client turns the
        /// type into text (FUN_142e5c9a0): Guild.Joined / Guild.Quit "[source] …", Guild.Kicked "[target] … by [source]",
        /// Guild.Promoted / Guild.Demoted "[target] … to [new rank] by [source]", Guild.NewLeader "[target] …", and
        /// CreateRank / UpdateRank / DeleteRank naming the rank (looked up in the guild's ranks).
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
        /// <summary>GuildAddLogUpdate (sub 0x1A, reader FUN_142e454f0): a row, then u64 guild.</summary>
        private const ushort AddLogSub = 0x1A;
        /// <summary>Rows kept per guild [I: live's limit isn't known].</summary>
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

        /// <summary>Adds a News row: kept (newest <see cref="MaxLog"/>), saved, and sent to the online members.</summary>
        private static void AddLog(Guild guild, string type, string source, string target, uint rank = 0, uint newRank = 0)
        {
            LogEntry entry;
            string json;
            List<uint> dropped = null;
            lock (sync)
            {
                entry = new LogEntry
                {
                    Id = guild.Log.Count == 0 ? 1 : guild.Log.Max(e => e.Id) + 1,
                    Type = type, Source = source ?? "", Target = target ?? "",
                    Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Rank = rank, NewRank = newRank
                };
                guild.Log.Add(entry);
                if (guild.Log.Count > MaxLog)
                {
                    dropped = guild.Log.Take(guild.Log.Count - MaxLog).Select(e => e.Id).ToList();
                    guild.Log.RemoveRange(0, guild.Log.Count - MaxLog);
                }
                json = System.Text.Json.JsonSerializer.Serialize(guild.Log);
            }
            guild.Model.Log = json;
            ulong id = guild.Model.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new GuildModel { Id = id };
                c.Guild.Attach(model);
                model.Log = json;
                c.MarkModified(model, nameof(model.Log));
            });
            // The oldest rows the cap dropped leave the online members' News tab too (it kept them until a relog).
            if (dropped is { Count: > 0 })
                SendToMembers(guild, BuildRemoveLog(id, dropped));
            SendToMembers(guild, Build(AddLogSub, w =>
            {
                WriteLogEntry(w, entry);
                w.Write(id);
            }));
        }

        /// <summary>
        /// GuildRemoveLogUpdate (sub 0x1B; reader FUN_142ee5580, handler FUN_142ef3a50) [P]: u32 n × u32 row id
        /// (FUN_142ee79b0), u64 guild. The client removes those rows from that guild's News map (guild +0x88, row id at
        /// +0xb8) and from the GuildLogEntries data source. Consumed exactly.
        /// </summary>
        public static byte[] BuildRemoveLog(ulong guildId, IReadOnlyCollection<uint> rowIds) => Build(RemoveLogSub, w =>
        {
            w.Write((uint)rowIds.Count);
            foreach (uint row in rowIds)
                w.Write(row);
            w.Write(guildId);
        });

        private static byte[] Build(ushort sub, Action<GamePacketWriter> write)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(sub);
            write(w);
            return output.ToArray();
        }
    }
}
