using System.Security.Cryptography;

namespace LandmarkLocal;

public static class DownloadCache
{
    public static async Task<string> EnsureAsync(LocalPaths paths, string url, string fileName, string expectedSha256,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        paths.EnsureLayout();
        string dest = Path.Combine(paths.CacheDir, fileName);
        if (File.Exists(dest) && await MatchesHashAsync(dest, expectedSha256, ct))
        {
            Report(progress, $"Using cached {fileName}");
            return dest;
        }

        Report(progress, $"Downloading {fileName}…");
        string temp = dest + ".partial";
        if (File.Exists(temp))
            File.Delete(temp);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LandmarkLocal/1.0");
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync(ct);
        await using (Stream output = File.Create(temp))
        {
            byte[] buffer = new byte[1 << 20];
            long read = 0;
            long lastReportMb = -1;
            int lastPct = -1;
            int n;
            while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                long mb = read / (1024 * 1024);
                if (total is long t && t > 0)
                {
                    int pct = (int)(read * 100 / t);
                    if (pct != lastPct && (pct == 100 || pct % 5 == 0 || read == t || lastPct < 0))
                    {
                        lastPct = pct;
                        Report(progress,
                            $"Downloading {fileName}… {pct}% ({mb} / {t / (1024 * 1024)} MB)");
                    }
                }
                else if (mb != lastReportMb && (mb % 5 == 0 || lastReportMb < 0))
                {
                    lastReportMb = mb;
                    Report(progress, $"Downloading {fileName}… {mb} MB");
                }
            }
        }

        if (!await MatchesHashAsync(temp, expectedSha256, ct))
        {
            File.Delete(temp);
            throw new InvalidOperationException($"Checksum failed for {fileName}. Delete cache\\{fileName} and try again.");
        }

        if (File.Exists(dest))
            File.Delete(dest);
        File.Move(temp, dest);
        Report(progress, $"Saved {fileName}");
        return dest;
    }

    public static async Task<bool> MatchesHashAsync(string path, string expectedSha256, CancellationToken ct = default)
    {
        await using Stream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    static void Report(IProgress<string>? progress, string line)
    {
        progress?.Report(line);
        progress?.Report(LocalOrchestrator.StatusPrefix + line);
    }
}
