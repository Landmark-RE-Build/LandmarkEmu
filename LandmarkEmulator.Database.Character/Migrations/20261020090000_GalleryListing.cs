using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>Build site tags, and Player Studio products for build sites (the gallery): the product's item type.</summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261020090000_GalleryListing")]
    public partial class GalleryListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "tags", table: "claim", type: "varchar(512)", nullable: false,
                defaultValue: "");
            migrationBuilder.AddColumn<uint>(name: "itemType", table: "player_studio_product", type: "int(10) unsigned",
                nullable: false, defaultValue: 2u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "tags", table: "claim");
            migrationBuilder.DropColumn(name: "itemType", table: "player_studio_product");
        }
    }
}
