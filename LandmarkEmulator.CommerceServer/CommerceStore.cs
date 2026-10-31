using LandmarkEmulator.Internal.Commerce;
using MySqlConnector;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>A Player Studio product or gallery listing (table product; the snapshot columns are read separately).</summary>
    public sealed class ProductRow
    {
        public ulong Id;
        /// <summary>The world it was made on: a gallery listing's site (ItemId) is a claim of this world.</summary>
        public uint WorldId;
        public ulong OwnerGuid;
        public ulong OwnerAccount;
        public string OwnerName = "";
        /// <summary>2 design (ItemId = template id, per realm), 3 build site (ItemId = claim id on <see cref="WorldId"/>).</summary>
        public uint ItemType;
        public ulong ItemId;
        public string Name = "", Description = "", Headline = "", Tags = "";
        public uint Price;
        /// <summary>0 waiting for peer review, 1 listed, 2 rejected, 3 removed by the owner (as PlayerStudioProductModel.Status).</summary>
        public byte Status;
        public uint Approvals, Rejections;
        public DateTime CreatedAt;
        public uint Voxels;
        /// <summary>The result mail (accepted / rejected) is still to be sent to the owner (<see cref="CommerceResultMail"/>).</summary>
        public bool MailPending;
        /// <summary>
        /// When it was listed: peer review (or an admin) accepted the design, or the site was published to the gallery
        /// (column listed_at, added 2026-10-18 for the following digest's "newly listed"; null for products not listed and
        /// for rows listed before, which the digest dates by <see cref="CreatedAt"/>).
        /// </summary>
        public DateTime? ListedAt;
    }

    /// <summary>
    /// A paid Player Studio sale (table studio_payout): who bought which product, the creator, the Lumens paid and the
    /// creator's share still to be mailed.
    /// </summary>
    public sealed class PayoutRow
    {
        public ulong Id;
        public ulong Product;
        public uint TakerWorld;
        public ulong TakerGuid;
        public string Buyer = "";
        public uint CreatorWorld;
        public ulong CreatorGuid;
        public string DesignName = "";
        public uint Paid, Share;
    }

    /// <summary>A contest (table contest; was Contests/contests.json).</summary>
    public sealed class ContestRow
    {
        public uint Id;
        public string Key = "", Name = "";
        /// <summary>4 = closed by the operators; otherwise the phase follows the dates (ContestService.Phase).</summary>
        public uint State = 1;
        public long SubmissionsStart, VotingStart, JudgingStart;
    }

    /// <summary>A contest entry (table contest_entry; was Contests/entries.json), keyed (contest, world, site).</summary>
    public sealed class EntryRow
    {
        public uint ContestId;
        public uint WorldId;
        public ulong ClaimId;
        public ulong OwnerGuid;
        public string OwnerName = "", SiteName = "", Description = "";
        public uint Screenshots;
        public DateTime SubmittedAt;
    }

    /// <summary>
    /// The commerce service's own store (trace/architecture/world-channel-service.md §6.1): schema `lmemu_commerce` on the
    /// same MariaDB server. Tables (all created here, <c>CREATE ... IF NOT EXISTS</c>):
    /// <list type="bullet">
    /// <item><c>commerce_world</c> (world, realm, name text, last seen): the worlds that linked and the character database
    /// each uses (CommerceHello), so a player's own rows are found on every world sharing that database.</item>
    /// <item><c>product</c>: Player Studio products and gallery listings, with the world and character guid of the owner
    /// (spec: "owner world + char guid + account"), the owner's name at listing time, the design's <b>snapshot</b> (name,
    /// tags, author, size, materials, blocks) so another world's players can review and take it, and mail_pending for
    /// the result mail. The id is the client's BundleId: it must fit a u32 and never be 0 (Commerce.WriteProduct; the
    /// client sends ProductRemove only for a product with one; Unlocks kind 16 stores it as a u32) — AUTO_INCREMENT from
    /// 1, imported rows keep their ids.</item>
    /// <item><c>product_review</c> (product, reviewer world + guid, verdict, at): one per player and product.</item>
    /// <item><c>taken_design</c> (product, taker world + guid, copy, at): designs taken from the Showcase (the zone still
    /// records Unlocks kind 16 for its own check, spec §6.1).</item>
    /// <item><c>contest</c>, <c>contest_entry</c> (contest, world, site, owner …), <c>contest_vote</c> (entry, voter world +
    /// guid): contests (were Contests/contests.json and entries.json of each world); screenshots are files in the data
    /// folder (Contests/shots).</item>
    /// <item><c>portrait</c> (world, character, kind, file, bytes, at): portraits and headshots; the images are files in
    /// the data folder (Portraits).</item>
    /// <item><c>commerce_import</c> (what, at, note): the one-time imports done.</item>
    /// <item>Showcase following (added 2026-10-18, OPEN-LEADS L137; FollowService): <c>following_tag</c> (follower world +
    /// guid + name, tag group, at) next to <c>following</c> (creators; its follower_name column added the same day),
    /// <c>digest_run</c> (realm, slot, at, mails: one row per digest run; the slot "day:yyyy-MM-dd" is unique per realm, so
    /// a day's mail goes out once even across restarts) and <c>digest_mail</c> (id, realm, follower world + guid + name,
    /// subject, body, pending, at: each follower's mail, owed until a world of their realm confirms it with
    /// CommerceDigestMailSent, as studio_payout). The product table's <c>listed_at</c> (when peer review or an admin listed
    /// a design, or a site was published) dates the digest's "newly listed".</item>
    /// <item><c>studio_payout</c> (id, product, taker world + guid, buyer name, creator world + guid, listing name, Lumens
    /// paid, creator's share, pending, at): every paid Player Studio sale and the creator's share, owed (pending = 1) until
    /// a world of the creator's realm confirms it mailed it (CommercePayoutSent); added 2026-10-17 (L148).</item>
    /// </list>
    /// <para>
    /// <c>following</c> (follower world + guid, creator id, creator name, follower name, at): the spec's table for
    /// FollowCreator 0x35 / UnFollowCreator 0x36, answered with AddFollowing 0x3A / RemoveFollowing 0x3B (0.695 numbers) and
    /// listed in AccountStore (ProductService.Follow; layouts traced 2026-10-17, lead L123); since 2026-10-18 also the
    /// Showcase page's creator follows, with <c>following_tag</c> for its tag groups (FollowService).
    /// </para>
    /// <para>
    /// Like the world channel's VoteStore (recorded deviation): no EF Core context / migrations project; plain
    /// MySqlConnector commands (the driver Pomelo uses, referenced through LandmarkEmulator.Shared). Every call runs on the
    /// service's one work loop (CommerceHub), so no locking is needed here. The database user needs the grant once
    /// (docker/db-init: lmemu_commerce).
    /// </para>
    /// </summary>
    public sealed class CommerceStore
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>A database name we are willing to put into SQL (no quoting tricks).</summary>
        private static readonly Regex SafeName = new("^[A-Za-z0-9_]{1,64}$");

        /// <summary>character_unlock kind of a taken Player Studio design (the zone's Unlocks.StudioDesign).</summary>
        private const uint StudioDesignKind = 16;

        public const byte Pending = 0, Listed = 1, Rejected = 2, Removed = 3;
        public const uint ItemDesign = 2, ItemClaim = 3;

        private readonly string connectionString;

        private CommerceStore(string connectionString)
        {
            this.connectionString = connectionString;
        }

        /// <summary>
        /// Opens the store: creates the schema named in the connection string and the tables when missing. Null when the
        /// connection string is empty or not a usable schema name, or the database can't be reached.
        /// </summary>
        public static CommerceStore Open(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return null;
            try
            {
                var builder = new MySqlConnectionStringBuilder(connectionString);
                string schema = builder.Database;
                if (!SafeName.IsMatch(schema ?? ""))
                {
                    log.Error($"Commerce database: \"{schema}\" is not a database name.");
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
                var store = new CommerceStore(connectionString);
                using MySqlConnection connection = store.Connect();
                const string Table = " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS commerce_world (" +
                    "world_id INT UNSIGNED NOT NULL PRIMARY KEY, realm VARCHAR(64) NOT NULL, name_text INT UNSIGNED NOT NULL DEFAULT 0, " +
                    "seen_at DATETIME NOT NULL)" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS product (" +
                    "id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, world_id INT UNSIGNED NOT NULL, " +
                    "owner_guid BIGINT UNSIGNED NOT NULL, owner_account BIGINT UNSIGNED NOT NULL DEFAULT 0, " +
                    "owner_name VARCHAR(64) NOT NULL DEFAULT '', item_type INT UNSIGNED NOT NULL, item_id BIGINT UNSIGNED NOT NULL, " +
                    "name VARCHAR(128) NOT NULL DEFAULT '', description VARCHAR(1024) NOT NULL DEFAULT '', " +
                    "headline VARCHAR(256) NOT NULL DEFAULT '', tags VARCHAR(512) NOT NULL DEFAULT '', price INT UNSIGNED NOT NULL DEFAULT 0, " +
                    "status TINYINT UNSIGNED NOT NULL DEFAULT 0, approvals INT UNSIGNED NOT NULL DEFAULT 0, " +
                    "rejections INT UNSIGNED NOT NULL DEFAULT 0, created_at DATETIME NOT NULL, voxels INT UNSIGNED NOT NULL DEFAULT 0, " +
                    "mail_pending TINYINT UNSIGNED NOT NULL DEFAULT 0, " +
                    "snap_name VARCHAR(128) NULL, snap_tags VARCHAR(512) NULL, snap_author VARCHAR(64) NULL, " +
                    "snap_size_x INT UNSIGNED NULL, snap_size_y INT UNSIGNED NULL, snap_size_z INT UNSIGNED NULL, " +
                    "snap_materials TEXT NULL, snap_blocks LONGBLOB NULL, " +
                    "KEY owner (owner_guid), KEY item (item_type, item_id), KEY state (status, item_type))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS product_review (" +
                    "product_id BIGINT UNSIGNED NOT NULL, reviewer_world INT UNSIGNED NOT NULL, reviewer_guid BIGINT UNSIGNED NOT NULL, " +
                    "verdict INT UNSIGNED NOT NULL, reviewed_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (product_id, reviewer_world, reviewer_guid), KEY reviewer (reviewer_guid))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS taken_design (" +
                    "product_id BIGINT UNSIGNED NOT NULL, taker_world INT UNSIGNED NOT NULL, taker_guid BIGINT UNSIGNED NOT NULL, " +
                    "copy_id BIGINT UNSIGNED NOT NULL DEFAULT 0, taken_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (product_id, taker_world, taker_guid), KEY taker (taker_guid))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS contest (" +
                    "id INT UNSIGNED NOT NULL PRIMARY KEY, contest_key VARCHAR(128) NOT NULL DEFAULT '', name VARCHAR(128) NOT NULL DEFAULT '', " +
                    "state INT UNSIGNED NOT NULL DEFAULT 1, submissions_start BIGINT NOT NULL DEFAULT 0, voting_start BIGINT NOT NULL DEFAULT 0, " +
                    "judging_start BIGINT NOT NULL DEFAULT 0)" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS contest_entry (" +
                    "contest_id INT UNSIGNED NOT NULL, world_id INT UNSIGNED NOT NULL, claim_id BIGINT UNSIGNED NOT NULL, " +
                    "owner_guid BIGINT UNSIGNED NOT NULL, owner_name VARCHAR(64) NOT NULL DEFAULT '', site_name VARCHAR(128) NOT NULL DEFAULT '', " +
                    "description VARCHAR(1024) NOT NULL DEFAULT '', screenshots INT UNSIGNED NOT NULL DEFAULT 0, submitted_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (contest_id, world_id, claim_id), KEY site (world_id, claim_id))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS contest_vote (" +
                    "contest_id INT UNSIGNED NOT NULL, world_id INT UNSIGNED NOT NULL, claim_id BIGINT UNSIGNED NOT NULL, " +
                    "voter_world INT UNSIGNED NOT NULL, voter_guid BIGINT UNSIGNED NOT NULL, voted_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (contest_id, world_id, claim_id, voter_world, voter_guid), KEY site (world_id, claim_id))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS portrait (" +
                    "world_id INT UNSIGNED NOT NULL, character_guid BIGINT UNSIGNED NOT NULL, kind VARCHAR(16) NOT NULL, " +
                    "file VARCHAR(128) NOT NULL, bytes INT UNSIGNED NOT NULL DEFAULT 0, stored_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (world_id, character_guid, kind), KEY character_kind (character_guid, kind))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS following (" +
                    "follower_world INT UNSIGNED NOT NULL, follower_guid BIGINT UNSIGNED NOT NULL, creator_id BIGINT UNSIGNED NOT NULL, " +
                    "creator_name VARCHAR(64) NOT NULL DEFAULT '', followed_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (follower_world, follower_guid, creator_id), KEY follower (follower_guid))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS studio_payout (" +
                    "id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, product_id BIGINT UNSIGNED NOT NULL, " +
                    "taker_world INT UNSIGNED NOT NULL, taker_guid BIGINT UNSIGNED NOT NULL, buyer_name VARCHAR(64) NOT NULL DEFAULT '', " +
                    "creator_world INT UNSIGNED NOT NULL, creator_guid BIGINT UNSIGNED NOT NULL, design_name VARCHAR(128) NOT NULL DEFAULT '', " +
                    "paid INT UNSIGNED NOT NULL DEFAULT 0, share INT UNSIGNED NOT NULL DEFAULT 0, " +
                    "pending TINYINT UNSIGNED NOT NULL DEFAULT 1, created_at DATETIME NOT NULL, " +
                    "KEY creator (creator_world, creator_guid), KEY owed (pending))" + Table);
                // Showcase following (2026-10-18). A column added after its table was first made follows the tables' rule:
                // made at start when missing (EnsureColumn), never dropped, so starting the service brings an older
                // database up to date.
                EnsureColumn(connection, schema, "product", "listed_at", "DATETIME NULL");
                EnsureColumn(connection, schema, "following", "follower_name", "VARCHAR(64) NOT NULL DEFAULT ''");
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS following_tag (" +
                    "follower_world INT UNSIGNED NOT NULL, follower_guid BIGINT UNSIGNED NOT NULL, follower_name VARCHAR(64) NOT NULL DEFAULT '', " +
                    "tags VARCHAR(200) NOT NULL, followed_at DATETIME NOT NULL, " +
                    "PRIMARY KEY (follower_world, follower_guid, tags), KEY follower (follower_guid))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS digest_run (" +
                    "realm VARCHAR(64) NOT NULL, slot VARCHAR(64) NOT NULL, ran_at DATETIME NOT NULL, mails INT UNSIGNED NOT NULL DEFAULT 0, " +
                    "PRIMARY KEY (realm, slot))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS digest_mail (" +
                    "id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, realm VARCHAR(64) NOT NULL, follower_world INT UNSIGNED NOT NULL, " +
                    "follower_guid BIGINT UNSIGNED NOT NULL, follower_name VARCHAR(64) NOT NULL DEFAULT '', subject VARCHAR(128) NOT NULL DEFAULT '', " +
                    "body TEXT NOT NULL, pending TINYINT UNSIGNED NOT NULL DEFAULT 1, created_at DATETIME NOT NULL, " +
                    "KEY owed (pending, realm))" + Table);
                Execute(connection,
                    "CREATE TABLE IF NOT EXISTS commerce_import (" +
                    "what VARCHAR(64) NOT NULL PRIMARY KEY, done_at DATETIME NOT NULL, note VARCHAR(255) NOT NULL DEFAULT '')" + Table);
                log.Info($"Commerce database {schema} ready ({store.Count("product")} product(s), {store.Count("contest")} contest(s), " +
                    $"{store.Count("contest_entry")} contest entr(ies), {store.Count("portrait")} portrait file(s)).");
                return store;
            }
            catch (Exception e)
            {
                log.Error($"Commerce database unavailable: {e.Message}");
                return null;
            }
        }

        // ---- SQL helpers ----------------------------------------------------------------------------------------------

        private MySqlConnection Connect()
        {
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static MySqlCommand Command(MySqlConnection connection, string sql, (string Name, object Value)[] parameters)
        {
            var command = new MySqlCommand(sql, connection);
            foreach ((string name, object value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return command;
        }

        /// <summary>
        /// Adds a column to an existing table when it is missing (an information_schema check, so it works on MySQL and
        /// MariaDB alike): the store's migration for a column added after its table was first made.
        /// </summary>
        private static void EnsureColumn(MySqlConnection connection, string schema, string table, string column, string definition)
        {
            using (MySqlCommand check = Command(connection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = @s " +
                "AND table_name = @t AND column_name = @c", new (string, object)[] { ("@s", schema), ("@t", table), ("@c", column) }))
                if (Convert.ToInt64(check.ExecuteScalar()) > 0)
                    return;
            // Table, column and definition are this class's constants, never input.
            Execute(connection, $"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}");
            log.Info($"Commerce database: column {table}.{column} added.");
        }

        private static int Execute(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using MySqlCommand command = Command(connection, sql, parameters);
            return command.ExecuteNonQuery();
        }

        private int Execute(string sql, params (string Name, object Value)[] parameters)
        {
            using MySqlConnection connection = Connect();
            return Execute(connection, sql, parameters);
        }

        private ulong Scalar(string sql, params (string Name, object Value)[] parameters)
        {
            using MySqlConnection connection = Connect();
            using MySqlCommand command = Command(connection, sql, parameters);
            object value = command.ExecuteScalar();
            return value == null || value is DBNull ? 0 : Convert.ToUInt64(value);
        }

        private List<T> Query<T>(string sql, Func<MySqlDataReader, T> row, params (string Name, object Value)[] parameters)
        {
            using MySqlConnection connection = Connect();
            using MySqlCommand command = Command(connection, sql, parameters);
            using MySqlDataReader reader = command.ExecuteReader();
            var list = new List<T>();
            while (reader.Read())
                list.Add(row(reader));
            return list;
        }

        private ulong Count(string table) => Scalar($"SELECT COUNT(*) FROM `{table}`");

        private static string Str(MySqlDataReader r, string column)
        {
            int i = r.GetOrdinal(column);
            return r.IsDBNull(i) ? "" : r.GetString(i);
        }

        private static DateTime Utc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);

        // ---- worlds ---------------------------------------------------------------------------------------------------

        /// <summary>A world linked: its realm and name text, and when (CommerceHello).</summary>
        public void UpsertWorld(uint world, string realm, uint nameText)
            => Execute("INSERT INTO commerce_world (world_id, realm, name_text, seen_at) VALUES (@w, @r, @n, @at) " +
                "ON DUPLICATE KEY UPDATE realm = @r, name_text = @n, seen_at = @at",
                ("@w", world), ("@r", realm), ("@n", nameText), ("@at", DateTime.UtcNow));

        /// <summary>Every world's realm, as last named.</summary>
        public Dictionary<uint, string> Realms()
            => Query("SELECT world_id, realm FROM commerce_world", r => (r.GetUInt32(0), r.GetString(1))).ToDictionary(x => x.Item1, x => x.Item2);

        // ---- products -------------------------------------------------------------------------------------------------

        private const string ProductColumns = "p.id, p.world_id, p.owner_guid, p.owner_account, p.owner_name, p.item_type, p.item_id, p.name, " +
            "p.description, p.headline, p.tags, p.price, p.status, p.approvals, p.rejections, p.created_at, p.voxels, p.mail_pending, p.listed_at";

        private static ProductRow ReadProduct(MySqlDataReader r) => new()
        {
            Id = r.GetUInt64(0), WorldId = r.GetUInt32(1), OwnerGuid = r.GetUInt64(2), OwnerAccount = r.GetUInt64(3), OwnerName = Str(r, "owner_name"),
            ItemType = r.GetUInt32(5), ItemId = r.GetUInt64(6), Name = Str(r, "name"), Description = Str(r, "description"), Headline = Str(r, "headline"),
            Tags = Str(r, "tags"), Price = r.GetUInt32(11), Status = r.GetByte(12), Approvals = r.GetUInt32(13), Rejections = r.GetUInt32(14),
            CreatedAt = Utc(r.GetDateTime(15)), Voxels = r.GetUInt32(16), MailPending = r.GetByte(17) != 0,
            ListedAt = r.IsDBNull(18) ? null : Utc(r.GetDateTime(18))
        };

        public ProductRow GetProduct(ulong id)
            => Query($"SELECT {ProductColumns} FROM product p WHERE p.id = @id", ReadProduct, ("@id", id)).FirstOrDefault();

        /// <summary>
        /// A live product (waiting or listed) for the same item: a design in the same realm (template ids are per character
        /// database) or a build site on the same world (claim ids are per world). Commerce.CreateProduct's duplicate check.
        /// </summary>
        public bool HasLive(uint world, string realm, uint itemType, ulong itemId)
            => itemType == ItemClaim
                ? Scalar("SELECT COUNT(*) FROM product p WHERE p.world_id = @w AND p.item_type = @t AND p.item_id = @i AND p.status IN (0, 1)",
                    ("@w", world), ("@t", itemType), ("@i", itemId)) > 0
                : Scalar("SELECT COUNT(*) FROM product p JOIN commerce_world w ON w.world_id = p.world_id " +
                    "WHERE w.realm = @r AND p.item_type = @t AND p.item_id = @i AND p.status IN (0, 1)",
                    ("@r", realm), ("@t", itemType), ("@i", itemId)) > 0;

        /// <summary>
        /// The newest live (pending or listed) product of <paramref name="owner"/> for one item, or 0: a design matched on the
        /// realm, a build site on its world, as <see cref="HasLive"/> (ProductService.CheckCreate, review 2026-10-18 #4).
        /// </summary>
        public ulong LiveProductOf(uint world, string realm, uint itemType, ulong itemId, ulong owner)
            => itemType == ItemClaim
                ? Scalar("SELECT MAX(p.id) FROM product p WHERE p.world_id = @w AND p.item_type = @t AND p.item_id = @i AND p.owner_guid = @o " +
                    "AND p.status IN (0, 1)", ("@w", world), ("@t", itemType), ("@i", itemId), ("@o", owner))
                : Scalar("SELECT MAX(p.id) FROM product p JOIN commerce_world w ON w.world_id = p.world_id " +
                    "WHERE w.realm = @r AND p.item_type = @t AND p.item_id = @i AND p.owner_guid = @o AND p.status IN (0, 1)",
                    ("@r", realm), ("@t", itemType), ("@i", itemId), ("@o", owner));

        /// <summary>Stores a new product; returns its id.</summary>
        public ulong InsertProduct(ProductRow p, TemplateSnapshot snapshot)
        {
            using MySqlConnection connection = Connect();
            Execute(connection,
                "INSERT INTO product (world_id, owner_guid, owner_account, owner_name, item_type, item_id, name, description, headline, tags, " +
                "price, status, approvals, rejections, created_at, voxels, mail_pending, snap_name, snap_tags, snap_author, snap_size_x, " +
                "snap_size_y, snap_size_z, snap_materials, snap_blocks, listed_at) VALUES (@w, @o, @a, @on, @t, @i, @n, @d, @h, @tags, @price, @s, " +
                "0, 0, @at, @v, 0, @sn, @st, @sa, @sx, @sy, @sz, @sm, @sb, @la)",
                ("@w", p.WorldId), ("@o", p.OwnerGuid), ("@a", p.OwnerAccount), ("@on", p.OwnerName), ("@t", p.ItemType), ("@i", p.ItemId),
                ("@n", p.Name), ("@d", p.Description), ("@h", p.Headline), ("@tags", p.Tags), ("@price", p.Price), ("@s", p.Status),
                ("@at", p.CreatedAt), ("@v", p.Voxels), ("@sn", snapshot?.Name), ("@st", snapshot?.Tags), ("@sa", snapshot?.Author),
                ("@sx", snapshot?.SizeX), ("@sy", snapshot?.SizeY), ("@sz", snapshot?.SizeZ), ("@sm", snapshot?.Materials),
                ("@sb", snapshot?.Blocks), ("@la", p.ListedAt));
            using var command = new MySqlCommand("SELECT LAST_INSERT_ID()", connection);
            return Convert.ToUInt64(command.ExecuteScalar());
        }

        /// <summary>Writes back what a product's handlers change: texts, price, status, votes, mail flag, when it was listed.</summary>
        public void SaveProduct(ProductRow p)
            => Execute("UPDATE product SET name = @n, description = @d, tags = @tags, price = @price, status = @s, approvals = @ap, " +
                "rejections = @rj, mail_pending = @m, listed_at = @la WHERE id = @id",
                ("@n", p.Name), ("@d", p.Description), ("@tags", p.Tags), ("@price", p.Price), ("@s", p.Status), ("@ap", p.Approvals),
                ("@rj", p.Rejections), ("@m", p.MailPending ? 1 : 0), ("@la", p.ListedAt), ("@id", p.Id));

        /// <summary>A product's design snapshot, or null (a gallery listing, or a design imported without its template).</summary>
        public TemplateSnapshot GetSnapshot(ulong id)
            => Query("SELECT snap_name, snap_tags, snap_author, snap_size_x, snap_size_y, snap_size_z, snap_materials, snap_blocks " +
                "FROM product WHERE id = @id AND snap_blocks IS NOT NULL", r => new TemplateSnapshot(Str(r, "snap_name"), Str(r, "snap_tags"),
                    Str(r, "snap_author"), r.IsDBNull(3) ? 0 : r.GetUInt32(3), r.IsDBNull(4) ? 0 : r.GetUInt32(4), r.IsDBNull(5) ? 0 : r.GetUInt32(5),
                    Str(r, "snap_materials"), (byte[])r["snap_blocks"]), ("@id", id)).FirstOrDefault();

        /// <summary>
        /// A player's own products waiting for review or listed, oldest first (Commerce.SendAccountStore): owner = the
        /// guid on any world of the caller's realm.
        /// </summary>
        public List<ProductRow> OwnLive(string realm, ulong owner)
            => Query($"SELECT {ProductColumns} FROM product p JOIN commerce_world w ON w.world_id = p.world_id " +
                "WHERE p.owner_guid = @o AND w.realm = @r AND p.status IN (0, 1) ORDER BY p.id", ReadProduct, ("@o", owner), ("@r", realm));

        /// <summary>
        /// The oldest product waiting for review that this player didn't make and hasn't voted on, and how many such
        /// products there are (Commerce.SendProductForReview).
        /// </summary>
        public (ProductRow Product, int Waiting) NextForReview(string realm, ulong reviewer, uint staffAt = uint.MaxValue)
        {
            // staffAt: rejections that put a waiting product in the staff queue (ProductService.StaffQueue), never offered.
            const string Open = "FROM product p JOIN commerce_world w ON w.world_id = p.world_id WHERE p.status = 0 AND p.rejections < @s " +
                "AND NOT (p.owner_guid = @g AND w.realm = @r) AND NOT EXISTS (SELECT 1 FROM product_review v " +
                "JOIN commerce_world vw ON vw.world_id = v.reviewer_world WHERE v.product_id = p.id AND v.reviewer_guid = @g AND vw.realm = @r)";
            int waiting = (int)Scalar("SELECT COUNT(*) " + Open, ("@g", reviewer), ("@r", realm), ("@s", staffAt));
            ProductRow product = waiting == 0 ? null
                : Query($"SELECT {ProductColumns} " + Open + " ORDER BY p.created_at, p.id LIMIT 1", ReadProduct, ("@g", reviewer), ("@r", realm),
                    ("@s", staffAt)).FirstOrDefault();
            return (product, waiting);
        }

        /// <summary>Reviews this player (guid on any world of the realm) submitted since a time (the per-day rule).</summary>
        public int ReviewsSince(string realm, ulong reviewer, DateTime since)
            => (int)Scalar("SELECT COUNT(*) FROM product_review v JOIN commerce_world w ON w.world_id = v.reviewer_world " +
                "WHERE v.reviewer_guid = @g AND w.realm = @r AND v.reviewed_at >= @since", ("@g", reviewer), ("@r", realm), ("@since", since));

        /// <summary>Waiting products with at least this many rejections (the staff queue), oldest first, every realm.</summary>
        public List<ProductRow> StaffQueue(uint staffAt)
            => Query($"SELECT {ProductColumns} FROM product p WHERE p.status = 0 AND p.rejections >= @s ORDER BY p.created_at, p.id",
                ReadProduct, ("@s", staffAt));

        /// <summary>
        /// The rejecting votes' verdict codes (0, 2-7: Internal.Commerce.RejectReasons) of every product in the staff queue
        /// (<see cref="StaffQueue"/>), per product.
        /// </summary>
        public ILookup<ulong, uint> StaffQueueVerdicts(uint staffAt)
            => Query("SELECT v.product_id, v.verdict FROM product_review v JOIN product p ON p.id = v.product_id " +
                    "WHERE p.status = 0 AND p.rejections >= @s AND v.verdict <> 1", r => (Product: r.GetUInt64(0), Verdict: r.GetUInt32(1)),
                    ("@s", staffAt))
                .ToLookup(v => v.Product, v => v.Verdict);

        /// <summary>Whether this player (guid on any world of the realm) reviewed the product.</summary>
        /// <summary>
        /// This reviewer's (any world of the realm) rejections that staff overruled: products they voted down that are
        /// listed with fewer approvals than the peers' threshold (StudioRules.Overruled / MayStillReview, OPEN-LEADS L185).
        /// </summary>
        public int OverruledRejections(string realm, ulong reviewer, uint approvalsNeeded)
            => (int)Scalar("SELECT COUNT(*) FROM product_review v JOIN commerce_world w ON w.world_id = v.reviewer_world " +
                "JOIN product p ON p.id = v.product_id WHERE v.reviewer_guid = @g AND w.realm = @r AND v.verdict <> @ok " +
                "AND p.status = @listed AND p.approvals < @a", ("@g", reviewer), ("@r", realm),
                ("@ok", Internal.Commerce.StudioRules.VerdictApprove), ("@listed", Listed), ("@a", Math.Max(1u, approvalsNeeded)));

        public bool HasReviewed(ulong product, string realm, ulong reviewer)
            => Scalar("SELECT COUNT(*) FROM product_review v JOIN commerce_world w ON w.world_id = v.reviewer_world " +
                "WHERE v.product_id = @p AND v.reviewer_guid = @g AND w.realm = @r", ("@p", product), ("@g", reviewer), ("@r", realm)) > 0;

        public void AddReview(ulong product, uint world, ulong reviewer, uint verdict)
            => Execute("INSERT IGNORE INTO product_review (product_id, reviewer_world, reviewer_guid, verdict, reviewed_at) VALUES (@p, @w, @g, @v, @at)",
                ("@p", product), ("@w", world), ("@g", reviewer), ("@v", verdict), ("@at", DateTime.UtcNow));

        /// <summary>The realm's designs on the market (waiting or listed): TemplateBasicData IsOnMarketplace on its worlds.</summary>
        public List<ulong> MarketDesigns(string realm)
            => Query("SELECT DISTINCT p.item_id FROM product p JOIN commerce_world w ON w.world_id = p.world_id " +
                "WHERE w.realm = @r AND p.item_type = 2 AND p.status IN (0, 1)", r => r.GetUInt64(0), ("@r", realm));

        /// <summary>A world's listed build sites: site, listed at, tags (Commerce.Listings).</summary>
        public List<ProductRow> Listings(uint world)
            => Query($"SELECT {ProductColumns} FROM product p WHERE p.world_id = @w AND p.item_type = 3 AND p.status = 1", ReadProduct, ("@w", world));

        /// <summary>The live gallery listing of one site, or null.</summary>
        public ProductRow Listing(uint world, ulong claim)
            => Query($"SELECT {ProductColumns} FROM product p WHERE p.world_id = @w AND p.item_type = 3 AND p.item_id = @c AND p.status = 1 " +
                "ORDER BY p.id DESC LIMIT 1", ReadProduct, ("@w", world), ("@c", claim)).FirstOrDefault();

        /// <summary>The designs peer review accepted, newest first (Commerce.ListedDesigns).</summary>
        public List<ProductRow> ListedDesigns()
            => Query($"SELECT {ProductColumns} FROM product p WHERE p.status = 1 AND p.item_type = 2 ORDER BY p.created_at DESC, p.id DESC", ReadProduct);

        /// <summary>Whether this player (guid on any world of the realm) took the product before.</summary>
        public bool HasTaken(ulong product, string realm, ulong taker)
            => Scalar("SELECT COUNT(*) FROM taken_design t JOIN commerce_world w ON w.world_id = t.taker_world " +
                "WHERE t.product_id = @p AND t.taker_guid = @g AND w.realm = @r", ("@p", product), ("@g", taker), ("@r", realm)) > 0;

        public void AddTaken(ulong product, uint world, ulong taker, ulong copy)
            => Execute("INSERT IGNORE INTO taken_design (product_id, taker_world, taker_guid, copy_id, taken_at) VALUES (@p, @w, @g, @c, @at)",
                ("@p", product), ("@w", world), ("@g", taker), ("@c", copy), ("@at", DateTime.UtcNow));

        /// <summary>Products whose result mail is owed to an owner of this realm.</summary>
        public List<ProductRow> PendingMail(string realm)
            => Query($"SELECT {ProductColumns} FROM product p JOIN commerce_world w ON w.world_id = p.world_id " +
                "WHERE p.mail_pending = 1 AND w.realm = @r", ReadProduct, ("@r", realm));

        public void MailSent(ulong product) => Execute("UPDATE product SET mail_pending = 0 WHERE id = @id", ("@id", product));

        // ---- Player Studio sales --------------------------------------------------------------------------------------

        /// <summary>A paid sale and the creator's share (still to be mailed); returns the payout id.</summary>
        public ulong AddPayout(PayoutRow p)
        {
            using MySqlConnection connection = Connect();
            using MySqlCommand command = Command(connection,
                "INSERT INTO studio_payout (product_id, taker_world, taker_guid, buyer_name, creator_world, creator_guid, design_name, " +
                "paid, share, pending, created_at) VALUES (@p, @tw, @tg, @b, @cw, @cg, @n, @paid, @share, 1, @at)",
                new (string, object)[] { ("@p", p.Product), ("@tw", p.TakerWorld), ("@tg", p.TakerGuid), ("@b", p.Buyer), ("@cw", p.CreatorWorld),
                    ("@cg", p.CreatorGuid), ("@n", p.DesignName), ("@paid", p.Paid), ("@share", p.Share), ("@at", DateTime.UtcNow) });
            command.ExecuteNonQuery();
            return (ulong)command.LastInsertedId;
        }

        /// <summary>The shares still owed to creators of this realm.</summary>
        public List<PayoutRow> PendingPayouts(string realm)
            => Query("SELECT s.id, s.product_id, s.taker_world, s.taker_guid, s.buyer_name, s.creator_world, s.creator_guid, s.design_name, " +
                "s.paid, s.share FROM studio_payout s JOIN commerce_world w ON w.world_id = s.creator_world WHERE s.pending = 1 AND w.realm = @r " +
                "ORDER BY s.id", r => new PayoutRow
                {
                    Id = r.GetUInt64(0), Product = r.GetUInt64(1), TakerWorld = r.GetUInt32(2), TakerGuid = r.GetUInt64(3), Buyer = Str(r, "buyer_name"),
                    CreatorWorld = r.GetUInt32(5), CreatorGuid = r.GetUInt64(6), DesignName = Str(r, "design_name"), Paid = r.GetUInt32(8),
                    Share = r.GetUInt32(9)
                }, ("@r", realm));

        public void PayoutSent(ulong payout) => Execute("UPDATE studio_payout SET pending = 0 WHERE id = @id", ("@id", payout));

        // ---- following ------------------------------------------------------------------------------------------------

        /// <summary>
        /// A follower's creators (guid on any world of the realm), oldest first; a creator followed from two worlds of the
        /// realm is listed once.
        /// </summary>
        public List<(ulong Id, string Name)> Followings(string realm, ulong follower)
            => Query("SELECT f.creator_id, MAX(f.creator_name) AS creator_name FROM following f JOIN commerce_world w ON w.world_id = f.follower_world " +
                "WHERE f.follower_guid = @g AND w.realm = @r GROUP BY f.creator_id ORDER BY MIN(f.followed_at), f.creator_id",
                r => (r.GetUInt64(0), Str(r, "creator_name")), ("@g", follower), ("@r", realm));

        /// <summary>Whether the follower (guid on any world of the realm) follows this creator.</summary>
        public bool IsFollowing(string realm, ulong follower, ulong creator)
            => Scalar("SELECT COUNT(*) FROM following f JOIN commerce_world w ON w.world_id = f.follower_world " +
                "WHERE f.follower_guid = @g AND f.creator_id = @c AND w.realm = @r", ("@g", follower), ("@c", creator), ("@r", realm)) > 0;

        /// <summary>Follows a creator (or refreshes the names of an existing follow from this world).</summary>
        public void Follow(uint world, ulong follower, ulong creator, string name, string followerName = "")
            => Execute("INSERT INTO following (follower_world, follower_guid, creator_id, creator_name, follower_name, followed_at) " +
                "VALUES (@w, @g, @c, @n, @fn, @at) ON DUPLICATE KEY UPDATE creator_name = @n, follower_name = @fn",
                ("@w", world), ("@g", follower), ("@c", creator), ("@n", name ?? ""), ("@fn", followerName ?? ""), ("@at", DateTime.UtcNow));

        /// <summary>Stops following on every world of the realm; false when the creator was not followed.</summary>
        public bool Unfollow(string realm, ulong follower, ulong creator)
            => Execute("DELETE f FROM following f JOIN commerce_world w ON w.world_id = f.follower_world " +
                "WHERE f.follower_guid = @g AND f.creator_id = @c AND w.realm = @r", ("@g", follower), ("@c", creator), ("@r", realm)) > 0;

        /// <summary>A creator's name: the owner name of their newest product on the realm ("" when they have none).</summary>
        public string CreatorName(string realm, ulong creator)
            => Query("SELECT p.owner_name FROM product p JOIN commerce_world w ON w.world_id = p.world_id " +
                "WHERE p.owner_guid = @o AND w.realm = @r ORDER BY p.id DESC LIMIT 1", r => Str(r, "owner_name"), ("@o", creator), ("@r", realm))
                .FirstOrDefault() ?? "";

        /// <summary>A follower's tag groups (guid on any world of the realm), oldest first, each once.</summary>
        public List<string> TagGroups(string realm, ulong follower)
            => Query("SELECT t.tags FROM following_tag t JOIN commerce_world w ON w.world_id = t.follower_world " +
                "WHERE t.follower_guid = @g AND w.realm = @r GROUP BY t.tags ORDER BY MIN(t.followed_at), t.tags",
                r => Str(r, "tags"), ("@g", follower), ("@r", realm));

        public void FollowTags(uint world, ulong follower, string followerName, string tags)
            => Execute("INSERT INTO following_tag (follower_world, follower_guid, follower_name, tags, followed_at) VALUES (@w, @g, @fn, @t, @at) " +
                "ON DUPLICATE KEY UPDATE follower_name = @fn", ("@w", world), ("@g", follower), ("@fn", followerName ?? ""), ("@t", tags),
                ("@at", DateTime.UtcNow));

        /// <summary>Stops following a tag group on every world of the realm; false when it was not followed.</summary>
        public bool UnfollowTags(string realm, ulong follower, string tags)
            => Execute("DELETE t FROM following_tag t JOIN commerce_world w ON w.world_id = t.follower_world " +
                "WHERE t.follower_guid = @g AND t.tags = @t AND w.realm = @r", ("@g", follower), ("@t", tags), ("@r", realm)) > 0;

        // ---- the following digest -------------------------------------------------------------------------------------

        /// <summary>A follow row the digest reads: who (the world they followed from, guid, name then) and a creator or a tag group.</summary>
        public sealed record FollowRow(uint World, ulong Follower, string FollowerName, ulong Creator, string Tags);

        /// <summary>A listed product in the digest's window.</summary>
        public sealed record ListedRow(ulong Id, uint World, ulong Owner, string OwnerName, string Name, uint ItemType, string Tags, DateTime ListedAt);

        /// <summary>A digest mail as sent to a world.</summary>
        public sealed record DigestMailRow(ulong Id, string Realm, uint World, ulong Follower, string FollowerName, string Subject, string Body);

        /// <summary>
        /// digest_run.mails of a run that has not finished (since 2026-10-18, review #5): written first with this marker,
        /// replaced by the mail count once its mails are stored in digest_mail (<see cref="AddDigestRunMails"/>). Only finished
        /// runs count as done and start the next window, so a run that failed half-way is run again.
        /// </summary>
        public const uint DigestRunPending = uint.MaxValue;

        /// <summary>When the realm's last finished digest run was (scheduled or dev), or null.</summary>
        public DateTime? LastDigestRun(string realm)
            => Query("SELECT MAX(ran_at) FROM digest_run WHERE realm = @r AND mails <> @p", r => r.IsDBNull(0) ? (DateTime?)null : Utc(r.GetDateTime(0)),
                ("@r", realm), ("@p", DigestRunPending)).FirstOrDefault();

        /// <summary>The realm has finished this slot's run.</summary>
        public bool HasDigestRun(string realm, string slot)
            => Scalar("SELECT COUNT(*) FROM digest_run WHERE realm = @r AND slot = @s AND mails <> @p", ("@r", realm), ("@s", slot), ("@p", DigestRunPending)) > 0;

        /// <summary>
        /// Records a run as unfinished; false when the realm already has that slot finished. An unfinished row of the slot
        /// (a run that failed or a service that stopped mid-run; the service runs one work loop, so it is never another run
        /// still going) is taken over with the new time.
        /// </summary>
        public bool AddDigestRun(string realm, string slot, DateTime at)
            => Execute("INSERT IGNORE INTO digest_run (realm, slot, ran_at, mails) VALUES (@r, @s, @at, @p)",
                    ("@r", realm), ("@s", slot), ("@at", at), ("@p", DigestRunPending)) > 0
                || Execute("UPDATE digest_run SET ran_at = @at WHERE realm = @r AND slot = @s AND mails = @p",
                    ("@r", realm), ("@s", slot), ("@at", at), ("@p", DigestRunPending)) > 0;

        /// <summary>
        /// The products listed in (<paramref name="since"/>, <paramref name="until"/>]: status listed, dated by listed_at
        /// (created_at for rows listed before that column), oldest first. One query.
        /// </summary>
        public List<ListedRow> ListedBetween(DateTime since, DateTime until)
            => Query("SELECT p.id, p.world_id, p.owner_guid, p.owner_name, p.name, p.item_type, p.tags, COALESCE(p.listed_at, p.created_at) AS listed " +
                "FROM product p WHERE p.status = 1 AND COALESCE(p.listed_at, p.created_at) > @since AND COALESCE(p.listed_at, p.created_at) <= @until " +
                "ORDER BY listed, p.id", r => new ListedRow(r.GetUInt64(0), r.GetUInt32(1), r.GetUInt64(2), Str(r, "owner_name"), Str(r, "name"),
                    r.GetUInt32(5), Str(r, "tags"), Utc(r.GetDateTime(7))), ("@since", since), ("@until", until));

        /// <summary>
        /// The realm's follow rows that can match: the follows of <paramref name="creators"/> and every tag group. One query.
        /// </summary>
        public List<FollowRow> FollowRows(string realm, IReadOnlyCollection<ulong> creators)
        {
            // The creator ids are numbers from the product table, written as literals (no parameter per id).
            string creatorRows = creators.Count == 0 ? "" :
                "SELECT f.follower_world, f.follower_guid, f.follower_name, f.creator_id, '' AS tags, f.followed_at FROM following f " +
                "JOIN commerce_world w ON w.world_id = f.follower_world WHERE w.realm = @r AND f.creator_id IN (" +
                string.Join(",", creators.Select(c => c.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ") UNION ALL ";
            return Query(creatorRows +
                "SELECT t.follower_world, t.follower_guid, t.follower_name, 0 AS creator_id, t.tags, t.followed_at FROM following_tag t " +
                "JOIN commerce_world w ON w.world_id = t.follower_world WHERE w.realm = @r ORDER BY followed_at",
                r => new FollowRow(r.GetUInt32(0), r.GetUInt64(1), Str(r, "follower_name"), Convert.ToUInt64(r.GetValue(3)), Str(r, "tags")),
                ("@r", realm));
        }

        /// <summary>
        /// A run's mails, all or none (one transaction), and its slot marked finished with their count in the same
        /// transaction; returns the mail ids in order. FollowService.Run stored them one by one until 2026-10-18: a failure
        /// part-way left the first mails stored (and sent) with the slot unfinished, and the retry a minute later composed
        /// and stored them all again, so those followers got the day's digest twice.
        /// </summary>
        public List<ulong> AddDigestRunMails(string realm, string slot,
            IReadOnlyList<(uint World, ulong Follower, string FollowerName, string Subject, string Body)> mails)
        {
            var ids = new List<ulong>(mails.Count);
            using MySqlConnection connection = Connect();
            using MySqlTransaction transaction = connection.BeginTransaction();
            DateTime at = DateTime.UtcNow;
            foreach ((uint world, ulong follower, string followerName, string subject, string body) in mails)
            {
                using MySqlCommand command = Command(connection,
                    "INSERT INTO digest_mail (realm, follower_world, follower_guid, follower_name, subject, body, pending, created_at) " +
                    "VALUES (@r, @w, @g, @n, @s, @b, 1, @at)", new (string, object)[] { ("@r", realm), ("@w", world), ("@g", follower),
                        ("@n", followerName ?? ""), ("@s", subject), ("@b", body), ("@at", at) });
                command.Transaction = transaction;
                command.ExecuteNonQuery();
                ids.Add((ulong)command.LastInsertedId);
            }
            using (MySqlCommand done = Command(connection, "UPDATE digest_run SET mails = @m WHERE realm = @r AND slot = @s",
                new (string, object)[] { ("@m", (uint)mails.Count), ("@r", realm), ("@s", slot) }))
            {
                done.Transaction = transaction;
                done.ExecuteNonQuery();
            }
            transaction.Commit();
            return ids;
        }

        /// <summary>The digest mails still owed to followers of this realm, oldest first.</summary>
        public List<DigestMailRow> PendingDigestMails(string realm)
            => Query("SELECT id, realm, follower_world, follower_guid, follower_name, subject, body FROM digest_mail WHERE pending = 1 AND realm = @r ORDER BY id",
                r => new DigestMailRow(r.GetUInt64(0), Str(r, "realm"), r.GetUInt32(2), r.GetUInt64(3), Str(r, "follower_name"), Str(r, "subject"),
                    Str(r, "body")), ("@r", realm));

        public void DigestMailSent(ulong mail) => Execute("UPDATE digest_mail SET pending = 0 WHERE id = @id", ("@id", mail));

        // ---- contests -------------------------------------------------------------------------------------------------

        public List<ContestRow> Contests()
            => Query("SELECT id, contest_key, name, state, submissions_start, voting_start, judging_start FROM contest ORDER BY id", r => new ContestRow
            {
                Id = r.GetUInt32(0), Key = Str(r, "contest_key"), Name = Str(r, "name"), State = r.GetUInt32(3),
                SubmissionsStart = r.GetInt64(4), VotingStart = r.GetInt64(5), JudgingStart = r.GetInt64(6)
            });

        /// <summary>Writes a contest; <paramref name="replace"/> false keeps an existing contest with the same id.</summary>
        public void SaveContest(ContestRow c, bool replace = true)
            => Execute((replace ? "REPLACE" : "INSERT IGNORE") + " INTO contest (id, contest_key, name, state, submissions_start, voting_start, judging_start) " +
                "VALUES (@id, @k, @n, @s, @ss, @vs, @js)", ("@id", c.Id), ("@k", c.Key), ("@n", c.Name), ("@s", c.State),
                ("@ss", c.SubmissionsStart), ("@vs", c.VotingStart), ("@js", c.JudgingStart));

        public List<EntryRow> Entries()
            => Query("SELECT contest_id, world_id, claim_id, owner_guid, owner_name, site_name, description, screenshots, submitted_at FROM contest_entry",
                r => new EntryRow
                {
                    ContestId = r.GetUInt32(0), WorldId = r.GetUInt32(1), ClaimId = r.GetUInt64(2), OwnerGuid = r.GetUInt64(3),
                    OwnerName = Str(r, "owner_name"), SiteName = Str(r, "site_name"), Description = Str(r, "description"),
                    Screenshots = r.GetUInt32(7), SubmittedAt = Utc(r.GetDateTime(8))
                });

        /// <summary>Stores an entry, replacing the site's earlier entry in the same contest (its votes go with it).</summary>
        public void SaveEntry(EntryRow e, bool keepVotes = false)
        {
            if (!keepVotes)
                Execute("DELETE FROM contest_vote WHERE contest_id = @c AND world_id = @w AND claim_id = @s",
                    ("@c", e.ContestId), ("@w", e.WorldId), ("@s", e.ClaimId));
            Execute("REPLACE INTO contest_entry (contest_id, world_id, claim_id, owner_guid, owner_name, site_name, description, screenshots, submitted_at) " +
                "VALUES (@c, @w, @s, @o, @on, @sn, @d, @n, @at)", ("@c", e.ContestId), ("@w", e.WorldId), ("@s", e.ClaimId), ("@o", e.OwnerGuid),
                ("@on", e.OwnerName), ("@sn", e.SiteName), ("@d", e.Description), ("@n", e.Screenshots), ("@at", e.SubmittedAt));
        }

        public void RemoveEntry(uint contest, uint world, ulong claim)
        {
            Execute("DELETE FROM contest_vote WHERE contest_id = @c AND world_id = @w AND claim_id = @s", ("@c", contest), ("@w", world), ("@s", claim));
            Execute("DELETE FROM contest_entry WHERE contest_id = @c AND world_id = @w AND claim_id = @s", ("@c", contest), ("@w", world), ("@s", claim));
        }

        /// <summary>Whether this voter (guid on any world of the realm) voted for the entry.</summary>
        public bool HasVoted(uint contest, uint world, ulong claim, string realm, ulong voter)
            => Scalar("SELECT COUNT(*) FROM contest_vote v JOIN commerce_world w ON w.world_id = v.voter_world WHERE v.contest_id = @c " +
                "AND v.world_id = @w AND v.claim_id = @s AND v.voter_guid = @g AND w.realm = @r",
                ("@c", contest), ("@w", world), ("@s", claim), ("@g", voter), ("@r", realm)) > 0;

        public void AddVote(uint contest, uint world, ulong claim, uint voterWorld, ulong voter)
            => Execute("INSERT IGNORE INTO contest_vote (contest_id, world_id, claim_id, voter_world, voter_guid, voted_at) VALUES (@c, @w, @s, @vw, @g, @at)",
                ("@c", contest), ("@w", world), ("@s", claim), ("@vw", voterWorld), ("@g", voter), ("@at", DateTime.UtcNow));

        /// <summary>Contest votes per entry: (contest, world, site) → votes.</summary>
        public Dictionary<(uint Contest, uint World, ulong Claim), int> VoteCounts()
            => Query("SELECT contest_id, world_id, claim_id, COUNT(*) FROM contest_vote GROUP BY contest_id, world_id, claim_id",
                r => ((r.GetUInt32(0), r.GetUInt32(1), r.GetUInt64(2)), Convert.ToInt32(r.GetValue(3)))).ToDictionary(x => x.Item1, x => x.Item2);

        // ---- portraits ------------------------------------------------------------------------------------------------

        public void SavePortrait(uint world, ulong character, string kind, string file, int bytes)
            => Execute("REPLACE INTO portrait (world_id, character_guid, kind, file, bytes, stored_at) VALUES (@w, @c, @k, @f, @b, @at)",
                ("@w", world), ("@c", character), ("@k", kind), ("@f", file), ("@b", bytes), ("@at", DateTime.UtcNow));

        /// <summary>The newest portrait file of a character (guid on any world of the realm) and kind, or null.</summary>
        public string FindPortrait(string realm, ulong character, string kind)
            => Query("SELECT p.file FROM portrait p JOIN commerce_world w ON w.world_id = p.world_id WHERE p.character_guid = @c AND p.kind = @k " +
                "AND w.realm = @r ORDER BY p.stored_at DESC LIMIT 1", r => r.GetString(0), ("@c", character), ("@k", kind), ("@r", realm)).FirstOrDefault();

        // ---- imports --------------------------------------------------------------------------------------------------

        public bool IsImported(string what) => Scalar("SELECT COUNT(*) FROM commerce_import WHERE what = @w", ("@w", what)) > 0;

        public void MarkImported(string what, string note)
            => Execute("REPLACE INTO commerce_import (what, done_at, note) VALUES (@w, @at, @n)",
                ("@w", what), ("@at", DateTime.UtcNow), ("@n", note.Length > 255 ? note[..255] : note));

        /// <summary>
        /// The one-time copy of the in-process commerce rows (spec §8 step 8 "tables to lmemu_commerce (migration with
        /// world id)") from <paramref name="source"/> (normally lmemu_character; only read): player_studio_product → product
        /// with the same ids (BundleIds and Unlocks kind 16 values stay valid), world = <paramref name="world"/>, owner's
        /// account and name from character, a design's snapshot from template (its state now); player_studio_review →
        /// product_review; character_unlock kind 16 (taken designs) → taken_design. Runs only while product is empty and
        /// the import is not marked done; every source table is checked first. Returns a summary for the log.
        /// </summary>
        public string ImportProducts(string source, uint world, string realm)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "no source (ImportFrom empty): nothing copied";
            source = source.Trim();
            if (!SafeName.IsMatch(source))
                return $"\"{source}\" is not a database name: nothing copied";
            if (IsImported("products"))
                return "already done";
            if (Count("product") > 0)
            {
                MarkImported("products", "skipped: product table not empty");
                return "product table not empty: nothing copied";
            }
            bool Has(string table) => Scalar("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @s AND table_name = @t",
                ("@s", source), ("@t", table)) > 0;
            if (!Has("player_studio_product"))
            {
                MarkImported("products", $"{source}.player_studio_product not found");
                return $"{source}.player_studio_product not found: nothing copied";
            }
            // The importing world's realm is needed for the realm-aware queries before that world has linked.
            if (!string.IsNullOrWhiteSpace(realm))
                Execute("INSERT IGNORE INTO commerce_world (world_id, realm, name_text, seen_at) VALUES (@w, @r, 0, @at)",
                    ("@w", world), ("@r", realm.Trim()), ("@at", DateTime.UtcNow));
            using MySqlConnection connection = Connect();
            using MySqlTransaction transaction = connection.BeginTransaction();
            int Run(string sql, params (string Name, object Value)[] parameters)
            {
                using MySqlCommand command = Command(connection, sql, parameters);
                command.Transaction = transaction;
                return command.ExecuteNonQuery();
            }
            bool hasCharacter = Has("character"), hasTemplate = Has("template");
            // player_studio_product.listedAt (world migration 20261102120000_ShowcaseFollowDigest, 2026-10-18): copied when the
            // source has it, so the following digest dates the imported listings as the world did.
            bool hasListedAt = Scalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = @s " +
                "AND table_name = 'player_studio_product' AND column_name = 'listedAt'", ("@s", source)) > 0;
            // The source name was checked against SafeName above.
            string owner = hasCharacter ? "IFNULL(c.accountId, 0), COALESCE(NULLIF(c.name, ''), c.originalName, '')" : "0, ''";
            string snapshot = hasTemplate ? "t.name, t.tags, t.authorName, t.sizeX, t.sizeY, t.sizeZ, t.materials, t.blocks"
                : "NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL";
            int products = Run(
                "INSERT IGNORE INTO product (id, world_id, owner_guid, owner_account, owner_name, item_type, item_id, name, description, headline, " +
                "tags, price, status, approvals, rejections, created_at, voxels, mail_pending, snap_name, snap_tags, snap_author, snap_size_x, " +
                "snap_size_y, snap_size_z, snap_materials, snap_blocks, listed_at) " +
                $"SELECT p.id, @w, p.ownerGuid, {owner}, p.itemType, p.templateId, IFNULL(p.name, ''), IFNULL(p.description, ''), " +
                "IFNULL(p.headline, ''), IFNULL(p.tags, ''), p.price, p.status, p.approvals, p.rejections, p.createdAt, 0, 0, " +
                $"{snapshot}, {(hasListedAt ? "p.listedAt" : "NULL")} FROM `{source}`.`player_studio_product` p" +
                (hasCharacter ? $" LEFT JOIN `{source}`.`character` c ON c.id = p.ownerGuid" : "") +
                (hasTemplate ? $" LEFT JOIN `{source}`.`template` t ON t.id = p.templateId AND p.itemType = 2" : ""),
                ("@w", world));
            int reviews = Has("player_studio_review") ? Run(
                "INSERT IGNORE INTO product_review (product_id, reviewer_world, reviewer_guid, verdict, reviewed_at) " +
                $"SELECT productId, @w, reviewerGuid, verdict, createdAt FROM `{source}`.`player_studio_review`", ("@w", world)) : 0;
            int taken = Has("character_unlock") ? Run(
                "INSERT IGNORE INTO taken_design (product_id, taker_world, taker_guid, copy_id, taken_at) " +
                $"SELECT value, @w, characterId, 0, UTC_TIMESTAMP() FROM `{source}`.`character_unlock` WHERE kind = @k",
                ("@w", world), ("@k", StudioDesignKind)) : 0;
            transaction.Commit();
            // Voxel counts for the Showcase list from the imported snapshots (Templates.MaterialCount: "material:count;...").
            foreach ((ulong id, string materials) in Query("SELECT id, snap_materials FROM product WHERE item_type = 2 AND snap_materials IS NOT NULL",
                r => (r.GetUInt64(0), Str(r, "snap_materials"))))
                Execute("UPDATE product SET voxels = @v WHERE id = @id", ("@v", (uint)Math.Min(uint.MaxValue, MaterialCount(materials))), ("@id", id));
            ulong sourceProducts = Scalar($"SELECT COUNT(*) FROM `{source}`.`player_studio_product`");
            string note = $"{source} → world {world}: {sourceProducts} product row(s) → {products} copied, {reviews} review(s), {taken} taken design(s)";
            MarkImported("products", note);
            return note;
        }

        /// <summary>Solid voxels in a stored material list ("material:count;..."), as the zone's Templates.MaterialCount.</summary>
        public static long MaterialCount(string materials)
        {
            long total = 0;
            foreach (string entry in (materials ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = entry.Split(':');
                if (f.Length == 2 && ushort.TryParse(f[0], out _) && uint.TryParse(f[1], out uint n))
                    total += n;
            }
            return total;
        }
    }
}
