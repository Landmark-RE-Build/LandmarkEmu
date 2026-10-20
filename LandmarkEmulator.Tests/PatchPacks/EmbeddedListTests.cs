using LandmarkEmulator.PatchPacks;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.PatchPacks
{
    /// <summary>
    /// The real lists built into LaunchPad and the patch host (names, sizes and checksums only): the strong list covers
    /// every file of both known 0.695 builds (January 2017 + Daybreak's final / Feb 2017; Feb fingerprints added
    /// 2026-10-20 from genuine copies).
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
            Assert.Empty(final.Unverifiable);
            // The two builds differ in exactly those three files, each with its own genuine bytes (no normalising).
            var differ = final.Files.Where(f => !january.Files[f.Key].Equals(f.Value)).Select(f => f.Key).ToList();
            Assert.Equal(3, differ.Count);
            Assert.Equal("8B0FC625F786FF05E2CCC63924C88283115CC8B234F798D9FF7D53033248F4F9", january.Files["Landmark64.exe"].Sha256);
            Assert.Equal("84D465705AEAA98E9D176DE5EAF02BD17649133FBC411001553D64B38B22CBF5", final.Files["Landmark64.exe"].Sha256);
            Assert.Equal("C455D39621241D8FAF66DC223C55F7837C09A46CB7796F4FCDEC4B7169753527", final.Files[@"Locale\en_us_data.dat"].Sha256);
            Assert.Equal("08B9EA2A8C902AA14B381C15A81E47D5CBB269616C618E5A7C037E414A18180A", final.Files[@"Locale\en_us_data.dir"].Sha256);
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
