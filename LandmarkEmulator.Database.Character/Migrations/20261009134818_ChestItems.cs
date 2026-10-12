using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class ChestItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chest_item",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    chestGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    itemDefinitionId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    count = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    slot = table.Column<uint>(type: "int(10) unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "chestGuid",
                table: "chest_item",
                column: "chestGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chest_item");
        }
    }
}
