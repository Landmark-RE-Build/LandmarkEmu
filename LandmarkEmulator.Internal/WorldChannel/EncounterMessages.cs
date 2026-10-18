using LandmarkEmulator.Internal.Social;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Internal.WorldChannel
{
    /// <summary>
    /// The encounter broker's messages between a world's world-channel service and its zone
    /// (trace/architecture/world-channel-service.md §3.4, §4 "Encounters / duels", §6.5; build step 7; transport:
    /// internal-transport.md §3).
    /// <para>
    /// Why the split: every encounter request the client sends goes on the <b>World</b> channel — RequestEntrance 0x6C
    /// (FUN_142cbe6e0 / 0.695 FUN_142d57a70), RequestExit 0x6D (FUN_142ca26b0 / FUN_142d3bbd0), CancelPending 0x7C
    /// (FUN_142c0cfb0 / FUN_142ca6d90), InvitationResponse 0x67 (FUN_143350240 / FUN_1433fc300), DuelInvitationResponse
    /// 0x69 (FUN_1433501d0 / FUN_1433fc290) — so on live a World back end ran the encounter <b>lifecycle</b> (invite,
    /// details, zone ready, enter, exit, terminate) while the zone, which has positions and health, ran the <b>combat</b>
    /// (the duel rules: who may hit whom, the forfeit distance, the death window 0x7D / 0x7E). The client reads every
    /// S→C encounter sub in one handler (FUN_142c428b0 → manager FUN_143310250) whatever channel it came on.
    /// </para>
    /// <para>
    /// Every message here is <b>ours</b> (live's internal layouts are not known, L48): the client family EncounterBase
    /// 0x2C and a sub from 0x10000 up (spec §6.2), so they can never be taken for a client sub (i16). They travel as a
    /// <see cref="ServicePacket"/> on the world channel's link to the zone (ZONE_INTERNAL_PORT). Ids 0x10050-0x10052 are
    /// the spec's (§6.5); <see cref="EncounterDuelAnswer"/> 0x10053 and <see cref="EncounterNotice"/> 0x10054 are additions
    /// (see their comments; internal-messages.md §2).
    /// </para>
    /// </summary>
    public static class EncounterMessages
    {
        /// <summary>EncounterBase 0x2C (the client's family; its subs are i16).</summary>
        public const ushort EncounterFamily = 0x2C;

        public const uint EncounterParticipantsSub = 0x10050, EncounterCreateDuelSub = 0x10051, EncounterForfeitSub = 0x10052,
            EncounterDuelAnswerSub = 0x10053, EncounterNoticeSub = 0x10054;

        /// <summary>
        /// Encounter ids the world channel hands out start here, so they never meet the ids the zone gives the
        /// encounters it runs itself (in-process mode, or while the world channel is away; those count up from 1). The
        /// client keeps the id as a u32 (game +0x32f28) and echoes it in the header of every encounter request.
        /// </summary>
        public const uint FirstBrokerEncounterId = 0x4000_0001;

        /// <summary><see cref="EncounterParticipants.State"/>: the encounter ended; the zone forgets it (no packets: the service sent Terminate / Shutdown).</summary>
        public const uint StateEnded = 0;
        /// <summary><see cref="EncounterParticipants.State"/>: the participants were told their zone is ready; not all have entered yet.</summary>
        public const uint StateOpen = 1;
        /// <summary><see cref="EncounterParticipants.State"/>: every participant entered (PlayerEnter sent for each): the duel rules apply.</summary>
        public const uint StateRunning = 2;

        /// <summary>
        /// <see cref="EncounterForfeit.Reason"/>: why a participant is out, as the zone's Encounters.Leave names it
        /// ("left", "defeated", "logout"; the range / landscape check of its tick is "range"). The service picks the
        /// chat lines the in-process code printed from it (defeated → "was defeated" / "You lost the duel."; anything
        /// else → "left the duel" / "You left the duel.", nothing to a player who logged out).
        /// </summary>
        public const byte ReasonLeft = 1, ReasonDefeated = 2, ReasonRange = 3, ReasonLogout = 4, ReasonLandscape = 5, ReasonDeclined = 6;

        /// <summary>The encounter message in a service packet, or null when it isn't one of these.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            if (packet.Family != EncounterFamily)
                return null;
            var r = new InternalReader(packet.Body);
            return packet.Sub switch
            {
                EncounterParticipantsSub => EncounterParticipants.Read(r),
                EncounterCreateDuelSub => EncounterCreateDuel.Read(r),
                EncounterForfeitSub => EncounterForfeit.Read(r),
                EncounterDuelAnswerSub => EncounterDuelAnswer.Read(r),
                EncounterNoticeSub => EncounterNotice.Read(r),
                _ => null
            };
        }
    }

    /// <summary>
    /// EncounterParticipants (world channel → zone, 0x2C/0x10050; spec §6.5): u32 encounter, string kind, u32 state
    /// (<see cref="EncounterMessages.StateEnded"/> / Open / Running), u32 n, n × u64 character. Pushed on every state
    /// change of an encounter the service runs — started (Open), every participant entered (Running), a participant
    /// left (Open / Running with the rest), ended (Ended, with whoever was still in it) — so the zone's duel rules keep
    /// working from this list: Encounters.Duelling (both in one Running duel), Hit (only the opponent takes the blow),
    /// the forfeit-distance tick and the death window's encounter id (IdOf). The zone sends no client packet for it.
    /// </summary>
    public sealed class EncounterParticipants : ISocialMessage
    {
        public ushort Family => EncounterMessages.EncounterFamily;
        public uint Sub => EncounterMessages.EncounterParticipantsSub;
        public uint EncounterId { get; init; }
        public string Kind { get; init; } = "";
        public uint State { get; init; }
        public IReadOnlyList<ulong> Characters { get; init; } = Array.Empty<ulong>();

        public void Write(InternalWriter w)
        {
            w.U32(EncounterId).Str(Kind).U32(State).U32((uint)Characters.Count);
            foreach (ulong c in Characters)
                w.U64(c);
        }

        public static EncounterParticipants Read(InternalReader r)
        {
            uint id = r.U32();
            string kind = r.Str();
            uint state = r.U32();
            uint n = r.U32();
            var chars = new List<ulong>();
            for (uint i = 0; i < n && i < 256; i++)
                chars.Add(r.U64());
            return new() { EncounterId = id, Kind = kind, State = state, Characters = chars };
        }
    }

    /// <summary>
    /// EncounterCreateDuel (zone → world channel, 0x2C/0x10051; spec §6.5): u64 challenger, u64 target — the spec's
    /// fields — then (ours, added) u64 challenger connection, string challenger name, u64 target connection, string
    /// target name. The zone's /duel command (Emotes.Run → Encounters.Challenge) checked what only it knows — both
    /// online on one landscape within the duel range (150 m), neither in an encounter — and asks the service to open
    /// the challenge. The connections and names are what the service needs to address the two clients
    /// (TunnelPacketToExternalConnection) and to word the chat lines; it has no presence table of its own yet (the
    /// gateway sends connection events only to the zone), the same gap TradeZoneSessionReady fills for trades.
    /// </summary>
    public sealed class EncounterCreateDuel : ISocialMessage
    {
        public ushort Family => EncounterMessages.EncounterFamily;
        public uint Sub => EncounterMessages.EncounterCreateDuelSub;
        public ulong Challenger { get; init; }
        public ulong Target { get; init; }
        public ulong ChallengerConnection { get; init; }
        public string ChallengerName { get; init; } = "";
        public ulong TargetConnection { get; init; }
        public string TargetName { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(Challenger).U64(Target).U64(ChallengerConnection).Str(ChallengerName)
            .U64(TargetConnection).Str(TargetName);
        public static EncounterCreateDuel Read(InternalReader r) => new()
        {
            Challenger = r.U64(), Target = r.U64(), ChallengerConnection = r.U64(), ChallengerName = r.Str(),
            TargetConnection = r.U64(), TargetName = r.Str()
        };
    }

    /// <summary>
    /// EncounterForfeit (zone → world channel, 0x2C/0x10052; spec §6.5): u32 encounter, u64 character, u8 reason
    /// (<see cref="EncounterMessages.ReasonLeft"/>…). The zone's combat rules took a participant out of an encounter
    /// the service runs: a killing blow in a duel (Encounters.Hit, "defeated"), the duellists drifting further apart
    /// than the duel range or onto different landscapes (Encounters.Update, "range"), /duelleave ("left"), or the
    /// player leaving the world (PlayerManager, "logout"). The service ends the participant's part (Shutdown 6 to the
    /// leaver; a duel then ends for the winner with Terminate 0x79 + Shutdown) and pushes
    /// <see cref="EncounterParticipants"/>. Encounter 0 with reason logout: the character is gone, drop any open
    /// duel challenge of theirs (the zone had no encounter to name).
    /// </summary>
    public sealed class EncounterForfeit : ISocialMessage
    {
        public ushort Family => EncounterMessages.EncounterFamily;
        public uint Sub => EncounterMessages.EncounterForfeitSub;
        public uint EncounterId { get; init; }
        public ulong Character { get; init; }
        public byte Reason { get; init; }

        public void Write(InternalWriter w) => w.U32(EncounterId).U64(Character).U8(Reason);
        public static EncounterForfeit Read(InternalReader r) => new() { EncounterId = r.U32(), Character = r.U64(), Reason = r.U8() };
    }

    /// <summary>
    /// EncounterDuelAnswer (ours, zone → world channel, 0x2C/0x10053; not in the spec): u64 character, u64 inviter (0 =
    /// whoever challenged), u8 accept. The /duelaccept and /dueldecline commands are <b>zone</b> commands (the zone's
    /// dynamic command list, Command 0x44; Emotes.Run → Encounters.Answer), so their answer has to reach the service
    /// that holds the challenge; the client's own DuelInvitationResponse 0x69 comes on the World channel and reaches
    /// the service directly. Same meaning as 0x69 {u64 inviter, u8 accept} (FUN_1433501d0).
    /// </summary>
    public sealed class EncounterDuelAnswer : ISocialMessage
    {
        public ushort Family => EncounterMessages.EncounterFamily;
        public uint Sub => EncounterMessages.EncounterDuelAnswerSub;
        public ulong Character { get; init; }
        public ulong Inviter { get; init; }
        public bool Accept { get; init; }

        public void Write(InternalWriter w) => w.U64(Character).U64(Inviter).Bool(Accept);
        public static EncounterDuelAnswer Read(InternalReader r) => new() { Character = r.U64(), Inviter = r.U64(), Accept = r.Bool() };
    }

    /// <summary>
    /// EncounterNotice (ours, world channel → zone, 0x2C/0x10054; not in the spec): u64 character, string text. A
    /// system chat line for one player, printed by the zone through its usual path (Chat.System → PacketShowSystemMessage
    /// with client text 558682 "[*message*]"), so the duel texts the in-process code printed ("You challenged X to a
    /// duel.", "X challenges you to a duel. Type /duelaccept or /dueldecline.", "Duel: A vs B. Fight!", "X was defeated.
    /// You win!", "You lost the duel." …) reach the client byte-identical whichever side runs the lifecycle. 0.589 has
    /// no duel UI text of its own (docs/encounters.md "Duels"), so these lines are the whole duel UI.
    /// </summary>
    public sealed class EncounterNotice : ISocialMessage
    {
        public ushort Family => EncounterMessages.EncounterFamily;
        public uint Sub => EncounterMessages.EncounterNoticeSub;
        public ulong Character { get; init; }
        public string Text { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(Character).Str(Text);
        public static EncounterNotice Read(InternalReader r) => new() { Character = r.U64(), Text = r.Str() };
    }
}
