using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Comments on gallery entries (the Showcase gallery's comments area, launch notes P4139 / P4686).</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261027090000_GalleryComment")]
    public partial class GalleryComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gallery_comment",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    claimId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    authorGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    authorName = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: ""),
                    text = table.Column<string>(type: "varchar(512)", nullable: false, defaultValue: ""),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => x.id))
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.CreateIndex(name: "claimId", table: "gallery_comment", column: "claimId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "gallery_comment");
        }
    }
}
