namespace LandmarkLocal;

public sealed class LaunchPadInstallResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? LaunchPadExe { get; init; }
}

/// <summary>Copies bundled LaunchPad into the Landmark folder; one-time LaunchPad.exe.orig backup.</summary>
public static class LaunchPadInstaller
{
    public static LaunchPadInstallResult Install(LocalPaths paths, LocalSettings settings)
    {
        if (!LocalSettings.LooksLikeLandmarkFolder(settings.LandmarkFolder))
        {
            return new LaunchPadInstallResult
            {
                Ok = false,
                Message = "Set your Landmark folder before installing LaunchPad.",
            };
        }

        string? source = FindBundledLaunchPad(paths);
        if (source == null)
        {
            return new LaunchPadInstallResult
            {
                Ok = false,
                Message = "No LaunchPad.exe was bundled with LandmarkLocal. Rebuild/publish with LaunchPad included, " +
                          "or copy LaunchPad.exe into the LaunchPad\\ folder next to LandmarkLocal.exe.",
            };
        }

        string dest = Path.Combine(settings.LandmarkFolder, "LaunchPad.exe");
        string orig = dest + ".orig";
        if (File.Exists(dest) && !File.Exists(orig))
        {
            File.Copy(dest, orig, overwrite: false);
        }

        File.Copy(source, dest, overwrite: true);

        // WebView2 / sidecar files if present beside the bundled exe.
        string? sourceDir = Path.GetDirectoryName(source);
        if (sourceDir != null)
        {
            foreach (string file in Directory.EnumerateFiles(sourceDir))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("LaunchPad.exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the game folder clean: single-file publish needs only LaunchPad.exe.
                    if (name.StartsWith("LaunchPad", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(settings.LandmarkFolder, name), overwrite: true);
                }
            }
        }

        return new LaunchPadInstallResult
        {
            Ok = true,
            LaunchPadExe = dest,
            Message = File.Exists(orig)
                ? $"LaunchPad installed into your Landmark folder (original kept as LaunchPad.exe.orig)."
                : "LaunchPad installed into your Landmark folder.",
        };
    }

    /// <summary>Optional note: LaunchPad remains for joining remote Docker servers; local Play is in LandmarkLocal.</summary>
    public static string RemoteLaunchPadNote(LocalSettings settings)
    {
        string name = string.IsNullOrWhiteSpace(settings.AdminAccountName)
            ? "(your account name)"
            : settings.AdminAccountName.Trim();
        return "For this PC: use Play in LandmarkLocal (account " + name + " is also your admin).\n"
               + "LaunchPad in your Landmark folder is still there if you later join a friend's server.";
    }

    [Obsolete("Phase 2: use Play in LandmarkLocal; kept for older callers/tests.")]
    public static string AdminNagMessage(LocalSettings settings) => RemoteLaunchPadNote(settings);

    static string? FindBundledLaunchPad(LocalPaths paths)
    {
        string bundled = Path.Combine(paths.BundledLaunchPadDir, "LaunchPad.exe");
        if (File.Exists(bundled))
            return bundled;

        string beside = Path.Combine(paths.Root, "LaunchPad.exe");
        if (File.Exists(beside))
            return beside;

        string? repo = LocalPaths.FindRepoRoot(paths.Root) ?? LocalPaths.FindRepoRoot(AppContext.BaseDirectory);
        if (repo == null)
            return null;
        foreach (string config in new[] { "Release", "Debug" })
        {
            string dir = Path.Combine(repo, "LandmarkEmulator.LaunchPad", "bin", config, "net10.0-windows");
            string exe = Path.Combine(dir, "LaunchPad.exe");
            if (File.Exists(exe))
                return exe;
        }
        return null;
    }
}
