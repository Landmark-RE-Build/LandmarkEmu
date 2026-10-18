namespace LandmarkEmulator.WorldChannel
{
    /// <summary>WorldChannel.json, overridable by environment variables with the same names, like the other servers.</summary>
    public class WorldChannelConfiguration
    {
        /// <summary>The world's gateway internal port (it routes channel 1 here): gateway-&lt;WORLD_KEY&gt;:20300.</summary>
        public string GatewayInternalAddress { get; set; } = "";

        /// <summary>The world's zone process internal port (ZONE_INTERNAL_PORT, 20310): channel-1 packets are handed on there.</summary>
        public string ZoneInternalAddress { get; set; } = "";

        /// <summary>The login service's internal port (LOGIN_INTERNAL_ADDRESS): relog sessions for logout to character select.</summary>
        public string LoginInternalAddress { get; set; } = "";

        /// <summary>
        /// The trade broker (world-channel-service.md §6.4, build step 5): trade invitations (0x3E subs 1 / 3 / 7) are
        /// answered here and the zone runs the exchange. False hands them on to the zone unchanged (step-2 pass-through).
        /// </summary>
        public bool TradeBroker { get; set; } = true;

        /// <summary>
        /// The encounter broker (world-channel-service.md §6.5, build step 7): the encounter lifecycle (duel challenges,
        /// 0x2C subs 0x67 / 0x69 / 0x6C / 0x6D / 0x7C and the S→C lifecycle packets) runs here and the zone keeps the duel
        /// combat rules from the participant list pushed to it. False hands the packets on to the zone unchanged.
        /// </summary>
        public bool EncounterBroker { get; set; } = true;

        /// <summary>
        /// Build-site ratings (world-channel-service.md §3.2, §6.5, build step 6): the "Claims" likes (0xA7 AddVote) and the
        /// can-vote query (0xA7 0x18) answered here from this service's own store (<see cref="Database"/>). False, or no
        /// database, hands them on to the zone unchanged (the zone's Ratings.cs answers, as before).
        /// </summary>
        public bool ClaimRatings { get; set; } = true;

        /// <summary>
        /// This service's own database (spec §6.1): a MySQL / MariaDB connection string naming schema
        /// lmemu_wc_&lt;WORLD_KEY&gt;, created at start when missing (table claim_vote). Empty = no store (ratings stay in the zone).
        /// </summary>
        public string Database { get; set; } = "";

        /// <summary>
        /// The zone's character database whose character_unlock kind-11 rows (the likes before step 6) are copied into
        /// claim_vote: all of them while claim_vote is empty, afterwards the ones the zone counted while this service was
        /// away. Normally lmemu_character; empty = no copy.
        /// </summary>
        public string ImportFrom { get; set; } = "";

        /// <summary>The world this service belongs to (its zone_server id).</summary>
        public uint WorldId { get; set; } = 1;
    }
}
