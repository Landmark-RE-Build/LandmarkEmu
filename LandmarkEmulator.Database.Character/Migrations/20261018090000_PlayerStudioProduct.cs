using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Player Studio products: a player's design on the marketplace, with its peer-review state.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261018090000_PlayerStudioProduct")]
    public partial class PlayerStudioProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_studio_product",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    ownerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    templateId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(128)", nullable: false, defaultValue: ""),
                    description = table.Column<string>(type: "varchar(1024)", nullable: false, defaultValue: ""),
                    headline = table.Column<string>(type: "varchar(256)", nullable: false, defaultValue: ""),
                    tags = table.Column<string>(type: "varchar(512)", nullable: false, defaultValue: ""),
                    price = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    status = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    approvals = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    rejections = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "ownerGuid", table: "player_studio_product", column: "ownerGuid");
            migrationBuilder.CreateIndex(name: "templateId", table: "player_studio_product", column: "templateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "player_studio_product");
        }
    }
}
