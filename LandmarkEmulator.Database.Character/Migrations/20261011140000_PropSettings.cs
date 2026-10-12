using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Placed props keep a custom name, sign text and an on/off switch.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261011140000_PropSettings")]
    public partial class PropSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customName",
                table: "world_prop",
                type: "varchar(128)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "text",
                table: "world_prop",
                type: "varchar(1024)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "disabled",
                table: "world_prop",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "customName", table: "world_prop");
            migrationBuilder.DropColumn(name: "text", table: "world_prop");
            migrationBuilder.DropColumn(name: "disabled", table: "world_prop");
        }
    }
}
