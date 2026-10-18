using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>The guild window's News tab: each guild keeps its newest log rows (Phase E, E.5).</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261029090000_GuildLog")]
    public partial class GuildLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "log", table: "guild", type: "mediumtext", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "log", table: "guild");
        }
    }
}
