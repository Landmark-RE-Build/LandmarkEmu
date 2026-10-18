using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Build-site inventory (OPEN-LEADS L146, trace/0695/agent-props2.md): a placed prop remembers the bag item it was
    /// placed from (world_prop.paidByItem / paidByItemGuid — before, only for the current run), and a site keeps the items
    /// added to it that are not placed (claim.inventory, the admin's /item claimAddItem).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261030090000_ClaimInventory")]
    public partial class ClaimInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(name: "paidByItem", table: "world_prop", type: "int(10) unsigned",
                nullable: false, defaultValue: 0u);
            migrationBuilder.AddColumn<ulong>(name: "paidByItemGuid", table: "world_prop", type: "bigint(20) unsigned",
                nullable: false, defaultValue: 0ul);
            migrationBuilder.AddColumn<string>(name: "inventory", table: "claim", type: "varchar(4096)",
                nullable: false, defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "paidByItem", table: "world_prop");
            migrationBuilder.DropColumn(name: "paidByItemGuid", table: "world_prop");
            migrationBuilder.DropColumn(name: "inventory", table: "claim");
        }
    }
}
