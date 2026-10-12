using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <inheritdoc />
    public partial class Claims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "claim",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    ownerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false, defaultValue: 0ul),
                    name = table.Column<string>(type: "varchar(64)", nullable: true, defaultValue: "")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    minX = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    minY = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    minZ = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    maxX = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    maxY = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0),
                    maxZ = table.Column<double>(type: "double", nullable: false, defaultValue: 0.0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ownerGuid",
                table: "claim",
                column: "ownerGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim");
        }
    }
}
