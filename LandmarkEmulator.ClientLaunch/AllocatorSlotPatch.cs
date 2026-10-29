using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LandmarkEmulator.PatchPacks;

namespace LandmarkEmulator.ClientLaunch
{
    /// <summary>
    /// In-memory P4: move the 0.695/0.699 allocator slot table from 64 to 127 entries while the process is suspended.
    /// One shared site profile (proven on PE stamps 0x586d8017 / 0x58a38fa2). Validates every rewrite site before any write.
    /// </summary>
    public static class AllocatorSlotPatch
    {
        public const ulong ImageBase = 0x140000000UL;
        public const ulong OldTable = 0x145427450UL;
        public const ulong PreferredNewTable = 0x1461B0000UL;
        public const int SlotStride = 0x258;
        public const int NewCount = 127;
        public const int NewTableBytes = NewCount * SlotStride; // 0x129A8
        /// <summary>Landmark64_0.695.exe (2017-01-04).</summary>
        public const uint TimeDateStamp0695 = 0x586D8017u;
        /// <summary>Shared 0.695/0.699 profile world build (2017-02-14).</summary>
        public const uint ExpectedTimeDateStamp = 0x58A38FA2u;
        public static readonly uint[] AcceptedTimeDateStamps = [TimeDateStamp0695, ExpectedTimeDateStamp];
        public const int AutoProcessorThreshold = 16;

        /// <summary>One RIP-relative LEA that must still target the old table (or table+Add) before patching.</summary>
        public readonly record struct LeaSite(ulong Va, int Add, string ExpectedHex);

        /// <summary>Shared 0.695 / 0.699 map: eight LEAs, capacity imm8, scan bound imm32.</summary>
        public static readonly LeaSite[] Leas =
        [
            new(0x142C050C5UL, 0, "48 8d 1d 84 23 82 02"),
            new(0x142C05A19UL, 0, "48 8d 1d 30 1a 82 02"),
            new(0x142C05C5CUL, 0, "48 8d 1d ed 17 82 02"),
            new(0x142C070B7UL, 0, "48 8d 0d 92 03 82 02"),
            new(0x142C0712EUL, 0, "4c 8d 0d 1b 03 82 02"),
            new(0x142C076E0UL, 0, "4c 8d 05 69 fd 81 02"),
            new(0x142C088FBUL, 0x238, "48 8d 1d 86 ed 81 02"),
            new(0x142C09112UL, 0, "48 8d 1d 37 e3 81 02"),
        ];

        public const string CapExpectedHex = "48 83 f9 40 73";
        public const ulong CapVa = 0x142C050F3UL;
        public const string ScanExpectedHex = "48 81 ff 00 96 00 00";
        public const ulong ScanVa = 0x142C05145UL;

        public static bool ShouldApply(AllocatorSlotExpand mode, int processorCount)
        {
            switch (mode)
            {
                case AllocatorSlotExpand.Off:
                    return false;
                case AllocatorSlotExpand.On:
                    return true;
                case AllocatorSlotExpand.Auto:
                    return processorCount >= AutoProcessorThreshold;
                default:
                    return false;
            }
        }

