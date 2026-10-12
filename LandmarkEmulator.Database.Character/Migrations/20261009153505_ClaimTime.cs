using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class ClaimTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "timeHour",
                table: "claim",
                type: "int(11)",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<float>(
                name: "timeRate",
                table: "claim",
                type: "float",
                nullable: false,
                defaultValue: 1f);

            migrationBuilder.AddColumn<int>(
                name: "visitorTimeHour",
                table: "claim",
                type: "int(11)",
                nullable: false,
                defaultValue: -1);

            migrationBuilder.AddColumn<float>(
                name: "visitorTimeRate",
                table: "claim",
                type: "float",
                nullable: false,
                defaultValue: 1f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "timeHour",
                table: "claim");

            migrationBuilder.DropColumn(
                name: "timeRate",
                table: "claim");

            migrationBuilder.DropColumn(
                name: "visitorTimeHour",
                table: "claim");

            migrationBuilder.DropColumn(
                name: "visitorTimeRate",
                table: "claim");
        }
    }
}
