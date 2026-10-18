using LandmarkEmulator.Database.Configuration;
using LandmarkEmulator.Database.Social.Model;
using Microsoft.EntityFrameworkCore;

namespace LandmarkEmulator.Database.Social
{
    /// <summary>
    /// The social service's own database `lmemu_social` (trace/architecture/social-service.md §5.2). Nobody else reads
    /// it (internal-transport.md §4 "each service owns its tables"); zones ask the service through its messages.
    /// Tables so far are the presence / friends / ignore / guild / mail areas of §6 step 5: social_character, friend,
    /// ignore_entry, social_guild, social_guild_member, social_mail, social_mail_attachment.
    /// Column names follow the spec (snake_case), unlike the older lmemu_character tables.
    /// </summary>
    public class SocialContext : DbContext
    {
        public DbSet<SocialCharacterModel> Character { get; set; }
        public DbSet<FriendModel> Friend { get; set; }
        public DbSet<IgnoreEntryModel> IgnoreEntry { get; set; }
        public DbSet<SocialGuildModel> Guild { get; set; }
        public DbSet<SocialGuildMemberModel> GuildMember { get; set; }
        public DbSet<SocialMailModel> Mail { get; set; }
        public DbSet<SocialMailAttachmentModel> MailAttachment { get; set; }

        private readonly IDatabaseConfig config;

