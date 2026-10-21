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
            progress?.Report($"Using cached {fileName}");
            return dest;
        }

        progress?.Report($"Downloading {fileName}…");
        string temp = dest + ".partial";
        if (File.Exists(temp))
            File.Delete(temp);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using (Stream input = await response.Content.ReadAsStreamAsync(ct))
        await using (Stream output = File.Create(temp))
        {
            await input.CopyToAsync(output, ct);
        }

        if (!await MatchesHashAsync(temp, expectedSha256, ct))
        {
            File.Delete(temp);
            throw new InvalidOperationException($"Checksum failed for {fileName}. Delete cache\\{fileName} and try again.");
        }

        if (File.Exists(dest))
            File.Delete(dest);
        File.Move(temp, dest);
        progress?.Report($"Saved {fileName}");
        return dest;
    }

    public static async Task<bool> MatchesHashAsync(string path, string expectedSha256, CancellationToken ct = default)
    {
        await using Stream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
