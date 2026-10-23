using System.IO.Compression;

namespace LandmarkLocal;

/// <summary>Zip extract with percent progress so long first-run unpacks do not look hung.</summary>
public static class ZipProgress
{
    /// <summary>
    /// Extracts <paramref name="zipPath"/> into <paramref name="destDir"/> on a background thread,
    /// reporting about every 5% (and start/finish). Also emits <see cref="LocalOrchestrator.StatusPrefix"/>
    /// lines so the GUI banner can update.
    /// </summary>
    public static async Task ExtractToDirectoryAsync(
        string zipPath,
        string destDir,
        string label,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        Report(progress, $"{label}…");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destDir);
            string destRoot = Path.GetFullPath(destDir);
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            int total = archive.Entries.Count;
            if (total == 0)
                return;

            int done = 0;
            int lastPct = -1;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrEmpty(relative))
                    continue;

                string full = Path.GetFullPath(Path.Combine(destRoot, relative));
                if (!full.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Zip entry points outside the destination folder.");

                bool isDir = relative.EndsWith(Path.DirectorySeparatorChar)
                             || relative.EndsWith('/')
                             || string.IsNullOrEmpty(entry.Name);
                if (isDir)
                {
                    Directory.CreateDirectory(full);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    entry.ExtractToFile(full, overwrite: true);
                }

                int pct = done * 100 / total;
                if (pct != lastPct && (pct == 100 || pct % 5 == 0 || done == 1))
                {
                    lastPct = pct;
                    Report(progress, $"{label}… {pct}% ({done}/{total})");
                }
            }
        }, ct);
        Report(progress, $"{label} — done.");
    }

    static void Report(IProgress<string>? progress, string line)
    {
        progress?.Report(line);
        progress?.Report(LocalOrchestrator.StatusPrefix + line);
    }
}