        /// <summary>Parse space-separated hex into bytes (for the short expected instruction blobs only).</summary>
        public static byte[] ParseHex(string hex)
        {
            string[] parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        /// <summary>RIP-relative LEA target: insnVa + 7 + signed disp32 at +3.</summary>
        public static ulong LeaTarget(ulong insnVa, ReadOnlySpan<byte> insn7)
        {
            if (insn7.Length < 7)
                throw new ArgumentException("LEA is 7 bytes.", nameof(insn7));
            int disp = BitConverter.ToInt32(insn7.Slice(3, 4));
            return unchecked(insnVa + 7UL + (ulong)(long)disp);
        }

        /// <summary>Rewrite the disp32 of a 7-byte RIP-relative LEA so it targets <paramref name="newTarget"/>.</summary>
        public static byte[] RewriteLea(ReadOnlySpan<byte> original7, ulong insnVa, ulong newTarget)
        {
            if (original7.Length < 7)
                throw new ArgumentException("LEA is 7 bytes.", nameof(original7));
            var rewritten = original7[..7].ToArray();
            long disp = unchecked((long)newTarget - (long)(insnVa + 7UL));
            if (disp is < int.MinValue or > int.MaxValue)
                throw new InvalidOperationException("New table is not RIP-reachable from this LEA.");
            BitConverter.TryWriteBytes(rewritten.AsSpan(3, 4), (int)disp);
            return rewritten;
        }

        /// <summary>
        /// Validate every rewrite site against the shared profile. <paramref name="read"/> returns null on failure.
        /// Returns null when all sites match, else a short reason.
        /// </summary>
        public static string? ValidateSites(Func<ulong, int, byte[]?> read)
        {
            foreach (LeaSite site in Leas)
            {
                byte[]? got = read(site.Va, 7);
                if (got == null || got.Length != 7)
                    return $"could not read LEA at 0x{site.Va:x}";
                byte[] want = ParseHex(site.ExpectedHex);
                if (!got.AsSpan().SequenceEqual(want))
                    return $"LEA at 0x{site.Va:x} bytes mismatch";
                ulong target = LeaTarget(site.Va, got);
                ulong expect = OldTable + (ulong)(uint)site.Add;
                if (target != expect)
                    return $"LEA at 0x{site.Va:x} targets 0x{target:x}, expected 0x{expect:x}";
            }

            byte[]? cap = read(CapVa, 5);
            if (cap == null || !cap.AsSpan().SequenceEqual(ParseHex(CapExpectedHex)))
                return $"capacity compare at 0x{CapVa:x} mismatch";

            byte[]? scan = read(ScanVa, 7);
            if (scan == null || !scan.AsSpan().SequenceEqual(ParseHex(ScanExpectedHex)))
                return $"scan bound at 0x{ScanVa:x} mismatch";

            return null;
        }

        /// <summary>Read PE TimeDateStamp from a little-endian image starting at <paramref name="imageBase"/>.</summary>
        public static string? ValidateTimeDateStamp(Func<ulong, int, byte[]?> read, ulong imageBase = ImageBase)
        {
            byte[]? e_lfanew = read(imageBase + 0x3C, 4);
            if (e_lfanew == null || e_lfanew.Length != 4)
                return "could not read PE header";
            uint lfanew = BitConverter.ToUInt32(e_lfanew);
            byte[]? stampBytes = read(imageBase + lfanew + 8, 4);
            if (stampBytes == null || stampBytes.Length != 4)
                return "could not read PE TimeDateStamp";
            uint stamp = BitConverter.ToUInt32(stampBytes);
            if (Array.IndexOf(AcceptedTimeDateStamps, stamp) < 0)
                return $"PE TimeDateStamp 0x{stamp:x8}, expected 0x{TimeDateStamp0695:x8} or 0x{ExpectedTimeDateStamp:x8}";
            return null;
        }

        [SupportedOSPlatform("windows")]
        public static AllocatorPatchResult TryApply(IntPtr process)
        {
            if (process == IntPtr.Zero)
                return AllocatorPatchResult.Failed("no process handle");

            byte[]? Read(ulong va, int len)
            {
                var buf = new byte[len];
                nuint read = 0;
                if (!Native.ReadProcessMemory(process, unchecked((IntPtr)(long)va), buf, (nuint)len, ref read) || (int)read != len)
                    return null;
                return buf;
            }

            if (ValidateTimeDateStamp(Read) is string stampFail)
                return AllocatorPatchResult.Failed(stampFail);
            if (ValidateSites(Read) is string siteFail)
                return AllocatorPatchResult.Failed(siteFail);

            IntPtr remote = Native.VirtualAllocEx(
                process, unchecked((IntPtr)(long)PreferredNewTable), (nuint)NewTableBytes,
                Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
            if (remote == IntPtr.Zero)
            {
                for (int i = 1; i <= 64 && remote == IntPtr.Zero; i++)
                {
                    remote = Native.VirtualAllocEx(
                        process, unchecked((IntPtr)(long)(PreferredNewTable + (ulong)(i * 0x10000))), (nuint)NewTableBytes,
                        Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
                }
            }
            if (remote == IntPtr.Zero)
                return AllocatorPatchResult.Failed("VirtualAllocEx near the image failed");

            ulong newBase = (ulong)remote;

            // Zero the new table (VirtualAllocEx already zeros committed pages; keep an explicit write for clarity).
            var zeros = new byte[NewTableBytes];
            if (!Write(process, remote, zeros))
                return AllocatorPatchResult.Failed("could not zero the new slot table");

            foreach (LeaSite site in Leas)
            {
                byte[] original = ParseHex(site.ExpectedHex);
                byte[] rewritten = RewriteLea(original, site.Va, newBase + (ulong)(uint)site.Add);
                if (!ProtectWriteVerify(process, unchecked((IntPtr)(long)site.Va), rewritten))
                    return AllocatorPatchResult.Failed($"could not rewrite LEA at 0x{site.Va:x}");
            }

            byte[] capPatched = ParseHex(CapExpectedHex);
            capPatched[3] = unchecked((byte)NewCount); // 0x7F
            if (!ProtectWriteVerify(process, unchecked((IntPtr)(long)CapVa), capPatched))
                return AllocatorPatchResult.Failed($"could not rewrite capacity at 0x{CapVa:x}");

            byte[] scanPatched = ParseHex(ScanExpectedHex);
            BitConverter.TryWriteBytes(scanPatched.AsSpan(3, 4), NewTableBytes);
            if (!ProtectWriteVerify(process, unchecked((IntPtr)(long)ScanVa), scanPatched))
                return AllocatorPatchResult.Failed($"could not rewrite scan bound at 0x{ScanVa:x}");

            return AllocatorPatchResult.Applied($"newTable=0x{newBase:x}");
        }

        [SupportedOSPlatform("windows")]
        static bool ProtectWriteVerify(IntPtr process, IntPtr address, byte[] bytes)
        {
            if (!Native.VirtualProtectEx(process, address, (nuint)bytes.Length, Native.PAGE_EXECUTE_READWRITE, out uint oldProtect))
                return false;
            try
            {
                if (!Write(process, address, bytes))
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

        [SupportedOSPlatform("windows")]
        static bool Write(IntPtr process, IntPtr address, byte[] bytes)
        {
            nuint written = 0;
            return Native.WriteProcessMemory(process, address, bytes, (nuint)bytes.Length, ref written)
                   && (int)written == bytes.Length;
        }

        static class Native
        {
            public const uint MEM_COMMIT = 0x1000;
            public const uint MEM_RESERVE = 0x2000;
            public const uint PAGE_READWRITE = 0x04;
            public const uint PAGE_EXECUTE_READWRITE = 0x40;

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, ref nuint lpNumberOfBytesRead);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, nuint nSize, ref nuint lpNumberOfBytesWritten);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, nuint dwSize);
        }
    }
}
