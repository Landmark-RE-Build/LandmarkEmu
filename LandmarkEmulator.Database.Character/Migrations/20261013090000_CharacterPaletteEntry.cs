using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Build palette (radial menu) slots per character: {slot, type, value}.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261013090000_CharacterPaletteEntry")]
    public partial class CharacterPaletteEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_palette_entry",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    slot = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    type = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    value = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.characterId, x.slot }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_palette_entry");
        }
    }
}
