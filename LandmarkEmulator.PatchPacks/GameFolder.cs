using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.PatchPacks
{
    /// <summary>A Landmark game folder: is it one, which build, is its game running, and is a path in it a plain one.</summary>
    public static class GameFolder
    {
        public static bool IsGameFolder(string? path)
            => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Landmark64.exe")) && Directory.Exists(Path.Combine(path, "Resources"));

        private static readonly Regex VersionPattern = new(@"0\.\d{3}\.\d\.\d{6}", RegexOptions.Compiled);

        /// <summary>
        /// The game build ("0.589.1.128895"): the exe has no version resource, but the build string the title bar shows is
        /// stored in it as text. "" when not found.
        /// </summary>
        public static string Build(string folder)
        {
            string exe = Path.Combine(folder, "Landmark64.exe");
            if (!File.Exists(exe))
                return "";
            string text = Encoding.Latin1.GetString(File.ReadAllBytes(exe));
            return VersionPattern.Match(text) is { Success: true } m ? m.Value : "";
        }

        /// <summary>Is the game in this folder running? (A copy in another folder doesn't count.)</summary>
        public static bool IsRunning(string folder)
        {
            string mine = Path.GetFullPath(Path.Combine(folder, "Landmark64.exe"));
            foreach (Process p in Process.GetProcessesByName("Landmark64"))
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, mine, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return true;   // can't tell where it runs from: be safe
                }
            }
            return false;
        }

        /// <summary>
        /// Throws unless every folder from <paramref name="root"/> down to <paramref name="rel"/> (and the file itself, when it
        /// exists) is a plain folder or file: a junction, symbolic link or other reparse point inside the game folder could
        /// send a write or delete somewhere else on the PC. The game folder itself may be one (a Steam library on another
        /// drive often is).
        /// </summary>
        public static void RequirePlainPath(string root, string rel)
        {
            string path = root;
            string[] parts = rel.Split('\\');
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
                FileSystemInfo info = i < parts.Length - 1 ? new DirectoryInfo(path) : new FileInfo(path);
                if (!info.Exists)
                {
                    if (i < parts.Length - 1 && File.Exists(path))
                        throw new InvalidOperationException($"{rel}: {string.Join("\\", parts[..(i + 1)])} is a file, not a folder.");
                    if (i == parts.Length - 1 && Directory.Exists(path))
                        throw new InvalidOperationException($"{rel} is a folder, not a file.");
                    continue;
                }
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"{rel}: {string.Join("\\", parts[..(i + 1)])} is a link (junction or symbolic " +
                        "link) to somewhere else, so LaunchPad won't write through it.");
            }
        }
    }
}
