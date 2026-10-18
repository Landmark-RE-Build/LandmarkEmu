using LandmarkEmulator.PatchPacks;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.PatchPacks
{
    /// <summary>
    /// The real lists built into LaunchPad and the patch host (names, sizes and checksums only): the strong list covers
    /// every file of the January 2017 build, and of the final build all but the three files no genuine local copy was
    /// found of (tools/strong_hash_list.py, 2026-10-18).
    /// </summary>
    public class EmbeddedListTests
    {
        [Fact]
        public void BothBuilds_AreKnown_WithTheirSha256()
        {
            Assert.Equal(2, Patching.Targets.Count);
            Assert.Equal(34138, Patching.FinalList.Count);
            Assert.Equal(1094, Patching.UpdateFiles.Count);
            Patching.TargetBuild final = Patching.Target("final"), january = Patching.Target("january-2017");
            Assert.Empty(january.Unverifiable);
            Assert.Equal(new[] { "Landmark64.exe", @"Locale\en_us_data.dat", @"Locale\en_us_data.dir" },
                final.Unverifiable.OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase).ToArray());
            // The two builds differ in exactly those three files, each with its own genuine bytes (no normalising).
            var differ = final.Files.Where(f => !january.Files[f.Key].Equals(f.Value)).Select(f => f.Key).ToList();
            Assert.Equal(3, differ.Count);
            Assert.Equal("8B0FC625F786FF05E2CCC63924C88283115CC8B234F798D9FF7D53033248F4F9", january.Files["Landmark64.exe"].Sha256);
        }

        [Fact]
        public void NoUpdateFile_IsASettingsOrLauncherFile()
        {
            Assert.Empty(Patching.UpdateFiles.Where(f => Patching.Mutable(f.Path) || Patching.Protected(f.Path)).Select(f => f.Path));
        }

        /// <summary>
        /// What is left out of the checks (Patching.Mutable): only the root .ini settings files and anything named
        /// LaunchPad*. No program or script file is ever left out.
        /// </summary>
        [Fact]
        public void LeftOutFiles_AreNeverCode()
        {
            string[] code = { ".exe", ".dll", ".lua", ".swf", ".js", ".bat", ".cmd", ".ps1", ".sys", ".ocx", ".com", ".scr" };
            var leftOut = Patching.FinalList.Keys.Concat(Patching.SteamList.Keys).Where(Patching.Mutable)
                .Where(p => !p.StartsWith("LaunchPad", System.StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            Assert.NotEmpty(leftOut);
            Assert.Empty(leftOut.Where(p => code.Any(c => p.EndsWith(c, System.StringComparison.OrdinalIgnoreCase))));
            Assert.Empty(leftOut.Where(p => !p.EndsWith(".ini", System.StringComparison.OrdinalIgnoreCase) || p.Contains('\\')));
            Assert.False(Patching.Mutable("Landmark64.exe"));
            Assert.False(Patching.Mutable(@"UI\Main.swf"));
            Assert.False(Patching.Mutable(@"Resources\Scripts\a.lua"));
        }
    }
}
