using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Weapons in creatures' hands (OPEN-LEADS L95; FEATURES "Story Node" / "Creature spawners" weapon visuals;
    /// trace/0695/agent-assets.md §2). <b>A best guess, on by default</b> (user decision 2026-10-18: "if the creature
    /// weapon table can not be found just do the best guess"). Tags: [P] = from the 0.695 client's own data, [W] = the
    /// fandom wiki / forum / dev posts (design/wiki, design/forum), [I] = inferred (ours).
    /// <para>
    /// <b>How the client shows one</b> [P]: EquipmentSetCharacterEquipmentSlot (0xCB, handler FUN_142e42da0) for a guid
    /// other than the player's own finds the entity through FUN_142cd1cb0 (the entity table full NPCs are in, L96), clears
    /// the slot (actor vtable +0x498), and when the attachment's model string is not empty creates / attaches it
    /// (FUN_142ebd620, the same per-slot attach the full NPC data's attachment list +0x28 and players' equipment use); the
    /// slot must be above the invalid sentinel DAT_1455fe56c, and the item guid is only recorded where the entity has an
    /// equipment model (FUN_142ea49e0 returns at once without one: creatures). So the packet goes only to players that
    /// have the creature as a full entity (<see cref="Creatures.IsFullFor"/>), once after the full data and again when a
    /// story creature equips / sheathes; an empty model takes the weapon away. Slot 2 = EquipmentSlotDefinitions "Main
    /// Hand" (561007). Bow creatures also get a quiver on attach slot 11 with outer slot still 2 (Courage capture;
    /// agent-creature-quiver.md). Sword-and-shield creatures still show the sword only (no NPC shield packets in that
    /// session; Off Hand shields remain open).
    /// </para>
    /// <para>
    /// A weapon actor also sets its AnimationEnumerations on the holder's network, as for players (Wield_*.adr: hash
    /// −1452685539 WeaponIdle, −775574123 Class = AnimEnums "Class", 2090366532 Weapon), e.g. HorseBow Class 1011
    /// BowUser; the NPC attack selections (AnimationSelection 356-359) set Class 1 themselves, so the held actor mostly
    /// changes the idle pose (runtime check).
    /// </para>
    /// <para>
    /// <b>What live gave each creature is not fully in the client</b> (searched, agent-assets.md §2: the creature .adr
    /// files have empty EquipType / ChildAttachSlotsEx and no Wield_* reference; no table ties a creature, its model or
    /// its prop to a Wield_*.adr; the prop list has no attachment field). One Courage combat session
    /// (<c>5IewoKPQE2BqKg6Q</c>) gave live <c>Wield_*</c> actors by model id (NetworkCaptures/_research/combat-loot-probe/);
    /// the table calibrates shared type defaults from that [P capture] and keeps affinity/epic [W]/[I] guesses where
    /// capture did not contradict. Built from:
    /// <list type="number">
    /// <item><b>The weapon type [P]</b>: every creature prop's palette icon names it — ImageSetMappings → Images.txt of
    /// the prop's image set: "Barren_Sword_Fire", "Edbonguard_Daggers_Earth", "Scalewind_Bow_Water",
    /// "Lumerian_Blaster_Lightning", "Giant_Club_Earth", "Highfall_Swordshield_Shadow" ... (types Bow, Sword, Broadsword,
    /// Swordshield, Staff, Daggers, Blaster, Pulverizer, Club, and Nothing for the monsters, animals, giants, hounds and
    /// wisps); the dev post of 15 Mar 2016 (Domino, "Build Softly and Carry a Big Stick") says so: the icons show the
    /// creature's kind, weapon and damage type. The Barren skeleton's animation override set
    /// (AnimationOverrides.xml "Skeleton") replaces the Core Adventurer Sword1H attacks, so its swords are 1H [P].</item>
    /// <item><b>The actor [P capture] / [W] / [I]</b>, in this order: the Courage capture's preferred actor for that weapon
    /// type (HorseBow, Dwarf staff / 1H / 2H, DarkElf dagger, Ogre 2H, …); else the players' weapon of the creature's
    /// type and affinity (wiki "Creature Prop"; items.json affinities); else the art set of the creature's own armour
    /// (Qeynos for Highfall, DarkElf for Ebonguard, Colony for NovaTech, Ogre for Stoneskin); else a plain actor of that
    /// type. The tint is the item's own when it is a player weapon. Wiki pages that name a weapon are in the row's
    /// proof.</item>
    /// <item><b>Only attachable actors</b>: every model is a Wield_*.adr in the 0.695 packs with its .dme mesh
    /// (LandmarkEmulator.Tests checks the list). The kobold spear (Wield_Polearm_Combine_KoboldSpear_000_Spear_000.dme)
    /// has no .adr, so it cannot be attached. Creatures whose icon says Nothing get none.</item>
    /// </list>
    /// Rows are per creature prop (the v2 creature props players place, with their ruin-template twins
    /// landmark_ruin_npc_*); the ruin spawners ("Spawner: Ruins: Barren Melee" ..., the outposts' garrisons) take the row
    /// of the creature of their model and role (<see cref="RuinSpawners"/> [I]); any other creature of a listed model
    /// (dev "creature" kinds) takes its model's default (<see cref="ForModel"/>).
    /// </para>
    /// <para>On unless WorldServer.json <c>CreatureWeapons</c> (Docker CREATURE_WEAPONS) is false; dev "creatureweapon"
    /// attaches any Wield_*.adr to a creature for testing either way.</para>
    /// </summary>
    public static class CreatureWeapons
    {
        /// <summary>One weapon: the actor file, its tint alias ("" = the actor's own default) and why this one.</summary>
        public sealed record Weapon(string Model, string Tint, string Proof);

        /// <summary>
        /// One creature kind: its name (PropDefs text), its model (Models.txt id, the creature prop's model in the prop
        /// list), the creature props that make it (the v2 prop first, then its ruin-template twins) and its weapon.
        /// </summary>
        public sealed record Row(string Creature, uint Model, uint[] Props, Weapon Weapon);

        /// <summary>EquipmentSlotDefinitions 2 "Main Hand" (name 561007) [P].</summary>
        public const uint MainHandSlot = 2;
        /// <summary>EquipmentSlotDefinitions 11 "Off Hand" (name 561008) [P]: quiver attach key (+0x188) [P capture].</summary>
        public const uint OffHandSlot = 11;

        // ---- Weapon actors (model, tint). The players' items are named (items.json 0.695 id: affinity). ----------------

        /// <summary>Kobold great staff, no item uses it [P names] (1023); boss guess only — not seen in Courage capture.</summary>
        private static readonly (string, string) KoboldGreatStaff = ("Wield_Staff_Kobold_Kobold_D_A1_000.adr", "");
        /// <summary>DarkElf-made bow, no item uses it (Class 1011).</summary>
        private static readonly (string, string) DarkElfBow = ("Wield_Bow_DarkElf_DarkElf_B_A4_000.adr", "");
        /// <summary>3761 Wanderer's Blazing Bow: Fire (1011).</summary>
        private static readonly (string, string) FireBow = ("Wield_Bow_Epic_Fire_01_Fire_Fire_000.adr", "");
        /// <summary>3028 Lightbringer's Justice: Radiance (1026 HeavenlyBow).</summary>
        private static readonly (string, string) RadianceBow = ("Wield_Bow_Epic_Heavenly_01_Heavenly_000.adr", "");
        /// <summary>4089 Night Herald's Mercy: Shadow (1011).</summary>
        private static readonly (string, string) ShadowBow = ("Wield_Bow_Epic_Physical_01_Physical_Physical_000.adr", "");
        /// <summary>Live NPC bow default (Courage 5Iewo… ×9; 1021 GreatBow) [P capture].</summary>
        private static readonly (string, string) PlainBow = ("Wield_Bow_HorseBow_HorseBow_B_A3_001.adr", "");
        /// <summary>Halas quiver paired with every live NPC HorseBow (outer 2, attach 11) [P capture].</summary>
        private static readonly (string, string) PlainQuiver = ("Wield_Quiver_Halas_Halas_B_A3_000.adr", "");
        /// <summary>3764 Armored Brawler's Blade: Earth (1010 SwordUser).</summary>
        private static readonly (string, string) EarthSword = ("Wield_1HBlade_Dwarf_Dwarf_C_A0_000.adr", "Blade1H_Dwarf_05");
        /// <summary>4096 Crusader's Courage: Radiance (1010); also the Qeynos art set's sword.</summary>
        private static readonly (string, string) RadianceSword = ("Wield_1HBlade_Qeynos_Qeynos_D_A0_000.adr", "");
        private static readonly (string, string) QeynosSword = RadianceSword;
        /// <summary>3030 Flame Bound Bastion's blade: Fire (1013 SwordAndShield).</summary>
        private static readonly (string, string) FireSword = ("Wield_Blade1H_Epic_Fire_Fire_000.adr", "");
        /// <summary>3766 Armament of Unrelenting Vengeance's blade: Shadow (1016 ShadowSword).</summary>
        private static readonly (string, string) ShadowSword = ("Wield_Blade1H_Epic_Shadow_Shadow_000.adr", "");
        /// <summary>Live plain 1H (Courage Blade1H_…_B_A0_000 ×5) [P capture].</summary>
        private static readonly (string, string) PlainSword = ("Wield_Blade1H_Dwarf_Dwarf_B_A0_000.adr", "");
        /// <summary>3763 Falchion of the Inner Spark: Lightning (1014 TwoHandedSword); also the DarkElf art set's.</summary>
        private static readonly (string, string) Lightning2H = ("Wield_Blade2H_DarkElf_DarkElf_C_A1_000.adr", "Blade2H_DarkElf_05");
        private static readonly (string, string) DarkElf2H = Lightning2H;
        /// <summary>The giant-sized Ogre greatsword, no item uses it (1014); also live on model 1121 [P capture].</summary>
        private static readonly (string, string) Ogre2H = ("Wield_Blade2H_Ogre_Ogre_D_A1_0_001.adr", "");
        /// <summary>Live plain greatsword (Courage Dwarf 2H ×8) [P capture].</summary>
        private static readonly (string, string) Plain2H = ("Wield_Blade2H_Dwarf_Dwarf_D_A1_000.adr", "");
        /// <summary>The DarkElf dagger of 3765 Poison Drenched Daggers (Affliction; 1015 DualDagger); live ×2 [P capture].</summary>
        private static readonly (string, string) DarkElfDagger = ("Wield_Dagger_DarkElf_DarkElf_A_A5_000.adr", "Dagger_DarkElf_05");
        /// <summary>3762 Staff of Frozen Sorrow: Water (1012 StaffUser).</summary>
        private static readonly (string, string) WaterStaff = ("Wield_Staff_Keldarain_Keldarain_C_A1_000.adr", "Staff_Keldarain_07");
        /// <summary>4107 Staff of the Lightning Caller: Lightning (1022); live NPC staff default ×13 [P capture].</summary>
        private static readonly (string, string) LightningStaff = ("Wield_Staff_Dwarf_Dwarf_D_A1_000.adr", "Staff_Dwarf_01");
        /// <summary>The Epic Fire staff (the Fire weapons' art set), no item uses it (1025 FireStaff).</summary>
        private static readonly (string, string) FireStaff = ("Wield_Staff_Epic_Fire_Fire_000.adr", "");
        /// <summary>A staff no item uses (1012); live once as Keldarain D [P capture].</summary>
        private static readonly (string, string) PlainStaff = ("Wield_Staff_Keldarain_Keldarain_D_A1_000.adr", "");
        /// <summary>3033 Overcharged Recon Blaster: Lightning (Class 5 Gun; Colony art set).</summary>
        private static readonly (string, string) ColonyGun = ("Wield_Gun1H_Colony_Colony_A_A4_000.adr", "Gun1H_Colony_06");
        /// <summary>3036 Supercooled Power Cannon: Water (1027 LaserCannon; Colony art set).</summary>
        private static readonly (string, string) WaterCannon = ("Wield_Pulverizer_Colony_Colony_D_A2_000.adr", "");
        /// <summary>218 Atomic Pulverizer (1027; Colony art set).</summary>
        private static readonly (string, string) ColonyPulverizer = ("Wield_Pulverizer_Colony_Colony_E_A2_000.adr", "Pulverizer_Colony_07");
        /// <summary>3035 Unflinching Embodiment of Might: Earth (Class 8 Blunt1H; the VoidGoliath club).</summary>
        private static readonly (string, string) EarthClub = ("Wield_Blunt_VoidGoliath_VoidGoliath_E_A0.adr", "");
        /// <summary>The giant-sized Ogre hammer, no item uses it (1019 HammerUser).</summary>
        private static readonly (string, string) OgreHammer = ("Wield_Hammer2H_Ogre_Ogre_C_A1_000.adr", "");
        /// <summary>The Target Dummy's practice sword (bone MainHandWeapon, no Class).</summary>
        private static readonly (string, string) PracticeSword = ("Wield_Pick_Landmark_PracticeSword_Dummy.adr", "");

        private static Row R(uint prop, uint model, string creature, (string Model, string Tint) actor, string proof, params uint[] twins)
            => new(creature, model, new[] { prop }.Concat(twins).ToArray(), new Weapon(actor.Model, actor.Tint, proof));

        /// <summary>
        /// Every creature kind that holds a weapon: creature prop (and ruin twins) → weapon, with its proof. Built from the
        /// props' icons (see the class notes; scratch generator re-run 2026-10-18 over 145-PropBase, ImageSetMappings,
        /// Images.txt, PropDefs and items.json). Creatures whose icon says Nothing are not listed (no weapon).
        /// </summary>
        public static readonly IReadOnlyList<Row> Rows = new[]
        {
            R(2445, 1457, "Barren Bowman", PlainBow, "[P] icon Barren_Bow_Earth; [P capture Courage 5Iewo…] HorseBow; [I] capture type default", 2742),
            R(2446, 1457, "Barren Fireshot", FireBow, "[P] icon Barren_Bow_Fire; [W] affinity creatures carry the players' Fire weapons; [W] wiki \"Uses a flaming bow to attack\"; [I] Wanderer's Blazing Bow", 2743),
            R(2447, 1457, "Barren Shadowshot", ShadowBow, "[P] icon Barren_Bow_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Night Herald's Mercy", 2744),
            R(2448, 1457, "Barren Stormshot", PlainBow, "[P] icon Barren_Bow_Lightning; [P capture Courage 5Iewo…] HorseBow; [I] capture type default", 2746),
            R(2449, 1457, "Elite Barren Sergeant", PlainBow, "[P] icon Barren_Bow_Water; [P capture Courage 5Iewo…] HorseBow; [I] capture type default", 2747),
            R(2450, 1457, "Barren Grunt", EarthSword, "[P] icon Barren_Sword_Earth; [P] the Skeleton animation set replaces the Sword1H attacks; [W] affinity creatures carry the players' Earth weapons; [W] wiki \"Attacks with a greatsword\" (the icon says Sword, not Broadsword); [I] Armored Brawler's Blade", 2734),
            R(2451, 1457, "Barren Flameblade", FireSword, "[P] icon Barren_Sword_Fire; [P] the Skeleton animation set replaces the Sword1H attacks; [W] affinity creatures carry the players' Fire weapons; [W] wiki \"Uses a flaming greatsword to attack\" (the icon says Sword, not Broadsword); [I] Flame Bound Bastion's blade", 2736),
            R(2452, 1457, "Barren Icebrute", Plain2H, "[P] icon Barren_Broadsword_Water; [P capture Courage 5Iewo…] Dwarf 2H; [I] capture type default", 2738),
            R(2453, 1457, "Barren Protector", EarthSword, "[P] icon Barren_Swordshield_Earth; [P] the Skeleton animation set replaces the Sword1H attacks; [W] affinity creatures carry the players' Earth weapons; [W] wiki \"Can block damage with its shield\" (the shield is not sent: main hand only); [I] Armored Brawler's Blade", 2735),
            R(2454, 1457, "Elite Barren Lieutenant", ShadowSword, "[P] icon Barren_Swordshield_Shadow; [P] the Skeleton animation set replaces the Sword1H attacks; [W] affinity creatures carry the players' Shadow weapons; [I] Armament of Unrelenting Vengeance's blade", 2741),
            R(2455, 1457, "Elite Barren Captain", FireSword, "[P] icon Barren_Swordshield_Fire; [P] the Skeleton animation set replaces the Sword1H attacks; [W] affinity creatures carry the players' Fire weapons; [W] wiki: blocks with its shield (not sent: main hand only); [I] Flame Bound Bastion's blade", 2739),
            R(2456, 1457, "Barren Flamemage", FireStaff, "[P] icon Barren_Staff_Fire; [I] the Epic Fire staff (the Fire weapons' art set, no item uses it)", 2749),
            R(2457, 1457, "Barren Lightbringer", PlainStaff, "[P] icon Barren_Staff_Radiance; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2751),
            R(2458, 1457, "Barren Stormmancer", LightningStaff, "[P] icon Barren_Staff_Lightning; [W] affinity creatures carry the players' Lightning weapons; [P capture Courage 5Iewo…] Dwarf staff also live default; [I] Staff of the Lightning Caller", 2750),
            R(2459, 1457, "Barren Coldmage", WaterStaff, "[P] icon Barren_Staff_Water; [W] affinity creatures carry the players' Water weapons; [I] Staff of Frozen Sorrow", 2748),
            R(2460, 1457, "Elite Barren Mageking", FireStaff, "[P] icon Barren_Staff_Fire; [I] the Epic Fire staff (the Fire weapons' art set, no item uses it)", 2753),
            R(2461, 1573, "Scalewind Recruit", EarthSword, "[P] icon Scalewind_Sword_Earth; [W] affinity creatures carry the players' Earth weapons; [I] Armored Brawler's Blade", 2755),
            R(2462, 1573, "Scalewind Dropper", PlainSword, "[P] icon Scalewind_Sword_Affliction; [P capture Courage 5Iewo…] Blade1H Dwarf B; [I] capture type default", 2760),
            R(2463, 1573, "Scalewind Scout", PlainBow, "[P] icon Scalewind_Bow_Earth; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2757),
            R(2464, 1573, "Scalewind Iceclipper", PlainBow, "[P] icon Scalewind_Bow_Water; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2768),
            R(2465, 1571, "Scalewind Shaman", LightningStaff, "[P] icon Scalewind_Staff_Radiance; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2777),
            R(2466, 1571, "Scalewind Burner", LightningStaff, "[P] icon Scalewind_Staff_Fire; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2774),
            R(2467, 1571, "Scalewind Icer", LightningStaff, "[P] icon Scalewind_Staff_Water; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2776),
            R(2468, 1571, "Scalewind Zapper", LightningStaff, "[P] icon Scalewind_Staff_Lightning; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2781),
            R(2469, 1569, "Scalewind Marine", Plain2H, "[P] icon Scalewind_Broadsword_Earth; [W] wiki \"This creature wields a large sword\"; [P capture Courage 5Iewo…] Dwarf 2H; [I] capture type default", 2762),
            R(2470, 1569, "Scalewind Defender", EarthSword, "[P] icon Scalewind_Swordshield_Earth; [W] affinity creatures carry the players' Earth weapons; [I] Armored Brawler's Blade", 2754),
            R(2471, 1569, "Scalewind Nightstalker", PlainBow, "[P] icon Scalewind_Bow_Shadow; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2770),
            R(2472, 1569, "Scalewind Flamehunter", PlainBow, "[P] icon Scalewind_Bow_Fire; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2766),
            R(2473, 1575, "Elite Scalewind Ranger", PlainBow, "[P] icon Scalewind_Bow_Lightning; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2773),
            R(2474, 1575, "Elite Scalewind Icesmasher", Plain2H, "[P] icon Scalewind_Broadsword_Water; [P capture Courage 5Iewo…] Dwarf 2H; [I] capture type default", 2763),
            R(2475, 1575, "Elite Scalewind Nightblade", ShadowSword, "[P] icon Scalewind_Swordshield_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Armament of Unrelenting Vengeance's blade", 2764),
            R(2476, 1575, "Elite Scalewind Corruptor", KoboldGreatStaff, "[P] icon Scalewind_Staff_Affliction; [I] the kobold great staff (made for kobolds, no item uses it) for the boss", 2782),
            R(2477, 1022, "NovaTech Explorer", ColonyGun, "[P] icon Lumerian_Blaster_Lightning; [W] affinity creatures carry the players' Lightning weapons; [I] Overcharged Recon Blaster (Colony art set)", 2805),
            R(2478, 1664, "NovaTech Pioneer", ColonyPulverizer, "[P] icon Lumerian_Pulverizer_Fire; [I] the Atomic Pulverizer (Colony art set of the NovaTech suits)", 2812),
            R(2479, 1149, "NovaTech Scientist", ColonyGun, "[P] icon Lumerian_Blaster_Water; [I] Overcharged Recon Blaster (Colony art set)", 2813),
            R(2480, 1660, "Elite NovaTech Researcher", Plain2H, "[P] icon Lumerian_Broadsword_Radiance; [P capture Courage 5Iewo…] Dwarf 2H; [I] capture type default", 2814),
            R(2486, 1662, "Highfall Befouler", LightningStaff, "[P] icon Highfall_Staff_Affliction; [P capture Courage 5Iewo…] model 1662 wore Dwarf staff; [I] capture model default", 2798),
            R(2487, 1661, "Elite Highfall Witch", FireStaff, "[P] icon Highfall_Staff_Fire; [I] the Epic Fire staff (the Fire weapons' art set, no item uses it)", 2802),
            R(2488, 1122, "Highfall Frostblade", QeynosSword, "[P] icon HIghfall_Sword_Water; [I] the Qeynos sword (art set of the Highfall Qeynos armour)", 2787),
            R(2489, 1122, "Elite Highfall Warrior", FireSword, "[P] icon Highfall_Swordshield_Fire; [W] affinity creatures carry the players' Fire weapons; [I] Flame Bound Bastion's blade", 2793),
            R(2490, 1026, "Highfall Burstshot", PlainBow, "[P] icon Highfall_Bow_Lightning; [W] forum May 2016 \"the Highfall using the lightning bow\"; [P capture Courage 5Iewo…] HorseBow; [I] capture type default", 2795),
            R(2491, 1026, "Elite Highfall Wanderer", PlainBow, "[P] icon Highfall_Bow_Water; [P capture Courage 5Iewo…] HorseBow; [I] capture type default", 2797),
            R(2492, 1125, "Highfall Shockblade", Plain2H, "[P] icon Highfall_Broadsword_Lightning; [P capture Courage 5Iewo…] model 1125 wore Dwarf 2H (superseded affinity DarkElf 2H [I]); [I] capture model default", 2790),
            R(2493, 1125, "Elite Highfall Gladiator", ShadowSword, "[P] icon Highfall_Swordshield_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Armament of Unrelenting Vengeance's blade", 2792),
            R(2494, 1025, "Highfall Frostmancer", WaterStaff, "[P] icon Highfall_Staff_Water; [W] affinity creatures carry the players' Water weapons; [I] Staff of Frozen Sorrow", 2800),
            R(2495, 1025, "Elite Highfall Magician", FireStaff, "[P] icon Highfall_Staff_Fire; [I] the Epic Fire staff (the Fire weapons' art set, no item uses it)", 2803),
            R(2496, 1570, "Scalewind Thunderer", Lightning2H, "[P] icon Scalewind_Broadsword_Lightning; [W] affinity creatures carry the players' Lightning weapons; [I] Falchion of the Inner Spark", 2756),
            R(2497, 1570, "Scalewind Chiller", PlainSword, "[P] icon Scalewind_Swordshield_Water; [P capture Courage 5Iewo…] Blade1H Dwarf B; [I] capture type default", 2758),
            R(2498, 1570, "Scalewind Hunter", PlainBow, "[P] icon Scalewind_Bow_Earth; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2767),
            R(2499, 1570, "Scalewind Coldhunter", PlainBow, "[P] icon Scalewind_Bow_Water; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2765),
            R(2504, 1147, "NovaTech Colonist", ColonyGun, "[P] icon Lumerian_Blaster_Fire; [I] Overcharged Recon Blaster (Colony art set)", 2810),
            R(2505, 1147, "NovaTech Pilot", WaterCannon, "[P] icon Lumerian_Pulverizer_Water; [W] affinity creatures carry the players' Water weapons; [I] Supercooled Power Cannon (Colony art set)", 2811),
            R(2507, 1574, "Scalewind Darkrunt", ShadowSword, "[P] icon Scalewind_Swordshield_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Armament of Unrelenting Vengeance's blade", 2759),
            R(2508, 1574, "Scalewind Lightrunt", RadianceSword, "[P] icon Scalewind_Sword_Radiance; [W] affinity creatures carry the players' Radiance weapons; [I] Crusader's Courage", 2761),
            R(2509, 1574, "Scalewind Sparker", PlainBow, "[P] icon Scalewind_Bow_Lightning; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2771),
            R(2510, 1574, "Scalewind Toxifier", PlainBow, "[P] icon Scalewind_Bow_Affliction; [P capture Courage 5Iewo…] HorseBow (live NPC bows; superseded kobold [I]); [I] capture type default", 2772),
            R(2511, 1572, "Scalewind Smasher", LightningStaff, "[P] icon Scalewind_Staff_Earth; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2778),
            R(2512, 1572, "Scalewind Weakener", LightningStaff, "[P] icon Scalewind_Staff_Affliction; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2779),
            R(2513, 1572, "Scalewind Darkener", LightningStaff, "[P] icon Scalewind_Staff_Shadow; [P capture Courage 5Iewo…] Dwarf staff (live NPC staffs; superseded kobold [I]); [I] capture type default", 2775),
            R(2514, 1102, "Ebonguard Fireblade", DarkElfDagger, "[P] icon Edbonguard_Daggers_Fire; [W] wiki \"This creature prop wields a menacing blade\"; [P capture Courage 5Iewo…] DarkElf dagger; [I] the DarkElf dagger (dark elf art set)", 2822, 2908),
            R(2515, 1101, "Ebonguard Flashbrand", Lightning2H, "[P] icon Edbonguard_Broadsword_Lightning; [W] affinity creatures carry the players' Lightning weapons; [I] Falchion of the Inner Spark", 2823, 2909),
            R(2516, 1103, "Ebonguard Shadowslayer", DarkElfDagger, "[P] icon Edbonguard_Daggers_Shadow; [P capture Courage 5Iewo…] DarkElf dagger; [I] the DarkElf dagger (dark elf art set)", 2825, 2911),
            R(2517, 1103, "Elite Ebonguard Captain", DarkElf2H, "[P] icon Edbonguard_Broadsword_Water; [I] the DarkElf greatsword (dark elf art set)", 2826, 2912),
            R(2518, 1104, "Ebonguard Attacker", DarkElfDagger, "[P] icon Edbonguard_Daggers_Earth; [P capture Courage 5Iewo…] DarkElf dagger; [I] the DarkElf dagger (dark elf art set)", 2818, 2905),
            R(2519, 1104, "Ebonguard Brutalizer", PlainSword, "[P] icon Edbonguard_Swordshield_Affliction; [P capture Courage 5Iewo…] Blade1H Dwarf B; [I] capture type default", 2820, 2906),
            R(2520, 1107, "Ebonguard Firemancer", FireStaff, "[P] icon Edbonguard_Staff_Fire; [I] the Epic Fire staff (the Fire weapons' art set, no item uses it)", 2837, 2923),
            R(2521, 1105, "Ebonguard Priestess", PlainStaff, "[P] icon Edbonguard_Staff_Radiance; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2817, 2904),
            R(2522, 1106, "Ebonguard Deathdealer", PlainStaff, "[P] icon Edbonguard_Staff_Affliction; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2836, 2922),
            R(2523, 1108, "Elite Ebonguard Sorcerer", WaterStaff, "[P] icon Edbonguard_Staff_Water; [W] affinity creatures carry the players' Water weapons; [I] Staff of Frozen Sorrow", 2842, 2928),
            R(2524, 1587, "Ebonguard Nightcaller", PlainStaff, "[P] icon Edbonguard_Staff_Shadow; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2839, 2925),
            R(2525, 1109, "Elite Ebonguard Magi", FireStaff, "[P] icon Edbonguard_Staff_Fire; [I] the Epic Fire staff (the Fire weapons' art set, no item uses it)", 2841, 2927),
            R(2526, 1110, "Ebonguard Archer", DarkElfBow, "[P] icon Edbonguard_Bow_Earth; [I] the DarkElf bow (dark elf art set, no item uses it)", 2828, 2914),
            R(2527, 1110, "Ebonguard Skyshot", DarkElfBow, "[P] icon Edbonguard_Bow_Radiance; [I] the DarkElf bow (dark elf art set, no item uses it)", 2833, 2919),
            R(2528, 1111, "Ebonguard Flashshot", DarkElfBow, "[P] icon Edbonguard_Bow_Lightning; [I] the DarkElf bow (dark elf art set, no item uses it)", 2829, 2915),
            R(2529, 1111, "Elite Ebonguard Pointer", DarkElfBow, "[P] icon Edbonguard_Bow_Water; [I] the DarkElf bow (dark elf art set, no item uses it)", 2835, 2921),
            R(2530, 1046, "Ebonguard Sentinel", DarkElfDagger, "[P] icon Edbonguard_Daggers_Radiance; [P capture Courage 5Iewo…] DarkElf dagger; [I] the DarkElf dagger (dark elf art set)", 2824, 2910),
            R(2531, 1114, "Ebonguard Destroyer", DarkElf2H, "[P] icon Edbonguard_Broadsword_Earth; [I] the DarkElf greatsword (dark elf art set)", 2821, 2907),
            R(2532, 1115, "Ebonguard Sentry", DarkElfDagger, "[P] icon Edbonguard_Daggers_Earth; [P capture Courage 5Iewo…] DarkElf dagger; [I] the DarkElf dagger (dark elf art set)", 2815, 2902),
            R(2533, 1725, "Ebonguard Enforcer", DarkElf2H, "[P] icon Edbonguard_Broadsword_Shadow; [I] the DarkElf greatsword (dark elf art set)", 2996),
            R(2534, 1116, "Ebonguard Watcher", DarkElfDagger, "[P] icon Edbonguard_Daggers_Water; [P capture Courage 5Iewo…] DarkElf dagger; [I] the DarkElf dagger (dark elf art set)", 2816, 2903),
            R(2535, 1116, "Elite Ebonguard Commander", DarkElf2H, "[P] icon Edbonguard_Broadsword_Fire; [I] the DarkElf greatsword (dark elf art set)", 2827, 2913),
            R(2536, 1117, "Ebonguard Flashmage", LightningStaff, "[P] icon Edbonguard_Staff_Lightning; [W] affinity creatures carry the players' Lightning weapons; [P capture Courage 5Iewo…] Dwarf staff also live default; [I] Staff of the Lightning Caller", 2838, 2924),
            R(2537, 1118, "Ebonguard Toximancer", PlainStaff, "[P] icon Edbonguard_Staff_Affliction; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2840, 2926),
            R(2538, 1119, "Ebonguard Nightshot", DarkElfBow, "[P] icon Edbonguard_Bow_Shadow; [I] the DarkElf bow (dark elf art set, no item uses it)", 2831, 2917),
            R(2539, 1119, "Ebonguard Frostshot", DarkElfBow, "[P] icon Edbonguard_Bow_Water; [I] the DarkElf bow (dark elf art set, no item uses it)", 2830, 2916),
            R(2540, 1120, "Ebonguard Plaguerider", DarkElfBow, "[P] icon Edbonguard_Bow_Affliction; [I] the DarkElf bow (dark elf art set, no item uses it)", 2832, 2918),
            R(2541, 1120, "Elite Ebonguard Lightseeker", DarkElfBow, "[P] icon Edbonguard_Bow_Radiance; [I] the DarkElf bow (dark elf art set, no item uses it)", 2834, 2920),
            R(2591, 1148, "Highfall Conjuror", LightningStaff, "[P] icon Highfall_Staff_Shadow; [P capture Courage 5Iewo…] model 1148 wore Dwarf staff; [I] capture model default", 2799),
            R(2592, 1706, "Highfall Tempest", LightningStaff, "[P] icon Highfall_Staff_Lightning; [W] affinity creatures carry the players' Lightning weapons; [P capture Courage 5Iewo…] Dwarf staff also live default; [I] Staff of the Lightning Caller", 2801),
            R(2593, 1121, "Highfall Lightblade", RadianceSword, "[P] icon Highfall_Sword_Radiance; [W] affinity creatures carry the players' Radiance weapons; [I] Crusader's Courage", 2789),
            R(2594, 1121, "Highfall Forceguard", Plain2H, "[P] icon Highfall_Broadsword_Earth; [W] wiki \"This creature wields a large sword\"; [P capture Courage 5Iewo…] Dwarf 2H; [I] capture type default", 2784),
            R(2595, 1123, "Highfall Warden", QeynosSword, "[P] icon HIghfall_Sword_Lightning; [I] the Qeynos sword (art set of the Highfall Qeynos armour)", 2785),
            R(2596, 1123, "Highfall Darkguard", ShadowSword, "[P] icon HIghfall_Sword_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Armament of Unrelenting Vengeance's blade", 2786),
            R(2597, 1124, "Highfall Trooper", Plain2H, "[P] icon Highfall_Broadsword_Radiance; [P capture Courage 5Iewo…] Dwarf 2H; [I] capture type default", 2791),
            R(2598, 1124, "Highfall Centurion", EarthSword, "[P] icon Highfall_Sword_Earth; [W] affinity creatures carry the players' Earth weapons; [I] Armored Brawler's Blade", 2783),
            R(2599, 1150, "Highfall Bandit", PlainBow, "[P] icon Highfall_Bow_Earth; [P capture Courage 5Iewo…] HorseBow; [I] capture type default", 2794),
            R(2600, 1150, "Elite Highfall Outlaw", ShadowBow, "[P] icon Highfall_Bow_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Night Herald's Mercy", 2796),
            R(2601, 1615, "Stoneskin Groundsmasher", EarthClub, "[P] icon Giant_Club_Earth; [W] affinity creatures carry the players' Earth weapons; [I] Unflinching Embodiment of Might", 2843),
            R(2602, 1620, "Stoneskin Coldbringer", Ogre2H, "[P] icon Giant_Broadsword_Water; [P capture Courage 5Iewo…] Ogre 2H; [I] the Ogre greatsword (giant-sized art set, no item uses it)", 2845),
            R(2603, 1619, "Stoneskin Flamecrusher", OgreHammer, "[P] icon Giant_Club_Fire; [I] the Ogre hammer (giant-sized art set, no item uses it)", 2844),
            R(2604, 1619, "Stoneskin Stormlasher", Lightning2H, "[P] icon Giant_Broadsword_Lightning; [W] affinity creatures carry the players' Lightning weapons; [W] wiki \"This creature wields a lightning infused sword\"; [I] Falchion of the Inner Spark", 2846),
            R(2605, 1615, "Stoneskin Nightreaper", ShadowBow, "[P] icon Giant_Bow_Shadow; [W] affinity creatures carry the players' Shadow weapons; [I] Night Herald's Mercy", 2847),
            R(2606, 1618, "Stoneskin Riftmender", PlainStaff, "[P] icon Giant_Staff_Radiance; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2850),
            R(2607, 1608, "Stoneskin Starshooter", RadianceBow, "[P] icon Giant_Bow_Radiance; [W] affinity creatures carry the players' Radiance weapons; [I] Lightbringer's Justice", 2848),
            R(2608, 1620, "Stoneskin Banespitter", PlainStaff, "[P] icon Giant_Staff_Affliction; [P capture Courage 5Iewo…] Keldarain D seen once; [I] plain staff default", 2851),
            R(2615, 1609, "The Outcast", ColonyGun, "[P] icon Giant_Blaster_Lightning; [W] affinity creatures carry the players' Lightning weapons; [I] Overcharged Recon Blaster (Colony art set)", 2992),
            R(2890, 1708, "Target Dummy", PracticeSword, "[W] hotfix 11 May 2016: it \"will stand in one location and intermittently swing its sword\"; [I] the practice sword (PracticeSword_Dummy)", 2993),
        };

        /// <summary>
        /// The ruin spawners (PropCategory "Creatures" internal props the outposts' garrisons come from: "Spawner: Ruins:
        /// Barren Melee (100 Limit)" ...; no icon of their own) → the creature prop whose weapon they use: the creature of
        /// the spawner's own model whose icon fits the role in its name (Melee / Patroller a melee weapon, Ranged a bow or
        /// blaster, Caster a staff, BOSS an Elite of that model) [I]. NovaTech Hound and the Stoneskin giants (BOSS GIANTS)
        /// have none (their icons say Nothing).
        /// </summary>
        public static readonly IReadOnlyList<(uint Spawner, string Name, uint Model, uint As)> RuinSpawners = new[]
        {
            (2702u, "Barren Patroller", 1457u, 2450u),       // Barren Grunt
            (2703u, "Barren Melee", 1457u, 2450u),           // Barren Grunt
            (2704u, "Barren Ranged", 1457u, 2445u),          // Barren Bowman
            (2705u, "Barren Caster", 1457u, 2456u),          // Barren Flamemage
            (2706u, "Barren BOSS", 1457u, 2455u),            // Elite Barren Captain
            (2707u, "Scalewind Patroller", 1569u, 2469u),    // Scalewind Marine
            (2708u, "Scalewind Melee", 1573u, 2461u),        // Scalewind Recruit
            (2709u, "Scalewind Ranged", 1570u, 2498u),       // Scalewind Hunter
            (2710u, "Scalewind Caster", 1571u, 2465u),       // Scalewind Shaman
            (2711u, "Scalewind BOSS", 1575u, 2476u),         // Elite Scalewind Corruptor
            (2712u, "Highfall Patroller", 1123u, 2595u),     // Highfall Warden
            (2713u, "Highfall Melee", 1124u, 2598u),         // Highfall Centurion
            (2714u, "Highfall Ranged", 1150u, 2599u),        // Highfall Bandit
            (2715u, "Highfall Caster", 1662u, 2486u),        // Highfall Befouler
            (2716u, "Highfall BOSS", 1125u, 2493u),          // Elite Highfall Gladiator
            (2717u, "NovaTech Patroller", 1022u, 2477u),     // NovaTech Explorer
            (2719u, "NovaTech Human", 1147u, 2504u),         // NovaTech Colonist
            (2720u, "NovaTech BOSS", 1147u, 2505u),          // NovaTech Pilot
            (2721u, "Ebonguard Patroller", 1116u, 2534u),    // Ebonguard Watcher
            (2722u, "Ebonguard Melee", 1104u, 2518u),        // Ebonguard Attacker
            (2723u, "Ebonguard Ranged", 1110u, 2526u),       // Ebonguard Archer
            (2724u, "Ebonguard Caster", 1106u, 2522u),       // Ebonguard Deathdealer
            (2725u, "Ebonguard BOSS", 1103u, 2517u),         // Elite Ebonguard Captain
            (2726u, "Stoneskin Patroller", 1615u, 2601u),    // Stoneskin Groundsmasher
            (2727u, "Stoneskin Melee", 1620u, 2602u),        // Stoneskin Coldbringer
            (2728u, "Stoneskin Ranged", 1608u, 2607u),       // Stoneskin Starshooter
            (2729u, "Stoneskin Caster", 1618u, 2606u),       // Stoneskin Riftmender
        };

        /// <summary>Creature prop (v2 prop, ruin twin or ruin spawner) → its weapon.</summary>
        private static readonly Dictionary<uint, Weapon> byProp = new();

        /// <summary>
        /// Model → the weapon of a creature of that model with no prop of its own in the table (dev "creature" kinds, the
        /// world's kobolds): the model's ruin spawner's when it has one, else its first row's [I].
        /// </summary>
        private static readonly Dictionary<uint, Weapon> byModel = new();

        static CreatureWeapons()
        {
            foreach (Row row in Rows)
                foreach (uint prop in row.Props)
                    if (!byProp.TryAdd(prop, row.Weapon))
                        throw new InvalidOperationException($"CreatureWeapons: prop {prop} is listed twice");
            foreach ((uint spawner, string name, uint model, uint @as) in RuinSpawners)
            {
                Row row = Rows.FirstOrDefault(r => r.Props[0] == @as)
                    ?? throw new InvalidOperationException($"CreatureWeapons: ruin spawner {spawner} uses unknown prop {@as}");
                if (!byProp.TryAdd(spawner, row.Weapon with { Proof = $"[I] {name}: its model's {row.Creature} ({row.Weapon.Proof})" }))
                    throw new InvalidOperationException($"CreatureWeapons: prop {spawner} is listed twice");
                byModel.TryAdd(model, row.Weapon);
            }
            foreach (Row row in Rows)
                byModel.TryAdd(row.Model, row.Weapon);
        }

        /// <summary>The model defaults (<see cref="byModel"/>), for tests and the dev log.</summary>
        public static IReadOnlyDictionary<uint, Weapon> ModelDefaults => byModel;

        private static bool Enabled =>
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.CreatureWeapons ?? true;

        /// <summary>
        /// The table's weapon for a creature made by creature prop <paramref name="prop"/> (0 = none) of model
        /// <paramref name="model"/>, whatever the setting: the prop's row, else the model's default, else null.
        /// </summary>
        public static Weapon Lookup(uint prop, uint model)
            => prop != 0 && byProp.TryGetValue(prop, out Weapon w) ? w : byModel.TryGetValue(model, out w) ? w : null;

        /// <summary>The weapon a creature holds while the setting is on (<see cref="Lookup"/>), else null.</summary>
        public static Weapon For(uint prop, uint model) => Enabled ? Lookup(prop, model) : null;

        /// <summary>The weapon a creature of this model holds while the setting is on (its model's default), else null.</summary>
        public static Weapon ForModel(uint model) => For(0, model);

        /// <summary>True when the main-hand actor is a bow (gets a Halas quiver after it) [P capture].</summary>
        public static bool IsBow(Weapon weapon)
            => weapon?.Model != null
               && weapon.Model.Contains("Bow_", StringComparison.Ordinal)
               && !weapon.Model.Contains("Quiver", StringComparison.Ordinal);

        /// <summary>
        /// Slot packets for one creature: main hand (or emptied), then a quiver when the weapon is a bow, else an empty
        /// off-hand clear so a prior quiver does not linger. Live recipe (A): bow
        /// <c>{outer=2,att=2,bool=0}</c> then quiver <c>{outer=2,att=11,bool=1}</c>
        /// (trace/0695/agent-creature-quiver.md).
        /// </summary>
        public static IEnumerable<EquipmentSetCharacterEquipmentSlot> Messages(ulong creature, Weapon weapon)
        {
            yield return new EquipmentSetCharacterEquipmentSlot
            {
                CharacterGuid = creature,
                EquipSlotId = MainHandSlot,
                AttachmentSlot = MainHandSlot,
                ModelName = weapon?.Model ?? "",
                TintAlias = weapon?.Tint ?? "",
                ReplaceIfOccupied = false
            };
            if (IsBow(weapon))
            {
                yield return new EquipmentSetCharacterEquipmentSlot
                {
                    CharacterGuid = creature,
                    EquipSlotId = MainHandSlot,
                    AttachmentSlot = OffHandSlot,
                    ModelName = PlainQuiver.Item1,
                    TintAlias = string.IsNullOrEmpty(weapon.Tint) ? PlainQuiver.Item2 : weapon.Tint,
                    ReplaceIfOccupied = true
                };
            }
            else
            {
                // Clear attach slot 11 (outer 11): sheath / non-bow must not leave a quiver mesh behind.
                yield return new EquipmentSetCharacterEquipmentSlot
                {
                    CharacterGuid = creature,
                    EquipSlotId = OffHandSlot,
                    AttachmentSlot = OffHandSlot,
                    ModelName = "",
                    TintAlias = "",
                    ReplaceIfOccupied = true
                };
            }
        }

        /// <summary>First slot packet only (main hand); prefer <see cref="Messages"/> for full bow+quiver.</summary>
        public static EquipmentSetCharacterEquipmentSlot Message(ulong creature, Weapon weapon)
            => Messages(creature, weapon).First();
    }
}
