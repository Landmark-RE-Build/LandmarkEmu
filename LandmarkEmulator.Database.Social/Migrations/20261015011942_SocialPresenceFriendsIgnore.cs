using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Social.Migrations
{
    /// <summary>
    /// lmemu_social, first tables (social-service.md §5.2, §6 step 5 presence / friends / ignore): the character
    /// directory social_character, friend and ignore_entry. Filled from lmemu_character by SocialImport (§5.7).
    /// </summary>
    public partial class SocialPresenceFriendsIgnore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "friend",
                columns: table => new
                {
                    character_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    friend_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "current_timestamp()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.character_guid, x.friend_guid });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ignore_entry",
                columns: table => new
                {
                    character_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    ignored_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.character_guid, x.ignored_guid });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "social_character",
                columns: table => new
                {
                    guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    account_id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    name = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: "", collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_name = table.Column<string>(type: "varchar(64)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    deleted_at = table.Column<DateTime>(type: "datetime", nullable: true),
                    last_world = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    last_zone = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    last_online = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.guid);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "friend_guid",
                table: "friend",
                column: "friend_guid");

            migrationBuilder.CreateIndex(
                name: "name",
                table: "social_character",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "friend");

            migrationBuilder.DropTable(
                name: "ignore_entry");

            migrationBuilder.DropTable(
                name: "social_character");
        }
    }
}
