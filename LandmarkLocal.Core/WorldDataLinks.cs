namespace LandmarkLocal;

/// <summary>
/// Mirrors docker/world-entrypoint.sh: world state lives under server-data/worlds/main, linked beside WorldServer.exe.
/// </summary>
public static class WorldDataLinks
{
    static readonly string[] Dirs =
    [
        "TerrainEdits", "Contests", "Portraits", "SiteImages", "TemplateImages", "TemplateProps", "Reports", "Telemetry"
    ];

    static readonly string[] Files =
    [
        "felled_trees.txt", "gathered_nodes.txt", "dev_commands.txt", "opcode_stats.tsv", "ItemAudit.txt", "CombatGear.txt"
    ];

    public static void Ensure(LocalPaths paths, string worldServerDir)
    {
        Directory.CreateDirectory(paths.WorldDataDir);
        Directory.CreateDirectory(worldServerDir);

        foreach (string name in Dirs)
        {
            string data = Path.Combine(paths.WorldDataDir, name);
            string link = Path.Combine(worldServerDir, name);
            Directory.CreateDirectory(data);
            EnsureDirLink(link, data);
        }

        foreach (string name in Files)
        {
            string data = Path.Combine(paths.WorldDataDir, name);
            string link = Path.Combine(worldServerDir, name);
            if (!File.Exists(data))
                File.WriteAllText(data, "");
            EnsureFileLink(link, data);
        }
    }

    static void EnsureDirLink(string linkPath, string targetPath)
    {
        if (Directory.Exists(linkPath) && !IsReparsePoint(linkPath))
        {
            // Dev build output: leave a real folder alone; copy is not needed for first run.
            return;
        }
        if (Directory.Exists(linkPath) || File.Exists(linkPath))
        {
            try { Directory.Delete(linkPath); }
            catch { try { File.Delete(linkPath); } catch { return; } }
        }
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch
        {
            // Junctions do not need elevation on Windows.
            CreateJunction(linkPath, targetPath);
        }
    }

    static void EnsureFileLink(string linkPath, string targetPath)
    {
        if (File.Exists(linkPath) && !IsReparsePoint(linkPath))
            return;
        if (File.Exists(linkPath) || Directory.Exists(linkPath))
        {
            try { File.Delete(linkPath); }
            catch { try { Directory.Delete(linkPath); } catch { return; } }
        }
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch
        {
            // Symlinks may need privilege; copy is a last resort (dev builds keep a real file in bin\).
            File.Copy(targetPath, linkPath, overwrite: true);
        }
    }

    static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    static void CreateJunction(string linkPath, string targetPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Could not create directory junction for world data.");
        p.WaitForExit();
        if (p.ExitCode != 0 || !Directory.Exists(linkPath))
            throw new InvalidOperationException($"mklink /J failed for {linkPath} -> {targetPath}");
    }
}
