using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Database.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LandmarkEmulator.Database.Character
{
    public class CharacterContext : DbContext
    {
        public DbSet<CharacterModel> Character { get; set; }
        public DbSet<CharacterCustomisationModel> CharacterCustomisation { get; set; }
        public DbSet<CharacterItemModel> CharacterItem { get; set; }
        public DbSet<WorldPropModel> WorldProp { get; set; }
        public DbSet<ChestItemModel> ChestItem { get; set; }
        public DbSet<ClaimModel> Claim { get; set; }
        public DbSet<CharacterActionSlotModel> CharacterActionSlot { get; set; }
        public DbSet<ClaimPermissionModel> ClaimPermission { get; set; }
        public DbSet<CharacterRecipeModel> CharacterRecipe { get; set; }
        public DbSet<CharacterUnlockModel> CharacterUnlock { get; set; }
        public DbSet<CharacterPaletteEntryModel> CharacterPaletteEntry { get; set; }
        public DbSet<WorldPropBindingModel> WorldPropBinding { get; set; }
        public DbSet<WorldPropSettingModel> WorldPropSetting { get; set; }
        /// <summary>Trigger volumes made with the Area Tool (Linking Area boxes): Game.AreaDefinitions.</summary>
        public DbSet<WorldAreaModel> WorldArea { get; set; }
        public DbSet<ClaimVendorItemModel> ClaimVendorItem { get; set; }
        public DbSet<CharacterFriendModel> CharacterFriend { get; set; }
        public DbSet<CharacterIgnoreModel> CharacterIgnore { get; set; }
        /// <summary>Client preference documents (PreferencePacketClientXml 0x03; Game.Preferences).</summary>
        public DbSet<CharacterPreferenceModel> CharacterPreference { get; set; }
        /// <summary>Saved bag filters (PlayerFilterUpdatePacket 0xDD sub 4; Game.InventoryFilters).</summary>
        public DbSet<CharacterInventoryFilterModel> CharacterInventoryFilter { get; set; }
        public DbSet<PlayerStudioProductModel> PlayerStudioProduct { get; set; }
        public DbSet<PlayerStudioReviewModel> PlayerStudioReview { get; set; }
        /// <summary>Comments on build sites' gallery entries (Game.Gallery).</summary>
        public DbSet<GalleryCommentModel> GalleryComment { get; set; }
        public DbSet<ShowcaseFollowModel> ShowcaseFollow { get; set; }
        public DbSet<ShowcaseDigestRunModel> ShowcaseDigestRun { get; set; }
        public DbSet<SiteTemplateInstanceModel> SiteTemplateInstance { get; set; }
        public DbSet<CharacterObjectiveModel> CharacterObjective { get; set; }
        public DbSet<CharacterNpcFlagModel> CharacterNpcFlag { get; set; }
        public DbSet<GuildModel> Guild { get; set; }
        public DbSet<GuildMemberModel> GuildMember { get; set; }
        public DbSet<MailModel> Mail { get; set; }
        public DbSet<TemplateModel> Template { get; set; }

        private readonly IDatabaseConfig config;
        private readonly DatabaseType type;

        /// <param name="type">
        /// Character: the shared database (characters, items, guilds, mail, designs...). Zone: one world server's own
        /// database (its build sites, props, chests, vendors, prop links) when worlds keep those apart; both databases
        /// have the same tables, each uses its own part (CharacterDatabase.NewWorldContext).
        /// </param>
        public CharacterContext(IDatabaseConfig config, DatabaseType type = DatabaseType.Character)
        {
            this.config = config;
            this.type = type;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseConfiguration(config, type);
        }

        /// <remarks>
        /// Keep this model, the migrations and Migrations/CharacterContextModelSnapshot.cs in step: a text or blob column a
        /// migration creates <c>nullable: false</c> needs <c>.IsRequired()</c> here (this project has no nullable reference
        /// types, so a string or byte[] property is otherwise nullable), or the model reports pending changes against the
        /// snapshot. 2026-10-18: 21 such columns (claim, mail, template, world_area, world_prop, player_studio_product,
        /// character_ignore) lacked it; their migrations always created them NOT NULL (the strings with DEFAULT ''), so the database was right
        /// and no migration was needed.
        /// </remarks>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CharacterModel>(entity =>
            {
                entity.ToTable("character");

                entity.HasIndex(e => e.AccountId)
                    .HasDatabaseName("accountId");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.AccountId)
                    .HasColumnName("accountId")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.CreateTime)
                    .HasColumnName("createTime")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.DeleteTime)
                    .HasColumnName("deleteTime")
                    .HasColumnType("datetime")
                    .HasDefaultValue(null);

                entity.Property(e => e.Gender)
                    .HasColumnName("gender")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.LastOnline)
                    .HasColumnName("lastOnline")
                    .HasColumnType("datetime");

                entity.Property(e => e.LastServerId)
                    .HasColumnName("lastServerId")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.ZoneId)
                    .HasColumnName("zoneId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(406u);

                entity.Property(e => e.LoginCount)
                    .HasColumnName("loginCount")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.PlayedMinutes)
                    .HasColumnName("playedMinutes")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.Bio)
                    .IsRequired()
                    .HasColumnName("bio")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.ChatBubbleForeground)
                    .HasColumnName("chatBubbleFg")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0xFFFFFFu);

                entity.Property(e => e.ChatBubbleBackground)
                    .HasColumnName("chatBubbleBg")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0xD4E2F0u);

                entity.Property(e => e.ChatBubbleSize)
                    .HasColumnName("chatBubbleSize")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(1u);

                entity.Property(e => e.PositionX)
                    .HasColumnName("positionX")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.PositionY)
                    .HasColumnName("positionY")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.PositionZ)
                    .HasColumnName("positionZ")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationX)
                    .HasColumnName("rotationX")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationY)
                    .HasColumnName("rotationY")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationZ)
                    .HasColumnName("rotationZ")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationW)
                    .HasColumnName("rotationW")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.Name)
                    .HasColumnName("name")
                    .HasColumnType("varchar(50)")
                    .HasDefaultValue("");

                entity.Property(e => e.OriginalName)
                    .HasColumnName("originalName")
                    .HasColumnType("varchar(50)")
                    .HasDefaultValue(null);

                entity.Property(e => e.ProfileTypeId)
                    .HasColumnName("profileTypeId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Race)
                    .HasColumnName("race")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.SkinTint)
                    .HasColumnName("skinTint")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);
            });

            modelBuilder.Entity<CharacterCustomisationModel>(entity =>
            {
                entity.ToTable("character_customisation");

                entity.HasKey(e => new { e.Id, e.Slot })
                    .HasName("PRIMARY");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Slot)
                    .HasColumnName("slot")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Option)
                    .HasColumnName("option")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Tint)
                    .HasColumnName("tint")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.HasOne(d => d.Character)
                    .WithMany(p => p.Customisation)
                    .HasForeignKey(d => d.Id)
                    .HasConstraintName("FK__character_customisation_id__character_id");
            });

            modelBuilder.Entity<CharacterItemModel>(entity =>
            {
                entity.ToTable("character_item");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.CharacterId)
                    .HasDatabaseName("characterId");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.ItemDefinitionId)
                    .HasColumnName("itemDefinitionId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Count)
                    .HasColumnName("count")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Container)
                    .HasColumnName("container")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Slot)
                    .HasColumnName("slot")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Wear)
                    .HasColumnName("wear")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.TradeableAfter)
                    .HasColumnName("tradeableAfter")
                    .HasColumnType("datetime");

                entity.Property(e => e.ExpiresAt)
                    .HasColumnName("expiresAt")
                    .HasColumnType("datetime");

                entity.Property(e => e.AutoResupply)
                    .HasColumnName("autoResupply")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.HasOne(d => d.Character)
                    .WithMany(p => p.Items)
                    .HasForeignKey(d => d.CharacterId)
                    .HasConstraintName("FK__character_item_characterId__character_id");
            });

            modelBuilder.Entity<ChestItemModel>(entity =>
            {
                entity.ToTable("chest_item");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.ChestGuid)
                    .HasDatabaseName("chestGuid");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.ChestGuid)
                    .HasColumnName("chestGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.ItemDefinitionId)
                    .HasColumnName("itemDefinitionId")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Count)
                    .HasColumnName("count")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Slot)
                    .HasColumnName("slot")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Wear)
                    .HasColumnName("wear")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.DepositorGuid)
                    .HasColumnName("depositorGuid")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);

                entity.Property(e => e.AutoResupply)
                    .HasColumnName("autoResupply")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);
            });

            modelBuilder.Entity<WorldPropModel>(entity =>
            {
                entity.ToTable("world_prop");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.OwnerGuid)
                    .HasDatabaseName("ownerGuid");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.PropId)
                    .HasColumnName("propId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.ModelId)
                    .HasColumnName("modelId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.NameId)
                    .HasColumnName("nameId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.OwnerGuid)
                    .HasColumnName("ownerGuid")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.PositionX)
                    .HasColumnName("positionX")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.PositionY)
                    .HasColumnName("positionY")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.PositionZ)
                    .HasColumnName("positionZ")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationX)
                    .HasColumnName("rotationX")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationY)
                    .HasColumnName("rotationY")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationZ)
                    .HasColumnName("rotationZ")
                    .HasColumnType("float")
                    .HasDefaultValue(0f);

                entity.Property(e => e.RotationW)
                    .HasColumnName("rotationW")
                    .HasColumnType("float")
                    .HasDefaultValue(1f);

                entity.Property(e => e.Scale)
                    .HasColumnName("scale")
                    .HasColumnType("float")
                    .HasDefaultValue(1f);

                entity.Property(e => e.ZoneId)
                    .HasColumnName("zoneId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(406u);

                entity.Property(e => e.TintAlias)
                    .IsRequired()
                    .HasColumnName("tintAlias")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");

                entity.Property(e => e.CustomName)
                    .IsRequired()
                    .HasColumnName("customName")
                    .HasColumnType("varchar(128)")
                    .HasDefaultValue("");

                entity.Property(e => e.Text)
                    .IsRequired()
                    .HasColumnName("text")
                    .HasColumnType("varchar(1024)")
                    .HasDefaultValue("");

                entity.Property(e => e.Disabled)
                    .HasColumnName("disabled")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(e => e.PackGroup)
                    .HasColumnName("packGroup")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);

                entity.Property(e => e.PackSpawner)
                    .HasColumnName("packSpawner")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.PaidByItem)
                    .HasColumnName("paidByItem")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.PaidByItemGuid)
                    .HasColumnName("paidByItemGuid")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);
            });

            modelBuilder.Entity<MailModel>(entity =>
            {
                entity.ToTable("mail");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.RecipientGuid)
                    .HasDatabaseName("recipientGuid");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.RecipientGuid)
                    .HasColumnName("recipientGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.SenderGuid)
                    .HasColumnName("senderGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.SenderName)
                    .IsRequired()
                    .HasColumnName("senderName")
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
                    .HasColumnName("sentAt")
                    .HasColumnType("datetime");

                entity.Property(e => e.IsRead)
                    .HasColumnName("isRead")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(e => e.Kind)
                    .HasColumnName("kind")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Attachments)
                    .IsRequired()
                    .HasColumnName("attachments")
                    .HasColumnType("varchar(256)")
                    .HasDefaultValue("");
            });

            modelBuilder.Entity<TemplateModel>(entity =>
            {
                entity.ToTable("template");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.OwnerGuid)
                    .HasDatabaseName("ownerGuid");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.AuthorName)
                    .IsRequired()
                    .HasColumnName("authorName")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(128)")
                    .HasDefaultValue("");

                entity.Property(e => e.Tags)
                    .IsRequired()
                    .HasColumnName("tags")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.SizeX)
                    .HasColumnName("sizeX")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.SizeY)
                    .HasColumnName("sizeY")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.SizeZ)
                    .HasColumnName("sizeZ")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Materials)
                    .IsRequired()
                    .HasColumnName("materials")
                    .HasColumnType("varchar(1024)")
                    .HasDefaultValue("");

                entity.Property(e => e.Blocks)
                    .IsRequired()
                    .HasColumnName("blocks")
                    .HasColumnType("longblob");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("createdAt")
                    .HasColumnType("datetime");

                entity.Property(e => e.OwnerGuid)
                    .HasColumnName("ownerGuid")
                    .HasColumnType("bigint(20) unsigned");
            });

            modelBuilder.Entity<CharacterActionSlotModel>(entity =>
            {
                entity.ToTable("character_action_slot");

                entity.HasKey(e => new { e.CharacterId, e.BarId, e.SlotId })
                    .HasName("PRIMARY");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.BarId)
                    .HasColumnName("barId")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.SlotId)
                    .HasColumnName("slotId")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Type)
                    .HasColumnName("type")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.Value)
                    .HasColumnName("value")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);
            });

            modelBuilder.Entity<ClaimModel>(entity =>
            {
                entity.ToTable("claim");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.OwnerGuid)
                    .HasDatabaseName("ownerGuid");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.OwnerGuid)
                    .HasColumnName("ownerGuid")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Name)
                    .HasColumnName("name")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");

                entity.Property(e => e.MinX)
                    .HasColumnName("minX")
                    .HasColumnType("double")
                    .HasDefaultValue(0d);

                entity.Property(e => e.MinY)
                    .HasColumnName("minY")
                    .HasColumnType("double")
                    .HasDefaultValue(0d);

                entity.Property(e => e.MinZ)
                    .HasColumnName("minZ")
                    .HasColumnType("double")
                    .HasDefaultValue(0d);

                entity.Property(e => e.MaxX)
                    .HasColumnName("maxX")
                    .HasColumnType("double")
                    .HasDefaultValue(0d);

                entity.Property(e => e.MaxY)
                    .HasColumnName("maxY")
                    .HasColumnType("double")
                    .HasDefaultValue(0d);

                entity.Property(e => e.MaxZ)
                    .HasColumnName("maxZ")
                    .HasColumnType("double")
                    .HasDefaultValue(0d);

                entity.Property(e => e.DefaultPermissions)
                    .HasColumnName("defaultPermissions")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.GroupPermissions)
                    .HasColumnName("groupPermissions")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.GuildPermissions)
                    .HasColumnName("guildPermissions")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.ZoneId)
                    .HasColumnName("zoneId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(406u);

                entity.Property(e => e.TimeHour)
                    .HasColumnName("timeHour")
                    .HasColumnType("int(11)")
                    .HasDefaultValue(-1);

                entity.Property(e => e.TimeRate)
                    .HasColumnName("timeRate")
                    .HasColumnType("float")
                    .HasDefaultValue(1f);

                entity.Property(e => e.VisitorTimeHour)
                    .HasColumnName("visitorTimeHour")
                    .HasColumnType("int(11)")
                    .HasDefaultValue(-1);

                entity.Property(e => e.VisitorTimeRate)
                    .HasColumnName("visitorTimeRate")
                    .HasColumnType("float")
                    .HasDefaultValue(1f);

                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasColumnName("description")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.Tags)
                    .IsRequired()
                    .HasColumnName("tags")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.PlayMode)
                    .HasColumnName("playMode")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(e => e.AdventureFlags)
                    .HasColumnName("adventureFlags")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValue(0ul);

                entity.Property(e => e.Inventory)
                    .IsRequired()
                    .HasColumnName("inventory")
                    .HasColumnType("varchar(4096)")
                    .HasDefaultValue("");
            });

            modelBuilder.Entity<CharacterUnlockModel>(entity =>
            {
                entity.ToTable("character_unlock");
                entity.HasKey(e => new { e.CharacterId, e.Kind, e.Value }).HasName("PRIMARY");
                entity.Property(e => e.CharacterId).HasColumnName("characterId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Value).HasColumnName("value").HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<CharacterPaletteEntryModel>(entity =>
            {
                entity.ToTable("character_palette_entry");
                entity.HasKey(e => new { e.CharacterId, e.Slot }).HasName("PRIMARY");
                entity.Property(e => e.CharacterId).HasColumnName("characterId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Slot).HasColumnName("slot").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Type).HasColumnName("type").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Value).HasColumnName("value").HasColumnType("bigint(20) unsigned");
            });

            modelBuilder.Entity<ClaimVendorItemModel>(entity =>
            {
                entity.ToTable("claim_vendor_item");
                entity.HasKey(e => e.Id).HasName("PRIMARY");
                entity.HasIndex(e => e.VendorId).HasDatabaseName("vendorId");
                entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20) unsigned").ValueGeneratedNever();
                entity.Property(e => e.VendorId).HasColumnName("vendorId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.OwnerGuid).HasColumnName("ownerGuid").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.ItemDefinitionId).HasColumnName("itemDefinitionId").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Count).HasColumnName("count").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Price).HasColumnName("price").HasColumnType("int(10) unsigned");
                entity.Property(e => e.FeePaid).HasColumnName("feePaid").HasColumnType("int(10) unsigned");
                entity.Property(e => e.ListedAt).HasColumnName("listedAt").HasColumnType("datetime");
            });

            modelBuilder.Entity<WorldAreaModel>(entity =>
            {
                entity.ToTable("world_area");
                entity.HasKey(e => e.Id).HasName("PRIMARY");
                entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20) unsigned").ValueGeneratedNever();
                entity.Property(e => e.ZoneId).HasColumnName("zoneId").HasColumnType("int(10) unsigned");
                entity.Property(e => e.OwnerId).HasColumnName("ownerId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.PropId).HasColumnName("propId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Data).IsRequired().HasColumnName("data").HasColumnType("blob");
            });

            modelBuilder.Entity<WorldPropSettingModel>(entity =>
            {
                entity.ToTable("world_prop_setting");
                entity.HasKey(e => new { e.PropId, e.Key }).HasName("PRIMARY");
                entity.Property(e => e.PropId).HasColumnName("propId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Key).HasColumnName("key").HasColumnType("varchar(64)");
                entity.Property(e => e.Value).HasColumnName("value").HasColumnType("float");
            });

            modelBuilder.Entity<WorldPropBindingModel>(entity =>
            {
                entity.ToTable("world_prop_binding");
                entity.HasKey(e => e.Id).HasName("PRIMARY");
                entity.Property(e => e.Id).HasColumnName("id").HasColumnType("bigint(20) unsigned").ValueGeneratedNever();
                entity.Property(e => e.SourceId).HasColumnName("sourceId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.EventId).HasColumnName("eventId").HasColumnType("int(10) unsigned");
                entity.Property(e => e.TargetId).HasColumnName("targetId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.ActionId).HasColumnName("actionId").HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<CharacterRecipeModel>(entity =>
            {
                entity.ToTable("character_recipe");

                entity.HasKey(e => new { e.CharacterId, e.RecipeId })
                    .HasName("PRIMARY");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.RecipeId)
                    .HasColumnName("recipeId")
                    .HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<GuildModel>(entity =>
            {
                entity.ToTable("guild");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.Name)
                    .IsUnique()
                    .HasDatabaseName("name");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(64)");

                entity.Property(e => e.Motd)
                    .IsRequired()
                    .HasColumnName("motd")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.LeaderId)
                    .HasColumnName("leaderId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.CreateTime)
                    .HasColumnName("createTime")
                    .HasColumnType("datetime");

                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasColumnName("description")
                    .HasColumnType("varchar(1024)")
                    .HasDefaultValue("");

                entity.Property(e => e.AppData)
                    .IsRequired()
                    .HasColumnName("appData")
                    .HasColumnType("varchar(2048)")
                    .HasDefaultValue("");

                entity.Property(e => e.Ranks)
                    .HasColumnName("ranks")
                    .HasColumnType("text");

                entity.Property(e => e.Log)
                    .HasColumnName("log")
                    .HasColumnType("mediumtext");
            });

            modelBuilder.Entity<GuildMemberModel>(entity =>
            {
                entity.ToTable("guild_member");

                entity.HasKey(e => e.CharacterId)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.GuildId)
                    .HasDatabaseName("guildId");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.GuildId)
                    .HasColumnName("guildId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.RankId)
                    .HasColumnName("rankId")
                    .HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<CharacterObjectiveModel>(entity =>
            {
                entity.ToTable("character_objective");

                entity.HasKey(e => new { e.CharacterId, e.ObjectiveId })
                    .HasName("PRIMARY");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.ObjectiveId)
                    .HasColumnName("objectiveId")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.AchievementId)
                    .HasColumnName("achievementId")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.State)
                    .HasColumnName("state")
                    .HasColumnType("tinyint(3) unsigned");

                entity.Property(e => e.Current)
                    .HasColumnName("current")
                    .HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<CharacterFriendModel>(entity =>
            {
                entity.ToTable("character_friend");

                entity.HasKey(e => new { e.CharacterId, e.FriendId })
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.FriendId)
                    .HasDatabaseName("friendId");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.FriendId)
                    .HasColumnName("friendId")
                    .HasColumnType("bigint(20) unsigned");
            });

            modelBuilder.Entity<CharacterIgnoreModel>(entity =>
            {
                entity.ToTable("character_ignore");

                entity.HasKey(e => new { e.CharacterId, e.IgnoredId })
                    .HasName("PRIMARY");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.IgnoredId)
                    .HasColumnName("ignoredId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");
            });

            modelBuilder.Entity<CharacterPreferenceModel>(entity =>
            {
                entity.ToTable("character_preference");
                entity.HasKey(e => new { e.CharacterId, e.Kind }).HasName("PRIMARY");
                entity.Property(e => e.CharacterId).HasColumnName("characterId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.Kind).HasColumnName("kind").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Xml).IsRequired().HasColumnName("xml").HasColumnType("blob");
                entity.Property(e => e.UpdatedAt).HasColumnName("updatedAt").HasColumnType("datetime");
            });

            modelBuilder.Entity<CharacterNpcFlagModel>(entity =>
            {
                entity.ToTable("character_npc_flag");
                entity.HasKey(e => new { e.CharacterId, e.NpcId, e.Key }).HasName("PRIMARY");
                entity.Property(e => e.CharacterId).HasColumnName("characterId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.NpcId).IsRequired().HasColumnName("npcId").HasColumnType("varchar(64)");
                entity.Property(e => e.Key).IsRequired().HasColumnName("key").HasColumnType("varchar(64)");
                entity.Property(e => e.Value).HasColumnName("value").HasColumnType("int(11)");
            });

            modelBuilder.Entity<CharacterInventoryFilterModel>(entity =>
            {
                entity.ToTable("character_inventory_filter");
                entity.HasKey(e => new { e.CharacterId, e.FilterId }).HasName("PRIMARY");
                entity.Property(e => e.CharacterId).HasColumnName("characterId").HasColumnType("bigint(20) unsigned");
                entity.Property(e => e.FilterId).HasColumnName("filterId").HasColumnType("int(10) unsigned");
                entity.Property(e => e.Name).IsRequired().HasColumnName("name").HasColumnType("varchar(255)");
                entity.Property(e => e.FilterString).IsRequired().HasColumnName("filterString").HasColumnType("varchar(2048)");
            });

            modelBuilder.Entity<PlayerStudioReviewModel>(entity =>
            {
                entity.ToTable("player_studio_review");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => new { e.ProductId, e.ReviewerGuid })
                    .IsUnique()
                    .HasDatabaseName("productReviewer");

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd()
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.ProductId)
                    .HasColumnName("productId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.ReviewerGuid)
                    .HasColumnName("reviewerGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.Verdict)
                    .HasColumnName("verdict")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("createdAt")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<GalleryCommentModel>(entity =>
            {
                entity.ToTable("gallery_comment");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.ClaimId)
                    .HasDatabaseName("claimId");

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd()
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.ClaimId)
                    .HasColumnName("claimId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.AuthorGuid)
                    .HasColumnName("authorGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.AuthorName)
                    .IsRequired()
                    .HasColumnName("authorName")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");

                entity.Property(e => e.Text)
                    .IsRequired()
                    .HasColumnName("text")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("createdAt")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<PlayerStudioProductModel>(entity =>
            {
                entity.ToTable("player_studio_product");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.OwnerGuid)
                    .HasDatabaseName("ownerGuid");

                entity.HasIndex(e => e.TemplateId)
                    .HasDatabaseName("templateId");

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd()
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.OwnerGuid)
                    .HasColumnName("ownerGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.TemplateId)
                    .HasColumnName("templateId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasColumnType("varchar(128)")
                    .HasDefaultValue("");

                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasColumnName("description")
                    .HasColumnType("varchar(1024)")
                    .HasDefaultValue("");

                entity.Property(e => e.Headline)
                    .IsRequired()
                    .HasColumnName("headline")
                    .HasColumnType("varchar(256)")
                    .HasDefaultValue("");

                entity.Property(e => e.Tags)
                    .IsRequired()
                    .HasColumnName("tags")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.Price)
                    .HasColumnName("price")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Status)
                    .HasColumnName("status")
                    .HasColumnType("tinyint(3) unsigned");

                entity.Property(e => e.ItemType)
                    .HasColumnName("itemType")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(2u);

                entity.Property(e => e.Approvals)
                    .HasColumnName("approvals")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Rejections)
                    .HasColumnName("rejections")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("createdAt")
                    .HasColumnType("datetime");

                entity.Property(e => e.ListedAt)
                    .HasColumnName("listedAt")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<ShowcaseFollowModel>(entity =>
            {
                entity.ToTable("showcase_follow");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => new { e.FollowerGuid, e.CreatorGuid, e.Tags })
                    .IsUnique()
                    .HasDatabaseName("follow");

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd()
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.FollowerGuid)
                    .HasColumnName("followerGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.CreatorGuid)
                    .HasColumnName("creatorGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.CreatorName)
                    .IsRequired()
                    .HasColumnName("creatorName")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("");

                entity.Property(e => e.Tags)
                    .IsRequired()
                    .HasColumnName("tags")
                    .HasColumnType("varchar(160)")
                    .HasDefaultValue("");

                entity.Property(e => e.FollowedAt)
                    .HasColumnName("followedAt")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<ShowcaseDigestRunModel>(entity =>
            {
                entity.ToTable("showcase_digest_run");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => e.Slot)
                    .IsUnique()
                    .HasDatabaseName("slot");

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd()
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.Slot)
                    .IsRequired()
                    .HasColumnName("slot")
                    .HasColumnType("varchar(32)");

                entity.Property(e => e.RanAt)
                    .HasColumnName("ranAt")
                    .HasColumnType("datetime");

                entity.Property(e => e.Mails)
                    .HasColumnName("mails")
                    .HasColumnType("int(10) unsigned");
            });

            modelBuilder.Entity<SiteTemplateInstanceModel>(entity =>
            {
                entity.ToTable("site_template_instance");

                entity.HasKey(e => e.Id)
                    .HasName("PRIMARY");

                entity.HasIndex(e => new { e.ZoneId, e.SiteGuid })
                    .HasDatabaseName("site");

                entity.Property(e => e.Id)
                    .ValueGeneratedOnAdd()
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.ZoneId)
                    .HasColumnName("zoneId")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.SiteGuid)
                    .HasColumnName("siteGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.TemplateGuid)
                    .HasColumnName("templateGuid")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.PlacedBy)
                    .HasColumnName("placedBy")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.MinX).HasColumnName("minX").HasColumnType("double");
                entity.Property(e => e.MinY).HasColumnName("minY").HasColumnType("double");
                entity.Property(e => e.MinZ).HasColumnName("minZ").HasColumnType("double");
                entity.Property(e => e.MaxX).HasColumnName("maxX").HasColumnType("double");
                entity.Property(e => e.MaxY).HasColumnName("maxY").HasColumnType("double");
                entity.Property(e => e.MaxZ).HasColumnName("maxZ").HasColumnType("double");

                entity.Property(e => e.PlacedAt)
                    .HasColumnName("placedAt")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<ClaimPermissionModel>(entity =>
            {
                entity.ToTable("claim_permission");

                entity.HasKey(e => new { e.ClaimId, e.CharacterId })
                    .HasName("PRIMARY");

                entity.Property(e => e.ClaimId)
                    .HasColumnName("claimId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("characterId")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.Mask)
                    .HasColumnName("mask")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);
            });
        }
    }
}
