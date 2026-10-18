using LandmarkEmulator.Database.Character;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LandmarkEmulator.Database.Character.Migrations
{
    /// <summary>
    /// Client preferences (Game.Preferences; trace/0695/agent-s2c-sweep.md §5 #1): <c>character_preference</c> holds the
    /// XML document the 0.695 client sends in PreferencePacketClientXml (zone opcode 0x03, {u32 kind = 1, u32 len,
    /// char[len] xml}; writer FUN_142c13420) — chat window positions and sizes and the action bar lock — and the server
    /// sends it back in BasePreferencePacket at the next login. One row per character and kind, overwritten; the XML is
    /// kept byte for byte (blob; capped at 16 KB by the server).
    /// </summary>
    [DbContext(typeof(CharacterContext))]
    [Migration("20261104090000_CharacterPreference")]
    public partial class CharacterPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_preference",
                columns: table => new
                {
                    characterId = table.Column<ulong>(type: "bigint(20) unsigned", nullable: false),
                    kind = table.Column<uint>(type: "int(10) unsigned", nullable: false),
                    xml = table.Column<byte[]>(type: "blob", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table => table.PrimaryKey("PRIMARY", x => new { x.characterId, x.kind }))
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "character_preference");
        }
    }
}
