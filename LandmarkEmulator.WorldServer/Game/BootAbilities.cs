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

        // ---- Dash Soles / Roll Wraps: the evade animation (OPEN-LEADS L168) --------------------------------------------
        //
        // None of the seven ClientExecutedEffect types moves the player sideways (agent-leads5.md L168), so a dash or roll
        // is an animation with root motion. What the client has [P, 0.695]:
        //  - the direction each cast id stands for: InputActionGroupEntries groups 82 (Dash Soles) and 153 (Roll Wraps),
        //    INPUT_MODIFIER1/2 = RefInputProfileActions 1 MoveForward, 2 MoveBackward, 7 MouseLookStrafeLeft, 8 …Right;
        //    no row asks for MoveBackward alone, so the entry with no modifier (809 / 1208, the one with UI_DISPLAY 1) covers
        //    Shift alone and Shift + back: the backward evade [I];
        //  - the player network's evade branch (PlayerAnimationNetwork64.mrn, Action|Action|Evade_Roll): switches on
        //    ControlParameters|AbilityIndex with thresholds read from their NodeDefs — Switch4 (8098) ≥ 20 → Blend2_1 →
        //    Translations_only (8096: 20 Teleport_Front, 21 _Right, 22 _Left, 23 _Back, 24 _Front_Right, 25 _Front_Left,
        //    26 _Back_Right, 27 _Back_Left), else Switch3 (8047) ≥ 10 → Sidestep (8343: 10-17 Step_*), else Roll (8381: 0-7
        //    Evade_*), in the same direction order;
        //  - the AnimationSelection rows that ask for them (MESSAGE Roll / Roll_2, variables AbilityIndex + Translation,
        //    the root motion's share): 415 / 465-467 / 474-477 = AbilityIndex 0-7 (Translation 0.6), 523-537 = 10-17
        //    (0.75), 593-600 = 20-27 (0.5), with more 20-27 sets (601-608, 623-630, 634-640) at 0.43 / 0.38.
        // Which row live's abilities named is server data (the caster info / CastSuccess AnimationId; the ability tables are
        // stripped) [I]: Roll Wraps ("Execute an acrobatic roll in any direction", 577211) take the Roll set, Dash Soles
        // ("Execute a sudden dash in any direction", 567463) the Translations_only set (a pure slide, no roll), each in the
        // cast's own direction. The CastSuccess carries it (+0x3c → FUN_142d5dfa0 → FUN_143cd5e90 plays it on the
        // caster); the players near see it through RequestAnimationSelection. Only while MovementEffects is on.

        /// <summary>AbilityIndex direction order of the evade switches: Front, Right, Left, Back, FR, FL, BR, BL.</summary>
        private enum Dir { Front = 0, Right = 1, Left = 2, Back = 3, FrontRight = 4, FrontLeft = 5, BackRight = 6, BackLeft = 7 }

        /// <summary>Cast id → direction, from the input groups' modifiers (see above).</summary>
        private static readonly Dictionary<uint, Dir> directionOf = new()
        {
            // Dash Soles, group 82
            [432] = Dir.Front, [807] = Dir.FrontLeft, [808] = Dir.FrontRight, [809] = Dir.Back,
            [810] = Dir.BackLeft, [811] = Dir.BackRight, [812] = Dir.Left, [813] = Dir.Right,
            // Roll Wraps, group 153
            [1211] = Dir.Front, [1212] = Dir.FrontLeft, [1213] = Dir.FrontRight, [1208] = Dir.Back,
            [1209] = Dir.BackLeft, [1210] = Dir.BackRight, [1214] = Dir.Left, [1207] = Dir.Right,
        };

        /// <summary>Evade_Roll|Roll selections by direction (AbilityIndex 0-7, Translation 0.6).</summary>
        private static readonly uint[] rollSelections = { 415, 465, 466, 467, 474, 475, 476, 477 };
        /// <summary>Translations_only selections by direction (AbilityIndex 20-27, Translation 0.5): 593 = 20 … 600 = 27.</summary>
        private static readonly uint[] dashSelections = { 593, 596, 595, 594, 597, 598, 599, 600 };

        /// <summary>
        /// The evade animation a Dash Soles / Roll Wraps cast plays (see above), or 0 for any other cast.
        /// </summary>
        public static uint EvadeSelection(uint abilityId)
        {
            if (!directionOf.TryGetValue(abilityId, out Dir dir))
                return 0;
            return BootOf(abilityId)?.ItemId == RollWraps ? rollSelections[(int)dir] : dashSelections[(int)dir];
        }
    }
}
