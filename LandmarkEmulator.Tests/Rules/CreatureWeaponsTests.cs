using LandmarkEmulator.WorldServer;
using LandmarkEmulator.WorldServer.Game;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The creature weapon table (Game/CreatureWeapons.cs, OPEN-LEADS L95; a best guess, on by default): every row's
    /// creature props and models agree with the live prop list (Resources/PacketDumps/145-PropBase, whose model ids are
    /// Models.txt ids), every weapon is an attachable 0.695 weapon actor, nothing is listed twice, every row carries its
    /// proof, and the monsters / animals have none.
    /// </summary>
    public class CreatureWeaponsTests
    {
        /// <summary>
        /// The 0.695 weapon actors that can be held (re/out/fileindex_695.tsv, read from the packs 2026-10-18): Wield_*.adr
        /// files whose Base .dme mesh is in the packs and whose Usage names a bone attachment (boneAttachmentName
        /// MainHandWeapon, OffHandWeapon, Root, R_Weapon ...) or borrows the holder's skeleton (borrowSkeleton 1: the bows,
        /// as the players' Wanderer's Blazing Bow); work-in-progress / hue-test / proxy actors left out. The kobold spear
        /// has no .adr.
        /// </summary>
        private static readonly HashSet<string> Attachable = new(StringComparer.Ordinal)
        {
            "Wield_1HAxe_Gnome_Gnome_000.adr", "Wield_1HBlade_Dwarf_Dwarf_B_A0_000.adr",
            "Wield_1HBlade_Dwarf_Dwarf_B_A0_001.adr", "Wield_1HBlade_Dwarf_Dwarf_C_A0_000.adr",
            "Wield_1HBlade_Dwarf_Dwarf_E_A0_000.adr", "Wield_1HBlade_Dwarf_Dwarf_E_A0_001.adr",
            "Wield_1HBlade_Qeynos_Qeynos_D_A0_000.adr", "Wield_2HAxe_DarkElf_DarkElf_D_A1_000.adr",
            "Wield_2HAxe_DarkElf_DarkElf_E_A1_000.adr", "Wield_2HAxe_Dwarf_Dwarf_C_A1_000.adr",
            "Wield_2HAxe_Landmark_Landmark_A_A1_000.adr", "Wield_2HAxe_Ogre_Ogre_B_A1_000.adr",
            "Wield_2HAxe_Qeynos_Qeynos_A_A1_000.adr", "Wield_2HBlade_Ogre_Ogre_Epic_A1_0_001.adr",
            "Wield_2HBlade_Ogre_Ogre_Epic_A1_0_001_Holster.adr", "Wield_Axe2H_DarkElf_DarkElf_D_A1_001.adr",
            "Wield_Axe2H_DarkElf_DarkElf_D_A1_002.adr", "Wield_Axe2H_DarkElf_DarkElf_DarkElf_E_B1_0_002.adr",
            "Wield_Blade1H_Combine_Combine_A_A0_000.adr", "Wield_Blade1H_Dwarf_Dwarf_B_A0_000.adr",
            "Wield_Blade1H_Dwarf_Dwarf_C_A0_001.adr", "Wield_Blade1H_Dwarf_Dwarf_E_A0_000.adr",
            "Wield_Blade1H_Dwarf_Dwarf_E_A0_001.adr", "Wield_Blade1H_Epic_Fire_Fire_000.adr",
            "Wield_Blade1H_Epic_Shadow_Shadow_000.adr", "Wield_Blade1H_Katana_Katana_A_A0_000.adr",
            "Wield_Blade1H_Katana_Katana_A_A0_000_Left.adr", "Wield_Blade2H_DarkElf_DarkElf_C_A1_000.adr",
            "Wield_Blade2H_Dwarf_Dwarf_D_A1_000.adr", "Wield_Blade2H_Katana_Katana_A_A0_000.adr",
            "Wield_Blade2H_Ogre_Ogre_D_A1_0_001.adr", "Wield_Blunt_Epic_Heavenly_Heavenly_000.adr",
            "Wield_Blunt_Qeynos_Qeynos_E_000.adr", "Wield_Blunt_VoidGoliath_VoidGoliath_E_A0.adr",
            "Wield_Bow_0000.adr", "Wield_Bow_0001.adr",
            "Wield_Bow_0002.adr", "Wield_Bow_DarkElf_DarkElf_B_A4_000.adr",
            "Wield_Bow_Epic_Fire_01_Fire_Fire_000.adr", "Wield_Bow_Epic_Heavenly_01_Heavenly_000.adr",
            "Wield_Bow_Epic_Physical_01_Physical_Physical_000.adr", "Wield_Bow_Firiona_Firiona_Z_A1_000.adr",
            "Wield_Bow_HorseBow_HorseBow_B_A3_001.adr", "Wield_Bow_Kobold_Kobold_A_A1_000.adr",
            "Wield_Bucket_DarkElf_DarkElf_Bucket_000.adr", "Wield_Bucket_DarkElf_DarkElf_E_A0_000.adr",
            "Wield_Bucket_Kerran_Kerran_C_A0_000.adr", "Wield_Bucket_Qeynos_Qeynos_A_A0_000.adr",
            "Wield_Dagger_DarkElf_DarkElf_A_A5_000.adr", "Wield_Dagger_DarkElf_DarkElf_A_A5_000_Left.adr",
            "Wield_Foci_Keldarain_Keldarain_E_A0_000.adr", "Wield_Gun1H_Colony_Colony_A_A4_000.adr",
            "Wield_Gun1H_Gunslinger_Gunslinger_A_A5_000.adr", "Wield_Gun1H_Gunslinger_Gunslinger_A_A5_000_Left.adr",
            "Wield_Hammer2H_Ogre_Ogre_C_A1_000.adr", "Wield_Pick_Dwarf_Dwarf_D_A0_000.adr",
            "Wield_Pick_Dwarf_Dwarf_D_A0_001.adr", "Wield_Pick_Elf_Elf_E_A0_000.adr",
            "Wield_Pick_Elf_Elf_E_A0_001.adr", "Wield_Pick_Halas_Halas_C_A0_000.adr",
            "Wield_Pick_Halas_Halas_C_A0_001.adr", "Wield_Pick_Landmark_Landmark_A_A0_000.adr",
            "Wield_Pick_Landmark_PracticeSword_Dummy.adr", "Wield_Pick_Ogre_Ogre_B_A0_000.adr",
            "Wield_Pick_Ogre_Ogre_B_A0_001.adr", "Wield_Pick_Qeynos_Qeynos_A_A0_000.adr",
            "Wield_Pick_Qeynos_Qeynos_A_A0_001.adr", "Wield_Pick_Qeynos_Qeynos_Pick_000.adr",
            "Wield_Pickaxe_Founder_Founder_E_A1_000.adr", "Wield_Pickaxe_Founder_Founder_E_A1_001.adr",
            "Wield_Polearm_Combine_Combine_C_A1_000.adr", "Wield_Pulverizer_Colony_Colony_B_A2_000.adr",
            "Wield_Pulverizer_Colony_Colony_C_A2_000.adr", "Wield_Pulverizer_Colony_Colony_D_A2_000.adr",
            "Wield_Pulverizer_Colony_Colony_E_A2_000.adr", "Wield_Quiver_Halas_Halas_B_A3_000.adr",
            "Wield_Shank_Bottle_Bottle_Bottle.adr", "Wield_Shield_Dwarf_Dwarf_B_A2_000.adr",
            "Wield_Shield_Dwarf_Dwarf_C_A2_000.adr", "Wield_Shield_Dwarf_Dwarf_C_A2_001.adr",
            "Wield_Shield_Dwarf_Dwarf_E_A2_000.adr", "Wield_Shield_Dwarf_Dwarf_E_A2_000_01.adr",
            "Wield_Shield_Dwarf_Dwarf_E_A2_001.adr", "Wield_Shield_Epic_Fire_Fire_000.adr",
            "Wield_Shield_Epic_Heavenly_Heavenly_000.adr", "Wield_Shield_Epic_Physical_Physical_000.adr",
            "Wield_Shield_Epic_Shadow_Shadow_000.adr", "Wield_Shrinkray_Landmark_A_A99_000.adr",
            "Wield_Sickle_DarkElf_DarkElf_E_A0_000.adr", "Wield_Sickle_DarkElf_DarkElf_E_A0_001.adr",
            "Wield_Sickle_Dwarf_Dwarf_C_A0_000.adr", "Wield_Sickle_Elf_Elf_B_A0_000.adr",
            "Wield_Sickle_Halas_Halas_A_A0_000.adr", "Wield_Sickle_Ogre_Ogre_D_A0_000.adr",
            "Wield_Staff_Dwarf_Dwarf_D_A1_000.adr", "Wield_Staff_Epic_Fire_Fire_000.adr",
            "Wield_Staff_Epic_Physical_Physical_000.adr", "Wield_Staff_Epic_Shadow_Shadow_000.adr",
            "Wield_Staff_Keldarain_Keldarain_C_A1_000.adr", "Wield_Staff_Keldarain_Keldarain_D_A1_000.adr",
            "Wield_Staff_Kobold_Kobold_A_A1_000.adr", "Wield_Staff_Kobold_Kobold_D_A1_000.adr",
            "Wield_Wand_Keldarain_Keldarain_C_A0_000.adr", "Wield_Wand_Qeynos_Qeynos_B_A0_000.adr",
        };

        /// <summary>One prop of the live prop list: id, model (Models.txt id), internal name, description.</summary>
        private sealed record PropEntry(uint Id, uint Model, string Name, string Description);

        private static Dictionary<uint, PropEntry> props;

        /// <summary>
        /// The live prop list (as Props.LoadDefinitions reads it): opcode bytes, u8 sub, u64, u8, u32 count, then per prop:
        /// model file, u32 prop id, internal name, description, f32, f32, u32 image set, u32, u32, u32 model id.
        /// </summary>
        private static Dictionary<uint, PropEntry> Props()
        {
            if (props != null)
                return props;
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "145-PropBase"));
            var reader = new BinaryReader(new MemoryStream(data));
            reader.ReadBytes(2 + 1 + 8 + 1);
            uint count = reader.ReadUInt32();
            string Str() => Encoding.Latin1.GetString(reader.ReadBytes((int)reader.ReadUInt32()));
            var list = new Dictionary<uint, PropEntry>();
            for (uint i = 0; i < count; i++)
            {
                Str();
                uint id = reader.ReadUInt32();
                string name = Str(), description = Str();
                reader.ReadBytes(4 * 5);
                uint model = reader.ReadUInt32();
                list.TryAdd(id, new PropEntry(id, model, name, description));
            }
            return props = list;
        }

        [Fact]
        public void Setting_IsOnByDefault()
            => Assert.True(new WorldServerConfiguration().CreatureWeapons);

        [Fact]
        public void EveryRow_MatchesThePropList()
        {
            Dictionary<uint, PropEntry> all = Props();
            Assert.True(CreatureWeapons.Rows.Count >= 100, $"{CreatureWeapons.Rows.Count} rows");
            foreach (CreatureWeapons.Row row in CreatureWeapons.Rows)
            {
                Assert.True(all.TryGetValue(row.Props[0], out PropEntry first), $"{row.Creature}: prop {row.Props[0]} not in the prop list");
                Assert.True(first.Description.StartsWith("landmark_v2: spawner:", StringComparison.Ordinal),
                    $"{row.Creature}: prop {first.Id} is not a creature prop ({first.Description})");
                foreach (uint id in row.Props)
                {
                    Assert.True(all.TryGetValue(id, out PropEntry p), $"{row.Creature}: prop {id} not in the prop list");
                    Assert.Equal(row.Model, p.Model, $"{row.Creature}: prop {id} has model {p.Model}, the row says {row.Model}");
                    if (id != row.Props[0])
                        Assert.True(p.Name.StartsWith("landmark_ruin_npc_", StringComparison.Ordinal), $"{row.Creature}: twin {id} is {p.Name}");
                }
            }
        }

        [Fact]
        public void RuinSpawners_MatchThePropList()
        {
            Dictionary<uint, PropEntry> all = Props();
            foreach ((uint spawner, string name, uint model, uint @as) in CreatureWeapons.RuinSpawners)
            {
                Assert.True(all.TryGetValue(spawner, out PropEntry p), $"ruin spawner {spawner} not in the prop list");
                Assert.Contains("Spawner: Ruins: " + name + " (", p.Name);
                Assert.Equal(model, p.Model, $"{name}: model");
                CreatureWeapons.Row row = CreatureWeapons.Rows.SingleOrDefault(r => r.Props[0] == @as);
                Assert.NotNull(row, $"{name}: stand-in {@as} has no row");
                Assert.Equal(model, row.Model, $"{name}: its stand-in {row.Creature} is another model");
                Assert.Equal(row.Weapon.Model, CreatureWeapons.Lookup(spawner, model).Model);
            }
        }

        [Fact]
        public void ModelDefaults_AreCreatureModels()
        {
            var creatureModels = Props().Values.Where(p => p.Description.StartsWith("landmark_v2: spawner:", StringComparison.Ordinal))
                .Select(p => p.Model).ToHashSet();
            Assert.NotEmpty(CreatureWeapons.ModelDefaults);
            foreach ((uint model, CreatureWeapons.Weapon weapon) in CreatureWeapons.ModelDefaults)
            {
                Assert.True(creatureModels.Contains(model), $"model {model} is no creature prop's model");
                Assert.True(CreatureWeapons.Rows.Any(r => r.Model == model && r.Weapon.Model == weapon.Model), $"model {model}: default not from its rows");
            }
            // The world's kobold kinds (no prop): the ranged kobold model keeps its bow, the caster its staff.
            Assert.Equal("Wield_Bow_Kobold_Kobold_A_A1_000.adr", CreatureWeapons.Lookup(0, 1570).Model);
            Assert.Equal("Wield_Staff_Kobold_Kobold_A_A1_000.adr", CreatureWeapons.Lookup(0, 1571).Model);
        }

        [Fact]
        public void EveryWeapon_IsAnAttachableWeaponActor()
        {
            var weapons = CreatureWeapons.Rows.Select(r => r.Weapon).ToList();
            foreach (CreatureWeapons.Weapon w in weapons)
            {
                Assert.True(w.Model.StartsWith("Wield_", StringComparison.Ordinal) && w.Model.EndsWith(".adr", StringComparison.Ordinal), w.Model);
                Assert.True(Attachable.Contains(w.Model), $"{w.Model} is not an attachable 0.695 weapon actor");
                Assert.NotNull(w.Tint);
            }
            Assert.DoesNotContain("Wield_Polearm_Combine_KoboldSpear_000_Spear_000.adr", weapons.Select(w => w.Model));
        }

        [Fact]
        public void NothingIsListedTwice()
        {
            var ids = CreatureWeapons.Rows.SelectMany(r => r.Props).Concat(CreatureWeapons.RuinSpawners.Select(s => s.Spawner)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count(), "a prop is listed twice");
            var names = CreatureWeapons.Rows.Select(r => r.Creature).ToList();
            Assert.Equal(names.Count, names.Distinct().Count(), "a creature is listed twice");
        }

        [Fact]
        public void EveryRow_CarriesItsProof()
        {
            foreach (CreatureWeapons.Row row in CreatureWeapons.Rows)
            {
                string proof = row.Weapon.Proof;
                Assert.True(proof.Contains("[P]") || proof.Contains("[W]"), $"{row.Creature}: no [P] / [W] evidence");
                Assert.True(proof.Contains("[I]"), $"{row.Creature}: the actor choice is not marked [I]");
            }
        }

        /// <summary>Creatures whose icon says Nothing (animals, monsters, hounds, giants, wisps, golem, elemental) hold none.</summary>
        [Theory]
        [InlineData(2433u)] // Abomination Slapper
        [InlineData(2437u)] // Lunging Chomper
        [InlineData(2441u)] // Slaug Slasher
        [InlineData(2481u)] // Earth Elemental
        [InlineData(2482u)] // Frostbite Giant
        [InlineData(2485u)] // Toxic Giant
        [InlineData(2500u)] // NovaTech Firehound
        [InlineData(2588u)] // White Wisp Monarch
        [InlineData(2611u)] // Darkfang Specter
        [InlineData(2612u)] // Oakmire Stag
        [InlineData(2613u)] // Fiery Fungus
        [InlineData(2614u)] // Powered Mega-Knight
        [InlineData(2730u)] // Spawner: Ruins: Stoneskin BOSS GIANTS
        [InlineData(2718u)] // Spawner: Ruins: NovaTech Hound
        public void NonHumanoids_HoldNothing(uint prop)
        {
            Assert.True(Props().TryGetValue(prop, out PropEntry p), $"prop {prop}");
            Assert.Null(CreatureWeapons.Lookup(prop, p.Model), $"{p.Description} holds a weapon");
        }

        [Fact]
        public void Examples()
        {
            Assert.Equal("Wield_Blade1H_Epic_Fire_Fire_000.adr", CreatureWeapons.Lookup(2451, 1457).Model);        // Barren Flameblade
            Assert.Equal("Wield_Blade1H_Epic_Fire_Fire_000.adr", CreatureWeapons.Lookup(2736, 1457).Model);        // its ruin twin
            Assert.Equal("Wield_Bow_Epic_Fire_01_Fire_Fire_000.adr", CreatureWeapons.Lookup(2446, 1457).Model);    // Barren Fireshot
            Assert.Equal("Wield_Dagger_DarkElf_DarkElf_A_A5_000.adr", CreatureWeapons.Lookup(2514, 1102).Model);   // Ebonguard Fireblade
            Assert.Equal("Wield_Pick_Landmark_PracticeSword_Dummy.adr", CreatureWeapons.Lookup(2890, 1708).Model); // Target Dummy
            Assert.Equal("Wield_Bow_Kobold_Kobold_A_A1_000.adr", CreatureWeapons.Lookup(2709, 1570).Model);        // Scalewind Ranged
            Assert.Equal("Wield_1HBlade_Dwarf_Dwarf_C_A0_000.adr", CreatureWeapons.Lookup(2703, 1457).Model);      // Barren Melee
        }
    }
}
