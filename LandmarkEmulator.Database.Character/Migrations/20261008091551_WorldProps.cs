using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class WorldProps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "world_prop",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    propId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    modelId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    nameId = table.Column<uint>(type: "int(10) unsigned", nullable: false, defaultValue: 0u),
                    ownerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    positionX = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    positionY = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    positionZ = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    rotationX = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    rotationY = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    rotationZ = table.Column<float>(type: "float", nullable: false, defaultValue: 0f),
                    rotationW = table.Column<float>(type: "float", nullable: false, defaultValue: 1f),
                    scale = table.Column<float>(type: "float", nullable: false, defaultValue: 1f)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ownerGuid",
                table: "world_prop",
                column: "ownerGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "world_prop");
        }
    }
}
