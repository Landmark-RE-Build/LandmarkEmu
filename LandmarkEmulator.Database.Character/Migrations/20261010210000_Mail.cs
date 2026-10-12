using System;
using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>In-game mail: one row per message per recipient.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261010210000_Mail")]
    public partial class Mail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mail",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    recipientGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    senderGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    senderName = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: ""),
                    subject = table.Column<string>(type: "varchar(128)", nullable: false, defaultValue: ""),
                    body = table.Column<string>(type: "varchar(2048)", nullable: false, defaultValue: ""),
                    sentAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    isRead = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "recipientGuid",
                table: "mail",
                column: "recipientGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "mail");
        }
    }
}
