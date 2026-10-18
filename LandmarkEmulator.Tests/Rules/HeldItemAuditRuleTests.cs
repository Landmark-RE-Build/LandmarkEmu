using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Held items and weapons as other players see them (audit 2026-10-18): equipment changes reach the landscape that
    /// has the character, and combat-gear abilities show the others what the caster's own client is sent.
    /// </summary>
    public class HeldItemAuditRuleTests
    {
        private static ulong nextGuid = 0x7C00_0000_0000_0000;

        private static WorldSession At(FakeWorld world, float x, float z = 0f, uint zone = 406)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Held" + (guid & 0xFFFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            character.PositionX = x;
            character.PositionZ = z;
            character.ZoneId = zone;
            return s;
        }

        /// <summary>Equipment changes go to the player and the players on the same landscape only.</summary>
        [Fact]
        public void EquipmentChanges_GoToTheLandscapeOnly()
        {
            using var world = new FakeWorld();
            WorldSession actor = At(world, 0f), here = At(world, 900f), elsewhere = At(world, 0f, zone: 407);
            var audience = actor.EquipmentAudience(new[] { actor, here, elsewhere }).ToList();
            Assert.Equal(2, audience.Count);
            Assert.Contains(actor, audience);
            Assert.Contains(here, audience);
        }

        /// <summary>A gear ability shows the six-shooters' look only with a weapon of no animation class in hand.</summary>
        [Fact]
        public void GearAbility_ShowsTheGunLookOnlyWithTheGunInHand()
        {
            FieldInfo classes = typeof(WeaponAttacks).GetField("modelClasses", BindingFlags.NonPublic | BindingFlags.Static)!;
            object previous = classes.GetValue(null);
            classes.SetValue(null, new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                ["Wield_Gun1H_Gunslinger_Gunslinger_A_A5_000.adr"] = 0, ["Wield_1HBlade_Dwarf_Dwarf_C_A0_000.adr"] = 1010
            });
            try
            {
                Assert.False(Abilities.GearAttackLook(null));
                Assert.True(Abilities.GearAttackLook(new ItemDefinitionManager.ItemDefinition { Id = 3040, ModelFile = "Wield_Gun1H_Gunslinger_Gunslinger_A_A5_000.adr" }));
                Assert.False(Abilities.GearAttackLook(new ItemDefinitionManager.ItemDefinition { Id = 3764, ModelFile = "Wield_1HBlade_Dwarf_Dwarf_C_A0_000.adr" }));
            }
            finally
            {
                classes.SetValue(null, previous);
            }
        }

        /// <summary>
        /// A charged attack shows the others nothing extra at the press (BeginCharge shows the wind-up, the let-go the
        /// strike); an attack that runs at once shows its pair; a weapon with no animation class the gun look.
        /// </summary>
        [Fact]
        public void WeaponPress_ChargedAttacksWaitForTheLetGo()
        {
            var swing = new WeaponAttacks.Animation(429, 430, 400);
            Assert.Equal(Abilities.PressLook.Nothing, Abilities.LookAtPress(charged: true, swing));
            Assert.Equal(Abilities.PressLook.Pair, Abilities.LookAtPress(charged: false, swing));
            Assert.Equal(Abilities.PressLook.Gun, Abilities.LookAtPress(charged: false, null));
            Assert.Equal(Abilities.PressLook.Gun, Abilities.LookAtPress(charged: true, null));
        }
    }
}
