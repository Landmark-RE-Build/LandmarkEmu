using System;
using LandmarkEmulator.Database.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Auth.Migrations
{
    /// <summary>When an account last got the member Lumen stipend (member benefits, user decision 2026-10-17).</summary>
    [DbContext(typeof(AuthContext))]
    [Migration("20261030110000_AccountMemberStipend")]
    public partial class AccountMemberStipend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(name: "memberStipendAt", table: "account", type: "datetime", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "memberStipendAt", table: "account");
        }
    }
}
