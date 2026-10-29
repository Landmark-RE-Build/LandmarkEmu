using System.Diagnostics;
using System.IO;
using LandmarkEmulator.ClientLaunch;

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
            => GameSession.Start(folder, sessionKey, locale);

        /// <summary>Starts the game with optional in-memory patches (allocator / contour expand).</summary>
        public static Process Start(
            string folder, string sessionKey, string locale, ClientLaunchOptions? options, out ClientLaunchPatches patches)
            => GameSession.Start(folder, sessionKey, locale, options, out patches);
    }
}
