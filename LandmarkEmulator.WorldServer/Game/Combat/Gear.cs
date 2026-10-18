using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// Armour and accessories with affinity effects (wiki "Maximum Affinity Effect"; texts are the items' descriptions).
    /// <para>
    /// Chest armour (equip slot 8, ability 1100-1148): "While wielding a &lt;Affinity&gt; weapon …" — a Maximum Affinity
    /// Effect that only works with a weapon of the same affinity (descriptions 575654-575681 in the order of the items'
    /// names 575605-575632). Accessories (slot 3): one property made stronger ("Erupt happens more often", "Flow stacks
    /// up faster" …); those work whenever the property happens.
    /// </para>
    /// The chances and sizes are ours (server data); the effects follow each text. The descriptions are the same in
    /// 0.589 and 0.695 (trace/0695/audit-equipment-goals-695.md): the 7 Jun 2016 "new passives" (P5736-P5746) are the
    /// texts both clients show. The passive ability ids on the items (1110-1148 armour, 1082-1095 accessories) are in
    /// no client table, so their numbers were live server data (OPEN-LEADS L90).
    /// <para>
    /// Every passive that fires logs one line, "&lt;character&gt;: &lt;item name&gt; passive: &lt;what happened&gt;", the proof
    /// for in-game tests.
    /// </para>
    /// </summary>
    public static class Gear
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // Chest armour (MAE).
        public const uint WanderersBlazingGuise = 1466, RaimentOfFrozenSorrow = 1491, ManifestationOfTheInnerSpark = 1495,
            ArmoredBrawlersDoublet = 1530, PoisonDrenchedLeathers = 1531, MantleOfBoundlessLight = 1532,
            PlateOfUnrelentingVengeance = 1533, FlameBoundCuirass = 1534, SupercooledPowerArmor = 1535,
            OverchargedReconGear = 1536, GunslingersLastResort = 1537, DiabolicalScientistsCoat = 1538,
            AvengingChampionsAegis = 1539, HauberkOfPiercingJudgment = 1540, FrenziedSlayersChainmail = 1541,
            ReinforcedCoatOfCrashingWaves = 1942, ElectrifiedAssaultExoSuit = 2249, UnflinchingEmbodimentOfStone = 2250,
            DesperadosDeadlyPoncho = 2251, LightbringersOath = 2252, SilentShadowsCloak = 2253, DragonfireRitualRobes = 2254,
            FrostLordsGuard = 2255, VestmentOfTheLightningCaller = 2256, TectonicDestroyersPlatemail = 2257,
            BlightWardensSuffering = 2258, CrusadersCalling = 2259, NightHeraldsPromise = 2260;

        // Accessories.
        public const uint BracerOfSurvival = 1607, DefendersWristguard = 1608, EarringsOfEnduringEnergy = 1609,
            BroochOfTheBoundlessEye = 1610, EarringsOfDarkestNight = 1611, ShadowforgedBand = 1612, RingOfIndifference = 1613,
            RadiantBulwark = 1614, MoltenIronOrb = 1615, VolcanicBracer = 1616, DefilersPiercings = 1617,
            ForsakenBandOfPower = 1618, BandOfFlame = 1620, BlessedBrooch = 1621, HighHeraldsRing = 1622,
            RingOfTheRisingTide = 1625, SigilOfPiercingFloods = 1626, DrownersRing = 1627, PoisonEtchedAmulet = 1628,
            BeltOfVirulence = 1629, SpiteEtchedBracer = 3819;

        /// <summary>Each chest armour's affinity (its MAE needs a weapon of that affinity).</summary>
        private static readonly Dictionary<uint, Affinity> armour = new()
        {
            [WanderersBlazingGuise] = Affinity.Fire, [FlameBoundCuirass] = Affinity.Fire,
            [FrenziedSlayersChainmail] = Affinity.Fire, [DragonfireRitualRobes] = Affinity.Fire,
            [RaimentOfFrozenSorrow] = Affinity.Water, [SupercooledPowerArmor] = Affinity.Water,
            [ReinforcedCoatOfCrashingWaves] = Affinity.Water, [FrostLordsGuard] = Affinity.Water,
            [ManifestationOfTheInnerSpark] = Affinity.Lightning, [OverchargedReconGear] = Affinity.Lightning,
            [ElectrifiedAssaultExoSuit] = Affinity.Lightning, [VestmentOfTheLightningCaller] = Affinity.Lightning,
            [ArmoredBrawlersDoublet] = Affinity.Earth, [GunslingersLastResort] = Affinity.Earth,
            [UnflinchingEmbodimentOfStone] = Affinity.Earth, [TectonicDestroyersPlatemail] = Affinity.Earth,
            [PoisonDrenchedLeathers] = Affinity.Affliction, [DiabolicalScientistsCoat] = Affinity.Affliction,
            [DesperadosDeadlyPoncho] = Affinity.Affliction, [BlightWardensSuffering] = Affinity.Affliction,
            [MantleOfBoundlessLight] = Affinity.Radiance, [AvengingChampionsAegis] = Affinity.Radiance,
            [LightbringersOath] = Affinity.Radiance, [CrusadersCalling] = Affinity.Radiance,
            [PlateOfUnrelentingVengeance] = Affinity.Shadow, [HauberkOfPiercingJudgment] = Affinity.Shadow,
            [SilentShadowsCloak] = Affinity.Shadow, [NightHeraldsPromise] = Affinity.Shadow,
        };

        /// <summary>
        /// Each accessory's affinity (the colour of the property its description names, texts 565621-565743 / 574932).
        /// The item records agree: Water accessories carry ability 1062 like every Water weapon, Lightning 1065, Earth
        /// 1067, Affliction 1069, Radiance 1071 (gameData695/items.json), next to their own boost ability (1082-1095).
        /// </summary>
        private static readonly Dictionary<uint, Affinity> accessories = new()
        {
            [MoltenIronOrb] = Affinity.Fire, [VolcanicBracer] = Affinity.Fire, [BandOfFlame] = Affinity.Fire,
            [RingOfTheRisingTide] = Affinity.Water, [SigilOfPiercingFloods] = Affinity.Water, [DrownersRing] = Affinity.Water,
            [EarringsOfEnduringEnergy] = Affinity.Lightning, [BroochOfTheBoundlessEye] = Affinity.Lightning,
            [ForsakenBandOfPower] = Affinity.Lightning,
            [BracerOfSurvival] = Affinity.Earth, [DefendersWristguard] = Affinity.Earth, [RingOfIndifference] = Affinity.Earth,
            [DefilersPiercings] = Affinity.Affliction, [PoisonEtchedAmulet] = Affinity.Affliction, [BeltOfVirulence] = Affinity.Affliction,
            [RadiantBulwark] = Affinity.Radiance, [BlessedBrooch] = Affinity.Radiance, [HighHeraldsRing] = Affinity.Radiance,
            [EarringsOfDarkestNight] = Affinity.Shadow, [ShadowforgedBand] = Affinity.Shadow, [SpiteEtchedBracer] = Affinity.Shadow,
        };

        /// <summary>
        /// The weaker property an accessory gives when the held weapon has another affinity (wiki, every accessory
        /// page: a Water accessory gives a weaker Flow when the weapon is not Water, on top of its other effects;
        /// likewise Fire → Erupt, Lightning → Energize, Earth → Regrowth, Affliction → Infect,
        /// Radiance → Rejuvenate, Shadow → Siphon). [I] "weaker" = half: half the chance, or half the hits, or half the
        /// share (WeaponCombat.Property).
        /// </summary>
        public const float WeakerProperty = 0.5f;

        /// <summary>
        /// The affinities whose weaker property the player gets on a hit: those of the equipped accessories, except the
        /// held weapon's own (its full property already happens). One per affinity, however many accessories share it.
        /// </summary>
        public static List<Affinity> WeakerProperties(WorldSession p, Affinity weapon)
        {
            var list = new List<Affinity>();
            if (p.Inventory == null)
                return list;
            foreach (var i in p.Inventory.Items)
                if (i.Container == Inventory.Inventory.EquippedContainer && accessories.TryGetValue(i.ItemDefinitionId, out Affinity a)
                    && a != weapon && !list.Contains(a))
                    list.Add(a);
            return list;
        }

        /// <summary>An accessory's affinity (None for anything else).</summary>
        public static Affinity AccessoryAffinity(uint item) => accessories.GetValueOrDefault(item, Affinity.None);

        /// <summary>A chest armour's affinity (None for anything else).</summary>
        public static Affinity ArmourAffinity(uint item) => armour.GetValueOrDefault(item, Affinity.None);

        /// <summary>Logs a passive that fired (the in-game test's proof).</summary>
        public static void Fired(WorldSession p, uint item, string what)
        {
            uint nameId = Inventory.ItemDefinitionManager.Get(item)?.NameId ?? 0;
            string name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(nameId) ?? $"item {item}";
            log.Info($"{p.CharacterName}: {name} passive: {what}.");
        }

        /// <summary>Is the item equipped?</summary>
        public static bool Has(WorldSession p, uint item)
            => p.Inventory?.Items.Any(i => i.ItemDefinitionId == item && i.Container == Inventory.Inventory.EquippedContainer) == true;

        /// <summary>Is the armour equipped together with a weapon of its affinity (its Maximum Affinity Effect is on)?</summary>
        public static bool Wields(WorldSession p, uint armourItem, Affinity weaponAffinity)
            => armour.TryGetValue(armourItem, out Affinity a) && a == weaponAffinity && Has(p, armourItem)
               && WeaponCombat.AffinityOf(Abilities.HeldWeapon(p)) == a;

        private static bool Wields(WorldSession p, uint armourItem) => Wields(p, armourItem, armour[armourItem]);

        private static bool Roll(float chance, float strength = 1f) => Random.Shared.NextDouble() < chance * strength;

        private const float Chance = 0.3f, Nearby = 5f;

        /// <summary>Plate of Unrelenting Vengeance's revenge: more damage when it triggers (hotfix notes 26 Apr 2016)
        /// than the 0.3 share of the other armours [I].</summary>
        private const float VengeanceShare = 0.45f;

        /// <summary>
        /// Supercooled Power Armor's shatter (26 Apr 2016 notes, "Armor Passive: Supercooled Shatter": a much larger
        /// area, less damage): 8 m and 0.35 of the weapon damage, against the other bursts'
        /// 5 m / 0.5 [I].
        /// </summary>
        private const float ShatterRadius = 8f, ShatterShare = 0.35f;

        /// <summary>
        /// Reinforced Coat of Crashing Waves (575669; 7 Jun 2016, P5748: less damage and range, no more damage
        /// reduction, an ice explosion and hit flashes added): 30 % when an opponent hits
        /// you, 0.4 of the weapon damage (below the other bursts' 0.5) and Freeze on everyone within 5 m. The old and new
        /// numbers are not in any source (L90), so these are ours [I]. The ice explosion is the client's own ice blast
        /// <see cref="IceExplosion"/> on the wearer; the hit flashes are the ice impact <see cref="IceHitFlash"/> on each
        /// opponent hit. Neither client build has an armour-specific ice effect (0.695 adds no combat effects), so the
        /// two ids are our choice [I].
        /// </summary>
        private const float CrashingWavesRadius = 5f, CrashingWavesShare = 0.4f;

        /// <summary>PFX_Combat_Ice_Staff_SpecialAttack_Explosion (FX 1091 Combat_Ice_ThirdAbility_SphereBlast).</summary>
        private const uint IceExplosion = 11533;

        /// <summary>PFX_Combat_Misc_Impact_Ice_NonDirectional (FX 1392).</summary>
        private const uint IceHitFlash = 10114;

        /// <summary>
        /// Armored Brawler's Doublet: "Harden now explodes" — the client's own effect for it, PFX_Combat_Physical_Passive_
        /// Explosion_5m (12732, FX 2189 "Combat_Earth_Harden_Explode_5m" + 2190 a shockwave; ActorFxDefinitions.xml, in
        /// both builds), sized for the 5 m the burst reaches. [P] effect name; the 5 m match is [I].
        /// </summary>
        private const uint HardenExplosion = 12732;

        /// <summary>PFX_Combat_Misc_Impact_Physical (the Earth impact on each opponent of a shockwave).</summary>
        private const uint EarthHit = 11285;

        /// <summary>Brooch of the Boundless Eye: "Energize grants more energy regeneration".</summary>
        public static double EnergizeRegenFactor(WorldSession p) => Has(p, BroochOfTheBoundlessEye) ? 1.6 : 1.0;

        /// <summary>
        /// After a hit landed (target still alive; <paramref name="lifeBefore"/> its health fraction before the hit): the
        /// attacker's armour effects that react to hits.
        /// </summary>
        public static void OnHit(WeaponCombat.Hit h, float lifeBefore)
        {
            var s = h.Swing;
            WorldSession p = s.Player;
            ulong t = h.Target;
            float life = Creatures.HealthFraction(t);
            Vector3? at = Creatures.PositionOfGuid(t);
            switch (s.Kit.Affinity)
            {
                case Affinity.Fire:
                    if (Statuses.Has(t, Status.Burn) && Wields(p, FlameBoundCuirass) && Roll(Chance, s.Strength) && at is Vector3 a1)
                    {
                        WeaponCombat.Burst(p, a1, Nearby, 0.5f, Status.Burn, 11059);           // ignite: spread Burn
                        Fired(p, FlameBoundCuirass, "ignite, Burn spread within 5 m");
                    }
                    if (Statuses.Has(p.PlayerGuid, Status.Frenzy) && Wields(p, FrenziedSlayersChainmail) && life < 0.2f && Roll(0.25f, s.Strength))
                    {
                        Creatures.Execute(p, t);
                        Fired(p, FrenziedSlayersChainmail, "executed a Frenzied hit's target below 20 % life");
                    }
                    if (Statuses.Has(p.PlayerGuid, Status.Frenzy) && Statuses.Has(t, Status.Burn) && Wields(p, DragonfireRitualRobes)
                        && Statuses.Apply(p.PlayerGuid, Status.Eruption, p))
                        Fired(p, DragonfireRitualRobes, "every attack Erupts for 4 s");
                    break;
                case Affinity.Water:
                    if (!s.Power && Statuses.Has(t, Status.Freeze) && Wields(p, SupercooledPowerArmor) && Roll(Chance, s.Strength) && at is Vector3 a2)
                    {
                        WeaponCombat.Burst(p, a2, ShatterRadius, ShatterShare, Status.Freeze, 11533); // shatter: spread Freeze
                        Fired(p, SupercooledPowerArmor, "shatter, Freeze spread within 8 m");
                    }
                    break;
                case Affinity.Lightning:
                    if (s.Power && Statuses.Has(t, Status.Electrocute) && Wields(p, OverchargedReconGear) && at is Vector3 a3)
                    {
                        WeaponCombat.Burst(p, a3, Nearby, 0.5f, Status.Electrocute, 11701);    // electric field
                        Fired(p, OverchargedReconGear, "electric field, Electrocute within 5 m");
                    }
                    break;
                case Affinity.Earth:
                    // Tectonic Destroyer's Platemail (575678): "hitting a Crushed opponent can create a shockwave".
                    // Gunslinger's Last Resort (575664) fires when you Crush someone instead: OnApplied.
                    if (Statuses.Has(t, Status.Crush) && Wields(p, TectonicDestroyersPlatemail) && Roll(Chance, s.Strength) && at is Vector3 a4)
                    {
                        WeaponCombat.Burst(p, a4, Nearby, 0.5f, Status.Crush, EarthHit);       // shockwave
                        Fired(p, TectonicDestroyersPlatemail, "shockwave, Crush within 5 m");
                    }
                    break;
                case Affinity.Affliction:
                    if (Statuses.Has(t, Status.Rot) && life < 0.2f && Wields(p, PoisonDrenchedLeathers))
                    {
                        Creatures.Execute(p, t);
                        Fired(p, PoisonDrenchedLeathers, "Rot executed a target below 20 % life");
                    }
                    else if (Statuses.Has(t, Status.Rot) && Wields(p, BlightWardensSuffering) && Roll(Chance, s.Strength) && at is Vector3 a5)
                    {
                        WeaponCombat.Burst(p, a5, Nearby, 0.5f, Status.Rot, 11638);            // infect all nearby
                        Fired(p, BlightWardensSuffering, "Rot spread within 5 m with bonus damage");
                    }
                    break;
                case Affinity.Radiance:
                    ulong me = p.PlayerGuid;
                    if (Statuses.Has(me, Status.Illuminate) && Statuses.Has(me, Status.Exalt) && Wields(p, MantleOfBoundlessLight)
                        && s.Hits.Count == 1)
                    {
                        Creatures.Damage(p, t, s.Damage, true);                                // radiant beam at the first opponent hit
                        Fired(p, MantleOfBoundlessLight, $"radiant beam for {s.Damage}");
                    }
                    if (s.Power && Statuses.Has(me, Status.Exalt) && Wields(p, LightbringersOath) && WeaponCombat.FirstPowerWhileExalted(p) && at is Vector3 a6)
                    {
                        WeaponCombat.Burst(p, a6, Nearby, 0.6f, null, 11649);                  // radiant burst
                        Fired(p, LightbringersOath, "radiant burst within 5 m of the target");
                    }
                    if (Statuses.Has(me, Status.Illuminate) && Wields(p, CrusadersCalling) && Roll(Chance, s.Strength))
                    {
                        int beams = 0;
                        foreach (ulong o in Creatures.InCone(p.ZoneId, p.Position, s.Forward, 15f, 45f).Where(o => o != t))
                        {
                            Creatures.Damage(p, o, s.Damage / 2, true);                         // radiant beams in front
                            beams++;
                        }
                        Fired(p, CrusadersCalling, $"radiant beams on {beams} opponent(s) in front");
                    }
                    break;
                case Affinity.Shadow:
                    if (Statuses.Has(t, Status.Torment) && life < 0.2f && Wields(p, SilentShadowsCloak))
                    {
                        Creatures.Execute(p, t);
                        Fired(p, SilentShadowsCloak, "executed a Tormented target below 20 % life");
                    }
                    // Night Herald's Promise (text 575681; P5741, 7 Jun 2016): only the first Power Attack against an
                    // opponent while it is Condemned bursts; Condemn put on again allows one more.
                    if (s.Power && Wields(p, NightHeraldsPromise) && s.Hits.Count == 1 && at is Vector3 a7
                        && Statuses.AppliedAt(t, Status.Condemn) is DateTime condemned && condemned != default
                        && FirstPowerOnCondemned(p.PlayerGuid, t, condemned))
                    {
                        WeaponCombat.Burst(p, a7, Nearby, 0.6f, null, 11681);                  // shadow burst
                        Fired(p, NightHeraldsPromise, "shadow burst within 5 m of the Condemned target");
                    }
                    break;
            }
        }

        /// <summary>(player, opponent) → the Condemn the player last burst on (its application time).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ulong Player, ulong Target), DateTime> heraldBursts = new();

        private static bool FirstPowerOnCondemned(ulong player, ulong target, DateTime condemned)
        {
            if (heraldBursts.TryGetValue((player, target), out DateTime last) && last == condemned)
                return false;
            heraldBursts[(player, target)] = condemned;
            if (heraldBursts.Count > 4096)
                foreach (var key in heraldBursts.Where(e => DateTime.UtcNow - e.Value > TimeSpan.FromMinutes(5)).Select(e => e.Key).ToList())
                    heraldBursts.TryRemove(key, out _);
            return true;
        }

        /// <summary>A state was just put on an opponent by the player's attack.</summary>
        public static void OnApplied(WeaponCombat.Hit h, Status st)
        {
            WorldSession p = h.Swing.Player;
            if (st == Status.Rot && Wields(p, DiabolicalScientistsCoat) && Roll(Chance, h.Swing.Strength)
                && Creatures.PositionOfGuid(h.Target) is Vector3 at)
            {
                WeaponCombat.Burst(p, at, Nearby, 0.4f, Status.Rot, 11638);                    // Rot festers and spreads
                Fired(p, DiabolicalScientistsCoat, "Rot festered and spread within 5 m");
            }
            if (st == Status.Electrocute && Statuses.Has(p.PlayerGuid, Status.Supercharge) && Wields(p, VestmentOfTheLightningCaller))
            {
                Statuses.Apply(h.Target, Status.Overloaded, p);                                  // periodically Electrocuted
                Fired(p, VestmentOfTheLightningCaller, "target Overloaded (Electrocuted again each second for 4 s)");
            }
            // Gunslinger's Last Resort (575664): "opponents you Crush can be hit with a shockwave that deals damage and
            // Crushes all nearby opponents" — when the player's attack Crushes someone (was: on hitting an already
            // Crushed opponent, which is Tectonic Destroyer's Platemail's text). The shockwave's own Crush goes through
            // Statuses.Apply, so it can't chain.
            if (st == Status.Crush && Wields(p, GunslingersLastResort) && Roll(Chance, h.Swing.Strength)
                && Creatures.PositionOfGuid(h.Target) is Vector3 crushed)
            {
                WeaponCombat.Burst(p, crushed, Nearby, 0.5f, Status.Crush, EarthHit);         // shockwave
                Fired(p, GunslingersLastResort, "shockwave, Crush within 5 m");
            }
        }

        /// <summary>
        /// A creature hit the player (before the damage lands): the defensive armour effects. Returns the damage factor.
        /// <paramref name="hardened"/>: the player had Harden when the hit came (WeaponCombat.Incoming has just used it up,
        /// so it can't be asked from Statuses any more).
        /// </summary>
        public static float OnHurt(WorldSession p, ulong attacker, bool hardened = false)
        {
            float f = 1f;
            ulong me = p.PlayerGuid;
            Affinity held = WeaponCombat.AffinityOf(Abilities.HeldWeapon(p));
            switch (held)
            {
                case Affinity.Fire:
                    break;
                case Affinity.Water:
                    // Reinforced Coat of Crashing Waves (575669, P5748): the ice explosion on the wearer, then damage,
                    // Freeze and an ice hit flash on every opponent within reach (CrashingWavesRadius).
                    if (Wields(p, ReinforcedCoatOfCrashingWaves) && Roll(Chance))
                    {
                        HitEffects.PlayOn(me, IceExplosion, Statuses.Viewers(me));
                        WeaponCombat.Burst(p, p.Position, CrashingWavesRadius, CrashingWavesShare, Status.Freeze, IceHitFlash);
                        Fired(p, ReinforcedCoatOfCrashingWaves, $"ice explosion, damage and Freeze within {CrashingWavesRadius:0} m");
                    }
                    if (Wields(p, FrostLordsGuard))
                    {
                        WeaponCombat.Revenge(p, attacker);
                        bool freeze = Statuses.AtMax(me, Status.Flow) && attacker != 0;
                        if (freeze)
                            Statuses.Apply(attacker, Status.Freeze, p);
                        Fired(p, FrostLordsGuard, freeze ? "revenge damage, attacker Frozen (Flow at max)" : "revenge damage");
                    }
                    break;
                case Affinity.Lightning:
                    if (Statuses.Has(me, Status.Supercharge) && Wields(p, ManifestationOfTheInnerSpark))
                    {
                        f *= 0.8f;
                        WeaponAttacks.GainEnergy(p, 20);
                        if (attacker != 0)
                            Statuses.Apply(attacker, Status.Electrocute, p);
                        Fired(p, ManifestationOfTheInnerSpark, "-20 % damage taken, +20 Energy, attacker Electrocuted");
                    }
                    int energize = Statuses.Stacks(me, Status.Energize);
                    if (energize > 0 && Wields(p, ElectrifiedAssaultExoSuit))
                    {
                        f *= 1f - 0.05f * energize;
                        WeaponCombat.Revenge(p, attacker, 0.06f * energize);
                        Fired(p, ElectrifiedAssaultExoSuit, $"{energize} Energize: -{5 * energize} % damage taken, revenge damage");
                    }
                    break;
                case Affinity.Earth:
                    // Harden was used up by this hit (WeaponCombat.Incoming), hence the flag, not Statuses.Has.
                    if (hardened && Wields(p, ArmoredBrawlersDoublet))
                    {
                        HitEffects.PlayOn(me, HardenExplosion, Statuses.Viewers(me));             // Harden explodes
                        WeaponCombat.Burst(p, p.Position, Nearby, 0.5f, Status.Crush, EarthHit);
                        Fired(p, ArmoredBrawlersDoublet, "Harden exploded: damage and Crush within 5 m");
                    }
                    if (hardened && Wields(p, UnflinchingEmbodimentOfStone))
                    {
                        WeaponCombat.Revenge(p, attacker);
                        Fired(p, UnflinchingEmbodimentOfStone, "Harden stopped the whole hit, revenge damage");
                    }
                    break;
                case Affinity.Affliction:
                    if (Wields(p, DesperadosDeadlyPoncho))
                    {
                        WeaponCombat.Revenge(p, attacker);
                        bool rot = Statuses.AtMax(me, Status.Infect);
                        if (rot)
                            WeaponCombat.Burst(p, p.Position, Nearby, 0f, Status.Rot, 0);
                        Fired(p, DesperadosDeadlyPoncho, rot ? "revenge damage, Rot within 5 m (Infect at max)" : "revenge damage");
                    }
                    break;
                case Affinity.Radiance:
                    if (Statuses.Has(me, Status.Exalt) && Wields(p, AvengingChampionsAegis))
                    {
                        f *= 0.8f;
                        WeaponCombat.Burst(p, p.Position, Nearby, 0.3f, null, 11649);          // radiant beams round you
                        Fired(p, AvengingChampionsAegis, "-20 % damage taken, radiant beams within 5 m");
                    }
                    break;
                case Affinity.Shadow:
                    // Plate of Unrelenting Vengeance (26 Apr 2016 notes): fires on any hit (no low-life / Tormented
                    // condition), deals more than the other armours' revenge, and not again while its Siphon window
                    // runs (WeaponCombat.TryVengeance).
                    if (Wields(p, PlateOfUnrelentingVengeance) && WeaponCombat.TryVengeance(p))
                    {
                        WeaponCombat.Revenge(p, attacker, VengeanceShare);
                        Fired(p, PlateOfUnrelentingVengeance, "revenge damage, Siphon doubled for 4 s");
                    }
                    break;
            }
            return f;
        }
    }
}
