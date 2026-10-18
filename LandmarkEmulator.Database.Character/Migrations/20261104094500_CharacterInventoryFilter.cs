using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Saved bag filters (Game.InventoryFilters; docs/protocol.md §14 "Bag filters"): <c>character_inventory_filter</c> holds the
    /// filters the 0.695 client reports with PlayerFilterUpdatePacket (zone 0xDD sub 4: {u32 FilterId, string FilterName,
    /// string FilterString, u32 op}; writer FUN_142fc3d20) and gets back in SendSelfToClient's InventoryFilterManager
    /// (reader FUN_142c3d470) at the next login. One row per character and filter id; the server caps the name at 255 and
    /// the filter string at 2048 bytes (UTF-8), so the columns always fit.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261104094500_CharacterInventoryFilter")]
    public partial class CharacterInventoryFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_inventory_filter",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    filterId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(255)", nullable: false),
                    filterString = table.Column<string>(type: "varchar(2048)", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.characterId, x.filterId }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_inventory_filter");
        }
    }
}
