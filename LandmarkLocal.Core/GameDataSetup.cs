namespace LandmarkLocal;

public sealed class GameDataSetupResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public bool TerrainPresent { get; init; }
}

/// <summary>
/// Windows in-process equivalent of docker/setup.sh (Docker keeps setup.sh unchanged).
/// Terrain heights are required for LandmarkLocal "ready".
/// </summary>
public static class GameDataSetup
{
    public const string TerrainZipName = "LandmarkEmu-terrain-heights.zip";
    public const string TerrainDownloadUrl =
        "https://github.com/Landmark-RE-Build/LandmarkEmu/releases/latest/download/LandmarkEmu-terrain-heights.zip";

    public static string ManualTerrainInstructions(LocalPaths paths) =>
        "Terrain heights are required for trees, ground checks and starting build sites.\n" +
        $"1. Download {TerrainZipName} from the project's GitHub Releases.\n" +
        $"2. Put the zip here (still zipped):\n   {Path.Combine(paths.Root, TerrainZipName)}\n" +
        "   or in cache\\ next to LandmarkLocal, then Start again.";

    public static async Task<GameDataSetupResult> RunAsync(LocalPaths paths, LocalSettings settings,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!LocalSettings.LooksLikeLandmarkFolder(settings.LandmarkFolder))
        {
            return new GameDataSetupResult
            {
                Ok = false,
                Message = "Landmark folder is not set or is missing Landmark64.exe and Resources. " +
                          "Set it to your Steam Landmark folder.",
            };
        }

        paths.EnsureLayout();
        Directory.CreateDirectory(paths.GameDataDir);

        string resources = Path.Combine(settings.LandmarkFolder, "Resources");
        progress?.Report("Copying client data tables…");
        foreach (string file in Directory.EnumerateFiles(resources, "*.txt"))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(paths.GameDataDir, Path.GetFileName(file)), overwrite: true);
        }

        string locale = Path.Combine(settings.LandmarkFolder, "Locale", "en_us_data.dat");
        if (File.Exists(locale))
            File.Copy(locale, Path.Combine(paths.GameDataDir, "en_us_data.dat"), overwrite: true);
        else
            progress?.Report("Locale/en_us_data.dat was not found: names will be missing in game.");

        CopyBundledServerData(paths, progress, ct);

        bool terrain = await EnsureTerrainAsync(paths, progress, ct);
        if (!terrain)
        {
            return new GameDataSetupResult
            {
                Ok = false,
                TerrainPresent = false,
                Message = ManualTerrainInstructions(paths),
            };
        }

        int count = Directory.EnumerateFiles(paths.GameDataDir).Count();
        return new GameDataSetupResult
        {
            Ok = true,
            TerrainPresent = true,
            Message = $"Game data ready ({count} files).",
        };
    }

    static void CopyBundledServerData(LocalPaths paths, IProgress<string>? progress, CancellationToken ct)
    {
        string? source = null;
        if (Directory.Exists(paths.BundledDataDir) && Directory.EnumerateFileSystemEntries(paths.BundledDataDir).Any())
            source = paths.BundledDataDir;
        else
        {
            string? repo = LocalPaths.FindRepoRoot(paths.Root) ?? LocalPaths.FindRepoRoot(AppContext.BaseDirectory);
            if (repo != null)
            {
                string repoData = Path.Combine(repo, "data");
                if (Directory.Exists(repoData))
                    source = repoData;
            }
        }

        if (source == null)
            return;

        progress?.Report("Copying server data tables…");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(source, file);
            string dest = Path.Combine(paths.GameDataDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    static async Task<bool> EnsureTerrainAsync(LocalPaths paths, IProgress<string>? progress, CancellationToken ct)
    {
        if (HasTerrainBins(paths.GameDataDir))
            return true;

        string? zip = FindTerrainZip(paths);
        if (zip == null)
        {
            progress?.Report("Downloading terrain heights…");
            try
            {
                // No pinned checksum in Releases (asset can grow); cache by file presence after unpack stamp.
                zip = await DownloadTerrainLooseAsync(paths, progress, ct);
            }
            catch (Exception ex)
            {
                progress?.Report("Terrain download failed: " + ex.Message);
                return false;
            }
        }

        if (zip == null || !File.Exists(zip))
            return false;

        string stamp = TerrainStamp(zip);
        string stampFile = Path.Combine(paths.GameDataDir, ".terrain-heights");
        if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp && HasTerrainBins(paths.GameDataDir))
            return true;

        await ZipProgress.ExtractToDirectoryAsync(
            zip, paths.GameDataDir, "Unpacking terrain heights (once; can take a few minutes)", progress, ct);
        File.WriteAllText(stampFile, stamp);
        return HasTerrainBins(paths.GameDataDir);
    }

    public static bool HasTerrainBins(string gameDataDir)
        => Directory.Exists(gameDataDir)
           && Directory.EnumerateFiles(gameDataDir, "TerrainHeights*.bin").Any();

    public static string? FindTerrainZip(LocalPaths paths)
    {
        string[] candidates =
        [
            Path.Combine(paths.Root, TerrainZipName),
            Path.Combine(paths.CacheDir, TerrainZipName),
        ];
        string? repo = LocalPaths.FindRepoRoot(paths.Root) ?? LocalPaths.FindRepoRoot(AppContext.BaseDirectory);
        if (repo != null)
            candidates = candidates.Append(Path.Combine(repo, TerrainZipName)).ToArray();

        return candidates.FirstOrDefault(File.Exists);
    }

    static string TerrainStamp(string zipPath)
    {
        var info = new FileInfo(zipPath);
        return $"{info.Length}-{info.LastWriteTimeUtc.Ticks}";
    }

    static async Task<string?> DownloadTerrainLooseAsync(LocalPaths paths, IProgress<string>? progress, CancellationToken ct)
    {
        paths.EnsureLayout();
        string dest = Path.Combine(paths.CacheDir, TerrainZipName);
        if (File.Exists(dest) && new FileInfo(dest).Length > 1_000_000)
            return dest;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LandmarkLocal/1.0");
        using HttpResponseMessage response = await http.GetAsync(TerrainDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            progress?.Report($"Terrain download HTTP {(int)response.StatusCode}");
            return null;
        }
        string temp = dest + ".partial";
        await using (Stream input = await response.Content.ReadAsStreamAsync(ct))
        await using (Stream output = File.Create(temp))
            await input.CopyToAsync(output, ct);
        if (File.Exists(dest))
            File.Delete(dest);
        File.Move(temp, dest);
        return dest;
    }
}
