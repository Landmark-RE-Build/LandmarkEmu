using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// A build site keeps all three Default Access masks of ClaimsPacketSetItemBasicPermissions (0xF9/0x2D, writer
    /// FUN_1435e6760: public, group, guild) instead of only the first (trace/0695/audit-wire-b.md finding #4). The two new
    /// columns default to 0, the value the shipped Site Management window sends for them; existing rows stay valid.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261101093700_ClaimGroupGuildPermissions")]
    public partial class ClaimGroupGuildPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(name: "groupPermissions", table: "claim", type: "int(10) unsigned",
                nullable: false, defaultValue: 0u);
            migrationBuilder.AddColumn<uint>(name: "guildPermissions", table: "claim", type: "int(10) unsigned",
                nullable: false, defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "groupPermissions", table: "claim");
            migrationBuilder.DropColumn(name: "guildPermissions", table: "claim");
        }
    }
}
