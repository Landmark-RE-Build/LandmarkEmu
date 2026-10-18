using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The dev commands that run code or raw packets in a game client (<see cref="ClientCodeCommands"/>: lua, luacode, luas,
    /// send) reach only the caller and players who are admins, and may not name the natives that open the player's desktop
    /// web browser, unless the server owner allows both (<see cref="WorldServerConfiguration.DevClientCodeOnOthers"/>,
    /// Docker DEV_CLIENT_CODE_ON_OTHERS; checked in <see cref="Run"/> before the command, by <see cref="ClientCodeTargets"/>).
    /// Until 2026-10-18 an admin could run them against any player online, and client code can call natives such as
    /// Ui.OpenSupportUrl that open a web page on that player's own PC. LandmarkEmulator.Tests DevCommandClientCodeTests.
    /// </summary>
    public static partial class DevCommands
    {
        /// <summary>
        /// A reply to the command's caller: to the log (the channel every dev command answers on: console.log, which the
        /// test harness and the owner read), to <paramref name="replies"/>, and as a system line to the caller when known.
        /// </summary>
        private static void Reply(Source from, List<string> replies, string text)
        {
            replies.Add(text);
            log.Warn($"Dev command ({from}): {text}");
            if (from.Caller is WorldSession caller && !caller.Disconnected)
                Chat.System(caller, text);
        }

        // ---- Client code (lua / luacode / luas / send) ----------------------------------------------------------------

        /// <summary>
        /// The commands that run code in a game client (lua: InteractionRunClientLua; luacode / luas: the first-time event
        /// Script packet, ClientLua) or send it raw packets (send: any packet, those included). Client Lua can call the
        /// client's natives, some of which open the player's own desktop web browser (<see cref="ClientLua.ExternalBrowserNatives"/>).
        /// </summary>
        internal static readonly HashSet<string> ClientCodeCommands = new() { "lua", "luacode", "luas", "send" };

        /// <summary>
        /// The targets a client-code command may reach: the caller themself and players who are admins
        /// (<see cref="Admin.IsAdmin"/>), every target with <see cref="WorldServerConfiguration.DevClientCodeOnOthers"/>.
        /// Code naming a desktop-browser native (<see cref="ClientLua.ExternalBrowserCall"/>; for send, the packet's bytes
        /// read as text) is refused for everyone unless the setting is on. Every use is logged with caller, target and the
        /// start of the code; refusals are replied to the caller. The test harness drives its own characters this way
        /// (its two test characters are admins on the dev world), so it keeps working with the default.
        /// </summary>
        internal static List<WorldSession> ClientCodeTargets(string command, string arguments, List<WorldSession> targets, Source from,
            List<string> replies)
        {
            bool open = ConfigurationManager<WorldServerConfiguration>.Instance.Config?.DevClientCodeOnOthers == true;
            string shown = arguments.Length > 200 ? arguments[..200] + $"… ({arguments.Length} characters)" : arguments;
            if (!open)
            {
                string text = arguments;
                if (command == "send")
                    try
                    {
                        text = System.Text.Encoding.Latin1.GetString(Convert.FromHexString(arguments.Replace(" ", "").Replace("-", "")));
                    }
                    catch (FormatException)
                    {
                        // Not hex: the command itself answers with its usage line.
                    }
                if (ClientLua.ExternalBrowserCall(text) is string native)
                {
                    log.Warn($"Dev {command} from {from} refused for {string.Join(", ", targets.Select(t => t.CharacterName))}: names {native}: {shown}");
                    Reply(from, replies, $"{command}: refused: it names {native}, which opens the player's desktop web browser "
                        + "(setting DEV_CLIENT_CODE_ON_OTHERS is off).");
                    return new List<WorldSession>();
                }
            }
            var allowed = new List<WorldSession>();
            var refused = new List<string>();
            foreach (WorldSession target in targets)
            {
                bool ok = open || target == from.Caller || Admin.IsAdmin(target);
                log.Info($"Dev {command} from {from} for {target.CharacterName}: {(ok ? "sent" : "refused")}: {shown}");
                if (ok)
                    allowed.Add(target);
                else
                    refused.Add(target.CharacterName);
            }
            if (refused.Count > 0)
                Reply(from, replies, $"{command}: refused for {string.Join(", ", refused)}: client code runs only on the caller and on "
                    + "admins (setting DEV_CLIENT_CODE_ON_OTHERS is off).");
            return allowed;
        }
    }
}
