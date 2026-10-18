using System;
using System.Collections.Generic;
using System.IO;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The live login capture (Resources/PacketDumps/SendSelf.bin, character "Freldam", profile 46) decodes to the values
    /// the named SendSelf records document (0.695 readers FUN_142c25b90, FUN_142c1ac40, FUN_142c1b810 / FUN_142c2e640,
    /// FUN_142c3e380), and each of those blocks re-encodes to the captured bytes.
    /// </summary>
    public class SendSelfNamedRecordTests
    {
        private static SendSelfToClient ReadCapture(out byte[] body)
        {
            byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "SendSelf.bin"));
            body = file[2..];   // the u16 opcode first
            var message = new SendSelfToClient();
            message.ReadCapture(new GamePacketReader(body));
            return message;
        }

        private static byte[] Write(Action<GamePacketWriter> write)
        {
            var bytes = new List<byte>();
            write(new GamePacketWriter(bytes));
            return bytes.ToArray();
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j])
                    j++;
                if (j == needle.Length)
                    return true;
            }
            return false;
        }

        [Fact]
        public void NamedRecords_MatchTheCapture()
        {
            SendSelfToClient m = ReadCapture(out _);

            Assert.Equal(0u, m.Identity.Gender);
            Assert.Equal(0u, m.Identity.Honor);
            Assert.Equal("Freldam", m.Identity.FirstName);

            SendSelfToClient.Profile p = Assert.Single(m.Profiles);
            Assert.Equal(46u, p.ProfileId);
            Assert.Equal(571666u, p.NameId.Id);
            Assert.Equal(3u, p.ProfileType);
            Assert.Equal(1, p.FactionId);
            Assert.False(p.NoJump);
            Assert.False(p.NoStrafe);
            Assert.Equal(2.8f, p.StandCameraHeight);
            Assert.Equal(1.4f, p.CrouchCameraHeight);
            Assert.Equal(2.75f, p.LineOfSightHeight);
            Assert.Equal(1u, p.UiModelCameraId);
            Assert.Equal(0.4f, p.CapsuleRadius);
            Assert.Equal(15f, p.Mass);
            Assert.Equal((3u, 3u, 3u), (p.CharSize, p.Feet, p.Armor));
            Assert.Empty(p.ItemClasses);

            Assert.Empty(m.Collections);
            Assert.Empty(m.Buffs);
            Assert.Empty(m.Acquaintances);

            SendSelfToClient.RecipeData.PlayerRecipeData cooldown = Assert.Single(m.Recipes.Cooldowns);
            Assert.Equal((2389u, 2389u, 1487449669ul), (cooldown.RecipeKey, cooldown.RecipeId, cooldown.CooldownStart));
            Assert.Equal(2363, m.Recipes.RecipeList.Count);
            SendSelfToClient.RecipeData.Recipe hewersPick = m.Recipes.RecipeList[0];
            Assert.Equal(17u, hewersPick.RecipeId);
            Assert.Equal(3u, hewersPick.RecipeType);
            Assert.Equal(20100u, hewersPick.SortOrder);
            Assert.Equal(0.25f, hewersPick.CastingTime);
            Assert.Equal(1u, hewersPick.StackSize);
            Assert.Equal(146u, hewersPick.TrackerAchievementId);
            Assert.Equal(new List<uint> { 2 }, hewersPick.Categories);
            Assert.Equal(1u, hewersPick.Components[0].ComponentType);

            SendSelfToClient.Loadout loadout = Assert.Single(m.Loadouts);
            Assert.Equal(1u, loadout.LoadoutId);
            Assert.Equal(46u, loadout.Record.ProfileId);
        }

        [Fact]
        public void NamedRecords_ReencodeToTheCapturedBytes()
        {
            SendSelfToClient m = ReadCapture(out byte[] body);
            var blocks = new Dictionary<string, byte[]>
            {
                ["identity"] = Write(w => m.Identity.Write(w)),
                ["profiles"] = Write(w =>
                {
                    w.Write((uint)m.Profiles.Count);
                    foreach (SendSelfToClient.Profile profile in m.Profiles)
                        profile.Write(w);
                    w.Write(m.CurrentProfile);
                }),
                ["recipes"] = Write(w => m.Recipes.Write(w)),
                ["loadouts"] = Write(w =>
                {
                    w.Write((uint)m.Loadouts.Count);
                    foreach (SendSelfToClient.Loadout loadout in m.Loadouts)
                        loadout.Write(w);
                }),
            };
            foreach ((string name, byte[] bytes) in blocks)
                Assert.True(Contains(body, bytes), $"{name}: the re-encoded block is not in the capture");
        }
    }
}
