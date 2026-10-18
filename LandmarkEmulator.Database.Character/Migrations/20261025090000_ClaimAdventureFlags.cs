using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// A build site's Play Mode Rules (0.695 "Adventure flags", ClaimsPacketSetAdventureFlags 0xF9/0x45) are kept on the
    /// claim (TASKS 2.1; trace/0695/plan-storytools-next.md Spec 10). 0 = no rule set.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261025090000_ClaimAdventureFlags")]
    public partial class ClaimAdventureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(name: "adventureFlags", table: "claim", type: "bigint(20) unsigned",
                nullable: false, defaultValue: 0ul);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "adventureFlags", table: "claim");
        }
    }
}
