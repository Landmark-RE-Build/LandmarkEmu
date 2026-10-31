using System.Collections.Generic;
using LandmarkEmulator.WorldServer.Game;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// RemoveAddBinding (GameScripting sub 0x16 → reply 11) replaces a Link in one step (StoryTools.ReplaceLink). The
    /// 0.695 reply handler FUN_142fef9b0 only tests result == 0: on any other result it says "Link modification
    /// failure." and keeps showing the old Link, so the server must keep it too (trace/0695/rebase-695.md G1). The
    /// reply's result table (FUN_142f47140) numbers the single-binding refusal 5, not AddBinding's 4.
    /// </summary>
    public class StoryToolReplaceLinkRuleTests
    {
        private readonly object gate = new();

        [Fact]
        public void AStoredNewLink_ForgetsTheOldOne()
        {
            var links = new List<int> { 1, 2 };
            var forgotten = new List<int>();
            uint result = StoryTools.ReplaceLink(links, gate, 1, () => { links.Add(3); return 0; }, forgotten.Add);
            Assert.Equal(0u, result);
            Assert.Equal(new List<int> { 2, 3 }, links);
            Assert.Equal(new List<int> { 1 }, forgotten);
        }

        [Fact]
        public void ARefusedNewLink_KeepsTheOldOne()
        {
            var links = new List<int> { 1, 2 };
            var forgotten = new List<int>();
            uint result = StoryTools.ReplaceLink(links, gate, 1, () => 1u, forgotten.Add);
            Assert.Equal(1u, result);
            Assert.Contains(1, links);
            Assert.Equal(2, links.Count);
            Assert.Empty(forgotten);
        }

        [Fact]
        public void TheOldLink_IsNotCountedWhileTheNewOneIsChecked()
        {
            // Replacing a Link by the same one again is no duplicate: the old one is out while the add runs.
            var links = new List<int> { 7 };
            uint result = StoryTools.ReplaceLink(links, gate, 7, () => links.Contains(7) ? 3u : 0u, _ => { });
            Assert.Equal(0u, result);
        }

        [Fact]
        public void TheSingleBindingRefusal_IsFiveInThisReply()
        {
            var links = new List<int> { 1 };
            Assert.Equal(StoryTools.RemoveAddSingleBinding, StoryTools.ReplaceLink(links, gate, 1, () => 4u, _ => { }));
            Assert.Equal(5u, StoryTools.RemoveAddSingleBinding);
            Assert.Equal(4u, StoryTools.RemoveAddRemoveFailed);
            Assert.Contains(1, links);
        }
    }
}
