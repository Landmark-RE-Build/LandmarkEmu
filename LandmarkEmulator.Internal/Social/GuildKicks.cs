using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// GuildKickPacket (GuildBase 0x83 sub 0x0C), shared by both places that carry it out — the world's own guild code
    /// when the social service runs in-process (WorldServer Game/Guilds.cs) and the social service
    /// (SocialServer/GuildService.cs) when SOCIAL_ADDRESS is set — so both find the member the same way
    /// (docs/request-variants.md).
    /// <para>
    /// Body after the u16 sub: u64 guild, u64 member, string name [P]. The 0.695 client fills it two ways:
    /// <list type="bullet">
    /// <item>the guild roster's right-click menu (RadialMenuButtons row 44 "Kick From Guild", menu 6) runs the Lua native
    /// Guild.Kick (FUN_1435b7ce0) with the member's guid as its only argument: {selected guild (+0x80 of the guild
    /// entry), member guid, ""}.</item>
    /// <item>the slash commands /guildkick and /guildremove (both ClientCommandProcessor::CommandGuildKickPlayer,
    /// registered in FUN_143396930; body FUN_1433abf90) send the typed name: {current guild, DAT_145462af8 (the
    /// client's empty guid, 0: .bss, never written), name}.</item>
    /// </list>
    /// Both check KickFromGuild on the client first (else Guild.InsufficientRank). Before this helper both servers read
    /// only the guid, so a typed /guildkick Name did nothing but "general failure".
    /// </para>
    /// </summary>
    public static class GuildKicks
    {
        /// <summary>GuildBase sub of GuildKickPacket.</summary>
        public const ushort KickSub = 0x0C;

        /// <summary>What a GuildKickPacket carries.</summary>
        public sealed record Request(ulong GuildId, ulong MemberGuid, string Name)
        {
            /// <summary>True when the member is named by guid (the roster menu path).</summary>
            public bool ByGuid => MemberGuid != 0;

            /// <summary>How the request names the member, for logs.</summary>
            public string Describe() => ByGuid ? $"guid 0x{MemberGuid:X}" : $"name '{Name}'";
        }

        /// <summary>Reads the body after the u16 sub. False when it runs short.</summary>
        public static bool TryRead(byte[] body, out Request request)
        {
            request = null;
            if (body == null)
                return false;
            try
            {
                var r = new InternalReader(body);
                ulong guild = r.U64();
                ulong member = r.U64();
                string name = r.Remaining > 0 ? (r.Str() ?? "").Trim() : "";
                request = new Request(guild, member, name);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        /// <summary>Writes a body the way the client does (tests, tools).</summary>
        public static byte[] Write(ulong guild, ulong member, string name)
            => new InternalWriter().U64(guild).U64(member).Str(name ?? "").ToArray();

        /// <summary>
        /// The guild member the request names, from the kicker's guild roster (character guid → name): by guid when it is
        /// set, else the member whose name matches the typed one (case-insensitive). 0 when none matches.
        /// </summary>
        public static ulong Resolve(Request request, IEnumerable<KeyValuePair<ulong, string>> members)
        {
            if (request == null || members == null)
                return 0;
            foreach (KeyValuePair<ulong, string> member in members)
            {
                if (request.ByGuid ? member.Key == request.MemberGuid
                    : request.Name.Length > 0 && string.Equals(member.Value?.Trim(), request.Name, StringComparison.OrdinalIgnoreCase))
                    return member.Key;
            }
            return 0;
        }
    }
}
