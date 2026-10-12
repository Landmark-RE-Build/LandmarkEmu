using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Placed props keep their tint (build tray tint tab).</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261010180000_PropTint")]
    public partial class PropTint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tintAlias",
                table: "world_prop",
                type: "varchar(64)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tintAlias",
                table: "world_prop");
        }
    }
}
