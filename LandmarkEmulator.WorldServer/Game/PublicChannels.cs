using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Membership of the public chat channels (wire channel 9; trace/0695/agent-chat.md). The 0.695 client knows one,
    /// General (public id 0, <see cref="Chat.GeneralChannelId"/>), and puts every player in it by itself at each login
    /// (FUN_143367e40 → FUN_14335e8e0), so the server keeps the exceptions: the characters who left it.
    /// <list type="bullet">
    /// <item>C→S Command 0x1E sub 0x22 CommandPacketChatChannelOn / 0x23 CommandPacketChatChannelOff {string channel}
    /// (World channel; built only by the Lua natives Ui.ChannelOn FUN_1435cb4b0 / Ui.ChannelOff FUN_1435cb760, writer
    /// FUN_142c12ce0 + string). The client has no answer class for either (RTTI), so the server sends nothing back.</item>
    /// <item>Dev posts: General is a joinable channel with server-kept membership (P1826, P1893, P4311) that can be turned
    /// off in Chat Options (P5310). No 0.695 script, AS3 window or native caller sends either request (agent-chat.md §1:
    /// the natives are reached only through the Lua binding table), so today nothing in the shipped client leaves
    /// General; the server still honours the requests the way the dev posts describe.</item>
    /// <item>Membership lasts for the session [I]: the client re-registers General at every login, so a player who
    /// left it is back in at the next login (a Chat Options switch would send ChannelOff again then).</item>
    /// </list>
    /// Part of the social service: <see cref="Social.LocalSocialService"/> runs this; with SOCIAL_ADDRESS the social
    /// service (LandmarkEmulator.SocialServer ChatService) keeps the same list for every world.
    /// </summary>
    public static class PublicChannels
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort ChannelOnSub = 0x22, ChannelOffSub = 0x23;

        private static readonly object sync = new();
        /// <summary>Characters (guid) who left General this session.</summary>
        private static readonly HashSet<ulong> leftGeneral = new();

        /// <summary>Is the player in General (everyone is, unless they left it)?</summary>
        public static bool IsMember(WorldSession session)
        {
            lock (sync)
                return !leftGeneral.Contains(session.PlayerGuid);
        }

        /// <summary>ChatChannelOn (0x22) / ChatChannelOff (0x23): join or leave a public channel by name.</summary>
        public static void Handle(WorldSession session, ushort sub, byte[] body)
        {
            string name;
            try
            {
                name = new GamePacketReader(body).ReadString().Trim();
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable chat channel request 0x{sub:X} from {session.CharacterName}: {e.Message}");
                return;
            }
            if (!IsGeneral(name))
            {
                log.Debug($"{session.CharacterName} asked to turn {(sub == ChannelOnSub ? "on" : "off")} unknown channel '{name}': ignored.");
                return;
            }
            bool changed;
            lock (sync)
                changed = sub == ChannelOnSub ? leftGeneral.Remove(session.PlayerGuid) : leftGeneral.Add(session.PlayerGuid);
            if (changed)
                log.Info($"{session.CharacterName} {(sub == ChannelOnSub ? "joined" : "left")} {Chat.GeneralChannelName}.");
        }

        /// <summary>The player left the world: back to the default (in General) for their next login.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                leftGeneral.Remove(session.PlayerGuid);
        }

        /// <summary>
        /// The name the client gives General (FUN_143b67f50(0) → "General"; its own comparisons ignore case, FUN_142b50af0).
        /// </summary>
        public static bool IsGeneral(string name) => string.Equals(name, Chat.GeneralChannelName, StringComparison.OrdinalIgnoreCase);
    }
}
