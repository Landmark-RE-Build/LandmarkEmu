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

        public static bool IsGameFolder(string? path)
            => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Landmark64.exe")) && Directory.Exists(Path.Combine(path, "Resources"));

        private static readonly Regex VersionPattern = new(@"0\.\d{3}\.\d\.\d{6}", RegexOptions.Compiled);

        /// <summary>
        /// The game build ("0.589.1.128895"): the exe has no version resource, but the build string the title bar shows is
        /// stored in it as text. "" when not found.
        /// </summary>
        public static string Build(string folder)
        {
            string exe = Path.Combine(folder, "Landmark64.exe");
            if (!File.Exists(exe))
                return "";
            string text = Encoding.Latin1.GetString(File.ReadAllBytes(exe));
            return VersionPattern.Match(text) is { Success: true } m ? m.Value : "";
        }

        /// <summary>Is the game in LaunchPad's folder running? (A copy in another folder doesn't block this one.)</summary>
        public static bool IsRunning()
        {
            string mine = Path.GetFullPath(Path.Combine(Folder, "Landmark64.exe"));
            foreach (Process p in Process.GetProcessesByName("Landmark64"))
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, mine, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return true;   // can't tell where it runs from: be safe
                }
            }
            return false;
        }

        /// <summary>
        /// Points ClientConfig.ini at a server: the login server (Server=), the in-game web pages (MarketplaceUrl=), and
        /// [AssetDelivery] DirectThreadCount=2 (fewer asset threads: with the shipped 10 the game's 64 allocator slots ran
        /// out as the terrain threads started and the loading screen crawled for minutes). The first change keeps the
        /// original as ClientConfig.ini.orig. (The same settings the server tools write.)
        /// </summary>
        public static void PointAt(string folder, string login, string marketplaceUrl)
        {
            string config = Path.Combine(folder, "ClientConfig.ini");
            var lines = File.Exists(config) ? File.ReadAllLines(config).ToList() : new List<string>();
            bool changed = false;
            void Set(string key, string value)
            {
                string line = $"{key}={value}";
                int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
                if (i >= 0 && lines[i] == line)
                    return;
                if (i >= 0)
                    lines[i] = line;
                else
                    lines.Add(line);
                changed = true;
            }
            Set("Server", login);
            Set("MarketplaceUrl", marketplaceUrl);
            const string assetLine = "DirectThreadCount=2";
            if (!lines.Contains(assetLine))
            {
                int i = lines.FindIndex(l => l.StartsWith("DirectThreadCount=", StringComparison.Ordinal));
                if (i >= 0)
                    lines[i] = assetLine;
                else if (lines.IndexOf("[AssetDelivery]") is int section and >= 0)
                    lines.Insert(section + 1, assetLine);
                else
                    lines.AddRange(new[] { "", "[AssetDelivery]", assetLine });
                changed = true;
            }
            if (!changed)
                return;
            if (File.Exists(config) && !File.Exists(config + ".orig"))
                File.Copy(config, config + ".orig");
            File.WriteAllLines(config, lines, Encoding.ASCII);
        }

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
