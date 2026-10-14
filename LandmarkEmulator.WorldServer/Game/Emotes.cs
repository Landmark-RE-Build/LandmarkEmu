using LandmarkEmulator.Shared.Network;
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

        /// <summary>Stance emotes (looping until replaced) and the "stand" selection that ends them.</summary>
        private static readonly HashSet<uint> stances = new() { 246, 224, 248, 250, 251 };
        private const uint Stand = 223;

        /// <summary>Where each player started their current stance; walking away from it ends the stance for everyone.</summary>
        private static readonly ConcurrentDictionary<ulong, Vector3> stanceAt = new();

        /// <summary>
        /// Movement from the client: half a metre from where a stance began ends it. The "stand" is sent once the player
        /// has stopped (checked every half second for up to 15 s): a selection sent while the character is still moving
        /// is ignored by the client and the stance carries on.
        /// </summary>
        public static void Moved(WorldSession session, Vector3 position)
        {
            if (!stanceAt.TryGetValue(session.PlayerGuid, out Vector3 start) || Vector3.Distance(start, position) <= 0.5f
                || !stanceAt.TryRemove(session.PlayerGuid, out _))
                return;
            log.Debug($"{session.CharacterName} walked out of a stance.");
            _ = StandWhenStopped(session);
        }

        private static async System.Threading.Tasks.Task StandWhenStopped(WorldSession session)
        {
            Vector3 last = session.Position;
            for (int i = 0; i < 30; i++)
            {
                await System.Threading.Tasks.Task.Delay(500);
                if (session.Disconnected || stanceAt.ContainsKey(session.PlayerGuid))
                    return;                                    // gone, or started another stance
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
            var names = new List<string> { "emotelist", "emote", "duel", "duelaccept", "dueldecline", "duelleave" };
            names.AddRange(emotes.Keys);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(0x44);
            writer.Write((uint)names.Count);
            foreach (string name in names)
                writer.Write(name);
            writer.Write(1u);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
        }

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
            // Duels (an encounter kind; docs/encounters.md): 0.589 has no duel menu, so they start from commands.
            if (name.Equals("duel", StringComparison.OrdinalIgnoreCase))
                Encounters.Challenge(session, args);
            else if (name.Equals("duelaccept", StringComparison.OrdinalIgnoreCase) || name.Equals("dueldecline", StringComparison.OrdinalIgnoreCase))
                Encounters.Answer(session, 0, name.Equals("duelaccept", StringComparison.OrdinalIgnoreCase));
            else if (name.Equals("duelleave", StringComparison.OrdinalIgnoreCase))
                Encounters.Leave(session, "left");
            else if (name.Equals("emotelist", StringComparison.OrdinalIgnoreCase))
                Chat.System(session, "Emotes: /" + string.Join(", /", emotes.Keys) + ". Also /emote 0-21 for every emote clip.");
            else if (name.Equals("emote", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(args, out int index) && index >= 0 && index < emoteByIndex.Length && emoteByIndex[index] != 0)
                    PlayAnim(session, emoteByIndex[index]);
                else
                    Chat.System(session, "Usage: /emote 0-21");
            }
            else if (emotes.TryGetValue(name, out uint selection))
                PlayAnim(session, selection);
        }

        /// <summary>How far away other players see an emote, metres.</summary>
        private const float ViewRange = 80f;

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
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(4);
            w.Write(session.PlayerGuid);
            w.Write(text);
            w.Write(false);
            w.Write(false);
            w.Write(false);
            w.Write(0u);
            w.Write(0ul);
            w.Write(0ul);
            w.Write(0u);
            byte[] packet = output.ToArray();
            int shown = 0;
            foreach (WorldSession other in PlayerManager.InZone(session.ZoneId))
                if (other != session && Vector3.Distance(other.Position, session.Position) <= ViewRange && !Ignores.Ignoring(other, session))
                {
                    other.EnqueueMessage(ZoneMessageOpcode.ChatBase, packet);
                    shown++;
                }
            log.Info($"{session.CharacterName}: emote text {text} shown to {shown} player(s).");
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
            byte[] body = AnimBody(session, selection, facing);
            if (stances.Contains(selection))
                stanceAt[session.PlayerGuid] = session.Position;
            else if (selection == Stand)
                stanceAt.TryRemove(session.PlayerGuid, out _);
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
