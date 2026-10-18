using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// The character's chat bubble colours and size (TASKS E.14, trace/0695/agent-chat.md §2): CommandPacketSetChatBubbleColor
    /// 0x1E/0x15 {u32 fg, u32 bg, u32 size, u64 guid}. Defaults are the client's own for a new character entity
    /// (FUN_142e88840: 0xFFFFFF, 0xD4E2F0, 1).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261031141400_ChatBubbleColor")]
    public partial class ChatBubbleColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(name: "chatBubbleFg", table: "character", type: "int(10) unsigned",
                nullable: false, defaultValue: 0xFFFFFFu);
            migrationBuilder.AddColumn<uint>(name: "chatBubbleBg", table: "character", type: "int(10) unsigned",
                nullable: false, defaultValue: 0xD4E2F0u);
            migrationBuilder.AddColumn<uint>(name: "chatBubbleSize", table: "character", type: "int(10) unsigned",
                nullable: false, defaultValue: 1u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "chatBubbleFg", table: "character");
            migrationBuilder.DropColumn(name: "chatBubbleBg", table: "character");
            migrationBuilder.DropColumn(name: "chatBubbleSize", table: "character");
        }
    }
}
