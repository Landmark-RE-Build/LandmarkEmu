namespace LandmarkLocal;

/// <summary>
/// Layout under the LandmarkLocal folder (install dir): app + servers + mariadb + server-data + logs.
/// </summary>
public sealed class LocalPaths
{
    public LocalPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string CacheDir => Path.Combine(Root, "cache");
    public string MariaDbDir => Path.Combine(Root, "mariadb");
    public string MariaDbDataDir => Path.Combine(Root, "mariadb-data");
    public string LogsDir => Path.Combine(Root, "logs");
    public string PatchesDir => Path.Combine(Root, "patches");
    public string BundledDataDir => Path.Combine(Root, "data");
    public string BundledLaunchPadDir => Path.Combine(Root, "LaunchPad");
    public string ServerDataDir => Path.Combine(Root, "server-data");
    public string GameDataDir => Path.Combine(ServerDataDir, "gamedata");
    public string WorldDataDir => Path.Combine(ServerDataDir, "worlds", "main");
    public string ServersDir => Path.Combine(Root, "servers");
    public string AuthServerDir => Path.Combine(ServersDir, "AuthServer");
    public string WorldServerDir => Path.Combine(ServersDir, "WorldServer");
    public string AuthServerJson => Path.Combine(AuthServerDir, "AuthServer.json");
    public string WorldServerJson => Path.Combine(WorldServerDir, "WorldServer.json");
    public string PidFile => Path.Combine(Root, "landmarklocal.pids.json");

    public void EnsureLayout()
    {
        foreach (string dir in new[]
                 {
                     CacheDir, LogsDir, PatchesDir, GameDataDir, WorldDataDir, AuthServerDir, WorldServerDir,
                     BundledDataDir, BundledLaunchPadDir
                 })
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Prefer a published <c>servers/</c> layout; in a repo checkout fall back to Debug/Release build outputs.
    /// </summary>
    public string? FindAuthExe() => FindServerExe(AuthServerDir, "LandmarkEmulator.AuthServer", "LandmarkEmulator.AuthServer");

    public string? FindWorldExe() => FindServerExe(WorldServerDir, "LandmarkEmulator.WorldServer", "LandmarkEmulator.WorldServer");

    string? FindServerExe(string publishedDir, string exeName, string projectFolder)
    {
        string published = Path.Combine(publishedDir, exeName + ".exe");
        if (File.Exists(published))
            return published;
        string? dll = Path.Combine(publishedDir, exeName + ".dll");
        if (File.Exists(dll))
            return dll;

        string? repo = FindRepoRoot(Root) ?? FindRepoRoot(AppContext.BaseDirectory);
        if (repo == null)
            return null;
        foreach (string config in new[] { "Release", "Debug" })
        {
            string dir = Path.Combine(repo, projectFolder, "bin", config, "net10.0");
            string exe = Path.Combine(dir, exeName + ".exe");
            if (File.Exists(exe))
                return exe;
            string builtDll = Path.Combine(dir, exeName + ".dll");
            if (File.Exists(builtDll))
                return builtDll;
        }
        return null;
    }

    public static string? FindRepoRoot(string start)
    {
        for (DirectoryInfo? d = new(start); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "LandmarkEmulator.sln")))
                return d.FullName;
        }
        return null;
    }

    /// <summary>
    /// LandmarkLocal folder: next to the exe when published; otherwise <c>LANDMARKLOCAL_ROOT</c> or a <c>run/</c> folder under the project.
    /// </summary>
    public static LocalPaths Resolve()
    {
        string? env = Environment.GetEnvironmentVariable("LANDMARKLOCAL_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return new LocalPaths(env);

        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(Path.Combine(baseDir, "servers")) || File.Exists(Path.Combine(baseDir, "settings.json")))
            return new LocalPaths(baseDir);

        string? repo = FindRepoRoot(baseDir);
        if (repo != null)
        {
            string run = Path.Combine(repo, "LandmarkLocal", "run");
            return new LocalPaths(run);
        }

        return new LocalPaths(baseDir);
    }
}
