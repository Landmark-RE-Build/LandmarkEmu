using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Social.Migrations
{
    /// <inheritdoc />
    public partial class SocialMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "social_mail",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    recipient_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    sender_guid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    sender_name = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject = table.Column<string>(type: "varchar(128)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    body = table.Column<string>(type: "varchar(4096)", nullable: false, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    is_read = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    kind = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    guild_id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    token = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "social_mail_attachment",
                columns: table => new
                {
                    mail_id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    slot = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    type = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    item_definition = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    count = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    taken = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.mail_id, x.slot });
                    table.ForeignKey(
                        name: "FK__social_mail_attachment_mail_id",
                        column: x => x.mail_id,
                        principalTable: "social_mail",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "recipient_guid",
                table: "social_mail",
                column: "recipient_guid");

            migrationBuilder.CreateIndex(
                name: "token",
                table: "social_mail",
                column: "token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "social_mail_attachment");

            migrationBuilder.DropTable(
                name: "social_mail");
        }
    }
}
