using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The guild tag under a player's name (OPEN-LEADS L196 / docs/observer-sync.md OL-1).
    /// <para>
    /// <b>What the client does [P, 0.695 decompile].</b> A character's nameplate shows its tag (entity +0x21a8) under the
    /// name as <c>PlayerNameWithGuildTag</c> "[*player*]&lt;br&gt;[*guildTag*]" with <c>GuildTagFormat</c> "&lt;[*guildTag*]&gt;"
    /// (CodeStringMappings 9900 / 9586), unless the player switched off the option "Display Player Guild Names"
    /// (UserOptions General.DisplayPlayerGuildNames, default 1 = shown, FUN_142b21620; UI/Default/Data.xml). One setter
    /// writes it, FUN_142ec2610, which rebuilds the nameplate. Its callers:
    /// <list type="bullet">
    /// <item>zone opcode <b>0xDF PlayerUpdatePacketGuildTag</b> {u64 character, string tag} (reader FUN_142c4c0a0, handler
    /// FUN_142cdf330: the character is looked up by guid, unknown guids are ignored);</item>
    /// <item>the full character (LightweightToFullPc, PlayerUpdate dispatcher FUN_142cdfa60 case 0x5a): the blob's third
    /// string (+0xf8), applied unconditionally — an empty string clears the tag;</item>
    /// <item>for the player's own nameplate, the guild's <b>alias</b> (guild core +0xa8) whenever GuildData arrives
    /// (FUN_142eedd60) and when a GuildRename reply changes it (FUN_142ef4ba0: "alias" → UI.GuildTagChanged).</item>
    /// </list>
    /// The alias is the second string of the guild core (reader FUN_142ee4b50: u64 id, string name → +0x08, string alias →
    /// +0xa8, …). The only client request that names an alias, Guild.Rename (FUN_1435b5f90 → GuildRenamePacket 0x83/3),
    /// fills <b>both</b> the name and the alias with the one new name; Guild.Create (FUN_1435b5c40, GuildCreatePacket
    /// 0x83/1) sends the name alone; no 0.589 or 0.695 window has a tag field (the 0.695 admin view's "Tags" box is
    /// Guild.SetTag → GuildUpdateAppDataField 0x83/0x11, the GuildGameData XML's GuildKeywords, 1,024 characters of
    /// search keywords, FUN_1435b61f0 / FUN_143c4f420). So the tag live showed is the guild's name [P client requests;
    /// I that live's server set the alias at creation the same way]. Live did show tags: hotfix 7 Aug 2014 (P1525) moved
    /// them from before the name to underneath it.
    /// </para>
    /// <para>
    /// <b>Ours.</b> The tag is the player's guild name (<see cref="TagOf"/>): the guild core's alias (Guilds.BuildData and
    /// the social service's), the full character's third string (WorldSession.BuildLightweightToFullPc), and 0xDF to the
    /// player's landscape (the player included) whenever their guild changes (<see cref="Changed"/>: create, join, leave,
    /// kick, disband in-process; a changed SocialStateForZone with the social service). Bandwidth: one packet per player on
    /// the landscape per membership change; nothing at introductions (the full character carries it).
    /// </para>
    /// </summary>
    public static class GuildTags
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The tag shown under the character's name: its guild's name, "" when in no guild.</summary>
        public static string TagOf(ulong character)
        {
            IGuildCache guilds = SocialServices.GuildCache;
            ulong guild = guilds.GuildIdOf(character);
            return guild == 0 ? "" : guilds.GuildName(guild) ?? "";
        }

        /// <summary>The 0xDF body after the opcode: u64 character, string tag (u32 length + bytes).</summary>
        public static byte[] Body(ulong character, string tag)
        {
            var output = new List<byte>(12 + (tag?.Length ?? 0));
            var w = new GamePacketWriter(output);
            w.Write(character);
            w.Write(tag ?? "");
            return output.ToArray();
        }

        /// <summary>
        /// The character's guild changed: everyone on its landscape (the player too, whose own nameplate also follows the
        /// GuildData alias) is sent the new tag. Nothing when the character is not online here.
        /// </summary>
        public static void Changed(ulong character)
        {
            if (PlayerManager.FindByGuid(character) is not WorldSession player)
                return;
            string tag = TagOf(character);
            byte[] body = Body(character, tag);
            List<WorldSession> viewers = PlayerManager.InZone(player.ZoneId);
            if (!viewers.Contains(player))
                viewers.Add(player);
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.UpdateGuildTag, body);
            log.Debug($"{player.CharacterName}: guild tag \"{tag}\" to {viewers.Count} client(s).");
        }
    }
}
