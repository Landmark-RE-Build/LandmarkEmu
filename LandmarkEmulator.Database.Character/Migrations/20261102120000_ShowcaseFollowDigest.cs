using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Following creators and tags in the Showcase, and the daily mail of what they listed (Update Notes 27 Aug 2014;
    /// OPEN-LEADS L137; Game.ShowcaseFollow): <c>showcase_follow</c> (who follows which creator or tag group; until now the
    /// client's FollowCreator list lived in memory only), <c>showcase_digest_run</c> (one row per digest run: the next run
    /// covers what was listed after the newest; the scheduled run's slot is unique per UTC day, so worlds sharing the
    /// character database send each day's mail once), and <c>player_studio_product.listedAt</c> (when a design passed
    /// review; null for older rows, which use createdAt).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261102120000_ShowcaseFollowDigest")]
    public partial class ShowcaseFollowDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "showcase_follow",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    followerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    creatorGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    creatorName = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: ""),
                    tags = table.Column<string>(type: "varchar(160)", nullable: false, defaultValue: ""),
                    followedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "follow", table: "showcase_follow",
                columns: new[] { "followerGuid", "creatorGuid", "tags" }, unique: true);

            migrationBuilder.CreateTable(
                name: "showcase_digest_run",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    slot = table.Column<string>(type: "varchar(32)", nullable: false),
                    ranAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    mails = table.Column<uint>(type: "int(10) unsigned", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "slot", table: "showcase_digest_run", column: "slot", unique: true);

            migrationBuilder.AddColumn<DateTime>(name: "listedAt", table: "player_studio_product", type: "datetime", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "showcase_follow");
            migrationBuilder.DropTable(name: "showcase_digest_run");
            migrationBuilder.DropColumn(name: "listedAt", table: "player_studio_product");
        }
    }
}
