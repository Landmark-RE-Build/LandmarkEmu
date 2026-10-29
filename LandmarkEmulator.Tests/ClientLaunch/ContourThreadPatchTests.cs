#nullable enable
using System;
using System.Collections.Generic;
using LandmarkEmulator.ClientLaunch;
using LandmarkEmulator.PatchPacks;
using Xunit;

namespace LandmarkEmulator.Tests.ClientLaunch
{
    public class ContourThreadPatchTests
    {
        [Theory]
        [InlineData(ContourThreadExpand.Off, AllocatorSlotExpand.On, 32, false)]
        [InlineData(ContourThreadExpand.On, AllocatorSlotExpand.Off, 32, false)]
        [InlineData(ContourThreadExpand.On, AllocatorSlotExpand.Auto, 8, false)]
        [InlineData(ContourThreadExpand.Auto, AllocatorSlotExpand.Auto, 16, true)]
        [InlineData(ContourThreadExpand.On, AllocatorSlotExpand.On, 1, true)]
        public void ShouldApply_RequiresAllocatorAttempt(
            ContourThreadExpand contour, AllocatorSlotExpand alloc, int cpus, bool expected)
            => Assert.Equal(expected, ContourThreadPatch.ShouldApply(contour, alloc, cpus));

        [Fact]
        public void ClampToAllocator_ForcesOffWhenAllocatorOff()
        {
            Assert.Equal(ContourThreadExpand.Off,
                ContourThreadPatch.ClampToAllocator(ContourThreadExpand.On, AllocatorSlotExpand.Off));
            Assert.Equal(ContourThreadExpand.On,
                ContourThreadPatch.ClampToAllocator(ContourThreadExpand.On, AllocatorSlotExpand.On));
        }

        [Fact]
        public void ValidateSites_AcceptsExpectedBytes()
        {
            var map = BuildMap();
            Assert.Null(ContourThreadPatch.ValidateSites((va, len) => Read(map, va, len)));
        }

        [Fact]
        public void ValidateSites_RejectsTamperedSite()
        {
            var map = BuildMap();
            map[ContourThreadPatch.Sites[0].Va][1] = 0x99;
            string? reason = ContourThreadPatch.ValidateSites((va, len) => Read(map, va, len));
            Assert.NotNull(reason);
            Assert.Contains("C1a", reason);
        }

        [Fact]
        public void PatchHex_IsNewCount()
        {
            Assert.Equal(5, ContourThreadPatch.NewCount);
            foreach (var site in ContourThreadPatch.Sites)
            {
                byte[] patch = AllocatorSlotPatch.ParseHex(site.PatchHex);
                Assert.Contains((byte)ContourThreadPatch.NewCount, patch);
            }
        }

        static Dictionary<ulong, byte[]> BuildMap()
        {
            var map = new Dictionary<ulong, byte[]>();
            foreach (var site in ContourThreadPatch.Sites)
                map[site.Va] = AllocatorSlotPatch.ParseHex(site.ExpectedHex);
            return map;
        }

        static byte[]? Read(Dictionary<ulong, byte[]> map, ulong va, int len)
        {
            if (!map.TryGetValue(va, out byte[]? bytes) || bytes.Length < len)
                return null;
            return bytes.AsSpan(0, len).ToArray();
        }
    }
}
