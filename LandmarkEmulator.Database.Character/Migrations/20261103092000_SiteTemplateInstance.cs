using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Placed Player Studio designs, live's "template instances" (trace/0695/audit-wiki-gaps.md #5; client text 563373
    /// Construction.OnlyClaimOwnerMayPlaceOrCopyTemplateInstances): <c>site_template_instance</c>, one row per placement
    /// (landscape, site, design, placer, box in world units). Copy and Save Design refuse a non-owner whose selection
    /// overlaps one. World data: created in the world's own database too.
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261103092000_SiteTemplateInstance")]
    public partial class SiteTemplateInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "site_template_instance",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    zoneId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    siteGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    templateGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    placedBy = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    minX = table.Column<double>(type: "double", nullable: false),
                    minY = table.Column<double>(type: "double", nullable: false),
                    minZ = table.Column<double>(type: "double", nullable: false),
                    maxX = table.Column<double>(type: "double", nullable: false),
                    maxY = table.Column<double>(type: "double", nullable: false),
                    maxZ = table.Column<double>(type: "double", nullable: false),
                    placedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "site", table: "site_template_instance", columns: new[] { "zoneId", "siteGuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "site_template_instance");
        }
    }
}
