using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Encounters (EncounterBase 0x2C; protocol in docs/encounters.md, decoded from the 0.589 client). Every packet
    /// starts {i16 sub, u32 pending id (client manager +0x3e0c), u32 encounter id}; the client keeps the encounter id
    /// (game +0x32f28) and echoes it in its requests. The client's game handler FUN_142c428b0 takes 0x7D / 0x7E (the
    /// death window) itself and passes every other sub to its encounter manager (FUN_143310250).
    ///
    /// Lifecycle as the client drives it: the server announces the encounter's zone as ready (0x6B), the client asks
    /// to enter (0x6C), the server confirms with PlayerEnter (2) and the encounter's state (0x6A); leaving is
    /// RequestExit (0x6D) or a server Terminate (0x79) + Shutdown (6). Invitations (0x66) reach an empty function in
    /// 0.589 (FUN_14332da10), so the client answers only through its own prompts (0x67 / 0x69); duels, the one encounter
    /// kind the 0.589 data names (StaticZoneData DUEL_ENCOUNTER_ID), start from the /duel and /duelaccept commands.
    /// </summary>
    public static class Encounters
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort SubPlayerEnter = 2, SubComplete = 3, SubLaunchFailed = 4, SubObjectiveState = 5,
            SubShutdown = 6, SubInvitation = 0x66, SubInvitationResponse = 0x67, SubDuelInvitationResponse = 0x69,
            SubState = 0x6A, SubZoneIsReady = 0x6B, SubRequestEntrance = 0x6C, SubRequestExit = 0x6D,
            SubDetailsResponse = 0x72, SubParticipantMessage = 0x78, SubTerminate = 0x79, SubCancelPending = 0x7C,
            SubDeathShow = 0x7D, SubDeathHide = 0x7E, SubRunningListResponse = 0x82;

        /// <summary>How long a duel challenge stays open.</summary>
        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(1);
        /// <summary>Duellists further apart than this (metres, or on different landscapes) forfeit.</summary>
        private const float DuelRange = 150f;

        private class Encounter
        {
            public uint Id;
            public string Kind;
            public readonly List<WorldSession> Participants = new();
            public readonly HashSet<WorldSession> Entered = new();
            public DateTime Started = DateTime.UtcNow;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Encounter> encounters = new();
        private static readonly Dictionary<WorldSession, Encounter> encounterOf = new();
        /// <summary>Open duel challenges: challenged player → (challenger, when).</summary>
        private static readonly Dictionary<WorldSession, (WorldSession From, DateTime At)> challenges = new();
        private static uint nextId = 1;

        /// <summary>The encounter id a player is in (0 for none): the header value of every encounter packet to them.</summary>
        public static uint IdOf(WorldSession session)
        {
            lock (sync)
                return encounterOf.TryGetValue(session, out Encounter e) ? e.Id : 0;
        }

        // ---- packets ------------------------------------------------------------------------------------------------

        private static void Send(WorldSession session, ushort sub, uint encounterId, Action<GamePacketWriter> body = null)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(sub);
            w.Write(encounterId);     // pending id (+0x14): the client finds its pending encounter by it (FUN_143268a70 / FUN_143261290)
            w.Write(encounterId);     // encounter id (+0x18), kept by the client at game +0x32f28
            body?.Invoke(w);
            session.EnqueueMessage(ZoneMessageOpcode.EncounterBase, output.ToArray());
        }

        /// <summary>The death window (0x7D "You have died.", RespawnHandler:Show) or its removal (0x7E).</summary>
        public static void SendDeathWindow(WorldSession session, bool show)
            => Send(session, show ? SubDeathShow : SubDeathHide, IdOf(session));

        /// <summary>
        /// ParticipantMessage (0x78): {u32 text id, u64 player, string}. The client prints the text with the player's
        /// name as "invited_player" (FUN_142cf1ee0) in the system channel.
        /// </summary>
        private static void SendMessage(WorldSession session, uint encounterId, uint textId, WorldSession about)
            => Send(session, SubParticipantMessage, encounterId, w =>
            {
                w.Write(textId);
                w.Write(about?.PlayerGuid ?? 0ul);
                w.Write(about?.CharacterName ?? "");
            });

        /// <summary>
        /// An empty RewardBundle (client reader FUN_142b95550, 81 bytes when its lists are empty): bool, u32,
        /// u32 list (FUN_142b9b7a0), u32 × 3, i64, u32 × 5, u64 × 2, u32 × 2, entry list {u32 type, entry}, u32.
        /// </summary>
        private static void WriteRewardBundle(GamePacketWriter w)
        {
            w.Write(false);
            w.Write(0u);
            w.Write(0u);                                  // list
            for (int i = 0; i < 3; i++) w.Write(0u);
            w.Write(0L);
            for (int i = 0; i < 5; i++) w.Write(0u);
            w.Write(0ul); w.Write(0ul);
            w.Write(0u); w.Write(0u);
            w.Write(0u);                                  // reward entries
            w.Write(0u);
        }

        /// <summary>
        /// EncounterDetailsCommon (FUN_1432d6ea0): u32, u32, two lists (FUN_1432db690 / FUN_1432db8f0), u32 +0x50
        /// (→ game +0x3286c), u32 +0x54 (→ game +0x327cc), bool +0x58, bool +0x59, u32 +0x5c (seconds to enter →
        /// game +0x327c0), u32 +0x60, the objective block (FUN_1432508f0: u32 × 5, u32, bool, 3 reward bundles, objective
        /// list, bool × 5, string, u32, bool, u32, bool × 3), bool +0x6490, bool +0x6491 (the receiver takes part).
        /// </summary>
        private static void WriteDetails(GamePacketWriter w, Encounter e, bool participant)
        {
            w.Write(e.Id);
            w.Write(0u);
            w.Write(0u);                                  // list
            w.Write(0u);                                  // list
            w.Write(0u);
            w.Write(0u);
            w.Write(false);
            w.Write(false);
            w.Write(0u);                                  // no entry deadline
            w.Write(0u);
            for (int i = 0; i < 5; i++) w.Write(0u);      // objective block
            w.Write(0u);
            w.Write(false);
            for (int i = 0; i < 3; i++) WriteRewardBundle(w);
            w.Write(0u);                                  // objectives
            for (int i = 0; i < 5; i++) w.Write(false);
            w.Write(e.Kind ?? "");
            w.Write(0u);
            w.Write(false);
            w.Write(0u);
            for (int i = 0; i < 3; i++) w.Write(false);
            w.Write(false);
            w.Write(participant);
        }

        /// <summary>
        /// EncounterDetailsResponse (0x72; reader FUN_1432e5dd0): details, bool, u32, int list (FUN_143188410). With the
        /// receiver taking part and the bool false the client records the encounter as joinable (game +0x327c8 = 1,
        /// FUN_143273c70) and keeps the header's pending id (+0x14) for its RequestEntrance.
        /// </summary>
        private static void SendDetails(WorldSession session, Encounter e)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(SubDetailsResponse);
            w.Write(e.Id);            // pending id (+0x14)
            w.Write(e.Id);            // encounter id (+0x18)
            WriteDetails(w, e, true);
            w.Write(false);
            w.Write(0u);
            w.Write(0u);              // int list
            session.EnqueueMessage(ZoneMessageOpcode.EncounterBase, output.ToArray());
        }

        // ---- requests -----------------------------------------------------------------------------------------------

        /// <summary>An EncounterBase packet from the client: {u32 pending id, u32 encounter id, body}.</summary>
        public static void Handle(WorldSession session, ushort sub, byte[] body)
        {
            var r = new GamePacketReader(body);
            uint pending, encounterId;
            try
            {
                pending = r.ReadUInt();
                encounterId = r.ReadUInt();
            }
            catch (Exception)
            {
                log.Warn($"Short encounter request 0x{sub:X} from {session.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }
            switch (sub)
            {
                case SubRequestEntrance:
                    // {u32 pending (manager +0x3e0c), u32 encounter, u64 (game +0x32508)} after ZoneIsReady.
                    Enter(session, encounterId);
                    break;
                case SubRequestExit:
                    Leave(session, "left");
                    break;
                case SubCancelPending:
                    lock (sync)
                        challenges.Remove(session);
                    log.Info($"{session.CharacterName} cancelled a pending encounter ({pending}).");
                    break;
                case SubInvitationResponse:
                {
                    // {u64 player, u8 accept} (FUN_143350240; the client also auto-accepts one invitation kind).
                    bool accept = body.Length >= 17 && body[16] != 0;
                    log.Info($"{session.CharacterName} answered encounter {encounterId}: {(accept ? "accept" : "decline")}.");
                    if (!accept)
                        Leave(session, "declined");
                    break;
                }
                case SubDuelInvitationResponse:
                {
                    // {u64 inviter, u8 accept} (FUN_1433501d0).
                    ulong inviter = body.Length >= 16 ? BitConverter.ToUInt64(body, 8) : 0;
                    bool accept = body.Length >= 17 && body[16] != 0;
                    Answer(session, inviter, accept);
                    break;
                }
                default:
                    log.Debug($"Encounter request 0x{sub:X} from {session.CharacterName}: {BitConverter.ToString(body)}");
                    break;
            }
        }

        // ---- lifecycle ----------------------------------------------------------------------------------------------

        /// <summary>Starts an encounter: every participant is told its zone is ready and is expected to ask to enter.</summary>
        private static Encounter Start(string kind, params WorldSession[] participants)
        {
            Encounter e;
            lock (sync)
            {
                e = new Encounter { Id = nextId++, Kind = kind };
                foreach (WorldSession p in participants)
                {
                    if (encounterOf.TryGetValue(p, out Encounter old))
                        old.Participants.Remove(p);
                    e.Participants.Add(p);
                    encounterOf[p] = e;
                }
                encounters[e.Id] = e;
            }
            foreach (WorldSession p in participants)
            {
                SendDetails(p, e);
                Send(p, SubZoneIsReady, e.Id);
            }
            log.Info($"Encounter {e.Id} ({kind}) started: {string.Join(", ", participants.Select(p => p.CharacterName))}.");
            return e;
        }

        /// <summary>RequestEntrance: the participant is in; everyone in the encounter sees PlayerEnter, then its state.</summary>
        private static void Enter(WorldSession session, uint encounterId)
        {
            Encounter e;
            List<WorldSession> all;
            lock (sync)
            {
                if (!encounters.TryGetValue(encounterId, out e) || !e.Participants.Contains(session))
                    return;
                e.Entered.Add(session);
                all = e.Participants.ToList();
            }
            foreach (WorldSession p in all)
                Send(p, SubPlayerEnter, e.Id, w => w.Write(session.PlayerGuid));
            Send(session, SubState, e.Id);
            if (e.Kind == "duel" && all.All(p => e.Entered.Contains(p)))
                foreach (WorldSession p in all)
                    Chat.System(p, $"Duel: {string.Join(" vs ", all.Select(x => x.CharacterName))}. Fight!");
        }

        /// <summary>A participant leaves; an encounter left with fewer than two (duel) or none ends.</summary>
        public static void Leave(WorldSession session, string why)
        {
            Encounter e;
            lock (sync)
            {
                challenges.Remove(session);
                if (!encounterOf.Remove(session, out e))
                    return;
                e.Participants.Remove(session);
                e.Entered.Remove(session);
            }
            Send(session, SubShutdown, e.Id, w => { w.Write(session.PlayerGuid); w.Write(0u); w.Write(0u); });
            if (e.Kind == "duel")
            {
                WorldSession winner = e.Participants.FirstOrDefault();
                if (winner != null)
                {
                    Chat.System(winner, $"{session.CharacterName} {(why == "defeated" ? "was defeated" : "left the duel")}. You win!");
                    End(e);
                }
                if (why != "logout")
                    Chat.System(session, why == "defeated" ? "You lost the duel." : "You left the duel.");
            }
            else if (e.Participants.Count == 0)
                End(e);
        }

        /// <summary>Ends an encounter: Terminate (0x79) then Shutdown (6) to everyone still in it.</summary>
        private static void End(Encounter e)
        {
            List<WorldSession> rest;
            lock (sync)
            {
                encounters.Remove(e.Id);
                rest = e.Participants.ToList();
                foreach (WorldSession p in rest)
                    encounterOf.Remove(p);
                e.Participants.Clear();
            }
            foreach (WorldSession p in rest)
            {
                Send(p, SubTerminate, e.Id, w => w.Write(true));
                Send(p, SubShutdown, e.Id, w => { w.Write(p.PlayerGuid); w.Write(0u); w.Write(0u); });
            }
            log.Info($"Encounter {e.Id} ({e.Kind}) ended.");
        }

        // ---- duels --------------------------------------------------------------------------------------------------

        /// <summary>/duel name: challenges a player on the same landscape within range.</summary>
        public static void Challenge(WorldSession session, string name)
        {
            WorldSession target = PlayerManager.Snapshot()
                .FirstOrDefault(p => string.Equals(p.CharacterName, name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target == null || target == session)
            {
                Chat.System(session, "Usage: /duel <player name> (the player must be online).");
                return;
            }
            if (target.ZoneId != session.ZoneId || Vector3.Distance(target.Position, session.Position) > DuelRange)
            {
                Chat.System(session, $"{target.CharacterName} is too far away to duel.");
                return;
            }
            if (IdOf(session) != 0 || IdOf(target) != 0)
            {
                Chat.System(session, "One of you is already in an encounter.");
                return;
            }
            lock (sync)
                challenges[target] = (session, DateTime.UtcNow);
            Chat.System(session, $"You challenged {target.CharacterName} to a duel.");
            Chat.System(target, $"{session.CharacterName} challenges you to a duel. Type /duelaccept or /dueldecline.");
        }

        /// <summary>/duelaccept, /dueldecline, or the client's DuelInvitationResponse (0x69).</summary>
        public static void Answer(WorldSession session, ulong inviterGuid, bool accept)
        {
            WorldSession from;
            lock (sync)
            {
                if (!challenges.Remove(session, out var c) || DateTime.UtcNow - c.At > ChallengeLifetime
                    || (inviterGuid != 0 && c.From.PlayerGuid != inviterGuid))
                {
                    Chat.System(session, "You have no duel challenge to answer.");
                    return;
                }
                from = c.From;
            }
            if (!accept)
            {
                Chat.System(from, $"{session.CharacterName} declined your duel.");
                Chat.System(session, "Duel declined.");
                return;
            }
            Start("duel", from, session);
        }

        /// <summary>Are these two players duelling each other (both entered the same running duel)?</summary>
        public static bool Duelling(WorldSession a, WorldSession b)
        {
            lock (sync)
                return encounterOf.TryGetValue(a, out Encounter e) && e.Kind == "duel" && encounterOf.TryGetValue(b, out Encounter f)
                    && e == f && e.Entered.Contains(a) && e.Entered.Contains(b);
        }

        /// <summary>
        /// A weapon swing from a duellist (Abilities, after the site-game check): hits the opponent if aimed at, in
        /// range or along the swing's ray. A blow that would kill ends the duel instead (the loser keeps 1 health).
        /// </summary>
        public static bool Hit(WorldSession attacker, ulong target, (Vector3 Origin, Vector3 Direction)? ray, Vector3 aim,
            uint damage, float range)
        {
            WorldSession victim;
            lock (sync)
            {
                if (!encounterOf.TryGetValue(attacker, out Encounter e) || e.Kind != "duel" || !e.Entered.Contains(attacker))
                    return false;
                victim = e.Participants.FirstOrDefault(p => p != attacker && e.Entered.Contains(p));
            }
            if (victim == null || Health.IsDead(victim) || victim.ZoneId != attacker.ZoneId)
                return false;
            float distance = Vector3.Distance(victim.Position, attacker.Position);
            bool aimed = target == victim.PlayerGuid && distance <= range;
            if (!aimed && ray is var (origin, direction))
            {
                Vector3 centre = victim.Position + new Vector3(0f, 1f, 0f);
                float t = Vector3.Dot(centre - origin, direction);
                aimed = t > 0 && t <= range + 10f && Vector3.Distance(origin + direction * t, centre) <= 1.2f;
            }
            if (!aimed && distance > 3.5f && Vector3.Distance(victim.Position, aim) > 2f)
                return false;
            uint hp = Health.Current(victim);
            if (damage >= hp)
            {
                Health.Damage(victim, hp - 1, attacker);
                Leave(victim, "defeated");
            }
            else
                Health.Damage(victim, damage, attacker);
            Creatures.SendCombatEvent(attacker, attacker.PlayerGuid, victim.PlayerGuid, damage);
            Creatures.SendCombatEvent(victim, attacker.PlayerGuid, victim.PlayerGuid, damage);
            return true;
        }

        /// <summary>Server tick: duellists who drift apart or change landscape forfeit; stale challenges expire.</summary>
        public static void Update()
        {
            List<WorldSession> forfeits = new();
            lock (sync)
            {
                foreach (var stale in challenges.Where(c => DateTime.UtcNow - c.Value.At > ChallengeLifetime).Select(c => c.Key).ToList())
                    challenges.Remove(stale);
                foreach (Encounter e in encounters.Values.Where(e => e.Kind == "duel" && e.Participants.Count == 2))
                {
                    WorldSession a = e.Participants[0], b = e.Participants[1];
                    if (a.ZoneId != b.ZoneId || Vector3.Distance(a.Position, b.Position) > DuelRange)
                        forfeits.Add(a);
                }
            }
            foreach (WorldSession s in forfeits)
                Leave(s, "left");
        }
    }
}
