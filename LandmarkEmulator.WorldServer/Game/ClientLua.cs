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
        /// the Lua escape \044 inside the string (<see cref="CommaSafe"/>).
        /// </summary>
        public static void RunCode(WorldSession session, string code)
        {
            code = CommaSafe(code);
            // The slash command runs a moment later, so each call binds its own command name: with one shared name a
            // second RunCode right behind the first rebound it first and the first code never ran (2026-10-12: the
            // Showcase sign-in was lost behind ClientFixes).
            string command = "lmemu" + (System.Threading.Interlocked.Increment(ref nextCommand) & 63);
            RunWithArgs(session, "BindCommandToLua", command, code);
            RunWithArgs(session, "Ui.ProcessChatCommand", "/" + command);
        }

        private static int nextCommand;

        /// <summary>
        /// Code as one comma-free argument: unchanged when it has no comma or line break, else load("...")() with every
        /// comma as the decimal escape \044. Always three digits: a Lua decimal escape takes up to three, so "\44" in
        /// front of a digit ran on into it ("string.sub(s,1441)" became "\441441": "decimal escape too large", and the
        /// whole chunk failed; found by the harness calibration 2026-10-18).
        /// </summary>
        public static string CommaSafe(string code)
        {
            if (!code.Contains(',') && !code.Contains('\n'))
                return code;
            return "load(\"" + code.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace(",", "\\044") + "\")()";
        }

        // ---- Code from dev commands (lua / luacode / luas / send, Game/DevCommands) --------------------------------------

        /// <summary>
        /// The client's Lua natives (and its UI scripts' wrappers around them) that open the player's own desktop web
        /// browser, not the game's built-in one: on 2026-10-12 a test call of Ui.OpenSupportUrl() opened the user's Opera at
        /// ClientConfig PetitionUri. Found in the 0.695 binding table (re/out/luabind_695.tsv) as the natives that reach the
        /// launcher FUN_143b0e840 ("WebBrowserLauncherWin", ShellExecuteA "open"):
        /// Ui.OpenSupportUrl FUN_1435c6df0 (PetitionUri), Ui.LaunchBrowser FUN_1435cf2d0 (a fixed planetside2.com),
        /// Browser.OpenRegistrationExternally FUN_1435e1660 (Marketplace PlayerStudioRegistrationUrl),
        /// LiveGamer.GoToLiveGamerSite FUN_1435c2700, the global BrowseTo FUN_143e785e0, and OnlineHelpWindow.NavigateUrl
        /// FUN_1435e8400 (through FUN_14317fef0: external unless the client's UseInternalBrowser is set). Also refused:
        /// InGamePurchaseStoreScreen.FundWalletExternal / PurchaseMembershipExternal (the external payment flows),
        /// Steam.ShowOverlay (the Steam overlay's web pages) and the UI scripts' ShowHelpPage (InGameBrowser.lua and
        /// InGameMarketplaceBrowser.lua, which call Ui.OpenSupportUrl). The names asked about in the 2026-10-18 review
        /// (OpenUrl, OpenBrowser, LaunchUrl) are not in the 0.695 binding table. The server's own in-game browser calls
        /// (OpenMarketplaceTo, InGameMarketplaceBrowser.ShowURL) stay allowed: they open the game's built-in browser.
        /// </summary>
        public static readonly string[] ExternalBrowserNatives =
        {
            "OpenSupportUrl", "LaunchBrowser", "OpenRegistrationExternally", "GoToLiveGamerSite", "BrowseTo", "NavigateUrl",
            "FundWalletExternal", "PurchaseMembershipExternal", "ShowOverlay", "ShowHelpPage",
        };

        /// <summary>
        /// The first <see cref="ExternalBrowserNatives"/> name in dev-command text (a function name, code, arguments or a
        /// raw packet as Latin-1 text), ignoring case; null when none. A name check, not a sandbox: code can build a name
        /// at run time (string concatenation, escapes), so the real protection is that these commands only reach the caller
        /// and admins (<see cref="WorldServerConfiguration.DevClientCodeOnOthers"/>); this stops the plain call that opened
        /// the user's browser by mistake.
        /// </summary>
        public static string ExternalBrowserCall(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            foreach (string native in ExternalBrowserNatives)
                if (text.Contains(native, System.StringComparison.OrdinalIgnoreCase))
                    return native;
            return null;
        }

        /// <summary>Do dev commands' client code skip the desktop-browser check (setting DevClientCodeOnOthers)?</summary>
        private static bool DevChecksOff
            => LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.DevClientCodeOnOthers == true;

        /// <summary>Refuses (and logs) dev-command client code that names a desktop-browser native, unless the setting allows it.</summary>
        private static bool DevAllowed(WorldSession session, string text)
        {
            if (DevChecksOff || ExternalBrowserCall(text) is not string native)
                return true;
            log.Warn($"{session.CharacterName}: dev client Lua refused: it names {native}, which opens the player's desktop web browser "
                + "(setting DevClientCodeOnOthers is off).");
            return false;
        }

        /// <summary><see cref="Run"/> for a dev command (lua): false = refused (<see cref="ExternalBrowserCall"/>).</summary>
        public static bool DevRun(WorldSession session, string function, float a = 0f, float b = 0f, float c = 0f)
        {
            if (!DevAllowed(session, function))
                return false;
            Run(session, function, a, b, c);
            return true;
        }

        /// <summary><see cref="RunWithArgs"/> for a dev command (luas): the function and every argument are checked.</summary>
        public static bool DevRunWithArgs(WorldSession session, string function, params string[] args)
        {
            if (!DevAllowed(session, function + " " + string.Join(" ", args)))
                return false;
            RunWithArgs(session, function, args);
            return true;
        }

        /// <summary><see cref="RunCode"/> for a dev command (luacode).</summary>
        public static bool DevRunCode(WorldSession session, string code)
        {
            if (!DevAllowed(session, code))
                return false;
            RunCode(session, code);
            return true;
        }

        /// <summary>Text as a single-quoted Lua string literal (commas are fine: RunCode escapes them).</summary>
        public static string Quote(string text)
            => "'" + (text ?? "").Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", " ").Replace("\r", "") + "'";
    }
}
