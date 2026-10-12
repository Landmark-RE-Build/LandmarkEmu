using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Coins are Lumen items in the currency container, not a column.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261011120000_DropCharacterCoins")]
    public partial class DropCharacterCoins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coins",
                table: "character");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "coins",
                table: "character",
                type: "int(10) unsigned",
                nullable: false,
                defaultValue: 0u);
        }
    }
}
