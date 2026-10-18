using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Internal.WorldChannel;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// The encounter broker (trace/architecture/world-channel-service.md §3.4, §4 "Encounters / duels", §6.5; build
    /// step 7, §8): the encounter <b>lifecycle</b> runs here, the <b>combat</b> stays in the zone. Every encounter
    /// request the client sends travels on the World channel (EncounterBase 0x2C; docs/encounters.md): RequestEntrance
    /// 0x6C (FUN_142cbe6e0 / 0.695 FUN_142d57a70), RequestExit 0x6D (FUN_142ca26b0 / FUN_142d3bbd0), CancelPending 0x7C
    /// (FUN_142c0cfb0 / FUN_142ca6d90), InvitationResponse 0x67 (FUN_143350240 / FUN_1433fc300) and
    /// DuelInvitationResponse 0x69 (FUN_1433501d0 / FUN_1433fc290). This class answers them with the S→C lifecycle
    /// packets — DetailsResponse 0x72, ZoneIsReady 0x6B, PlayerEnter 2, State 0x6A, Terminate 0x79, Shutdown 6 — in
    /// exactly the bytes the zone's Encounters.cs sends in-process (the 0.695 client's numbering, sent as given (docs/protocol.md §1.6); the Zone-channel destination as before, the client reads them in one handler FUN_142c428b0 →
    /// FUN_143310250 whatever the channel). The death window 0x7D / 0x7E stays the zone's (Health.cs).
    /// <para>The flow (messages in <see cref="EncounterMessages"/>, all on the link to the zone):</para>
    /// <list type="number">
    /// <item>/duel name is a <b>zone</b> command: the zone checks what only it knows (both online on one landscape within
    /// 150 m, neither in an encounter) and sends <b>EncounterCreateDuel</b>. A challenge is opened here (one minute, as
    /// in-process) and both players get the chat lines through <b>EncounterNotice</b> (the zone prints them).</item>
    /// <item>The answer: the client's 0x69 (World channel, here directly) or /duelaccept, /dueldecline (zone commands →
    /// <b>EncounterDuelAnswer</b>). Decline → two chat lines. Accept → <see cref="Start"/>: an encounter id from
    /// <see cref="EncounterMessages.FirstBrokerEncounterId"/>, DetailsResponse 0x72 (the receiver takes part, "may
    /// enter") then ZoneIsReady 0x6B to each, and <b>EncounterParticipants</b> state Open to the zone.</item>
    /// <item>Each client answers ZoneIsReady with RequestEntrance 0x6C: PlayerEnter 2 to everyone in the encounter, State
    /// 0x6A to the enterer. When all have entered: EncounterParticipants state <b>Running</b> (the zone's duel rules now
    /// apply) and "Duel: A vs B. Fight!" to both.</item>
    /// <item>Out: RequestExit 0x6D / InvitationResponse decline (here), or the zone's <b>EncounterForfeit</b> (a killing
    /// blow, out of range / off the landscape, /duelleave, logout). Shutdown 6 to the leaver; a duel then ends for the
    /// other with Terminate 0x79 + Shutdown 6 and "You win!"; EncounterParticipants state Ended.</item>
    /// </list>
    /// <para>
    /// Encounter ids this broker did not hand out (one the zone started itself while this service was away, when the
    /// gateway routes channel 1 to the zone) are passed on to the zone unchanged, so such an encounter can still be
    /// entered or left; likewise a 0x69 / 0x7C for a challenge this broker does not hold. When the zone link drops every
    /// encounter here is forgotten; the zone ends them on its side (spec §7, as it cancels brokered trades). Encounters
    /// are memory only (spec §6.1).
    /// </para>
    /// <para>
    /// Threading: <see cref="TryTake"/> runs on the gateway link's thread, <see cref="FromZone"/> on the zone link's and
    /// <see cref="Sweep"/> on a timer; the registry is guarded by one lock, packets are sent outside it.
    /// </para>
    /// </summary>
    public static class EncounterBroker
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The subs (docs/encounters.md; the same constants as the zone's Encounters.cs).</summary>
        private const ushort SubPlayerEnter = 2, SubShutdown = 6, SubInvitationResponse = 0x67, SubDuelInvitationResponse = 0x69,
            SubState = 0x6A, SubZoneIsReady = 0x6B, SubRequestEntrance = 0x6C, SubRequestExit = 0x6D, SubDetailsResponse = 0x72,
            SubTerminate = 0x79, SubCancelPending = 0x7C;

        /// <summary>How long a duel challenge stays open (Encounters.ChallengeLifetime in-process).</summary>
        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(1);

        private sealed class Participant
        {
            public ulong Connection, Character;
            public string Name = "";
            public bool Entered;
        }

        private sealed class Encounter
        {
            public uint Id;
            public string Kind = "";
            public readonly List<Participant> Participants = new();
            public DateTime Started = DateTime.UtcNow;
            public bool AllEntered => Participants.Count > 0 && Participants.All(p => p.Entered);
            public uint State => AllEntered ? EncounterMessages.StateRunning : EncounterMessages.StateOpen;
            public Participant Of(ulong character) => Participants.FirstOrDefault(p => p.Character == character);
        }

        private sealed class DuelChallenge
        {
            public Participant From, To;
            public DateTime At = DateTime.UtcNow;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Encounter> encounters = new();
        private static readonly Dictionary<ulong, Encounter> encounterOf = new();
        /// <summary>Open duel challenges: challenged character → challenge.</summary>
        private static readonly Dictionary<ulong, DuelChallenge> challenges = new();
        private static uint nextId = EncounterMessages.FirstBrokerEncounterId - 1;

        /// <summary>WorldChannel.json EncounterBroker (default true): false hands every encounter packet on to the zone as in step 2.</summary>
        public static bool Enabled { get; set; } = true;

        private static Func<IInternalMessage, bool> toGateway, toZone;

        /// <summary>The two links' send functions (<see cref="ChannelRelay"/>).</summary>
        public static void Start(Func<IInternalMessage, bool> gateway, Func<IInternalMessage, bool> zone)
        {
            toGateway = gateway;
            toZone = zone;
        }

        // ---- from the client (World channel) ---------------------------------------------------------------------------

        /// <summary>
        /// A channel-1 packet from the gateway: true when it was an encounter request this broker took (for an
        /// encounter or a challenge it holds); false hands it on to the zone (its own encounters, or anything else).
        /// Wire: u16 base 0x2C (bytes 2C 00), i16 sub, u32 pending id, u32 encounter id, body (reader FUN_142b7dda0).
        /// </summary>
        public static bool TryTake(TunnelFromExternal packet)
        {
            byte[] p = packet.Packet;
            if (!Enabled || p.Length < 12 || p[0] != 0x2C || p[1] != 0x00)
                return false;
            ushort sub = (ushort)(p[2] | (p[3] << 8));
            ulong me = packet.CharacterGuid;
            try
            {
                var r = new InternalReader(p, 4);
                uint pending = r.U32();
                uint encounterId = r.U32();
                switch (sub)
                {
                    case SubRequestEntrance:
                        // {u32 pending (manager +0x3e0c), u32 encounter, u64 (game +0x32508)} after ZoneIsReady.
                        return Enter(me, encounterId);
                    case SubRequestExit:
                        return Leave(me, EncounterMessages.ReasonLeft);
                    case SubCancelPending:
                    {
                        // {u32 (manager +0x3e0c)}: the challenged player drops the pending encounter (FUN_142c0cfb0).
                        bool ours;
                        lock (sync)
                            ours = challenges.Remove(me);
                        if (ours)
                            log.Info($"Character {me} cancelled a pending encounter ({pending}).");
                        return ours;
                    }
                    case SubInvitationResponse:
                    {
                        // {u64 player, u8 accept} (FUN_143350240; the client also auto-accepts one invitation kind).
                        r.U64();
                        bool accept = r.Remaining > 0 && r.Bool();
                        bool ours;
                        lock (sync)
                            ours = encounterOf.ContainsKey(me);
                        if (!ours)
                            return IsOurs(encounterId);
                        log.Info($"Character {me} answered encounter {encounterId}: {(accept ? "accept" : "decline")}.");
                        if (!accept)
                            Leave(me, EncounterMessages.ReasonDeclined);
                        return true;
                    }
                    case SubDuelInvitationResponse:
                    {
                        // {u64 inviter, u8 accept} (FUN_1433501d0).
                        ulong inviter = r.U64();
                        bool accept = r.Remaining > 0 && r.Bool();
                        bool ours;
                        lock (sync)
                            ours = challenges.ContainsKey(me);
                        if (!ours)
                            return false;
                        Answer(me, inviter, accept);
                        return true;
                    }
                    default:
                        // Other requests (none in 0.589's send sites) belong to whoever holds the encounter.
                        return IsOurs(encounterId);
                }
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable encounter packet 0x2C/0x{sub:X} from connection {packet.ConnectionId:X}: {e.Message}");
                return false;
            }
        }

        /// <summary>An id from this broker's range: ours even when the encounter is already gone (the zone never knew it).</summary>
        private static bool IsOurs(uint encounterId) => encounterId >= EncounterMessages.FirstBrokerEncounterId;

        // ---- from the zone -----------------------------------------------------------------------------------------------

        /// <summary>An encounter message from the zone (CreateDuel / DuelAnswer / Forfeit). True when it was one.</summary>
        public static bool FromZone(ServicePacket packet)
        {
            switch (EncounterMessages.Parse(packet))
            {
                case EncounterCreateDuel duel:
                    Challenge(duel);
                    return true;
                case EncounterDuelAnswer answer:
                    Answer(answer.Character, answer.Inviter, answer.Accept);
                    return true;
                case EncounterForfeit forfeit:
                    Forfeit(forfeit);
                    return true;
                case null:
                    return false;
                default:
                    log.Debug($"Encounter message 0x{packet.Sub:X} from the zone: not for the world channel.");
                    return true;
            }
        }

        // ---- duels (the texts are the in-process Encounters.cs lines, printed by the zone through EncounterNotice) -------

        /// <summary>/duel name, checked by the zone (range, landscape, online): open the challenge (Encounters.Challenge).</summary>
        private static void Challenge(EncounterCreateDuel duel)
        {
            var from = new Participant { Connection = duel.ChallengerConnection, Character = duel.Challenger, Name = duel.ChallengerName };
            var to = new Participant { Connection = duel.TargetConnection, Character = duel.Target, Name = duel.TargetName };
            lock (sync)
            {
                if (duel.Challenger == 0 || duel.Target == 0 || duel.Challenger == duel.Target)
                    return;
                // The zone checked its mirror of the registry; this is the authority (a push may be in flight).
                if (encounterOf.ContainsKey(duel.Challenger) || encounterOf.ContainsKey(duel.Target))
                {
                    Notice(duel.Challenger, "One of you is already in an encounter.");
                    return;
                }
                challenges[duel.Target] = new DuelChallenge { From = from, To = to };
            }
            Notice(duel.Challenger, $"You challenged {to.Name} to a duel.");
            Notice(duel.Target, $"{from.Name} challenges you to a duel. Type /duelaccept or /dueldecline.");
            log.Info($"{from.Name} challenged {to.Name} to a duel.");
        }

        /// <summary>/duelaccept, /dueldecline, or the client's DuelInvitationResponse 0x69 (Encounters.Answer).</summary>
        private static void Answer(ulong character, ulong inviter, bool accept)
        {
            DuelChallenge c;
            lock (sync)
            {
                if (!challenges.Remove(character, out c) || DateTime.UtcNow - c.At > ChallengeLifetime
                    || (inviter != 0 && c.From.Character != inviter))
                {
                    Notice(character, "You have no duel challenge to answer.");
                    return;
                }
            }
            if (!accept)
            {
                Notice(c.From.Character, $"{c.To.Name} declined your duel.");
                Notice(character, "Duel declined.");
                log.Info($"{c.To.Name} declined {c.From.Name}'s duel.");
                return;
            }
            Start("duel", c.From, c.To);
        }

        /// <summary>The zone's combat rules took a participant out (Encounters.Hit / Update / Leave), or a player left the world.</summary>
        private static void Forfeit(EncounterForfeit forfeit)
        {
            lock (sync)
            {
                challenges.Remove(forfeit.Character);
                if (forfeit.Reason == EncounterMessages.ReasonLogout)
                    foreach (ulong target in challenges.Where(c => c.Value.From.Character == forfeit.Character).Select(c => c.Key).ToList())
                        challenges.Remove(target);
            }
            if (forfeit.EncounterId == 0)
                return;
            if (!Leave(forfeit.Character, forfeit.Reason))
                log.Debug($"Forfeit of encounter {forfeit.EncounterId} by character {forfeit.Character}: not in one here.");
        }

        // ---- lifecycle (docs/encounters.md "Server": the order the client's handlers require) ----------------------------

        /// <summary>Starts an encounter: every participant is told its zone is ready and is expected to ask to enter.</summary>
        private static void Start(string kind, params Participant[] participants)
        {
            Encounter e;
            lock (sync)
            {
                e = new Encounter { Id = NextId(), Kind = kind };
                foreach (Participant p in participants)
                {
                    if (encounterOf.TryGetValue(p.Character, out Encounter old))
                        old.Participants.RemoveAll(x => x.Character == p.Character);
                    p.Entered = false;
                    e.Participants.Add(p);
                    encounterOf[p.Character] = e;
                }
                encounters[e.Id] = e;
            }
            foreach (Participant p in participants)
            {
                // 1. DetailsResponse (0x72) first: "may enter" (game +0x327c8 = 1, FUN_143273c70); 2. ZoneIsReady (0x6B):
                // the client answers RequestEntrance (FUN_142cbe6e0 checks +0x327c8 == 1).
                SendDetails(p, e);
                Send(p, SubZoneIsReady, e.Id);
            }
            Push(e, EncounterMessages.StateOpen);
            log.Info($"Encounter {e.Id} ({kind}) started: {string.Join(", ", participants.Select(p => p.Name))}.");
        }

        /// <summary>RequestEntrance: the participant is in; everyone in the encounter sees PlayerEnter, then its state.</summary>
        private static bool Enter(ulong character, uint encounterId)
        {
            Encounter e;
            Participant me;
            List<Participant> all;
            bool running;
            lock (sync)
            {
                if (!encounters.TryGetValue(encounterId, out e))
                    return IsOurs(encounterId);
                me = e.Of(character);
                if (me == null)
                    return true;
                me.Entered = true;
                all = e.Participants.ToList();
                running = e.AllEntered;
            }
            foreach (Participant p in all)
                Send(p, SubPlayerEnter, e.Id, w => w.U64(character));
            Send(me, SubState, e.Id);
            if (running)
            {
                Push(e, EncounterMessages.StateRunning);
                if (e.Kind == "duel")
                    foreach (Participant p in all)
                        Notice(p.Character, $"Duel: {string.Join(" vs ", all.Select(x => x.Name))}. Fight!");
            }
            return true;
        }

        /// <summary>A participant leaves; a duel left with one ends for the other; an encounter left empty ends. False = not in one here.</summary>
        private static bool Leave(ulong character, byte reason)
        {
            Encounter e;
            Participant me;
            lock (sync)
            {
                challenges.Remove(character);
                if (!encounterOf.Remove(character, out e))
                    return false;
                me = e.Of(character) ?? new Participant { Character = character };
                e.Participants.Remove(me);
            }
            // Terminate {false} + Shutdown, as the zone's Encounters.Leave (Shutdown alone leaves the client's encounter
            // state set: 0.695 FUN_143315580 only fires the UI ":exit" event; Terminate resets it, FUN_142ca6d90).
            Send(me, SubTerminate, e.Id, w => w.Bool(false));
            Send(me, SubShutdown, e.Id, w => w.U64(character).U32(0).U32(0));
            bool defeated = reason == EncounterMessages.ReasonDefeated;
            if (e.Kind == "duel")
            {
                Participant winner;
                lock (sync)
                    winner = e.Participants.FirstOrDefault();
                if (winner != null)
                {
                    Notice(winner.Character, $"{me.Name} {(defeated ? "was defeated" : "left the duel")}. You win!");
                    End(e);
                }
                else
                    Push(e, EncounterMessages.StateEnded);
                if (reason != EncounterMessages.ReasonLogout)
                    Notice(character, defeated ? "You lost the duel." : "You left the duel.");
            }
            else
            {
                bool empty;
                lock (sync)
                    empty = e.Participants.Count == 0;
                if (empty)
                    End(e);
                else
                    Push(e, e.State);
            }
            log.Info($"Character {character} ({me.Name}) left encounter {e.Id} (reason {reason}).");
            return true;
        }

        /// <summary>
        /// Ends an encounter: Terminate (0x79) then Shutdown (6) to everyone still in it. Terminate's bool is "took too
        /// long" (the client prints the code string "TookTooLong" when it is set; 0.695 case 0x79 at 0x1433bdbcd), so a
        /// normal end sends false (Encounters.End).
        /// </summary>
        private static void End(Encounter e)
        {
            List<Participant> rest;
            lock (sync)
            {
                encounters.Remove(e.Id);
                rest = e.Participants.ToList();
                foreach (Participant p in rest)
                    encounterOf.Remove(p.Character);
            }
            foreach (Participant p in rest)
            {
                Send(p, SubTerminate, e.Id, w => w.Bool(false));   // not "took too long"
                Send(p, SubShutdown, e.Id, w => w.U64(p.Character).U32(0).U32(0));
            }
            // The zone forgets it, with whoever was still in it (its mirror of the participants).
            Zone(new EncounterParticipants
            {
                EncounterId = e.Id, Kind = e.Kind, State = EncounterMessages.StateEnded, Characters = rest.Select(p => p.Character).ToList()
            });
            lock (sync)
                e.Participants.Clear();
            log.Info($"Encounter {e.Id} ({e.Kind}) ended.");
        }

        /// <summary>Timer: stale challenges expire (the in-process Update tick did this each server tick).</summary>
        public static void Sweep()
        {
            lock (sync)
                foreach (ulong stale in challenges.Where(c => DateTime.UtcNow - c.Value.At > ChallengeLifetime).Select(c => c.Key).ToList())
                    challenges.Remove(stale);
        }

        /// <summary>The zone link is gone: the zone ends the encounters it mirrored itself (Terminate + Shutdown); forget them here.</summary>
        public static void ZoneLost()
        {
            int n;
            lock (sync)
            {
                n = encounters.Count;
                encounters.Clear();
                encounterOf.Clear();
                challenges.Clear();
            }
            if (n > 0)
                log.Warn($"Zone link lost: {n} encounter(s) forgotten (the zone ends them).");
        }

        // ---- to the zone -------------------------------------------------------------------------------------------------

        /// <summary>EncounterParticipants with the encounter's current list (spec §6.5: on every state change).</summary>
        private static void Push(Encounter e, uint state)
        {
            List<ulong> chars;
            lock (sync)
                chars = e.Participants.Select(p => p.Character).ToList();
            Zone(new EncounterParticipants { EncounterId = e.Id, Kind = e.Kind, State = state, Characters = chars });
        }

        /// <summary>A system chat line to one player, printed by the zone (EncounterNotice).</summary>
        private static void Notice(ulong character, string text)
        {
            if (!Zone(new EncounterNotice { Character = character, Text = text }))
                log.Debug($"Notice to character {character} not sent (zone unreachable): {text}");
        }

        private static bool Zone(ISocialMessage message) => toZone?.Invoke(message.ToPacket()) ?? false;

        private static uint NextId()
        {
            // Never 0, never below the broker's range (the zone's own encounters count up from 1).
            if (++nextId < EncounterMessages.FirstBrokerEncounterId || nextId == uint.MaxValue)
                nextId = EncounterMessages.FirstBrokerEncounterId;
            return nextId;
        }

        // ---- packets (byte-identical to the zone's Encounters.cs; comments there give the client readers) ----------------

        /// <summary>
        /// One encounter packet to a client: EncounterBase 0x2C00 (bytes 2C 00), i16 sub, u32 pending id, u32 encounter id,
        /// body. The client finds its pending encounter by the pending id (FUN_143268a70 / FUN_143261290) and keeps the
        /// encounter id at game +0x32f28; the zone sent the encounter id in both fields, so does this.
        /// </summary>
        private static void Send(Participant to, ushort sub, uint encounterId, Action<InternalWriter> body = null)
        {
            var w = new InternalWriter();
            w.U8(0x2C).U8(0x00).U16(sub).U32(encounterId).U32(encounterId);
            body?.Invoke(w);
            toGateway?.Invoke(new TunnelToExternal
            {
                Destination = new ExternalDestination(to.Connection, GatewayChannel.Zone, ExternalDestination.Reliable),
                Packet = w.ToArray()
            });
        }

        /// <summary>
        /// EncounterDetailsResponse (0x72; reader FUN_1432e5dd0): details, bool, u32, int list (FUN_143188410). With the
        /// receiver taking part and the bool false the client records the encounter as joinable (game +0x327c8 = 1,
        /// FUN_143273c70) and keeps the header's pending id (+0x14) for its RequestEntrance.
        /// </summary>
        private static void SendDetails(Participant to, Encounter e)
            => Send(to, SubDetailsResponse, e.Id, w =>
            {
                WriteDetails(w, e, true);
                w.Bool(false);
                w.U32(0);
                w.U32(0);              // int list
            });

        /// <summary>
        /// EncounterDetailsCommon (FUN_1432d6ea0): u32, u32, two lists (FUN_1432db690 / FUN_1432db8f0), u32 +0x50
        /// (→ game +0x3286c), u32 +0x54 (→ game +0x327cc), bool +0x58, bool +0x59, u32 +0x5c (seconds to enter →
        /// game +0x327c0), u32 +0x60, the objective block (FUN_1432508f0: u32 × 5, u32, bool, 3 reward bundles, objective
        /// list, bool × 5, string, u32, bool, u32, bool × 3), bool +0x6490, bool +0x6491 (the receiver takes part).
        /// </summary>
        private static void WriteDetails(InternalWriter w, Encounter e, bool participant)
        {
            w.U32(e.Id);
            w.U32(0);
            w.U32(0);                                     // list
            w.U32(0);                                     // list
            w.U32(0);
            w.U32(0);
            w.Bool(false);
            w.Bool(false);
            w.U32(0);                                     // no entry deadline
            w.U32(0);
            for (int i = 0; i < 5; i++) w.U32(0);         // objective block
            w.U32(0);
            w.Bool(false);
            for (int i = 0; i < 3; i++) WriteRewardBundle(w);
            w.U32(0);                                     // objectives
            for (int i = 0; i < 5; i++) w.Bool(false);
            ClientString(w, e.Kind ?? "");
            w.U32(0);
            w.Bool(false);
            w.U32(0);
            for (int i = 0; i < 3; i++) w.Bool(false);
            w.Bool(false);
            w.Bool(participant);
        }

        /// <summary>
        /// An empty RewardBundle (client reader FUN_142b95550, 81 bytes when its lists are empty): bool, u32,
        /// u32 list (FUN_142b9b7a0), u32 × 3, i64, u32 × 5, u64 × 2, u32 × 2, entry list {u32 type, entry}, u32.
        /// </summary>
        private static void WriteRewardBundle(InternalWriter w)
        {
            w.Bool(false);
            w.U32(0);
            w.U32(0);                                     // list
            for (int i = 0; i < 3; i++) w.U32(0);
            w.U64(0);
            for (int i = 0; i < 5; i++) w.U32(0);
            w.U64(0); w.U64(0);
            w.U32(0); w.U32(0);
            w.U32(0);                                     // reward entries
            w.U32(0);
        }

        /// <summary>A client string: u32 byte count, UTF-8 (GamePacketWriter.Write(string)).</summary>
        private static void ClientString(InternalWriter w, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            w.U32((uint)bytes.Length).Bytes(bytes);
        }

        /// <summary>Encounters and open challenges now (for the log).</summary>
        public static (int Encounters, int Challenges) Count
        {
            get
            {
                lock (sync)
                    return (encounters.Count, challenges.Count);
            }
        }
    }
}
