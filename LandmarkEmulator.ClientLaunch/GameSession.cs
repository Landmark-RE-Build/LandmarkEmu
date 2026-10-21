using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.ClientLaunch
{
    /// <summary>Start Landmark64 with a session key (LaunchPad PLAY / LandmarkLocal Play).</summary>
    public static class GameSession
    {
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
            // Steam registration from a launcher is not passed on: the game runs with Steam off.
            info.Environment.Remove("SteamAppId");
            info.Environment.Remove("SteamGameId");
            try
            {
                return Process.Start(info) ?? throw new InvalidOperationException("The game did not start.");
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                throw new InvalidOperationException(
                    $"Landmark64.exe did not start: {new System.ComponentModel.Win32Exception(e.NativeErrorCode).Message}");
            }
        }
    }
}
