using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Placed props' settings (timer length, dice odds...): {prop, key, value}.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261015090000_WorldPropSetting")]
    public partial class WorldPropSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "world_prop_setting",
                columns: table => new
                {
                    propId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    key = table.Column<string>(type: "varchar(64)", nullable: false),
                    value = table.Column<float>(type: "float", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.propId, x.key }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "world_prop_setting");
        }
    }
}
