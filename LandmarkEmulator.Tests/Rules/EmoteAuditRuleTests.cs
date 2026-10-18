using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The emote / animation audit of 2026-10-18 (Game/Emotes.cs, docs/protocol.md "Emotes"): every emote word plays the
    /// same selection on the player's own client and on the observers' (RequestAnimationSelection, no turn), says its chat
    /// lines, and moods and walk stances are remembered for players who meet the player later.
    /// </summary>
    public class EmoteAuditRuleTests
    {
        private static ulong nextGuid = 0x7B00_0000_0000_0000;

        private static WorldSession At(FakeWorld world, float x, float z = 0f, uint zone = 406)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Emote" + (guid & 0xFFFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.PositionX = x;
            character.PositionZ = z;
            character.ZoneId = zone;
            return s;
        }

        /// <summary>The RequestAnimationSelection packets a viewer got for this subject: (selection, clear sit).</summary>
        private static (uint Selection, bool ClearSit)[] Selections(List<(ZoneMessageOpcode Opcode, byte[] Body)> sent, WorldSession subject)
            => sent.Where(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire && BitConverter.ToUInt64(p.Body, 0) == subject.PlayerGuid)
                .Select(p => (BitConverter.ToUInt32(p.Body, 8), p.Body[12] != 0)).ToArray();

        /// <summary>The ChatPacketFromStringId lines a viewer got: (text id, target).</summary>
        private static (uint Text, ulong Target)[] Lines(List<(ZoneMessageOpcode Opcode, byte[] Body)> sent)
            => sent.Where(p => p.Opcode == ZoneMessageOpcode.ChatBase && BitConverter.ToUInt16(p.Body, 0) == 4)
                .Select(p => (BitConverter.ToUInt32(p.Body, 10), BitConverter.ToUInt64(p.Body, 21))).ToArray();

        /// <summary>
        /// [P] the 0.695 player network's single-shot emote switches (re/out/l117/player_switches.txt, Emotes_SingleShot|
        /// Nuetral 11807 / 11808 / 11830 / 11842) flattened to Emote_Index → clip, and AnimationSelectionVariables'
        /// Emote_Index of each EmoteSingle row the words use.
        /// </summary>
        private static readonly string[] clipByIndex =
        {
            null, "Crafting", "Beg", "Bow", "Cheer", "Congratulate", "Cry", "Flirt", "Frustrated", "Giggle", "Go_Away", "Laugh",
            "Point", "Shrug", "Sob", "Talk_Long", "Taunt", "Wave", "No", "Yes", "Dance", "Onod",
            "UsePotion", "Call", "Cough", "Flex", "Give", "Sneeze", "Thanks", "Welcome", "Yawn", "Come_Here", "Stretch", "Hug"
        };

        private static readonly Dictionary<uint, int> emoteIndexOf = new()
        {
            [217] = 17, [222] = 10, [228] = 2, [229] = 3, [230] = 4, [231] = 6, [232] = 5, [233] = 7, [234] = 8, [235] = 9,
            [237] = 11, [238] = 12, [239] = 13, [240] = 14, [241] = 15, [242] = 16, [244] = 18, [245] = 19, [252] = 15,
            [449] = 24, [450] = 23, [451] = 25, [452] = 27, [453] = 29, [454] = 30, [455] = 31, [456] = 32, [817] = 33
        };

        [Theory]
        [InlineData("wave", "Wave")]
        [InlineData("groan", "Frustrated")]
        [InlineData("shoo", "Go_Away")]
        [InlineData("nod", "Yes")]
        [InlineData("talk", "Talk_Long")]
        [InlineData("talking", "Talk_Long")]
        [InlineData("cough", "Cough")]
        [InlineData("flex", "Flex")]
        [InlineData("sneeze", "Sneeze")]
        [InlineData("welcome", "Welcome")]
        [InlineData("yawn", "Yawn")]
        [InlineData("beckon", "Come_Here")]
        [InlineData("stretch", "Stretch")]
        [InlineData("hug", "Hug")]
        public void Gestures_PlayTheirOwnClip(string word, string clip)
            => Assert.Equal(clip, clipByIndex[emoteIndexOf[Emotes.SelectionOf(word)]]);

        /// <summary>Live's words (wiki Commands List) are registered, the client's chat-command /yell is not.</summary>
        [Fact]
        public void ZoneCommandList_HasLivesWords_NotYell()
        {
            List<string> names = Emotes.ZoneCommandNames();
            foreach (string word in new[] { "beckon", "cough", "flex", "hug", "sneeze", "welcome", "yawn", "stretch", "talking",
                         "stop", "lie", "sad", "angry", "happy", "calm", "dance", "drunk", "kneel", "playdead", "skip", "sneak", "stand" })
                Assert.Contains(word, names);
            Assert.DoesNotContain("yell", names);
            Assert.DoesNotContain("sit", names);    // the client's own command (CommandSit)
            Assert.Equal(480u, Emotes.SelectionOf("lie"));
            Assert.Equal(221u, Emotes.SelectionOf("sad"));
            Assert.True(Emotes.ClearsSit(480));
            Assert.True(Emotes.IsMood(221));
            Assert.False(Emotes.ClearsSit(221));
        }

        /// <summary>Every word but "stop" has the player's line and the others' line; the wiki's displayed messages.</summary>
        [Fact]
        public void EveryWord_HasItsChatLines()
        {
            foreach (string word in Emotes.ZoneCommandNames().Where(n => n != "emote" && n != "emotelist" && n != "stop"))
            {
                Emotes.Lines? l = Emotes.LinesOf(word);
                Assert.True(l.HasValue, word);
                Assert.NotEqual(0u, l.Value.Self);
                Assert.NotEqual(0u, l.Value.Other);
                Assert.NotEqual(0u, l.Value.OtherTarget);
            }
            Assert.Equal(559125u, Emotes.LinesOf("wave")!.Value.Self);         // "You wave."
            Assert.Equal(559356u, Emotes.LinesOf("beg")!.Value.Self);          // "You plead for assistance."
            Assert.Equal(560147u, Emotes.LinesOf("groan")!.Value.Self);        // "You groan loudly."
            Assert.Equal(559389u, Emotes.LinesOf("sad")!.Value.Self);          // "You feel sad."
            Assert.Equal(571886u, Emotes.LinesOf("lie")!.Value.Self);          // "You lie down."
            Assert.Equal(575934u, Emotes.LinesOf("hug")!.Value.Self);          // "You give a hug."
            Assert.Null(Emotes.LinesOf("stop"));
        }

        /// <summary>
        /// /wave: the player and a player 30 m away get the same RequestAnimationSelection (no clear-sit, no AbilityPlayAnim,
        /// which would turn the character); a player 200 m away gets nothing. Lines: "You wave." to the player, "[*speaker*]
        /// waves." to the near one.
        /// </summary>
        [Fact]
        public void OneShotEmote_SameSelectionOnBothScreens_AndItsLines()
        {
            using var world = new FakeWorld();
            WorldSession actor = At(world, 0f), near = At(world, 30f), far = At(world, 200f);
            Emotes.Emote(actor, "wave", new[] { actor, near, far });

            var own = FakeWorld.Outgoing(actor);
            var seen = FakeWorld.Outgoing(near);
            Assert.Equal(new[] { (217u, false) }, Selections(own, actor));
            Assert.Equal(new[] { (217u, false) }, Selections(seen, actor));
            Assert.False(own.Any(p => p.Opcode == ZoneMessageOpcode.AbilityPlayAnim));
            Assert.False(seen.Any(p => p.Opcode == ZoneMessageOpcode.AbilityPlayAnim));
            Assert.Equal(new[] { (559125u, 0ul) }, Lines(own));
            Assert.Equal(new[] { (559127u, 0ul) }, Lines(seen));
            Assert.Empty(FakeWorld.Outgoing(far));
        }

        /// <summary>With a target the "[*target*]" lines go out and carry the target's guid for the client to fill.</summary>
        [Fact]
        public void Lines_WithATarget_UseTheTargetForms()
        {
            using var world = new FakeWorld();
            WorldSession actor = At(world, 0f), near = At(world, 5f);
            actor.CurrentTarget = near.PlayerGuid;
            Emotes.Emote(actor, "bow", new[] { actor, near });
            Assert.Equal(new[] { (560136u, near.PlayerGuid) }, Lines(FakeWorld.Outgoing(actor)));   // You bow to [*target*].
            Assert.Equal(new[] { (559186u, near.PlayerGuid) }, Lines(FakeWorld.Outgoing(near)));    // [*speaker*] bows to [*target*].

            // A word with no targeted line for the player: the plain one, still with the target for the others' line.
            Emotes.Emote(actor, "shoo", new[] { actor, near });
            Assert.Equal(new[] { (560151u, near.PlayerGuid) }, Lines(FakeWorld.Outgoing(actor)));
            Assert.Equal(new[] { (559368u, near.PlayerGuid) }, Lines(FakeWorld.Outgoing(near)));
        }

        /// <summary>
        /// /angry: shown to those in range, remembered: a newcomer (full character) and a player who walks in later see it
        /// once; /calm reaches everyone who was shown the anger and forgets it.
        /// </summary>
        [Fact]
        public void Mood_IsRemembered_ForLateComers_AndCalmReachesEveryoneTold()
        {
            using var world = new FakeWorld();
            WorldSession actor = At(world, 0f), near = At(world, 10f), walker = At(world, 300f), newcomer = At(world, 500f);
            var zone = new[] { actor, near, walker, newcomer };
            Emotes.Emote(actor, "angry", zone);
            Assert.Equal(new[] { (219u, false) }, Selections(FakeWorld.Outgoing(actor), actor));
            Assert.Equal(new[] { (219u, false) }, Selections(FakeWorld.Outgoing(near), actor));
            Assert.Empty(Selections(FakeWorld.Outgoing(walker), actor));
            Assert.Equal(219u, Emotes.MoodOf(actor.PlayerGuid));

            Emotes.Moved(walker, new Vector3(50f, 0f, 0f), () => zone);
            Emotes.Moved(walker, new Vector3(49f, 0f, 0f), () => zone);
            Assert.Equal(new[] { (219u, false) }, Selections(FakeWorld.Outgoing(walker), actor));

            Emotes.IntroducePose(newcomer, actor);
            Assert.Equal(new[] { (219u, false) }, Selections(FakeWorld.Outgoing(newcomer), actor));

            // The mood stays while the player walks about (it is not a stance).
            Emotes.Moved(actor, new Vector3(20f, 0f, 0f), () => zone);
            Assert.Equal(219u, Emotes.MoodOf(actor.PlayerGuid));

            Emotes.Emote(actor, "calm", zone);
            Assert.Equal(new[] { (218u, false) }, Selections(FakeWorld.Outgoing(walker), actor));
            Assert.Equal(new[] { (218u, false) }, Selections(FakeWorld.Outgoing(newcomer), actor));
            Assert.Equal(0u, Emotes.MoodOf(actor.PlayerGuid));
            Emotes.IntroducePose(At(world, 1f), actor);
            Emotes.Forget(actor);
        }

        /// <summary>A player in a mood walks up to a player standing still: shown to them once (subject-side scan).</summary>
        [Fact]
        public void MovingSubject_ShowsItsMoodToPlayersItComesNear()
        {
            TimeSpan previous = Emotes.ScanInterval;
            Emotes.ScanInterval = TimeSpan.Zero;
            try
            {
                using var world = new FakeWorld();
                WorldSession actor = At(world, 0f), standing = At(world, 150f);
                var zone = new[] { actor, standing };
                Emotes.Emote(actor, "happy", zone);
                Assert.Empty(Selections(FakeWorld.Outgoing(standing), actor));
                Emotes.Moved(actor, new Vector3(100f, 0f, 0f), () => zone);
                Emotes.Moved(actor, new Vector3(101f, 0f, 0f), () => zone);
                Assert.Equal(new[] { (220u, false) }, Selections(FakeWorld.Outgoing(standing), actor));
                Emotes.Forget(actor);
            }
            finally
            {
                Emotes.ScanInterval = previous;
            }
        }

        /// <summary>/skip is kept while walking (Use /stand to stop skipping); /kneel ends when the player walks away.</summary>
        [Fact]
        public void WalkStances_LastWhileWalking_OthersEnd()
        {
            using var world = new FakeWorld();
            WorldSession skipper = At(world, 0f), kneeler = At(world, 0f, 50f);
            var zone = new[] { skipper, kneeler };
            Emotes.Emote(skipper, "skip", zone);
            Emotes.Emote(kneeler, "kneel", zone);
            Emotes.Moved(skipper, new Vector3(10f, 0f, 0f), () => zone);
            Emotes.Moved(kneeler, new Vector3(10f, 0f, 50f), () => zone);
            Assert.Equal(248u, Emotes.StanceOf(skipper.PlayerGuid));
            Assert.Equal(0u, Emotes.StanceOf(kneeler.PlayerGuid));
            Emotes.Forget(skipper);
            Emotes.Forget(kneeler);
        }

        /// <summary>/lie is a stance: clear-sit, remembered, shown to a newcomer with the mood.</summary>
        [Fact]
        public void Newcomer_GetsStanceAndMood()
        {
            using var world = new FakeWorld();
            WorldSession actor = At(world, 0f), newcomer = At(world, 400f);
            Emotes.Emote(actor, "sad", new[] { actor });
            Emotes.Emote(actor, "lie", new[] { actor });
            Assert.Equal(new[] { (221u, false), (480u, true) }, Selections(FakeWorld.Outgoing(actor), actor));
            Emotes.IntroducePose(newcomer, actor);
            Assert.Equal(new[] { (480u, true), (221u, false) }, Selections(FakeWorld.Outgoing(newcomer), actor));
            Emotes.Forget(actor);
            Assert.Equal(0u, Emotes.MoodOf(actor.PlayerGuid));
            Assert.Equal(0u, Emotes.StanceOf(actor.PlayerGuid));
        }

        /// <summary>
        /// A swing with a facing point: the others get AbilityPlayAnim towards the point, the player's own client the
        /// selection without a point (no turn on its own screen).
        /// </summary>
        [Fact]
        public void FacingPlay_TurnsTheCharacterOnlyForTheOthers()
        {
            using var world = new FakeWorld();
            WorldSession actor = At(world, 0f), near = At(world, 5f);
            Emotes.Play(actor, 226, new Vector3(0f, 0f, 3f), includeSelf: true, new[] { actor, near });
            var own = FakeWorld.Outgoing(actor);
            Assert.Equal(new[] { (226u, false) }, Selections(own, actor));
            Assert.False(own.Any(p => p.Opcode == ZoneMessageOpcode.AbilityPlayAnim));
            var seen = FakeWorld.Outgoing(near);
            Assert.Equal(1, seen.Count(p => p.Opcode == ZoneMessageOpcode.AbilityPlayAnim));
            Assert.Empty(Selections(seen, actor));
        }
    }
}
