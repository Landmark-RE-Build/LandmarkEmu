using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Story-tool Links between placed props: {id, source prop, event, target prop, action}.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261014090000_WorldPropBinding")]
    public partial class WorldPropBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "world_prop_binding",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    sourceId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    eventId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    targetId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    actionId = table.Column<uint>(type: "int(10) unsigned", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "world_prop_binding");
        }
    }
}
