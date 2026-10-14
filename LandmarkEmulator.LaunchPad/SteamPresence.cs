using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// "Playing Landmark" in Steam. Steam's own Play button no longer starts Landmark: its launch stops at
    /// "GettingLegacyKey" (error 25; Steam\logs\console_log.txt), because the Daybreak CD key it fetches is gone. So
    /// LaunchPad tells a running Steam itself that it is Landmark (app 297810) through the game's own
    /// steam_api64.dll, as Steam-launched games do. Steam then shows the player in Landmark and counts the playtime for
    /// as long as LaunchPad is open. The game is still started with STEAM_ENABLED=0 (Game.Start).
    /// </summary>
    public static class SteamPresence
    {
        public const string AppId = "297810";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool InitFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ShutdownFn();

        private static ShutdownFn? shutdown;

        public static bool Active => shutdown != null;

        /// <summary>Registers with Steam when it is running and the game's steam_api64.dll is there; false otherwise.</summary>
        public static bool Start(string gameFolder)
        {
            try
            {
                string dll = Path.Combine(gameFolder, "steam_api64.dll");
                if (!File.Exists(dll) || Process.GetProcessesByName("steam").Length == 0)
                    return false;
                // Steam sets these for the games it starts; SteamAPI_Init reads them.
                Environment.SetEnvironmentVariable("SteamAppId", AppId);
                Environment.SetEnvironmentVariable("SteamGameId", AppId);
                IntPtr library = NativeLibrary.Load(dll);
                var init = Marshal.GetDelegateForFunctionPointer<InitFn>(NativeLibrary.GetExport(library, "SteamAPI_Init"));
                if (!init())
                    return false;
                shutdown = Marshal.GetDelegateForFunctionPointer<ShutdownFn>(NativeLibrary.GetExport(library, "SteamAPI_Shutdown"));
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or IOException)
            {
                return false;
            }
        }

        public static void Stop()
        {
            shutdown?.Invoke();
            shutdown = null;
        }
    }
}
