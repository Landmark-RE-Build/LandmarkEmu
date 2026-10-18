using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Social.Migrations
{
    /// <inheritdoc />
    public partial class SocialGuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "last_world_name_text",
                table: "social_character",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "last_zone_name_text",
                table: "social_character",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "social_guild",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    motd = table.Column<string>(type: "varchar(512)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1024)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    app_data = table.Column<string>(type: "varchar(2048)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    leader_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    ranks = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "social_guild_member",
                columns: table => new
                {
                    character_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    guild_id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    rank_id = table.Column<uint>(type: "int(10) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.character_guid);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "name",
                table: "social_guild",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "guild_id",
                table: "social_guild_member",
                column: "guild_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_guild");

            migrationBuilder.DropTable(
                name: "social_guild_member");

            migrationBuilder.DropColumn(
                name: "last_world_name_text",
                table: "social_character");

            migrationBuilder.DropColumn(
                name: "last_zone_name_text",
                table: "social_character");
        }
    }
}
