using LandmarkEmulator.Database.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Auth.Migrations
{
    /// <summary>
    /// Global admin list in the auth database (account: / character: / bare entries). SQL-managed; .env ADMIN_* stays as
    /// an optional bootstrap overlay.
    /// </summary>
    [DbContext(typeof(AuthContext))]
    [Migration("20261101100000_AdminEntries")]
    public partial class AdminEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    entry = table.Column<string>(type: "varchar(255)", nullable: false, defaultValue: ""),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "entry",
                table: "admin",
                column: "entry",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "admin");
        }
    }
}
