using LandmarkEmulator.Internal.Commerce;
using System;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The Player Studio staff queue text (commerce service "psqueue" reply) and the peers' rejection reasons
    /// (RejectReasons: the client's verdict codes, ReviewProduct FUN_1435e9f30, L185).
    /// </summary>
    public class StaffQueueTests
    {
        /// <summary>approve → 1; reject (type 1-3, subtype 0/1) → 2..7; anything else 0.</summary>
        [Theory]
        [InlineData(0u, "no reason")]
        [InlineData(2u, "Offensive, option 1")]
        [InlineData(3u, "Offensive, option 2")]
        [InlineData(4u, "Copyright (name), option 1")]
        [InlineData(5u, "Copyright (name), option 2")]
        [InlineData(6u, "Copyright (object or description), option 1")]
        [InlineData(7u, "Copyright (object or description), option 2")]
        [InlineData(9u, "code 9")]
        public void Label_FollowsTheClientsCodes(uint verdict, string label) => Assert.Equal(label, RejectReasons.Label(verdict));

        [Fact]
        public void Summary_CountsRejectionsMostFrequentFirst_WithoutApprovals()
        {
            Assert.Equal("Offensive, option 1 x2; no reason x1; Copyright (name), option 1 x1",
                RejectReasons.Summary(new uint[] { 0, 2, 1, 4, 2, 1 }));
            Assert.Equal("", RejectReasons.Summary(new uint[] { 1, 1 }));
            Assert.Equal("", RejectReasons.Summary(null));
        }

        [Fact]
        public void EncodeDecode_KeepsTheReasons()
        {
            var created = new DateTime(2026, 10, 18, 9, 30, 0, DateTimeKind.Utc);
            var entry = new StaffQueueEntry(42, "Tower\tof doom", "A\ntall one", "castle", "RobinHood", 500, 1, 3, created,
                "Offensive, option 1 x2; no reason x1");
            var back = StaffQueue.Decode(StaffQueue.Encode(new[] { entry }));
            Assert.Equal(1, back.Count);
            Assert.Equal(entry with { Name = "Tower of doom", Description = "A tall one" }, back[0]);
        }

        /// <summary>A line from a service that doesn't send the reasons yet (nine fields) still decodes.</summary>
        [Fact]
        public void Decode_NineFieldLine_HasNoReasons()
        {
            var back = StaffQueue.Decode("7\tName\tDesc\ttags\tOwner\t100\t0\t3\t1792368000");
            Assert.Equal(1, back.Count);
            Assert.Equal("", back[0].Reasons);
            Assert.Equal(3u, back[0].Rejections);
        }
    }
}
