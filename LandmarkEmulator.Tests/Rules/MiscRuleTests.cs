using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Smaller rules with clear expected outcomes: unlock kinds, storage expansions (the Lumen Station's hidden unlocks),
    /// the chest no-trade rule, movement-ability costs and cooldowns, and the swimming relay's rounding.
    /// </summary>
    public class MiscRuleTests
    {
        // ---- Unlock kinds ---------------------------------------------------------------------------------------------

        private static Dictionary<string, uint> UnlockKinds()
            => typeof(Unlocks).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(uint))
                .ToDictionary(f => f.Name, f => (uint)f.GetRawConstantValue()!);

        /// <summary>Every (kind, value) row of character_unlock means one thing: no two kinds share a number (32 / 33 did).</summary>
        [Fact]
        public void UnlockKinds_AreUnique()
        {
            var clashes = UnlockKinds().GroupBy(k => k.Value).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(k => k.Key))}");
            Assert.Empty(clashes);
        }

        /// <summary>The services that read character_unlock themselves use the world's numbers.</summary>
        [Theory]
        [InlineData("LandmarkEmulator.CommerceServer/CommerceStore.cs", "StudioDesignKind", nameof(Unlocks.StudioDesign))]
        [InlineData("LandmarkEmulator.WorldChannel/VoteStore.cs", "ClaimVoteKind", nameof(Unlocks.ClaimVote))]
        public void ServiceCopiesOfUnlockKinds_MatchTheWorld(string file, string constant, string unlock)
        {
            Match m = Regex.Match(Repo.Read(file.Split('/')), $@"const uint {constant} = (\d+);");
            Assert.True(m.Success, $"{constant} not found in {file}");
            Assert.Equal(UnlockKinds()[unlock], uint.Parse(m.Groups[1].Value));
        }

        /// <summary>No code passes a bare number as an unlock kind (it would bypass the uniqueness above).</summary>
        [Fact]
        public void UnlockCalls_UseTheNamedKinds()
        {
            // The kind is the second argument (after the session or character id).
            var bare = new Regex(@"Unlocks\.(Has|Add|Set|Values|ValuesOfAnyone|Replace|Remove|Clear)\(\s*[\w.]+\s*,\s*\d+u?\s*[,)]");
            var hits = Directory.EnumerateFiles(Repo.PathOf("LandmarkEmulator.WorldServer"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
                .Where(x => bare.IsMatch(x.line))
                .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}");
            Assert.Empty(hits);
        }

        // ---- Storage expansions (the hidden Lumen Station unlocks) -------------------------------------------------------

        private static void SetUnlocks(ulong character, params (uint Kind, uint Value)[] rows)
        {
            var cache = (ConcurrentDictionary<ulong, HashSet<(uint Kind, uint Value)>>)Private.Field(typeof(Unlocks), "cache");
            cache[character] = rows.ToHashSet();
        }

        [Theory]
        [InlineData(3711u, Unlocks.BagSize, 0u, false, 3)]            // a new character: 40 → 100 by 20
        [InlineData(3711u, Unlocks.BagSize, 80u, false, 1)]
        [InlineData(3711u, Unlocks.BagSize, 100u, true, 0)]
        [InlineData(3710u, Unlocks.VaultSize, 0u, false, 3)]          // 60 → 120 by 20
        [InlineData(3710u, Unlocks.VaultSize, 120u, true, 0)]
        [InlineData(3712u, Unlocks.DesignStorage, 0u, false, 7)]      // 6 M → 48 M by 6 M (MAX_QUEUE 7)
        [InlineData(3712u, Unlocks.DesignStorage, 48_000_000u, true, 0)]
        public void Expansions_FullAndUsesLeft(uint item, uint kind, uint size, bool full, int usesLeft)
        {
            using var world = new FakeWorld();
            ulong guid = 0x9900 + item + size;
            WorldSession s = world.Session(guid, "Expander", FakeWorld.Item(3253));
            SetUnlocks(guid, size == 0 ? Array.Empty<(uint, uint)>() : new[] { (kind, size) });
            Assert.Equal(full, ItemUses.ExpansionFull(s, item));
            Assert.Equal(usesLeft, ItemUses.ExpansionUsesLeft(s, item));
        }

        [Fact]
        public void Expansions_HeldCopiesCountAgainstTheUsesLeft_ButNotAgainstFull()
        {
            using var world = new FakeWorld();
            WorldSession s = world.Session(0x9A00, "Expander", FakeWorld.Item(3253), FakeWorld.Item(3711, 1, slot: 1),
                FakeWorld.Item(3711, 1, slot: 2));
            SetUnlocks(0x9A00, (Unlocks.BagSize, 80));
            Assert.Equal(0, ItemUses.ExpansionUsesLeft(s, 3711));
            Assert.Equal(false, ItemUses.ExpansionFull(s, 3711));
            Assert.Null(ItemUses.ExpansionFull(s, 3253));
        }

        /// <summary>
        /// The Lumen Station's one-time unlocks the character already owns are hidden (RecipeBook.HiddenUnlocks): a recipe
        /// item whose recipes are all known, or a storage expansion at its most. An unused copy in the bag does not hide
        /// it, and an unlock that teaches nothing the server can tell is never hidden.
        /// </summary>
        [Fact]
        public void HiddenUnlocks_AreTheOnesAlreadyOwned()
        {
            using var world = new FakeWorld();
            const uint KnownItem = 5001, KnownRecipe = 7001, NewItem = 5002, NewRecipe = 7002, Unknown = 5003;
            FieldInfo byItem = typeof(RecipeBook).GetField("recipesByItem", BindingFlags.NonPublic | BindingFlags.Static)!;
            FieldInfo unlocks = typeof(RecipeBook).GetField("unlockRecipes", BindingFlags.NonPublic | BindingFlags.Static)!;
            object savedByItem = byItem.GetValue(null), savedUnlocks = unlocks.GetValue(null);
            try
            {
                byItem.SetValue(null, new Dictionary<uint, uint[]> { [KnownItem] = new[] { KnownRecipe }, [NewItem] = new[] { NewRecipe } });
                var entries = new[]
                {
                    new LandmarkEmulator.Shared.GameTable.Model.RecipeEntry { Id = 9001, ItemDefinitionId = KnownItem, VisibilityRequirementId = 1 },
                    new LandmarkEmulator.Shared.GameTable.Model.RecipeEntry { Id = 9002, ItemDefinitionId = NewItem, VisibilityRequirementId = 1 },
                    new LandmarkEmulator.Shared.GameTable.Model.RecipeEntry { Id = 9003, ItemDefinitionId = 3711, VisibilityRequirementId = 1 },
                    new LandmarkEmulator.Shared.GameTable.Model.RecipeEntry { Id = 9004, ItemDefinitionId = Unknown, VisibilityRequirementId = 1 },
                };
                unlocks.SetValue(null, entries);
                WorldSession s = world.Session(0x9B00, "Crafter", FakeWorld.Item(3253), FakeWorld.Item(NewItem, 1, slot: 1));
                s.LearnedRecipes.Add(KnownRecipe);
                SetUnlocks(0x9B00, (Unlocks.BagSize, 80));
                Assert.Equal(new[] { 9001u }, RecipeBook.HiddenUnlocks(s).OrderBy(x => x).ToArray());
                SetUnlocks(0x9B00, (Unlocks.BagSize, 100));
                Assert.Equal(new[] { 9001u, 9003u }, RecipeBook.HiddenUnlocks(s).OrderBy(x => x).ToArray());
            }
            finally
            {
                byItem.SetValue(null, savedByItem);
                unlocks.SetValue(null, savedUnlocks);
            }
        }

        // ---- Chests: non-tradeable items --------------------------------------------------------------------------------

        [Theory]
        [InlineData(false, 0x1ul, 0x2ul, 0x3ul, true)]     // tradeable: anyone with access
        [InlineData(true, 0x1ul, 0x2ul, 0x1ul, true)]      // the depositor
        [InlineData(true, 0x1ul, 0x2ul, 0x2ul, false)]     // the chest's owner, not the depositor (client reason 0x21)
        [InlineData(true, 0x1ul, 0x2ul, 0x3ul, false)]     // a visitor
        [InlineData(true, 0x0ul, 0x2ul, 0x2ul, true)]      // an old row without a depositor: the chest's owner
        [InlineData(true, 0x0ul, 0x2ul, 0x3ul, false)]
        public void Chest_NoTradeItems_ComeOutOnlyForTheirDepositor(bool noTrade, ulong depositor, ulong owner, ulong taker, bool may)
            => Assert.Equal(may, Chests.MayWithdraw(noTrade, depositor, owner, taker));

        // ---- Movement abilities: costs and cooldowns (14 Oct 2014 Heroic Movement post) ---------------------------------

        [Theory]
        [InlineData(320u, 50u, 5f, 0f, false)]     // Cloud Burst
        [InlineData(321u, 100u, 5f, 0f, false)]    // Charged Leap
        [InlineData(322u, 80u, 10f, 0f, false)]    // Explosive Leap
        [InlineData(327u, 75u, 10f, 0.5f, false)]  // Electric Charge
        [InlineData(328u, 40u, 10f, 0f, true)]     // Immediate Delivery, 40 a second
        [InlineData(542u, 0u, 0f, 0f, true)]       // Endless Breeze
        [InlineData(326u, 0u, 0f, 0f, true)]       // Adventurer's Sprint (Shift)
        public void MovementAbility_CostCooldownAndKind(uint ability, uint energy, float cooldown, float castTime, bool channel)
        {
            using var world = new FakeWorld(c => c.MovementEffects = true);
            var reply = new AbilityCasterInfoReply { AbilityId = ability };
            uint defaultKind = reply.KindHash;
            MovementEffects.Describe(reply);
            Assert.NotEmpty(reply.ExecutedEffects);
            if (energy > 0)
            {
                Assert.Equal(CharacterResources.EnergyType, reply.CostResource);
                Assert.Equal(energy, reply.CostAmount);
            }
            else
                Assert.Equal(0u, reply.CostAmount);
            Assert.Equal(cooldown, reply.U70);
            Assert.Equal(cooldown > 0 ? ability : 0u, reply.Ua0);   // its own cooldown group
            Assert.Equal(castTime, reply.CastTime);
            uint expectedKind = channel ? MovementEffects.ChannelledKind
                : ability is 321 or 322 ? LandmarkEmulator.WorldServer.Game.Combat.WeaponCombat.ChargedKind : defaultKind;
            Assert.Equal(expectedKind, reply.KindHash);
        }

        [Fact]
        public void MovementEffectsOff_DescribeNothing()
        {
            using var world = new FakeWorld(c => c.MovementEffects = false);
            var reply = new AbilityCasterInfoReply { AbilityId = 320 };
            MovementEffects.Describe(reply);
            Assert.Empty(reply.ExecutedEffects);
            Assert.Equal(0u, reply.CostAmount);
        }

        /// <summary>Each effect element is 14 × 4 bytes (reader FUN_142e03a00: u32, type hash, u32, 9 × f32, u32, u32) [P].</summary>
        [Fact]
        public void MovementEffect_ElementLayout()
        {
            var effect = new MovementEffects.Effect(MovementEffects.Jump, 205f, 25f);
            byte[] bytes = Wire.Write(w => effect.Write(w));
            Assert.Equal(new Layout().U32(0).U32(MovementEffects.Jump).U32(0).F32(205f).F32(25f)
                .F32(0).F32(0).F32(0).F32(0).F32(0).F32(0).F32(0).U32(0).U32(0).ToArray(), bytes);
        }

        // ---- Swimming relay: rounding (HeroicMovement.Quantise) ---------------------------------------------------------

        private static byte[] Swim(byte flags, float depth, float? surface = null, float? second = null)
        {
            var l = new Layout().U8(flags).F32(depth);
            if (surface != null)
                l.F32(surface.Value).F32(second ?? 0f);
            return l.ToArray();
        }

        private static byte[] Quantise(byte[] body) => Private.Call<byte[]>(typeof(HeroicMovement), "Quantise", body);

        [Theory]
        [InlineData(0.5f, 0.5f)]
        [InlineData(0.51f, 0.515625f)]        // 33 / 64
        [InlineData(0.001f, 0.015625f)]       // a non-zero depth never rounds to 0 ("out of water")
        [InlineData(-0.001f, -0.015625f)]
        [InlineData(0f, 0f)]
        public void Depth_RoundsTo64ths_NeverToZero(float depth, float expected)
        {
            byte[] q = Quantise(Swim(0x01, depth));
            Assert.Equal(expected, BitConverter.ToSingle(q, 1));
            Assert.Equal(0x01, q[0]);
        }

        [Fact]
        public void Heights_RoundTo5cm_OnlyUnderTheSurface()
        {
            byte[] under = Quantise(Swim(0x40, 0.5f, 12.34f, 0.01f));
            Assert.Equal(12.35f, BitConverter.ToSingle(under, 5), 1e-5f);
            Assert.Equal(0.05f, BitConverter.ToSingle(under, 9), 1e-6f);    // never 0: "no second height"
            byte[] surface = Swim(0x00, 0.5f, 12.34f, 0.01f);
            byte[] q = Quantise(surface);
            Assert.Equal(12.34f, BitConverter.ToSingle(q, 5));               // flag 0x40 off: left alone
        }

        [Fact]
        public void NonFiniteValues_AreLeftAlone_AndTheInputIsNotChanged()
        {
            byte[] body = Swim(0x40, float.NaN, float.PositiveInfinity, 1.01f);
            byte[] copy = (byte[])body.Clone();
            byte[] q = Quantise(body);
            Assert.Equal(copy, body);
            Assert.True(float.IsNaN(BitConverter.ToSingle(q, 1)));
            Assert.True(float.IsPositiveInfinity(BitConverter.ToSingle(q, 5)));
        }
    }
}
