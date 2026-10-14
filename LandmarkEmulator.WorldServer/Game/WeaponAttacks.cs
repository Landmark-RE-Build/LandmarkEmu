using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// What a weapon cast is, and what it costs and does. The client picks the cast id from its input table
    /// (InputActionGroupEntries: ACTION_TYPE 1 = cast ACTION_ID; TRIGGER_ID 9 = Primary / left mouse, 10 = Secondary /
    /// right mouse, 11 = Special / E; DISPLAY_ON_TOOLTIP 0 = a combo follow-up of the same button, e.g. 816 after 760).
    /// The Landmark wiki's weapon pages give the design: the Basic Attack (primary) is free; the Power Attack
    /// (secondary) costs Mana (Energy, 55-74) and hits harder, chained stages more each time. Ability effects are server
    /// data the client doesn't ship (AbilityEx.txt is empty), so the multipliers and costs below are ours, set from
    /// those pages.
    /// </summary>
    public static class WeaponAttacks
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public enum Kind { Unknown, Basic, BasicCombo, Power, PowerCombo, Special }

        /// <summary>Damage multipliers and Energy costs per kind.</summary>
        private static readonly Dictionary<Kind, (double Damage, uint Energy)> rules = new()
        {
            [Kind.Unknown] = (1.0, 0),
            [Kind.Basic] = (1.0, 0),
            [Kind.BasicCombo] = (1.3, 0),
            [Kind.Power] = (1.8, 60),
            [Kind.PowerCombo] = (2.1, 70),
            [Kind.Special] = (2.4, 80)
        };

        private static Dictionary<uint, Kind> kinds;

        /// <summary>The kind of a cast id, from the client's input table.</summary>
        public static Kind KindOf(uint ability)
        {
            if (kinds == null)
            {
                var map = new Dictionary<uint, Kind>();
                foreach (string[] r in GameDataFiles.Rows("InputActionGroupEntries.txt"))
                {
                    if (r.U(12) != 1 || r.U(13) == 0)
                        continue;
                    bool follow = r.U(18) == 0;
                    Kind kind = r.U(3) switch
                    {
                        9 => follow ? Kind.BasicCombo : Kind.Basic,
                        10 => follow ? Kind.PowerCombo : Kind.Power,
                        11 => Kind.Special,
                        _ => Kind.Unknown
                    };
                    if (kind != Kind.Unknown)
                        map.TryAdd(r.U(13), kind);
                }
                kinds = map;
                log.Info($"Weapon attacks: {map.Count} cast ids classified.");
            }
            return kinds.GetValueOrDefault(ability);
        }

        // ---- Attack animations ----------------------------------------------------------------------------------------
        //
        // The live server's ability definitions (server data, not in the client) named the AnimationSelection each attack
        // plays. The client data has everything to rebuild that choice:
        //  - the held weapon's animation class: its actor definition (.adr) sets the Class enumeration on the character
        //    (gameData/WeaponAnimationClasses.txt, built from the packs by re/tools/weaponclasses.py; AnimEnums.xml names
        //    them: 1010 SwordUser, 1011 BowUser, 1012 StaffUser, 1014 TwoHandedSwordUser, 1015 DualDaggerUser, ...);
        //  - per class, AnimationSelection rows with that Class and an AttackIndex: an "Enter" (wind-up) and its
        //    "Release" (the strike), or a one-shot "Attack". Indices 0-2 are the basic combo's stages, 3 the heavy attack
        //    (often Charged), higher ones channelled / special moves;
        //  - the combo stage: how many follow-ups (combo casts, which the client picks itself) came in a row.
        // So: basic stage n plays index n (wrapping over the class's basic indices), power plays index 3 (else the
        // charged swing 11 / 13, else 10, else the last basic one), the special (E) attack index 4 (else 3, 12, 11).
        // Enter/Release pairs win over one-shot "Attack" selections, which only show from the matching stance. An
        // Enter is always followed by its Release, else the character stays in the wind-up pose.

        /// <summary>An attack animation: the Enter (or one-shot Attack) selection, its Release (0 = none) and when.</summary>
        public readonly record struct Animation(uint Enter, uint Release, int ReleaseMs);

        private static Dictionary<string, uint> modelClasses;
        private static Dictionary<(uint Class, uint Index), Animation> classAnimations;
        /// <summary>Strike delay when a selection has no AttackTime (the tool swings' 350 ms looked right).</summary>
        private const int DefaultReleaseMs = 350;

        /// <summary>The animation class the held weapon's model gives the character (0 = unknown).</summary>
        public static uint ClassOf(Inventory.ItemDefinitionManager.ItemDefinition weapon)
        {
            if (weapon?.ModelFile == null)
                return 0;
            if (modelClasses == null)
            {
                var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                foreach (string[] r in GameDataFiles.Rows("WeaponAnimationClasses.txt"))
                    if (r.Length > 1 && uint.TryParse(r[1], out uint c))
                        map[r[0]] = c;
                modelClasses = map;
            }
            return modelClasses.GetValueOrDefault(weapon.ModelFile);
        }

        /// <summary>
        /// Combo stage of a player's cast: 0 for an attack that starts a chain; a follow-up (the input table's combo
        /// entries, DISPLAY_ON_TOOLTIP 0: the client itself switches to them while the combo window is open) is one
        /// more than the player's previous stage of the same kind if that came within the window, else 1.
        /// </summary>
        public static int NextStage(WorldSession session, uint ability)
        {
            Kind kind = KindOf(ability);
            DateTime now = DateTime.UtcNow;
            lock (comboSync)
            {
                int stage = 0;
                if (kind is Kind.BasicCombo or Kind.PowerCombo)
                    stage = combos.TryGetValue(session, out var last) && (now - last.At) <= ComboWindow
                        && Base(last.Kind) == Base(kind) ? last.Stage + 1 : 1;
                combos[session] = (kind, stage, now);
                return stage;
            }
        }

        private static Kind Base(Kind k) => k switch { Kind.BasicCombo => Kind.Basic, Kind.PowerCombo => Kind.Power, _ => k };

        /// <summary>How long a combo stays open after an attack (InputActionEntryComboMap COMBO_ACTIVATION_DURATION 1.4 s, plus slack).</summary>
        private static readonly TimeSpan ComboWindow = TimeSpan.FromSeconds(2);
        private static readonly object comboSync = new();
        private static readonly Dictionary<WorldSession, (Kind Kind, int Stage, DateTime At)> combos = new();

        private static void LoadClassAnimations()
        {
            var message = new Dictionary<uint, string>();
            foreach (string[] r in GameDataFiles.Rows("AnimationSelection.txt"))
                if (r.Length > 1)
                    message[r.U(0)] = r[1];
            var vars = new Dictionary<uint, Dictionary<string, string>>();
            foreach (string[] r in GameDataFiles.Rows("AnimationSelectionVariables.txt"))
            {
                // Rows naming an input action are overrides while that input is held (e.g. moving): not the base pose.
                if (r.Length < 4 || (r.Length > 4 && r[4].Length > 0))
                    continue;
                if (!vars.TryGetValue(r.U(1), out var v))
                    vars[r.U(1)] = v = new Dictionary<string, string>();
                v[r[2]] = r[3];
            }
            var enters = new Dictionary<(uint, uint), uint>();
            var releases = new Dictionary<(uint, uint), uint>();
            var oneShots = new Dictionary<(uint, uint), uint>();
            var times = new Dictionary<uint, float>();
            foreach ((uint id, var v) in vars.OrderBy(x => x.Key))
            {
                if (!v.TryGetValue("Class", out string cs) || !uint.TryParse(cs, out uint cls)
                    || !v.TryGetValue("AttackIndex", out string xs) || !uint.TryParse(xs, out uint index))
                    continue;
                // Channelled holds and alternate playback speeds are other moves of the same index.
                if (v.GetValueOrDefault("Channeled") == "1" || v.ContainsKey("PlayBackSpeed"))
                    continue;
                if (v.TryGetValue("AttackTime", out string ts) && float.TryParse(ts, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float t))
                    times[id] = t;
                var key = (cls, index);
                switch (message.GetValueOrDefault(id))
                {
                    case "Enter": enters.TryAdd(key, id); break;
                    case "Release": releases.TryAdd(key, id); break;
                    case "Attack": oneShots.TryAdd(key, id); break;
                }
            }
            var map = new Dictionary<(uint, uint), Animation>();
            foreach (((uint, uint) key, uint enter) in enters)
            {
                uint release = releases.GetValueOrDefault(key);
                int ms = times.TryGetValue(enter, out float t) ? (int)(t * 1000) : DefaultReleaseMs;
                map[key] = new Animation(enter, release, Math.Clamp(ms, 100, 1500));
            }
            foreach (((uint, uint) key, uint attack) in oneShots)
                map.TryAdd(key, new Animation(attack, 0, 0));
            classAnimations = map;
            log.Info($"Weapon attack animations: {map.Count} (class, index) pairs.");
        }

        /// <summary>Time a strike's motion needs after its Release before ActiveTimeEnd may stop it (seconds; not in client data).</summary>
        private const float StrikeMotionSeconds = 0.35f;

        /// <summary>Slack for the cast success's trip to the client: the window starts at the client's cast, the strike at the answer.</summary>
        private const float AnswerSlackSeconds = 0.15f;

        /// <summary>
        /// How long a weapon cast's window must last (cast + recovery) so ActiveTimeEnd comes after the strike: the latest
        /// Release this cast can play with this weapon (any combo stage) plus the strike's motion and the answer's trip.
        /// </summary>
        public static float StrikeWindowSeconds(Inventory.ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            int releaseMs = 0;
            for (int stage = 0; stage < 3; stage++)
                if (AttackAnimation(weapon, ability, stage) is { Release: not 0 } a)
                    releaseMs = Math.Max(releaseMs, a.ReleaseMs);
            return releaseMs / 1000f + StrikeMotionSeconds + AnswerSlackSeconds;
        }

        /// <summary>The animation a weapon cast plays with this weapon, or null when its class has none.</summary>
        public static Animation? AttackAnimation(Inventory.ItemDefinitionManager.ItemDefinition weapon, uint ability, int stage)
        {
            uint cls = ClassOf(weapon);
            if (cls == 0)
                return null;
            if (classAnimations == null)
                LoadClassAnimations();
            bool Has(uint i) => classAnimations.ContainsKey((cls, i));
            // A pair (Enter + Release) plays on its own; a one-shot "Attack" shows nothing unless the character is
            // already in that stance (MODLOG: selection 1; the sword's 429 lunge stayed invisible as a power attack).
            bool Pair(uint i) => classAnimations.TryGetValue((cls, i), out Animation x) && x.Release != 0;
            uint First(uint fallback, params uint[] order) => order.FirstOrDefault(Pair, order.FirstOrDefault(Has, fallback));
            uint[] basic = new uint[] { 0, 1, 2 }.Where(Has).ToArray();
            uint index;
            switch (KindOf(ability))
            {
                case Kind.Power:
                case Kind.PowerCombo:
                    // The heavy attack (3), else the class's charged swing (11, 13) or other strike (10).
                    index = First(basic.Length > 0 ? basic[^1] : 0u, 3, 11, 13, 10);
                    break;
                case Kind.Special:
                    index = First(0u, 4, 3, 12, 11);
                    break;
                default:
                    index = basic.Length > 0 ? basic[stage % basic.Length] : 0u;
                    break;
            }
            return classAnimations.TryGetValue((cls, index), out Animation a) ? a : null;
        }

        public static double DamageMultiplier(uint ability) => rules[KindOf(ability)].Damage;

        /// <summary>Every weapon cast id the input table names (basic, power, their combo stages, special).</summary>
        public static IEnumerable<uint> CastIds
        {
            get
            {
                KindOf(0);
                return kinds.Keys.ToList();
            }
        }

        public static uint EnergyCost(uint ability) => rules[KindOf(ability)].Energy;

        // ---- Energy (Resources.txt 19: max 456, REGEN_PER_MS 0.02) -------------------------------------------------

        private const double EnergyMax = CharacterResources.EnergyMax, EnergyRegenPerMs = 0.02;
        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, (double Value, DateTime At)> energy = new();

        /// <summary>The player's Energy now (regenerating since the last change).</summary>
        public static uint EnergyOf(WorldSession session)
        {
            lock (sync)
                return (uint)EnergyNow(session, DateTime.UtcNow);
        }

        private static double EnergyNow(WorldSession session, DateTime now)
        {
            if (!energy.TryGetValue(session, out var e))
                return EnergyMax;
            return Math.Min(EnergyMax, e.Value + (now - e.At).TotalMilliseconds * EnergyRegenPerMs);
        }

        /// <summary>
        /// Pays a cast's Energy: false (nothing taken) when the player hasn't enough. The new value goes to the client,
        /// which regenerates it from there itself.
        /// </summary>
        public static bool PayEnergy(WorldSession session, uint ability)
        {
            uint cost = (uint)Math.Round(EnergyCost(ability) * Combat.WeaponCombat.CostFactor(session, ability));
            if (cost == 0)
                return true;
            DateTime now = DateTime.UtcNow;
            double left;
            lock (sync)
            {
                double have = EnergyNow(session, now);
                if (have < cost)
                    return false;
                left = have - cost;
                energy[session] = (left, now);
            }
            CharacterResources.Send(session, CharacterResources.EnergyId, CharacterResources.EnergyType, (uint)left);
            return true;
        }

        /// <summary>Adds Energy (Flow, Inner Spark armour; capped at the maximum) and tells the client.</summary>
        public static void GainEnergy(WorldSession session, double amount)
        {
            if (amount <= 0)
                return;
            DateTime now = DateTime.UtcNow;
            double value;
            lock (sync)
            {
                double have = EnergyNow(session, now);
                if (have >= EnergyMax)
                    return;
                value = Math.Min(EnergyMax, have + amount);
                energy[session] = (value, now);
            }
            CharacterResources.Send(session, CharacterResources.EnergyId, CharacterResources.EnergyType, (uint)value);
        }

        /// <summary>Surge: spends half of the stored Energy; returns how much was spent.</summary>
        public static double SpendHalfEnergy(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            double spent, left;
            lock (sync)
            {
                double have = EnergyNow(session, now);
                spent = have / 2;
                left = have - spent;
                energy[session] = (left, now);
            }
            CharacterResources.Send(session, CharacterResources.EnergyId, CharacterResources.EnergyType, (uint)left);
            return spent;
        }

        /// <summary>Forgets a player who left (Energy is full again at the next world entry).</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                energy.Remove(session);
            lock (comboSync)
                combos.Remove(session);
        }
    }
}
