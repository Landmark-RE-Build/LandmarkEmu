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
    /// <summary>
    /// Dedicated <c>terrain</c> release tag — not <c>latest</c>, so LandmarkLocal / other release tags
    /// do not have to carry this ~750 MB asset or become "latest" for the download to work.
    /// </summary>
    public const string TerrainDownloadUrl =
        "https://github.com/Landmark-RE-Build/LandmarkEmu/releases/download/terrain/LandmarkEmu-terrain-heights.zip";

    public static string ManualTerrainInstructions(LocalPaths paths) =>
        "What to do:\n" +
        $"1. Get {TerrainZipName} from the project's GitHub Releases (same place as LandmarkLocal).\n" +
        "2. Put that zip (still zipped) next to LandmarkLocal.exe:\n" +
        $"   {Path.Combine(paths.Root, TerrainZipName)}\n" +
        "   or in the cache folder:\n" +
        $"   {Path.Combine(paths.CacheDir, TerrainZipName)}\n" +
        "3. Click Start again.";

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

        (bool terrainOk, string? terrainError) = await EnsureTerrainAsync(paths, progress, ct);
        if (!terrainOk)
        {
            string why = string.IsNullOrWhiteSpace(terrainError)
                ? "Terrain heights are missing."
                : terrainError.Trim();
            return new GameDataSetupResult
            {
                Ok = false,
                TerrainPresent = false,
                Message = why + "\n\n" + ManualTerrainInstructions(paths),
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

    static async Task<(bool Ok, string? Error)> EnsureTerrainAsync(
        LocalPaths paths, IProgress<string>? progress, CancellationToken ct)
    {
        if (HasTerrainBins(paths.GameDataDir))
            return (true, null);

        string? zip = FindTerrainZip(paths);
        if (zip == null)
        {
            progress?.Report("Downloading terrain heights…");
            progress?.Report(LocalOrchestrator.StatusPrefix + "Downloading terrain heights…");
            try
            {
                zip = await DownloadTerrainLooseAsync(paths, progress, ct);
            }
            catch (Exception ex)
            {
                string msg = "Could not download terrain heights: " + FriendlyDownloadError(ex);
                progress?.Report(msg);
                progress?.Report(LocalOrchestrator.StatusPrefix + "Failed — terrain download");
                return (false, msg);
            }
        }

        if (zip == null || !File.Exists(zip))
        {
            string msg = "Terrain heights zip was not found and could not be downloaded.";
            progress?.Report(msg);
            progress?.Report(LocalOrchestrator.StatusPrefix + "Failed — terrain missing");
            return (false, msg);
        }

        string stamp = TerrainStamp(zip);
        string stampFile = Path.Combine(paths.GameDataDir, ".terrain-heights");
        if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp && HasTerrainBins(paths.GameDataDir))
            return (true, null);

        try
        {
            await ZipProgress.ExtractToDirectoryAsync(
                zip, paths.GameDataDir, "Unpacking terrain heights (once; can take a few minutes)", progress, ct);
        }
        catch (Exception ex)
        {
            string msg = "Could not unpack terrain heights: " + ex.Message;
            progress?.Report(msg);
            return (false, msg);
        }

        File.WriteAllText(stampFile, stamp);
        if (!HasTerrainBins(paths.GameDataDir))
        {
            string msg = $"Unpacked {Path.GetFileName(zip)} but found no TerrainHeights*.bin files inside.";
            progress?.Report(msg);
            return (false, msg);
        }

        return (true, null);
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

    static async Task<string> DownloadTerrainLooseAsync(LocalPaths paths, IProgress<string>? progress, CancellationToken ct)
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
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} from\n{TerrainDownloadUrl}\n" +
                "(that file is not on GitHub Releases yet, or the link is wrong).");
        }

        long? total = response.Content.Headers.ContentLength;
        string temp = dest + ".partial";
        await using Stream input = await response.Content.ReadAsStreamAsync(ct);
        await using (Stream output = File.Create(temp))
        {
            byte[] buffer = new byte[1 << 20];
            long read = 0;
            long lastReport = -1;
            int n;
            while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                long mb = read / (1024 * 1024);
                if (mb != lastReport && (mb % 5 == 0 || read == total))
                {
                    lastReport = mb;
                    string line = total is long t && t > 0
                        ? $"Downloading terrain heights… {mb} / {t / (1024 * 1024)} MB"
                        : $"Downloading terrain heights… {mb} MB";
                    progress?.Report(line);
                    progress?.Report(LocalOrchestrator.StatusPrefix + line);
                }
            }
        }

        if (File.Exists(dest))
            File.Delete(dest);
        File.Move(temp, dest);
        progress?.Report("Terrain heights download finished.");
        return dest;
    }

    static string FriendlyDownloadError(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is HttpRequestException http)
                return http.Message;
            if (e is TaskCanceledException)
                return "the download timed out or was cancelled.";
        }
        return ex.Message;
    }
}
