using System.IO;
using System.Text.Json;
using static LandmarkEmulator.LaunchPad.Patching;

namespace LandmarkEmulator.LaunchPad
{
    /// <summary>
    /// Patches hosted by a server (its /info "patches", LandmarkEmulator.WebAPI/PatchHost.cs), which can only be the
    /// verified update to the final build (Patching.CheckUpdate, GameStore.CheckComplete): the game folder's stack of
    /// installed packs is brought in line with the server's list. Packs the server doesn't list (another server's, or the
    /// player's own) come off first, newest first, from their backups; then the server's missing packs are downloaded and
    /// installed in order. A pack is one .lmpatch file, kept in the cache after it is installed (so an undo and a later reinstall
    /// download nothing), and a broken-off download resumes. Switched off for now: <see cref="Enabled"/>. Nothing happens unless the folder
    /// is a verified Steam copy of the game (<see cref="GameStore.RequireSteamCopy"/>).
    /// </summary>
    public static class ServerPatches
    {
        /// <summary>
        /// Server-pushed patches are switched off (2026-10-15: the user wants to come back to them later). Players
        /// install the update themselves from an .lmpatch file. Everything else here is kept and works; the environment variable
        /// LANDMARKEMU_SERVER_PATCHES=1 turns it on, for development only.
        /// </summary>
        public static bool Enabled => Environment.GetEnvironmentVariable("LANDMARKEMU_SERVER_PATCHES") == "1";

        /// <summary>A pack in a server's list.</summary>
        public sealed class Listed
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "";
            public string Requires { get; set; } = "";
            public string Description { get; set; } = "";
            public int Files { get; set; }
            public long Bytes { get; set; }
            public int Delete { get; set; }
            public string Sha256 { get; set; } = "";
        }

        public sealed class Plan
        {
            /// <summary>Installed packs that come off, newest first.</summary>
            public List<InstalledPack> Undo { get; } = new();
            /// <summary>Server packs that go on, in order.</summary>
            public List<Listed> Install { get; } = new();
            public bool Needed => Undo.Count > 0 || Install.Count > 0;
        }

        public static List<Listed> FromInfo(JsonElement info)
        {
            var list = new List<Listed>();
            if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("patches", out JsonElement patches) && patches.ValueKind == JsonValueKind.Array)
                foreach (JsonElement p in patches.EnumerateArray())
                    list.Add(new Listed
                    {
                        Id = Str(p, "id"), Name = Str(p, "name"), Version = Str(p, "version"), Requires = Str(p, "requires"),
                        Description = Str(p, "description"), Sha256 = Str(p, "sha256").ToUpperInvariant(),
                        Files = p.TryGetProperty("files", out var f) && f.TryGetInt32(out int n) ? n : 0,
                        Bytes = p.TryGetProperty("bytes", out var b) && b.TryGetInt64(out long l) ? l : 0,
                        Delete = p.TryGetProperty("delete", out var d) && d.TryGetInt32(out int k) ? k : 0
                    });
            return list;
        }

        private static string Str(JsonElement e, string name)
            => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        /// <summary>
        /// What playing on this server needs. A server that hosts no patches changes nothing (the player's own packs
        /// stay); one that does gets exactly its list.
        /// </summary>
        public static Plan PlanFor(GameStore store, List<Listed> listed)
        {
            var plan = new Plan();
            if (listed.Count == 0)
                return plan;
            int keep = 0;
            while (keep < store.Stack.Count && keep < listed.Count && store.Stack[keep].Id.Equals(listed[keep].Sha256, StringComparison.OrdinalIgnoreCase))
                keep++;
            for (int i = store.Stack.Count - 1; i >= keep; i--)
                plan.Undo.Add(store.Stack[i]);
            plan.Install.AddRange(listed.Skip(keep));
            return plan;
        }

        /// <summary>Carries out a plan: verify the Steam copy, undo, download, install.</summary>
        public static async Task Sync(GameStore store, ServerApi api, string source, string sourceName, Plan plan,
            Progress progress, CancellationToken cancel)
        {
            await Task.Run(() => store.RequireSteamCopy(progress, cancel), cancel);
            foreach (InstalledPack p in plan.Undo)
                await Task.Run(() => store.UndoLast(progress, cancel), cancel);

            foreach (Listed listed in plan.Install)
            {
                // The list first, checked against the sha256 the server announced.
                byte[] manifestBytes = await api.PackManifest(listed.Id, cancel);
                string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(manifestBytes));
                if (!sha.Equals(listed.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The server's list for \"{listed.Name}\" changed while downloading. Try again.");
                Manifest m = (JsonSerializer.Deserialize<Manifest>(manifestBytes) ?? throw new InvalidDataException("Empty pack list.")).Checked();
                // Only a verified update, and only when it would leave the whole build: checked on the list before a byte
                // of the pack is downloaded.
                List<string> problems = CheckUpdate(m);
                if (problems.Count > 0)
                    throw new InvalidOperationException($"This server's patch \"{listed.Name}\" is not a verified update to {FinalBuild}: {string.Join("; ", problems)}. Nothing was downloaded.");
                List<Problem> left = await Task.Run(() => store.CheckComplete(m, progress, cancel), cancel);
                if (left.Count > 0)
                    throw new InvalidOperationException($"This server's patch \"{listed.Name}\" is incomplete: {left.Count} file(s) of the build would still be missing or old (first: {left[0].Path}). Nothing was downloaded.");

                // The pack's .lmpatch: kept in the cache (an earlier download, or after an undo), or downloaded (resumed if
                // it broke off). It must be the pack the server listed (its pack.json's SHA-256).
                Directory.CreateDirectory(store.CacheFolder);
                string zip = Path.Combine(store.CacheFolder, listed.Sha256 + PatchFile.Extension);
                if (!IsPack(zip, listed.Sha256))
                {
                    long total = m.Files.Sum(f => f.Size);
                    await api.DownloadPack(listed.Id, zip + ".part", n => progress("downloading", n, Math.Max(total, n), listed.Name), cancel);
                    File.Move(zip + ".part", zip, true);
                    if (!IsPack(zip, listed.Sha256))
                    {
                        File.Delete(zip);
                        throw new InvalidDataException($"The patch \"{listed.Name}\" from the server is damaged. Try again.");
                    }
                }
                // Every file is checked against the list as it is installed (Install, filesChecked: false).
                using var pack = new Pack(zip);
                await Task.Run(() => store.Install(pack, source, sourceName, filesChecked: false, progress, cancel), cancel);
            }
        }

        /// <summary>Is <paramref name="zip"/> a readable pack whose pack.json has this SHA-256?</summary>
        private static bool IsPack(string zip, string sha256)
        {
            if (!File.Exists(zip))
                return false;
            try
            {
                using var pack = new Pack(zip);
                return pack.Id.Equals(sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or JsonException)
            {
                return false;
            }
        }
    }
}
