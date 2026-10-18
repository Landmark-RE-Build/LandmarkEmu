using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// "[player] is not online": the answer when something a player aimed at another player can't reach them (OPEN-LEADS
    /// L175).
    /// <para>The client's own packet is <b>PacketTargetClientNotOnline 0x1A</b> (wire 1A 00; zone dispatcher case 6, reader
    /// FUN_142c4af20, consumed exactly) [P]: u64 (+0x10, the target's guid [I]: the handler doesn't read it), string name
    /// (StringFixed&lt;32&gt;, +0x18). (The older note "{StringFixed&lt;32&gt; name}" missed the u64.) With a non-empty name the
    /// handler takes CodeStringMappings PlayerNotOnline (text 560709 "[*player*] is not online at this."), and uses it as
    /// a printf format for the name, then prints it as a chat line. Two client quirks follow [P]: the text has no %s, so
    /// the name sent is never shown; and the string provider (ClientStringProvider vtable +0x10, FUN_142fdd4b0) expands
    /// [*player*] with the <i>local</i> player (rdx = the player object, DAT_1454319c0 + 0x282c8, at 0x142fdd4e7), so the
    /// line reads "&lt;your own name&gt; is not online at this.".</para>
    /// <para>It answers ClientToTargetClientsZoneWrapped 0x19, a server-internal routing message (no client sender), so no
    /// source shows when live sent it. Here the same client text goes out as a system line with the target's name in
    /// [*player*] (PacketShowSystemMessage 0x69 fills the token from its parameter: 0.695 case 0x2f, FUN_142d8b2b0 with
    /// the packet's parameters only), for: Teleport to Friend when the friend is offline, a trade invitation to someone
    /// no longer here, and Inspect of someone no longer here. Setting <c>TargetNotOnlinePacket</c> (default false) sends
    /// the client's 0x1A instead, quirks and all.</para>
    /// </summary>
    public static class TargetNotOnline
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>CodeStringMappings PlayerNotOnline: "[*player*] is not online at this."</summary>
        public const uint PlayerNotOnlineText = 560709;

        /// <summary>The client keeps the name in a StringFixed&lt;32&gt;.</summary>
        public const int MaxNameBytes = 31;

        /// <summary>TargetNotOnlinePacket: send the client's own 0x1A (it names the reader, not the target) instead of the system line.</summary>
        public static bool UsePacket =>
            LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("TargetNotOnlinePacket", false) ?? false;

        /// <summary>PacketTargetClientNotOnline body: u64 target, string name.</summary>
        public static byte[] Body(ulong target, string name)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(target);
            w.Write(Broadcasts.Fit(name, MaxNameBytes));
            return output.ToArray();
        }

        /// <summary>
        /// Tells <paramref name="session"/> that the player <paramref name="target"/> (name looked up when not given) is not
        /// online. Returns false (nothing sent) when no name can be found (an id that is no character).
        /// </summary>
        public static bool Tell(WorldSession session, ulong target, string name = null)
        {
            if (session == null || session.Disconnected)
                return false;
            if (string.IsNullOrWhiteSpace(name))
                name = NameOf(target);
            if (string.IsNullOrWhiteSpace(name))
            {
                log.Debug($"{session.CharacterName}: target 0x{target:X} is not online and has no name; nothing shown.");
                return false;
            }
            if (UsePacket)
                session.EnqueueMessage(ZoneMessageOpcode.TargetClientNotOnline, Body(target, name));
            else
                SystemMessages.Show(session, PlayerNotOnlineText, ("player", name));
            return true;
        }

        /// <summary>A character's name from the database (null when there is none or the database is away).</summary>
        public static string NameOf(ulong character)
        {
            if (character == 0)
                return null;
            try
            {
                return DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(new[] { character }).GetValueOrDefault(character);
            }
            catch (Exception e)
            {
                log.Warn($"Name of character 0x{character:X} not found ({e.GetBaseException().Message}).");
                return null;
            }
        }
    }
}
