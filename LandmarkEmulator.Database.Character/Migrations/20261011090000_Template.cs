using System;
using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Saved designs (templates).</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261011090000_Template")]
    public partial class Template : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "template",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                    ownerGuid = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    authorName = table.Column<string>(type: "varchar(64)", nullable: false, defaultValue: ""),
                    name = table.Column<string>(type: "varchar(128)", nullable: false, defaultValue: ""),
                    tags = table.Column<string>(type: "varchar(512)", nullable: false, defaultValue: ""),
                    sizeX = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    sizeY = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    sizeZ = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    materials = table.Column<string>(type: "varchar(1024)", nullable: false, defaultValue: ""),
                    blocks = table.Column<byte[]>(type: "longblob", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ownerGuid",
                table: "template",
                column: "ownerGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "template");
        }
    }
}
