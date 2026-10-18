using System;
using LandmarkEmulator.Database.Auth;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Auth.Migrations
{
    /// <summary>
    /// Membership and the trial key on the account (TASKS 3.12; trace/0695/agent-membership.md): the client's membership
    /// type (0 none, 1 trial, 2 member) and when it ends, whether the account's one trial key has been mailed to it, and
    /// whether it has used a trial key. Kept with the account (live's membership was an account state; the login server
    /// reads it for the login queue's member priority, the world servers for SendSelf / PacketMembershipActivation).
    /// </summary>
    [DbContext(typeof(AuthContext))]
    [Migration("20261030100000_AccountMembership")]
    public partial class AccountMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(name: "memberType", table: "account", type: "tinyint(3) unsigned", nullable: false,
                defaultValue: (byte)0);
            migrationBuilder.AddColumn<DateTime>(name: "memberUntil", table: "account", type: "datetime", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "trialKeyMailed", table: "account", type: "tinyint(1)", nullable: false,
                defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "trialRedeemed", table: "account", type: "tinyint(1)", nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "memberType", table: "account");
            migrationBuilder.DropColumn(name: "memberUntil", table: "account");
            migrationBuilder.DropColumn(name: "trialKeyMailed", table: "account");
            migrationBuilder.DropColumn(name: "trialRedeemed", table: "account");
        }
    }
}
