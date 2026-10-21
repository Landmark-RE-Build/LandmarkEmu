using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>The Landmark install: finding it, its build, pointing it at a server and starting it.</summary>
    public static class Game
    {
        /// <summary>
        /// The game folder: the one LaunchPad.exe is in. It replaces the game's own LaunchPad.exe, so it must sit next to
        /// Landmark64.exe (checked at start; see <see cref="IsGameFolder"/>).
        /// </summary>
        public static string Folder { get; } = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        public static bool IsGameFolder(string? path) => GameFolder.IsGameFolder(path);

        /// <summary>The game build ("0.589.1.128895"; GameFolder.Build), "" when not found.</summary>
        public static string Build(string folder) => GameFolder.Build(folder);

        /// <summary>Is the game in LaunchPad's folder running? (A copy in another folder doesn't block this one.)</summary>
        public static bool IsRunning() => GameFolder.IsRunning(Folder);

        /// <summary>
        /// Points ClientConfig.ini at a server: the login server (Server=), the in-game web pages ([Marketplace]
        /// MarketplaceUrl=), and [AssetDelivery] DirectThreadCount (recommended 2: with the shipped 10 the game's 64
        /// allocator slots ran out as the terrain threads started and the loading screen crawled for minutes). In
        /// advanced mode the thread count follows the player's choice (LaunchPad's 2, their own value, or the file left
        /// as it is); without it, always 2. The first change keeps the original as ClientConfig.ini.orig. The rules are
        /// <see cref="ClientConfigFile"/> (LandmarkEmulator.PatchPacks/ClientConfig.cs).
        /// </summary>
        public static void PointAt(string folder, string login, string marketplaceUrl, ClientConfigOptions? options)
            => ClientConfigFile.PointAt(folder, login, marketplaceUrl, options);

        /// <summary>Starts the game logged in with a session key from the account service.</summary>
        public static Process Start(string folder, string sessionKey, string locale)
        {
            string arguments = $"SessionId={sessionKey} CasSessionId=0 STEAM_ENABLED=0";
            if (!string.IsNullOrWhiteSpace(locale) && Regex.IsMatch(locale, "^[a-z]{2}_[A-Z]{2}$"))
                arguments += $" Internationalization:Locale={locale}";
            var info = new ProcessStartInfo(Path.Combine(folder, "Landmark64.exe"), arguments)
            {
                WorkingDirectory = folder,
                UseShellExecute = false
            };
            // LaunchPad's own Steam registration (SteamPresence) is not passed on: the game runs with Steam off.
            info.Environment.Remove("SteamAppId");
            info.Environment.Remove("SteamGameId");
            try
            {
                return Process.Start(info) ?? throw new InvalidOperationException("The game did not start.");
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // The system's own reason, without .NET's long "An error occurred trying to start process ..." wrapper.
                throw new InvalidOperationException($"Landmark64.exe did not start: {new System.ComponentModel.Win32Exception(e.NativeErrorCode).Message}");
            }
        }
    }
}
