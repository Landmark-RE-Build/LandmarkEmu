using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Guild window edits: the guild's description, its app data (GuildGameData XML: emblem, recruiting, keywords /
    /// tag) and its ranks (JSON list of {id, level, name, permissions}; empty = the three default ranks).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261021090000_GuildDetails")]
    public partial class GuildDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "description", table: "guild", type: "varchar(1024)", nullable: false,
                defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "appData", table: "guild", type: "varchar(2048)", nullable: false,
                defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "ranks", table: "guild", type: "text", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "description", table: "guild");
            migrationBuilder.DropColumn(name: "appData", table: "guild");
            migrationBuilder.DropColumn(name: "ranks", table: "guild");
        }
    }
}
