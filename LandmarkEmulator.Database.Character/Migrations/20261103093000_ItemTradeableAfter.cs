using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// A store purchase's waiting period before it can be traded (trace/0695/audit-wiki-gaps.md #7; live's "fraud timer":
    /// TransferItemResponse 0x12 "Item can't be traded if it has fraud timer on it", texts 577061 / 562759 / 577356):
    /// <c>character_item.tradeableAfter</c> (UTC, null = none). Existing items have none.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261103093000_ItemTradeableAfter")]
    public partial class ItemTradeableAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "tradeableAfter", table: "character_item", type: "datetime", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "tradeableAfter", table: "character_item");
        }
    }
}
