using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Item wear survives a chest (trace/0695/audit-wiki-gaps.md #1): <c>chest_item.wear</c>, the stored item's durability
    /// points lost (as <c>character_item.wear</c>). Until now a chest rebuilt the item from its definition on the way out,
    /// so storing a worn item and taking it back repaired it for free. Existing rows start at 0 (unworn).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261103090000_ChestItemWear")]
    public partial class ChestItemWear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(name: "wear", table: "chest_item", type: "float", nullable: false, defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "wear", table: "chest_item");
        }
    }
}
