using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class ActionSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_action_slot",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    barId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    slotId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    type = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    value = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.characterId, x.barId, x.slotId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_action_slot");
        }
    }
}
