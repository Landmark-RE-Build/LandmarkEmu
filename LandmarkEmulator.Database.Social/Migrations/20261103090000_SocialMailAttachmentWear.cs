using LandmarkEmulator.Database.Social;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Social.Migrations
{
    /// <summary>
    /// A mailed item keeps its wear (trace/0695/audit-wiki-gaps.md #1): <c>social_mail_attachment.wear</c>, the attached
    /// item's durability points lost (as the world's character_item.wear). Existing rows start at 0 (unworn).
    /// </summary>
    [DbContext(typeof(SocialContext))]
    [Migration("20261103090000_SocialMailAttachmentWear")]
    public partial class SocialMailAttachmentWear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(name: "wear", table: "social_mail_attachment", type: "float", nullable: false, defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "wear", table: "social_mail_attachment");
        }
    }
}
