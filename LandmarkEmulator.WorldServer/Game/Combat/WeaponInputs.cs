using LandmarkEmulator.WorldServer.Game.Inventory;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// Which cast id each mouse button sends with a given weapon, read from the client's own input chain [P, 0.695 tables].
    /// <para>
    /// <b>The chain.</b> A weapon's item record lists DynamicInputActionSystems ids (u59,
    /// <see cref="ItemDefinitionManager.ItemDefinition.InputSystems"/>). Each names a state machine (DesignBasedStateMachineDefinition: its initial state); a state's properties
    /// (DesignBasedStatePropertiesDefinition, ACTION_TYPE 1) switch on an input action group (PARAM_1; START_DELAY /
    /// STATE_DURATION: when a follow-up state opens and how long it stays; PARAM_2 1 on every follow-up state, read as
    /// "on top of the state before" [I]), and the group's InputActionGroupEntries say what each button casts: TRIGGER_ID
    /// 9 = Primary (InputProfile_Default.xml action "Primary", Mouse_0), 10 = Secondary (Mouse_1), 11 = Tertiary;
    /// ACTION_TYPE 1 = cast ACTION_ID. A state moves on (DesignBasedStateMachineMap → StateToStateTransitionMap →
    /// TransitionGroupRuleMap → TransitionRuleDefinition type 1: PARAM_1 = the entry that fired) when its entry is used,
    /// which is how the second left click casts the combo id (e.g. Staff of Frozen Sorrow 1043, then 1045 while state
    /// 117 is open: from 0.4 s, for 2.25 s) and how a Power Attack chains (Electrified Assault Axe 761 → 1181 → 1182,
    /// Rolling Thunder's three leaps).
    /// </para>
    /// <para>
    /// <b>Hold [P, ClientInputActionManager GiveTime FUN_1434783f0 / UpdateCastState FUN_1434793b0].</b> Every weapon
    /// entry has TRIGGER_ON 2 with no minimum time: the cast is sent the moment the button goes down. What the hold then
    /// does is the caster info's kind: the charged kind (<see cref="WeaponCombat.ChargedKind"/>) keeps charging while the
    /// button stays down and releases on key up (FinishChargedCast); the channelled kind
    /// (<see cref="MovementEffects.ChannelledKind"/>) keeps the cast open while held and sends CancelCast on key up; any
    /// other kind is one instant cast. So the server chooses hold behaviour per cast id, the client chooses the id.
    /// </para>
    /// <para>
    /// <b>E and Q.</b> The 0.695 profile has the Tertiary (E) action commented out (InputProfile_Default.xml lines 156-158,
    /// bound in 0.589), and no launch weapon's group has a TRIGGER_ID 11 entry in either build: only the 2015 items
    /// (1291, 1293, 1296, 1503, 1517, 1528, 1529) and the four never-live weapons (3028, 3029, 3038, 3039) do. Q is the
    /// grappling hook (Generic set "FireNewGrappleHook"), never a weapon input.
    /// </para>
    /// The live server's ability data was server-side; this class only reads what the client itself does with the
    /// buttons, so the server can describe and answer exactly the casts the held weapon can send.
    /// </summary>
    public static class WeaponInputs
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The input action a weapon entry is triggered by (RefInputProfileActions / InputActionGroupEntries TRIGGER_ID).</summary>
        public enum Button : uint { Primary = 9, Secondary = 10, Tertiary = 11 }

        /// <summary>
        /// One cast a weapon's buttons can send: the button, the cast id, its place in that button's chain (0 = the first
        /// press, 1 = the press after it while the follow-up state is open, …), the follow-up state's opening delay and
        /// length (0 for step 0), and the entry's tooltip order.
        /// </summary>
        public sealed record Slot(Button Button, uint CastId, int Step, float WindowStart, float WindowLength, uint DisplayOrder);

        private static Dictionary<uint, IReadOnlyList<Slot>> bySystem;
        private static readonly Dictionary<uint, IReadOnlyList<Slot>> byItem = new();
        private static Dictionary<uint, uint> ownerOfCast;
        private static readonly object sync = new();

        /// <summary>The casts a weapon's buttons send (empty when its record names no input system or the tables are missing).</summary>
        public static IReadOnlyList<Slot> Of(ItemDefinitionManager.ItemDefinition weapon)
        {
            if (weapon == null || weapon.InputSystems.Count == 0)
                return Array.Empty<Slot>();
            lock (sync)
            {
                if (byItem.TryGetValue(weapon.Id, out var cached))
                    return cached;
                bySystem ??= Load();
                var list = weapon.InputSystems.SelectMany(s => bySystem.GetValueOrDefault(s) ?? Array.Empty<Slot>()).ToList();
                byItem[weapon.Id] = list;
                return list;
            }
        }

        /// <summary>The cast ids a weapon's buttons send.</summary>
        public static IEnumerable<uint> CastIdsOf(ItemDefinitionManager.ItemDefinition weapon) => Of(weapon).Select(s => s.CastId).Distinct();

        /// <summary>This weapon's slot for a cast id, or null when its buttons never send it.</summary>
        public static Slot SlotOf(ItemDefinitionManager.ItemDefinition weapon, uint castId) => Of(weapon).FirstOrDefault(s => s.CastId == castId);

        /// <summary>The cast id a button sends at a step of its chain with this weapon (0 = none).</summary>
        public static uint CastOf(ItemDefinitionManager.ItemDefinition weapon, Button button, int step = 0)
            => Of(weapon).FirstOrDefault(s => s.Button == button && s.Step == step)?.CastId ?? 0;

        /// <summary>
        /// The weapon whose buttons send this cast id (the first item in the definitions that has it), or null. Used to
        /// describe a cast id the client asks about while another weapon is held (a weapon's tooltip in the bag).
        /// </summary>
        public static ItemDefinitionManager.ItemDefinition OwnerOf(uint castId)
        {
            Dictionary<uint, uint> map;
            lock (sync)
                map = ownerOfCast;
            if (map == null)
            {
                map = new Dictionary<uint, uint>();
                foreach (var item in ItemDefinitionManager.All.Where(d => d.InputSystems.Count > 0).OrderBy(d => d.Id))
                    foreach (Slot s in Of(item))
                        map.TryAdd(s.CastId, item.Id);
                lock (sync)
                    ownerOfCast = map;
            }
            return map.TryGetValue(castId, out uint id) ? ItemDefinitionManager.Get(id) : null;
        }

        /// <summary>Drops the cache (tests, a reload of the game data).</summary>
        public static void Reset()
        {
            lock (sync)
            {
                bySystem = null;
                byItem.Clear();
                ownerOfCast = null;
            }
        }

        /// <summary>Walks every input system's state machine once: system id → the slots it gives.</summary>
        private static Dictionary<uint, IReadOnlyList<Slot>> Load()
        {
            var systems = GameDataFiles.Rows("DynamicInputActionSystems.txt").ToDictionary(r => r.U(0), r => r.U(1));
            var initial = GameDataFiles.Rows("DesignBasedStateMachineDefinition.txt").ToDictionary(r => r.U(0), r => r.U(1));
            var stateProps = GameDataFiles.Rows("DesignBasedStateDefinition.txt").ToDictionary(r => r.U(0), r => r.U(1));
            var props = GameDataFiles.Rows("DesignBasedStatePropertiesDefinition.txt").ToDictionary(r => r.U(0),
                r => (Action: r.U(1), Group: r.U(2), Start: r.F(4), Length: r.F(5)));
            var transitionsOf = GameDataFiles.Rows("DesignBasedStateMachineMap.txt").GroupBy(r => r.U(1))
                .ToDictionary(g => g.Key, g => g.Select(r => r.U(2)).ToList());
            var transitions = GameDataFiles.Rows("DesignBasedStateToStateTransitionMap.txt").ToDictionary(r => r.U(0),
                r => (From: r.U(1), Group: r.U(2), To: r.U(3)));
            var groupRules = GameDataFiles.Rows("DesignBasedTransitionGroupRuleMap.txt").GroupBy(r => r.U(1))
                .ToDictionary(g => g.Key, g => g.Select(r => r.U(2)).ToList());
            var rules = GameDataFiles.Rows("DesignBasedTransitionRuleDefinition.txt").ToDictionary(r => r.U(0),
                r => (Type: r.U(1), Param: r.U(2)));
            var entries = GameDataFiles.Rows("InputActionGroupEntries.txt").Select(r => (Id: r.U(0), Group: r.U(1),
                Trigger: r.U(3), Action: r.U(12), Cast: r.U(13), Order: r.U(19))).ToList();
            var entriesOf = entries.GroupBy(e => e.Group).ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<uint, IReadOnlyList<Slot>>();
            foreach ((uint system, uint machine) in systems)
            {
                if (!initial.TryGetValue(machine, out uint start))
                    continue;
                var machineTransitions = (transitionsOf.GetValueOrDefault(machine) ?? new List<uint>())
                    .Where(transitions.ContainsKey).Select(t => transitions[t]).ToList();
                var slots = new List<Slot>();
                var seen = new HashSet<uint>();
                var queue = new Queue<(uint State, int Depth)>();
                queue.Enqueue((start, 0));
                while (queue.Count > 0)
                {
                    (uint state, int depth) = queue.Dequeue();
                    if (!seen.Add(state))
                        continue;
                    if (!stateProps.TryGetValue(state, out uint propId) || !props.TryGetValue(propId, out var p) || p.Action != 1)
                        continue;
                    var stateEntries = entriesOf.GetValueOrDefault(p.Group) ?? new();
                    foreach (var e in stateEntries)
                        if (e.Action == 1 && e.Cast != 0 && Enum.IsDefined(typeof(Button), e.Trigger)
                            && !slots.Any(s => s.CastId == e.Cast))
                            slots.Add(new Slot((Button)e.Trigger, e.Cast, depth, depth == 0 ? 0f : p.Start, depth == 0 ? 0f : p.Length, e.Order));
                    // Follow-up states: reached when one of this state's entries fires (rule type 1, PARAM_1 = the entry).
                    var ids = stateEntries.Select(e => e.Id).ToHashSet();
                    foreach (var t in machineTransitions.Where(t => t.From == state))
                        if ((groupRules.GetValueOrDefault(t.Group) ?? new()).Any(r => rules.TryGetValue(r, out var rule)
                                && rule.Type == 1 && ids.Contains(rule.Param)))
                            queue.Enqueue((t.To, depth + 1));
                }
                if (slots.Count > 0)
                    result[system] = slots;
            }
            log.Info($"Weapon inputs: {result.Count} input systems with weapon casts.");
            return result;
        }
    }
}
