using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// A build site's Play Mode survives a server restart (launch notes P4248 / P4351, TASKS 6.56).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261024090000_ClaimPlayMode")]
    public partial class ClaimPlayMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "playMode", table: "claim", type: "tinyint(1)", nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "playMode", table: "claim");
        }
    }
}
