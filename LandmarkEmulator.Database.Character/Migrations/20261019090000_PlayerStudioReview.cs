using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Player Studio peer-review votes, and the mail kind flags (Player Studio accepted / rejected mail).</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261019090000_PlayerStudioReview")]
    public partial class PlayerStudioReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(name: "kind", table: "mail", type: "int(10) unsigned", nullable: false,
                defaultValue: 0u);
            migrationBuilder.CreateTable(
                name: "player_studio_review",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    productId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    reviewerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    verdict = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "productReviewer", table: "player_studio_review",
                columns: new[] { "productId", "reviewerGuid" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "player_studio_review");
            migrationBuilder.DropColumn(name: "kind", table: "mail");
        }
    }
}
