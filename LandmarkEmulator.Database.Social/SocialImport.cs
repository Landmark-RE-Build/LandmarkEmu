using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.Database.Social
{
    /// <summary>
    /// The one-time copy of today's social data from the shared character database into `lmemu_social`
    /// (social-service.md §5.7 step 3), for the areas the service owns so far: the character directory, friends and
    /// ignore lists. Guilds and mail are copied by their own step when those areas move.
    /// <para>
    /// When it runs: at the service's start, when SocialServer.json / env <c>ImportFrom</c> (compose:
    /// <c>SOCIAL_IMPORT_FROM</c>) names the source database (normally <c>lmemu_character</c>, on the same MariaDB server
    /// and readable by the same user) and the three tables are all empty. It copies with INSERT ... SELECT across the
    /// two databases inside one transaction, then logs the row counts of source and copy side by side (§5.7 step 4).
    /// Back up both databases first (§5.7 step 1: mariadb-dump --single-transaction). Once the tables hold rows it
    /// never runs again; to repeat it, empty the three tables. The source tables are only read.
    /// </para>
    /// Mapping: character (id, accountId, name, originalName, deleteTime, lastServerId, zoneId, lastOnline) →
    /// social_character (guid, account_id, name, original_name, deleted_at, last_world, last_zone, last_online);
    /// character_friend (characterId, friendId) → friend (character_guid, friend_guid, added_at = the copy's time);
    /// character_ignore (characterId, ignoredId, name) → ignore_entry (character_guid, ignored_guid, name).
    /// </summary>
    public static class SocialImport
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>A database name we are willing to put into SQL (no quoting tricks).</summary>
        private static readonly Regex SafeName = new("^[A-Za-z0-9_]{1,64}$");

        /// <summary>Copies when the tables are empty; returns true when a copy was made.</summary>
        public static bool RunIfEmpty(SocialDatabase database, string sourceDatabase)
        {
            if (string.IsNullOrWhiteSpace(sourceDatabase))
                return false;
            sourceDatabase = sourceDatabase.Trim();
            if (!SafeName.IsMatch(sourceDatabase))
            {
                log.Error($"Social import: \"{sourceDatabase}\" is not a database name; nothing copied.");
                return false;
            }

            using SocialContext context = database.NewContext();
            if (context.Character.Any() || context.Friend.Any() || context.IgnoreEntry.Any())
            {
                log.Info("Social import: the social tables already hold data; the one-time copy is not repeated.");
                return false;
            }

            bool HasTable(string table) => context.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}",
                sourceDatabase, table).AsEnumerable().First() > 0;

            if (!HasTable("character"))
            {
                log.Warn($"Social import: {sourceDatabase}.character not found; nothing copied.");
                return false;
            }
            bool friends = HasTable("character_friend"), ignores = HasTable("character_ignore");

            string src = $"`{sourceDatabase}`";
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
            strategy.Execute(() =>
            {
                using var transaction = context.Database.BeginTransaction();
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO social_character (guid, account_id, name, original_name, deleted_at, last_world, last_zone, last_online) " +
                    $"SELECT id, accountId, COALESCE(name, ''), originalName, deleteTime, lastServerId, zoneId, lastOnline FROM {src}.`character`");
                if (friends)
                    context.Database.ExecuteSqlRaw(
                        "INSERT IGNORE INTO friend (character_guid, friend_guid, added_at) " +
                        $"SELECT characterId, friendId, UTC_TIMESTAMP() FROM {src}.`character_friend`");
                if (ignores)
                    context.Database.ExecuteSqlRaw(
                        "INSERT IGNORE INTO ignore_entry (character_guid, ignored_guid, name) " +
                        $"SELECT characterId, ignoredId, COALESCE(name, '') FROM {src}.`character_ignore`");
                transaction.Commit();
            });

            // Table names are ours or the source name checked against SafeName above.
            long Count(string table)
            {
                string sql = "SELECT COUNT(*) AS Value FROM " + table;
                return context.Database.SqlQueryRaw<long>(sql).AsEnumerable().First();
            }
            log.Info($"Social import from {sourceDatabase}: characters {Count($"{src}.`character`")} → {Count("social_character")}, "
                + $"friends {(friends ? Count($"{src}.`character_friend`") : 0)} → {Count("friend")}, "
                + $"ignores {(ignores ? Count($"{src}.`character_ignore`") : 0)} → {Count("ignore_entry")}.");
            return true;
        }

        /// <summary>
        /// The guild step of the one-time copy (social-service.md §5.7, guilds moved in §6 step 5): guild → social_guild
        /// (id, name, motd, description, appData → app_data, leaderId → leader_guid, createTime → created_at, ranks) and
        /// guild_member (characterId, guildId, rankId) → social_guild_member, keeping the guild ids. Runs on its own
        /// condition — both guild tables empty — so a service whose friends were imported earlier still gets the guilds
        /// once. Same source rules as <see cref="RunIfEmpty"/>.
        /// </summary>
        public static bool RunGuildsIfEmpty(SocialDatabase database, string sourceDatabase)
        {
            if (string.IsNullOrWhiteSpace(sourceDatabase))
                return false;
            sourceDatabase = sourceDatabase.Trim();
            if (!SafeName.IsMatch(sourceDatabase))
                return false;
            using SocialContext context = database.NewContext();
            if (context.Guild.Any() || context.GuildMember.Any())
                return false;
            bool HasTable(string table) => context.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}",
                sourceDatabase, table).AsEnumerable().First() > 0;
            if (!HasTable("guild") || !HasTable("guild_member"))
            {
                log.Info($"Social import: no guild tables in {sourceDatabase}; no guilds copied.");
                return false;
            }
            string src = $"`{sourceDatabase}`";
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
            strategy.Execute(() =>
            {
                using var transaction = context.Database.BeginTransaction();
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO social_guild (id, name, motd, description, app_data, leader_guid, created_at, ranks) " +
                    "SELECT id, COALESCE(name, ''), COALESCE(motd, ''), COALESCE(description, ''), COALESCE(appData, ''), leaderId, " +
                    $"createTime, ranks FROM {src}.`guild`");
                context.Database.ExecuteSqlRaw(
                    "INSERT IGNORE INTO social_guild_member (character_guid, guild_id, rank_id) " +
                    $"SELECT characterId, guildId, rankId FROM {src}.`guild_member`");
                transaction.Commit();
            });
            long Count(string table) => context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM " + table).AsEnumerable().First();
            log.Info($"Social import from {sourceDatabase}: guilds {Count($"{src}.`guild`")} → {Count("social_guild")}, "
                + $"members {Count($"{src}.`guild_member`")} → {Count("social_guild_member")}.");
            return true;
        }

        /// <summary>
        /// The mail step of the one-time copy (mail moved in §6 step 5): mail (id, recipientGuid, senderGuid, senderName,
        /// subject, body, sentAt, isRead, kind) → social_mail keeping the ids (an open reading window's id stays valid),
        /// and each mail's attachments string ("definition:count[:wear];…", count 0 = already taken, definition 0 = coins,
        /// 4294967295 = cash on delivery, wear only on a worn item) → one social_mail_attachment row per entry, slot = its
        /// place in the string.
        /// Runs when social_mail is empty; same source rules as <see cref="RunIfEmpty"/>.
        /// </summary>
        public static bool RunMailIfEmpty(SocialDatabase database, string sourceDatabase)
        {
            if (string.IsNullOrWhiteSpace(sourceDatabase))
                return false;
            sourceDatabase = sourceDatabase.Trim();
            if (!SafeName.IsMatch(sourceDatabase))
                return false;
            using SocialContext context = database.NewContext();
            if (context.Mail.Any())
                return false;
            bool HasTable(string table) => context.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*) AS Value FROM information_schema.tables WHERE table_schema = {0} AND table_name = {1}",
                sourceDatabase, table).AsEnumerable().First() > 0;
            if (!HasTable("mail"))
            {
                log.Info($"Social import: no mail table in {sourceDatabase}; no mail copied.");
                return false;
            }
            string src = $"`{sourceDatabase}`";

            // The attachment strings are parsed here, not in SQL.
            var attachments = new List<(ulong Mail, string Text)>();
            DbConnection connection = context.Database.GetDbConnection();
            context.Database.OpenConnection();
            try
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT id, attachments FROM {src}.`mail` WHERE attachments IS NOT NULL AND attachments <> ''";
                using DbDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    attachments.Add((Convert.ToUInt64(reader.GetValue(0)), reader.GetString(1)));
            }
            finally
            {
                context.Database.CloseConnection();
            }

            int rows = 0;
            IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
            strategy.Execute(() =>
            {
                rows = 0;
                using var transaction = context.Database.BeginTransaction();
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO social_mail (id, recipient_guid, sender_guid, sender_name, subject, body, sent_at, is_read, kind, guild_id, token) " +
                    "SELECT id, recipientGuid, senderGuid, COALESCE(senderName, ''), COALESCE(subject, ''), COALESCE(body, ''), sentAt, " +
                    $"isRead, COALESCE(kind, 0), 0, 0 FROM {src}.`mail`");
                foreach ((ulong mail, string text) in attachments)
                {
                    uint slot = 0;
                    foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] f = part.Split(':');
                        if (f.Length is not (2 or 3) || !uint.TryParse(f[0], out uint definition) || !uint.TryParse(f[1], out uint count))
                            continue;
                        float wear = f.Length == 3 && float.TryParse(f[2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float w) ? w : 0f;
                        byte type = definition == 0 ? (byte)1 : definition == uint.MaxValue ? (byte)2 : (byte)0;
                        context.Database.ExecuteSqlRaw(
                            "INSERT INTO social_mail_attachment (mail_id, slot, type, item_definition, count, taken, wear) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                            mail, slot, type, type == 0 ? definition : 0u, count, count == 0, type == 0 ? wear : 0f);
                        slot++;
                        rows++;
                    }
                }
                transaction.Commit();
            });
            long Count(string table) => context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM " + table).AsEnumerable().First();
            log.Info($"Social import from {sourceDatabase}: mail {Count($"{src}.`mail`")} → {Count("social_mail")}, "
                + $"attachment slots {rows} → {Count("social_mail_attachment")}.");
            return true;
        }
    }
}
