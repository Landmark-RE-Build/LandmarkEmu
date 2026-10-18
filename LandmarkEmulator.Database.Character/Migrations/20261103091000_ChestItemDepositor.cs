using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Who put each chest item in (trace/0695/audit-wiki-gaps.md #2): <c>chest_item.depositorGuid</c>. A non-tradeable
    /// item comes out only for its depositor, as the client's text 561999 says ("Can't take non-tradeable items that
    /// didn't originally belong to you.", TransferItemResponse 0x21). Existing rows start at 0: their chest's owner counts
    /// as the depositor.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261103091000_ChestItemDepositor")]
    public partial class ChestItemDepositor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(name: "depositorGuid", table: "chest_item", type: "bigint(20) unsigned",
                nullable: false, defaultValue: 0ul);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "depositorGuid", table: "chest_item");
        }
    }
}
