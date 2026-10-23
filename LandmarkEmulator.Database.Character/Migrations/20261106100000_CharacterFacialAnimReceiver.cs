using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Per-character SOEmote facial-anim receiver preference (SendSelf +0x285; updated by ClientFacialAnimSettings 0xF3
    /// from <c>/soemote receiver</c>). Default on so new characters match the client ctor / live default.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261106100000_CharacterFacialAnimReceiver")]
    public partial class CharacterFacialAnimReceiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "facialAnimReceiver", table: "character", type: "tinyint(1)",
                nullable: false, defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "facialAnimReceiver", table: "character");
        }
    }
}
