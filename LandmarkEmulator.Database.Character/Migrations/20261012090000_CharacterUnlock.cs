using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Per-character unlocks (titles owned, the title shown, …) as (kind, value) rows.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261012090000_CharacterUnlock")]
    public partial class CharacterUnlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_unlock",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    kind = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    value = table.Column<uint>(type: "int(10) unsigned", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.characterId, x.kind, x.value }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_unlock");
        }
    }
}
