using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// Launch weapon combat (wiki "Combat", "Weapons", "Affinity", the weapon pages; the client's own texts): every
    /// weapon has an affinity and two named attacks — Basic (left mouse) and Power (right mouse) — whose descriptions
    /// say what they hit and which affinity property they trigger, e.g. Staff of Frozen Sorrow "Basic Attack: Shatter"
    /// (574445/574446) and "Power Attack: Blizzard" (574447/574448). The cast ids are the same for every weapon (the
    /// client's input table: 760 basic, 761 power …, see <see cref="WeaponAttacks"/>), so the live server applied the
    /// held weapon's attack; this class does the same from <see cref="Kits"/>.
    /// <para>
    /// What is the client's: names, descriptions, icons, state effects and impacts. What is ours (the ability data was
    /// server-side and is lost): shapes (reach, arc), damage split, durations and chances — kept as named numbers here
    /// and in <see cref="Statuses.Defs"/>. Charged attacks ("Charge a powerful …") get the charged kind in their caster
    /// info, so the client charges while the button is held and sends FinishChargedCast on release; the server times the
    /// hold (damage 60-100 %, "fully charged" from <see cref="MaxChargeSeconds"/>). Channels are a few timed hits. Frenzy ("attacks faster") and Supercharge ("cost
    /// more energy") change the attacks' cast time and Energy cost in the caster info the client paces itself by.
    /// </para>
    /// </summary>
    public static class WeaponCombat
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public enum Shape
        {
            /// <summary>Opponents in front within Range, inside ±Size degrees.</summary>
            Cone,
            /// <summary>Everyone within Range of the player.</summary>
            Around,
            /// <summary>The first opponent along the aim, up to Range; Size &gt; 0 also hits those within Size metres of it.</summary>
            FirstHit,
            /// <summary>Every opponent along the aim within Size metres of the line, up to Range.</summary>
            Pierce,
            /// <summary>Everyone within Size metres of the aimed point (at most Range away).</summary>
            AreaAtAim,
            /// <summary>A dash / leap: everyone within Size metres of the path ahead (Range long).</summary>
            Path,
            /// <summary>A blocking stance: nothing is hit now; the next attack from the front is stopped and countered.</summary>
            Block
        }

        /// <summary>
        /// One attack: name / description texts, what it hits, whether "fully charged" applies, channel hits (Ticks, every
        /// TickMs), a per-target rule, a rule once the swing is over, and for blocks the counter's state.
        /// </summary>
        public sealed record Attack(uint Name, uint Description, Shape Shape, float Range, float Size, bool Charged = false,
            int Ticks = 1, int TickMs = 400, Action<Hit> OnHit = null, Action<Swing> After = null, Status? Counter = null,
            int MaxTargets = 0, uint Effect = 0);

        /// <summary>A weapon's affinity and its two attacks.</summary>
        public sealed record Kit(Affinity Affinity, Attack Basic, Attack Power, uint Icon);

        /// <summary>One use of an attack.</summary>
        public sealed class Swing
        {
            public WorldSession Player;
            public Kit Kit;
            public Attack Attack;
            public bool Power, Charged;
            public Vector3 Origin, Forward, Aim;
            public uint Damage;
            public readonly List<ulong> Hits = new();
            public readonly HashSet<ulong> Erupted = new();
            /// <summary>Exalt / High Herald's Ring: every weapon property stronger (chances, durations).</summary>
            public float Strength = 1f;
            public bool Ranged;
        }

        /// <summary>One opponent hit by a swing.</summary>
        public sealed class Hit
        {
            public Swing Swing;
            public ulong Target;
            public uint Dealt;
            public bool Killed;
            public bool Erupted;
            /// <summary>Times this player hit this opponent in a row (same attack kind, within the combo window).</summary>
            public int InARow;
        }

        // ---- Numbers (ours) ------------------------------------------------------------------------------------------

        private const float EruptChance = 0.15f, RegrowthChance = 0.2f, RejuvenateChance = 0.2f, MaeChance = 0.3f;
        private const float BurnPerSecond = 0.15f, RotPerSecond = 0.12f, CondemnPerSecond = 0.1f;
        private const float RevengeShare = 0.3f, ExecuteBelow = 0.2f, SiphonShare = 0.15f;
        private const float FlowEnergy = 10f, IlluminateShare = 0.1f, RegrowthShare = 0.03f, RejuvenateShare = 0.01f;
        private static readonly TimeSpan ComboWindow = TimeSpan.FromSeconds(2);

        // ---- The weapons ---------------------------------------------------------------------------------------------

        private static Dictionary<uint, Kit> kits;

        /// <summary>Weapon item → kit. Pre-launch items (1291 … 1529) share the kit of the launch weapon that replaced them.</summary>
        public static Dictionary<uint, Kit> Kits => kits ??= BuildKits();

        private static Dictionary<uint, Kit> BuildKits()
        {
            var k = new Dictionary<uint, Kit>();
            void Add(Kit kit, params uint[] items) { foreach (uint i in items) k[i] = kit; }

            // Fire — Erupt, Frenzy, Burn.
            Add(new Kit(Affinity.Fire,
                new Attack(574435, 574443, Shape.FirstHit, 35f, 3f,                                   // Explosive Shot
                    After: s => { if (s.Erupted.Count > 0) Grant(s, Status.Frenzy); }),
                new Attack(574436, 574444, Shape.Cone, 15f, 35f,                                      // Immolating Barrage
                    OnHit: h => { if (h.Erupted) Burn(h); }), 12211), 3761, 1296);
            Add(new Kit(Affinity.Fire,
                new Attack(576300, 576301, Shape.Cone, 5f, 60f,                                       // Fiery Cleave
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Burn)) Grant(h.Swing, Status.Frenzy); }),
                new Attack(576303, 576302, Shape.Cone, 6f, 50f, Charged: true,                        // Conflagration
                    After: s => { if (s.Charged) Field(s, s.Origin + s.Forward * 4f, 4f, 4, Status.Burn, 0f, effect: 11613); }), 12211), 3031);
            Add(new Kit(Affinity.Fire,
                new Attack(573122, 573124, Shape.Cone, 5f, 60f,                                       // Cinder Strike
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Burn)) Grant(h.Swing, Status.Frenzy); }),
                new Attack(573125, 573126, Shape.Block, 4f, 60f, Counter: Status.Burn), 12211), 3030); // Molten Rebuke

            // Water — Flow, Surge, Freeze.
            Add(new Kit(Affinity.Water,
                new Attack(574445, 574446, Shape.FirstHit, 35f, 3f, Charged: true,                    // Shatter
                    OnHit: h => { if (h.Swing.Charged) Surge(h); }),
                new Attack(574447, 574448, Shape.AreaAtAim, 30f, 5f,                                  // Blizzard
                    After: s => Field(s, s.Aim, 5f, 4, null, 0.25f, endFreeze: true, effect: 11528)), 12207), 3762, 1291);
            Add(new Kit(Affinity.Water,
                new Attack(574717, 574718, Shape.FirstHit, 35f, 0f, Charged: true,                    // Tidal Burst
                    OnHit: h =>
                    {
                        if (Statuses.Has(h.Target, Status.Freeze)) Surge(h);
                        if (h.Swing.Charged) Grant(h.Swing, Status.Flow);
                    }),
                new Attack(574719, 574720, Shape.Cone, 15f, 25f, Ticks: 4, TickMs: 300,              // Torrent
                    OnHit: h => { if (h.InARow >= 3) Apply(h, Status.Freeze); }), 12207), 3036);
            Add(new Kit(Affinity.Water,
                new Attack(576334, 576335, Shape.Cone, 6f, 45f,                                       // Fracture
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Freeze)) Surge(h); }),
                new Attack(576336, 576337, Shape.Around, 6f, 0f, Charged: true, Effect: 11533,        // Frozen Domain
                    OnHit: h => { if (h.Swing.Charged) Apply(h, Status.Freeze); }), 12207), 4097, 3029);

            // Lightning — Supercharge, Energize, Electrocute.
            Add(new Kit(Affinity.Lightning,
                new Attack(574449, 574450, Shape.Cone, 5f, 80f,                                       // Arcing Strike
                    After: s =>
                    {
                        if (s.Hits.Count > 0 && SecondInARow(s))
                        {
                            Chain(s, 5f, 0.5f);
                            Grant(s, Status.Supercharge);
                        }
                    }),
                new Attack(574451, 0, Shape.Cone, 6f, 50f, Charged: true,                             // Terminal Voltage
                    OnHit: h => { if (h.Swing.Charged) Apply(h, Status.Electrocute); }), 12209), 3763, 1503);
            Add(new Kit(Affinity.Lightning,
                new Attack(574721, 574722, Shape.FirstHit, 35f, 0f, Charged: true,                    // Feedback
                    OnHit: h => { if (h.Swing.Charged) Apply(h, Status.Electrocute); }),
                new Attack(574723, 574724, Shape.Cone, 10f, 50f,                                      // Shock and Awe
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Electrocute)) Grant(h.Swing, Status.Supercharge); }), 12209), 3033);
            Add(new Kit(Affinity.Lightning,
                new Attack(576330, 576331, Shape.FirstHit, 35f, 0f, Charged: true,                    // Crackling Orb
                    OnHit: h => { if (h.Swing.Charged) Grant(h.Swing, Status.Supercharge); }),
                new Attack(576332, 576333, Shape.AreaAtAim, 25f, 5f, Effect: 12784,                   // Static Bomb
                    OnHit: h => { if (Statuses.Has(h.Swing.Player.PlayerGuid, Status.Supercharge)) Apply(h, Status.Electrocute); }), 12209), 4107);
            Add(new Kit(Affinity.Lightning,
                new Attack(576428, 576436, Shape.Cone, 5f, 60f, Charged: true,                        // Overcharge
                    OnHit: h => { if (h.Swing.Charged) Grant(h.Swing, Status.Supercharge); }),
                new Attack(576424, 0, Shape.Path, 10f, 3f, Effect: 12782,                            // Rolling Thunder
                    OnHit: h => { if (Statuses.Has(h.Swing.Player.PlayerGuid, Status.Supercharge)) Apply(h, Status.Electrocute); }), 12209), 3027);

            // Earth — Harden, Regrowth, Crush.
            Add(new Kit(Affinity.Earth,
                new Attack(574452, 574453, Shape.Cone, 5f, 60f,                                       // Assault
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Crush)) Grant(h.Swing, Status.Harden); }),
                new Attack(574454, 574455, Shape.Path, 8f, 3f, Charged: true,                         // Overrun
                    OnHit: h => { if (h.Swing.Charged && h.Swing.Hits.Count == 1) Apply(h, Status.Crush); }), 12213), 3764, 1293);
            Add(new Kit(Affinity.Earth,
                new Attack(574876, 574877, Shape.Cone, 20f, 20f, Ticks: 3, TickMs: 250,               // Unload
                    OnHit: h => Apply(h, Status.Exposed)),
                new Attack(574878, 574879, Shape.Around, 12f, 0f, MaxTargets: 3,                     // Trick Shot
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Exposed)) { Statuses.Remove(h.Target, Status.Exposed); Apply(h, Status.Crush); } },
                    After: s =>
                    {
                        if (s.Hits.Count > 0 && Recently(s.Player, "trickshot", TimeSpan.FromSeconds(4)))
                            Grant(s, Status.Harden);
                        if (s.Hits.Count > 0) Mark(s.Player, "trickshot");
                    }), 12213), 3040);
            Add(new Kit(Affinity.Earth,
                new Attack(573199, 573202, Shape.Cone, 5f, 50f,                                       // Decimate
                    OnHit: h => { if (h.InARow >= 2) Apply(h, Status.Crush); }),
                new Attack(573200, 573203, Shape.Around, 5f, 0f, Effect: 12732,                       // Obliteration Turn
                    After: s => { if (s.Hits.Any(t => Statuses.Has(t, Status.Crush))) Grant(s, Status.Harden); }), 12213), 3035);

            // Affliction — Infect, Rot, Cripple.
            Add(new Kit(Affinity.Affliction,
                new Attack(574456, 574457, Shape.Around, 4f, 0f,                                      // Debilitating Shiv
                    After: s =>
                    {
                        if (s.Hits.Count > 0 && SecondInARow(s))
                            foreach (ulong t in Chain(s, 6f, 0.5f))
                                Statuses.Apply(t, Status.Cripple, s.Player, seconds: Defs(Status.Cripple) * s.Strength);
                    }),
                new Attack(574458, 574459, Shape.FirstHit, 12f, 3f,                                   // Contaminate
                    OnHit: h => Rot(h)), 12205), 3765, 1517);
            Add(new Kit(Affinity.Affliction,
                new Attack(576304, 576305, Shape.Cone, 5f, 60f,                                       // Enfeeble
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Rot)) Apply(h, Status.Cripple); }),
                new Attack(576306, 576307, Shape.Block, 4f, 60f, Counter: Status.Rot), 12205), 4091); // Corrupting Revenge
            Add(new Kit(Affinity.Affliction,
                new Attack(573028, 573071, Shape.FirstHit, 35f, 0f, Charged: true,                    // Ooze Bolt
                    After: s =>
                    {
                        if (!s.Charged)
                            return;
                        foreach (ulong t in Pierce(s, 35f, 1.5f).Where(t => !s.Hits.Contains(t)))
                            Strike(s, t, s.Damage, then: h => Apply(h, Status.Cripple));
                        foreach (ulong t in s.Hits.ToList())
                            Statuses.Apply(t, Status.Cripple, s.Player, seconds: Defs(Status.Cripple) * s.Strength);
                    }),
                new Attack(573069, 0, Shape.AreaAtAim, 25f, 5f, OnHit: h => Rot(h), Effect: 11638), 12205), 3032);   // Sludge Storm

            // Shadow — Siphon, Torment, Condemn.
            Add(new Kit(Affinity.Shadow,
                new Attack(574460, 574461, Shape.Cone, 6f, 50f, Charged: true,                        // Shadow Rend
                    OnHit: h => { if (h.Swing.Charged) Condemn(h); }),
                new Attack(574462, 574463, Shape.Cone, 6f, 50f, Ticks: 4, TickMs: 400,                // Dark Embrace
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Condemn)) Apply(h, Status.Torment); }), 12217), 3766, 1528);
            Add(new Kit(Affinity.Shadow,
                new Attack(574880, 574882, Shape.Path, 8f, 3f,                                        // Hateseeker
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Torment)) Condemn(h); }),
                new Attack(574881, 574883, Shape.Cone, 6f, 60f,                                       // Nightfall
                    OnHit: h => Apply(h, Status.Torment)), 12217), 3039, 3034);
            Add(new Kit(Affinity.Shadow,
                new Attack(576223, 576224, Shape.FirstHit, 40f, 0f,                                   // Cursed Bolt
                    OnHit: h => { if (h.InARow >= 3) Condemn(h); }),
                new Attack(576220, 576222, Shape.Cone, 20f, 30f, Charged: true, Effect: 12737,        // Agonizing Volley
                    OnHit: h => { if (h.Swing.Charged) Apply(h, Status.Torment); }), 12217), 4089);

            // Radiance — Illuminate, Exalt, Rejuvenate.
            Add(new Kit(Affinity.Radiance,
                new Attack(574464, 574465, Shape.FirstHit, 25f, 0f,                                   // Brilliance
                    After: s =>
                    {
                        if (s.Hits.Count > 0 && SecondInARow(s))
                        {
                            Chain(s, 6f, 1f);
                            Grant(s, Status.Illuminate);
                        }
                    }),
                new Attack(574466, 574467, Shape.Pierce, 20f, 2.5f,                                   // Cascade
                    After: s => { if (s.Hits.Count > 0) ExaltAllies(s, s.Hits.Select(Creatures.PositionOfGuid).OfType<Vector3>().First(), 10f); }), 12215), 3767, 1529);
            Add(new Kit(Affinity.Radiance,
                new Attack(574884, 574885, Shape.Cone, 5f, 60f, Charged: true,                        // Smite
                    After: s => { if (s.Charged && s.Hits.Count > 0) Grant(s, Status.Exalt); }),
                new Attack(574886, 574887, Shape.Around, 6f, 0f, Ticks: 5, TickMs: 400, Effect: 10300, // Consecrate
                    After: s => Consecrate(s)), 12215), 3037);
            Add(new Kit(Affinity.Radiance,
                new Attack(576314, 576315, Shape.Cone, 5f, 60f,                                       // Sanctify
                    After: s => { if (s.Hits.Count > 0 && Statuses.Has(s.Player.PlayerGuid, Status.Exalt)) Grant(s, Status.Illuminate); }),
                new Attack(576316, 576317, Shape.Cone, 6f, 50f, Charged: true,                        // Righteous Swing
                    After: s =>
                    {
                        if (!s.Charged)
                            return;
                        var orb = Pierce(s, 20f, 2f).Where(t => !s.Hits.Contains(t)).ToList();
                        foreach (ulong t in orb)
                            Strike(s, t, s.Damage);
                        if (orb.Count > 0 || s.Hits.Count > 0)
                            ExaltAllies(s, s.Player.Position, 10f);
                    }), 12215), 4096);
            log.Info($"Weapon kits: {k.Count} weapons.");
            return k;
        }

        /// <summary>
        /// Weapons with no named attacks in the client (Lightbringer's Justice, Desperado's Deadly Arsenal …): the generic
        /// "Basic Attack" / "Power Attack" names, a shot or a swing by the weapon's model, and the affinity's main
        /// opponent state on Power hits.
        /// </summary>
        private static Kit GenericKit(ItemDefinitionManager.ItemDefinition weapon)
        {
            Affinity a = AffinityOf(weapon);
            bool ranged = IsRangedModel(weapon);
            Status? main = a switch
            {
                Affinity.Fire => Status.Burn, Affinity.Water => Status.Freeze, Affinity.Lightning => Status.Electrocute,
                Affinity.Earth => Status.Crush, Affinity.Affliction => Status.Rot, Affinity.Shadow => Status.Torment, _ => null
            };
            var basic = ranged ? new Attack(565568, 0, Shape.FirstHit, 35f, 0f) : new Attack(565568, 0, Shape.Cone, 5f, 60f);
            var power = new Attack(565569, 0, Shape.Cone, ranged ? 15f : 6f, 45f,
                OnHit: h => { if (main is Status m) Apply(h, m); },
                After: s => { if (a == Affinity.Radiance && s.Hits.Count > 0) Grant(s, Status.Exalt); });
            return new Kit(a, basic, power, AttackIcon(a));
        }

        public static uint AttackIcon(Affinity a) => a switch
        {
            Affinity.Affliction => 12205, Affinity.Water => 12207, Affinity.Lightning => 12209, Affinity.Fire => 12211,
            Affinity.Earth => 12213, Affinity.Radiance => 12215, Affinity.Shadow => 12217, _ => 9898
        };

        /// <summary>The weapon's affinity: its description is the affinity text (574424 Fire, 574437 Water … 574442 Radiance).</summary>
        public static Affinity AffinityOf(ItemDefinitionManager.ItemDefinition weapon)
        {
            if (weapon == null)
                return Affinity.None;
            if (Kits.TryGetValue(weapon.Id, out Kit kit))
                return kit.Affinity;
            return weapon.DescriptionId switch
            {
                574424 => Affinity.Fire, 574437 => Affinity.Water, 574438 => Affinity.Lightning, 574439 => Affinity.Earth,
                574440 => Affinity.Affliction, 574441 => Affinity.Shadow, 574442 => Affinity.Radiance, _ => Affinity.None
            };
        }

        public static Kit KitOf(ItemDefinitionManager.ItemDefinition weapon)
            => weapon == null ? null : Kits.TryGetValue(weapon.Id, out Kit k) ? k : GenericKit(weapon);

        private static bool IsRangedModel(ItemDefinitionManager.ItemDefinition w)
        {
            string m = w?.ModelFile ?? "";
            return m.Contains("_Bow_") || m.Contains("_Gun") || m.Contains("Shrinkray") || m.Contains("_Staff_")
                || m.Contains("_Wand_") || m.Contains("Pulverizer");
        }

        // ---- Swinging ------------------------------------------------------------------------------------------------

        private sealed class Pace
        {
            public DateTime LastBasic;
            public int BasicRun;
            public readonly Dictionary<ulong, (int Count, DateTime At)> Streak = new();
            public readonly Dictionary<string, DateTime> Marks = new();
            public bool FirstPowerAfterFrenzy, FirstPowerWhileExalted;
            public ItemDefinitionManager.ItemDefinition InfoFor;
        }

        private static readonly Dictionary<WorldSession, Pace> paces = new();
        private static readonly object sync = new();

        private static Pace PaceOf(WorldSession s)
        {
            lock (sync)
            {
                if (!paces.TryGetValue(s, out Pace p))
                    paces[s] = p = new Pace();
                return p;
            }
        }

        /// <summary>
        /// A weapon cast: runs the held weapon's Basic or Power attack. <paramref name="damage"/> is the cast's damage
        /// before affinity bonuses. Returns true when something was hit.
        /// </summary>
        public static bool Use(WorldSession player, AbilityCastAttempt attempt, ItemDefinitionManager.ItemDefinition weapon, uint ability, uint damage)
            => Use(player, attempt.AimRay(player.Position), new Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ), weapon, ability, damage);

        // ---- Charged attacks ---------------------------------------------------------------------------------------

        /// <summary>The client's charged ability kind (FUN_142d71800 sets the charging state for it).</summary>
        public const uint ChargedKind = 0xF7150354;
        /// <summary>Hold this long for a full charge; the client won't release sooner than the minimum.</summary>
        public const float MaxChargeSeconds = 1.2f, MinChargeSeconds = 0.15f;

        private sealed record Charging(AbilityCastAttempt Attempt, ItemDefinitionManager.ItemDefinition Weapon, uint Damage,
            DateTime Start, WeaponAttacks.Animation? Animation, Vector3 Facing);

        private static readonly Dictionary<WorldSession, Charging> charging = new();

        /// <summary>Is this cast id a charged attack of this weapon?</summary>
        public static bool IsCharged(ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            Kit kit = KitOf(weapon);
            if (kit == null)
                return false;
            WeaponAttacks.Kind k = WeaponAttacks.KindOf(ability);
            Attack a = k is WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo or WeaponAttacks.Kind.Special ? kit.Power : kit.Basic;
            return a.Charged;
        }

        /// <summary>
        /// A charged attack's cast: remembered until FinishChargedCast (or CancelCast); others see the wind-up. False
        /// when the cast isn't a charged attack (it runs at once).
        /// </summary>
        public static bool BeginCharge(WorldSession p, AbilityCastAttempt attempt, ItemDefinitionManager.ItemDefinition weapon, uint damage,
            WeaponAttacks.Animation? animation, Vector3 facing)
        {
            if (!IsCharged(weapon, attempt.AbilityId) || Health.IsDead(p))
                return false;
            Charging old;
            lock (sync)
            {
                charging.Remove(p, out old);
                charging[p] = new Charging(attempt, weapon, damage, DateTime.UtcNow, animation, facing);
            }
            if (old != null)
                Abilities.AnswerCharged(p, old.Attempt, 0);
            if (animation is { } a)
                Emotes.ShowWeaponAttack(p, a.Enter, 0, 0, facing);
            log.Debug($"{p.CharacterName} charging {attempt.AbilityId}.");
            return true;
        }

        /// <summary>The charge was let go: the attack runs (charge share of the damage), then the cast is answered.</summary>
        public static void FinishCharge(WorldSession p, AbilityFinishChargedCast finish)
        {
            Charging c;
            lock (sync)
            {
                if (!charging.TryGetValue(p, out c) || (finish != null && !finish.Names(c.Attempt.AbilityId)))
                    return;
                charging.Remove(p);
            }
            double held = (DateTime.UtcNow - c.Start).TotalSeconds;
            float fraction = (float)Math.Clamp(held / MaxChargeSeconds, 0, 1);
            bool full = held >= MaxChargeSeconds * 0.95;
            uint damage = (uint)Math.Round(c.Damage * (0.6 + 0.4 * fraction));
            AbilityCastAttempt a = c.Attempt;
            // Aimed where the player looks when letting go (FinishChargedCast carries the camera ray), else at the press.
            var ray = finish?.Aim ?? a.AimRay(p.Position);
            bool hit = Use(p, ray, new Vector3(a.TargetX, a.TargetY, a.TargetZ), c.Weapon, a.AbilityId, damage, full);
            uint release = c.Animation?.Release ?? 0;
            if (release != 0)
                Emotes.ShowWeaponAttack(p, release, 0, 0, c.Facing);
            Abilities.AnswerCharged(p, a, release != 0 ? release : c.Animation?.Enter ?? 0);
            log.Info($"{p.CharacterName} released {a.AbilityId} after {held:0.00} s ({(full ? "full" : $"{fraction:P0}")}): {(hit ? "hit" : "missed")}.");
        }

        /// <summary>The player gave the charge up: nothing happens, the cast is closed.</summary>
        public static void CancelCharge(WorldSession p)
        {
            Charging c;
            lock (sync)
                if (!charging.Remove(p, out c))
                    return;
            Abilities.AnswerCharged(p, c.Attempt, 0);
        }

        /// <summary>A charge nobody let go of (lost packet, disconnect): released after the full charge plus 5 s.</summary>
        private static void ExpireCharges(DateTime now)
        {
            List<WorldSession> stale;
            lock (sync)
                stale = charging.Where(c => (now - c.Value.Start).TotalSeconds > MaxChargeSeconds + 5).Select(c => c.Key).ToList();
            foreach (WorldSession p in stale)
                if (p.Disconnected)
                    lock (sync) charging.Remove(p);
                else
                    FinishCharge(p, null);
        }

        /// <summary>Dev / test: an attack aimed at the nearest creature within 20 m (or straight ahead).</summary>
        public static bool DevAttack(WorldSession player, bool power, bool charged = false)
        {
            var weapon = Abilities.HeldWeapon(player);
            uint ability = WeaponAttacks.CastIds.FirstOrDefault(id => WeaponAttacks.KindOf(id) == (power ? WeaponAttacks.Kind.Power : WeaponAttacks.Kind.Basic));
            Vector3 eye = player.Position + new Vector3(0f, 1.6f, 0f);
            ulong near = Creatures.InRadius(player.ZoneId, player.Position, 20f).FirstOrDefault();
            Vector3 dir = near != 0 && Creatures.PositionOfGuid(near) is Vector3 at ? Vector3.Normalize(at + new Vector3(0f, 1f, 0f) - eye) : Facing(player);
            uint damage = (uint)Math.Round(Abilities.WeaponDamageFor(player) * WeaponAttacks.DamageMultiplier(ability));
            bool hit = Use(player, (eye, dir), eye + dir * 10f, weapon, ability, damage, charged ? true : null);
            log.Info($"{player.CharacterName} dev {(power ? "power" : charged ? "charged basic" : "basic")} attack ({ability}) with {weapon?.Id}: {(hit ? "hit" : "missed")}"
                + $" (nearest {(near == 0 ? "none" : $"{Vector3.Distance(Creatures.PositionOfGuid(near) ?? player.Position, player.Position):0.0} m")}).");
            return hit;
        }

        /// <summary>Dev: the combat states on a player and on the creatures around them.</summary>
        public static string DevStates(WorldSession player)
        {
            var parts = new List<string>();
            string Of(ulong g) => string.Join(",", Enum.GetValues<Status>().Where(s => Statuses.Has(g, s))
                .Select(s => Statuses.Stacks(g, s) > 1 ? $"{s}x{Statuses.Stacks(g, s)}" : s.ToString()));
            parts.Add($"self [{Of(player.PlayerGuid)}]");
            foreach (ulong c in Creatures.InRadius(player.ZoneId, player.Position, 25f))
                parts.Add($"0x{c:X} {Creatures.HealthFraction(c):P0} [{Of(c)}]");
            return string.Join("; ", parts);
        }

        private static bool Use(WorldSession player, (Vector3 Origin, Vector3 Direction)? ray, Vector3 fallbackAim,
            ItemDefinitionManager.ItemDefinition weapon, uint ability, uint damage, bool? fullyCharged = null)
        {
            Kit kit = KitOf(weapon);
            if (kit == null || Health.IsDead(player))
                return false;
            WeaponAttacks.Kind kind = WeaponAttacks.KindOf(ability);
            bool power = kind is WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo or WeaponAttacks.Kind.Special;
            Attack attack = power ? kit.Power : kit.Basic;
            Pace pace = PaceOf(player);
            DateTime now = DateTime.UtcNow;
            bool charged;
            lock (sync)
            {
                if (!power)
                {
                    pace.BasicRun = now - pace.LastBasic <= ComboWindow ? pace.BasicRun + 1 : 0;
                    pace.LastBasic = now;
                }
                // Charged attacks come back through FinishCharge with how long they were held; a dev or
                // uncharged use counts as fully charged for a Power Attack, every third Basic in a row otherwise.
                charged = attack.Charged && (fullyCharged ?? (power || pace.BasicRun % 3 == 2));
            }
            Vector3 dir = ray?.Direction ?? Facing(player);
            Vector3 flat = new(dir.X, 0f, dir.Z);
            flat = flat.LengthSquared() > 0.0001f ? Vector3.Normalize(flat) : Vector3.UnitZ;
            Vector3 aim = ray is var (o, d) ? AimPoint(player, o, d, attack.Range) : fallbackAim;
            var swing = new Swing
            {
                Player = player, Kit = kit, Attack = attack, Power = power, Charged = charged,
                Origin = player.Position, Forward = flat, Aim = aim,
                Damage = Boosted(player, kit, power, damage),
                Strength = (Statuses.Has(player.PlayerGuid, Status.Exalt) ? 1.25f : 1f)
                    * (Statuses.Has(player.PlayerGuid, Status.Rejuvenate) && Gear.Has(player, Gear.HighHeraldsRing) ? 1.25f : 1f),
                Ranged = attack.Shape is Shape.FirstHit or Shape.Pierce or Shape.AreaAtAim || attack.Range > 10f
            };
            if (attack.Shape == Shape.Block)
            {
                Statuses.Apply(player.PlayerGuid, Status.Block, player);
                return false;
            }
            if (attack == kit.Power && kit.Power.Name == 574881)
                Statuses.Apply(player.PlayerGuid, Status.Immune, player);   // Nightfall: immune while in the shadows
            Resolve(swing, ray);
            for (int t = 1; t < attack.Ticks; t++)
            {
                int tick = t;
                Later(attack.TickMs * tick, () =>
                {
                    if (player.Disconnected || Health.IsDead(player))
                        return;
                    var again = new Swing
                    {
                        Player = player, Kit = kit, Attack = attack, Power = power, Charged = charged,
                        Origin = player.Position, Forward = flat, Aim = aim, Damage = swing.Damage, Strength = swing.Strength,
                        Ranged = swing.Ranged
                    };
                    Resolve(again, ray);
                });
            }
            return swing.Hits.Count > 0;
        }

        /// <summary>Where the aim lands: the first creature on the ray, else Range along it (dropped to the ground).</summary>
        private static Vector3 AimPoint(WorldSession player, Vector3 origin, Vector3 dir, float range)
        {
            ulong first = Creatures.AlongRay(player.ZoneId, origin, dir, range + 5f, 1.5f).FirstOrDefault();
            if (first != 0 && Creatures.PositionOfGuid(first) is Vector3 p)
                return p;
            Vector3 point = origin + dir * Math.Min(range, 30f);
            return new Vector3(point.X, Math.Min(point.Y, player.Position.Y + 2f), point.Z);
        }

        /// <summary>Damage after the attacker's states and gear: Exalt +20 %, Infect +4 % a stack, Supercharge +30 % on Power, Flow at max +15 % (The Drowners Ring).</summary>
        private static uint Boosted(WorldSession p, Kit kit, bool power, uint damage)
        {
            ulong g = p.PlayerGuid;
            float f = 1f;
            if (Statuses.Has(g, Status.Exalt)) f *= 1.2f;
            int infect = Statuses.Stacks(g, Status.Infect);
            if (infect > 0) f *= 1f + infect * (Gear.Has(p, Gear.PoisonEtchedAmulet) ? 0.06f : 0.04f);
            if (power && Statuses.Has(g, Status.Supercharge)) f *= 1.3f;
            if (Gear.Has(p, Gear.DrownersRing) && Statuses.AtMax(g, Status.Flow)) f *= 1.15f;
            return (uint)Math.Round(damage * f);
        }

        /// <summary>Players near a point (and the attacker) — who sees an area effect.</summary>
        private static IEnumerable<WorldSession> AreaViewers(WorldSession p, Vector3 at)
            => PlayerManager.InZone(p.ZoneId).Where(v => Vector3.Distance(v.Position, at) < 150f).Append(p).Distinct();

        /// <summary>Finds who the swing hits and strikes them.</summary>
        private static void Resolve(Swing s, (Vector3 Origin, Vector3 Direction)? ray)
        {
            Attack a = s.Attack;
            WorldSession p = s.Player;
            List<ulong> targets = a.Shape switch
            {
                Shape.Cone => Creatures.InCone(p.ZoneId, p.Position, s.Forward, a.Range, a.Size),
                Shape.Around => Creatures.InRadius(p.ZoneId, p.Position, a.Range),
                Shape.AreaAtAim => Creatures.InRadius(p.ZoneId, s.Aim, a.Size),
                Shape.Pierce => Pierce(s, a.Range, a.Size),
                Shape.Path => Creatures.AlongRay(p.ZoneId, p.Position + new Vector3(0f, 1f, 0f), s.Forward, a.Range, a.Size),
                Shape.FirstHit => FirstHit(s, ray, a.Range, a.Size),
                _ => new List<ulong>()
            };
            if (a.MaxTargets > 0)
                targets = targets.Take(a.MaxTargets).ToList();
            if (a.Effect != 0)
            {
                // The attack's area effect where it lands: the aimed point, round the player, or along the path.
                Vector3 at = a.Shape switch
                {
                    Shape.AreaAtAim => s.Aim,
                    Shape.Around => p.Position,
                    Shape.Path => p.Position + s.Forward * (a.Range / 2f),
                    _ => p.Position + s.Forward * Math.Min(a.Range / 2f, 8f)
                };
                HitEffects.PlayAt(at, a.Effect, AreaViewers(p, at));
            }
            foreach (ulong t in targets)
                Strike(s, t, s.Damage, a.OnHit);
            a.After?.Invoke(s);
            if (s.Power)
            {
                Pace pace = PaceOf(p);
                lock (sync)
                {
                    pace.FirstPowerAfterFrenzy = false;
                    pace.FirstPowerWhileExalted = false;
                }
            }
        }

        private static List<ulong> FirstHit(Swing s, (Vector3 Origin, Vector3 Direction)? ray, float range, float splash)
        {
            Vector3 origin = ray?.Origin ?? s.Origin + new Vector3(0f, 1.5f, 0f);
            Vector3 dir = ray?.Direction ?? s.Forward;
            ulong first = Creatures.AlongRay(s.Player.ZoneId, origin, dir, range, 1.5f).FirstOrDefault();
            if (first == 0)
                return new List<ulong>();
            var hits = new List<ulong> { first };
            if (splash > 0 && Creatures.PositionOfGuid(first) is Vector3 at)
                hits.AddRange(Creatures.InRadius(s.Player.ZoneId, at, splash).Where(g => g != first));
            return hits;
        }

        private static List<ulong> Pierce(Swing s, float range, float width)
            => Creatures.AlongRay(s.Player.ZoneId, s.Origin + new Vector3(0f, 1f, 0f), s.Forward, range, width);

        /// <summary>
        /// One opponent hit: Erupt roll (Fire), the damage, then the attack's rule, the affinity's on-hit property and
        /// the armour's Maximum Affinity Effect.
        /// </summary>
        private static void Strike(Swing s, ulong target, uint damage, Action<Hit> then = null)
        {
            WorldSession p = s.Player;
            Affinity aff = s.Kit.Affinity;
            bool erupted = false;
            if (aff == Affinity.Fire)
            {
                Pace pace = PaceOf(p);
                bool guaranteed = Statuses.Has(p.PlayerGuid, Status.Eruption)
                    || (s.Power && pace.FirstPowerAfterFrenzy && Gear.Wields(p, Gear.WanderersBlazingGuise, aff));
                float chance = EruptChance * s.Strength * (Gear.Has(p, Gear.MoltenIronOrb) ? 1.6f : 1f);
                erupted = guaranteed || Random.Shared.NextDouble() < chance;
                if (erupted)
                {
                    damage = (uint)(damage * (Gear.Has(p, Gear.VolcanicBracer) ? 2.5f : 2f));
                    s.Erupted.Add(target);
                    HitEffects.PlayOn(target, 11059, Statuses.Viewers(target, p));   // PFX_Combat_Fire_Generic_Explosion_02m
                    if (Gear.Has(p, Gear.BandOfFlame))
                        Statuses.Apply(target, Status.Burn, p, power: s.Damage * BurnPerSecond);
                    if (guaranteed && s.Power && Gear.Wields(p, Gear.WanderersBlazingGuise, aff) && Creatures.PositionOfGuid(target) is Vector3 at)
                        Field(s, at, 3f, 4, null, 0.2f, effect: 12729);                // the blazing cyclone
                }
            }
            if (Statuses.Has(target, Status.Weakened) == false && aff == Affinity.Affliction && Gear.Has(p, Gear.BeltOfVirulence)
                && Statuses.AtMax(p.PlayerGuid, Status.Infect))
                Statuses.Apply(target, Status.Weakened, p);
            if (aff == Affinity.Shadow && Gear.Has(p, Gear.SpiteEtchedBracer))
                Statuses.Apply(target, Status.Siphoned, p);
            float lifeBefore = Creatures.HealthFraction(target);
            var (dealt, killed) = Creatures.Damage(p, target, damage, s.Ranged);
            if (dealt == 0 && !killed)
                return;
            int inARow;
            Pace pc = PaceOf(p);
            lock (sync)
            {
                DateTime now = DateTime.UtcNow;
                inARow = pc.Streak.TryGetValue(target, out var st) && now - st.At <= ComboWindow ? st.Count + 1 : 1;
                pc.Streak[target] = (inARow, now);
                foreach (ulong old in pc.Streak.Where(x => now - x.Value.At > ComboWindow).Select(x => x.Key).ToList())
                    pc.Streak.Remove(old);
            }
            s.Hits.Add(target);
            var hit = new Hit { Swing = s, Target = target, Dealt = dealt, Killed = killed, Erupted = erupted, InARow = inARow };
            if (!killed)
            {
                then?.Invoke(hit);
                Gear.OnHit(hit, lifeBefore);
            }
            Property(hit);
        }

        /// <summary>The weapon affinity's own on-hit property (wiki: Flow, Energize, Regrowth, Infect, Siphon, Rejuvenate).</summary>
        private static void Property(Hit h)
        {
            Swing s = h.Swing;
            WorldSession p = s.Player;
            switch (s.Kit.Affinity)
            {
                case Affinity.Water:
                    int flow = Gear.Has(p, Gear.RingOfTheRisingTide) ? 2 : 1;
                    Statuses.Apply(p.PlayerGuid, Status.Flow, p, stacks: flow);
                    WeaponAttacks.GainEnergy(p, FlowEnergy * flow * (Gear.Has(p, Gear.SigilOfPiercingFloods) ? 1.5f : 1f));
                    break;
                case Affinity.Lightning:
                    if (s.Power)
                        Statuses.Apply(p.PlayerGuid, Status.Energize, p,
                            seconds: Statuses.Defs[Status.Energize].Seconds * (Gear.Has(p, Gear.EarringsOfEnduringEnergy) ? 1.5f : 1f));
                    break;
                case Affinity.Earth:
                    float chance = RegrowthChance * s.Strength * (Gear.Has(p, Gear.BracerOfSurvival) ? 1.75f : 1f);
                    if (Random.Shared.NextDouble() < chance)
                    {
                        Health.Heal(p, (uint)(Health.MaxHealth * RegrowthShare * (Gear.Has(p, Gear.DefendersWristguard) ? 1.6f : 1f)));
                        HitEffects.PlayOn(p.PlayerGuid, 11668, Statuses.Viewers(p.PlayerGuid));
                        if (Gear.Has(p, Gear.RingOfIndifference))
                            Statuses.Apply(p.PlayerGuid, Status.Bulwark, p);
                    }
                    break;
                case Affinity.Affliction:
                    Statuses.Apply(p.PlayerGuid, Status.Infect, p, stacks: Gear.Has(p, Gear.DefilersPiercings) ? 2 : 1);
                    break;
                case Affinity.Shadow:
                    float share = s.Power ? SiphonShare * (Gear.Has(p, Gear.ShadowforgedBand) ? 1.6f : 1f)
                        : Gear.Has(p, Gear.EarringsOfDarkestNight) ? SiphonShare * 0.5f : 0f;
                    if (Recently(p, "revenge", TimeSpan.FromSeconds(4)) && Gear.Wields(p, Gear.PlateOfUnrelentingVengeance, Affinity.Shadow))
                        share *= 2f;
                    if (share > 0)
                        Health.Heal(p, (uint)Math.Max(1, h.Dealt * share * s.Strength));
                    break;
                case Affinity.Radiance:
                    if (Random.Shared.NextDouble() < RejuvenateChance * s.Strength)
                        Statuses.Apply(p.PlayerGuid, Status.Rejuvenate, p,
                            power: Health.MaxHealth * RejuvenateShare * (Gear.Has(p, Gear.BlessedBrooch) ? 2f : 1f),
                            seconds: Statuses.Defs[Status.Rejuvenate].Seconds * (Gear.Has(p, Gear.RadiantBulwark) ? 1.6f : 1f));
                    break;
            }
        }

        // ---- Rule helpers --------------------------------------------------------------------------------------------

        private static float Defs(Status s) => Statuses.Defs[s].Seconds;

        /// <summary>Puts an opponent state on the hit target (durations scaled by the swing's strength).</summary>
        public static void Apply(Hit h, Status st)
        {
            float power = st switch
            {
                Status.Burn => h.Swing.Damage * BurnPerSecond,
                Status.Rot => h.Swing.Damage * RotPerSecond,
                _ => 0f
            };
            if (st == Status.Condemn && Gear.Wields(h.Swing.Player, Gear.HauberkOfPiercingJudgment, Affinity.Shadow))
                power = h.Swing.Damage * CondemnPerSecond;
            Statuses.Apply(h.Target, st, h.Swing.Player, power: power, seconds: Defs(st) * h.Swing.Strength);
            Gear.OnApplied(h, st);
        }

        private static void Burn(Hit h) => Apply(h, Status.Burn);
        private static void Rot(Hit h) => Apply(h, Status.Rot);
        private static void Condemn(Hit h) => Apply(h, Status.Condemn);

        /// <summary>A state on the attacker (scaled by strength).</summary>
        public static void Grant(Swing s, Status st)
        {
            float power = st == Status.Illuminate ? Health.MaxHealth * IlluminateShare * s.Strength : 0f;
            Statuses.Apply(s.Player.PlayerGuid, st, s.Player, power: power, seconds: Defs(st) * s.Strength);
        }

        /// <summary>
        /// Surge: half of the player's stored energy is spent as bonus damage on the target (Raiment of Frozen Sorrow:
        /// double against the Frozen, and a small ice storm stays behind).
        /// </summary>
        public static void Surge(Hit h)
        {
            WorldSession p = h.Swing.Player;
            double spent = WeaponAttacks.SpendHalfEnergy(p);
            if (spent < 1)
                return;
            bool frozen = Statuses.Has(h.Target, Status.Freeze);
            bool raiment = frozen && Gear.Wields(p, Gear.RaimentOfFrozenSorrow, Affinity.Water);
            uint bonus = (uint)(spent * (raiment ? 2.0 : 1.0));
            HitEffects.PlayOn(h.Target, 11533, Statuses.Viewers(h.Target, p));   // PFX_Combat_Ice_Staff_SpecialAttack_Explosion
            Creatures.Damage(p, h.Target, bonus, true, impact: false);
            if (raiment && Creatures.PositionOfGuid(h.Target) is Vector3 at)
                Field(h.Swing, at, 4f, 4, null, 0.1f, effect: 11528);
        }

        /// <summary>Bonus damage (share of the swing) to everyone within <paramref name="radius"/> of each target hit; returns who was hit.</summary>
        private static List<ulong> Chain(Swing s, float radius, float share)
        {
            var near = new HashSet<ulong>();
            foreach (ulong t in s.Hits.ToList())
                if (Creatures.PositionOfGuid(t) is Vector3 at)
                    foreach (ulong n in Creatures.InRadius(s.Player.ZoneId, at, radius))
                        near.Add(n);
            foreach (ulong n in near)
                Creatures.Damage(s.Player, n, (uint)(s.Damage * share), s.Ranged);
            return near.ToList();
        }

        /// <summary>Was this the player's second (or later) hit of the same kind in a row on someone?</summary>
        private static bool SecondInARow(Swing s)
        {
            Pace pace = PaceOf(s.Player);
            lock (sync)
                return s.Hits.Any(t => pace.Streak.TryGetValue(t, out var st) && st.Count >= 2);
        }

        private static void Mark(WorldSession p, string what)
        {
            Pace pace = PaceOf(p);
            lock (sync)
                pace.Marks[what] = DateTime.UtcNow;
        }

        private static bool Recently(WorldSession p, string what, TimeSpan within)
        {
            Pace pace = PaceOf(p);
            lock (sync)
                return pace.Marks.TryGetValue(what, out DateTime at) && DateTime.UtcNow - at <= within;
        }

        /// <summary>Exalt for the player and every other player within <paramref name="radius"/> of a point.</summary>
        private static void ExaltAllies(Swing s, Vector3 at, float radius)
        {
            foreach (WorldSession ally in PlayerManager.InZone(s.Player.ZoneId).Where(a => Vector3.Distance(a.Position, at) <= radius && !Health.IsDead(a)).Append(s.Player).Distinct())
                Statuses.Apply(ally.PlayerGuid, Status.Exalt, s.Player, seconds: Defs(Status.Exalt) * s.Strength);
        }

        /// <summary>Consecrate's channel: the caster and allies within 8 m take less damage while it lasts.</summary>
        private static void Consecrate(Swing s)
        {
            foreach (WorldSession ally in PlayerManager.InZone(s.Player.ZoneId).Where(a => Vector3.Distance(a.Position, s.Player.Position) <= 8f).Append(s.Player).Distinct())
                Statuses.Apply(ally.PlayerGuid, Status.Consecrated, s.Player, seconds: 0.6f);
            Mark(s.Player, "consecrating");
        }

        // ---- Lingering areas (Blizzard, molten field, cyclone, ice storm) -------------------------------------------

        private sealed class Area
        {
            public WorldSession Source;
            public uint Zone;
            public Vector3 At;
            public float Radius;
            public int TicksLeft;
            public Status? Applies;
            public uint PerTick;
            public bool EndFreeze;
            public DateTime Next;
            public float Strength;
            public uint Damage;
        }

        private static readonly List<Area> areas = new();

        /// <summary>A lingering area: every second for <paramref name="seconds"/> it damages (share of the swing) and/or applies a state to everyone inside.</summary>
        private static void Field(Swing s, Vector3 at, float radius, int seconds, Status? applies, float share, bool endFreeze = false,
            uint effect = 0)
        {
            if (effect != 0)
                HitEffects.PlayAt(at, effect, AreaViewers(s.Player, at), (uint)(seconds * 1000));
            lock (sync)
                areas.Add(new Area
                {
                    Source = s.Player, Zone = s.Player.ZoneId, At = at, Radius = radius, TicksLeft = seconds, Applies = applies,
                    PerTick = (uint)(s.Damage * share), EndFreeze = endFreeze, Next = DateTime.UtcNow, Strength = s.Strength,
                    Damage = s.Damage
                });
        }

        private static readonly List<(DateTime At, Action Do)> later = new();

        /// <summary>Runs something after a delay on the server tick (channel hits).</summary>
        private static void Later(int ms, Action action)
        {
            lock (sync)
                later.Add((DateTime.UtcNow.AddMilliseconds(ms), action));
        }

        /// <summary>From <see cref="Statuses.Update"/>: lingering areas and channel hits.</summary>
        public static void Update(DateTime now)
        {
            ExpireCharges(now);
            List<Action> due;
            List<Area> ticking;
            lock (sync)
            {
                due = later.Where(l => l.At <= now).Select(l => l.Do).ToList();
                later.RemoveAll(l => l.At <= now);
                ticking = areas.Where(a => a.Next <= now).ToList();
                foreach (Area a in ticking)
                {
                    a.Next = now.AddSeconds(1);
                    a.TicksLeft--;
                }
                areas.RemoveAll(a => a.TicksLeft < 0);
            }
            foreach (Action a in due)
            {
                try { a(); }
                catch (Exception e) { log.Warn(e, "Delayed weapon hit failed."); }
            }
            foreach (Area a in ticking)
            {
                if (a.Source.Disconnected)
                    continue;
                foreach (ulong t in Creatures.InRadius(a.Zone, a.At, a.Radius))
                {
                    if (a.PerTick > 0 && a.TicksLeft >= 0)
                        Creatures.Damage(a.Source, t, a.PerTick, true, impact: false);
                    if (a.Applies is Status st && a.TicksLeft >= 0)
                        Statuses.Apply(t, st, a.Source, power: st == Status.Burn ? a.Damage * BurnPerSecond : 0f);
                    if (a.EndFreeze && a.TicksLeft == 0)
                        Statuses.Apply(t, Status.Freeze, a.Source, seconds: Defs(Status.Freeze) * a.Strength);
                }
            }
        }

        // ---- Taking hits ---------------------------------------------------------------------------------------------

        /// <summary>
        /// A creature's attack on a player, before it lands: Immune (Nightfall), a block from the front (stops it and
        /// counters with a shield bash), Consecrate, Harden, Bulwark, the armour's defensive Maximum Affinity Effects
        /// (less damage, revenge damage, states on attackers) and Illuminate's shield. Returns the damage that gets through.
        /// </summary>
        public static uint Incoming(WorldSession victim, uint amount, ulong attacker, Vector3 attackerAt)
        {
            ulong g = victim.PlayerGuid;
            if (Statuses.Has(g, Status.Immune))
                return 0;
            if (Statuses.Has(g, Status.Block))
            {
                Vector3 to = attackerAt - victim.Position;
                to.Y = 0;
                // "the next attack that hits you head on": the attacker is in the half the player faces.
                if (to.LengthSquared() < 0.01f || Vector3.Dot(Vector3.Normalize(to), Facing(victim)) > 0.2f)
                {
                    Statuses.Remove(g, Status.Block);
                    ShieldBash(victim);
                    return 0;
                }
            }
            float f = 1f;
            if (Statuses.Has(g, Status.Consecrated))
            {
                f *= 0.7f;
                if (!Recently(victim, "consecrate-shield", TimeSpan.FromSeconds(3)))
                {
                    Mark(victim, "consecrate-shield");
                    Statuses.Apply(g, Status.Illuminate, victim, power: Health.MaxHealth * IlluminateShare);
                }
            }
            if (Statuses.Has(g, Status.Bulwark)) f *= 0.8f;
            if (Statuses.Has(g, Status.Harden))
            {
                Statuses.Remove(g, Status.Harden);
                f *= Gear.Wields(victim, Gear.UnflinchingEmbodimentOfStone, Affinity.Earth) ? 0f : 0.5f;
            }
            f *= Gear.OnHurt(victim, attacker);
            uint through = (uint)Math.Round(amount * f);
            Mark(victim, "revenge");
            return Statuses.Absorb(g, through);
        }

        /// <summary>The way the player faces (their last movement rotation, flattened), else +Z.</summary>
        private static Vector3 Facing(WorldSession p)
        {
            Vector3 f = Vector3.Transform(Vector3.UnitZ, p.Facing);
            f.Y = 0f;
            return f.LengthSquared() > 0.01f ? Vector3.Normalize(f) : Vector3.UnitZ;
        }

        /// <summary>A block caught a hit: shield bash on everyone directly in front, with the kit's counter state.</summary>
        private static void ShieldBash(WorldSession p)
        {
            var weapon = Abilities.HeldWeapon(p);
            Kit kit = KitOf(weapon);
            if (kit == null)
                return;
            var s = new Swing
            {
                Player = p, Kit = kit, Attack = kit.Power, Power = true, Charged = true, Origin = p.Position,
                Forward = Facing(p), Aim = p.Position, Damage = Abilities.WeaponDamageFor(p)
            };
            HitEffects.PlayOn(p.PlayerGuid, 11356, Statuses.Viewers(p.PlayerGuid));   // PFX_Combat_Physical_Sword_SpecialAttack_Release
            foreach (ulong t in Creatures.InCone(p.ZoneId, p.Position, s.Forward, kit.Power.Range, kit.Power.Size))
                Strike(s, t, s.Damage, h => { if (kit.Power.Counter is Status st) Apply(h, st); });
        }

        /// <summary>Revenge damage (share of the player's weapon damage) to an attacking creature.</summary>
        public static void Revenge(WorldSession p, ulong attacker, float share = RevengeShare)
        {
            if (attacker != 0)
                Creatures.Damage(p, attacker, (uint)Math.Max(1, Abilities.WeaponDamageFor(p) * share), false, impact: true);
        }

        /// <summary>Damage and a state on every creature within <paramref name="radius"/> of a point (armour bursts, shockwaves).</summary>
        public static void Burst(WorldSession p, Vector3 at, float radius, float share, Status? st, uint effect)
        {
            var kit = KitOf(Abilities.HeldWeapon(p));
            var s = new Swing
            {
                Player = p, Kit = kit ?? new Kit(Affinity.None, null, null, 0), Attack = kit?.Basic, Origin = p.Position,
                Forward = Facing(p), Aim = at, Damage = (uint)(Abilities.WeaponDamageFor(p) * share)
            };
            foreach (ulong t in Creatures.InRadius(p.ZoneId, at, radius))
            {
                if (effect != 0)
                    HitEffects.PlayOn(t, effect, Statuses.Viewers(t, p));
                var (dealt, killed) = Creatures.Damage(p, t, s.Damage, true, impact: false);
                if (st is Status state && !killed)
                    Statuses.Apply(t, state, p, power: state == Status.Burn ? s.Damage * BurnPerSecond
                        : state == Status.Rot ? s.Damage * RotPerSecond : 0f);
            }
        }

        // ---- States changing the attacks' caster info ----------------------------------------------------------------

        /// <summary>
        /// A player's state started or ended. Frenzy and Supercharge change the attacks' cast time / Energy cost, which
        /// the client reads from the caster info, so it is sent again; Frenzy and Exalt arm the "first Power Attack
        /// after …" armour effects.
        /// </summary>
        public static void StatusChanged(WorldSession p, Status s, bool started)
        {
            if (started && s == Status.Frenzy)
                lock (sync) PaceOf(p).FirstPowerAfterFrenzy = true;
            if (started && s == Status.Exalt)
                lock (sync) PaceOf(p).FirstPowerWhileExalted = true;
            if (s is Status.Frenzy or Status.Supercharge or Status.Energize)
                SendAttackInfo(p);
        }

        /// <summary>Is this the player's first Power Attack since Exalt started (Lightbringer's Oath)?</summary>
        public static bool FirstPowerWhileExalted(WorldSession p) { lock (sync) return PaceOf(p).FirstPowerWhileExalted; }

        /// <summary>Energy cost factor for a cast: Supercharge ×1.5 on Power Attacks, Forsaken Band of Power −25 % at max Energize.</summary>
        public static double CostFactor(WorldSession p, uint ability)
        {
            WeaponAttacks.Kind k = WeaponAttacks.KindOf(ability);
            bool power = k is WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo or WeaponAttacks.Kind.Special;
            double f = 1;
            if (power && Statuses.Has(p.PlayerGuid, Status.Supercharge)) f *= 1.5;
            if (power && Gear.Has(p, Gear.ForsakenBandOfPower) && Statuses.AtMax(p.PlayerGuid, Status.Energize)) f *= 0.75;
            return f;
        }

        /// <summary>
        /// Fills a weapon attack's caster info from the held weapon: the attack's own name and description (Basic / Power
        /// by the cast's button), cast time (Frenzy: 40 % faster) and Energy cost (<see cref="CostFactor"/>).
        /// </summary>
        public static void Describe(WorldSession p, ItemDefinitionManager.ItemDefinition weapon, uint ability, AbilityCasterInfoReply reply)
        {
            Kit kit = KitOf(weapon);
            if (kit == null)
                return;
            WeaponAttacks.Kind k = WeaponAttacks.KindOf(ability);
            Attack a = k is WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo or WeaponAttacks.Kind.Special ? kit.Power : kit.Basic;
            reply.NameId = a.Name;
            reply.DescriptionId = a.Description;
            if (Statuses.Has(p.PlayerGuid, Status.Frenzy))
            {
                reply.CastTime *= 0.6f;
                reply.RecoveryTime *= 0.6f;
            }
            if (reply.CostAmount > 0)
                reply.CostAmount = (uint)Math.Round(reply.CostAmount * CostFactor(p, ability));
            reply.MaxRange = Math.Max(reply.MaxRange, a.Shape is Shape.Around or Shape.Block ? 4f : Math.Min(a.Range, 40f));
            if (a.Charged)
            {
                reply.KindHash = ChargedKind;
                reply.MaxCharge = MaxChargeSeconds;
                reply.MinCharge = MinChargeSeconds;
            }
        }

        /// <summary>Sends the caster info of every weapon cast id again (weapon changed, Frenzy / Supercharge started or ended).</summary>
        public static void SendAttackInfo(WorldSession p)
        {
            var weapon = Abilities.HeldWeapon(p);
            if (weapon == null)
                return;
            foreach (uint id in WeaponAttacks.CastIds)
                p.EnqueueMessage(Network.Handlers.MiscHandler.WeaponAttackInfo(p, weapon, id));
            lock (sync)
                PaceOf(p).InfoFor = weapon;
        }

        /// <summary>The held weapon changed since the attack info was last sent?</summary>
        public static void CheckWeapon(WorldSession p)
        {
            var weapon = Abilities.HeldWeapon(p);
            ItemDefinitionManager.ItemDefinition last;
            lock (sync)
                last = PaceOf(p).InfoFor;
            if (weapon != null && weapon != last)
                SendAttackInfo(p);
        }

        public static void Forget(WorldSession p)
        {
            lock (sync)
            {
                paces.Remove(p);
                areas.RemoveAll(a => a.Source == p);
                charging.Remove(p);
            }
            Statuses.Forget(p);
        }
    }
}
