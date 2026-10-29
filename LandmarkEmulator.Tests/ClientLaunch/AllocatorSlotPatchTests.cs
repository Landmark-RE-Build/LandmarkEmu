using System;
using System.Collections.Generic;
using LandmarkEmulator.ClientLaunch;
using LandmarkEmulator.PatchPacks;
using Xunit;

namespace LandmarkEmulator.Tests.ClientLaunch
{
    /// <summary>
    /// Pure validation / LEA rewrite math for the experimental 64→127 allocator expand.
    /// Synthetic buffers only — no Landmark64.exe bytes beyond the short expected instruction hex in code.
    /// </summary>
    public class AllocatorSlotPatchTests
    {
        [Theory]
        [InlineData(AllocatorSlotExpand.Off, 32, false)]
        [InlineData(AllocatorSlotExpand.Off, 64, false)]
        [InlineData(AllocatorSlotExpand.Auto, 15, false)]
        [InlineData(AllocatorSlotExpand.Auto, 16, true)]
        [InlineData(AllocatorSlotExpand.Auto, 32, true)]
        [InlineData(AllocatorSlotExpand.On, 1, true)]
        [InlineData(AllocatorSlotExpand.On, 64, true)]
        public void ShouldApply_RespectsModeAndCpuCount(AllocatorSlotExpand mode, int cpus, bool expected)
            => Assert.Equal(expected, AllocatorSlotPatch.ShouldApply(mode, cpus));

        [Fact]
        public void ValidateSites_Match_ReturnsNull()
        {
            Dictionary<ulong, byte[]> map = GoodMap();
            Assert.Null(AllocatorSlotPatch.ValidateSites((va, len) => Read(map, va, len)));
        }

        [Fact]
        public void ValidateSites_SingleByteMismatch_RejectsWithoutNeedingWrites()
        {
            Dictionary<ulong, byte[]> map = GoodMap();
            map[AllocatorSlotPatch.CapVa][3] = 0x41; // was 0x40
            string reason = AllocatorSlotPatch.ValidateSites((va, len) => Read(map, va, len));
            Assert.NotNull(reason);
            Assert.Contains("capacity", reason);
        }

        [Fact]
        public void ValidateSites_LeaByteMismatch_Rejects()
        {
            Dictionary<ulong, byte[]> map = GoodMap();
            ulong va = AllocatorSlotPatch.Leas[0].Va;
            map[va][0] ^= 0x01;
            string reason = AllocatorSlotPatch.ValidateSites((va2, len) => Read(map, va2, len));
            Assert.NotNull(reason);
            Assert.Contains("0x142c050c5", reason);
        }

        [Fact]
        public void LeaTarget_And_RewriteLea_RetargetDisp32()
        {
            byte[] original = AllocatorSlotPatch.ParseHex(AllocatorSlotPatch.Leas[0].ExpectedHex);
            ulong insnVa = AllocatorSlotPatch.Leas[0].Va;
            Assert.Equal(AllocatorSlotPatch.OldTable, AllocatorSlotPatch.LeaTarget(insnVa, original));

            const ulong neu = 0x1461B0000UL;
            byte[] rewritten = AllocatorSlotPatch.RewriteLea(original, insnVa, neu);
            Assert.Equal(neu, AllocatorSlotPatch.LeaTarget(insnVa, rewritten));
            // Opcode / ModRM preserved; only disp32 changes.
            Assert.Equal(original[0], rewritten[0]);
            Assert.Equal(original[1], rewritten[1]);
            Assert.Equal(original[2], rewritten[2]);
            Assert.False(original.AsSpan(3, 4).SequenceEqual(rewritten.AsSpan(3, 4)));
        }

        [Fact]
        public void RewriteLea_TablePlus238_Site()
        {
            var site = AllocatorSlotPatch.Leas[6]; // add 0x238
            byte[] original = AllocatorSlotPatch.ParseHex(site.ExpectedHex);
            Assert.Equal(AllocatorSlotPatch.OldTable + 0x238UL, AllocatorSlotPatch.LeaTarget(site.Va, original));

            const ulong neu = 0x1461B0000UL;
            byte[] rewritten = AllocatorSlotPatch.RewriteLea(original, site.Va, neu + 0x238UL);
            Assert.Equal(neu + 0x238UL, AllocatorSlotPatch.LeaTarget(site.Va, rewritten));
        }

        [Fact]
        public void ValidateTimeDateStamp_RejectsWrongStamp()
        {
            var image = new byte[0x200];
            BitConverter.TryWriteBytes(image.AsSpan(0x3C, 4), 0x80); // e_lfanew
            BitConverter.TryWriteBytes(image.AsSpan(0x80 + 8, 4), 0xDEADBEEFu);
            string reason = AllocatorSlotPatch.ValidateTimeDateStamp((va, len) =>
            {
                int off = (int)(va - AllocatorSlotPatch.ImageBase);
                if (off < 0 || off + len > image.Length)
                    return null;
                var slice = new byte[len];
                Buffer.BlockCopy(image, off, slice, 0, len);
                return slice;
            });
            Assert.NotNull(reason);
            Assert.Contains("TimeDateStamp", reason);
        }

        [Theory]
        [InlineData(AllocatorSlotPatch.TimeDateStamp0695)]
        [InlineData(AllocatorSlotPatch.ExpectedTimeDateStamp)]
        public void ValidateTimeDateStamp_AcceptsKnownBuilds(uint stamp)
        {
            var image = new byte[0x200];
            BitConverter.TryWriteBytes(image.AsSpan(0x3C, 4), 0x80);
            BitConverter.TryWriteBytes(image.AsSpan(0x80 + 8, 4), stamp);
            Assert.Null(AllocatorSlotPatch.ValidateTimeDateStamp((va, len) =>
            {
                int off = (int)(va - AllocatorSlotPatch.ImageBase);
                var slice = new byte[len];
                Buffer.BlockCopy(image, off, slice, 0, len);
                return slice;
            }));
        }

        [Fact]
        public void FailedResult_HasUserVisibleMessage()
        {
            AllocatorPatchResult r = AllocatorPatchResult.Failed("LEA at 0x142c050c5 bytes mismatch");
            Assert.Equal(AllocatorPatchStatus.Failed, r.Status);
            Assert.Contains("0.695", r.UserMessage);
            Assert.Contains("0.699", r.UserMessage);
            Assert.Contains("LEA at 0x142c050c5", r.UserMessage);
        }

        static Dictionary<ulong, byte[]> GoodMap()
        {
            var map = new Dictionary<ulong, byte[]>();
            foreach (var site in AllocatorSlotPatch.Leas)
                map[site.Va] = AllocatorSlotPatch.ParseHex(site.ExpectedHex);
            map[AllocatorSlotPatch.CapVa] = AllocatorSlotPatch.ParseHex(AllocatorSlotPatch.CapExpectedHex);
            map[AllocatorSlotPatch.ScanVa] = AllocatorSlotPatch.ParseHex(AllocatorSlotPatch.ScanExpectedHex);
            return map;
        }

        static byte[] Read(Dictionary<ulong, byte[]> map, ulong va, int len)
        {
            if (!map.TryGetValue(va, out byte[] bytes) || bytes.Length < len)
                return null;
            var slice = new byte[len];
            Buffer.BlockCopy(bytes, 0, slice, 0, len);
            return slice;
        }
    }
}
