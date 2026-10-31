using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The Player Studio reviewer rule without the world tick's database reads (review 2026-10-18b #3) and the Founder's
    /// Chest click cooldown (#5): what can be answered from memory is answered at once, on the caller's thread.
    /// </summary>
    public class ReviewSpendRuleTests
    {
        private static void SetSpent(ulong character, uint lumens)
        {
            var cache = (ConcurrentDictionary<ulong, HashSet<(uint Kind, uint Value)>>)Private.Field(typeof(Unlocks), "cache");
            cache[character] = lumens == 0 ? new HashSet<(uint, uint)>() : new HashSet<(uint, uint)> { (Unlocks.StoreSpent, lumens) };
        }

        private static bool? MayReviewNow(WorldSession session)
        {
            bool? answer = null;
            Commerce.MayReview(session, ok => answer = ok);
            return answer;
        }

        [Fact]
        public void NoSpendNeeded_AnswersAtOnce()
        {
            using var world = new FakeWorld(c => { c.PlayerStudioReviewerMinSpend = 0; c.PlayerStudioReviewerOverrules = 0; });
            WorldSession s = world.Session(0x7100_0001, "Reviewer");
            SetSpent(s.PlayerGuid, 0);
            Assert.Equal(true, MayReviewNow(s));
        }

        [Fact]
        public void OwnSpendEnough_AnswersAtOnce()
        {
            using var world = new FakeWorld(c => { c.PlayerStudioReviewerMinSpend = 1000; c.PlayerStudioReviewerOverrules = 0; });
            WorldSession s = world.Session(0x7100_0002, "Spender");
            SetSpent(s.PlayerGuid, Commerce.ReviewerMinLumens);
            Assert.Equal(true, MayReviewNow(s));
        }

        /// <summary>
        /// The overrule rule (L185, PlayerStudioReviewerOverrules) answers at once from a count already taken, and holds a
        /// reviewer back once staff overruled that many of their rejections.
        /// </summary>
        [Fact]
        public void OverruleCountKnown_AnswersAtOnce()
        {
            using var world = new FakeWorld(c => { c.PlayerStudioReviewerMinSpend = 0; c.PlayerStudioReviewerOverrules = 1; });
            WorldSession clean = world.Session(0x7100_0005, "Fair"), overruled = world.Session(0x7100_0006, "Harsh");
            var counts = (ConcurrentDictionary<ulong, (int Count, System.DateTime At)>)Private.Field(typeof(Commerce), "overruledCount");
            counts[clean.PlayerGuid] = (0, System.DateTime.UtcNow);
            counts[overruled.PlayerGuid] = (1, System.DateTime.UtcNow);
            Assert.Equal(true, MayReviewNow(clean));
            Assert.Equal(false, MayReviewNow(overruled));
            counts.TryRemove(clean.PlayerGuid, out _);
            counts.TryRemove(overruled.PlayerGuid, out _);
        }

        [Fact]
        public void NotEnough_WithoutAccount_NoDatabase()
        {
            // No account: no other characters to add, so no query and an answer at once.
            using var world = new FakeWorld(c => c.PlayerStudioReviewerMinSpend = 1000);
            WorldSession s = world.Session(0x7100_0003, "Saver");
            SetSpent(s.PlayerGuid, 100);
            Assert.Equal(false, MayReviewNow(s));
            long? total = null;
            Commerce.AccountSpent(s, t => total = t);
            Assert.Equal(100L, total);
        }

        [Fact]
        public void FounderChest_SecondClickWithinCooldown_IsIgnored()
        {
            // The founder package is off: the first click is answered (562156 in chat), the second within the cooldown not.
            using var world = new FakeWorld(c => c.FounderEntitlements = false);
            WorldSession s = world.Session(0x7100_0004, "Founder");
            FakeWorld.Outgoing(s);
            Entitlements.RestoreFounderItems(s);
            Assert.NotEmpty(FakeWorld.Outgoing(s));
            Entitlements.RestoreFounderItems(s);
            Assert.Empty(FakeWorld.Outgoing(s));
        }
    }
}
