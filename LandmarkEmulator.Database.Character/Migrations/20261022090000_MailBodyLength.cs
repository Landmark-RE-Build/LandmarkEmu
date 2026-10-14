using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Mail bodies as long as the client's compose window allows (client constant Mail.MaxBodyCharLength 4096).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261022090000_MailBodyLength")]
    public partial class MailBodyLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(name: "body", table: "mail", type: "varchar(4096)", nullable: false,
                defaultValue: "", oldClrType: typeof(string), oldType: "varchar(2048)", oldDefaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(name: "body", table: "mail", type: "varchar(2048)", nullable: false,
                defaultValue: "", oldClrType: typeof(string), oldType: "varchar(4096)", oldDefaultValue: "");
        }
    }
}
