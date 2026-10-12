using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class CharacterRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "rotationW",
                table: "character",
                type: "float",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "rotationX",
                table: "character",
                type: "float",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "rotationY",
                table: "character",
                type: "float",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "rotationZ",
                table: "character",
                type: "float",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rotationW",
                table: "character");

            migrationBuilder.DropColumn(
                name: "rotationX",
                table: "character");

            migrationBuilder.DropColumn(
                name: "rotationY",
                table: "character");

            migrationBuilder.DropColumn(
                name: "rotationZ",
                table: "character");
        }
    }
}
