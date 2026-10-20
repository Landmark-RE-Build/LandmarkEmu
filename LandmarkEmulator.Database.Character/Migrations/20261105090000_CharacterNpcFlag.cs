using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>World NPC per-player flags (Game.Npc.WorldNpcs): <c>character_npc_flag</c>.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261105090000_CharacterNpcFlag")]
    public partial class CharacterNpcFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_npc_flag",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    npcId = table.Column<string>(type: "varchar(64)", nullable: false),
                    key = table.Column<string>(type: "varchar(64)", nullable: false),
                    value = table.Column<int>(type: "int(11)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.characterId, x.npcId, x.key }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_npc_flag");
        }
    }
}
