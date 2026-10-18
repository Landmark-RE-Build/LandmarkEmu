using LandmarkEmulator.Internal.WorldChannel;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
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
    /// <para>
    /// 0.695 (verified 2026-10-17, docs/encounters.md "0.695 dispatch"): game handler FUN_142cdc370, manager vtable
    /// +0x18 FUN_1433bd040, whose jump table (byte index 0x1433be214, offsets 0x1433be1e0, sub − 2 &lt; 0x81) has the
    /// same cases as 0.589 (2, 3, 4, 5, 6, 0x66, 0x6A, 0x6B, 0x72, 0x78, 0x79, 0x82). Every body this class and the
    /// broker send matches its 0.695 reader field for field, and every reader drops a packet that is not consumed
    /// exactly [P]. Changed then: Terminate's bool is false (see <see cref="End"/>), and a player taken out gets
    /// Terminate before Shutdown too (<see cref="Leave"/>).
    /// </para>
    ///
    /// Lifecycle as the client drives it: the server announces the encounter's zone as ready (0x6B), the client asks
    /// to enter (0x6C), the server confirms with PlayerEnter (2) and the encounter's state (0x6A); leaving is
    /// RequestExit (0x6D) or a server Terminate (0x79) + Shutdown (6). Invitations (0x66) reach an empty function in
    /// 0.589 (FUN_14332da10), so the client answers only through its own prompts (0x67 / 0x69); duels, the one encounter
    /// kind the 0.589 data names (StaticZoneData DUEL_ENCOUNTER_ID), start from the /duel and /duelaccept commands.
    /// <para>
    /// Two places run the lifecycle (trace/architecture/world-channel-service.md §3.4, §4 "Encounters / duels", §6.5;
    /// build step 7). Every encounter request the client sends goes on the World channel, so live's World back end owned
    /// the lifecycle while the zone, which has positions and health, owned the combat.
    /// <list type="bullet">
    /// <item><b>In-process</b> (no world-channel service — the default — or while it is away and the gateway sends
    /// channel 1 here, spec §7): this class does everything as before (<see cref="Handle"/>, <see cref="Challenge"/>,
    /// <see cref="Answer"/>, <see cref="Leave"/>); ids count up from 1.</item>
    /// <item><b>World-channel broker</b> (LandmarkEmulator.WorldChannel, EncounterBroker; <see cref="WorldChannelLink"/>
    /// linked): the service answers the client's requests and sends the lifecycle packets (Details, ZoneIsReady,
    /// PlayerEnter, State, Terminate, Shutdown; ids from 0x40000001, EncounterMessages.FirstBrokerEncounterId). This
    /// class keeps a <b>mirror</b> of its encounters from the EncounterParticipants pushes (<see cref="BrokerParticipants"/>)
    /// so the combat rules — <see cref="Duelling"/>, <see cref="Hit"/>, the range / landscape check of <see cref="Update"/>,
    /// the death window's encounter id (<see cref="IdOf"/>) — work unchanged; what it decides goes back as
    /// EncounterCreateDuel (/duel, after the zone's own range check), EncounterDuelAnswer (/duelaccept, /dueldecline) and
    /// EncounterForfeit (a killing blow, out of range, /duelleave, logout); the duel chat lines come back as
    /// EncounterNotice and are printed by <see cref="Notice"/>. When that service's link goes, its encounters end here
    /// (<see cref="BrokerLost"/>: Terminate + Shutdown to the clients), as brokered trades are canceled (spec §7).</item>
    /// </list>
    /// </para>
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
            /// <summary>
            /// The world-channel broker generation that runs it (WorldChannelLink), 0 when this zone runs the lifecycle
            /// itself. A brokered encounter's packets come from the service; this zone only mirrors its participants.
            /// </summary>
            public int Broker;
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
        /// An empty RewardBundle (client reader 0.589 FUN_142b95550 / 0.695 FUN_142c2f040, 81 bytes when its lists are
        /// empty; fields in Model.Shared.RewardBundle). Written by the model so it carries the live empty-bundle values:
        /// icon and name text 0xFFFFFFFF ("use the first entry's"), everything else 0 (docs/unknowns.md).
        /// </summary>
        private static void WriteRewardBundle(GamePacketWriter w) => new RewardBundle().Write(w);

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

        /// <summary>
        /// A participant leaves; an encounter left with fewer than two (duel) or none ends. <paramref name="why"/>:
        /// "left" (/duelleave, RequestExit), "defeated" (<see cref="Hit"/>), "range" (<see cref="Update"/>: apart or on
        /// different landscapes), "logout" (PlayerManager), "declined". A brokered encounter is left through the
        /// service instead (EncounterForfeit; it sends Shutdown / Terminate and the chat lines, and pushes the new list),
        /// after taking the player out of the mirror here so the combat rules stop at once.
        /// </summary>
        public static void Leave(WorldSession session, string why)
        {
            Encounter e;
            lock (sync)
            {
                challenges.Remove(session);
                if (!encounterOf.Remove(session, out e))
                {
                    // Gone from the world with no encounter: the service drops any duel challenge of theirs.
                    if (why == "logout" && WorldChannelLink.IsLinked)
                        WorldChannelLink.Send(new EncounterForfeit { EncounterId = 0, Character = session.PlayerGuid, Reason = EncounterMessages.ReasonLogout });
                    return;
                }
                e.Participants.Remove(session);
                e.Entered.Remove(session);
            }
            if (e.Broker != 0)
            {
                if (!WorldChannelLink.SendTo(e.Broker, new EncounterForfeit { EncounterId = e.Id, Character = session.PlayerGuid, Reason = ReasonOf(why) }))
                    log.Warn($"{session.CharacterName} left brokered encounter {e.Id} ({why}) but its world channel is gone.");
                return;
            }
            // Terminate {bool false} first, as End sends it to the one left in: the client's Shutdown handler only fires
            // the mini-game UI's ":exit" event (0.695 FUN_143315580), while Terminate resets its encounter state
            // (FUN_142ca6d90 + FUN_142d06480: game +0x327c8 / +0x327cc / +0x3286c, the manager). [P] for what each does;
            // [I] that live sent it to a player it took out, since no live capture of an encounter exists.
            Send(session, SubTerminate, e.Id, w => w.Write(false));
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

        /// <summary>
        /// Ends an encounter: Terminate (0x79) then Shutdown (6) to everyone still in it. Terminate's bool is "took too
        /// long" [P, 0.695 jump-table case 0x79 at 0x1433bdbcd]: when set the client prints the code string
        /// "TookTooLong" (looked up by name, ClientStringProvider vtable +0x28 FUN_142fdeba0; the name is in neither
        /// build's CodeStringMappings.txt, so it prints text id 0), so a normal end sends false. Either way the client
        /// then cancels its encounter (FUN_142ca6d90(game, 0)) and leaves it (FUN_142d06480 → FUN_14331a850, which
        /// sends one RequestExit 0x6D when it was a participant; <see cref="Leave"/> then finds nothing to do).
        /// </summary>
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
                Send(p, SubTerminate, e.Id, w => w.Write(false));   // not "took too long"
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
            // With the world-channel service linked the challenge is held there (spec §6.5 EncounterCreateDuel: "the
            // /duel zone command checked range"); it prints the two chat lines back through EncounterNotice.
            if (WorldChannelLink.IsLinked && WorldChannelLink.Send(new EncounterCreateDuel
            {
                Challenger = session.PlayerGuid, Target = target.PlayerGuid,
                ChallengerConnection = session.Link.ConnectionId, ChallengerName = session.CharacterName ?? "",
                TargetConnection = target.Link.ConnectionId, TargetName = target.CharacterName ?? ""
            }))
            {
                log.Info($"{session.CharacterName} challenged {target.CharacterName} to a duel (through the world channel).");
                return;
            }
            lock (sync)
                challenges[target] = (session, DateTime.UtcNow);
            Chat.System(session, $"You challenged {target.CharacterName} to a duel.");
            Chat.System(target, $"{session.CharacterName} challenges you to a duel. Type /duelaccept or /dueldecline.");
        }

        /// <summary>
        /// /duelaccept, /dueldecline, or the client's DuelInvitationResponse (0x69). A challenge this zone holds (made
        /// in-process) is answered here; otherwise, with the world-channel service linked, the answer goes to it
        /// (EncounterDuelAnswer: /duelaccept is a zone command, the service holds the challenge), which prints "You have
        /// no duel challenge to answer." itself when there is none.
        /// </summary>
        public static void Answer(WorldSession session, ulong inviterGuid, bool accept)
        {
            WorldSession from;
            lock (sync)
            {
                if (!challenges.TryGetValue(session, out var c))
                {
                    if (WorldChannelLink.IsLinked && WorldChannelLink.Send(new EncounterDuelAnswer
                    {
                        Character = session.PlayerGuid, Inviter = inviterGuid, Accept = accept
                    }))
                        return;
                    Chat.System(session, "You have no duel challenge to answer.");
                    return;
                }
                challenges.Remove(session);
                if (DateTime.UtcNow - c.At > ChallengeLifetime || (inviterGuid != 0 && c.From.PlayerGuid != inviterGuid))
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
            // Challenge checked this, but either side may have entered another duel since (accepting someone else's
            // challenge): Start would take them out of that one without ending it, leaving its other duellist alone in an
            // encounter nothing ends — no range check (Update needs two), every new challenge refused "already in an
            // encounter" — until they left by hand (bughunt-combat.md B6). Gone players can't start one either.
            if (from.Disconnected || session.Disconnected || IdOf(from) != 0 || IdOf(session) != 0)
            {
                Chat.System(session, "One of you is already in an encounter.");
                if (!from.Disconnected)
                    Chat.System(from, $"{session.CharacterName} couldn't accept your duel: one of you is already in an encounter.");
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
                Leave(s, "range");
        }

        // ---- the world-channel encounter broker (world-channel-service.md §6.5; WorldChannelLink, world thread) ---------

        /// <summary>The EncounterForfeit reason for a <see cref="Leave"/> cause.</summary>
        private static byte ReasonOf(string why) => why switch
        {
            "defeated" => EncounterMessages.ReasonDefeated,
            "range" => EncounterMessages.ReasonRange,
            "logout" => EncounterMessages.ReasonLogout,
            "declined" => EncounterMessages.ReasonDeclined,
            _ => EncounterMessages.ReasonLeft
        };

        /// <summary>
        /// EncounterParticipants from the world channel (spec §6.5, pushed on every state change): the mirror of one of
        /// its encounters. State Ended removes it (no packets: the service sent Terminate / Shutdown); Open / Running
        /// (re)builds its participant list from the online sessions, with every participant counted as entered when
        /// Running, which is what <see cref="Duelling"/> and <see cref="Hit"/> test. A participant moved here from an
        /// encounter of this zone's own is taken out of that one.
        /// </summary>
        public static void BrokerParticipants(int broker, uint id, string kind, uint state, IReadOnlyList<ulong> characters)
        {
            lock (sync)
            {
                if (state == EncounterMessages.StateEnded)
                {
                    if (encounters.Remove(id, out Encounter gone))
                        foreach (WorldSession p in gone.Participants)
                            if (encounterOf.TryGetValue(p, out Encounter f) && f == gone)
                                encounterOf.Remove(p);
                    log.Info($"Encounter {id} ({kind}) ended by the world channel.");
                    return;
                }
                if (!encounters.TryGetValue(id, out Encounter e))
                    encounters[id] = e = new Encounter { Id = id, Kind = kind, Broker = broker };
                else if (e.Broker != broker)
                {
                    log.Warn($"Encounter {id} is held by world-channel generation {e.Broker}, not {broker}: push ignored.");
                    return;
                }
                foreach (WorldSession p in e.Participants)
                    if (encounterOf.TryGetValue(p, out Encounter f) && f == e)
                        encounterOf.Remove(p);
                e.Participants.Clear();
                e.Entered.Clear();
                foreach (ulong guid in characters)
                {
                    WorldSession p = PlayerManager.FindByGuid(guid);
                    if (p == null)
                        continue;
                    if (encounterOf.TryGetValue(p, out Encounter old) && old != e)
                    {
                        old.Participants.Remove(p);
                        old.Entered.Remove(p);
                    }
                    e.Participants.Add(p);
                    encounterOf[p] = e;
                    if (state == EncounterMessages.StateRunning)
                        e.Entered.Add(p);
                }
                log.Info($"Encounter {id} ({kind}) from the world channel: state {state}, {string.Join(", ", e.Participants.Select(p => p.CharacterName))}.");
            }
        }

        /// <summary>EncounterNotice from the world channel: one of the duel chat lines, printed as the in-process code did.</summary>
        public static void Notice(ulong character, string text)
        {
            WorldSession session = PlayerManager.FindByGuid(character);
            if (session != null)
                Chat.System(session, text);
        }

        /// <summary>
        /// The world channel of <paramref name="broker"/> is gone (its link closed): every encounter it ran ends here
        /// with Terminate + Shutdown to the clients, so their encounter state is clean for the next one (spec §7, as
        /// brokered trades are canceled); a restarted service starts with an empty registry.
        /// </summary>
        public static void BrokerLost(int broker)
        {
            List<Encounter> gone;
            lock (sync)
                gone = encounters.Values.Where(e => e.Broker == broker).ToList();
            foreach (Encounter e in gone)
                End(e);
            if (gone.Count > 0)
                log.Warn($"World channel gone: {gone.Count} brokered encounter(s) ended.");
        }
    }
}
