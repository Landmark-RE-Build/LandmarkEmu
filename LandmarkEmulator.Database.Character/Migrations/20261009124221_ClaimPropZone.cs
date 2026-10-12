using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class ClaimPropZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "zoneId",
                table: "world_prop",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 406u);

            migrationBuilder.AddColumn<uint>(
                name: "zoneId",
                table: "claim",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 406u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "zoneId",
                table: "world_prop");

            migrationBuilder.DropColumn(
                name: "zoneId",
                table: "claim");
        }
    }
}
