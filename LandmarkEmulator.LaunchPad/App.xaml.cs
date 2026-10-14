using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// LaunchPad.exe. Opened normally it shows the launcher (one per game folder). For server owners it also makes patch
    /// packs from the command line:
    ///   LaunchPad.exe --make-pack &lt;new game folder&gt; --out pack.lmpatch --name "..." --version 0.695.1.132647
    ///                 [--base &lt;old game folder&gt;] [--requires 0.589.1.128895] [--description "..."] [--delete-removed]
    /// </summary>
    public partial class App : Application
    {
        private Mutex? single;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (e.Args.Length == 3 && e.Args[0] == "--delta-test")
            {
                AttachConsole(-1);
                Shutdown(DeltaTest(e.Args[1], e.Args[2]));
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--make-update")
            {
                AttachConsole(-1);
                Shutdown(MakeUpdate(e.Args));
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--make-pack")
            {
                AttachConsole(-1);
                Shutdown(MakePack(e.Args));
                return;
            }

            // One launcher per game folder (two would race over the same files and settings).
            string name = "LandmarkEmu.LaunchPad." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Game.Folder.ToLowerInvariant())))[..16];
            single = new Mutex(true, name, out bool first);
            if (!first)
            {
                MessageBox.Show("LaunchPad is already open.", "LaunchPad", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            new MainWindow().Show();
            if (Game.IsGameFolder(Game.Folder))
                SteamPresence.Start(Game.Folder);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SteamPresence.Stop();
            base.OnExit(e);
        }

        private static int MakePack(string[] args)
        {
            string? Arg(string flag)
            {
                int i = Array.IndexOf(args, flag);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            string? source = args.Length > 1 ? args[1] : null, output = Arg("--out");
            if (source == null || source.StartsWith("--") || output == null || !Directory.Exists(source))
            {
                Console.WriteLine();
                Console.WriteLine("LaunchPad.exe --make-pack <new game folder> --out pack.lmpatch --name \"...\" --version <build>");
                Console.WriteLine("              [--base <old game folder>] [--requires <build>] [--description \"...\"] [--delete-removed]");
                Console.WriteLine("  --base: only files that differ from the old folder go in the pack.");
                Console.WriteLine("  --delete-removed: with --base, files only the old folder has are deleted by the pack.");
                return 1;
            }
            string? baseFolder = Arg("--base");
            if (baseFolder != null && !Directory.Exists(baseFolder))
            {
                Console.WriteLine($"\nNo folder {baseFolder}");
                return 1;
            }
            string version = Arg("--version") ?? (Game.IsGameFolder(source) ? Game.Build(source) : "");
            string requires = Arg("--requires") ?? (baseFolder != null && Game.IsGameFolder(baseFolder) ? Game.Build(baseFolder) : "");
            try
            {
                Console.WriteLine($"\nMaking {output} from {source}{(baseFolder != null ? $" (changes since {baseFolder})" : "")}...");
                int files = Patching.MakePack(source, baseFolder, output, Arg("--name") ?? Path.GetFileName(Path.GetFullPath(source)),
                    version, requires, Arg("--description") ?? "", args.Contains("--delete-removed"), Console.WriteLine);
                Console.WriteLine($"Done: {files} files, version {(version.Length > 0 ? version : "(none)")}" +
                    $"{(requires.Length > 0 ? $", requires {requires}" : "")}.");
                // Players' LaunchPad only installs the verified update to the final build: say whether this is it.
                using var made = new Patching.Pack(output);
                List<string> problems = Patching.CheckUpdate(made.Manifest, out Patching.TargetBuild target);
                Console.WriteLine(problems.Count == 0
                    ? $"Every file is a file of the {target.Label} build. Each player's LaunchPad " +
                      "also checks that the pack leaves that WHOLE build on their Steam copy: make it from a complete 0.695 " +
                      "folder with --base <Steam game folder> so it carries every changed file."
                    : "Players' LaunchPad will REFUSE this pack: " + string.Join("; ", problems) + ".");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Console.WriteLine($"Failed: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Development check of <see cref="Delta"/>: makes the delta from an old file to a new one, rebuilds the new file from
        /// the old one plus the delta, and checks it is byte for byte the new file. Neither input is changed.
        /// </summary>
        private static int DeltaTest(string oldPath, string newPath)
        {
            byte[] old = File.ReadAllBytes(oldPath), now = File.ReadAllBytes(newPath);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            byte[] delta = Delta.Create(old, now);
            double made = watch.Elapsed.TotalSeconds;
            watch.Restart();
            using var rebuilt = new MemoryStream();
            Delta.Apply(old, delta, rebuilt);
            double applied = watch.Elapsed.TotalSeconds;
            bool exact = rebuilt.ToArray().AsSpan().SequenceEqual(now);
            long packed = Delta.Deflated(delta), whole = Delta.Deflated(now);
            Console.WriteLine();
            Console.WriteLine($"{Path.GetFileName(newPath)}: old {old.Length:N0}, new {now.Length:N0} bytes");
            Console.WriteLine($"  whole new file, zipped: {whole:N0} bytes");
            Console.WriteLine($"  delta: {delta.Length:N0} bytes raw, {packed:N0} zipped ({100.0 * packed / whole:F1}% of the zipped file)");
            Console.WriteLine($"  made in {made:F1} s, applied in {applied:F1} s; rebuilt file {(exact ? "IDENTICAL to the new file (CRC32 checked too)" : "DIFFERENT - FAILED")}");
            return exact ? 0 : 1;
        }

        /// <summary>
        /// LaunchPad.exe --make-update --steam &lt;Steam game folder&gt; --from &lt;zip&gt; [--from &lt;zip&gt; ...] --out update.lmpatch [--name "..."]:
        /// the update pack from a verified Steam copy and the 0.695 files in zips (<see cref="Patching.MakeUpdate"/>).
        /// </summary>
        private static int MakeUpdate(string[] args)
        {
            string? steam = null, output = null, name = "Update to 0.695";
            var zips = new List<string>();
            for (int i = 1; i + 1 < args.Length; i += 2)
                switch (args[i])
                {
                    case "--steam": steam = args[i + 1]; break;
                    case "--from": zips.Add(args[i + 1]); break;
                    case "--out": output = args[i + 1]; break;
                    case "--name": name = args[i + 1]; break;
                }
            if (steam == null || output == null || zips.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("LaunchPad.exe --make-update --steam <Steam game folder> --from <0.695 folder | zip> [--from <zip> ...] --out update.lmpatch [--name \"...\"]");
                return 1;
            }
            // The Steam folder must be a genuine, unchanged copy: the edits and patches are made against its files.
            var store = new GameStore(steam);
            Console.WriteLine();
            Console.WriteLine("Checking the Steam copy...");
            List<Patching.Problem> problems = store.VerifySteam((_, _, _, _) => { }, CancellationToken.None);
            if (problems.Count > 0)
            {
                Console.WriteLine($"Not a complete Steam copy: {problems.Count} file(s) missing or changed (first: {problems[0].Path}).");
                return 1;
            }
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var (edits, patches, whole, bytes) = Patching.MakeUpdate(steam, zips, output, name, Console.WriteLine);
                Console.WriteLine($"Done in {watch.Elapsed.TotalMinutes:F1} min: {edits} in-place edits, {patches} patches, {whole} whole files; {bytes:N0} bytes.");
                using var made = new Patching.Pack(output);
                List<string> check = Patching.CheckUpdate(made.Manifest, out Patching.TargetBuild target);
                Console.WriteLine(check.Count == 0 ? $"Verified: an update to {target.Label}." : "NOT verified: " + string.Join("; ", check));
                return check.Count == 0 ? 0 : 1;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Console.WriteLine($"Failed: {ex.Message}");
                return 1;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);
    }
}
