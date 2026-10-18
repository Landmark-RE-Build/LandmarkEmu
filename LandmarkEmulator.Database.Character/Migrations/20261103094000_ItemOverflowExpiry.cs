using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Overflow items expire (trace/0695/audit-wiki-gaps.md #8; wiki Inventory: 24 hours): <c>character_item.expiresAt</c>
    /// (UTC, null = never). Items already waiting in overflow get their time at the next login.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261103094000_ItemOverflowExpiry")]
    public partial class ItemOverflowExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "expiresAt", table: "character_item", type: "datetime", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "expiresAt", table: "character_item");
        }
    }
}
