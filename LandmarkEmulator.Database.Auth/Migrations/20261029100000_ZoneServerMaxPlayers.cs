using LandmarkEmulator.Database.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Auth.Migrations
{
    /// <summary>
    /// The world's player limit beside its heartbeat, so the login server can queue logins into a full world
    /// (TASKS E.8; AuthServer LoginQueue).
    /// </summary>
    [DbContext(typeof(AuthContext))]
    [Migration("20261029100000_ZoneServerMaxPlayers")]
    public partial class ZoneServerMaxPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(name: "maxPlayers", table: "zone_server", type: "int(10) unsigned", nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "maxPlayers", table: "zone_server");
        }
    }
}
