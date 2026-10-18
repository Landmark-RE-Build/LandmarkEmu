using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Items keep their auto-resupply switch (Inventory 0x2D sub 0x0D InventoryPacketToggleAutoResupply; the item record's
    /// byte at ClientItem +0xdc): <c>character_item.autoResupply</c> and <c>chest_item.autoResupply</c>, false for every
    /// existing item (live sent false on every captured item). Live's own item table had an <c>auto_resupply</c> column.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261104091500_ItemAutoResupply")]
    public partial class ItemAutoResupply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "autoResupply", table: "character_item", type: "tinyint(1)", nullable: false,
                defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "autoResupply", table: "chest_item", type: "tinyint(1)", nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "autoResupply", table: "character_item");
            migrationBuilder.DropColumn(name: "autoResupply", table: "chest_item");
        }
    }
}
