using System;
using System.IO;

namespace LandmarkEmulator.Tests.Support
{
    /// <summary>The repository checkout the tests run from (found by walking up to LandmarkEmulator.sln).</summary>
    public static class Repo
    {
        private static string root;

        public static string Root => root ??= Find();

        public static string PathOf(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

        public static string Read(params string[] parts) => File.ReadAllText(PathOf(parts));

        private static string Find()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "LandmarkEmulator.sln")))
                    return dir;
            throw new InvalidOperationException("LandmarkEmulator.sln not found above " + AppContext.BaseDirectory);
        }
    }
}
