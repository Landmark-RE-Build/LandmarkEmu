using LandmarkEmulator.WorldServer.Network;
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
    /// The chances and sizes are ours (server data); the effects follow each text.
    /// </summary>
    public static class Gear
    {
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
                        WeaponCombat.Burst(p, a1, Nearby, 0.5f, Status.Burn, 11059);           // ignite: spread Burn
                    if (Statuses.Has(p.PlayerGuid, Status.Frenzy) && Wields(p, FrenziedSlayersChainmail) && life < 0.2f && Roll(0.25f, s.Strength))
                        Creatures.Execute(p, t);
                    if (Statuses.Has(p.PlayerGuid, Status.Frenzy) && Statuses.Has(t, Status.Burn) && Wields(p, DragonfireRitualRobes))
                        Statuses.Apply(p.PlayerGuid, Status.Eruption, p);
                    break;
                case Affinity.Water:
                    if (!s.Power && Statuses.Has(t, Status.Freeze) && Wields(p, SupercooledPowerArmor) && Roll(Chance, s.Strength) && at is Vector3 a2)
                        WeaponCombat.Burst(p, a2, Nearby, 0.5f, Status.Freeze, 11533);         // shatter: spread Freeze
                    break;
                case Affinity.Lightning:
                    if (s.Power && Statuses.Has(t, Status.Electrocute) && Wields(p, OverchargedReconGear) && at is Vector3 a3)
                        WeaponCombat.Burst(p, a3, Nearby, 0.5f, Status.Electrocute, 11701);    // electric field
                    break;
                case Affinity.Earth:
                    if (Statuses.Has(t, Status.Crush) && (Wields(p, GunslingersLastResort) || Wields(p, TectonicDestroyersPlatemail))
                        && Roll(Chance, s.Strength) && at is Vector3 a4)
                        WeaponCombat.Burst(p, a4, Nearby, 0.5f, Status.Crush, 11285);          // shockwave
                    break;
                case Affinity.Affliction:
                    if (Statuses.Has(t, Status.Rot) && life < 0.2f && Wields(p, PoisonDrenchedLeathers))
                        Creatures.Execute(p, t);
                    else if (Statuses.Has(t, Status.Rot) && Wields(p, BlightWardensSuffering) && Roll(Chance, s.Strength) && at is Vector3 a5)
                        WeaponCombat.Burst(p, a5, Nearby, 0.5f, Status.Rot, 11638);            // infect all nearby
                    break;
                case Affinity.Radiance:
                    ulong me = p.PlayerGuid;
                    if (Statuses.Has(me, Status.Illuminate) && Statuses.Has(me, Status.Exalt) && Wields(p, MantleOfBoundlessLight)
                        && s.Hits.Count == 1)
                        Creatures.Damage(p, t, s.Damage, true);                                // radiant beam at the first opponent hit
                    if (s.Power && Statuses.Has(me, Status.Exalt) && Wields(p, LightbringersOath) && WeaponCombat.FirstPowerWhileExalted(p) && at is Vector3 a6)
                        WeaponCombat.Burst(p, a6, Nearby, 0.6f, null, 11649);                  // radiant burst
                    if (Statuses.Has(me, Status.Illuminate) && Wields(p, CrusadersCalling) && Roll(Chance, s.Strength))
                        foreach (ulong o in Creatures.InCone(p.ZoneId, p.Position, s.Forward, 15f, 45f).Where(o => o != t))
                            Creatures.Damage(p, o, s.Damage / 2, true);                         // radiant beams in front
                    break;
                case Affinity.Shadow:
                    if (Statuses.Has(t, Status.Torment) && life < 0.2f && Wields(p, SilentShadowsCloak))
                        Creatures.Execute(p, t);
                    if (s.Power && Statuses.Has(t, Status.Condemn) && Wields(p, NightHeraldsPromise) && s.Hits.Count == 1 && at is Vector3 a7)
                        WeaponCombat.Burst(p, a7, Nearby, 0.6f, null, 11681);                  // shadow burst
                    break;
            }
        }

        /// <summary>A state was just put on an opponent by the player's attack.</summary>
        public static void OnApplied(WeaponCombat.Hit h, Status st)
        {
            WorldSession p = h.Swing.Player;
            if (st == Status.Rot && Wields(p, DiabolicalScientistsCoat) && Roll(Chance, h.Swing.Strength)
                && Creatures.PositionOfGuid(h.Target) is Vector3 at)
                WeaponCombat.Burst(p, at, Nearby, 0.4f, Status.Rot, 11638);                    // Rot festers and spreads
            if (st == Status.Electrocute && Statuses.Has(p.PlayerGuid, Status.Supercharge) && Wields(p, VestmentOfTheLightningCaller))
                Statuses.Apply(h.Target, Status.Overloaded, p);                                  // periodically Electrocuted
        }

        /// <summary>
        /// A creature hit the player (before the damage lands): the defensive armour effects. Returns the damage factor.
        /// </summary>
        public static float OnHurt(WorldSession p, ulong attacker)
        {
            float f = 1f;
            ulong me = p.PlayerGuid;
            Affinity held = WeaponCombat.AffinityOf(Abilities.HeldWeapon(p));
            switch (held)
            {
                case Affinity.Fire:
                    break;
                case Affinity.Water:
                    if (Wields(p, ReinforcedCoatOfCrashingWaves) && Roll(Chance))
                        WeaponCombat.Burst(p, p.Position, Nearby, 0.4f, Status.Freeze, 10114);
                    if (Wields(p, FrostLordsGuard))
                    {
                        WeaponCombat.Revenge(p, attacker);
                        if (Statuses.AtMax(me, Status.Flow) && attacker != 0)
                            Statuses.Apply(attacker, Status.Freeze, p);
                    }
                    break;
                case Affinity.Lightning:
                    if (Statuses.Has(me, Status.Supercharge) && Wields(p, ManifestationOfTheInnerSpark))
                    {
                        f *= 0.8f;
                        WeaponAttacks.GainEnergy(p, 20);
                        if (attacker != 0)
                            Statuses.Apply(attacker, Status.Electrocute, p);
                    }
                    int energize = Statuses.Stacks(me, Status.Energize);
                    if (energize > 0 && Wields(p, ElectrifiedAssaultExoSuit))
                    {
                        f *= 1f - 0.05f * energize;
                        WeaponCombat.Revenge(p, attacker, 0.06f * energize);
                    }
                    break;
                case Affinity.Earth:
                    if (Statuses.Has(me, Status.Harden) && Wields(p, ArmoredBrawlersDoublet))
                        WeaponCombat.Burst(p, p.Position, Nearby, 0.5f, Status.Crush, 11285);  // Harden explodes
                    if (Statuses.Has(me, Status.Harden) && Wields(p, UnflinchingEmbodimentOfStone))
                        WeaponCombat.Revenge(p, attacker);
                    break;
                case Affinity.Affliction:
                    if (Wields(p, DesperadosDeadlyPoncho))
                    {
                        WeaponCombat.Revenge(p, attacker);
                        if (Statuses.AtMax(me, Status.Infect))
                            WeaponCombat.Burst(p, p.Position, Nearby, 0f, Status.Rot, 0);
                    }
                    break;
                case Affinity.Radiance:
                    if (Statuses.Has(me, Status.Exalt) && Wields(p, AvengingChampionsAegis))
                    {
                        f *= 0.8f;
                        WeaponCombat.Burst(p, p.Position, Nearby, 0.3f, null, 11649);          // radiant beams round you
                    }
                    break;
                case Affinity.Shadow:
                    if (Wields(p, PlateOfUnrelentingVengeance))
                        WeaponCombat.Revenge(p, attacker);
                    break;
            }
            return f;
        }
    }
}
