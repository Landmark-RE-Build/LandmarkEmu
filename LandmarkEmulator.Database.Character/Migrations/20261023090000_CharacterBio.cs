using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// The player's profile text (0.695 client: PlayerUpdate SetPlayerBio, shown in the inventory window and to
    /// players who inspect them; at most 512 characters, client constant BioLengthMax).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261023090000_CharacterBio")]
    public partial class CharacterBio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "bio", table: "character", type: "varchar(512)", nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "bio", table: "character");
        }
    }
}
