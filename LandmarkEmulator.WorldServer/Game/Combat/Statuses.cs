using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>The seven launch affinities (weapon / armour / accessory "keywords"; wiki "Affinity").</summary>
    public enum Affinity { None, Fire, Water, Lightning, Earth, Affliction, Radiance, Shadow }

    /// <summary>
    /// The affinity properties (wiki weapon pages: the properties each affinity's weapons have) as
    /// timed states on a creature or a player, plus a few helper states the attack and armour texts need.
    /// <list type="bullet">
    /// <item>Fire: <b>Erupt</b> (a hit can deal double damage — a roll, not a state), <b>Frenzy</b> (all attacks
    /// temporarily faster), <b>Burn</b> (stops the opponent's attacks, damage over time).</item>
    /// <item>Water: <b>Flow</b> (stacks: more maximum energy), <b>Surge</b> (spends half the stored energy as bonus
    /// damage — instant), <b>Freeze</b> (stops attacks and movement).</item>
    /// <item>Lightning: <b>Supercharge</b> (Power Attacks deal more and cost more), <b>Energize</b> (stacks: energy
    /// regeneration), <b>Electrocute</b> (stops attacks and movement).</item>
    /// <item>Earth: <b>Harden</b> (the next hit taken deals less), <b>Regrowth</b> (hits restore life — instant),
    /// <b>Crush</b> (knocked to the ground: stops attacks and movement).</item>
    /// <item>Affliction: <b>Infect</b> (stacks: more damage), <b>Rot</b> (the opponent deals less, damage over time),
    /// <b>Cripple</b> (slowed).</item>
    /// <item>Shadow: <b>Siphon</b> (Power Attack damage partly turned into life — instant), <b>Torment</b> (the opponent
    /// takes more damage), <b>Condemn</b> (stops movement).</item>
    /// <item>Radiance: <b>Illuminate</b> (a shield intercepts damage), <b>Exalt</b> (more damage, every weapon property
    /// stronger), <b>Rejuvenate</b> (life regeneration).</item>
    /// </list>
    /// Durations, strengths and stack limits were server data (lost); the numbers here are ours and kept in one place
    /// (<see cref="Defs"/>). Visuals are the client's own state effects (ActorCompositeEffectDefinitions:
    /// PFX_Combat_Misc_State_*); a player's own states also show on the buff bar under the client's property name (text
    /// 574468 Frenzy … 574494 Exalt) with the affinity's buff icon (image sets 12206-12218 icon_abilities_*_buff).
    /// </summary>
    public enum Status
    {
        // On opponents
        Burn, Freeze, Electrocute, Crush, Cripple, Rot, Condemn, Torment,
        /// <summary>Unload: "temporarily vulnerable to being Crushed by Trick Shot".</summary>
        Exposed,
        /// <summary>Belt of Virulence: Infect at max stacks "temporarily reduces the damage of opponents you attack".</summary>
        Weakened,
        /// <summary>Spite Etched Bracer: Siphon "temporarily increases the damage opponents take when you attack them".</summary>
        Siphoned,
        /// <summary>Vestment of the Lightning Caller: lightning keeps "periodically Electrocuting" an opponent for a short time.</summary>
        Overloaded,
        /// <summary>
        /// Desperado's Dirty Fighting shots ("Snaring your opponent for 2 seconds", 573170 / 573172): slowed, like Cripple.
        /// The client keeps Snared and Rooted apart (its test weapons 571366-571378 "Rooted Cast/Snared Active" …), so a
        /// snare slows rather than stops [I strong]; name text 565472 "Snare".
        /// </summary>
        Snare,
        // On players
        Frenzy, Flow, Supercharge, Energize, Harden, Infect, Illuminate, Exalt, Rejuvenate,
        /// <summary>A blocking stance (Molten Rebuke, Corrupting Revenge): the next attack from the front is stopped.</summary>
        Block,
        /// <summary>Nightfall's leap: "immune to incoming attacks".</summary>
        Immune,
        /// <summary>Dragonfire Ritual Robes: "all of your attacks Erupt for a short time".</summary>
        Eruption,
        /// <summary>Ring of Indifference: Regrowth "temporarily reduces all incoming damage".</summary>
        Bulwark,
        /// <summary>Consecrate's channel: less damage for the caster and allies nearby.</summary>
        Consecrated,
        /// <summary>
        /// Desperado's Deadly Arsenal: "a 25% chance to gain Dirty Fighting for the next 4 seconds" when a shot lands
        /// (573170); the next attack uses its Dirty Fighting form (WeaponCombat, Attack.Primer). Name text 573184.
        /// </summary>
        DirtyFighting
    }

    public static class Statuses
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>One state's rules: name text, debuff or buff, seconds, stack cap, visual, the affinity it belongs to.</summary>
        public sealed record Def(Status Kind, uint NameText, bool Debuff, float Seconds, int MaxStacks, uint Visual, Affinity Affinity);

        public static readonly Dictionary<Status, Def> Defs = new Def[]
        {
            new(Status.Burn, 574469, true, 4f, 1, 11287, Affinity.Fire),             // PFX_Combat_Misc_State_Debuff_DoT_Fire_01
            new(Status.Freeze, 565471, true, 3f, 1, 10181, Affinity.Water),          // PFX_Combat_Misc_State_Frozen
            new(Status.Electrocute, 574482, true, 2.5f, 1, 11290, Affinity.Lightning), // ..._Debuff_DoT_Lightning_01
            new(Status.Crush, 574488, true, 3f, 1, 11292, Affinity.Earth),            // PFX_Combat_Misc_State_Knockdown
            new(Status.Cripple, 574489, true, 5f, 1, 11289, Affinity.Affliction),     // PFX_Combat_Misc_State_Snared
            new(Status.Rot, 574490, true, 6f, 1, 11565, Affinity.Affliction),         // Affliction_Daggers_Attack02_DamageDot
            new(Status.Condemn, 574492, true, 3f, 1, 10297, Affinity.Shadow),         // PFX_Combat_Misc_State_Rooted_StasisField
            new(Status.Torment, 574491, true, 6f, 1, 11579, Affinity.Shadow),         // Shadow_SwordShield_Attack01_DamageHit
            new(Status.Exposed, 0, true, 4f, 1, 0, Affinity.Earth),
            new(Status.Weakened, 0, true, 4f, 1, 0, Affinity.Affliction),
            new(Status.Siphoned, 0, true, 4f, 1, 0, Affinity.Shadow),
            new(Status.Overloaded, 0, true, 4f, 1, 11290, Affinity.Lightning),
            new(Status.Snare, 565472, true, 2f, 1, 11289, Affinity.Affliction),       // PFX_Combat_Misc_State_Snared
            new(Status.Frenzy, 574468, false, 8f, 1, 11535, Affinity.Fire),          // PFX_Combat_Fire_Bow_Attack01_Buff_Stage2
            new(Status.Flow, 574471, false, 10f, 5, 0, Affinity.Water),
            new(Status.Supercharge, 574473, false, 8f, 1, 11556, Affinity.Lightning), // Lightning_2HSword_Attack01_Buff
            new(Status.Energize, 574483, false, 8f, 5, 11560, Affinity.Lightning),    // PFX_Combat_Misc_State_Energy_Gain
            new(Status.Harden, 574487, false, 10f, 1, 11616, Affinity.Earth),         // PFX_Combat_Misc_State_Shield_Physical
            new(Status.Infect, 574485, false, 10f, 5, 0, Affinity.Affliction),
            new(Status.Illuminate, 574493, false, 8f, 1, 11667, Affinity.Radiance),   // PFX_Combat_Misc_State_Shield_Radiance
            new(Status.Exalt, 574494, false, 8f, 1, 11649, Affinity.Radiance),
            new(Status.Rejuvenate, 574486, false, 5f, 1, 11668, Affinity.Radiance),   // PFX_Combat_Misc_State_Health_Gain
            new(Status.Block, 0, false, 2f, 1, 0, Affinity.None),
            new(Status.Immune, 0, false, 1.2f, 1, 0, Affinity.Shadow),
            new(Status.Eruption, 0, false, 4f, 1, 0, Affinity.Fire),
            new(Status.Bulwark, 0, false, 4f, 1, 0, Affinity.Earth),
            new(Status.Consecrated, 0, false, 1.5f, 1, 0, Affinity.Radiance),
            new(Status.DirtyFighting, 573184, false, 4f, 1, 0, Affinity.Affliction),
        }.ToDictionary(d => d.Kind);

        /// <summary>Buff-bar icon per affinity (icon_abilities_&lt;affinity&gt;_buff).</summary>
        public static uint BuffIcon(Affinity a) => a switch
        {
            Affinity.Affliction => 12206, Affinity.Water => 12208, Affinity.Lightning => 12210, Affinity.Fire => 12212,
            Affinity.Earth => 12214, Affinity.Radiance => 12216, Affinity.Shadow => 12218, _ => 0
        };

        private sealed class Entry
        {
            public DateTime Ends;
            public int Stacks;
            /// <summary>When it was last put on (a re-application counts as a new one).</summary>
            public DateTime Applied;
            public WorldSession Source;
            /// <summary>Damage per second (Burn, Rot), life per second (Rejuvenate), shield left (Illuminate).</summary>
            public float Power;
            public DateTime NextTick;
        }

        private static readonly Dictionary<ulong, Dictionary<Status, Entry>> on = new();
        private static readonly object sync = new();

        // ---- Queries ---------------------------------------------------------------------------------------------

        public static bool Has(ulong entity, Status s)
        {
            lock (sync)
                return on.TryGetValue(entity, out var map) && map.TryGetValue(s, out Entry e) && e.Ends > DateTime.UtcNow;
        }

        /// <summary>When the state was last put on the entity; default when it doesn't have it.</summary>
        public static DateTime AppliedAt(ulong entity, Status s)
        {
            lock (sync)
                return on.TryGetValue(entity, out var map) && map.TryGetValue(s, out Entry e) && e.Ends > DateTime.UtcNow ? e.Applied : default;
        }

        public static int Stacks(ulong entity, Status s)
        {
            lock (sync)
                return on.TryGetValue(entity, out var map) && map.TryGetValue(s, out Entry e) && e.Ends > DateTime.UtcNow ? e.Stacks : 0;
        }

        public static bool AtMax(ulong entity, Status s) => Stacks(entity, s) >= Defs[s].MaxStacks;

        /// <summary>Can it attack? Burn, Freeze, Electrocute and Crush stop attacks.</summary>
        public static bool CanAct(ulong entity) =>
            !Has(entity, Status.Burn) && !Has(entity, Status.Freeze) && !Has(entity, Status.Electrocute) && !Has(entity, Status.Crush);

        /// <summary>Can it move? Freeze, Electrocute, Crush and Condemn stop movement.</summary>
        public static bool CanMove(ulong entity) =>
            !Has(entity, Status.Freeze) && !Has(entity, Status.Electrocute) && !Has(entity, Status.Crush) && !Has(entity, Status.Condemn);

        /// <summary>Movement speed factor: Cripple and Snare slow to half.</summary>
        public static float Speed(ulong entity) => Has(entity, Status.Cripple) || Has(entity, Status.Snare) ? 0.5f : 1f;

        /// <summary>Damage it takes: Torment +25 %, Siphoned (Spite Etched Bracer) +15 %.</summary>
        public static float TakenFactor(ulong entity) =>
            (Has(entity, Status.Torment) ? 1.25f : 1f) * (Has(entity, Status.Siphoned) ? 1.15f : 1f);

        /// <summary>Damage it deals: Rot −25 %, Weakened (Belt of Virulence) −20 %.</summary>
        public static float DealtFactor(ulong entity) =>
            (Has(entity, Status.Rot) ? 0.75f : 1f) * (Has(entity, Status.Weakened) ? 0.8f : 1f);

        // ---- Changes ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Puts a state on an entity (or refreshes it and adds <paramref name="stacks"/> up to the cap). The state's
        /// effect plays for its viewers; on a player its buff-bar icon is shown. Returns true if it was new.
        /// </summary>
        public static bool Apply(ulong entity, Status s, WorldSession source, float power = 0f, float seconds = 0f, int stacks = 1,
            IEnumerable<WorldSession> viewers = null)
        {
            Def def = Defs[s];
            float secs = seconds > 0 ? seconds : def.Seconds;
            DateTime now = DateTime.UtcNow;
            bool fresh;
            int total;
            lock (sync)
            {
                if (!on.TryGetValue(entity, out var map))
                    on[entity] = map = new Dictionary<Status, Entry>();
                fresh = !map.TryGetValue(s, out Entry e) || e.Ends <= now;
                if (fresh)
                    map[s] = e = new Entry { Stacks = 0, NextTick = now.AddSeconds(1) };
                e.Ends = now.AddSeconds(secs);
                e.Applied = now;
                e.Stacks = Math.Min(def.MaxStacks, e.Stacks + stacks);
                e.Source = source ?? e.Source;
                if (power > 0)
                    e.Power = s == Status.Illuminate ? power : Math.Max(e.Power, power);
                total = e.Stacks;
            }
            if (def.Visual != 0 && (fresh || def.MaxStacks > 1))
                HitEffects.PlayOn(entity, def.Visual, viewers ?? Viewers(entity, source), (uint)(secs * 1000));
            if (PlayerManager.FindByGuid(entity) is WorldSession player && def.NameText != 0)
                Buffs.ShowStatus(player, "status:" + s, def.NameText, BuffIcon(def.Affinity), TimeSpan.FromSeconds(secs), (uint)total);
            if (fresh && PlayerManager.FindByGuid(entity) is WorldSession p2)
                WeaponCombat.StatusChanged(p2, s, true);
            // A blocking stance (started or refreshed) is shown to the other clients: their sweeps stop on it (BlockStance).
            if (s == Status.Block && PlayerManager.FindByGuid(entity) != null)
                BlockStance.PlayerBlocked(entity, secs);
            return fresh;
        }

        /// <summary>Takes a state off (the shield is used up, the block caught a hit, a stack was spent).</summary>
        public static void Remove(ulong entity, Status s)
        {
            bool had;
            lock (sync)
                had = on.TryGetValue(entity, out var map) && map.Remove(s);
            if (had)
                Ended(entity, s);
        }

        /// <summary>Everything off an entity (death, despawn, logout).</summary>
        public static void Clear(ulong entity)
        {
            List<Status> had;
            lock (sync)
            {
                if (!on.Remove(entity, out var map))
                    return;
                had = map.Keys.ToList();
            }
            foreach (Status s in had)
                Ended(entity, s);
        }

        private static void Ended(ulong entity, Status s)
        {
            if (s == Status.Block)
                BlockStance.PlayerUnblocked(entity);
            if (PlayerManager.FindByGuid(entity) is not WorldSession player)
                return;
            if (Defs[s].NameText != 0)
                Buffs.HideStatus(player, "status:" + s);
            WeaponCombat.StatusChanged(player, s, false);
        }

        /// <summary>Illuminate's shield soaks up to its strength; returns what gets through.</summary>
        public static uint Absorb(ulong entity, uint amount)
        {
            bool gone = false;
            uint through;
            lock (sync)
            {
                if (!on.TryGetValue(entity, out var map) || !map.TryGetValue(Status.Illuminate, out Entry e) || e.Ends <= DateTime.UtcNow)
                    return amount;
                float soak = Math.Min(e.Power, amount);
                e.Power -= soak;
                through = amount - (uint)soak;
                gone = e.Power <= 0.5f;
                if (gone)
                    map.Remove(Status.Illuminate);
            }
            if (gone)
                Ended(entity, Status.Illuminate);
            return through;
        }

        /// <summary>Who sees an entity's state effect: a creature's viewers, a player's zone.</summary>
        public static IEnumerable<WorldSession> Viewers(ulong entity, WorldSession fallback = null)
        {
            if (PlayerManager.FindByGuid(entity) is WorldSession p)
                return PlayerManager.InZone(p.ZoneId).Where(v => Vector3.Distance(v.Position, p.Position) < 150f).Append(p).Distinct();
            var list = Creatures.ViewersOfGuid(entity);
            return fallback != null ? list.Append(fallback).Distinct() : list;
        }

        // ---- Tick ------------------------------------------------------------------------------------------------

        private static DateTime nextTick;

        /// <summary>
        /// Four times a second: damage over time (Burn, Rot) and Rejuvenate's healing each second, Energize's energy,
        /// Overloaded's repeated Electrocute, expiry (icons go, MAE / attack info update).
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextTick)
                return;
            nextTick = now.AddMilliseconds(250);
            var ticks = new List<(ulong Entity, Status S, Entry E)>();
            var ended = new List<(ulong, Status)>();
            lock (sync)
            {
                foreach ((ulong entity, var map) in on.ToList())
                {
                    foreach ((Status s, Entry e) in map.ToList())
                    {
                        if (e.Ends <= now)
                        {
                            map.Remove(s);
                            ended.Add((entity, s));
                        }
                        else if (now >= e.NextTick)
                        {
                            e.NextTick = now.AddSeconds(1);
                            ticks.Add((entity, s, e));
                        }
                    }
                    if (map.Count == 0)
                        on.Remove(entity);
                }
            }
            foreach (var (entity, s) in ended)
            {
                try { Ended(entity, s); }
                catch (Exception ex) { log.Error(ex, $"End of {s} on 0x{entity:X} failed."); }
            }
            // Each tick on its own: a Burn / Rot tick can kill a story creature, whose "dies" Links run right here
            // (Creatures.Kill → StoryTools.CreatureDied). One that throws skipped the rest of the ticks and the channel
            // hits, sweeps and lingering areas below (WeaponCombat.Update) for that tick (bughunt-combat.md B10).
            foreach (var (entity, s, e) in ticks)
            {
                try
                {
                    Tick(entity, s, e);
                }
                catch (Exception ex)
                {
                    log.Error(ex, $"{s} tick on 0x{entity:X} failed.");
                }
            }
            BlockStance.Update(now);
            WeaponCombat.Update(now);
        }

        /// <summary>One second of a state: damage over time, healing, Energy, Overloaded's Electrocute.</summary>
        private static void Tick(ulong entity, Status s, Entry e)
        {
            switch (s)
            {
                case Status.Burn:
                case Status.Rot:
                    if (e.Power > 0 && e.Source != null)
                        Creatures.StatusDamage(e.Source, entity, (uint)Math.Max(1, e.Power));
                    break;
                case Status.Rejuvenate:
                    if (PlayerManager.FindByGuid(entity) is WorldSession healed && e.Power > 0)
                        Health.Heal(healed, (uint)e.Power);
                    break;
                case Status.Energize:
                    if (PlayerManager.FindByGuid(entity) is WorldSession energized)
                        WeaponAttacks.GainEnergy(energized, 3.0 * e.Stacks * Gear.EnergizeRegenFactor(energized));
                    break;
                case Status.Overloaded:
                    Apply(entity, Status.Electrocute, e.Source, seconds: 1f);
                    break;
            }
        }

        /// <summary>A player left: their own states go (no icons to clear).</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                on.Remove(session.PlayerGuid);
            BlockStance.Forget(session);   // their character leaves every client with them: no end packets
        }
    }
}
