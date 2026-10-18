using System;
using LandmarkEmulator.Database.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Auth.Migrations
{
    /// <summary>
    /// Ticket hardening: when a server ticket was issued and for which world, so a ticket can't be kept for later (to skip
    /// a login queue) or used on another world.
    /// </summary>
    [DbContext(typeof(AuthContext))]
    [Migration("20261031100000_AccountServerTicketBinding")]
    public partial class AccountServerTicketBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "serverTicketIssuedAt", table: "account", type: "datetime", nullable: true);
            migrationBuilder.AddColumn<uint>(name: "serverTicketWorld", table: "account", type: "int(10) unsigned", nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "serverTicketIssuedAt", table: "account");
            migrationBuilder.DropColumn(name: "serverTicketWorld", table: "account");
        }
    }
}
