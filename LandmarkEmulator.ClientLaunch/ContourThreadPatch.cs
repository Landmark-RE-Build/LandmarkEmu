using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LandmarkEmulator.PatchPacks;

namespace LandmarkEmulator.ClientLaunch
{
    /// <summary>
    /// In-memory contour / Contouring Job pool raise 3→5 (0.695 / 0.699). Sites from
    /// research/trace/0695/contour-thread-patch-sites.md (0.589-era count). Apply only after a successful P4 allocator expand.
    /// </summary>
    public static class ContourThreadPatch
    {
        public const byte NewCount = 5;

        public readonly record struct Site(ulong Va, string ExpectedHex, string PatchHex, string Name);

        /// <summary>C1a+C1b+C1c (contour + VoxelCellJob) and C2a+C2b (Contouring Job queue).</summary>
        public static readonly Site[] Sites =
        [
            new(0x143F94767UL, "b9 03 00 00 00", "b9 05 00 00 00", "C1a contour floor"),
            new(0x143F94773UL, "83 f9 03", "83 f9 05", "C1b contour min cmp"),
            new(0x143F94783UL, "83 e0 03", "83 e0 05", "C1c contour min and"),
            new(0x143133840UL, "83 f8 03", "83 f8 05", "C2a job min cmp"),
            new(0x143133850UL, "83 e1 03", "83 e1 05", "C2b job min and"),
        ];

        /// <summary>
        /// True when the user wants contour expand and allocator expand will be attempted this launch
        /// (contour cannot run without P4).
        /// </summary>
        public static bool ShouldApply(ContourThreadExpand contour, AllocatorSlotExpand allocator, int processorCount)
        {
            if (contour == ContourThreadExpand.Off)
                return false;
            if (!AllocatorSlotPatch.ShouldApply(allocator, processorCount))
                return false;
            return contour is ContourThreadExpand.Auto or ContourThreadExpand.On;
        }

        /// <summary>Force Off when allocator expand is Off (UI / persist gate).</summary>
        public static ContourThreadExpand ClampToAllocator(ContourThreadExpand contour, AllocatorSlotExpand allocator)
            => allocator == AllocatorSlotExpand.Off ? ContourThreadExpand.Off : contour;

        /// <summary>Validate every site. Returns null when all match, else a short reason.</summary>
        public static string? ValidateSites(Func<ulong, int, byte[]?> read)
        {
            foreach (Site site in Sites)
            {
                byte[] want = AllocatorSlotPatch.ParseHex(site.ExpectedHex);
                byte[]? got = read(site.Va, want.Length);
                if (got == null || got.Length != want.Length)
                    return $"could not read {site.Name} at 0x{site.Va:x}";
                if (!got.AsSpan().SequenceEqual(want))
                    return $"{site.Name} at 0x{site.Va:x} bytes mismatch";
            }
            return null;
        }

        [SupportedOSPlatform("windows")]
        public static AllocatorPatchResult TryApply(IntPtr process)
        {
            if (process == IntPtr.Zero)
                return Failed("no process handle");

            byte[]? Read(ulong va, int len)
            {
                var buf = new byte[len];
                nuint read = 0;
                if (!Native.ReadProcessMemory(process, unchecked((IntPtr)(long)va), buf, (nuint)len, ref read) || (int)read != len)
                    return null;
                return buf;
            }

            if (AllocatorSlotPatch.ValidateTimeDateStamp(Read) is string stampFail)
                return Failed(stampFail);
            if (ValidateSites(Read) is string siteFail)
                return Failed(siteFail);

            foreach (Site site in Sites)
            {
                byte[] patched = AllocatorSlotPatch.ParseHex(site.PatchHex);
                if (!ProtectWriteVerify(process, unchecked((IntPtr)(long)site.Va), patched))
                    return Failed($"could not rewrite {site.Name} at 0x{site.Va:x}");
            }

            return AllocatorPatchResult.Applied($"contourK={NewCount}");
        }

        static AllocatorPatchResult Failed(string reason) => new()
        {
            Status = AllocatorPatchStatus.Failed,
            Reason = reason,
            UserMessage =
                "Contour threads (experimental) could not be applied: this Landmark64.exe does not match the expected " +
                "0.695 / 0.699 code at the contour patch sites. The game will start without raising contour workers." +
                (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"),
        };

        [SupportedOSPlatform("windows")]
        static bool ProtectWriteVerify(IntPtr process, IntPtr address, byte[] bytes)
        {
            if (!Native.VirtualProtectEx(process, address, (nuint)bytes.Length, Native.PAGE_EXECUTE_READWRITE, out uint oldProtect))
                return false;
            try
            {
                nuint written = 0;
                if (!Native.WriteProcessMemory(process, address, bytes, (nuint)bytes.Length, ref written)
                    || (int)written != bytes.Length)
                    return false;
                Native.FlushInstructionCache(process, address, (nuint)bytes.Length);
                var back = new byte[bytes.Length];
                nuint read = 0;
                if (!Native.ReadProcessMemory(process, address, back, (nuint)bytes.Length, ref read) || (int)read != bytes.Length)
                    return false;
                return back.AsSpan().SequenceEqual(bytes);
            }
            finally
            {
                Native.VirtualProtectEx(process, address, (nuint)bytes.Length, oldProtect, out _);
            }
        }

        static class Native
        {
            public const uint PAGE_EXECUTE_READWRITE = 0x40;

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, ref nuint lpNumberOfBytesRead);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, ref nuint lpNumberOfBytesWritten);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nuint dwSize);
        }
    }
}
