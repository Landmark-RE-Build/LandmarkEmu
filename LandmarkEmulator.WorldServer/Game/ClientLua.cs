using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Runs a Lua function in a player's client: Command 0x1E sub 0x53 InteractionRunClientLua (reader FUN_1432d5aa0,
    /// handled by FUN_143311920): string function name, then three f32 arguments. The client calls the function by name
    /// (FUN_142e3d110, as it does for its own "HudStateManager:OnOpenBarberShop") with the three numbers. A "Table:Method"
    /// name is a method call (the table is passed as self). This is how the server can drive the client's own UI:
    /// open windows, reset state the UI scripts leave behind.
    /// </summary>
    public static class ClientLua
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const ushort RunClientLuaSub = 0x53;

        public static void Run(WorldSession session, string function, float a = 0f, float b = 0f, float c = 0f)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(RunClientLuaSub);
            writer.Write(function);
            writer.Write(a);
            writer.Write(b);
            writer.Write(c);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
            log.Debug($"{session.CharacterName}: client Lua {function}({a}, {b}, {c}).");
        }

        /// <summary>
        /// Runs a Lua function with any arguments (strings; the client turns each into a Lua value), through the
        /// first-time event Script packet (FirstTimeEventBase sub 5, FUN_143580670 → FUN_142e3fdf0): the name is split
        /// at its first '.' into table and function. A Lua-defined table function gets exactly these arguments, no
        /// self (InGameMarketplaceBrowser.ShowURL got its URL as self: "attempt to index a string value (local
        /// 'self')"); a native table function gets the table first (Ui.DoCommand saw (table, "Ui"), probe
        /// re/test/scenarios/adhoc/docmd.js); a name without a '.' is a global function with just the arguments.
        /// The arguments travel as one comma-separated string, so none may contain a comma. Event id -1 with clear =
        /// false leaves the client's first-time state alone (FUN_1435802c0 only acts on -1 when clearing). While the
        /// client is loading it queues the call and runs it afterwards.
        /// </summary>
        public static void RunWithArgs(WorldSession session, string function, params string[] args)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)5);
            writer.Write(function);
            writer.Write(string.Join(",", args));
            writer.Write(uint.MaxValue);
            writer.Write(false);
            session.EnqueueMessage(ZoneMessageOpcode.FirstTimeEventBase, output.ToArray());
            log.Debug($"{session.CharacterName}: client Lua {function}({string.Join(", ", args)}).");
        }

        /// <summary>
        /// Runs Lua code in the client: the global BindCommandToLua (FUN_142e3beb0, a plain call, so it gets exactly our
        /// two arguments) binds a console command "lmemuN" to the code (console commands are lower case), then
        /// Ui.ProcessChatCommand("/lmemuN") (FUN_14351cfe0, which reads the LAST argument: table functions get their
        /// table as the first one, so Ui.DoCommand, which reads the first, can't be used) runs it as a slash command.
        /// The client also logs "Unrecognized command - lmemuN" (CommandQueue.log) after running the bound code. The
        /// arguments travel comma-separated, so code with a comma is sent as load("...")() with every comma written as
        /// the Lua escape \44 inside the string.
        /// </summary>
        public static void RunCode(WorldSession session, string code)
        {
            if (code.Contains(',') || code.Contains('\n'))
                code = "load(\"" + code.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace(",", "\\44") + "\")()";
            // The slash command runs a moment later, so each call binds its own command name: with one shared name a
            // second RunCode right behind the first rebound it first and the first code never ran (2026-10-12: the
            // Showcase sign-in was lost behind ClientFixes).
            string command = "lmemu" + (System.Threading.Interlocked.Increment(ref nextCommand) & 63);
            RunWithArgs(session, "BindCommandToLua", command, code);
            RunWithArgs(session, "Ui.ProcessChatCommand", "/" + command);
        }

        private static int nextCommand;

        /// <summary>Text as a single-quoted Lua string literal (commas are fine: RunCode escapes them).</summary>
        public static string Quote(string text)
            => "'" + (text ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", " ").Replace("\r", "") + "'";
    }
}
