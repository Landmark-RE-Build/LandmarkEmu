using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Trigger volumes made with the Area Tool (AreaDefinition family 0xEA, TASKS 2.33): {area guid, landscape, maker,
    /// linked Linking Area prop, the AreaDefinition record as sent on the wire}. See Game.AreaDefinitions.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261026093300_WorldArea")]
    public partial class WorldArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "world_area",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    zoneId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    ownerId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    propId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    data = table.Column<byte[]>(type: "blob", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "world_area");
        }
    }
}
