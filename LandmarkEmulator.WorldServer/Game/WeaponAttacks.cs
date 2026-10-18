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
    /// those pages. Each weapon then has its own damage factors and Power Attack cost (<see cref="Balance"/>: the wiki's
    /// tooltip costs and the 26 April / 25 May 2016 balance passes, TASKS 6.9).
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

        /// <summary>
        /// When a melee cast's Sweep targeter runs (WeaponCombat.Targeters, OPEN-LEADS L170), seconds after the client's
        /// cast: from the earliest Release this cast can play with this weapon (the animation's AttackTime, any combo stage;
        /// <see cref="DefaultReleaseMs"/> without one) to the latest Release plus the strike's motion. The caster info is
        /// per cast id, not per combo stage, so one window covers every stage [I].
        /// </summary>
        public static (float Delay, float Duration) SweepTiming(Inventory.ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            int first = int.MaxValue, last = 0;
            for (int stage = 0; stage < 3; stage++)
                if (AttackAnimation(weapon, ability, stage) is { Release: not 0 } a)
                {
                    first = Math.Min(first, a.ReleaseMs);
                    last = Math.Max(last, a.ReleaseMs);
                }
            if (first == int.MaxValue)
                first = last = DefaultReleaseMs;
            return (first / 1000f, (last - first) / 1000f + StrikeMotionSeconds);
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

        // ---- Per-weapon balance (TASKS 6.9) ------------------------------------------------------------------------
        //
        // Live balanced every weapon on its own; the numbers were server data (the client's AbilityEx.txt is empty, and
        // its AbilityParameterDefinitions rows — 296 "Damage stat × PARAM_2 + PARAM_3" formulas — carry no ability or
        // weapon id; see trace/0695/gear-stats-695.md). A row is tied to an attack only where an attack's description
        // names it with an [*AbilityParamN*] token, and the only such 2016 attack texts are for attacks that never went
        // live: 573112-573114 (Katana of Crashing Waves, rows 676-681 / 686 / 687), 573128 (Flame Bound Bastion's
        // unshipped "Devouring Flame", 691 / 693 / 695 / 696) and 573170-573172 (Desperado's Deadly Arsenal, 703-711,
        // used by Combat.WeaponCombat's kit for it); the other token texts are 2015 skill texts (trace/0695/
        // agent-leads4.md L163). So no live weapon's numbers come from a row. What we have:
        //  [W] the Energy ("mana") cost of the Power Attack shown by the live tooltip, copied onto the fandom wiki weapon
        //      pages (design/wiki/pages/<weapon>.wiki, "<attack>'s Stats"). The page revision dates are not in the dump,
        //      so whether a value predates the 25 May 2016 pass is not known (open question; the pages of the weapons
        //      added on 25 May — Electrified Assault Axe, Crusader's Courage, Night Herald's Mercy, Blight-Warden's
        //      Pain — are necessarily later). A wiki value is used as it stands and wins over the notes' directions.
        //  [N] the patch notes' directions: Hotfix notes 26 April 2016 (design/forum/patches/2016-04-26-56999-…, old ids
        //      P3678-P3727, now P4720-P4770) and Update notes 25 May 2016 "All weapons have undergone a damage and energy
        //      cost balance pass" (2016-05-24-57170-…, old ids P4220-P4288, now P5553-P5596). No later note up to the
        //      0.695 build (Oct 2016) changes a weapon's damage or cost (searched trace/0695/patchnotes.tsv).
        //  [I] the size of each step is ours: every "increased / decreased damage" is ±10 % on our uniform baseline
        //      (Basic 1.0 / Power 1.8 of the base weapon damage), applied in order (26 Apr, then 25 May), so two steps
        //      the same way compound (1.21 / 0.81). Costs without a wiki value: ±10 % on our 60 per step.
        // Weapons the notes never name keep 1.0 / 60. Pre-launch items that share a launch weapon's kit (1291 … 1529,
        // see Combat.WeaponCombat.Kits) share its balance.

        /// <summary>
        /// One weapon's balance: damage factors for its Basic and Power Attacks (on top of the kind's multiplier), the
        /// Power Attack's Energy cost per use in a row (<paramref name="PowerEnergy"/>[run % length]; the combo follow-up
        /// cast id is shown the second entry, or the first + 10 when there is one entry), and the Energy each further
        /// hit of a channelled Power Attack costs ("energy cost over time"; 0 = none).
        /// </summary>
        public sealed record Balance(float Basic, float Power, uint[] PowerEnergy, uint TickEnergy = 0);

        private const uint DefaultPowerEnergy = 60;

        private static readonly Dictionary<uint, Balance> balance = BuildBalance();

        private static Dictionary<uint, Balance> BuildBalance()
        {
            var b = new Dictionary<uint, Balance>();
            void Add(Balance x, params uint[] items) { foreach (uint i in items) b[i] = x; }
            const float Up = 1.1f, Up2 = 1.21f, Down = 0.9f, Down2 = 0.81f;

            // Wanderer's Blazing Bow: 25 May damage up [N]; power attack energy cost up —
            // Immolating Barrage 55 [W].
            Add(new Balance(Up, Up, new uint[] { 55 }), 3761, 1296);
            // Staff of Frozen Sorrow: 25 May damage up, power attack energy cost down (no wiki
            // value: 60 → 54 [I]).
            Add(new Balance(Up, Up, new uint[] { 54 }), 3762, 1291);
            // Supercooled Power Cannon: Tidal Burst 26 Apr more damage, Torrent 26 Apr less damage, then
            // 25 May damage up for both → 1.21 / 0.99 [N][I]. Torrent's cost: the wiki
            // shows "N/A" (no initial cost) [W]; 26 Apr energy cost over time up, 25 May energy cost over time
            // down → 12 per further hit of the 4-hit channel [I].
            Add(new Balance(Up2, Down * Up, new uint[] { 0 }, TickEnergy: 12), 3036);
            // Falchion of the Inner Spark: Arcing Strike 26 Apr more damage + 25 May basic attack damage
            // up → 1.21; Terminal Voltage 26 Apr less damage + 25 May power attack damage down →
            // 0.81 [N][I]. Cost 70 [W] (26 Apr energy cost down, 25 May energy cost down).
            Add(new Balance(Up2, Down2, new uint[] { 70 }), 3763, 1503);
            // Overcharged Recon Blaster: 25 May basic attack damage up, power attack damage down [N];
            // Shock and Awe 30 [W]. energy cost over time down is for the follow-up channelled volley, which our
            // kit doesn't have (open).
            Add(new Balance(Up, Down, new uint[] { 30 }), 3033);
            // Armored Brawler's Blade: 25 May damage down [N]; Overrun 68 [W] (energy cost up).
            Add(new Balance(Down, Down, new uint[] { 68 }), 3764, 1293);
            // Gunslinger's Six-Shooters: 25 May damage up [N]. Trick Shot 55 [W]; 26 Apr energy cost up, then 25 May first use
            // cheaper / second use dearer → 50 then 62 [I] around the wiki's 55.
            Add(new Balance(Up, Up, new uint[] { 50, 62 }), 3040);
            // Poison Drenched Daggers: 25 May damage down [N]; Contaminate 52 [W] (energy cost down).
            Add(new Balance(Down, Down, new uint[] { 52 }), 3765, 1517);
            // Diabolical Scientist's Raygun: 25 May damage down [N]; Sludge Storm 76 [W] (energy cost down).
            Add(new Balance(Down, Down, new uint[] { 76 }), 3032);
            // Armament of Unrelenting Vengeance: Dark Embrace 26 Apr more damage, 25 May damage up
            // (both attacks) → Basic 1.1, Power 1.21 [N][I]. Initial cost 30 [W] (26 Apr initial energy cost down);
            // over time: 26 Apr up, 25 May energy cost over time down → 6 per further hit of the 4-hit
            // channel [I].
            Add(new Balance(Up, Up2, new uint[] { 30 }, TickEnergy: 6), 3766, 1528);
            // Flame Bound Bastion: 25 May basic attack damage down, power attack damage up (Molten
            // Rebuke's counter, Combat.WeaponCombat.ShieldBash) [N]. No wiki cost: 60.
            Add(new Balance(Down, Up, new uint[] { DefaultPowerEnergy }), 3030);
            // Avenging Champion's Exaltation: 25 May damage up, initial energy cost down and
            // energy cost over time up of Consecrate (5-hit channel) → 50 + 6 per further hit [I].
            Add(new Balance(Up, Up, new uint[] { 50 }, TickEnergy: 6), 3037);
            // Spear of Piercing Judgement: 25 May damage down [N]; Nightfall 58 [W] (energy cost down).
            // (Silent Shadow's Condemnation 3039 shares the kit but is not named: default.)
            Add(new Balance(Down, Down, new uint[] { 58 }), 3034);
            // Wand of Boundless Light: 25 May damage down [N]; Cascade 56 [W] (energy cost down).
            Add(new Balance(Down, Down, new uint[] { 56 }), 3767, 1529);
            // Weapons added on 25 May (pages written after it): costs [W], damage 1.0.
            Add(new Balance(1f, 1f, new uint[] { 59, 74, 48 }), 3027);   // Electrified Assault Axe: Rolling Thunder's three leaps
            Add(new Balance(1f, 1f, new uint[] { 21 }), 4091);           // Blight-Warden's Pain: Corrupting Revenge
            Add(new Balance(1f, 1f, new uint[] { 56 }), 4096);           // Crusader's Courage: Righteous Swing
            Add(new Balance(1f, 1f, new uint[] { 68 }), 4089);           // Night Herald's Mercy: Agonizing Volley
            return b;
        }

        /// <summary>The weapon's balance entry, or null for a weapon the notes and the wiki never priced.</summary>
        public static Balance BalanceOf(Inventory.ItemDefinitionManager.ItemDefinition weapon)
            => weapon != null && balance.TryGetValue(weapon.Id, out Balance b) ? b : null;

        private static bool IsPower(Kind k) => k is Kind.Power or Kind.PowerCombo;

        /// <summary>
        /// The damage multiplier of a weapon cast: the kind's (<see cref="rules"/>) × the weapon's Basic or Power factor
        /// (<see cref="Balance"/>). The Special (E) attack and gear abilities keep the kind's.
        /// </summary>
        public static double DamageMultiplier(Inventory.ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            Kind k = KindOf(ability);
            double m = rules[k].Damage;
            if (BalanceOf(weapon) is Balance b)
            {
                if (k is Kind.Basic or Kind.BasicCombo) m *= b.Basic;
                else if (IsPower(k)) m *= b.Power;
            }
            return m;
        }

        /// <summary>The weapon's factor for one of its attacks (shield bashes and other hits that aren't a cast).</summary>
        public static float DamageFactor(Inventory.ItemDefinitionManager.ItemDefinition weapon, bool power)
            => BalanceOf(weapon) is Balance b ? (power ? b.Power : b.Basic) : 1f;

        /// <summary>
        /// A weapon cast's Energy cost before states (Supercharge, Forsaken Band): the weapon's Power Attack cost for
        /// the <paramref name="run"/>-th Power Attack in a row, else the kind's (<see cref="rules"/>).
        /// </summary>
        public static uint EnergyCost(Inventory.ItemDefinitionManager.ItemDefinition weapon, uint ability, int run)
        {
            Kind k = KindOf(ability);
            if (!IsPower(k) || BalanceOf(weapon) is not Balance b || b.PowerEnergy.Length == 0)
                return rules[k].Energy;
            if (b.PowerEnergy.Length == 1 && k == Kind.PowerCombo && b.PowerEnergy[0] > 0)
                return b.PowerEnergy[0] + (rules[Kind.PowerCombo].Energy - rules[Kind.Power].Energy);
            return b.PowerEnergy[Math.Max(0, run) % b.PowerEnergy.Length];
        }

        /// <summary>The cost a cast id shows in its caster info: the first use for Power, the second for its combo id.</summary>
        public static uint ShownEnergyCost(Inventory.ItemDefinitionManager.ItemDefinition weapon, uint ability)
            => EnergyCost(weapon, ability, KindOf(ability) == Kind.PowerCombo ? 1 : 0);

        /// <summary>Energy each further hit of a channelled Power Attack costs (0 = none).</summary>
        public static uint TickEnergy(Inventory.ItemDefinitionManager.ItemDefinition weapon) => BalanceOf(weapon)?.TickEnergy ?? 0;

        // ---- Power Attacks in a row (Rolling Thunder's leaps, the Six-Shooters' second Trick Shot) ----------------

        private static readonly Dictionary<WorldSession, (int Run, DateTime At)> powerRuns = new();

        /// <summary>The run the next Power Attack would be (0 first, 1 second … within the combo window).</summary>
        private static int NextPowerRun(WorldSession session, DateTime now)
            => powerRuns.TryGetValue(session, out var r) && now - r.At <= ComboWindow ? r.Run + 1 : 0;

        /// <summary>The run of the player's last paid Power Attack (0 = first in a row).</summary>
        public static int PowerRunOf(WorldSession session)
        {
            lock (sync)
                return powerRuns.TryGetValue(session, out var r) ? r.Run : 0;
        }

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
        /// <summary>The Energy maximum the player's client was last told (no entry = the table's 456).</summary>
        private static readonly Dictionary<WorldSession, double> sentCap = new();

        /// <summary>
        /// Flow (Water's on-hit property) raises the Energy maximum while it lasts: hits can raise the Energy maximum
        /// for a while (21 Mar 2016 launch notes, P4197). Sigil of Piercing Floods: "Flow grants more maximum
        /// energy" (565737). [I] sizes ours: +20 a stack (5 stacks: +100 over the table's 456), ×1.5 with the Sigil.
        /// </summary>
        public const double FlowMaxPerStack = 20, PiercingFloodsFactor = 1.5;

        /// <summary>The player's Energy maximum now: Resources.txt's 456 plus Flow (<see cref="FlowMaxPerStack"/>).</summary>
        public static double EnergyCap(WorldSession session)
        {
            int flow = Combat.Statuses.Stacks(session.PlayerGuid, Combat.Status.Flow);
            if (flow <= 0)
                return EnergyMax;
            return EnergyMax + flow * FlowMaxPerStack
                * (Combat.Gear.Has(session, Combat.Gear.SigilOfPiercingFloods) ? PiercingFloodsFactor : 1.0);
        }

        /// <summary>The player's Energy now (regenerating since the last change).</summary>
        public static uint EnergyOf(WorldSession session)
        {
            double cap = EnergyCap(session);
            lock (sync)
                return (uint)EnergyNow(session, DateTime.UtcNow, cap);
        }

        private static double EnergyNow(WorldSession session, DateTime now, double cap)
        {
            if (!energy.TryGetValue(session, out var e))
                return cap;
            return Math.Min(cap, e.Value + (now - e.At).TotalMilliseconds * EnergyRegenPerMs);
        }

        /// <summary>
        /// Sends the Energy pool. Above the table's maximum the record carries the MAX_VALUE variable (CharacterResources:
        /// base 456 + modifier), on every Set while it lasts, because a Set replaces the whole variable list; back at 456
        /// the plain record lets the client fall back to Resources.txt. Call outside <see cref="sync"/>.
        /// </summary>
        private static void SendEnergy(WorldSession session, double value, double cap)
        {
            lock (sync)
            {
                if (cap > EnergyMax)
                    sentCap[session] = cap;
                else
                    sentCap.Remove(session);
            }
            CharacterResources.Send(session, CharacterResources.EnergyId, CharacterResources.EnergyType, (uint)value,
                cap > EnergyMax ? ((uint)EnergyMax, (uint)Math.Round(cap)) : null);
        }

        /// <summary>
        /// The Energy maximum may have changed (Flow stacks gained or Flow ended): Energy above the new maximum is cut
        /// and the client gets the pool with its maximum. Nothing is sent when the client already has this maximum.
        /// </summary>
        public static void EnergyCapChanged(WorldSession session)
        {
            double cap = EnergyCap(session);
            DateTime now = DateTime.UtcNow;
            double value;
            lock (sync)
            {
                if ((sentCap.TryGetValue(session, out double sent) ? sent : EnergyMax) == cap)
                    return;
                value = EnergyNow(session, now, cap);
                energy[session] = (value, now);
            }
            SendEnergy(session, value, cap);
        }

        /// <summary>
        /// Pays a cast's Energy: false (nothing taken) when the player hasn't enough. The new value goes to the client,
        /// which regenerates it from there itself.
        /// </summary>
        public static bool PayEnergy(WorldSession session, uint ability)
        {
            // The held weapon's own cost (TASKS 6.9, Balance): Power Attacks in a row may cost differently.
            var weapon = Abilities.HeldWeapon(session);
            bool power = IsPower(KindOf(ability));
            DateTime now = DateTime.UtcNow;
            int run;
            lock (sync)
                run = power ? NextPowerRun(session, now) : 0;
            uint cost = (uint)Math.Round(EnergyCost(weapon, ability, run) * Combat.WeaponCombat.CostFactor(session, ability));
            double left = -1, cap = EnergyCap(session);
            lock (sync)
            {
                if (cost > 0)
                {
                    double have = EnergyNow(session, now, cap);
                    if (have < cost)
                        return false;
                    left = have - cost;
                    energy[session] = (left, now);
                }
                if (power)
                    powerRuns[session] = (run, now);
            }
            if (left >= 0)
                SendEnergy(session, left, cap);
            return true;
        }

        /// <summary>
        /// Takes Energy for a further hit of a channel ("energy cost over time"): false (nothing taken) when the player
        /// hasn't enough, which ends the channel.
        /// </summary>
        public static bool TrySpendEnergy(WorldSession session, double amount)
        {
            if (amount <= 0)
                return true;
            DateTime now = DateTime.UtcNow;
            double left, cap = EnergyCap(session);
            lock (sync)
            {
                double have = EnergyNow(session, now, cap);
                if (have < amount)
                    return false;
                left = have - amount;
                energy[session] = (left, now);
            }
            SendEnergy(session, left, cap);
            return true;
        }

        /// <summary>
        /// Adds Energy (Flow, Energize, Inner Spark armour; capped at the maximum) and tells the client. Also sends the
        /// pool when it is full but the maximum changed since the client last heard (a new Flow stack).
        /// </summary>
        public static void GainEnergy(WorldSession session, double amount)
        {
            DateTime now = DateTime.UtcNow;
            double value, cap = EnergyCap(session);
            lock (sync)
            {
                bool capKnown = (sentCap.TryGetValue(session, out double sent) ? sent : EnergyMax) == cap;
                double have = EnergyNow(session, now, cap);
                if ((amount <= 0 || have >= cap) && capKnown)
                    return;
                value = Math.Min(cap, have + Math.Max(0, amount));
                energy[session] = (value, now);
            }
            SendEnergy(session, value, cap);
        }

        /// <summary>Surge: spends half of the stored Energy; returns how much was spent.</summary>
        public static double SpendHalfEnergy(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            double spent, left, cap = EnergyCap(session);
            lock (sync)
            {
                double have = EnergyNow(session, now, cap);
                spent = have / 2;
                left = have - spent;
                energy[session] = (left, now);
            }
            SendEnergy(session, left, cap);
            return spent;
        }

        /// <summary>Forgets a player who left (Energy is full again at the next world entry).</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                energy.Remove(session);
                sentCap.Remove(session);
                powerRuns.Remove(session);
            }
            lock (comboSync)
                combos.Remove(session);
        }
    }
}
