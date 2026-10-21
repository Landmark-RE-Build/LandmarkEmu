namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// GuildChangeMemberRankPacket (GuildBase 0x83 sub 0x0A) and its three senders in the 0.695 client, shared by the
    /// in-process guilds (WorldServer Game/Guilds.cs) and the social service (SocialServer/GuildService.cs)
    /// (docs/request-variants.md). Body: u64 guild, u64 member, string, u64, string rank name, u32, string comment, u8,
    /// u8, u32 [P].
    /// <list type="bullet">
    /// <item>The roster menu's Promote Rank / Demote Rank (RadialMenuButtons rows 42 / 46; Lua Guild.PromoteMember
    /// FUN_1435b7710 / DemoteMember FUN_1435b7900 → FUN_142ef91f0): the rank one step up or down (FUN_143c4edc0 /
    /// FUN_143c4ed20) by name, comment "Player promoted from roster menu.".</item>
    /// <item>Lua Guild.ChangeMemberRank(guild, member, rank name, comment) (FUN_1435b74d0): no shipped UI calls it.</item>
    /// <item>Leadership transfer: when the leader promotes a member into the leader's rank, FUN_142ef91f0 keeps the packet
    /// and asks "Transfer Guild Leadership" / "This action will cause you to lose your leadership rank in the Guild.  Is
    /// this what you want?" (Guild.ConfirmLeadershipTransferTitle / Msg, texts 564406 / 564407); Yes runs Lua
    /// Guild.TransferGuildLeadershipConfirm (FUN_1435b9cd0 → FUN_142efadc0), which sends the kept packet unchanged —
    /// the leader rank's name in the rank field.</item>
    /// </list>
    /// The guild log has a Guild.NewLeader entry (the client's log reader FUN_142efb6b0 handles it; text 563442
    /// "[*LeaderName*] has become the leader of [*GuildName*]!"), so live carried the transfer out. Both servers refused
    /// any change into the leader rank, so the confirmed transfer only answered "change rank failed".
    /// What rank the old leader takes is not in the client [I]: the member's former rank (the two swap).
    /// </summary>
    public static class GuildRankChanges
    {
        /// <summary>
        /// Is this rank change the confirmed leadership transfer: into the leader rank, asked by the guild's leader?
        /// </summary>
        public static bool IsLeadershipTransfer(uint newRank, uint changerRank, bool changerLeadsGuild, uint leaderRank)
            => newRank == leaderRank && changerRank == leaderRank && changerLeadsGuild;

        /// <summary>
        /// The rank the old leader takes after the transfer: the new leader's former rank [I] (the client's confirmation
        /// only says the old leader loses the leadership rank; no rank is named).
        /// </summary>
        public static uint FormerLeaderRank(uint newLeaderFormerRank, uint leaderRank, uint memberRank)
            => newLeaderFormerRank == leaderRank || newLeaderFormerRank == 0 ? memberRank : newLeaderFormerRank;
    }
}
