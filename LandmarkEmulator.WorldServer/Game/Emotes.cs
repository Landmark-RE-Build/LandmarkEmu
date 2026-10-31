using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player emotes and one-off animations. Animations are AnimationSelection.txt rows ("EmoteSingle"/"EmoteLoop" rows
    /// pick the clip through their Emote_Index variable, AnimationSelectionVariables.txt). How each kind reaches the
    /// player's own client and the others (audit 2026-10-18, docs/protocol.md "Emotes"):
    /// <list type="bullet">
    /// <item>one-shot emotes: RequestAnimationSelection (0x26/0x65) without clear-sit, to the player and those within
    ///   <see cref="ViewRange"/> — not AbilityPlayAnim, which turns the character (see <see cref="PlayAnim(WorldSession, uint, Vector3?)"/>);</item>
    /// <item>stances (kneel, lie, dance, play dead and the walk stances skip / drunk / sneak) and "stand": the same
    ///   packet with clear-sit, remembered and shown to players who meet the player later (<see cref="ShowPose"/>);</item>
    /// <item>moods (angry, happy, sad; calm ends them): remembered the same way (<see cref="ShowMood"/>);</item>
    /// <item>each emote word's chat lines: "You wave." to the player, "[*speaker*] waves." to those in range, the "with
    ///   [*target*]" forms when the player has a target (<see cref="lines"/>).</item>
    /// </list>
    /// </summary>
    public static class Emotes
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Emote commands → AnimationSelection id. The words are live's (wiki "Commands List", 2016: moods, poses and
        /// gestures with their "Displayed Message"; the client's own emote word texts 559123-559194: groan, shoo, talk and
        /// nod are its words for the Frustrated, GoAway, TalkLong and Yes clips). Each word's Emote_Index comes from the
        /// player network (PlayerAnimationNetwork64.mrn). Its looping emotes sit under two Switch nodes whose child lists
        /// are, in order, Switch1 = Crafting, Beg, Bow, Cheer, Congratulate, Cry, Flirt, Frustrated, Giggle, GoAway, Laugh
        /// (Emote_Index 1-11) and Switch2 = Switch1, Point, Shrug, Sob, TalkLong, Taunt, Wave, No, Yes, Dancing (12-20);
        /// see trace/systems/social.md. The single-shot branch the EmoteSingle rows play (0.695 network,
        /// re/out/l117/player_switches.txt: Emotes_SingleShot|Nuetral, switches 11807 / 11808 / 11830 / 11842) has the same
        /// 1-20 and goes on: Switch2 [10] Onod (21), Switch3 = Switch2, UsePotion (22), Call (23), Cough (24), Flex (25),
        /// Give (26), Sneeze (27), Thanks (28), Welcome (29), Yawn (30), Come_Here (31), Switch4 = Switch3, Stretch (32),
        /// Hug (33) — exactly the Emote_Index values of the EmoteSingle rows 287, 450, 449, 451, 452-456 and 817 (no rows
        /// for Give / Thanks). So cough 449, flex 451, sneeze 452, welcome 453, yawn 454, beckon 455 (Come_Here), stretch
        /// 456 and hug 817 have their own clips [P]; each also has its four chat lines (texts 572091-572122, 575933-575936).
        /// "yell" plays the Call clip 450 with its lines 572100 / 572095 / 572114 / 572107 (OPEN-LEADS L192 (c)): the client
        /// has "yell" as an emote word text of its own (570042, next to those lines) and the 2016 wiki's emote table lists
        /// "/yell — You yell loudly." [P text, W]. The client also has /yell as an alias of its /shout chat command (chat
        /// manager set-up FUN_1433595d0 → FUN_14336e150(…, "yell", 0x36)); a zone command of the same name replaces it, because
        /// both go into one command table (FUN_142dd7560 removes the entry with the same name hash before adding the new
        /// one; the zone list handler FUN_1433bad50 → FUN_142dd7560, DynamicCommands.log "Command from server") [P]. So live's
        /// /yell was the emote and /shout the shout [I strong]. Setting YellIsEmote (Docker YELL_IS_EMOTE, default true =
        /// live); off leaves /yell to the client's shout. Index 1 (Crafting) is the crafting loop, not a word.
        /// </summary>
        private static readonly Dictionary<string, uint> emotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["beg"] = 228, ["bow"] = 229, ["cheer"] = 230, ["congratulate"] = 232, ["cry"] = 231, ["flirt"] = 233,
            ["groan"] = 234, ["giggle"] = 235, ["shoo"] = 222, ["laugh"] = 237, ["point"] = 238, ["shrug"] = 239,
            ["sob"] = 240, ["talk"] = 241, ["taunt"] = 242, ["wave"] = 217, ["no"] = 244, ["nod"] = 245,
            ["cough"] = 449, ["flex"] = 451, ["sneeze"] = 452, ["welcome"] = 453, ["yawn"] = 454, ["beckon"] = 455,
            ["stretch"] = 456, ["hug"] = 817, ["yell"] = 450,
            // "talking" (wiki "You talk continuously"; client word 559193): 252 = TalkLong (Emote_Index 15) with EmoteLoop 1,
            // the loop; "stop" (client word 559194): 253, EmoteLoop 0, which ends it.
            ["talking"] = 252, ["stop"] = 253,
            ["dance"] = 246, ["kneel"] = 224, ["lie"] = Lie, ["skip"] = 248, ["drunk"] = 250, ["sneak"] = 251, ["stand"] = 223,
            // Moods (the client's words Calm / Angry / Happy; wiki /sad): selections 218-221 only set Emote_Emotion 0-3,
            // which every Emotion_Switch in the network reads in the order neutral, Angry, Happy, Sad - the standing idle
            // and every later emote play in that mood until another mood is chosen.
            ["calm"] = Calm, ["angry"] = 219, ["happy"] = 220, ["sad"] = 221
        };

        /// <summary>
        /// Stance emotes (looping until replaced) and the "stand" selection that ends them. Both are sent as
        /// RequestAnimationSelection with the clear-sit bool (<see cref="ClearsSit"/>, <see cref="ShowPose"/>), never as
        /// AbilityPlayAnim, and a stance is remembered for players who meet the player later. 480 /lie: Stance_Index 5,
        /// the network's Laydown state (StanceSwitch children Stand, Kneel, Placeholder, Dance, Sit_Chair, Laydown, PlayDead,
        /// AFK_States), wiki "You lie down." / texts 571884-571887.
        /// A stance (and the client's own /sit) shows only while the character holds nothing: a held tool or weapon's
        /// actor sets WeaponIdle 1, and the network's StandPoses|Idle_Stand_2 switch then plays the weapon / tool idle,
        /// which has no Stance_Index poses (client rule, live behaviour; docs/protocol.md "Sitting"). The same holds for
        /// every emote. The server can put the held item away for them (<see cref="EmoteHolster"/>, setting
        /// EmotesPutAwayHeldItem, off by default = live, where nothing was put away).
        /// </summary>
        private static readonly HashSet<uint> stances = new() { 246, 224, 248, 250, 251, Lie, PlayDead };
        private const uint Stand = 223, Lie = 480;

        /// <summary>
        /// The walk stances: skip 248, drunk 250, sneak 251 (Stance_Index 102 / 101 / 104, the StanceSwitch "Switch1"
        /// children Drunk, Skip, Skip1, Sneak whose conditions are the ranges 100.5-104.5 on the locomotion transitions,
        /// trace/0695/agent-playmode.md §2.2). The character keeps them while it walks — the player's own lines say so:
        /// 560184 "(Use /stand to stop being drunk.)", 560187 "(Use /stand to stop skipping.)", 560189 "(Use /stand to stop
        /// sneaking.)" — so walking does not end them here (it ends the others, <see cref="Moved"/>).
        /// </summary>
        private static readonly HashSet<uint> walkStances = new() { 248, 249, 250, 251 };

        /// <summary>Moods (Emote_Emotion 0-3). <see cref="Calm"/> (0, the default) ends a mood.</summary>
        private static readonly HashSet<uint> moodSelections = new() { Calm, 219, 220, 221 };
        private const uint Calm = 218;

        /// <summary>
        /// /playdead (0.695 only; features audit #55c, trace/0695/agent-abilities.md §2). The word is the client's text 577222
        /// "playdead" (the zone's command list registers it, as the other emote words 559123-559192); live's emote set was
        /// server data, as for every word here. Its animation is AnimationSelection <b>844</b>, new in 0.695 with the
        /// variables Stance_Index 6 and BreatheSpeed 0 (AnimationSelectionVariables rows 2781 / 2782): in the 0.695
        /// PlayerAnimationNetwork64.mrn (Assets_153.pack) the Stance_Index switch StanceSwitch|Switch1 (node 14514, control
        /// ControlParameters|Stance_Index) has the children Stand, Kneel, Placeholder, Dance, Sit_Chair, Laydown,
        /// <b>PlayDead</b>, AFK_States, so index 6 is the new PlayDead state (clips Biped_Core_React_Death_01 / _2, exit
        /// Biped_Core_React_GetUp_F01; 0.589's switch had AFK_States at 6 and no PlayDead). BreatheSpeed 0 stops the
        /// breathing; 0.695 also gave "stand" (223) BreatheSpeed 1 (row 2783), which restores it. So it is a stance like
        /// kneel: it lasts until "stand" or another stance, and walking away ends it (<see cref="Moved"/>).
        /// </summary>
        public const uint PlayDead = 844;
        public const string PlayDeadWord = "playdead";

        /// <summary>
        /// /playdead's chat lines (texts new in 0.695): to the player 577224 "You play dead." / 577225 "You play dead with
        /// [*target*]."; to the others nearby 577227 "[*speaker*] plays dead." / 577226 "[*speaker*] plays dead with
        /// [*target*]." The "with" lines are used when the player has a target (TargetBase 0x2 / 0x3 →
        /// WorldSession.CurrentTarget) other than themselves. 577223 "Playing Dead" is the stance's label (not sent).
        /// </summary>
        private const uint PlayDeadSelf = 577224, PlayDeadSelfWith = 577225, PlayDeadOtherWith = 577226, PlayDeadOther = 577227;

        /// <summary>
        /// One emote word's chat lines (client text ids; 0 = the word has no such line, the line without the target is
        /// used): to the player, to the player with a target, to the others, to the others with a target.
        /// </summary>
        internal readonly record struct Lines(uint Self, uint SelfTarget, uint Other, uint OtherTarget);

        /// <summary>
        /// Each word's chat lines [P texts, W which word]: the wiki's "Displayed Message" column (Commands List, 2016) is the
        /// player's line for every word ("You wave.", "You plead for assistance.", "You groan loudly.", "You offer your
        /// congratulations. / You applaud [*target*].", "You feel sad." ...); the client's locale has, for each, the
        /// player's line, its "[*target*]" form and the others' two lines ("[*speaker*] waves." / "[*speaker*] waves to
        /// [*target*]."). Where the client has two copies of a line (559127 / 559402 "[*speaker*] waves.") either reads the
        /// same. Live sent them as server data (the words are the zone's command list); how exactly it sent them is not
        /// captured, so they go out as /playdead's do (ChatPacketFromStringId, emote flag: the Emote channel, client-side
        /// range check). Lines the client lacks: "no", "shoo", "happy", "drunk", "sneak", "talking" with a target for the
        /// player (their plain line is used). /sit's lines are the client's own (it prints "You sit down." and asks for
        /// 572086, <see cref="RelayEmoteText"/>).
        /// </summary>
        private static readonly Dictionary<string, Lines> lines = new(StringComparer.OrdinalIgnoreCase)
        {
            ["wave"] = new(559125, 560119, 559127, 559403),
            ["beg"] = new(559356, 560134, 560133, 560132),
            ["bow"] = new(560135, 560136, 559358, 559186),
            ["cheer"] = new(560137, 559357, 560138, 559124),
            ["congratulate"] = new(576530, 559126, 576532, 576531),
            ["cry"] = new(560142, 559359, 560143, 559360),
            ["flirt"] = new(560144, 559361, 560145, 559362),
            ["groan"] = new(560147, 559363, 560148, 559364),
            ["giggle"] = new(560149, 559365, 560150, 559366),
            ["shoo"] = new(560151, 0, 560152, 559368),
            ["laugh"] = new(560153, 559369, 560154, 559370),
            ["no"] = new(560155, 0, 559371, 576533),
            ["point"] = new(560158, 560157, 559373, 559374),
            ["shrug"] = new(560159, 559375, 560160, 559376),
            ["sob"] = new(560161, 559377, 560163, 560162),
            ["talk"] = new(560164, 559379, 560165, 559380),
            ["taunt"] = new(560166, 559381, 560168, 560167),
            ["nod"] = new(560169, 559383, 560170, 559384),
            ["cough"] = new(572092, 572091, 572094, 572093),
            ["flex"] = new(572101, 572096, 572115, 572108),
            ["sneeze"] = new(572102, 572121, 572116, 572109),
            ["welcome"] = new(572103, 572097, 572117, 572110),
            ["yawn"] = new(572104, 572098, 572118, 572111),
            ["beckon"] = new(572105, 572122, 572119, 572112),
            ["stretch"] = new(572106, 572099, 572120, 572113),
            ["hug"] = new(575934, 575933, 575936, 575935),
            ["yell"] = new(572100, 572095, 572114, 572107),   // "You yell loudly." / "You yell to [*target*]." / "[*speaker*] yells loudly" / "… to [*target*]."
            ["talking"] = new(560193, 0, 560194, 559407),
            ["dance"] = new(560191, 559404, 560192, 559405),
            ["kneel"] = new(560179, 559392, 560180, 559393),
            ["lie"] = new(571886, 571887, 571885, 571884),
            ["skip"] = new(560187, 559398, 560188, 559399),
            ["drunk"] = new(560184, 0, 560186, 560185),
            ["sneak"] = new(560189, 0, 560190, 559401),
            ["stand"] = new(560181, 559394, 560183, 560182),
            ["calm"] = new(560176, 559408, 560178, 560177),
            ["angry"] = new(560171, 559385, 560172, 559386),
            ["happy"] = new(560173, 0, 560174, 559388),
            ["sad"] = new(559389, 572123, 560175, 559390),
            [PlayDeadWord] = new(PlayDeadSelf, PlayDeadSelfWith, PlayDeadOther, PlayDeadOtherWith)
        };

        /// <summary>The chat lines of a word (tests, docs), or null.</summary>
        internal static Lines? LinesOf(string word) => lines.TryGetValue(word, out Lines l) ? l : null;

        /// <summary>The AnimationSelection of a word (tests), or 0.</summary>
        internal static uint SelectionOf(string word) => emotes.TryGetValue(word, out uint s) ? s : word == PlayDeadWord ? PlayDead : 0;

        /// <summary>
        /// A player's remembered pose: a stance (a looping pose: <see cref="stances"/>) or a mood: the selection, where it
        /// began (walking away from a non-walk stance ends it), and the players whose clients were shown it (they are the
        /// ones who must see the stand / the calm). <see cref="Ending"/>: the player walked out of the stance and the stand
        /// is waiting for them to stop (<see cref="StandWhenStopped"/>); newcomers are no longer shown it.
        /// <see cref="NextScan"/>: when a moving subject next looks for players who came within range.
        /// </summary>
        private sealed class Pose
        {
            public WorldSession Subject;
            public uint Selection;
            public bool Mood;
            public Vector3 At;
            public bool Ending;
            public DateTime NextScan;
            public readonly HashSet<ulong> Told = new();
            public byte[] Body => SelectionBody(Subject.PlayerGuid, Selection, clearSit: !Mood);
        }

        /// <summary>Stances, moods and looping emotes by player guid (<see cref="Pose"/>), under <see cref="poseSync"/>.</summary>
        private static readonly Dictionary<ulong, Pose> poses = new(), moods = new(), loops = new();

        /// <summary>
        /// The looping emotes (AnimationSelection "EmoteLoop" rows 236, 243, 252 "talking", 265: EmoteLoop 1) [P data]. A
        /// loop is a lasting actor variable like a mood (OPEN-LEADS L192 (b)): in the 0.695 player network the locomotion
        /// state machine Move|…|Locomotion (node 206) enters its emoting locomotion from any state on "EmoteLoop &gt; 0.5 and
        /// WeaponIdle &lt; 0.5" (transition 1408) and leaves it only on "EmoteLoop &lt; 0.5" (with ForwardSpeed &gt; 9, 1415, or
        /// the EndSplit request, 1416); the idle turns the same (1525 TurnEmote → 1476 on EmoteLoop &lt; 0.5) [P, decoded
        /// state machines]. So walking does not end a loop on any client; only "stop" (253, EmoteLoop 0) or a single-shot
        /// emote (every EmoteSingle row sets EmoteLoop 0) does. It is remembered like a mood: shown to players who meet the
        /// player later, never ended by walking.
        /// </summary>
        private static readonly HashSet<uint> loopSelections = new() { 236, 243, 252, 265 };

        /// <summary>A looping emote selection (<see cref="loopSelections"/>).</summary>
        internal static bool IsLoop(uint selection) => loopSelections.Contains(selection);
        private static readonly object poseSync = new();

        /// <summary>How often a moving player in a stance or mood looks for players who came within range of them.</summary>
        internal static TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The selections that replace the character's stance (Stance_Index): the stances and "stand". They go out as
        /// RequestAnimationSelection with its clear-sit bool (<see cref="SelectionBody"/>), because a sit is client state
        /// (<see cref="PositionUpdate.SittingState"/>): the client's own /sit sets the character's sitting flag, its
        /// movement carries it to every other client, and only moving / jumping or that bool clears it on the sitting
        /// client. AbilityPlayAnim only plays the animation: after "/sit, /stand" the player's own client showed them
        /// standing, but the flag stayed set and went on in their movement, so any client that applied it afresh (a
        /// newcomer, or a re-zoned viewer, from the last full movement) showed them sitting (2026-10-18). One-shot
        /// emotes and moods don't touch the stance and go without the clear-sit bool.
        /// </summary>
        internal static bool ClearsSit(uint selection) => selection == Stand || stances.Contains(selection);

        /// <summary>A mood selection (218-221).</summary>
        internal static bool IsMood(uint selection) => moodSelections.Contains(selection);

        /// <summary>
        /// Movement from the client. For the player's own stance: half a metre from where it began ends it (not the walk
        /// stances, <see cref="walkStances"/>); the "stand" is sent once the player has stopped (checked every half second
        /// for up to 15 s): a selection sent while the character is still moving is ignored by the client and the stance
        /// carries on. For the others' stances and moods: a player who comes within <see cref="ViewRange"/> of someone in
        /// one they were never shown is shown it once (they were too far when it began). A player who walks in a walk
        /// stance or a mood shows it, once a second at most, to the players they come within range of.
        /// </summary>
        public static void Moved(WorldSession session, Vector3 position)
            => Moved(session, position, () => PlayerManager.InZone(session.ZoneId));

        internal static void Moved(WorldSession session, Vector3 position, Func<IEnumerable<WorldSession>> zone)
        {
            // Every movement packet of every player comes here: no lock while nobody is in a stance or mood (a racy read
            // of the counts only delays a catch-up to the next packet).
            if (poses.Count == 0 && moods.Count == 0 && loops.Count == 0)
                return;
            bool walkedOut = false;
            List<byte[]> show = null;
            List<Pose> scan = null;
            DateTime now = DateTime.UtcNow;
            lock (poseSync)
            {
                if (poses.Count == 0 && moods.Count == 0 && loops.Count == 0)
                    return;
                foreach (Pose pose in poses.Values.Concat(moods.Values).Concat(loops.Values))
                {
                    if (pose.Subject == session)
                    {
                        if (!pose.Mood && !pose.Ending && !walkStances.Contains(pose.Selection) && Vector3.Distance(pose.At, position) > 0.5f)
                            pose.Ending = walkedOut = true;
                        else if (!pose.Ending && now >= pose.NextScan)
                        {
                            pose.NextScan = now + ScanInterval;
                            (scan ??= new()).Add(pose);
                        }
                    }
                    else if (!pose.Ending && pose.Subject.ZoneId == session.ZoneId && !pose.Told.Contains(session.PlayerGuid)
                        && Vector3.Distance(pose.Subject.Position, position) <= ViewRange)
                    {
                        pose.Told.Add(session.PlayerGuid);
                        (show ??= new()).Add(pose.Body);
                    }
                }
            }
            if (show != null)
                foreach (byte[] body in show)
                    session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, body);
            if (scan != null)
            {
                // The player list is taken outside the pose lock (PlayerManager has its own).
                List<WorldSession> near = zone().Where(p => p != session && !p.Disconnected
                    && Vector3.Distance(p.Position, position) <= ViewRange).ToList();
                var tell = new List<(WorldSession Viewer, byte[] Body)>();
                lock (poseSync)
                    foreach (Pose pose in scan)
                    {
                        if (pose.Ending)
                            continue;
                        foreach (WorldSession p in near)
                            if (pose.Told.Add(p.PlayerGuid))
                                tell.Add((p, pose.Body));
                    }
                foreach ((WorldSession viewer, byte[] body) in tell)
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, body);
            }
            if (walkedOut)
            {
                log.Debug($"{session.CharacterName} walked out of a stance.");
                _ = StandWhenStopped(session);
            }
        }

        private static async System.Threading.Tasks.Task StandWhenStopped(WorldSession session)
        {
            Vector3 last = session.Position;
            for (int i = 0; i < 30; i++)
            {
                await System.Threading.Tasks.Task.Delay(500);
                lock (poseSync)
                    if (session.Disconnected || !poses.TryGetValue(session.PlayerGuid, out Pose pose) || !pose.Ending)
                        return;                                // gone, already stood up, or started another stance
                Vector3 now = session.Position;
                if (Vector3.Distance(now, last) < 0.05f)
                {
                    log.Debug($"{session.CharacterName} stopped: stand.");
                    PlayAnim(session, Stand);
                    return;
                }
                last = now;
            }
        }

        /// <summary>
        /// Shows a stance, or "stand", on a player (<see cref="ClearsSit"/>): to the player, to everyone on the landscape
        /// within <see cref="ViewRange"/>, and to everyone who was shown the stance it replaces (wherever they are now, so
        /// nobody keeps a stale pose). A stance is remembered for players who meet the player later
        /// (<see cref="IntroducePose"/>, <see cref="Moved"/>); "stand" forgets it.
        /// </summary>
        internal static int ShowPose(WorldSession session, uint selection, IEnumerable<WorldSession> zone)
            => Show(poses, mood: false, session, selection, ends: selection == Stand, zone);

        /// <summary>
        /// Shows a mood (angry, happy, sad), or calm, on a player, the same way as a stance (<see cref="ShowPose"/>) without
        /// the clear-sit bool: a mood is an actor variable (Emote_Emotion) that lasts until another mood, so a player who
        /// meets the player later (in range later, a re-zone, a login) would otherwise see the neutral idle while the
        /// player's own client shows the mood. Calm (the default) forgets it.
        /// </summary>
        internal static int ShowMood(WorldSession session, uint selection, IEnumerable<WorldSession> zone)
            => Show(moods, mood: true, session, selection, ends: selection == Calm, zone);

        /// <summary>
        /// Shows a looping emote ("talking"), or "stop", on a player the way a mood is shown (<see cref="ShowMood"/>, no
        /// clear-sit): the loop lasts until "stop" or a single-shot emote, walking included (<see cref="loopSelections"/>),
        /// so it is remembered for players who meet the player later; "stop" forgets it and reaches everyone shown it.
        /// </summary>
        internal static int ShowLoop(WorldSession session, uint selection, IEnumerable<WorldSession> zone)
            => Show(loops, mood: true, session, selection, ends: selection == StopLoop, zone);

        /// <summary>The selection of a player's current looping emote (0 = none).</summary>
        internal static uint LoopOf(ulong player)
        {
            lock (poseSync)
                return loops.TryGetValue(player, out Pose pose) ? pose.Selection : 0;
        }

        private static int Show(Dictionary<ulong, Pose> table, bool mood, WorldSession session, uint selection, bool ends,
            IEnumerable<WorldSession> zone)
        {
            var audience = new List<WorldSession> { session };
            lock (poseSync)
            {
                table.TryGetValue(session.PlayerGuid, out Pose before);
                foreach (WorldSession p in zone)
                    if (p != session && !p.Disconnected && (Vector3.Distance(p.Position, session.Position) <= ViewRange
                            || (before != null && before.Told.Contains(p.PlayerGuid))))
                        audience.Add(p);
                if (ends)
                    table.Remove(session.PlayerGuid);
                else
                {
                    var pose = new Pose { Subject = session, Selection = selection, Mood = mood, At = session.Position,
                        NextScan = DateTime.UtcNow + ScanInterval };
                    foreach (WorldSession p in audience)
                        if (p != session)
                            pose.Told.Add(p.PlayerGuid);
                    table[session.PlayerGuid] = pose;
                }
            }
            byte[] body = SelectionBody(session.PlayerGuid, selection, clearSit: !mood);
            foreach (WorldSession viewer in audience)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, body);
            log.Info($"{session.CharacterName}: {(mood ? "mood" : "pose")} {selection}{(ends ? " (ends it)" : "")} shown to {audience.Count - 1} other player(s).");
            return audience.Count - 1;
        }

        /// <summary>
        /// A viewer was just sent a player's full character (FullCharacterDataRequest → LightweightToFullPc: zone-in, a
        /// re-zone, coming onto the landscape, coming near): the player's stance and mood, if they have one, so the
        /// newcomer sees the same as everyone else. One packet each per introduction, whatever the distance — the full
        /// character itself is far bigger. RequestAnimationSelection waits on the client until the character's actor is
        /// ready (case 100: FUN_142dfc010 fails → FUN_142e977b0 queues it). A client sit needs nothing here: it travels
        /// in the player's last full movement, which the LightweightToFullPc carries (<see cref="PositionUpdate.SittingState"/>).
        /// </summary>
        public static void IntroducePose(WorldSession viewer, WorldSession subject)
        {
            if (viewer == subject)
                return;
            byte[] stance = null, mood = null, loop = null;
            lock (poseSync)
            {
                if (poses.TryGetValue(subject.PlayerGuid, out Pose pose) && !pose.Ending)
                {
                    pose.Told.Add(viewer.PlayerGuid);
                    stance = pose.Body;
                }
                if (moods.TryGetValue(subject.PlayerGuid, out Pose m))
                {
                    m.Told.Add(viewer.PlayerGuid);
                    mood = m.Body;
                }
                if (loops.TryGetValue(subject.PlayerGuid, out Pose l))
                {
                    l.Told.Add(viewer.PlayerGuid);
                    loop = l.Body;
                }
            }
            if (stance != null)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, stance);
            if (mood != null)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, mood);
            if (loop != null)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, loop);
        }

        /// <summary>Left the world: their stance and mood are gone, and their client no longer holds anyone else's.</summary>
        public static void Forget(WorldSession session)
        {
            EmoteHolster.Forget(session);
            lock (poseSync)
                foreach (Dictionary<ulong, Pose> table in new[] { poses, moods, loops })
                {
                    if (table.TryGetValue(session.PlayerGuid, out Pose own) && own.Subject == session)
                        table.Remove(session.PlayerGuid);
                    foreach (Pose pose in table.Values)
                        pose.Told.Remove(session.PlayerGuid);
                }
        }

        /// <summary>The selection of a player's current stance (0 = none, or walking out of it).</summary>
        internal static uint StanceOf(ulong player)
        {
            lock (poseSync)
                return poses.TryGetValue(player, out Pose pose) && !pose.Ending ? pose.Selection : 0;
        }

        /// <summary>The selection of a player's current mood (0 = calm).</summary>
        internal static uint MoodOf(ulong player)
        {
            lock (poseSync)
                return moods.TryGetValue(player, out Pose pose) ? pose.Selection : 0;
        }

        /// <summary>
        /// RequestAnimationSelection body (PlayerUpdate 0x26 sub 0x65, <see cref="ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire"/>):
        /// u64 entity, u32 AnimationSelection, bool clear the sitting flag, u8 seated (0 = leave the client's seated byte
        /// as it is). Stances and "stand" clear the sit (they replace the stance), emotes and moods don't; none sets
        /// "seated": that byte makes the client stand the character up by itself on the next move / jump (FUN_142ecd1d0
        /// and the other callers of FUN_142e93b20), which for server stances <see cref="Moved"/> does; whether live set it
        /// for its stances is not known.
        /// </summary>
        internal static byte[] SelectionBody(ulong entity, uint selection, bool clearSit = true, bool seated = false)
        {
            var output = new List<byte>(14);
            var w = new GamePacketWriter(output);
            w.Write(entity);
            w.Write(selection);
            w.Write(clearSit);
            w.Write(seated);
            return output.ToArray();
        }

        /// <summary>
        /// "emoteclip n" plays emote clip n (Emote_Index 0-33: the EmoteSingle AnimationSelection row whose Emote_Index is n;
        /// 26 Give and 28 Thanks have none). The "EmoteLoop" rows (236, 243 wave, 252, 265) repeat until selection 253
        /// (EmoteLoop 0); 252 is "talking", 253 "stop".
        /// </summary>
        private static readonly uint[] emoteByIndex =
        {
            225, 247, 228, 229, 230, 232, 231, 233, 234, 235, 222, 237, 238, 239, 240, 241, 242, 217, 244, 245, 0, 260,
            287, 450, 449, 451, 0, 452, 0, 453, 454, 455, 456, 817
        };

        /// <summary>The single-shot emote selections (EmoteSingle rows: EmoteLoop 0), which end a running loop.</summary>
        private static readonly HashSet<uint> singleShots = new(emoteByIndex.Where(s => s != 0 && !loopSelections.Contains(s)));

        /// <summary>
        /// The server commands the client accepts as "/name" (CommandBase 0x44 dynamic command list: u16 sub, u32 n, n ×
        /// string, u32 source 1 = zone; client FUN_14330df60 registers each and logs DynamicCommands.log). Typing one
        /// sends Command 0x43 (see <see cref="HandleCommand"/>).
        /// </summary>
        public static void SendCommandList(WorldSession session)
        {
            List<string> names = ZoneCommandNames();
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(0x44);
            writer.Write((uint)names.Count);
            foreach (string name in names)
                writer.Write(name);
            writer.Write(1u);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
            DynamicCommands.SendAll(session);
        }

        /// <summary>
        /// The names of the zone's CommandListPacket (source 1): "emotelist", "emoteclip", the emote words ("yell" while
        /// <see cref="YellIsEmote"/>) and "playdead". The duel commands are world commands, added one by one after the
        /// list (DynamicCommands).
        /// <para>
        /// Not "emote" (OPEN-LEADS L192 (d)): /emote, /em and /me are the client's own free-text emote command (chat
        /// manager set-up FUN_1433595d0: FUN_14336e150(…, "emote" / "em" / "me", 0x37), the Emote chat channel; wiki Commands List
        /// 2016 "/emote &lt;text&gt; — Allows you to send any emote that you type") [P, W], and a zone command of that name
        /// would replace it (one command table, FUN_142dd7560) [P]. The server's "play clip n" command is "emoteclip".
        /// </para>
        /// </summary>
        public static List<string> ZoneCommandNames()
        {
            var names = new List<string> { "emotelist", EmoteClipCommand };
            names.AddRange(emotes.Keys.Where(w => YellIsEmote || !w.Equals(YellWord, StringComparison.OrdinalIgnoreCase)));
            names.Add(PlayDeadWord);
            return names;
        }

        /// <summary>The server's "play emote clip n" command ("emote" is the client's own free-text emote).</summary>
        public const string EmoteClipCommand = "emoteclip";

        /// <summary>The yell emote word (the Call clip).</summary>
        public const string YellWord = "yell";

        /// <summary>Setting YellIsEmote: /yell is the yell emote (true = live) or the client's shout alias.</summary>
        public static bool YellIsEmote
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.YellIsEmote ?? true;

        /// <summary>The duel commands (world commands since 2026-10-16, <see cref="DynamicCommands"/>).</summary>
        public static readonly string[] DuelCommands = { "duel", "duelaccept", "dueldecline", "duelleave" };

        /// <summary>
        /// Command 0x43 ClientCommandPacket (writer FUN_143316f90): a "/name args" the server registered. Body: string
        /// name, u32 n × string arguments (words typed after the name; "/emote 4" = "emote", ["4"]).
        /// </summary>
        public static void HandleCommand(WorldSession session, byte[] body)
        {
            string name, args;
            try
            {
                var r = new GamePacketReader(body);
                name = r.ReadString();
                uint n = r.ReadUInt();
                var parts = new List<string>();
                for (uint i = 0; i < n && i < 16; i++)
                    parts.Add(r.ReadString());
                args = string.Join(' ', parts);
            }
            catch (Exception)
            {
                log.Warn($"Unreadable client command from {session.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }
            log.Info($"{session.CharacterName} ran command {name} {args}".TrimEnd());
            Run(session, name, args);
        }

        /// <summary>Runs one of these commands (from the zone list, 0x43, or added one by one, 0x47).</summary>
        public static void Run(WorldSession session, string name, string args)
        {
            // Duels (an encounter kind; docs/encounters.md): 0.589 has no duel menu, so they start from commands.
            if (name.Equals("duel", StringComparison.OrdinalIgnoreCase))
                WorldChannelServices.WorldChannel.DuelChallenge(session, args);
            else if (name.Equals("duelaccept", StringComparison.OrdinalIgnoreCase) || name.Equals("dueldecline", StringComparison.OrdinalIgnoreCase))
                WorldChannelServices.WorldChannel.DuelAnswer(session, 0, name.Equals("duelaccept", StringComparison.OrdinalIgnoreCase));
            else if (name.Equals("duelleave", StringComparison.OrdinalIgnoreCase))
                WorldChannelServices.WorldChannel.EncounterLeave(session, "left");
            else if (name.Equals("emotelist", StringComparison.OrdinalIgnoreCase))
                Chat.System(session, "Emotes: /" + string.Join(", /", ZoneCommandNames().Where(n => n != "emotelist" && n != EmoteClipCommand))
                    + ". Also /" + EmoteClipCommand + " 0-33 for every emote clip.");
            else if (name.Equals(PlayDeadWord, StringComparison.OrdinalIgnoreCase))
                EmoteHolster.Then(session, EmoteHolster.PutAwayFor(session, untilStand: false), () => PlayDeadNow(session));
            else if (name.Equals(EmoteClipCommand, StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args, out int index) && index >= 0 && index < emoteByIndex.Length && emoteByIndex[index] != 0)
                    EmoteHolster.Then(session, EmoteHolster.PutAwayFor(session), () => PlayAnim(session, emoteByIndex[index]));
                else
                    Chat.System(session, "Usage: /" + EmoteClipCommand + " 0-33");
            }
            else if (emotes.ContainsKey(name) && (YellIsEmote || !name.Equals(YellWord, StringComparison.OrdinalIgnoreCase)))
                EmoteWord(session, name);
        }

        /// <summary>
        /// An emote word typed by the player. A held tool or weapon hides every emote and stance (the client's animation
        /// network plays them only with empty hands), so it is put away first and the word runs a moment later
        /// (<see cref="EmoteHolster"/>, setting EmotesPutAwayHeldItem). /stand and /stop bring it back; moods leave it
        /// alone (they show whenever the hands are empty).
        /// </summary>
        private static void EmoteWord(WorldSession session, string word)
        {
            uint selection = emotes[word];
            if (selection == Stand || selection == StopLoop || IsMood(selection))
            {
                Emote(session, word, PlayerManager.InZone(session.ZoneId));
                if (!IsMood(selection))
                    EmoteHolster.Stood(session);
                return;
            }
            bool? untilStand = stances.Contains(selection) ? walkStances.Contains(selection) : null;
            bool delayed = EmoteHolster.PutAwayFor(session, untilStand);
            EmoteHolster.Then(session, delayed, () => Emote(session, word, PlayerManager.InZone(session.ZoneId)));
        }

        /// <summary>"stop" (EmoteLoop 0): ends the looping emote ("talking").</summary>
        private const uint StopLoop = 253;

        /// <summary>
        /// An emote word: its animation (<see cref="Play"/>), its chat lines (<see cref="ShowLines"/>) and the secret emote
        /// goals 492-503 (TASKS 5.10). <paramref name="zone"/>: the players on the player's landscape.
        /// </summary>
        internal static void Emote(WorldSession session, string word, IReadOnlyCollection<WorldSession> zone)
        {
            if (!emotes.TryGetValue(word, out uint selection))
                return;
            Play(session, selection, null, includeSelf: true, zone);
            ShowLines(session, word, zone);
            Achievements.Emoted(session, selection);
        }

        /// <summary>How far away other players see an emote, metres.</summary>
        public const float ViewRange = 80f;

        /// <summary>
        /// /playdead: the PlayDead stance (<see cref="PlayDead"/>) for the player and the players within
        /// <see cref="ViewRange"/>, and its chat lines (<see cref="ShowLines"/>): the player's own line and the others'
        /// line, "with [*target*]" when the player has someone targeted. The client fills [*speaker*] and [*target*] from
        /// those entities (0.695 FUN_143364980 case 4: speaker, target, owner, player tokens).
        /// </summary>
        public static void PlayDeadNow(WorldSession session)
        {
            List<WorldSession> zone = PlayerManager.InZone(session.ZoneId);
            Play(session, PlayDead, null, includeSelf: true, zone);
            int shown = ShowLines(session, PlayDeadWord, zone);
            log.Info($"{session.CharacterName} plays dead (line shown to {shown} player(s)).");
        }

        /// <summary>
        /// A word's chat lines (<see cref="lines"/>): the player's line to the player, the others' line to each player on
        /// the landscape within <see cref="ViewRange"/> who isn't ignoring them; the "[*target*]" forms when the player has
        /// a target (TargetBase → WorldSession.CurrentTarget) other than themselves. Returns how many others got it.
        /// </summary>
        internal static int ShowLines(WorldSession session, string word, IEnumerable<WorldSession> zone)
        {
            if (!lines.TryGetValue(word, out Lines l))
                return 0;
            ulong target = session.CurrentTarget != session.PlayerGuid ? session.CurrentTarget : 0;
            uint self = target != 0 && l.SelfTarget != 0 ? l.SelfTarget : l.Self;
            uint other = target != 0 && l.OtherTarget != 0 ? l.OtherTarget : l.Other;
            if (self != 0)
                session.EnqueueMessage(ZoneMessageOpcode.ChatBase, FromStringId(session.PlayerGuid, self, target));
            if (other == 0)
                return 0;
            byte[] others = FromStringId(session.PlayerGuid, other, target);
            int shown = 0;
            foreach (WorldSession p in zone)
                if (p != session && !p.Disconnected && Vector3.Distance(p.Position, session.Position) <= ViewRange
                    && !Social.SocialServices.IgnoreCache.Ignoring(p, session))
                {
                    p.EnqueueMessage(ZoneMessageOpcode.ChatBase, others);
                    shown++;
                }
            return shown;
        }

        /// <summary>The emote lines the client asks others to see: "/sit" sends Emote.SitOther "[*speaker*] sits down."</summary>
        private static readonly HashSet<uint> relayedEmoteTexts = new() { 572086 };

        /// <summary>
        /// EmoteStringById (PlayerUpdate 0x26 sub 0x77; built by the client's /sit, FUN_1433080a0, which prints its own
        /// "You sit down." and sends u32 text id Emote.SitOther for the others): relayed to the players within
        /// <see cref="ViewRange"/> as ChatPacketFromStringId (ChatBase 0x13 sub 4, reader FUN_1432a9950): {u64 speaker,
        /// u32 text id, bool, bool, bool, u32, u64 target, u64 owner, u32 elapsed}; their client fills [*speaker*] from the
        /// speaker's entity (FUN_1432b7bc0 case 4). Only the client's own emote lines are relayed.
        /// </summary>
        public static void RelayEmoteText(WorldSession session, byte[] body)
        {
            if (body == null || body.Length < 4)
                return;
            uint text = BitConverter.ToUInt32(body, body.Length - 4);
            if (!relayedEmoteTexts.Contains(text))
                return;
            byte[] packet = FromStringId(session.PlayerGuid, text);
            int shown = 0;
            foreach (WorldSession other in PlayerManager.InZone(session.ZoneId))
                if (other != session && Vector3.Distance(other.Position, session.Position) <= ViewRange && !Social.SocialServices.IgnoreCache.Ignoring(other, session))
                {
                    other.EnqueueMessage(ZoneMessageOpcode.ChatBase, packet);
                    shown++;
                }
            log.Info($"{session.CharacterName}: emote text {text} shown to {shown} player(s).");
            // The client has already set its sit; with the held item put away it shows (EmoteHolster).
            EmoteHolster.PutAwayFor(session, untilStand: false);
        }

        /// <summary>
        /// ChatPacketFromStringId body (ChatBase 0x13 sub 4, reader FUN_1432a9950; 0.695 reader FUN_143356860): {u16 4,
        /// u64 speaker, u32 text id, bool emote (+0x24), bool (+0x25), bool bubble (+0x26), u32 bubble colour (+0x28),
        /// u64 target (+0x38), u64 owner (+0x30), u32 elapsed (+0x40)}. The client fills [*speaker*] / [*target*] /
        /// [*owner*] / [*player*] from those entities and prints nothing when it doesn't know the speaker. How the 0.695
        /// handler (FUN_143364980 case 4) presents the line:
        /// <list type="bullet">
        /// <item>bubble (+0x26) set: an overhead bubble (FUN_1433ff200) in the colour u32 (0 white, 1 red, 2 yellow,
        ///   3 green, 4 blue), "name: text" when the emote flag is clear; then nothing in chat when the emote flag is set;</item>
        /// <item>emote flag (+0x24) <b>clear</b>: FUN_14336da10 → chat add FUN_142ca7920 on <b>channel 0 (Say)</b>, i.e.
        ///   558679 " [[*player*]] says: [*message*]" — what /playdead first showed in game (2026-10-17);</item>
        /// <item>emote flag <b>set</b> (bubble clear): FUN_14336cb40 → FUN_142ca7920 on <b>channel 4 (Emote)</b>, only
        ///   when the local player is within the emote range of the speaker's position (a client setting looked up by
        ///   hash). The +0x25 bool is passed through to the chat add as its 4th argument in both paths (left false).</item>
        /// </list>
        /// Every caller here sends an emote line (the emote words, the /sit relay, a Story Node's "Announce Emotes in
        /// Chat"), so <paramref name="emote"/> defaults to true.
        /// </summary>
        internal static byte[] FromStringId(ulong speaker, uint text, ulong target = 0, bool emote = true)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(4);
            w.Write(speaker);
            w.Write(text);
            w.Write(emote);
            w.Write(false);
            w.Write(false);
            w.Write(0u);
            w.Write(target);
            w.Write(0ul);
            w.Write(0u);
            return output.ToArray();
        }

        /// <summary>
        /// An emote line spoken by an entity that is not a player (a story creature's spawner for a Story Node's
        /// "Announce Emotes in Chat", StoryTools): one packet for all the given viewers.
        /// </summary>
        public static void ShowLine(IEnumerable<WorldSession> viewers, ulong speaker, uint text)
        {
            byte[] packet = FromStringId(speaker, text);
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.ChatBase, packet);
        }

        /// <summary>
        /// Plays an AnimationSelection on a player for everyone nearby, the player included. With a facing point it is
        /// AbilityPlayAnim (Abilities 0x27 sub 0x12: header u16 sub, u8 flag, u32 time; u64 entity, u32 selection, point
        /// x, y, z, w), which turns the character towards the point; without one it is RequestAnimationSelection (see
        /// <see cref="Play"/>), which doesn't turn it.
        /// </summary>
        public static void PlayAnim(WorldSession session, uint selection, Vector3? facing = null)
            => PlayAnim(session, selection, facing, includeSelf: true);

        /// <summary>
        /// A tool swing as other players see it: the "Enter" selection (wind-up), then its "Release" (the strike) a moment
        /// later. AnimationSelection classes: pick 1001 (226/227), axe 1002 (254/255), sickle 1004 (256/257), matched by
        /// eye. The swinging player's own client animates the swing itself, so only the others are sent it.
        /// </summary>
        public static void ShowSwing(WorldSession session, uint enter, Vector3 towards, bool includeSelf = false)
        {
            PlayAnim(session, enter, towards, includeSelf);
            System.Threading.Tasks.Task.Delay(350).ContinueWith(_ =>
            {
                if (!session.Disconnected)
                    PlayAnim(session, enter + 1, towards, includeSelf);
            });
        }

        /// <summary>
        /// A weapon attack as other players see it: class 1007 Enter/Release (261 aim, 262 fire; the held weapon picks the
        /// clip — the six-shooters' aim and shot), turned towards the target. Selection 1 "Attack" alone shows nothing
        /// unless the character is already in that stance.
        /// </summary>
        public static void ShowAttack(WorldSession session, Vector3 towards)
        {
            ShowSwing(session, 261, towards);
            // The aim stance stays after the shot; 2.5 s after the last one a lone class-1006 Release (259) lowers the
            // weapon and returns to idle (262/264 keep the stance; 223 "stand" doesn't end it).
            long shot = attackStamps.AddOrUpdate(session.PlayerGuid, 1, (_, v) => v + 1);
            System.Threading.Tasks.Task.Delay(2500).ContinueWith(_ =>
            {
                if (!session.Disconnected && attackStamps.TryGetValue(session.PlayerGuid, out long latest) && latest == shot)
                    PlayAnim(session, 259, null, includeSelf: false);
            });
        }

        private static readonly ConcurrentDictionary<ulong, long> attackStamps = new();

        /// <summary>
        /// A weapon attack as other players see it: the held weapon's own Enter selection, then its Release after the
        /// attack time (or a one-shot Attack selection alone), turned towards the target. The attacker's own client
        /// gets the same pair through the cast success (Abilities.Answer). See WeaponAttacks.AttackAnimation.
        /// </summary>
        public static void ShowWeaponAttack(WorldSession session, uint enter, uint release, int releaseMs, Vector3 towards)
        {
            PlayAnim(session, enter, towards, includeSelf: false);
            if (release != 0)
                System.Threading.Tasks.Task.Delay(releaseMs).ContinueWith(_ =>
                {
                    if (!session.Disconnected)
                        PlayAnim(session, release, towards, includeSelf: false);
                });
        }

        private static void PlayAnim(WorldSession session, uint selection, Vector3? facing, bool includeSelf)
            => Play(session, selection, facing, includeSelf, PlayerManager.InZone(session.ZoneId));

        /// <summary>
        /// Plays a selection on a player for the player (when <paramref name="includeSelf"/>) and the players on the
        /// landscape within <see cref="ViewRange"/>:
        /// <list type="bullet">
        /// <item>a stance or "stand" → <see cref="ShowPose"/>; a mood → <see cref="ShowMood"/>;</item>
        /// <item>no facing point (emotes, the weapon-lowering 259) → RequestAnimationSelection without clear-sit. Until
        ///   2026-10-18 these were AbilityPlayAnim aimed at the player's own position "so they don't turn". But its
        ///   handler (0.695 FUN_142e1b9d0) always sets the character's heading to atan2 of (point − the entity's position
        ///   on that client) (FUN_1437f7c8c → FUN_142ec4e60), and with the point on the character that difference is only
        ///   each client's rounding of the position (1 cm on the wire, the remote frame's float on the others), so every
        ///   emote turned the character to a random heading — a different one on each screen (the audit's own-screen vs
        ///   observer mismatch). RequestAnimationSelection (case 100) plays the same selection through the same
        ///   FUN_142dfc010 without touching the heading, and queues it while the actor isn't ready;</item>
        /// <item>a facing point (tool swings, weapon attacks towards the target) → AbilityPlayAnim to the others, to turn
        ///   the character; the player's own client gets RequestAnimationSelection instead: on its own screen the turn
        ///   pulled the player (and the camera) towards each swing while they were moving, and AbilityPlayAnim aimed at its
        ///   own position turns it at random (above).</item>
        /// </list>
        /// </summary>
        internal static void Play(WorldSession session, uint selection, Vector3? facing, bool includeSelf, IEnumerable<WorldSession> zone)
        {
            if (ClearsSit(selection))
            {
                ShowPose(session, selection, zone);
                return;
            }
            if (IsMood(selection))
            {
                ShowMood(session, selection, zone);
                return;
            }
            if (IsLoop(selection) || selection == StopLoop)
            {
                ShowLoop(session, selection, zone);
                return;
            }
            // A single-shot emote sets EmoteLoop 0 on the client (every EmoteSingle row), which ends a running loop:
            // forget it, and tell the viewers shown it who are out of range now (the others see the new emote).
            if (singleShots.Contains(selection) && LoopOf(session.PlayerGuid) != 0)
                ShowLoop(session, StopLoop, zone);
            IEnumerable<WorldSession> viewers = zone.Where(p => p == session ? includeSelf
                : !p.Disconnected && Vector3.Distance(p.Position, session.Position) <= ViewRange);
            if (includeSelf && !zone.Contains(session))
                viewers = viewers.Append(session);
            if (facing == null)
            {
                byte[] selectionBody = SelectionBody(session.PlayerGuid, selection, clearSit: false);
                foreach (WorldSession viewer in viewers)
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, selectionBody);
                return;
            }
            byte[] body = AnimBody(session, selection, facing);
            foreach (WorldSession viewer in viewers)
                if (viewer == session)
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire,
                        SelectionBody(session.PlayerGuid, selection, clearSit: false));
                else
                    viewer.EnqueueMessage(ZoneMessageOpcode.AbilityPlayAnim, body);
        }

        /// <summary>PlayAnim body: header, player guid, animation selection, the point to face.</summary>
        internal static byte[] AnimBody(WorldSession session, uint selection, Vector3? facing)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)0x12);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(session.PlayerGuid);
            writer.Write(selection);
            Vector3 at = facing ?? session.Position;
            writer.Write(at.X);
            writer.Write(at.Y);
            writer.Write(at.Z);
            writer.Write(1f);
            return output.ToArray();
        }
    }
}
