using LandmarkEmulator.Internal.Social;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// GuildChangeMemberRankPacket's third sender (Internal/Social/GuildRankChanges.cs, docs/request-variants.md): the
    /// leader's confirmed "Transfer Guild Leadership" (Guild.TransferGuildLeadershipConfirm FUN_1435b9cd0 sends the packet
    /// FUN_142ef91f0 kept, naming the leader rank). Before, both servers refused every change into the leader rank.
    /// </summary>
    public class GuildRankChangeRuleTests
    {
        private const uint Leader = 1, Member = 2, Officer = 3;

        [Fact]
        public void TheLeader_PromotingIntoTheLeaderRank_IsATransfer()
            => Assert.True(GuildRankChanges.IsLeadershipTransfer(Leader, Leader, changerLeadsGuild: true, Leader));

        [Fact]
        public void AnyoneElse_OrAnotherRank_IsNoTransfer()
        {
            Assert.False(GuildRankChanges.IsLeadershipTransfer(Leader, Officer, changerLeadsGuild: false, Leader));
            Assert.False(GuildRankChanges.IsLeadershipTransfer(Leader, Leader, changerLeadsGuild: false, Leader));
            Assert.False(GuildRankChanges.IsLeadershipTransfer(Officer, Leader, changerLeadsGuild: true, Leader));
            Assert.False(GuildRankChanges.IsLeadershipTransfer(Member, Leader, changerLeadsGuild: true, Leader));
        }

        [Fact]
        public void TheOldLeader_TakesTheNewLeadersFormerRank()
        {
            Assert.Equal(Officer, GuildRankChanges.FormerLeaderRank(Officer, Leader, Member));
            Assert.Equal(Member, GuildRankChanges.FormerLeaderRank(Member, Leader, Member));
            // Never the leader rank twice, nor no rank.
            Assert.Equal(Member, GuildRankChanges.FormerLeaderRank(Leader, Leader, Member));
            Assert.Equal(Member, GuildRankChanges.FormerLeaderRank(0, Leader, Member));
        }
    }
}
