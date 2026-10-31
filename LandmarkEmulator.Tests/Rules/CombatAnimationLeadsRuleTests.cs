using LandmarkEmulator.WorldServer.Game;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Animation choices closed in Phase E part 2 (trace/0695/phaseE2-combat.md): the Dash Soles / Roll Wraps evade
    /// selections by direction (L168), the drink / throw / revive clips (L192 (f) (h)), the sword's basic combo without the
    /// dash attack (L194).
    /// </summary>
    public class CombatAnimationLeadsRuleTests
    {
        /// <summary>
        /// Each cast id's direction comes from its input group's modifiers (1 forward, 2 back, 7 left, 8 right; none = back);
        /// the selection from the network's AbilityIndex order Front, Right, Left, Back, FR, FL, BR, BL.
        /// </summary>
        [Theory]
        [InlineData(1211u, 415u)]   // Roll Wraps forward → Evade_Front (AbilityIndex 0)
        [InlineData(1207u, 465u)]   // right → Evade_Right (1)
        [InlineData(1214u, 466u)]   // left → Evade_Left (2)
        [InlineData(1208u, 467u)]   // Shift alone / back → Evade_Back (3)
        [InlineData(1213u, 474u)]   // forward + right (4)
        [InlineData(1209u, 477u)]   // back + left (7)
        [InlineData(432u, 593u)]    // Dash Soles forward → Teleport_Front (AbilityIndex 20)
        [InlineData(813u, 596u)]    // right (21)
        [InlineData(812u, 595u)]    // left (22)
        [InlineData(809u, 594u)]    // back (23)
        [InlineData(807u, 598u)]    // forward + left (25)
        [InlineData(811u, 599u)]    // back + right (26)
        public void EvadeSelection_FollowsTheCastsDirection(uint ability, uint selection)
            => Assert.Equal(selection, BootAbilities.EvadeSelection(ability));

        [Fact]
        public void EvadeSelection_IsNoneForOtherCasts()
        {
            Assert.Equal(0u, BootAbilities.EvadeSelection(326));   // sprint
            Assert.Equal(0u, BootAbilities.EvadeSelection(320));   // Cloud Burst
            Assert.Equal(0u, BootAbilities.EvadeSelection(1018));  // a pick swing
        }

        [Fact]
        public void Clips_AreTheNetworksOwn()
        {
            Assert.Equal(287u, Consumables.UsePotionAnimation);        // Emote_Index 22 → Biped_Core_Utility_UsePotion_01
            Assert.Equal(41u, Consumables.ThrowEnterAnimation);        // Class 7 Enter → Biped_Core_Harvesting_Throw_F01 (42 Release)
            Assert.Equal(840u, Health.ReviveAllyAnimation);            // AbilityIndex 1, Interacting 1 → ReviveAlly
            Assert.Equal(841u, Health.ReviveDoneAnimation);            // Interacting 0
            Assert.Equal(837u, Health.ReviveCancelAnimation);          // Cancel, Interacting 0
        }

        /// <summary>The SwordUser class (1010) keeps its dash attack (AttackIndex 1) out of the basic combo; others keep 1.</summary>
        [Fact]
        public void SwordBasicCombo_SkipsTheDashAttack()
        {
            MethodInfo isDash = typeof(WeaponAttacks).GetMethod("IsDashIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.True((bool)isDash.Invoke(null, new object[] { 1010u, 1u })!);
            Assert.False((bool)isDash.Invoke(null, new object[] { 1010u, 2u })!);
            Assert.False((bool)isDash.Invoke(null, new object[] { 1013u, 1u })!);   // SwordAndShield: index 1 is Attack_L01
        }
    }
}
