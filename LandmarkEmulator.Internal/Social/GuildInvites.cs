using System;
using System.IO;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// GuildInvitePacket (GuildBase 0x83 sub 4), shared by both places that carry it out — the world's own guild code
    /// when the social service runs in-process (WorldServer Game/Guilds.cs) and the social service
    /// (SocialServer/GuildService.cs) when SOCIAL_ADDRESS is set — so both find the invitee the same way.
    /// <para>
    /// Body after the u16 sub: u64 guild, u64 invitee, string name [P]. The client's only sender is the Lua native
    /// Guild.Invite (FUN_1435b66e0, 0.695), which fills it two ways:
    /// <list type="bullet">
    /// <item>three Lua arguments (guild key, invitee, name): u64 guild = argument 1, u64 invitee = argument 2 parsed as an
    /// unsigned number (FUN_143c9f460: tostring, a leading '-' or nothing → 0), name = argument 3. The Guild window's
    /// roster "Invite" box calls GuildWrapper.Invite(guildKey, "", typed name) (re/as3_695 RosterView.as:566), so it
    /// sends {guild, 0, name}.</item>
    /// <item>one or more arguments with a number first (FUN_1439cee90): u64 guild = the player's current guild
    /// (FUN_142ef1200, its id at +0x80), u64 invitee = argument 1, name empty. The right-click radial buttons run their
    /// LUA_CALL this way: FUN_14372d940 (button click, via RadialMenu.ButtonClicked FUN_1435fd060 → FUN_143444a60 →
    /// FUN_143730060) calls the row's function with param 1 = the menu's target guid (ClientLuaParam type 5) and param 2
    /// = a name string from the target actor; Guild.Invite reads only argument 1 on this path. RadialMenuButtons rows 57
    /// (menu 3, another player) and 58 (menu 4, a friend) are Guild.Invite, so they send {current guild, target guid,
    /// ""}.</item>
    /// </list>
    /// Both paths need CreateGuildInvite (else the client's own Guild.InsufficientRank) and a guild under
    /// Guild.MaxMembers (else Guild.TooManyMembers) before anything is sent.
    /// </para>
    /// The server therefore finds the invitee by guid when the guid is set and by name otherwise (bug 2026-10-18: the
    /// radial menu's invite was looked up by its empty name, "Guild invite failed.  Player not found.").
    /// </summary>
    public static class GuildInvites
    {
        /// <summary>GuildBase sub of GuildInvitePacket.</summary>
        public const ushort InviteSub = 4;

        /// <summary>What a GuildInvitePacket carries.</summary>
        public sealed record Request(ulong GuildId, ulong InviteeGuid, string Name)
        {
            /// <summary>True when the invitee is named by guid (the radial menu path).</summary>
            public bool ByGuid => InviteeGuid != 0;

            /// <summary>How the request names the invitee, for logs.</summary>
            public string Describe() => ByGuid ? $"guid 0x{InviteeGuid:X}" + (Name.Length > 0 ? $" ('{Name}')" : "") : $"name '{Name}'";
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
                ulong invitee = r.U64();
                string name = r.Remaining > 0 ? (r.Str() ?? "").Trim() : "";
                request = new Request(guild, invitee, name);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        /// <summary>Writes a body the way the client does (tests, tools).</summary>
        public static byte[] Write(ulong guild, ulong invitee, string name)
            => new InternalWriter().U64(guild).U64(invitee).Str(name ?? "").ToArray();

        /// <summary>
        /// The invitee the request names: by guid when it is set (the name is then not used for the lookup), else by
        /// name; nothing for an empty name and no guid. The caller still checks the invitee is online.
        /// </summary>
        public static T Resolve<T>(Request request, Func<ulong, T> byGuid, Func<string, T> byName) where T : class
        {
            if (request == null)
                return null;
            if (request.ByGuid)
                return byGuid(request.InviteeGuid);
            return request.Name.Length > 0 ? byName(request.Name) : null;
        }

        /// <summary>
        /// The name to put in a failure notification: the invitee's known name (a guid lookup that found the character
        /// but not online), else the typed name. The client's "Player not found." line (code 5) does not show it, but
        /// the notification's player field stays filled for the lines that do.
        /// </summary>
        public static string ShownName(Request request, string knownName)
            => !string.IsNullOrEmpty(knownName) ? knownName : request?.Name ?? "";
    }
}
