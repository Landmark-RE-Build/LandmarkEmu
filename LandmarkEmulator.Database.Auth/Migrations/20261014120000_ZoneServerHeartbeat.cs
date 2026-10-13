using System;
using LandmarkEmulator.Database.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Auth.Migrations
{
    /// <summary>
    /// World server heartbeat: the world server stamps its zone_server row every few seconds (and the players online),
    /// so the login server can list the world as up or down at character select.
    /// </summary>
    [DbContext(typeof(AuthContext))]
    [Migration("20261014120000_ZoneServerHeartbeat")]
    public partial class ZoneServerHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "heartbeat", table: "zone_server", type: "datetime", nullable: true);
            migrationBuilder.AddColumn<uint>(name: "playersOnline", table: "zone_server", type: "int(10) unsigned", nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "heartbeat", table: "zone_server");
            migrationBuilder.DropColumn(name: "playersOnline", table: "zone_server");
        }
    }
}
