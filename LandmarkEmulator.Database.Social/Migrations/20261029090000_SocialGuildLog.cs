using LandmarkEmulator.Database.Social;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Social.Migrations
{
    /// <summary>The guild window's News tab in the shared social service (as the world server's GuildLog).</summary>
    [DbContext(typeof(SocialContext))]
    [Migration("20261029090000_SocialGuildLog")]
    public partial class SocialGuildLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "log", table: "social_guild", type: "mediumtext", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "log", table: "social_guild");
        }
    }
}