        public SocialContext(IDatabaseConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// A context on ready-made options: the design-time factory (migrations without a database connection) and the
        /// self-test (which only builds the model).
        /// </summary>
        public SocialContext(DbContextOptions<SocialContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseConfiguration(config, DatabaseType.Social);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SocialCharacterModel>(entity =>
            {
                entity.ToTable("social_character");
                entity.HasKey(e => e.Guid).HasName("PRIMARY");
                // Not unique: a deleted character keeps its row and name (guild logs, P5312) while a new character may
                // take the name; lookups by name skip deleted rows.
                entity.HasIndex(e => e.Name).HasDatabaseName("name");

                entity.Property(e => e.Guid)
                    .HasColumnName("guid")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();
                entity.Property(e => e.AccountId)
                    .HasColumnName("account_id")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);
                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(64)")
                    .UseCollation("utf8mb4_unicode_ci")
                    .HasDefaultValue("");
                entity.Property(e => e.OriginalName)
                    .HasColumnName("original_name")
                    .HasColumnType("varchar(64)");
                entity.Property(e => e.DeletedAt)
                    .HasColumnName("deleted_at")
                    .HasColumnType("datetime");
                entity.Property(e => e.LastWorld)
                    .HasColumnName("last_world")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);
                entity.Property(e => e.LastZone)
                    .HasColumnName("last_zone")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);
                entity.Property(e => e.LastOnline)
                    .HasColumnName("last_online")
                    .HasColumnType("datetime");

                entity.Property(e => e.LastZoneNameText)
                    .HasColumnName("last_zone_name_text")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.LastWorldNameText)
                    .HasColumnName("last_world_name_text")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);
            });

            modelBuilder.Entity<SocialGuildModel>(entity =>
            {
                entity.ToTable("social_guild");
                entity.HasKey(e => e.Id).HasName("PRIMARY");
                entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("name");
                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();
                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(64)")
                    .UseCollation("utf8mb4_unicode_ci");
                entity.Property(e => e.Motd)
                    .IsRequired()
                    .HasColumnName("motd")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");
                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasColumnName("description")
                    .HasColumnType("varchar(1024)")
                    .HasDefaultValue("");
                entity.Property(e => e.AppData)
                    .IsRequired()
                    .HasColumnName("app_data")
                    .HasColumnType("varchar(2048)")
                    .HasDefaultValue("");
                entity.Property(e => e.LeaderGuid)
                    .HasColumnName("leader_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("datetime");
                entity.Property(e => e.Ranks)
                    .HasColumnName("ranks")
                    .HasColumnType("text");
                entity.Property(e => e.Log)
                    .HasColumnName("log")
                    .HasColumnType("mediumtext");
            });

            modelBuilder.Entity<SocialGuildMemberModel>(entity =>
            {
                entity.ToTable("social_guild_member");
                entity.HasKey(e => e.CharacterGuid).HasName("PRIMARY");
                entity.HasIndex(e => e.GuildId).HasDatabaseName("guild_id");
                entity.Property(e => e.CharacterGuid)
                    .HasColumnName("character_guid")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();
                entity.Property(e => e.GuildId)
                    .HasColumnName("guild_id")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.RankId)
                    .HasColumnName("rank_id")
                    .HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<SocialMailModel>(entity =>
            {
                entity.ToTable("social_mail");
                entity.HasKey(e => e.Id).HasName("PRIMARY");
                entity.HasIndex(e => e.RecipientGuid).HasDatabaseName("recipient_guid");
                entity.HasIndex(e => e.Token).HasDatabaseName("token");
                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedOnAdd();
                entity.Property(e => e.RecipientGuid)
                    .HasColumnName("recipient_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.SenderGuid)
                    .HasColumnName("sender_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.SenderName)
                    .IsRequired()
                    .HasColumnName("sender_name")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");
                entity.Property(e => e.Subject)
                    .IsRequired()
                    .HasColumnName("subject")
                    .HasColumnType("varchar(128)")
                    .HasDefaultValue("");
                entity.Property(e => e.Body)
                    .IsRequired()
                    .HasColumnName("body")
                    .HasColumnType("varchar(4096)")
                    .HasDefaultValue("");
                entity.Property(e => e.SentAt)
                    .HasColumnName("sent_at")
                    .HasColumnType("datetime");
                entity.Property(e => e.IsRead)
                    .HasColumnName("is_read")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);
                entity.Property(e => e.Kind)
                    .HasColumnName("kind")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);
                entity.Property(e => e.GuildId)
                    .HasColumnName("guild_id")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);
                entity.Property(e => e.Token)
                    .HasColumnName("token")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);
                entity.HasMany(e => e.Attachments)
                    .WithOne()
                    .HasForeignKey(a => a.MailId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("FK__social_mail_attachment_mail_id");
            });

            modelBuilder.Entity<SocialMailAttachmentModel>(entity =>
            {
                entity.ToTable("social_mail_attachment");
                entity.HasKey(e => new { e.MailId, e.Slot }).HasName("PRIMARY");
                entity.Property(e => e.MailId)
                    .HasColumnName("mail_id")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Slot)
                    .HasColumnName("slot")
                    .HasColumnType("int(10) unsigned");
                entity.Property(e => e.Type)
                    .HasColumnName("type")
                    .HasColumnType("tinyint(3) unsigned");
                entity.Property(e => e.ItemDefinition)
                    .HasColumnName("item_definition")
                    .HasColumnType("int(10) unsigned");
                entity.Property(e => e.Count)
                    .HasColumnName("count")
                    .HasColumnType("int(10) unsigned");
                entity.Property(e => e.Taken)
                    .HasColumnName("taken")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);
                entity.Property(e => e.Wear)
                    .HasColumnName("wear")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);
            });

            modelBuilder.Entity<FriendModel>(entity =>
            {
                entity.ToTable("friend");
                entity.HasKey(e => new { e.CharacterGuid, e.FriendGuid }).HasName("PRIMARY");
                // "Who lists X" (watchers for online / offline notes, SocialStateForZone friend-of).
                entity.HasIndex(e => e.FriendGuid).HasDatabaseName("friend_guid");

                entity.Property(e => e.CharacterGuid)
                    .HasColumnName("character_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.FriendGuid)
                    .HasColumnName("friend_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.AddedAt)
                    .HasColumnName("added_at")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("current_timestamp()");
            });

            modelBuilder.Entity<IgnoreEntryModel>(entity =>
            {
                entity.ToTable("ignore_entry");
                entity.HasKey(e => new { e.CharacterGuid, e.IgnoredGuid }).HasName("PRIMARY");

                entity.Property(e => e.CharacterGuid)
                    .HasColumnName("character_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.IgnoredGuid)
                    .HasColumnName("ignored_guid")
                    .HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");
            });
        }
    }
}
