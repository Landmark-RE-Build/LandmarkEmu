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
    /// held weapon's attack; this class does the same from <see cref="Kits"/>. A kit may also have a Special attack
    /// (E, input trigger 11): only the four never-live weapons have an E cast in the client's input table (OPEN-LEADS
    /// L163); Desperado's Deadly Arsenal has three named attacks for its three casts (see <see cref="Kit"/>).
    /// <para>
    /// What is the client's: names, descriptions, icons, state effects and impacts. What is ours (the ability data was
    /// server-side and is lost): shapes (reach, arc), damage split, durations and chances — kept as named numbers here
    /// and in <see cref="Statuses.Defs"/>. Charged attacks ("Charge a powerful …") get the charged kind in their caster
    /// info, so the client charges while the button is held and sends FinishChargedCast on release; the server times the
    /// hold (damage 60-100 %, "fully charged" from <see cref="MaxChargeSeconds"/>). Channels are a few timed hits. Frenzy ("attacks faster") and Supercharge ("cost
    /// more energy") change the attacks' cast time and Energy cost in the caster info the client paces itself by.
    /// </para>
    /// </summary>
    public static partial class WeaponCombat
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
        /// <paramref name="ChargeSeconds"/>: a full charge for this attack (0 = <see cref="MaxChargeSeconds"/>);
        /// <paramref name="Immune"/>: "become immune to incoming attacks" while it runs (Nightfall, Terminal Voltage);
        /// <paramref name="Series"/>: "a series of … attacks, dealing greater damage with each strike" (Rolling Thunder:
        /// each Power Attack in a row deals <see cref="SeriesStep"/> more, the third is the finisher).
        /// <paramref name="DamageRow"/>: the attack hits for the client's AbilityParameterDefinitions row of that id
        /// (<see cref="RowDamage"/>: the same number its description's [*AbilityParamN*] token shows) instead of the
        /// base weapon damage × the kind's multiplier; 0 = the usual damage. <paramref name="Primer"/>: a state on the
        /// player that makes this use the attack's stronger form (<see cref="Swing.Primed"/>; Dirty Fighting), whose hit
        /// is <paramref name="PrimedRow"/> when that is another row (0 = <paramref name="DamageRow"/>).
        /// </summary>
        public sealed record Attack(uint Name, uint Description, Shape Shape, float Range, float Size, bool Charged = false,
            int Ticks = 1, int TickMs = 400, Action<Hit> OnHit = null, Action<Swing> After = null, Status? Counter = null,
            int MaxTargets = 0, uint Effect = 0, float ChargeSeconds = 0f, bool Immune = false, bool Series = false,
            uint DamageRow = 0, Status? Primer = null, uint PrimedRow = 0);

        /// <summary>
        /// A weapon's affinity and its attacks: Basic (left mouse), Power (right mouse) and, for a weapon whose input
        /// group has an E cast (trigger 11), an optional <paramref name="Special"/>. Without one, an E cast runs the
        /// Power attack (<see cref="AttackFor"/>).
        /// </summary>
        public sealed record Kit(Affinity Affinity, Attack Basic, Attack Power, uint Icon, Attack Special = null);

        /// <summary>
        /// The attack of a kit that a cast kind runs: Special (E) the kit's own Special when it has one, else its Power
        /// attack; Power and its combo stages the Power attack; everything else the Basic attack.
        /// </summary>
        public static Attack AttackFor(Kit kit, WeaponAttacks.Kind kind) => kind switch
        {
            WeaponAttacks.Kind.Special => kit.Special ?? kit.Power,
            WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo => kit.Power,
            _ => kit.Basic
        };

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
            /// <summary>The ProjectileDefinitions row drawn for a ranged weapon's shot (0 = none; <see cref="ProjectileRow"/>).</summary>
            public uint Projectile;
            /// <summary>For a Power Attack: how many came before it in a row (0 = first; WeaponAttacks.PowerRunOf).</summary>
            public int Run;
            /// <summary>A channel ran out of Energy: its remaining hits are dropped.</summary>
            public bool Stopped;
            /// <summary>The player had the attack's <see cref="Attack.Primer"/> state when it started (Dirty Fighting).</summary>
            public bool Primed;
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

        /// <summary>
        /// Surge (26 Apr 2016 notes, Supercooled Power set, "Weapon Passive: Surge"): a base damage whatever the energy
        /// spent, only half the stored energy used, more damage per energy but less in total at high energy. Base =
        /// this share of the swing's
        /// damage, plus <see cref="SurgePerEnergy"/> per Energy spent (half the store): at the full 456 that is 342 +
        /// base, below the old all-energy-at-1-each 456; at low reserves it is more. [I] sizes ours.
        /// </summary>
        private const float SurgeBaseShare = 0.3f, SurgePerEnergy = 1.5f;

        /// <summary>Rolling Thunder: each leap in a row deals this much more (×1, ×1.25, ×1.5) [I].</summary>
        private const float SeriesStep = 0.25f;

        /// <summary> 25 May 2016 notes: melee weapons give their wielders a little more passive damage resistance. Our
        /// server had none before, so the melee wielder's resistance is the whole "slightly more": damage taken × 0.9
        /// [I] while a melee weapon (no ranged attack shape, see <see cref="IsMelee"/>) is held.
        /// </summary>
        private const float MeleeResistance = 0.9f;

        /// <summary>
        /// A creature attack counts as a projectile when the attacker is further than this from the player (creature
        /// attacks carry no "ranged" flag here; melee reach is a few metres) [I]. Used by Molten Rebuke (11 May 2016).
        /// </summary>
        private const float ProjectileDistance = 6f;

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
            // Supercooled Power Cannon. 26 Apr 2016 (Tidal Burst): hitting a frozen target triggers surge, and the
            // minimum and maximum charge times are longer (1.5 s full charge [I]); the client text 574718 adds "When
            // fully
            // charged this attack passes through multiple opponents, granting Flow for each opponent hit". Torrent:
            // freezes faster (2 hits in a row instead of 3 [I]), its projectiles pass through several opponents (the
            // cone hits everyone in front), and it no longer triggers surge.
            Add(new Kit(Affinity.Water,
                new Attack(574717, 574718, Shape.FirstHit, 35f, 0f, Charged: true, ChargeSeconds: 1.5f, // Tidal Burst
                    OnHit: TidalBurstHit,
                    After: s =>
                    {
                        if (!s.Charged)
                            return;
                        foreach (ulong t in Pierce(s, 35f, 1.5f).Where(t => !s.Hits.Contains(t)))
                            Strike(s, t, s.Damage, then: TidalBurstHit);
                    }),
                new Attack(574719, 574720, Shape.Cone, 15f, 25f, Ticks: 4, TickMs: 300,              // Torrent
                    OnHit: h => { if (h.InARow >= 2) Apply(h, Status.Freeze); }), 12207), 3036);
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
                // Terminal Voltage: client text 574555 (the wiki's tooltip for this attack): "Transform into pure energy
                // and become immune to incoming attacks as you leap towards opponents in front of you, dealing damage in
                // a small area on impact. If you are Supercharged all opponents are also Electrocuted." — a leap, not a
                // charged attack (it was a charged cone that Electrocuted when fully charged).
                new Attack(574451, 574555, Shape.Path, 8f, 3f, Immune: true,                          // Terminal Voltage
                    OnHit: h => { if (Statuses.Has(h.Swing.Player.PlayerGuid, Status.Supercharge)) Apply(h, Status.Electrocute); }),
                12209), 3763, 1503);
            Add(new Kit(Affinity.Lightning,
                new Attack(574721, 574722, Shape.FirstHit, 35f, 0f, Charged: true,                    // Feedback
                    OnHit: h => { if (h.Swing.Charged) Apply(h, Status.Electrocute); }),
                // Shock and Awe: 25 May 2016, the first shot reaches as far as the later ones, but its area is
                // narrower: 15 m (the volley's reach) and ±30° instead of 10 m / ±50° [I].
                new Attack(574723, 574724, Shape.Cone, 15f, 30f,                                      // Shock and Awe
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Electrocute)) Grant(h.Swing, Status.Supercharge); }), 12209), 3033);
            Add(new Kit(Affinity.Lightning,
                new Attack(576330, 576331, Shape.FirstHit, 35f, 0f, Charged: true,                    // Crackling Orb
                    OnHit: h => { if (h.Swing.Charged) Grant(h.Swing, Status.Supercharge); }),
                new Attack(576332, 576333, Shape.AreaAtAim, 25f, 5f, Effect: 12784,                   // Static Bomb
                    OnHit: h => { if (Statuses.Has(h.Swing.Player.PlayerGuid, Status.Supercharge)) Apply(h, Status.Electrocute); }), 12209), 4107);
            Add(new Kit(Affinity.Lightning,
                new Attack(576428, 576436, Shape.Cone, 5f, 60f, Charged: true,                        // Overcharge
                    OnHit: h => { if (h.Swing.Charged) Grant(h.Swing, Status.Supercharge); }),
                // Rolling Thunder (25 May 2016 notes = client text 576437): "Execute a series of leaping attacks,
                // dealing greater damage to all opponents in front of you with each strike. The third attack in a row
                // also briefly makes you immune to incoming attacks and Electrocutes all opponents hit." (It used the
                // Supercharged rule of Terminal Voltage before.)
                new Attack(576424, 576437, Shape.Path, 10f, 3f, Effect: 12782, Series: true,          // Rolling Thunder
                    OnHit: h => { if (h.Swing.Run % 3 == 2) Apply(h, Status.Electrocute); }), 12209), 3027);

            // Earth — Harden, Regrowth, Crush.
            Add(new Kit(Affinity.Earth,
                new Attack(574452, 574453, Shape.Cone, 5f, 60f,                                       // Assault
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Crush)) Grant(h.Swing, Status.Harden); }),
                new Attack(574454, 574455, Shape.Path, 8f, 3f, Charged: true,                         // Overrun
                    OnHit: h => { if (h.Swing.Charged && h.Swing.Hits.Count == 1) Apply(h, Status.Crush); }), 12213), 3764, 1293);
            Add(new Kit(Affinity.Earth,
                new Attack(574876, 574877, Shape.Cone, 20f, 20f, Ticks: 3, TickMs: 250,               // Unload
                    OnHit: h => Apply(h, Status.Exposed)),
                // Trick Shot, 26 Apr 2016: its projectiles pass through several opponents — no 3-target cap any more;
                // it crushes opponents Unload hit recently, and a second use after a hit grants Harden (kept below).
                new Attack(574878, 574879, Shape.Around, 12f, 0f,                                     // Trick Shot
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
                // Contaminate, 25 May 2016: a larger area — 4.5 m round the impact, was 3 m [I].
                new Attack(574458, 574459, Shape.FirstHit, 12f, 4.5f,                                 // Contaminate
                    OnHit: h => Rot(h)), 12205), 3765, 1517);
            Add(new Kit(Affinity.Affliction,
                new Attack(576304, 576305, Shape.Cone, 5f, 60f,                                       // Enfeeble
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Rot)) Apply(h, Status.Cripple); }),
                new Attack(576306, 576307, Shape.Block, 4f, 60f, Counter: Status.Rot), 12205), 4091); // Corrupting Revenge
            // Diabolical Scientist's Raygun. The two attacks' rules were swapped: the wiki page gives "Ooze Bolt: Lob a
            // bolt of contaminated ooze … explodes on impact dealing damage and inflicting Rot on all opponents in the
            // area" (client text 573035) and "Sludge Storm: Charge a powerful attack … When fully charged this attack
            // passes through multiple opponents, dealing damage and inflicting Cripple on each one" (573071, 76 Energy),
            // and the 25 May 2016 notes agree: the basic shots fly flatter and hit a larger area (4 m [I]); the power
            // attack lets you move a little faster while charging but roots you briefly after the shot (movement is the
            // client's).
            Add(new Kit(Affinity.Affliction,
                new Attack(573028, 573035, Shape.AreaAtAim, 30f, 4f, OnHit: h => Rot(h), Effect: 11638),  // Ooze Bolt
                new Attack(573069, 573071, Shape.FirstHit, 35f, 0f, Charged: true,                    // Sludge Storm
                    After: s =>
                    {
                        if (!s.Charged)
                            return;
                        foreach (ulong t in Pierce(s, 35f, 1.5f).Where(t => !s.Hits.Contains(t)))
                            Strike(s, t, s.Damage, then: h => Apply(h, Status.Cripple));
                        foreach (ulong t in s.Hits.ToList())
                            Statuses.Apply(t, Status.Cripple, s.Player, seconds: Defs(Status.Cripple) * s.Strength);
                    }), 12205), 3032);

            // Shadow — Siphon, Torment, Condemn.
            Add(new Kit(Affinity.Shadow,
                new Attack(574460, 574461, Shape.Cone, 6f, 50f, Charged: true,                        // Shadow Rend
                    OnHit: h => { if (h.Swing.Charged) Condemn(h); }),
                new Attack(574462, 574463, Shape.Cone, 6f, 50f, Ticks: 4, TickMs: 400,                // Dark Embrace
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Condemn)) Apply(h, Status.Torment); }), 12217), 3766, 1528);
            Add(new Kit(Affinity.Shadow,
                new Attack(574880, 574882, Shape.Path, 8f, 3f,                                        // Hateseeker
                    OnHit: h => { if (Statuses.Has(h.Target, Status.Torment)) Condemn(h); }),
                new Attack(574881, 574883, Shape.Cone, 6f, 60f, Immune: true,                         // Nightfall
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
                // Cascade: Exalt round the first opponent hit. A hit that killed leaves no position (PositionOfGuid is
                // null for the dead), so a Cascade that killed everything it hit falls back to the aimed point (AimPoint:
                // the first creature on the ray). Before, .First() threw there, the exception left Abilities.HandleCast
                // before the cast's answer, and the client stayed "casting" (bughunt-combat.md B1).
                new Attack(574466, 574467, Shape.Pierce, 20f, 2.5f,                                   // Cascade
                    After: s =>
                    {
                        if (s.Hits.Count > 0)
                            ExaltAllies(s, s.Hits.Select(Creatures.PositionOfGuid).OfType<Vector3>().DefaultIfEmpty(s.Aim).First(), 10f);
                    }), 12215), 3767, 1529);
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

            // Desperado's Deadly Arsenal (OPEN-LEADS L163; spec B4 in trace/0695/agent-leads4.md): a revolver and a shank
            // (model Wield_Gun1H_Gunslinger_…, secondary Wield_Shank_Bottle_…), Affliction (description 574440). Its input
            // group 78 has three casts, LMB 793 / RMB 794 / E 795 (InputActionGroupEntries 345-347) [P], and the client has
            // three named attacks for it whose texts match a revolver shot, a shank stab and a dive: 573167 Run n' Gun /
            // 573170, 573168 Get 'em Good / 573171, 573169 All or Nothin' / 573172 [P text]; name ↔ button by text order =
            // cast order, with the damage rows rising the same way [I strong]. It never reached players: its recipe 3999
            // is one of the four retired weapon recipes (RecipeBook.RetiredRecipeItems), the four weapons with an E cast;
            // only an admin-granted copy uses this kit. Damage is each row the description names ([*AbilityParamN*],
            // RowDamage) [P formula; I that live hit for the row]. Dirty Fighting 573184: "a 25 % chance to gain Dirty
            // Fighting for the next 4 seconds" whenever a shot lands; the next attack uses its Dirty Fighting form and
            // spends it (Use). Ranges / arcs and the dive's timing are ours [I].
            Add(new Kit(Affinity.Affliction,
                // Run n' Gun: a revolver shot (row 703); Dirty Fighting: "Aim for a weak spot", row 706 and a 2 s Snare.
                new Attack(573167, 573170, Shape.FirstHit, 35f, 0f, DamageRow: 703, Primer: Status.DirtyFighting, PrimedRow: 706,
                    OnHit: h => { if (h.Swing.Primed) Apply(h, Status.Snare); },
                    After: s =>
                    {
                        if (s.Hits.Count > 0 && Random.Shared.NextDouble() < DirtyFightingChance)
                            Statuses.Apply(s.Player.PlayerGuid, Status.DirtyFighting, s.Player);
                    }),
                // Get 'em Good: a shank stab at the opponents in front (row 704); Dirty Fighting: "Leave an ugly wound",
                // row 707 three times over the next 3 s.
                new Attack(573168, 573171, Shape.Cone, 5f, 60f, DamageRow: 704, Primer: Status.DirtyFighting,
                    OnHit: h => { if (h.Swing.Primed) Bleed(h.Swing, h.Target, 707); }),
                12205,
                // All or Nothin' (E): a dive forward firing mid-air shots at the opponents in front (row 710; Dirty
                // Fighting: row 711 and a 2 s Snare), then on landing a shank hit on everyone in front (row 708; Dirty
                // Fighting: an additional row 709 three times over 3 s; DesperadoShank).
                Special: new Attack(573169, 573172, Shape.Cone, 15f, 35f, DamageRow: 710, Primer: Status.DirtyFighting,
                    PrimedRow: 711,
                    OnHit: h => { if (h.Swing.Primed) Apply(h, Status.Snare); },
                    After: s => Later(DiveLandingMs, () => DesperadoShank(s)))), 3038);
            log.Info($"Weapon kits: {k.Count} weapons.");
            return k;
        }

        // ---- Desperado's Deadly Arsenal (rows and Dirty Fighting) ---------------------------------------------------

        /// <summary>Run n' Gun: "a 25 % chance to gain Dirty Fighting" on a landed shot (text 573170) [P].</summary>
        private const double DirtyFightingChance = 0.25;
        /// <summary>All or Nothin': the shank hit comes this long after the dive starts ("After you land") [I].</summary>
        private const int DiveLandingMs = 700;
        /// <summary>All or Nothin's landing shank: opponents in front within this reach and ±arc (degrees) [I].</summary>
        private const float ShankRange = 4f, ShankArc = 60f;

        private static Dictionary<uint, (uint Stat, float Factor, float Add)> parameterRows;

        /// <summary>
        /// The value of a client AbilityParameterDefinitions StatMultiplier row for this player: stat(PARAM_1) × PARAM_2 +
        /// PARAM_3 (ParameterCalculator FUN_1435079e0; GearStats) [P], with stat 10 (Damage) as the client holds it (the
        /// armour's 70 × factor, then buffs and consumables, Abilities.CalculatedStats), so the hit equals the number the
        /// attack's description shows; then the weapon's condition (half at 0 %, as every weapon hit) [I ours]. Rows of
        /// another type or stat give the base weapon damage.
        /// </summary>
        public static uint RowDamage(WorldSession p, uint row)
        {
            if (parameterRows == null)
            {
                var rows = new Dictionary<uint, (uint, float, float)>();
                foreach (string[] r in GameDataFiles.Rows("AbilityParameterDefinitions.txt"))
                    if (r.Length > 6 && r[1] == "StatMultiplier")
                        rows[r.U(0)] = ((uint)r.F(4), r.F(5), r.F(6));
                parameterRows = rows;
            }
            if (!parameterRows.TryGetValue(row, out var def) || def.Stat != GearStats.DamageStatId)
                return Abilities.WeaponDamageFor(p);
            uint stat = GearStats.DamageStatId;
            float damage = Buffs.Apply(p, stat, Consumables.Apply(p, stat, GearStats.DamageStat(p)));
            return (uint)Math.Max(1, Math.Round((damage * def.Factor + def.Add) * Abilities.WeaponCondition(p)));
        }

        /// <summary>
        /// Dirty Fighting's wound: the row's damage (after the attacker's states, as the hit) three times, one a second
        /// (texts 573171 / 573172: "3 times over the next 3 seconds") [P count; I timing from the first second].
        /// </summary>
        private static void Bleed(Swing s, ulong target, uint row)
        {
            WorldSession p = s.Player;
            uint amount = Boosted(p, s.Kit, s.Power, RowDamage(p, row));
            for (int i = 1; i <= 3; i++)
                Later(1000 * i, () =>
                {
                    if (!p.Disconnected)
                        Creatures.StatusDamage(p, target, amount);
                });
        }

        /// <summary>
        /// All or Nothin' after the dive: "After you land everyone in front of you gets a taste of your shank" — row 708 on
        /// the opponents in front of where the player is now (the client moves the character), Dirty Fighting adding the
        /// row 709 wound when the dive started with it (text 573172) [P text; I reach, arc, timing].
        /// </summary>
        private static void DesperadoShank(Swing dive)
        {
            WorldSession p = dive.Player;
            if (p.Disconnected || Health.IsDead(p))
                return;
            var shank = new Swing
            {
                Player = p, Kit = dive.Kit, Attack = dive.Attack, Power = true, Origin = p.Position, Forward = dive.Forward,
                Aim = p.Position, Damage = Boosted(p, dive.Kit, true, RowDamage(p, 708)), Strength = dive.Strength,
                Run = dive.Run, Primed = dive.Primed
            };
            foreach (ulong t in Creatures.InCone(p.ZoneId, p.Position, dive.Forward, ShankRange, ShankArc))
                Strike(shank, t, shank.Damage, h => { if (h.Swing.Primed) Bleed(h.Swing, h.Target, 709); });
        }

        /// <summary>
        /// Weapons with no named attacks in the client (Lightbringer's Justice …): the generic
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

        /// <summary>Tidal Burst on one opponent: Surge if it was Frozen; a fully charged shot grants Flow per opponent hit.</summary>
        private static void TidalBurstHit(Hit h)
        {
            if (Statuses.Has(h.Target, Status.Freeze)) Surge(h);
            if (h.Swing.Charged) Grant(h.Swing, Status.Flow);
        }

        /// <summary>
        /// Is the weapon a melee weapon (25 May 2016 passive damage resistance)? Not a ranged model (bow, gun, raygun,
        /// cannon, staff, wand). This split matches the wiki's "Melee weapons" / "Ranged weapons" categories for all 31
        /// kit weapons (e.g. the daggers are melee although Contaminate leaps 12 m).
        /// </summary>
        public static bool IsMelee(ItemDefinitionManager.ItemDefinition weapon) => weapon != null && !IsRangedModel(weapon);

        /// <summary>
        /// The flying shot a ranged weapon draws (Projectiles 0xEE; OPEN-LEADS L157): bows an arrow, the other ranged weapons
        /// (staffs, wands, guns, rayguns) a bolt, in the kit's affinity. Rows by their trail effects [P: the
        /// ActorCompositeEffectDefinitions names]: arrows 85 Physical, 86 Affliction, 87 Fire, 88 Lightning, 89 Shadow, 90 Ice,
        /// 91 Radiance, 92 Water (80 m/s); bolts 94 Fire, 95 Radiance, 96 Lightning, 98 Shadow, 100 Affliction, 109 Physical
        /// (40 m/s), 108 Water (80 m/s). Which row live used per weapon was server data [I: by affinity; Earth = Physical,
        /// Water = the Water trail].
        /// </summary>
        public static uint ProjectileRow(ItemDefinitionManager.ItemDefinition weapon, Affinity affinity, bool power = false)
        {
            if (weapon == null || !IsRangedModel(weapon))
                return 0;
            if (WeaponRows(weapon) is (uint basic, uint strong))
                return power ? strong : basic;
            bool bow = (weapon.ModelFile ?? "").Contains("_Bow_");
            return affinity switch
            {
                Affinity.Affliction => bow ? 86u : 100u,
                Affinity.Fire => bow ? 87u : 94u,
                Affinity.Lightning => bow ? 88u : 96u,
                Affinity.Shadow => bow ? 89u : 98u,
                Affinity.Radiance => bow ? 91u : 95u,
                Affinity.Water => bow ? 92u : 108u,
                _ => bow ? 85u : 109u
            };
        }

        /// <summary>
        /// The tooltip keywords of a weapon attack (KeywordDefinitions rows shown to players, HIDE_FROM_CLIENT 0): the kit's
        /// affinity (1 Fire, 2 Water, 3 Shadow, 4 Radiance, 5 Affliction, 6 Earth, 7 Lightning), Ranged 56 / Melee 57, and
        /// Basic Attack 59 for a basic attack [P rows; I: which keywords live put on each attack — server data, L158].
        /// </summary>
        /// <summary>Dev "weaponkeywords on|off": whether weapon attack replies carry their keywords (default on).</summary>
        public static bool SendKeywords { get; set; } = true;

        public static List<uint> KeywordsOf(ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            var keywords = new List<uint>();
            Kit kit = KitOf(weapon);
            if (kit == null)
                return keywords;
            uint affinity = kit.Affinity switch
            {
                Affinity.Fire => 1u, Affinity.Water => 2u, Affinity.Shadow => 3u, Affinity.Radiance => 4u,
                Affinity.Affliction => 5u, Affinity.Earth => 6u, Affinity.Lightning => 7u, _ => 0u
            };
            if (affinity != 0)
                keywords.Add(affinity);
            keywords.Add(IsRangedModel(weapon) ? 56u : 57u);
            if (WeaponAttacks.KindOf(ability) is WeaponAttacks.Kind.Basic or WeaponAttacks.Kind.BasicCombo)
                keywords.Add(59u);
            return keywords;
        }

        /// <summary>
        /// Per-weapon rows (Basic, Power) for the 2016 ranged weapons (trace/0695/agent-leads2-d2.md L157) [I: matched by
        /// the rows' bones and the neighbouring effect ids of each weapon's own cast / release effects]: the Supercooled
        /// Power Cannon and the Raygun fire from the shoulder bone R_UpWeaponFlex (Water 115 / 114 Large, Affliction 93 lob /
        /// 99 Large), the Six-Shooters alternate 117 (right) / 118 (left), the Recon Blaster uses the left-hand Lightning
        /// rows (113 / 101), Night Herald's Mercy 119, the Wand of Boundless Light 112 (its release 11574 is the wand's),
        /// the Wanderer's Blazing Bow 111, the Staff of the Lightning Caller 121, the Staff of Frozen Sorrow 97 (Ice),
        /// Lightbringer's Justice 91. Items that share one of these weapons' kit (the pre-launch versions) use its rows.
        /// </summary>
        private static readonly Dictionary<uint, (uint Basic, uint Power)> weaponRows = new()
        {
            [3036] = (115, 114), [3032] = (93, 99), [3040] = (117, 117), [3033] = (113, 101), [4089] = (119, 119),
            [3767] = (112, 112), [3761] = (111, 111), [4107] = (121, 121), [3762] = (97, 97), [3028] = (91, 91)
        };

        private static (uint Basic, uint Power)? WeaponRows(ItemDefinitionManager.ItemDefinition weapon)
        {
            if (weaponRows.TryGetValue(weapon.Id, out var rows))
                return rows;
            if (Kits.TryGetValue(weapon.Id, out Kit kit))
                foreach (var (id, known) in weaponRows)
                    if (Kits.TryGetValue(id, out Kit other) && ReferenceEquals(other, kit))
                        return known;
            return null;
        }

        private static Dictionary<uint, float> rowSpeeds;

        /// <summary>A ProjectileDefinitions row's SPEED (m/s), read from the client's table [P]; 40 when unknown.</summary>
        private static float ProjectileSpeed(uint row)
        {
            if (rowSpeeds == null)
            {
                var speeds = new Dictionary<uint, float>();
                foreach (string[] r in GameDataFiles.Rows("ProjectileDefinitions.txt"))
                    if (r.Length > 3 && uint.TryParse(r[0], out uint id)
                        && float.TryParse(r[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed))
                        speeds[id] = speed;
                rowSpeeds = speeds;
            }
            return rowSpeeds.TryGetValue(row, out float v) && v > 0f ? v : 40f;
        }

        /// <summary>Dual revolvers: the shooter's next shot leaves the left hand (row 118) after a right-hand one (117).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WorldSession, bool> leftHandNext = new();

        /// <summary>
        /// The keywords an item's tooltip lists (InventoryPacketItemTooltipReply 0x2D / 0x12, the record's 4th int list:
        /// FUN_142c34c30 reads u32 n, n × u32 into the list ItemTooltipInfo FUN_143763a20 walks; trace/0695/agent-leads2-d2.md
        /// L158) [P]: a weapon's affinity and Ranged 56 / Melee 57, a chest armour's or an accessory's affinity, a pet
        /// figurine Pet 86. This reply is the only source of an item tooltip's keywords: the item definition carries none
        /// (items.json has no keyword field; definition +0xd0 is a plain u32, FUN_142c176d0), and FUN_143763a20's only other
        /// caller is the weapon variant FUN_14376ec10 with the same record [P]. Armour and accessories: the wiki's Armor /
        /// Accessories pages call their affinity associations "keywords" and Affinity (Equipment List) lists each piece's
        /// affinity (as Gear.cs) [P source; I that the tooltip showed exactly the affinity keyword].
        /// </summary>
        /// <summary>The KeywordDefinitions row of an affinity (1 Fire … 7 Lightning, the rows with icons) [P rows].</summary>
        public static uint KeywordOf(Affinity a) => a switch
        {
            Affinity.Fire => 1u, Affinity.Water => 2u, Affinity.Shadow => 3u, Affinity.Radiance => 4u,
            Affinity.Affliction => 5u, Affinity.Earth => 6u, Affinity.Lightning => 7u, _ => 0u
        };

        public static List<uint> ItemKeywordsOf(ItemDefinitionManager.ItemDefinition item)
        {
            var keywords = new List<uint>();
            if (item == null)
                return keywords;
            if (Pets.IsFigurine(item.Id))
                keywords.Add(86u);
            else if (Kits.ContainsKey(item.Id) || (item.EquipSlotId == 2 && AffinityOf(item) != Affinity.None))
            {
                Kit kit = KitOf(item);
                uint affinity = kit?.Affinity switch
                {
                    Affinity.Fire => 1u, Affinity.Water => 2u, Affinity.Shadow => 3u, Affinity.Radiance => 4u,
                    Affinity.Affliction => 5u, Affinity.Earth => 6u, Affinity.Lightning => 7u, _ => 0u
                };
                if (affinity != 0)
                    keywords.Add(affinity);
                keywords.Add(IsRangedModel(item) ? 56u : 57u);
            }
            else if (Gear.ArmourAffinity(item.Id) is var armour and not Affinity.None)
                keywords.Add(KeywordOf(armour));
            else if (Gear.AccessoryAffinity(item.Id) is var accessory and not Affinity.None)
                keywords.Add(KeywordOf(accessory));
            return keywords;
        }

        /// <summary>Draws a ranged swing's shot from the player to <paramref name="to"/>, ended when it gets there.</summary>
        private static void Fly(Swing s, Vector3 to)
        {
            if (s.Projectile == 0)
                return;
            WorldSession p = s.Player;
            Vector3 from = p.Position + new Vector3(0f, 1.5f, 0f);
            if (Vector3.Distance(from, to) < 1f)
                return;
            var viewers = AreaViewers(p, to).ToList();
            uint row = s.Projectile;
            if (row == 117)
            {
                bool left = leftHandNext.TryGetValue(p, out bool l) && l;
                leftHandNext[p] = !left;
                if (left)
                    row = 118;
            }
            uint id = Projectiles.NewId();
            // The shooter guid lets the client start the shot at the weapon bone (R_Weapon) of the player's actor.
            Projectiles.Create(viewers, id, row, from, to - from, p.PlayerGuid, to, 0);
            Later((int)(Vector3.Distance(from, to) / ProjectileSpeed(row) * 1000f) + 50,
                () => Projectiles.Delete(viewers, id, to, false, 0));
        }

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
            => Use(player, attempt.AimRay(player.Position), new Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ), weapon, ability, damage,
                cast: attempt);

        /// <summary>Dev "fire": the held weapon's Basic (cast 760) or Power (761) attack straight ahead, as a cast would run it.</summary>
        public static bool DevFire(WorldSession player, bool power)
            => Use(player, null, player.Position + Facing(player) * 20f, Abilities.HeldWeapon(player), power ? 761u : 760u, 50);

        // ---- Charged attacks ---------------------------------------------------------------------------------------

        /// <summary>The client's charged ability kind (FUN_142d71800 sets the charging state for it).</summary>
        public const uint ChargedKind = 0xF7150354;
        /// <summary>Hold this long for a full charge; the client won't release sooner than the minimum.</summary>
        public const float MaxChargeSeconds = 1.2f, MinChargeSeconds = 0.15f;

        private sealed record Charging(AbilityCastAttempt Attempt, ItemDefinitionManager.ItemDefinition Weapon, uint Damage,
            DateTime Start, WeaponAttacks.Animation? Animation, Vector3 Facing, float FullCharge);

        /// <summary>The attack a cast id runs with this weapon (Basic, Power or Special), or null.</summary>
        private static Attack AttackOf(ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            Kit kit = KitOf(weapon);
            if (kit == null)
                return null;
            return AttackFor(kit, WeaponAttacks.KindOf(ability));
        }

        /// <summary>Seconds to a full charge for this attack (its own, e.g. Tidal Burst's longer charge, else the default).</summary>
        private static float FullChargeOf(Attack a) => a?.ChargeSeconds > 0 ? a.ChargeSeconds : MaxChargeSeconds;

        private static readonly Dictionary<WorldSession, Charging> charging = new();

        /// <summary>Is this cast id a charged attack of this weapon?</summary>
        public static bool IsCharged(ItemDefinitionManager.ItemDefinition weapon, uint ability)
            => AttackOf(weapon, ability)?.Charged == true;

        /// <summary>
        /// Is this cast id a blocking stance of this weapon (<see cref="Shape.Block"/>: Molten Rebuke, Corrupting Revenge)?
        /// Such a cast hits nothing when it is used; <see cref="Use"/> returns false for it, which is not a miss.
        /// </summary>
        public static bool IsBlock(ItemDefinitionManager.ItemDefinition weapon, uint ability)
            => AttackOf(weapon, ability)?.Shape == Shape.Block;

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
                charging[p] = new Charging(attempt, weapon, damage, DateTime.UtcNow, animation, facing,
                    FullChargeOf(AttackOf(weapon, attempt.AbilityId)));
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
            float fraction = (float)Math.Clamp(held / c.FullCharge, 0, 1);
            bool full = held >= c.FullCharge * 0.95;
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
                stale = charging.Where(c => (now - c.Value.Start).TotalSeconds > c.Value.FullCharge + 5).Select(c => c.Key).ToList();
            foreach (WorldSession p in stale)
                if (p.Disconnected)
                    lock (sync) charging.Remove(p);
                else
                    FinishCharge(p, null);
        }

        /// <summary>
        /// Dev / test: an attack aimed at the nearest creature within 20 m (or straight ahead); <paramref name="special"/>
        /// runs an E cast (the kit's Special attack, else its Power attack).
        /// </summary>
        public static bool DevAttack(WorldSession player, bool power, bool charged = false, bool special = false)
        {
            var weapon = Abilities.HeldWeapon(player);
            WeaponAttacks.Kind wanted = special ? WeaponAttacks.Kind.Special : power ? WeaponAttacks.Kind.Power : WeaponAttacks.Kind.Basic;
            uint ability = WeaponAttacks.CastIds.FirstOrDefault(id => WeaponAttacks.KindOf(id) == wanted);
            Vector3 eye = player.Position + new Vector3(0f, 1.6f, 0f);
            ulong near = Creatures.InSight(player.ZoneId, player.Position, 20f).FirstOrDefault();
            Vector3 dir = near != 0 && Creatures.PositionOfGuid(near) is Vector3 at ? Vector3.Normalize(at + new Vector3(0f, 1f, 0f) - eye) : Facing(player);
            uint damage = (uint)Math.Round(Abilities.WeaponDamageFor(player) * WeaponAttacks.DamageMultiplier(weapon, ability));
            bool hit = Use(player, (eye, dir), eye + dir * 10f, weapon, ability, damage, charged ? true : null);
            log.Info($"{player.CharacterName} dev {(special ? "special" : power ? "power" : charged ? "charged basic" : "basic")} attack ({ability}) with {weapon?.Id}: {(hit ? "hit" : "missed")}"
                + $" (nearest {(near == 0 ? "none" : $"{Vector3.Distance(Creatures.PositionOfGuid(near) ?? player.Position, player.Position):0.0} m")}).");
            return hit;
        }

        /// <summary>Dev: the combat states on a player and on the creatures around them.</summary>
        public static string DevStates(WorldSession player)
        {
            var parts = new List<string>();
            string Of(ulong g) => string.Join(",", Enum.GetValues<Status>().Where(s => Statuses.Has(g, s))
                .Select(s => Statuses.Stacks(g, s) > 1 ? $"{s}x{Statuses.Stacks(g, s)}" : s.ToString()));
            parts.Add($"self [{Of(player.PlayerGuid)}] Energy {WeaponAttacks.EnergyOf(player)}/{WeaponAttacks.EnergyCap(player):0}");
            foreach (ulong c in Creatures.InSight(player.ZoneId, player.Position, 25f))
                parts.Add($"0x{c:X} {Creatures.HealthFraction(c):P0} [{Of(c)}]");
            return string.Join("; ", parts);
        }

        private static bool Use(WorldSession player, (Vector3 Origin, Vector3 Direction)? ray, Vector3 fallbackAim,
            ItemDefinitionManager.ItemDefinition weapon, uint ability, uint damage, bool? fullyCharged = null, AbilityCastAttempt cast = null)
        {
            Kit kit = KitOf(weapon);
            if (kit == null || Health.IsDead(player))
                return false;
            WeaponAttacks.Kind kind = WeaponAttacks.KindOf(ability);
            bool power = kind is WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo or WeaponAttacks.Kind.Special;
            Attack attack = AttackFor(kit, kind);
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
            int run = power ? WeaponAttacks.PowerRunOf(player) : 0;
            // Rolling Thunder: "dealing greater damage … with each strike"; the third in a row is the finisher.
            if (attack.Series)
                damage = (uint)Math.Round(damage * (1f + SeriesStep * (run % 3)));
            // Dirty Fighting and the like: the player's primer state makes this use the attack's stronger form.
            // "with your next shot" / "your next swing" (573170 / 573171): the attack that uses it spends it, hit or miss
            // [I literal reading].
            bool primed = attack.Primer is Status primer && Statuses.Has(player.PlayerGuid, primer);
            if (primed)
                Statuses.Remove(player.PlayerGuid, attack.Primer.Value);
            // An attack with its own parameter row hits for that row's value (the number its description shows), not the
            // base weapon damage × the kind's multiplier. (No row attack is charged, so no charge share is lost here.)
            if (attack.DamageRow != 0)
                damage = RowDamage(player, primed && attack.PrimedRow != 0 ? attack.PrimedRow : attack.DamageRow);
            var swing = new Swing
            {
                Player = player, Kit = kit, Attack = attack, Power = power, Charged = charged,
                Origin = player.Position, Forward = flat, Aim = aim,
                Damage = Boosted(player, kit, power, damage),
                Strength = (Statuses.Has(player.PlayerGuid, Status.Exalt) ? 1.25f : 1f)
                    * (Statuses.Has(player.PlayerGuid, Status.Rejuvenate) && Gear.Has(player, Gear.HighHeraldsRing) ? 1.25f : 1f),
                Ranged = attack.Shape is Shape.FirstHit or Shape.Pierce or Shape.AreaAtAim || attack.Range > 10f,
                Projectile = attack.Shape is Shape.FirstHit or Shape.Pierce or Shape.AreaAtAim ? ProjectileRow(weapon, kit.Affinity, power) : 0,
                Run = run,
                Primed = primed
            };
            if (attack.Shape == Shape.Block)
            {
                Statuses.Apply(player.PlayerGuid, Status.Block, player);
                return false;
            }
            // Nightfall, Terminal Voltage: immune during the leap; Rolling Thunder's third leap "briefly makes you immune".
            if (attack.Immune || (attack.Series && run % 3 == 2))
                Statuses.Apply(player.PlayerGuid, Status.Immune, player);
            // A melee swing whose caster info carried a Sweep targeter (OPEN-LEADS L170): the client's sweep reports the
            // opponents it hits (FindTarget) and the server strikes those after checking them against this cone; see
            // WeaponCombat.Targeters.cs. Dev swings and casts without a client targeter are resolved here, as before.
            if (cast != null && DeferToSweep(swing, ray, weapon, ability, cast))
                return false;
            Resolve(swing, ray);
            // Channels: every further hit costs the weapon's "energy cost over time" (WeaponAttacks.Balance, 26 Apr /
            // 25 May 2016 notes); the channel stops when the Energy runs out.
            uint tickEnergy = power ? WeaponAttacks.TickEnergy(weapon) : 0;
            for (int t = 1; t < attack.Ticks; t++)
            {
                int tick = t;
                Later(attack.TickMs * tick, () =>
                {
                    if (player.Disconnected || Health.IsDead(player) || swing.Stopped)
                        return;
                    if (!WeaponAttacks.TrySpendEnergy(player, tickEnergy))
                    {
                        swing.Stopped = true;
                        return;
                    }
                    var again = new Swing
                    {
                        Player = player, Kit = kit, Attack = attack, Power = power, Charged = charged,
                        Origin = player.Position, Forward = flat, Aim = aim, Damage = swing.Damage, Strength = swing.Strength,
                        Ranged = swing.Ranged, Projectile = swing.Projectile, Run = run, Primed = swing.Primed
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
                Shape.Around => Creatures.InSight(p.ZoneId, p.Position, a.Range),
                Shape.AreaAtAim => Creatures.InSight(p.ZoneId, s.Aim, a.Size),
                Shape.Pierce => Pierce(s, a.Range, a.Size),
                Shape.Path => Creatures.AlongRay(p.ZoneId, p.Position + new Vector3(0f, 1f, 0f), s.Forward, a.Range, a.Size),
                Shape.FirstHit => FirstHit(s, ray, a.Range, a.Size),
                _ => new List<ulong>()
            };
            if (a.MaxTargets > 0)
                targets = targets.Take(a.MaxTargets).ToList();
            // The visible shot: to the first target hit, else to the aimed point (Pierce: its full range).
            if (s.Projectile != 0)
            {
                Vector3 to = a.Shape == Shape.Pierce ? p.Position + new Vector3(0f, 1.2f, 0f) + s.Forward * a.Range
                    : targets.Count > 0 && Creatures.PositionOfGuid(targets[0]) is Vector3 hitAt ? hitAt + new Vector3(0f, 1f, 0f)
                    : s.Aim + new Vector3(0f, 1f, 0f);
                Fly(s, to);
            }
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
            Finish(s);
        }

        /// <summary>
        /// The end of a swing, once all its hits are in: the attack's rule for the whole swing (After), and a Power
        /// Attack spends the "first Power Attack after Frenzy / while Exalted" armour triggers.
        /// </summary>
        private static void Finish(Swing s)
        {
            WorldSession p = s.Player;
            s.Attack.After?.Invoke(s);
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
                hits.AddRange(Creatures.InSight(s.Player.ZoneId, at, splash).Where(g => g != first));
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
            // Erupt: a Fire weapon's property, or the weaker one a Fire accessory gives with another weapon
            // (Gear.WeakerProperty: half the chance; nothing guarantees it).
            bool weakFire = aff != Affinity.Fire && Gear.WeakerProperties(p, aff).Contains(Affinity.Fire);
            if (aff == Affinity.Fire || weakFire)
            {
                Pace pace = PaceOf(p);
                bool guaranteed = !weakFire && (Statuses.Has(p.PlayerGuid, Status.Eruption)
                    || (s.Power && pace.FirstPowerAfterFrenzy && Gear.Wields(p, Gear.WanderersBlazingGuise, aff)));
                float chance = EruptChance * s.Strength * (Gear.Has(p, Gear.MoltenIronOrb) ? 1.6f : 1f)
                    * (weakFire ? Gear.WeakerProperty : 1f);
                erupted = guaranteed || Random.Shared.NextDouble() < chance;
                if (erupted)
                {
                    damage = (uint)(damage * (Gear.Has(p, Gear.VolcanicBracer) ? 2.5f : 2f));
                    s.Erupted.Add(target);
                    HitEffects.PlayOn(target, 11059, Statuses.Viewers(target, p));   // PFX_Combat_Fire_Generic_Explosion_02m
                    if (Gear.Has(p, Gear.BandOfFlame))
                        Statuses.Apply(target, Status.Burn, p, power: s.Damage * BurnPerSecond);
                    if (guaranteed && s.Power && Gear.Wields(p, Gear.WanderersBlazingGuise, aff) && Creatures.PositionOfGuid(target) is Vector3 at)
                    {
                        Field(s, at, 3f, 4, null, 0.2f, effect: 12729);                // the blazing cyclone
                        Gear.Fired(p, Gear.WanderersBlazingGuise, "first Power Attack after Frenzy Erupted, blazing cyclone 3 m / 4 s");
                    }
                    if (weakFire)
                        Gear.Fired(p, WeakerItem(p, Affinity.Fire), "weaker Erupt (no Fire weapon)");
                }
            }
            if (Statuses.Has(target, Status.Weakened) == false && aff == Affinity.Affliction && Gear.Has(p, Gear.BeltOfVirulence)
                && Statuses.AtMax(p.PlayerGuid, Status.Infect))
                Statuses.Apply(target, Status.Weakened, p);
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

        /// <summary>
        /// The on-hit properties (wiki / 21 Mar 2016 notes P4197: Flow, Energize, Regrowth, Infect, Siphon, Rejuvenate):
        /// the weapon affinity's own, then the weaker one of each accessory affinity the weapon doesn't have
        /// (<see cref="Gear.WeakerProperties"/>; Fire's weaker Erupt is rolled in <see cref="Strike"/>).
        /// </summary>
        private static void Property(Hit h)
        {
            WorldSession p = h.Swing.Player;
            Affinity weapon = h.Swing.Kit.Affinity;
            PropertyOf(h, weapon, 1f);
            foreach (Affinity a in Gear.WeakerProperties(p, weapon))
                if (a != Affinity.Fire && PropertyOf(h, a, Gear.WeakerProperty))
                    Gear.Fired(p, WeakerItem(p, a), $"weaker {PropertyName(a)} (no {a} weapon)");
        }

        /// <summary>The on-hit property of an affinity (21 Mar 2016 notes, P4197).</summary>
        private static string PropertyName(Affinity a) => a switch
        {
            Affinity.Fire => "Erupt", Affinity.Water => "Flow", Affinity.Lightning => "Energize", Affinity.Earth => "Regrowth",
            Affinity.Affliction => "Infect", Affinity.Shadow => "Siphon", Affinity.Radiance => "Rejuvenate", _ => "none"
        };

        /// <summary>The first equipped accessory of an affinity (whose weaker property fired; for the log).</summary>
        private static uint WeakerItem(WorldSession p, Affinity a)
            => p.Inventory?.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer)
                   .Select(i => i.ItemDefinitionId).FirstOrDefault(id => Gear.AccessoryAffinity(id) == a) ?? 0;

        /// <summary>
        /// One affinity's on-hit property at a strength: 1 for the weapon's own, <see cref="Gear.WeakerProperty"/> for an
        /// accessory's weaker version, where it happens on that share of the hits (Flow, Energize, Infect), with that
        /// share of the chance (Regrowth, Rejuvenate) or turns that share of the damage into life (Siphon). The
        /// accessories' boosts apply to either. True when the property happened.
        /// </summary>
        private static bool PropertyOf(Hit h, Affinity affinity, float strength)
        {
            Swing s = h.Swing;
            WorldSession p = s.Player;
            bool some = strength >= 1f || Random.Shared.NextDouble() < strength;
            switch (affinity)
            {
                case Affinity.Water:
                    if (!some)
                        return false;
                    int flow = Gear.Has(p, Gear.RingOfTheRisingTide) ? 2 : 1;
                    Statuses.Apply(p.PlayerGuid, Status.Flow, p, stacks: flow);
                    // Flow raises the Energy maximum while it lasts (WeaponAttacks.EnergyCap; Sigil of Piercing Floods:
                    // more per stack); the stack's Energy is ours on top. GainEnergy also tells the client the new maximum.
                    WeaponAttacks.GainEnergy(p, FlowEnergy * flow);
                    return true;
                case Affinity.Lightning:
                    if (!s.Power || !some)
                        return false;
                    Statuses.Apply(p.PlayerGuid, Status.Energize, p,
                        seconds: Statuses.Defs[Status.Energize].Seconds * (Gear.Has(p, Gear.EarringsOfEnduringEnergy) ? 1.5f : 1f));
                    return true;
                case Affinity.Earth:
                    float chance = RegrowthChance * s.Strength * strength * (Gear.Has(p, Gear.BracerOfSurvival) ? 1.75f : 1f);
                    if (Random.Shared.NextDouble() >= chance)
                        return false;
                    Health.Heal(p, (uint)(Health.Max(p) * RegrowthShare * (Gear.Has(p, Gear.DefendersWristguard) ? 1.6f : 1f)));
                    HitEffects.PlayOn(p.PlayerGuid, 11668, Statuses.Viewers(p.PlayerGuid));
                    if (Gear.Has(p, Gear.RingOfIndifference))
                        Statuses.Apply(p.PlayerGuid, Status.Bulwark, p);
                    return true;
                case Affinity.Affliction:
                    if (!some)
                        return false;
                    Statuses.Apply(p.PlayerGuid, Status.Infect, p, stacks: Gear.Has(p, Gear.DefilersPiercings) ? 2 : 1);
                    return true;
                case Affinity.Shadow:
                    float share = (s.Power ? SiphonShare * (Gear.Has(p, Gear.ShadowforgedBand) ? 1.6f : 1f)
                        : Gear.Has(p, Gear.EarringsOfDarkestNight) ? SiphonShare * 0.5f : 0f) * strength;
                    // Plate of Unrelenting Vengeance: "significantly" more Siphon while its window runs (TryVengeance).
                    if (Recently(p, "revenge", VengeanceSiphon) && Gear.Wields(p, Gear.PlateOfUnrelentingVengeance, Affinity.Shadow))
                        share *= 2f;
                    if (share <= 0)
                        return false;
                    Health.Heal(p, (uint)Math.Max(1, h.Dealt * share * s.Strength));
                    // Spite Etched Bracer (574932): "Siphon temporarily increases the damage opponents take when you
                    // attack them" — when Siphon happens (was: on every Shadow hit, Siphon or not).
                    if (!h.Killed && Gear.Has(p, Gear.SpiteEtchedBracer))
                        Statuses.Apply(h.Target, Status.Siphoned, p);
                    return true;
                case Affinity.Radiance:
                    if (Random.Shared.NextDouble() >= RejuvenateChance * s.Strength * strength)
                        return false;
                    Statuses.Apply(p.PlayerGuid, Status.Rejuvenate, p,
                        power: Health.Max(p) * RejuvenateShare * (Gear.Has(p, Gear.BlessedBrooch) ? 2f : 1f),
                        seconds: Statuses.Defs[Status.Rejuvenate].Seconds * (Gear.Has(p, Gear.RadiantBulwark) ? 1.6f : 1f));
                    return true;
            }
            return false;
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
            {
                power = h.Swing.Damage * CondemnPerSecond;
                Gear.Fired(h.Swing.Player, Gear.HauberkOfPiercingJudgment, $"Condemn deals {power:0} damage a second");
            }
            Statuses.Apply(h.Target, st, h.Swing.Player, power: power, seconds: Defs(st) * h.Swing.Strength);
            Gear.OnApplied(h, st);
        }

        private static void Burn(Hit h) => Apply(h, Status.Burn);
        private static void Rot(Hit h) => Apply(h, Status.Rot);
        private static void Condemn(Hit h) => Apply(h, Status.Condemn);

        /// <summary>A state on the attacker (scaled by strength).</summary>
        public static void Grant(Swing s, Status st)
        {
            float power = st == Status.Illuminate ? Health.Max(s.Player) * IlluminateShare * s.Strength : 0f;
            Statuses.Apply(s.Player.PlayerGuid, st, s.Player, power: power, seconds: Defs(st) * s.Strength);
        }

        /// <summary>
        /// Surge: half of the player's stored energy is spent as bonus damage on the target — a base amount whatever
        /// was spent, plus more per Energy than before but less in all at high reserves (26 Apr 2016 notes, see
        /// <see cref="SurgeBaseShare"/>). Raiment of Frozen Sorrow: double against the Frozen, and a small ice storm
        /// stays behind.
        /// </summary>
        public static void Surge(Hit h)
        {
            WorldSession p = h.Swing.Player;
            double spent = WeaponAttacks.SpendHalfEnergy(p);
            bool frozen = Statuses.Has(h.Target, Status.Freeze);
            bool raiment = frozen && Gear.Wields(p, Gear.RaimentOfFrozenSorrow, Affinity.Water);
            double surge = h.Swing.Damage * SurgeBaseShare + spent * SurgePerEnergy;
            uint bonus = (uint)(surge * (raiment ? 2.0 : 1.0));
            if (bonus == 0)
                return;
            HitEffects.PlayOn(h.Target, 11533, Statuses.Viewers(h.Target, p));   // PFX_Combat_Ice_Staff_SpecialAttack_Explosion
            Creatures.Damage(p, h.Target, bonus, true, impact: false);
            if (raiment && Creatures.PositionOfGuid(h.Target) is Vector3 at)
            {
                Field(h.Swing, at, 4f, 4, null, 0.1f, effect: 11528);
                Gear.Fired(p, Gear.RaimentOfFrozenSorrow, $"Surge on a Frozen target doubled to {bonus}, ice storm 4 m / 4 s");
            }
        }

        /// <summary>Bonus damage (share of the swing) to everyone within <paramref name="radius"/> of each target hit; returns who was hit.</summary>
        private static List<ulong> Chain(Swing s, float radius, float share)
        {
            var near = new HashSet<ulong>();
            foreach (ulong t in s.Hits.ToList())
                if (Creatures.PositionOfGuid(t) is Vector3 at)
                    foreach (ulong n in Creatures.InSight(s.Player.ZoneId, at, radius))
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
                // Each area on its own, as the delayed hits above: a kill runs story Links (bughunt-combat.md B10).
                try
                {
                    foreach (ulong t in Creatures.InSight(a.Zone, a.At, a.Radius))
                    {
                        if (a.PerTick > 0 && a.TicksLeft >= 0)
                            Creatures.Damage(a.Source, t, a.PerTick, true, impact: false);
                        if (a.Applies is Status st && a.TicksLeft >= 0)
                            Statuses.Apply(t, st, a.Source, power: st == Status.Burn ? a.Damage * BurnPerSecond : 0f);
                        if (a.EndFreeze && a.TicksLeft == 0)
                            Statuses.Apply(t, Status.Freeze, a.Source, seconds: Defs(Status.Freeze) * a.Strength);
                    }
                }
                catch (Exception e)
                {
                    log.Warn(e, "Lingering area tick failed.");
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
                    // Hotfix notes 11 May 2016: the Flame Bound Bastion's Molten Rebuke keeps blocking and no longer
                    // stops to counter projectiles.
                    // A shot (attacker further than ProjectileDistance [I]) is stopped and the stance stays up.
                    var held = KitOf(Abilities.HeldWeapon(victim));
                    if (held?.Power.Name == MoltenRebukeName && to.Length() > ProjectileDistance)
                        return 0;
                    Statuses.Remove(g, Status.Block);
                    ShieldBash(victim);
                    return 0;
                }
            }
            float f = 1f;
            // 25 May 2016: melee weapons give their wielders a little more passive damage resistance.
            if (IsMelee(Abilities.HeldWeapon(victim)))
                f *= MeleeResistance;
            if (Statuses.Has(g, Status.Consecrated))
            {
                f *= 0.7f;
                if (!Recently(victim, "consecrate-shield", TimeSpan.FromSeconds(3)))
                {
                    Mark(victim, "consecrate-shield");
                    Statuses.Apply(g, Status.Illuminate, victim, power: Health.Max(victim) * IlluminateShare);
                }
            }
            if (Statuses.Has(g, Status.Bulwark)) f *= 0.8f;
            // Harden is used up by this hit; the armour effects that react to it (Armored Brawler's Doublet,
            // Unflinching Embodiment of Stone) get the flag, as Statuses no longer has it.
            bool hardened = Statuses.Has(g, Status.Harden);
            if (hardened)
            {
                Statuses.Remove(g, Status.Harden);
                f *= Gear.Wields(victim, Gear.UnflinchingEmbodimentOfStone, Affinity.Earth) ? 0f : 0.5f;
            }
            f *= Gear.OnHurt(victim, attacker, hardened);
            uint through = (uint)Math.Round(amount * f);
            return Statuses.Absorb(g, through);
        }

        /// <summary>Molten Rebuke's name text (the Flame Bound Bastion's Power Attack).</summary>
        private const uint MoltenRebukeName = 573125;

        /// <summary>How long Plate of Unrelenting Vengeance's stronger Siphon lasts after it fires.</summary>
        public static readonly TimeSpan VengeanceSiphon = TimeSpan.FromSeconds(4);

        /// <summary>
        /// Plate of Unrelenting Vengeance (text 575660: "opponents that attack you suffer revenge damage. When this
        /// happens Siphon also briefly converts significantly more damage to life"). Hotfix notes 26 April 2016,
        /// "Armor Passive: Unrelenting Vengeance": no longer needs low life or a Tormented opponent, more damage when
        /// it triggers, a much stronger Siphon for a while, no new trigger until that Siphon buff ends, and no benefit
        /// from Siphon itself (revenge damage is not a weapon hit, so it never heals),
        /// "No longer reduces incoming damage at low life". True when it fires now (and starts the Siphon window).
        /// </summary>
        public static bool TryVengeance(WorldSession p)
        {
            Pace pace = PaceOf(p);
            lock (sync)
            {
                if (pace.Marks.TryGetValue("revenge", out DateTime at) && DateTime.UtcNow - at <= VengeanceSiphon)
                    return false;
                pace.Marks["revenge"] = DateTime.UtcNow;
                return true;
            }
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
                Forward = Facing(p), Aim = p.Position,
                // The counter is the Power Attack: the weapon's Power factor (Flame Bound Bastion 25 May 2016: more
                // power attack damage, WeaponAttacks.Balance).
                Damage = (uint)Math.Round(Abilities.WeaponDamageFor(p) * WeaponAttacks.DamageFactor(weapon, power: true))
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
            foreach (ulong t in Creatures.InSight(p.ZoneId, at, radius))
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
            // Flow over (expired, death): the Energy maximum drops back to the table's (WeaponAttacks.EnergyCap).
            if (!started && s == Status.Flow)
                WeaponAttacks.EnergyCapChanged(p);
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
        /// / Special by the cast's button), cast time (Frenzy: 40 % faster) and Energy cost (<see cref="CostFactor"/>).
        /// </summary>
        public static void Describe(WorldSession p, ItemDefinitionManager.ItemDefinition weapon, uint ability, AbilityCasterInfoReply reply)
        {
            Kit kit = KitOf(weapon);
            if (kit == null)
                return;
            Attack a = AttackFor(kit, WeaponAttacks.KindOf(ability));
            reply.NameId = a.Name;
            reply.DescriptionId = a.Description;
            // The weapon's own Power Attack cost (WeaponAttacks.Balance: wiki tooltip values, 25 May 2016 pass).
            if (reply.CostAmount > 0 || WeaponAttacks.KindOf(ability) is WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo)
            {
                reply.CostAmount = WeaponAttacks.ShownEnergyCost(weapon, ability);
                reply.CostResource = 2;
            }
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
                float full = FullChargeOf(a);
                reply.MaxCharge = full;
                reply.MinCharge = MinChargeSeconds * full / MaxChargeSeconds;
            }
            // A melee swing: the client runs it as a Sweep targeter and reports the opponents it hits (L170).
            if (SweepOf(weapon, ability) is ClientExecutedTargeter sweep)
                reply.ExecutedTargeters.Add(sweep);
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
            // Equip / unequip: the armour's maximum life and damage (sent only when they changed).
            GearStats.Refresh(p);
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
            // The six-shooters' hand (a concurrent map): kept the whole session alive after logout (bughunt-combat.md B9).
            leftHandNext.TryRemove(p, out _);
            ForgetSweeps(p);
            Abilities.Forget(p);
            Statuses.Forget(p);
        }
    }
}
