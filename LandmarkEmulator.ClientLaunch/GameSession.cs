using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using LandmarkEmulator.PatchPacks;

namespace LandmarkEmulator.ClientLaunch
{
    /// <summary>Start Landmark64 with a session key (LaunchPad PLAY / LandmarkLocal Play).</summary>
    public static class GameSession
    {
        /// <summary>Starts the game logged in with a session key from the account service.</summary>
        public static Process Start(string folder, string sessionKey, string locale)
            => Start(folder, sessionKey, locale, null, out ClientLaunchPatches _);

        /// <summary>
        /// Starts the game; when <paramref name="options"/> requests allocator expand, creates the process suspended,
        /// validates/patches in memory (P4, then optional contour), then resumes. On patch mismatch the process still
        /// starts and <paramref name="patches"/> carries user-visible alerts.
        /// </summary>
        public static Process Start(
            string folder,
            string sessionKey,
            string locale,
            ClientLaunchOptions? options,
            out ClientLaunchPatches patches)
        {
            string arguments = BuildArguments(sessionKey, locale);
            string exe = Path.Combine(folder, "Landmark64.exe");
            AllocatorSlotExpand expand = options?.AllocatorSlotExpand ?? AllocatorSlotExpand.Off;
            ContourThreadExpand contour = ContourThreadPatch.ClampToAllocator(
                options?.ContourThreadExpand ?? ContourThreadExpand.Off, expand);
            bool tryPatch = AllocatorSlotPatch.ShouldApply(expand, Environment.ProcessorCount);

            if (!tryPatch || !OperatingSystem.IsWindows())
            {
                patches = new ClientLaunchPatches();
                return StartViaProcessApi(exe, arguments, folder);
            }

            return StartSuspendedAndMaybePatch(exe, arguments, folder, contour, out patches);
        }

        internal static string BuildArguments(string sessionKey, string locale)
        {
            string arguments = $"SessionId={sessionKey} CasSessionId=0 STEAM_ENABLED=0";
            if (!string.IsNullOrWhiteSpace(locale) && Regex.IsMatch(locale, "^[a-z]{2}_[A-Z]{2}$"))
                arguments += $" Internationalization:Locale={locale}";
            return arguments;
        }

        static Process StartViaProcessApi(string exe, string arguments, string folder)
        {
            var info = new ProcessStartInfo(exe, arguments)
            {
                WorkingDirectory = folder,
                UseShellExecute = false
            };
            info.Environment.Remove("SteamAppId");
            info.Environment.Remove("SteamGameId");
            try
            {
                return Process.Start(info) ?? throw new InvalidOperationException("The game did not start.");
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException(
                    $"Landmark64.exe did not start: {new Win32Exception(e.NativeErrorCode).Message}");
            }
        }

        [SupportedOSPlatform("windows")]
        static Process StartSuspendedAndMaybePatch(
            string exe, string arguments, string folder, ContourThreadExpand contour, out ClientLaunchPatches patches)
        {
            patches = new ClientLaunchPatches();
            string commandLine = "\"" + exe + "\" " + arguments;
            var si = new Native.STARTUPINFOW { cb = Marshal.SizeOf<Native.STARTUPINFOW>() };
            Native.PROCESS_INFORMATION pi = default;

            Dictionary<string, string?> env = new(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            {
                string key = (string)e.Key!;
                if (key is "SteamAppId" or "SteamGameId")
                    continue;
                env[key] = e.Value as string;
            }

            IntPtr envBlock = Native.BuildEnvironmentBlock(env);
            try
            {
                bool ok = Native.CreateProcessW(
                    exe,
                    new StringBuilder(commandLine),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    Native.CREATE_SUSPENDED | Native.CREATE_UNICODE_ENVIRONMENT,
                    envBlock,
                    folder,
                    ref si,
                    out pi);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"Landmark64.exe did not start: {new Win32Exception(err).Message}");
                }

                try
                {
                    AllocatorPatchResult allocator = AllocatorSlotPatch.TryApply(pi.hProcess);
                    AllocatorPatchResult contourResult = AllocatorPatchResult.Skipped;
                    if (allocator.Status == AllocatorPatchStatus.Applied
                        && contour is ContourThreadExpand.Auto or ContourThreadExpand.On)
                    {
                        contourResult = ContourThreadPatch.TryApply(pi.hProcess);
                    }

                    patches = new ClientLaunchPatches { Allocator = allocator, Contour = contourResult };

                    if (Native.ResumeThread(pi.hThread) == unchecked((uint)-1))
                    {
                        int err = Marshal.GetLastWin32Error();
                        throw new InvalidOperationException(
                            $"Landmark64.exe started but could not be resumed: {new Win32Exception(err).Message}");
                    }

                    return Process.GetProcessById(unchecked((int)pi.dwProcessId));
                }
                catch
                {
                    try { Native.ResumeThread(pi.hThread); } catch { /* ignore */ }
                    throw;
                }
                finally
                {
                    if (pi.hThread != IntPtr.Zero)
                        Native.CloseHandle(pi.hThread);
                    if (pi.hProcess != IntPtr.Zero)
                        Native.CloseHandle(pi.hProcess);
                }
            }
            finally
            {
                if (envBlock != IntPtr.Zero)
                    Marshal.FreeHGlobal(envBlock);
            }
        }

        static class Native
        {
            public const uint CREATE_SUSPENDED = 0x00000004;
            public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct STARTUPINFOW
            {
                public int cb;
                public string? lpReserved;
                public string? lpDesktop;
                public string? lpTitle;
                public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
                public int dwFlags;
                public short wShowWindow, cbReserved2;
                public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct PROCESS_INFORMATION
            {
                public IntPtr hProcess;
                public IntPtr hThread;
                public uint dwProcessId;
                public uint dwThreadId;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern bool CreateProcessW(
                string? lpApplicationName,
                StringBuilder lpCommandLine,
                IntPtr lpProcessAttributes,
                IntPtr lpThreadAttributes,
                bool bInheritHandles,
                uint dwCreationFlags,
                IntPtr lpEnvironment,
                string? lpCurrentDirectory,
                ref STARTUPINFOW lpStartupInfo,
                out PROCESS_INFORMATION lpProcessInformation);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint ResumeThread(IntPtr hThread);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr hObject);

            public static IntPtr BuildEnvironmentBlock(Dictionary<string, string?> env)
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, string?> kv in env.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                        continue;
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
                }
                sb.Append('\0');
                byte[] bytes = Encoding.Unicode.GetBytes(sb.ToString());
                IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                return ptr;
            }
        }
    }
}
