using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Configuration;
using Microsoft.EntityFrameworkCore;

namespace LandmarkEmulator.Database.Auth
{
    public class AuthContext : DbContext
    {
        public DbSet<AccountModel> Account { get; set; }
        public DbSet<ZoneServerModel> ZoneServer { get; set; }

        private readonly IDatabaseConfig config;

        public AuthContext(IDatabaseConfig config)
        {
            this.config = config;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseConfiguration(config, DatabaseType.Auth);

            optionsBuilder.EnableSensitiveDataLogging();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AccountModel>(entity =>
            {
                entity.ToTable("account");

                entity.HasIndex(e => e.Username)
                    .HasDatabaseName("username");

                entity.HasIndex(e => e.Salt)
                    .HasDatabaseName("salt");

                entity.HasIndex(e => e.Verifier)
                    .HasDatabaseName("verifier");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.Username)
                    .IsRequired()
                    .HasColumnName("username")
                    // varchar(255): what 20220111035602_Accounts created. This read "varchar(128" (no closing bracket)
                    // until 2026-10-18; Pomelo could not parse it and fell back to its varchar(255) default, so the
                    // effective model was already varchar(255) and nothing changes.
                    .HasColumnType("varchar(255)")
                    .HasDefaultValue("");

                entity.Property(e => e.Salt)
                    .IsRequired()
                    .HasColumnName("s")
                    .HasColumnType("varchar(32)")
                    .HasDefaultValue("");

                entity.Property(e => e.Verifier)
                    .IsRequired()
                    .HasColumnName("v")
                    .HasColumnType("varchar(512)")
                    .HasDefaultValue("");

                entity.Property(e => e.SessionKey)
                    .IsRequired()
                    .HasColumnName("sessionKey")
                    .HasColumnType("varchar(32)")
                    .HasDefaultValue("");

                entity.Property(e => e.SessionKeyExpiration)
                    .HasColumnName("sessionKeyExpiration")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("current_timestamp()");

                entity.Property(e => e.ServerTicket)
                    .IsRequired()
                    .HasColumnName("serverTicket")
                    .HasColumnType("varchar(32)")
                    .HasDefaultValue("");

                entity.Property(e => e.ServerTicketIssuedAt)
                    .HasColumnName("serverTicketIssuedAt")
                    .HasColumnType("datetime");

                entity.Property(e => e.ServerTicketWorld)
                    .HasColumnName("serverTicketWorld")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.CreateTime)
                    .HasColumnName("createTime")
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("current_timestamp()");

                // Membership and the trial key (migration AccountMembership; trace/0695/agent-membership.md).
                entity.Property(e => e.MemberType)
                    .HasColumnName("memberType")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValue((byte)0);

                entity.Property(e => e.MemberUntil)
                    .HasColumnName("memberUntil")
                    .HasColumnType("datetime");

                entity.Property(e => e.TrialKeyMailed)
                    .HasColumnName("trialKeyMailed")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(e => e.TrialRedeemed)
                    .HasColumnName("trialRedeemed")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValue(false);

                entity.Property(e => e.MemberStipendAt)
                    .HasColumnName("memberStipendAt")
                    .HasColumnType("datetime");
            });

            modelBuilder.Entity<ZoneServerModel>(entity =>
            {
                entity.ToTable("zone_server");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedOnAdd();

                entity.Property(e => e.NameId)
                    .IsRequired()
                    .HasColumnName("nameId")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(62147);

                entity.Property(e => e.Host)
                    .IsRequired()
                    .HasColumnName("host")
                    .HasColumnType("varchar(64)")
                    .HasDefaultValue("127.0.0.1");

                entity.Property(e => e.Port)
                    .IsRequired()
                    .HasColumnName("port")
                    .HasColumnType("smallint(5) unsigned")
                    .HasDefaultValue(19000);

                entity.Property(e => e.Flags)
                    .HasColumnName("flags")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0);

                entity.Property(e => e.Heartbeat)
                    .HasColumnName("heartbeat")
                    .HasColumnType("datetime");

                entity.Property(e => e.PlayersOnline)
                    .HasColumnName("playersOnline")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.Property(e => e.MaxPlayers)
                    .HasColumnName("maxPlayers")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValue(0u);

                entity.HasData(new ZoneServerModel
                {
                    Id     = 1,
                    NameId = 62147,
                    Host   = "127.0.0.1",
                    Port   = 19000,
                    Flags  = 1
                });
            });
        }
    }
}
