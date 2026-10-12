using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class CharacterObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_objective",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    objectiveId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    achievementId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    state = table.Column<byte>(type: "tinyint(3) unsigned", nullable: false),
                    current = table.Column<uint>(type: "int(10) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.characterId, x.objectiveId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_objective");
        }
    }
}
