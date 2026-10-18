using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The per-world world-channel back end (`worldchannel`, one per world; trace/architecture/world-channel-service.md
    /// §5.2): the state tied to this world's players that the client reaches on channel 1 (World) — the trade broker,
    /// the encounter lifecycle, claim votes, the logout-to-character-select session, the client's locale, the teleport
    /// queue and the telemetry sink. Live ran it behind the world's gateway; the zone keeps inventories, combat and
    /// positions.
    ///
    /// Build step 1 (2026-10-15): an interface split inside the world server, no behaviour change. Today's code is the
    /// in-process implementation (<see cref="LocalWorldChannelService"/>); a later step puts a network client behind
    /// the same calls (internal-transport.md). Every method is a client World-channel message or one of the zone ↔
    /// worldchannel messages of the spec (§6.4, §6.5), named after them.
    /// </summary>
    public interface IWorldChannelService
    {
        // ---- trade broker (TradeBase 0x3E: Initiate 1, InviteReply 3, Cancel 7 come on World; §3.3, §6.4) ----------
        // Build step 5 (2026-10-16): with the world-channel container running, those three subs never reach the zone —
        // LandmarkEmulator.WorldChannel.TradeBroker answers them and drives the zone's Trades through
        // TradeZoneSessionStart / Open / End (WorldChannelLink). These calls are the in-process path (no container, or
        // the gateway sending channel 1 to the zone while it is away), and the zone-side events below report a brokered
        // trade's end back to the service from inside Trades.

        /// <summary>A World-channel trade request (<see cref="Trades.IsWorldChannelSub"/>).</summary>
        void TradeRequest(WorldSession session, byte sub, byte[] body);

        /// <summary>A trader left the world: the partner is told and the trade ends (spec TradeZoneSessionEnd, code 2).</summary>
        void TradePartyLeft(WorldSession session);

        /// <summary>
        /// A trader changed landscape or entered a running instanced game: the trade ends (spec TradeZoneSessionEnd,
        /// codes 1 / 8 / 9).
        /// </summary>
        void TradePartyMoved(WorldSession session, bool intoGame);

        // ---- encounters (EncounterBase 0x2C requests, all on World; §3.4, §6.5) -------------------------------------
        // Build step 7 (2026-10-16): with the world-channel container running, LandmarkEmulator.WorldChannel.EncounterBroker
        // answers the client's encounter requests and sends the lifecycle packets; the zone's Encounters keeps the duel
        // combat rules from the EncounterParticipants list it pushes and reports its decisions back (EncounterCreateDuel,
        // EncounterDuelAnswer, EncounterForfeit through WorldChannelLink). These calls stay the entry points: Encounters
        // routes them to the service when one is linked and runs the lifecycle itself otherwise (spec §7).

        /// <summary>An EncounterBase request from the client: entrance, exit, cancel pending, invitation / duel answers.</summary>
        void EncounterRequest(WorldSession session, ushort sub, byte[] body);

        /// <summary>/duel name (spec EncounterCreateDuel: the zone command asks for a duel).</summary>
        void DuelChallenge(WorldSession session, string name);

        /// <summary>/duelaccept, /dueldecline.</summary>
        void DuelAnswer(WorldSession session, ulong inviterGuid, bool accept);

        /// <summary>A participant leaves its encounter (spec EncounterForfeit: emote, logout).</summary>
        void EncounterLeave(WorldSession session, string why);

        // ---- claim votes (RatingBase 0xA7 "Claims"; §3.2) -----------------------------------------------------------
        // Build step 6 (2026-10-16): with the world-channel container running, AddVote "Claims" and CanVoteRequest never
        // reach the zone — LandmarkEmulator.WorldChannel.RatingBroker answers them from its own store and asks / tells the
        // zone through WorldChannelLink (ClaimInfoQuery, RatingVoteCounted / Refused → Ratings). These calls are the
        // in-process path and the fallback while it is away (spec §7); ClaimVoteTotal reads the zone's kind-11 read copy.

        /// <summary>RatingBase AddVote (sub 8), both categories ("Contests" is relayed to commerce).</summary>
        void ClaimVote(WorldSession session, byte[] body);

        /// <summary>RatingBase CanVoteRequest (sub 0x18).</summary>
        void CanVote(WorldSession session, byte[] body);

        /// <summary>How many players rated a build site (the Showcase gallery).</summary>
        ulong ClaimVoteTotal(ulong claimId);

        // ---- session ------------------------------------------------------------------------------------------------

        /// <summary>CharacterSelectSessionRequest 0xF6 (Lua Ui.Logout): a fresh login session for character select.</summary>
        void CharacterSelectSession(WorldSession session);

        /// <summary>PacketSetLocale 0x5B (C→S only, StringFixed&lt;8&gt;; sent once per world entry).</summary>
        void SetLocale(WorldSession session, byte[] body);

        /// <summary>
        /// The teleport queue: TeleportBase 0xFC sub 6 TeleportToQueuedLocation, sub 7 TeleportCancelQueue (both on
        /// World; Game/TravelQueue, L60). False = not handled.
        /// </summary>
        bool TeleportQueueRequest(WorldSession session, byte sub, byte[] body);

        /// <summary>
        /// The telemetry sink: PacketClientLuaMetrics 0x8B, PlayerUpdateUpdateSteamInfo, WallOfData, ItemReview OptInAccount
        /// (§3.1, §3.6; no answer). WorldSession hands every <see cref="Telemetry.IsTelemetry"/> opcode here before
        /// the message lookup (TASKS E.7); the local back end records it with <see cref="Telemetry"/>.
        /// </summary>
        void ClientTelemetry(WorldSession session, ZoneMessageOpcode opcode, byte[] body);

        // ---- tick ---------------------------------------------------------------------------------------------------

        /// <summary>Server tick: stale duel challenges expire, duellists out of range forfeit.</summary>
        void Update();
    }
}
