using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// The abilities of the creature props, including the rank 1 / 2 / 3 rows that are new in 0.695 (features audit #55a,
    /// trace/0695/agent-abilities.md §1).
    /// <para>
    /// <b>What the ranks are.</b> The 0.695 texts 577260-577337 ("Poison Blade 2", "Poison Blade 3" ... "Darkfire 3") are
    /// <b>creature</b> abilities, not player weapon abilities. Their rank-1 rows were renamed in 0.695 ("Slashing Blade" →
    /// "Slashing Blade 1", 572981; "Fireball" → "Fireball 1", 564156; trace/0695/text-new.tsv), and every rank-1 text sits
    /// in the creature text block 572869-573380 right after the name of the creature that uses it (572980 Barren Grunt,
    /// 572981 Slashing Blade 1, 572982 Blade Lunge). The fandom wiki's creature pages list them as the Combat Node's
    /// "perform Ability N" choices, e.g. Barren Grunt: "Ability 1: Slashing Blade 1, Ability 2: Slashing Blade 2,
    /// Ability 3: Slashing Blade 3, Ability 4: Blade Lunge" (design/wiki/pages/Barren Grunt.wiki; 25 pages with lists).
    /// No player item, stat or account flag selects a rank: the client has no code for them (functions-695.md §8), the
    /// Ability* tables hold no rows for them (AbilityLines / AbilityLineMembers / AbilityEx are stubs) and no item record
    /// names them. Ability data was server-side (AbilityDefinitions.txt is not shipped, trace/systems/abilities-combat.md),
    /// so the server picks the ability; the client only shows its name.
    /// </para>
    /// <para>
    /// <b>Where the client shows the name.</b> CombatSpam row 1 "Damage" (564566 "#Owns([*SourcePlayer*]) [*Ability*] hit
    /// YOU for [*Value*] damage."): the combat-spam builder FUN_142fe2840 fills [*Ability*] from a text id (its 5th
    /// argument's first u32, looked up with FUN_142d8b3e0). The damage path from the wire is PlayerUpdate
    /// CombatResourceEvent (0x26 sub 0x67, reader FUN_142c278c0): body {u64 source +0x18, u64 target +0x20, u64 +0x28,
    /// i32 delta +0x30, u32 type +0x34 (1 = health), u32 +0x38, u32 +0x3c, u32 +0x40, bool crit +0x44, u32 +0x48};
    /// the handler case in FUN_142c4b910's caller (part_0065 ~20821) passes a negative type-1 delta to FUN_14308e060 →
    /// FUN_14308de50 → FUN_142fe2240, whose disassembly stores its 4th argument — the wire u32 at <b>+0x40</b> — as the
    /// spam's ability text id and the damage as its value. So <see cref="Creatures"/> writes the ability's name there.
    /// </para>
    /// <para>
    /// <b>What is ours [I].</b> Which ability the AI uses on its own (rank 1, the ability that kept the 0.589 name), the
    /// damage of each rank (×1, ×1.25, ×1.5) and of the specials (×1.5), the shield block's length, and the creature
    /// lists marked T (text adjacency only, no wiki page): see <see cref="Sets"/>. Open leads: OPEN-LEADS L19 (live's
    /// ability data), agent-abilities.md §4.
    /// </para>
    /// </summary>
    public static class CreatureAbilities
    {
        /// <summary>What an ability does when a creature uses it.</summary>
        public enum Role
        {
            /// <summary>An ordinary attack (unranked: Skull Toss, Claw Slash ...), damage ×1.</summary>
            Attack,
            /// <summary>A creature's special attack (Blade Lunge, Piercing Spike, Inferno ...), damage <see cref="StrongFactor"/>.</summary>
            Strong,
            /// <summary>Rejuvenate (Barren Lightbringer): a heal for its side instead of an attack.</summary>
            Heal,
            /// <summary>A secondary ability that blocks with the shield against all damage (wiki: Barren Protector,
            /// Elite Barren Captain). No name text: the wiki gives only a description.</summary>
            Block,
            /// <summary>Grow-splode Dud (Fiery Fungus, 564884): the fungus goes off without harming anyone [I from the name].</summary>
            Fizzle
        }

        /// <summary>
        /// One ability: its 0.695 name text (rank 2 / 3 texts 577260-577337 exist only in 0.695), its rank (0 unranked)
        /// and role.
        /// </summary>
        public sealed record Ability(uint Name, int Rank, Role Role)
        {
            /// <summary>Damage multiplier [I]: ranks 1 / 2 / 3 ×1 / ×1.25 / ×1.5, specials ×1.5, others ×1.</summary>
            public float DamageFactor => Role == Role.Strong ? StrongFactor
                : Rank >= 2 ? 1f + RankStep * (Rank - 1) : Role is Role.Fizzle or Role.Block or Role.Heal ? 0f : 1f;
        }

        /// <summary>Each rank above 1 deals this much more [I].</summary>
        public const float RankStep = 0.25f;
        /// <summary>A special attack's damage [I].</summary>
        public const float StrongFactor = 1.5f;
        /// <summary>How long a shield block (<see cref="Role.Block"/>) makes the creature immune [I].</summary>
        public static readonly TimeSpan BlockTime = TimeSpan.FromSeconds(3);

        private static Ability[] Ranked(uint rank1, uint rank2, uint rank3) => new[]
        {
            new Ability(rank1, 1, Role.Attack), new Ability(rank2, 2, Role.Attack), new Ability(rank3, 3, Role.Attack)
        };

        private static Ability One(uint name, Role role = Role.Strong) => new(name, 0, role);

        private static readonly Ability Shield = One(0, Role.Block);

        private static Ability[] Set(params object[] parts)
            => parts.SelectMany(p => p is Ability[] many ? many : new[] { (Ability)p }).ToArray();

        /// <summary>
        /// Creature name (the creature prop's name, PropDefs / item_names; as <see cref="Creatures"/> names story creatures)
        /// → its abilities in Combat Node order ("perform Ability 1" = index 0).
        /// <list type="bullet">
        /// <item><b>W</b> — order and count from the creature's wiki page (design/wiki/pages/&lt;name&gt;.wiki, section
        /// "&lt;name&gt; Attacks"), names resolved to text ids in text_695.tsv.</item>
        /// <item><b>T [I]</b> — no wiki list: the rank-1 text directly follows the creature's name text (e.g. 573173 Highfall
        /// Frostblade, 573174 Frozen Blade 1, 573175 Frostbite Lunge) and the 0.695 rank 2 / 3 pair of that line; order
        /// ranks then special (the order of 8 of the 13 wiki lists that have both). Elite Barren Lieutenant / Mageking:
        /// the wiki gives four unnamed abilities, matching the four found this way.</item>
        /// </list>
        /// Two "Arctic Blast" lines exist: 577330 / 577331 are the Barren Coldmage's (the 2 / 3 pairs end with the Barren
        /// casters in wiki order: Fireball, Arctic Blast, Voltaic Sphere, Light Orb, Darkfire), 577322 / 577323 the NovaTech
        /// Scientist's (between Jagged Shard and Venom Blast, with the other NovaTech lines) [I, strong].
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Ability[]> Sets = new Dictionary<string, Ability[]>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- W: wiki lists ----
            ["Abomination Skullslapper"] = Set(One(566162, Role.Attack), One(566154, Role.Attack)),     // Skull Toss, Tentacle Slap
            ["Abomination Skulltosser"] = Set(One(566162, Role.Attack)),
            ["Abomination Slapper"] = Set(One(566154, Role.Attack)),
            ["Agitated Chomper"] = Set(One(564868, Role.Attack), One(564869, Role.Attack)),             // Carnivorous Lunge, Acidic Spittle
            ["Lunging Chomper"] = Set(One(564868, Role.Attack)),
            ["Spitting Chomper"] = Set(One(564869, Role.Attack)),
            ["Barren Bowman"] = Set(Ranked(572948, 577296, 577297), One(572949)),                      // Piercing Shot 1-3, Piercing Spike
            ["Barren Coldmage"] = Set(One(573020), Ranked(573019, 577330, 577331)),                     // Frozen Orb, Arctic Blast 1-3
            ["Barren Fireshot"] = Set(Ranked(572951, 577298, 577299), One(572952)),                     // Searing Shot 1-3, Searing Spike
            ["Barren Flameblade"] = Set(One(572985), Ranked(572984, 577264, 577265)),                   // Fiery Lunge, Burning Blade 1-3
            ["Barren Flamemage"] = Set(Ranked(564156, 577328, 577329), One(572999)),                    // Fireball 1-3, Inferno
            ["Barren Grunt"] = Set(Ranked(572981, 577262, 577263), One(572982)),                        // Slashing Blade 1-3, Blade Lunge
            ["Barren Icebrute"] = Set(Ranked(572987, 577282, 577283), One(572988)),                     // Icy Slash 1-3, Icy Strike
            ["Barren Lightbringer"] = Set(One(573008, Role.Heal), Ranked(573009, 577334, 577335)),       // Rejuvenate, Light Orb 1-3
            ["Barren Protector"] = Set(Shield, Ranked(572981, 577262, 577263), One(572982)),            // block, Slashing Blade 1-3, Blade Lunge
            ["Barren Shadowshot"] = Set(One(572966), Ranked(572967, 577306, 577307)),                   // Darkening Spike, Darkening Shot 1-3
            ["Barren Stormmancer"] = Set(Ranked(573016, 577332, 577333), One(573017)),                  // Voltaic Sphere 1-3, Tempest Jolt
            ["Barren Stormshot"] = Set(Ranked(572971, 577302, 577303), One(572972)),                    // Crackling Shot 1-3, Crackling Spike
            ["Elite Barren Captain"] = Set(Shield, Ranked(572984, 577264, 577265), One(572985)),        // block, Burning Blade 1-3, Fiery Lunge
            ["Elite Barren Sergeant"] = Set(Ranked(572974, 577300, 577301), One(572975)),               // Freezing Shot 1-3, Freezing Spike
            ["Elite Barren Sargeant"] = Set(Ranked(572974, 577300, 577301), One(572975)),               // (the prop's own spelling, PropDefs 2449 / 2747)
            ["Fiery Fungus"] = Set(One(564883, Role.Attack), One(564884, Role.Fizzle)),                 // Grow-splode, Grow-splode Dud
            ["Powered Mega-Knight"] = Set(One(574199, Role.Attack), One(574199, Role.Attack),           // Electric Swipe ×2,
                One(574200), One(574201)),                                                               // Power Stomp, Crackling Slam
            ["Slaug Slasher"] = Set(One(565464, Role.Attack)),                                          // Claw Slash
            ["Slaug Stormer"] = Set(One(567377, Role.Attack)),                                          // Claw Storm
            ["Slaug Stormslasher"] = Set(One(565464, Role.Attack), One(567377, Role.Attack)),           // (wiki page "Slaug Stormlasher")
            ["Vine Trap"] = Set(One(577120, Role.Attack)),                                              // Vine Lash

            // ---- T [I]: text adjacency ----
            ["Elite Barren Lieutenant"] = Set(Ranked(572994, 577288, 577289), One(572995)),             // Shaded Slash 1-3, Shaded Strike
            ["Elite Barren Mageking"] = Set(Ranked(573024, 577336, 577337), One(573025)),               // Darkfire 1-3, Shadow Seal
            ["Scalewind Dropper"] = Set(Ranked(573031, 577260, 577261), One(573032)),                   // Poison Blade 1-3, Toxic Lunge
            ["Highfall Frostblade"] = Set(Ranked(573174, 577266, 577267), One(573175)),                 // Frozen Blade 1-3, Frostbite Lunge
            ["Highfall Warden"] = Set(Ranked(573369, 577268, 577269), One(573370)),                     // Charged Blade 1-3, Arcing Lunge
            ["Scalewind Lightrunt"] = Set(Ranked(573221, 577270, 577271), One(573222)),                 // Divine Blade 1-3, Luminous Lunge
            ["Elite Scalewind Nightblade"] = Set(Ranked(573084, 577272, 577273), One(573085)),          // Shadow Blade 1-3, Darkening Lunge
            ["Elite NovaTech Researcher"] = Set(Ranked(573119, 577274, 577275), One(573120)),           // Crouching Lighter 1-3, Hidden Flagon
            ["Ebonguard Brutalizer"] = Set(Ranked(573248, 577276, 577277), One(573249)),                // Sickening Slash 1-3, Diseased Strike
            ["Elite Ebonguard Commander"] = Set(Ranked(573281, 577280, 577281), One(573283)),           // Smoldering Slash 1-3, Blazing Strike
            ["Highfall Shockblade"] = Set(Ranked(573182, 577284, 577285), One(573183)),                 // Charged Slash 1-3, Crackling Strike
            ["Highfall Darkguard"] = Set(Ranked(573372, 577286, 577287), One(573373)),                  // Righteous Slash 1-3, Shielding Strike
            ["NovaTech Colonist"] = Set(Ranked(573212, 577290, 577291), One(573213)),                   // Inferno Shot 1-3, Inferno Charge
            ["Scalewind Toxifier"] = Set(Ranked(573225, 577294, 577295), One(573226)),                  // Viral Shot 1-3, Plagued Spike
            ["Ebonguard Archer"] = Set(Ranked(573259, 577304, 577305), One(573260)),                    // Shining Shot 1-3, Sheltering Spike
            ["Ebonguard Attacker"] = Set(Ranked(573245, 577308, 577309), One(573246)),                  // Force Slash 1-3, Terra Strike
            ["Ebonguard Fireblade"] = Set(Ranked(573233, 577310, 577311), One(573234)),                 // Fire Slash 1-3, Leaping Flame
            ["Ebonguard Watcher"] = Set(Ranked(573272, 577312, 577313), One(573273)),                   // Frigid Slash 1-3, Flashfreeze Strike
            ["Ebonguard Sentinel"] = Set(Ranked(573265, 577314, 577315), One(573266)),                  // Sacred Slash 1-3, Celestial Strike
            ["Ebonguard Shadowslayer"] = Set(Ranked(573240, 577316, 577317), One(573241)),              // Midnight Slash 1-3, Darkened Strike
            ["NovaTech Pilot"] = Set(Ranked(573215, 577320, 577321), One(573216)),                      // Jagged Shard 1-3, Freezing Burst
            ["NovaTech Scientist"] = Set(Ranked(573116, 577322, 577323), One(573117)),                  // Arctic Blast 1-3, Tundra Burst
            ["Scalewind Smasher"] = Set(Ranked(573228, 577326, 577327), One(573229)),                   // Force Missile 1-3, Power Punch
        };

        /// <summary>A creature's abilities by its name (null when the name has no known list).</summary>
        public static Ability[] Of(string creatureName)
            => creatureName != null && Sets.TryGetValue(creatureName.Trim(), out Ability[] set) ? set : null;

        /// <summary>
        /// The ability a creature uses on its own [I]: its rank-1 attack (the one whose text kept the 0.589 name), else its
        /// first attack; null when the name has no list (then no name is shown, as before).
        /// </summary>
        public static Ability Basic(string creatureName)
        {
            Ability[] set = Of(creatureName);
            return set?.FirstOrDefault(a => a.Rank == 1) ?? set?.FirstOrDefault(a => a.Role == Role.Attack);
        }

        /// <summary>A Combat Node's "perform Ability <paramref name="n"/>" (1-based) for this creature, or null.</summary>
        public static Ability Get(string creatureName, int n)
        {
            Ability[] set = Of(creatureName);
            return set != null && n >= 1 && n <= set.Length ? set[n - 1] : null;
        }
    }
}
