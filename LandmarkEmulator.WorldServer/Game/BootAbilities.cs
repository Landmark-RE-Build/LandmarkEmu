using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Boot movement abilities (TASKS 4.15 Roll Wraps, lead L26; trace/0695/cosmetics-695.md §3). Data only: proven from
    /// the 0.695 client tables (the game install folder's Resources\); not wired into the cast path yet (hand-off below).
    ///
    /// A boot's item record carries one value in its u59 list (client definition +0x1d0, FUN_142c34a10 at the end of
    /// FUN_142c176d0). It is a <b>DynamicInputActionSystems</b> id: every boot's value is a row there, and Roll Wraps' 90 is
    /// the one row 0.695 added (84 → state machine 84). The chain, per boot [P]:
    /// DynamicInputActionSystems.ID → STATE_MACHINE_ID → DesignBasedStateMachineDefinition.INITIAL_STATE_ID →
    /// DesignBasedStateDefinition.STATE_PROPERTIES → DesignBasedStatePropertiesDefinition (ACTION_TYPE 1, PARAM_1 = an
    /// input action group) → InputActionGroupEntries of that group: TRIGGER 17 / 3, INPUT_MODIFIER1/2 (direction keys),
    /// ACTION_TYPE 1, ACTION_ID = the ability the client casts. Every one of those rows also got CLIENT_REQUIRMENT_ID 42
    /// ("CanUseBootAbilities", Play Mode Rules bit 2) in 0.695.
    ///
    /// So wearing Roll Wraps makes the client cast 1207-1214 (one per direction, like Dash Soles' 432 / 807-813), and
    /// the server has to answer their CasterInfoRequest (instant, no cost — the client runs Energy itself, see
    /// MiscHandler's 326 case) and the casts. Hand-off (Abilities.cs / MiscHandler.cs, not edited here):
    /// <list type="bullet">
    /// <item>MiscHandler.AbilityCasterInfoRequest: answer <see cref="IsBootAbility"/> ids like 326 (today only 326 is;
    /// the others get the generic 1098 layout, 60 m range, and a name of 0).</item>
    /// <item>Abilities.HandleCast: exclude <see cref="IsBootAbility"/> ids from the "weapon held → melee swing" path, as
    /// 326 is (today Dash / Roll with a weapon in hand would swing the weapon), and answer them.</item>
    /// </list>
    /// The name / description of the roll ability are server texts (no client table names them): 577207 "Roll", 577211
    /// "Execute an acrobatic roll in any direction." [P text, I use].
    /// </summary>
    public static class BootAbilities
    {
        /// <summary>A boot item, its u59 value and the abilities its input group casts.</summary>
        public sealed record Boot(uint ItemId, string Name, uint InputSystem, uint InputGroup, uint[] Abilities);

        public static readonly Boot[] Boots =
        {
            new(1120, "Nimbus Boots", 51, 81, new uint[] { 320 }),
            new(235, "Dash Soles", 52, 82, new uint[] { 432, 807, 808, 809, 810, 811, 812, 813 }),
            new(2851, "Breeze Boots", 64, 84, new uint[] { 542 }),
            new(243, "Boots of Bounding", 65, 85, new uint[] { 321 }),
            new(244, "Leaper's Launchers", 66, 86, new uint[] { 322 }),
            new(233, "Neon Blitzing Boots", 67, 87, new uint[] { 327 }),
            new(2178, "Adventurer's Sprint Boots", 68, 88, new uint[] { 326 }),
            new(234, "Messenger's Sandals", 69, 89, new uint[] { 328 }),
            // 0.695: InputActionGroupEntries 445-452 (group 153): Shift alone 1208, with direction modifiers 1209-1214, 1207.
            new(RollWraps, "Roll Wraps", 90, 153, new uint[] { 1207, 1208, 1209, 1210, 1211, 1212, 1213, 1214 }),
        };

        public const uint RollWraps = 4406;

        /// <summary>Roll Wraps' ability name / description texts (0.695).</summary>
        public const uint RollName = 577207, RollDescription = 577211;

        private static readonly Dictionary<uint, Boot> byAbility =
            Boots.SelectMany(b => b.Abilities.Select(a => (a, b))).ToDictionary(x => x.a, x => x.b);

        /// <summary>Is this an ability a boot's input group casts?</summary>
        public static bool IsBootAbility(uint abilityId) => byAbility.ContainsKey(abilityId);

        /// <summary>The boot whose input group casts this ability, or null.</summary>
        public static Boot BootOf(uint abilityId) => byAbility.GetValueOrDefault(abilityId);
    }
}
