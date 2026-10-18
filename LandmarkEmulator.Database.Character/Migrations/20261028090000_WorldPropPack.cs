using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Placement packs (TASKS E.1, trace/0695/movers-695.md): a mover's parts (two Translocation Pads, a Flinger and its
    /// target, a Magical Platform and its docks) share a group id and remember the spawner prop that placed them.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261028090000_WorldPropPack")]
    public partial class WorldPropPack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(name: "packGroup", table: "world_prop", type: "bigint(20) unsigned",
                nullable: false, defaultValue: 0ul);
            migrationBuilder.AddColumn<uint>(name: "packSpawner", table: "world_prop", type: "int(10) unsigned",
                nullable: false, defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "packGroup", table: "world_prop");
            migrationBuilder.DropColumn(name: "packSpawner", table: "world_prop");
        }
    }
}
