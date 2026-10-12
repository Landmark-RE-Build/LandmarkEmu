using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Build site vendor listings: {id, vendor prop, owner, item, count, price, fee paid, listed at}.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261016090000_ClaimVendorItem")]
    public partial class ClaimVendorItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "claim_vendor_item",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    vendorId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    ownerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    itemDefinitionId = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    count = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    price = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    feePaid = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    listedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "vendorId", table: "claim_vendor_item", column: "vendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "claim_vendor_item");
        }
    }
}
