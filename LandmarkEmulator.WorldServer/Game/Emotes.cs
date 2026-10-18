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
    /// pick the clip through their Emote_Index variable, AnimationSelectionVariables.txt), played with AbilityPlayAnim.
    /// </summary>
    public static class Emotes
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Emote commands → AnimationSelection id. The names are the client's own emote words (text 559123-559192:
        /// groan, shoo, talk and nod are its words for the Frustrated, GoAway, TalkLong and Yes clips). Each word's
        /// Emote_Index comes from the player network (PlayerAnimationNetwork64.mrn): the neutral emotes sit under two
        /// Switch nodes whose child lists are, in order, Switch1 = Crafting, Beg, Bow, Cheer, Congratulate, Cry, Flirt,
        /// Frustrated, Giggle, GoAway, Laugh (Emote_Index 1-11) and Switch2 = Switch1, Point, Shrug, Sob, TalkLong,
        /// Taunt, Wave, No, Yes, Dancing (12-20); see trace/systems/social.md. Index 1 (Crafting) is the crafting
        /// loop, not a word. Stances (Stance_Index rows) loop until "stand" (223) or another stance replaces them.
        /// </summary>
        private static readonly Dictionary<string, uint> emotes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["beg"] = 228, ["bow"] = 229, ["cheer"] = 230, ["congratulate"] = 232, ["cry"] = 231, ["flirt"] = 233,
            ["groan"] = 234, ["giggle"] = 235, ["shoo"] = 222, ["laugh"] = 237, ["point"] = 238, ["shrug"] = 239,
            ["sob"] = 240, ["talk"] = 241, ["taunt"] = 242, ["wave"] = 217, ["no"] = 244, ["nod"] = 245,
            ["dance"] = 246, ["kneel"] = 224, ["skip"] = 248, ["drunk"] = 250, ["sneak"] = 251, ["stand"] = 223,
            // Moods (the client's words Calm / Angry / Happy): selections 218-220 only set Emote_Emotion 0-2, which every
            // Emotion_Switch in the network reads in the order neutral, Angry, Happy, Sad (221, no client word) - the
            // standing idle and every later emote play in that mood until another mood is chosen.
            ["calm"] = 218, ["angry"] = 219, ["happy"] = 220
        };

        /// <summary>
        /// Stance emotes (looping until replaced) and the "stand" selection that ends them. Both are sent as
        /// RequestAnimationSelection with the clear-sit bool (<see cref="ClearsSit"/>, <see cref="ShowPose"/>), never as
        /// AbilityPlayAnim, and a stance is remembered for players who meet the player later.
        /// </summary>
        private static readonly HashSet<uint> stances = new() { 246, 224, 248, 250, 251, PlayDead };
        private const uint Stand = 223;

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
        /// 0.589 has neither the selection nor the texts: the word is only offered to, and the pose and lines only sent to,
        /// 0.695 clients.
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
        /// A player's current stance (a looping pose the server played: <see cref="stances"/>): the selection, where it
        /// began (walking away from it ends it), its landscape, and the players whose clients were shown it (they are the
        /// ones who must see the stand). <see cref="Ending"/>: the player walked out of it and the stand is waiting for
        /// them to stop (<see cref="StandWhenStopped"/>); newcomers are no longer shown it.
        /// </summary>
        private sealed class Pose
        {
            public WorldSession Subject;
            public uint Selection;
            public Vector3 At;
            public uint Zone;
            public bool Ending;
            public readonly HashSet<ulong> Told = new();
        }

        /// <summary>Stances by player guid (<see cref="Pose"/>), under <see cref="poseSync"/>.</summary>
        private static readonly Dictionary<ulong, Pose> poses = new();
        private static readonly object poseSync = new();

        /// <summary>
        /// The selections that replace the character's stance (Stance_Index): the stances and "stand". They go out as
        /// RequestAnimationSelection with its clear-sit bool (<see cref="SelectionBody"/>), because a sit is client state
        /// (<see cref="PositionUpdate.SittingState"/>): the client's own /sit sets the character's sitting flag, its
        /// movement carries it to every other client, and only moving / jumping or that bool clears it on the sitting
        /// client. AbilityPlayAnim only plays the animation: after "/sit, /stand" the player's own client showed them
        /// standing, but the flag stayed set and went on in their movement, so any client that applied it afresh (a
        /// newcomer, or a re-zoned viewer, from the last full movement) showed them sitting (2026-10-18). One-shot
        /// emotes and moods don't touch the stance and keep AbilityPlayAnim.
        /// </summary>
        internal static bool ClearsSit(uint selection) => selection == Stand || stances.Contains(selection);

        /// <summary>
        /// Movement from the client. For the player's own stance: half a metre from where it began ends it; the "stand" is
        /// sent once the player has stopped (checked every half second for up to 15 s): a selection sent while the
        /// character is still moving is ignored by the client and the stance carries on. For the others' stances: a
        /// player who comes within <see cref="ViewRange"/> of someone in a stance they were never shown is shown it once
        /// (they were too far when it began).
        /// </summary>
        public static void Moved(WorldSession session, Vector3 position)
        {
            // Every movement packet of every player comes here: no lock while nobody is in a stance (a racy read of the
            // count only delays a catch-up to the next packet).
            if (poses.Count == 0)
                return;
            bool walkedOut = false;
            List<(ulong Subject, uint Selection)> show = null;
            lock (poseSync)
            {
                if (poses.Count == 0)
                    return;
                foreach (Pose pose in poses.Values)
                {
                    if (pose.Subject == session)
                    {
                        if (!pose.Ending && Vector3.Distance(pose.At, position) > 0.5f)
                            pose.Ending = walkedOut = true;
                    }
                    else if (!pose.Ending && pose.Zone == session.ZoneId && !pose.Told.Contains(session.PlayerGuid)
                        && Vector3.Distance(pose.Subject.Position, position) <= ViewRange)
                    {
                        pose.Told.Add(session.PlayerGuid);
                        (show ??= new()).Add((pose.Subject.PlayerGuid, pose.Selection));
                    }
                }
            }
            if (show != null)
                foreach ((ulong subject, uint selection) in show)
                    session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, SelectionBody(subject, selection));
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
        {
            var audience = new List<WorldSession> { session };
            lock (poseSync)
            {
                poses.TryGetValue(session.PlayerGuid, out Pose before);
                foreach (WorldSession p in zone)
                    if (p != session && !p.Disconnected && (Vector3.Distance(p.Position, session.Position) <= ViewRange
                            || (before != null && before.Told.Contains(p.PlayerGuid))))
                        audience.Add(p);
                if (selection == Stand)
                    poses.Remove(session.PlayerGuid);
                else
                {
                    var pose = new Pose { Subject = session, Selection = selection, At = session.Position, Zone = session.ZoneId };
                    foreach (WorldSession p in audience)
                        if (p != session)
                            pose.Told.Add(p.PlayerGuid);
                    poses[session.PlayerGuid] = pose;
                }
            }
            byte[] body = SelectionBody(session.PlayerGuid, selection);
            foreach (WorldSession viewer in audience)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, body);
            log.Info($"{session.CharacterName}: pose {selection}{(selection == Stand ? " (stand)" : "")} shown to {audience.Count - 1} other player(s).");
            return audience.Count - 1;
        }

        /// <summary>
        /// A viewer was just sent a player's full character (FullCharacterDataRequest → LightweightToFullPc: zone-in, a
        /// re-zone, coming onto the landscape): the player's stance, if they are in one, so the newcomer sees the same
        /// pose as everyone else. One packet per introduction, whatever the distance — the full character itself is far
        /// bigger. A client sit needs nothing here: it travels in the player's last full movement, which the
        /// LightweightToFullPc carries (<see cref="PositionUpdate.SittingState"/>).
        /// </summary>
        public static void IntroducePose(WorldSession viewer, WorldSession subject)
        {
            uint selection = 0;
            lock (poseSync)
                if (viewer != subject && poses.TryGetValue(subject.PlayerGuid, out Pose pose) && !pose.Ending && pose.Zone == subject.ZoneId)
                {
                    pose.Told.Add(viewer.PlayerGuid);
                    selection = pose.Selection;
                }
            if (selection != 0)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire, SelectionBody(subject.PlayerGuid, selection));
        }

        /// <summary>Left the world: their stance is gone, and their client no longer holds anyone else's.</summary>
        public static void Forget(WorldSession session)
        {
            lock (poseSync)
            {
                if (poses.TryGetValue(session.PlayerGuid, out Pose own) && own.Subject == session)
                    poses.Remove(session.PlayerGuid);
                foreach (Pose pose in poses.Values)
                    pose.Told.Remove(session.PlayerGuid);
            }
        }

        /// <summary>The selection of a player's current stance (0 = none, or walking out of it).</summary>
        internal static uint StanceOf(ulong player)
        {
            lock (poseSync)
                return poses.TryGetValue(player, out Pose pose) && !pose.Ending ? pose.Selection : 0;
        }

        /// <summary>
        /// RequestAnimationSelection body (PlayerUpdate 0x26 sub 0x65, <see cref="ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire"/>):
        /// u64 entity, u32 AnimationSelection, bool clear the sitting flag, u8 seated (0 = leave the client's seated byte
        /// as it is). Ours always clears the sit (every caller replaces the stance) and never sets "seated": that byte
        /// makes the client stand the character up by itself on the next move / jump (FUN_142ecd1d0 and the other callers
        /// of FUN_142e93b20), which for server stances <see cref="Moved"/> does; whether live set it for its stances is
        /// not known.
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
        /// "emote n" plays emote clip n (Emote_Index 0-21: the EmoteSingle AnimationSelection row whose Emote_Index is n).
        /// The "EmoteLoop" rows (236, 243 wave, 252, 265) repeat until selection 253 (EmoteLoop 0) and aren't used.
        /// </summary>
        private static readonly uint[] emoteByIndex =
            { 225, 247, 228, 229, 230, 232, 231, 233, 234, 235, 222, 237, 238, 239, 240, 241, 242, 217, 244, 245, 0, 260 };

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
        /// The names of the zone's CommandListPacket (source 1): "emotelist", "emote", the emote words and "playdead" (the
        /// PlayDead stance is 0.695 only). The duel commands are world commands, added one by one after the list
        /// (DynamicCommands).
        /// </summary>
        public static List<string> ZoneCommandNames()
        {
            var names = new List<string> { "emotelist", "emote" };
            names.AddRange(emotes.Keys);
            names.Add(PlayDeadWord);
            return names;
        }

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
                Chat.System(session, "Emotes: /" + string.Join(", /", emotes.Keys.Append(PlayDeadWord))
                    + ". Also /emote 0-21 for every emote clip.");
            else if (name.Equals(PlayDeadWord, StringComparison.OrdinalIgnoreCase))
                PlayDeadNow(session);
            else if (name.Equals("emote", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args, out int index) && index >= 0 && index < emoteByIndex.Length && emoteByIndex[index] != 0)
                    PlayAnim(session, emoteByIndex[index]);
                else
                    Chat.System(session, "Usage: /emote 0-21");
            }
            else if (emotes.TryGetValue(name, out uint selection))
                { PlayAnim(session, selection); Achievements.Emoted(session, selection); }   // secret emote goals 492-503 (TASKS 5.10)
        }

        /// <summary>How far away other players see an emote, metres.</summary>
        public const float ViewRange = 80f;

        /// <summary>
        /// /playdead: the PlayDead stance (<see cref="PlayDead"/>) for the player and the players within
        /// <see cref="ViewRange"/>, and its chat lines (ChatPacketFromStringId, <see cref="FromStringId"/>): the player's own
        /// line and the others' line, "with [*target*]" when the player has someone targeted. The client fills [*speaker*]
        /// and [*target*] from those entities (0.695 FUN_143364980 case 4: speaker, target, owner, player tokens).
        /// </summary>
        public static void PlayDeadNow(WorldSession session)
        {
            ulong target = session.CurrentTarget != session.PlayerGuid ? session.CurrentTarget : 0;
            PlayAnim(session, PlayDead, null, includeSelf: true);
            session.EnqueueMessage(ZoneMessageOpcode.ChatBase, FromStringId(session.PlayerGuid, target != 0 ? PlayDeadSelfWith : PlayDeadSelf, target));
            byte[] others = FromStringId(session.PlayerGuid, target != 0 ? PlayDeadOtherWith : PlayDeadOther, target);
            int shown = 0;
            foreach (WorldSession other in PlayerManager.InZone(session.ZoneId))
                if (other != session && Vector3.Distance(other.Position, session.Position) <= ViewRange
                    && !Social.SocialServices.IgnoreCache.Ignoring(other, session))
                {
                    other.EnqueueMessage(ZoneMessageOpcode.ChatBase, others);
                    shown++;
                }
            log.Info($"{session.CharacterName} plays dead{(target != 0 ? $" with 0x{target:X}" : "")} (line shown to {shown} player(s)).");
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
        /// Every caller here sends an emote line (/playdead, the /sit relay, a Story Node's "Announce Emotes in Chat"), so
        /// <paramref name="emote"/> defaults to true.
        /// </summary>
        private static byte[] FromStringId(ulong speaker, uint text, ulong target = 0, bool emote = true)
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
        /// Plays an AnimationSelection on a player for everyone nearby, the player included (AbilityPlayAnim, Abilities
        /// 0x27 sub 0x12: header u16 sub, u8 flag, u32 time; u64 entity, u32 selection, facing point x, y, z, w). The
        /// facing point defaults to the player's own position, so they don't turn.
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
        {
            if (ClearsSit(selection))
            {
                ShowPose(session, selection, PlayerManager.InZone(session.ZoneId));
                return;
            }
            byte[] body = AnimBody(session, selection, facing);
            // The player's own client gets no facing point: the facing turns the character, and on its own screen that
            // turned the player (and the camera) towards each swing while they were moving.
            byte[] ownBody = includeSelf && facing != null ? AnimBody(session, selection, null) : body;
            foreach (WorldSession viewer in PlayerManager.InZone(session.ZoneId)
                         .Where(p => p == session ? includeSelf : Vector3.Distance(p.Position, session.Position) <= ViewRange))
                viewer.EnqueueMessage(ZoneMessageOpcode.AbilityPlayAnim, viewer == session ? ownBody : body);
        }

        /// <summary>PlayAnim body: header, player guid, animation selection, the point to face (own position = keep facing).</summary>
        private static byte[] AnimBody(WorldSession session, uint selection, Vector3? facing)
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
