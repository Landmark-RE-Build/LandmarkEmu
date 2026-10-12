using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class ClaimPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "defaultPermissions",
                table: "claim",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "claim_permission",
                columns: table => new
                {
                    claimId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    mask = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.claimId, x.characterId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim_permission");

            migrationBuilder.DropColumn(
                name: "defaultPermissions",
                table: "claim");
        }
    }
}
