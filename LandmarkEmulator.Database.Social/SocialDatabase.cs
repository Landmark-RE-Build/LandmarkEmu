using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Database.Social.Model;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.Database.Social
{
    /// <summary>
    /// <see cref="ISocialStore"/> on MySQL / MariaDB (`lmemu_social`, <see cref="SocialContext"/>). One short context per
    /// call, like the other databases of the solution. Called from the social service's single work loop.
    /// </summary>
    public class SocialDatabase : ISocialStore
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly IDatabaseConfig config;

        public SocialDatabase(IDatabaseConfig config)
        {
            this.config = config;
        }

        public SocialContext NewContext() => new SocialContext(config);

        /// <summary>Creates the database when missing and applies pending migrations (at every start).</summary>
        public void Migrate()
        {
            using var context = NewContext();
            List<string> migrations = context.Database.GetPendingMigrations().ToList();
            if (migrations.Count == 0)
                return;
            log.Info($"Applying {migrations.Count} social database migration(s)...");
            foreach (string migration in migrations)
                log.Info(migration);
            context.Database.Migrate();
        }

        public SocialCharacterModel GetCharacter(ulong guid)
        {
            using var context = NewContext();
            return context.Character.AsNoTracking().SingleOrDefault(c => c.Guid == guid);
        }

        public IReadOnlyList<SocialCharacterModel> GetCharacters(IReadOnlyCollection<ulong> guids)
        {
            if (guids.Count == 0)
                return Array.Empty<SocialCharacterModel>();
            using var context = NewContext();
            return context.Character.AsNoTracking().Where(c => guids.Contains(c.Guid)).ToList();
        }

        public SocialCharacterModel FindCharacterByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            using var context = NewContext();
            // The column's collation (utf8mb4_unicode_ci) makes = case-insensitive.
            return context.Character.AsNoTracking()
                .Where(c => c.Name == name && c.DeletedAt == null)
                .OrderByDescending(c => c.Guid)
                .FirstOrDefault();
        }

        public void UpsertCharacter(ulong guid, ulong accountId, string name, bool deleted = false)
        {
            using var context = NewContext();
            SocialCharacterModel row = context.Character.SingleOrDefault(c => c.Guid == guid);
            if (row == null)
            {
                context.Character.Add(new SocialCharacterModel
                {
                    Guid = guid, AccountId = accountId, Name = name ?? "", DeletedAt = deleted ? DateTime.UtcNow : null
                });
            }
            else
            {
                if (accountId != 0)
                    row.AccountId = accountId;
                if (!string.IsNullOrEmpty(name) && row.Name != name)
                {
                    row.OriginalName ??= row.Name;
                    row.Name = name;
                }
                if (deleted && row.DeletedAt == null)
                    row.DeletedAt = DateTime.UtcNow;
            }
            context.SaveChanges();
        }

        public void RecordLastSeen(ulong guid, ulong world, uint zone, DateTime? lastOnline, uint zoneNameText = 0, uint worldNameText = 0)
        {
            using var context = NewContext();
            if (zoneNameText != 0 || worldNameText != 0)
                context.Character.Where(c => c.Guid == guid).ExecuteUpdate(s => s
                    .SetProperty(c => c.LastZoneNameText, zoneNameText).SetProperty(c => c.LastWorldNameText, worldNameText));
            if (lastOnline.HasValue)
                context.Character.Where(c => c.Guid == guid).ExecuteUpdate(s => s
                    .SetProperty(c => c.LastWorld, world).SetProperty(c => c.LastZone, zone).SetProperty(c => c.LastOnline, lastOnline));
            else
                context.Character.Where(c => c.Guid == guid).ExecuteUpdate(s => s
                    .SetProperty(c => c.LastWorld, world).SetProperty(c => c.LastZone, zone));
        }

        public IReadOnlyList<ulong> GetFriends(ulong character)
        {
            using var context = NewContext();
            return context.Friend.AsNoTracking().Where(f => f.CharacterGuid == character).Select(f => f.FriendGuid).ToList();
        }

        public IReadOnlyList<ulong> GetFriendOf(ulong character)
        {
            using var context = NewContext();
            return context.Friend.AsNoTracking().Where(f => f.FriendGuid == character).Select(f => f.CharacterGuid).ToList();
        }

        public bool AddFriend(ulong character, ulong friend)
        {
            using var context = NewContext();
            if (context.Friend.Any(f => f.CharacterGuid == character && f.FriendGuid == friend))
                return false;
            context.Friend.Add(new FriendModel { CharacterGuid = character, FriendGuid = friend, AddedAt = DateTime.UtcNow });
            context.SaveChanges();
            return true;
        }

        public bool RemoveFriend(ulong character, ulong friend)
        {
            using var context = NewContext();
            return context.Friend.Where(f => f.CharacterGuid == character && f.FriendGuid == friend).ExecuteDelete() > 0;
        }

        public IReadOnlyDictionary<ulong, string> GetIgnores(ulong character)
        {
            using var context = NewContext();
            return context.IgnoreEntry.AsNoTracking().Where(i => i.CharacterGuid == character)
                .ToDictionary(i => i.IgnoredGuid, i => i.Name);
        }

        public bool AddIgnore(ulong character, ulong ignored, string name)
        {
            using var context = NewContext();
            if (context.IgnoreEntry.Any(i => i.CharacterGuid == character && i.IgnoredGuid == ignored))
                return false;
            context.IgnoreEntry.Add(new IgnoreEntryModel { CharacterGuid = character, IgnoredGuid = ignored, Name = name ?? "" });
            context.SaveChanges();
            return true;
        }

        public bool RemoveIgnore(ulong character, ulong ignored)
        {
            using var context = NewContext();
            return context.IgnoreEntry.Where(i => i.CharacterGuid == character && i.IgnoredGuid == ignored).ExecuteDelete() > 0;
        }

        public IReadOnlyList<SocialGuildModel> GetGuilds()
        {
            using var context = NewContext();
            return context.Guild.AsNoTracking().ToList();
        }

        public IReadOnlyList<SocialGuildMemberModel> GetGuildMembers()
        {
            using var context = NewContext();
            return context.GuildMember.AsNoTracking().ToList();
        }

        public void SaveGuild(SocialGuildModel guild)
        {
            using var context = NewContext();
            SocialGuildModel row = context.Guild.SingleOrDefault(g => g.Id == guild.Id);
            if (row == null)
                context.Guild.Add(new SocialGuildModel
                {
                    Id = guild.Id, Name = guild.Name, Motd = guild.Motd ?? "", Description = guild.Description ?? "",
                    AppData = guild.AppData ?? "", LeaderGuid = guild.LeaderGuid, CreatedAt = guild.CreatedAt, Ranks = guild.Ranks,
                    Log = guild.Log
                });
            else
            {
                row.Name = guild.Name;
                row.Motd = guild.Motd ?? "";
                row.Description = guild.Description ?? "";
                row.AppData = guild.AppData ?? "";
                row.LeaderGuid = guild.LeaderGuid;
                row.Ranks = guild.Ranks;
                row.Log = guild.Log;
            }
            context.SaveChanges();
        }

        public void DeleteGuild(ulong guildId)
        {
            using var context = NewContext();
            context.GuildMember.Where(m => m.GuildId == guildId).ExecuteDelete();
            context.Guild.Where(g => g.Id == guildId).ExecuteDelete();
        }

        public void SetGuildMember(ulong character, ulong guildId, uint rankId)
        {
            using var context = NewContext();
            SocialGuildMemberModel row = context.GuildMember.SingleOrDefault(m => m.CharacterGuid == character);
            if (row == null)
                context.GuildMember.Add(new SocialGuildMemberModel { CharacterGuid = character, GuildId = guildId, RankId = rankId });
            else
            {
                row.GuildId = guildId;
                row.RankId = rankId;
            }
            context.SaveChanges();
        }

        public void RemoveGuildMember(ulong character)
        {
            using var context = NewContext();
            context.GuildMember.Where(m => m.CharacterGuid == character).ExecuteDelete();
        }

        public IReadOnlyList<SocialMailModel> GetInbox(ulong recipient, int max)
        {
            using var context = NewContext();
            return context.Mail.AsNoTracking().Include(m => m.Attachments).Where(m => m.RecipientGuid == recipient)
                .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(max).ToList();
        }

        public SocialMailModel GetMail(ulong id, ulong recipient)
        {
            using var context = NewContext();
            return context.Mail.AsNoTracking().Include(m => m.Attachments).SingleOrDefault(m => m.Id == id && m.RecipientGuid == recipient);
        }

        public int CountInbox(ulong recipient)
        {
            using var context = NewContext();
            return context.Mail.Count(m => m.RecipientGuid == recipient);
        }

        public bool HasMailToken(ulong token)
        {
            if (token == 0)
                return false;
            using var context = NewContext();
            return context.Mail.Any(m => m.Token == token);
        }

        public void AddMail(IReadOnlyCollection<SocialMailModel> mail)
        {
            using var context = NewContext();
            context.Mail.AddRange(mail);
            context.SaveChanges();
        }

        public void MarkMailRead(ulong id)
        {
            using var context = NewContext();
            context.Mail.Where(m => m.Id == id).ExecuteUpdate(s => s.SetProperty(m => m.IsRead, true));
        }

        public bool DeleteMail(ulong id, ulong recipient)
        {
            using var context = NewContext();
            // The attachments go with it (cascade).
            return context.Mail.Where(m => m.Id == id && m.RecipientGuid == recipient).ExecuteDelete() > 0;
        }

        public int DeleteExpiredMail(ulong recipient, DateTime cutoff)
        {
            using var context = NewContext();
            return context.Mail.Where(m => m.RecipientGuid == recipient && m.SentAt < cutoff && !m.Attachments.Any(a => !a.Taken))
                .ExecuteDelete();
        }

        public void SetMailAttachmentsTaken(ulong mailId, IReadOnlyCollection<uint> slots)
        {
            using var context = NewContext();
            context.MailAttachment.Where(a => a.MailId == mailId && slots.Contains(a.Slot))
                .ExecuteUpdate(s => s.SetProperty(a => a.Taken, true));
        }

        public bool HasWaitingMailItem(ulong recipient, uint itemDefinition)
        {
            using var context = NewContext();
            return context.Mail.Any(m => m.RecipientGuid == recipient
                && m.Attachments.Any(a => !a.Taken && a.Type == 0 && a.ItemDefinition == itemDefinition));
        }
    }
}
