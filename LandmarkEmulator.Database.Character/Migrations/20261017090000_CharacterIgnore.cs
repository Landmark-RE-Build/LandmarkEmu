using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Ignore lists: {character, ignored character, the ignored name as typed}.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261017090000_CharacterIgnore")]
    public partial class CharacterIgnore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_ignore",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    ignoredId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: "")
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.characterId, x.ignoredId }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_ignore");
        }
    }
}
