using MySqlConnector;
using NLog;
using System;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// The world channel's own store (trace/architecture/world-channel-service.md §6.1): schema `lmemu_wc_&lt;world&gt;` on
    /// the same MariaDB server, table <c>claim_vote</c> (voter char, claim, at) — the build-site likes, which until build
    /// step 6 were character_unlock rows of kind 11 (ClaimVote) in the zone's character database.
    /// <para>
    /// Deviation, recorded in the spec: no EF Core context / migrations project (as LandmarkEmulator.Database.Social has)
    /// for one table; the schema and the table are created here with <c>CREATE ... IF NOT EXISTS</c> at start, and the
    /// queries are plain MySqlConnector commands (the driver Pomelo uses, already referenced through
    /// LandmarkEmulator.Shared). The database user needs the grant once (docker/db-init: <c>lmemu\_wc\_%</c>).
    /// </para>
    /// <para>
    /// Import (<see cref="Import"/>), like the social service's SOCIAL_IMPORT_FROM: with WorldChannel.json / env
    /// <c>ImportFrom</c> naming the zone's character database (normally <c>lmemu_character</c>), the first start with an
    /// empty table copies every kind-11 row; every later start copies the kind-11 rows the table doesn't have yet — the
    /// likes the zone counted itself while this service was away (spec §7 fallback). The zone keeps writing kind 11 for
    /// the votes counted here too (RatingVoteCounted), so both sides hold the same set; the source is only read.
    /// </para>
    /// </summary>
    public sealed class VoteStore
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>A database name we are willing to put into SQL (no quoting tricks).</summary>
        private static readonly Regex SafeName = new("^[A-Za-z0-9_]{1,64}$");

        /// <summary>character_unlock kind of a claim vote (the zone's Unlocks.ClaimVote).</summary>
        private const uint ClaimVoteKind = 11;

        private readonly string connectionString;

        private VoteStore(string connectionString)
        {
            this.connectionString = connectionString;
        }

        /// <summary>
        /// Opens the store: creates the schema named in the connection string and the table when missing. Null when the
        /// connection string is empty (the votes then stay in the zone) or the database can't be reached / created.
        /// </summary>
        public static VoteStore Open(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return null;
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                string schema = builder.Database;
                if (!SafeName.IsMatch(schema ?? ""))
                {
                    log.Error($"World-channel database: \"{schema}\" is not a database name; claim votes stay in the zone.");
                    return null;
                }
                builder.Database = "";
                try
                {
                    using var server = new MySqlConnection(builder.ConnectionString);
                    server.Open();
                    Execute(server, $"CREATE DATABASE IF NOT EXISTS `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
                }
                catch (MySqlException e)
                {
                    // A user without CREATE on the server can still use a schema made by an administrator.
                    log.Debug($"CREATE DATABASE {schema}: {e.Message} (using it if it exists).");
                }
                var store = new VoteStore(connectionString);
                using MySqlConnection connection = store.Connect();
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS claim_vote (" +
                    "voter_guid BIGINT(20) UNSIGNED NOT NULL, " +
                    "claim_id BIGINT(20) UNSIGNED NOT NULL, " +
                    "voted_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (voter_guid, claim_id), " +
                    "KEY claim (claim_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
                log.Info($"World-channel database {schema}: claim_vote ready ({store.CountAll()} vote(s)).");
                return store;
            }
            catch (Exception e)
            {
                log.Error($"World-channel database unavailable ({e.Message}); claim votes stay in the zone.");
                return null;
            }
        }

        private MySqlConnection Connect()
        {
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static int Execute(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = new MySqlCommand(sql, connection);
            foreach ((string name, object value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return command.ExecuteNonQuery();
        }

        private static ulong Scalar(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = new MySqlCommand(sql, connection);
            foreach ((string name, object value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return Convert.ToUInt64(command.ExecuteScalar() ?? 0);
        }

        /// <summary>Records a like; false when this voter had liked the site before (one vote per player per site).</summary>
        public bool Add(ulong voter, ulong claim)
        {
            using MySqlConnection connection = Connect();
            return Execute(connection, "INSERT IGNORE INTO claim_vote (voter_guid, claim_id, voted_at) VALUES (@voter, @claim, UTC_TIMESTAMP())",
                ("@voter", voter), ("@claim", claim)) > 0;
        }

        /// <summary>Whether this voter liked the site.</summary>
        public bool Has(ulong voter, ulong claim)
        {
            using MySqlConnection connection = Connect();
            return Scalar(connection, "SELECT COUNT(*) FROM claim_vote WHERE voter_guid = @voter AND claim_id = @claim",
                ("@voter", voter), ("@claim", claim)) > 0;
        }

        /// <summary>How many players liked a build site (the VoteReply total).</summary>
        public ulong Total(ulong claim)
        {
            using MySqlConnection connection = Connect();
            return Scalar(connection, "SELECT COUNT(*) FROM claim_vote WHERE claim_id = @claim", ("@claim", claim));
        }

        private ulong CountAll()
        {
            using MySqlConnection connection = Connect();
            return Scalar(connection, "SELECT COUNT(*) FROM claim_vote");
        }

        /// <summary>
        /// Copies the kind-11 rows of <paramref name="sourceDatabase"/>.character_unlock that the table doesn't hold
        /// (characterId → voter_guid, value → claim_id, voted_at = the copy's time): all of them on the first start (the
        /// one-time import), afterwards the likes the zone counted while this service was away. Returns the rows added.
        /// </summary>
        public int Import(string sourceDatabase)
        {
            if (string.IsNullOrWhiteSpace(sourceDatabase))
                return 0;
            sourceDatabase = sourceDatabase.Trim();
            if (!SafeName.IsMatch(sourceDatabase))
            {
                log.Error($"Claim vote import: \"{sourceDatabase}\" is not a database name; nothing copied.");
                return 0;
            }
            try
            {
                using MySqlConnection connection = Connect();
                if (Scalar(connection, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = 'character_unlock'",
                    ("@schema", sourceDatabase)) == 0)
                {
                    log.Warn($"Claim vote import: {sourceDatabase}.character_unlock not found; nothing copied.");
                    return 0;
                }
                bool first = CountAll() == 0;
                // The source name was checked against SafeName above.
                int added = Execute(connection,
                    "INSERT IGNORE INTO claim_vote (voter_guid, claim_id, voted_at) " +
                    $"SELECT characterId, value, UTC_TIMESTAMP() FROM `{sourceDatabase}`.`character_unlock` WHERE kind = @kind",
                    ("@kind", ClaimVoteKind));
                ulong source = Scalar(connection, $"SELECT COUNT(*) FROM `{sourceDatabase}`.`character_unlock` WHERE kind = @kind", ("@kind", ClaimVoteKind));
                if (first)
                    log.Info($"Claim vote import from {sourceDatabase}: {source} kind-11 row(s) → {CountAll()} vote(s) (one-time copy).");
                else if (added > 0)
                    log.Info($"Claim vote import from {sourceDatabase}: {added} vote(s) the zone counted while the world channel was away.");
                return added;
            }
            catch (Exception e)
            {
                log.Error($"Claim vote import from {sourceDatabase} failed: {e.Message}");
                return 0;
            }
        }
    }
}
