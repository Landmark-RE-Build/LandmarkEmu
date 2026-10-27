using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Combat;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using Button = LandmarkEmulator.WorldServer.Game.Combat.WeaponInputs.Button;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Weapon input → attack: which cast id each button sends with a weapon (the client's own input chain, rows copied
    /// from the 0.695 tables), which attack each cast runs (Combat.WeaponCombat's kits: Basic = left mouse, Power = right
    /// mouse), which of them are charged or held channels, and that a held channel waits for the button's release.
    /// </summary>
    public class WeaponInputRuleTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "lmemu-weaponinput-" + Guid.NewGuid().ToString("N"));
        private readonly object previousConfig;
        private static readonly PropertyInfo Shared = typeof(SharedConfiguration).GetProperty(nameof(SharedConfiguration.Configuration))!;
        private static readonly FieldInfo Kinds = typeof(WeaponAttacks).GetField("kinds", BindingFlags.NonPublic | BindingFlags.Static)!;

        /// <summary>
        /// The 0.695 rows (gameData695) of these weapons' chains: Staff of Frozen Sorrow (system 75), Overcharged Recon
        /// Blaster (43, 81), Electrified Assault Axe (37, 89), Gunslinger's Six-Shooters (50, 82), Flame Bound Bastion (40)
        /// and the never-live Katana of Crashing Waves (39, the one with an E cast here).
        /// </summary>
        private static readonly Dictionary<string, string[]> Tables = new()
        {
            ["DynamicInputActionSystems.txt"] = new[] { "37^27^", "39^36^", "40^37^", "43^40^", "50^47^", "75^69^", "81^75^", "82^76^", "89^83^" },
            ["DesignBasedStateMachineDefinition.txt"] = new[] { "27^53^", "36^62^", "37^63^", "40^66^", "47^73^", "69^116^", "75^129^", "76^131^", "83^146^" },
            ["DesignBasedStateDefinition.txt"] = new[] { "53^58^", "62^68^", "63^69^", "66^72^", "73^79^", "89^93^", "91^95^", "92^96^", "95^99^", "116^120^", "117^121^", "128^132^", "129^133^", "131^135^", "132^136^", "146^150^", "147^151^", "148^152^" },
            ["DesignBasedStatePropertiesDefinition.txt"] = new[] { "58^1^67^0^0^0^", "68^1^69^0^0^0^", "69^1^70^0^0^0^", "72^1^73^0^0^0^", "79^1^80^0^0^0^", "93^1^92^1^0^1.9^", "95^1^94^1^0.2^1.4^", "96^1^95^1^0^1.4^", "99^1^98^1^0.05^2.25^", "120^1^119^0^0^0^", "121^1^120^1^0.4^2.25^", "132^1^131^1^0.45^1.85^", "133^1^135^0^0^0^", "135^1^136^0^0^0^", "136^1^137^1^0.5^1.4^", "150^1^150^0^0^0^", "151^1^151^0^0.3^1.4^", "152^1^152^1^0.6^1.4^" },
            ["DesignBasedStateMachineMap.txt"] = new[] { "104^27^106^", "106^36^108^", "107^37^109^", "110^40^112^", "118^27^120^", "120^36^122^", "121^37^123^", "124^40^126^", "162^69^164^", "163^69^165^", "183^75^185^", "184^75^186^", "185^76^187^", "186^76^188^", "198^83^200^", "199^83^202^", "200^83^201^" },
            ["DesignBasedStateToStateTransitionMap.txt"] = new[] { "106^53^59^89^0^", "108^62^61^91^0^", "109^63^62^92^0^", "112^66^65^95^0^", "120^89^73^53^0^", "122^91^75^62^0^", "123^92^76^63^0^", "126^95^79^66^0^", "164^116^110^117^0^", "165^117^111^116^0^", "185^129^122^128^0^", "186^128^123^129^0^", "187^131^126^132^0^", "188^132^127^131^0^", "200^146^139^147^0^", "201^148^141^146^0^", "202^147^140^148^0^" },
            ["DesignBasedTransitionGroupRuleMap.txt"] = new[] { "82^59^84^", "84^61^86^", "85^62^87^", "88^65^90^", "96^73^98^", "98^75^100^", "99^76^101^", "102^79^104^", "133^110^135^", "134^111^136^", "146^123^148^", "147^122^149^", "150^126^152^", "151^127^153^", "163^139^165^", "164^140^166^", "165^141^167^" },
            ["DesignBasedTransitionRuleDefinition.txt"] = new[] { "84^1^312^0^0^", "86^1^318^0^0^", "87^1^321^0^0^", "90^1^330^0^0^", "98^1^370^0^0^", "100^1^372^0^0^", "101^1^373^0^0^", "104^1^376^0^0^", "135^1^400^0^0^", "136^1^402^0^0^", "148^1^419^0^0^", "149^1^422^0^0^", "152^1^353^0^0^", "153^1^423^0^0^", "165^1^442^0^0^", "166^1^443^0^0^", "167^1^444^0^0^" },
            ["InputActionGroupEntries.txt"] = new[] { "312^67^0^9^2^0^0^0^0^0^0^0^1^760^^0^0^1^1^1^", "318^69^0^9^2^0^0^0^0^0^0^0^1^766^^0^0^1^1^1^", "319^69^0^10^2^0^0^0^0^0^0^0^1^767^^0^0^1^1^2^", "320^69^0^11^2^0^0^0^0^0^0^0^1^768^^0^0^1^1^3^", "321^70^0^9^2^0^0^0^0^0^0^0^1^769^^0^0^1^1^1^", "322^70^0^10^2^0^0^0^0^0^0^0^1^770^^0^0^1^1^2^", "330^73^0^9^2^0^0^0^0^0^0^0^1^778^^0^0^1^1^1^", "351^80^0^9^2^0^0^0^0^0^0^0^1^799^^0^0^1^1^1^", "353^136^0^10^2^0^0^0^0^0^0^0^1^801^^0^0^1^1^2^", "370^92^0^9^2^0^0^0^0^0^0^0^1^816^^0^0^1^0^1^", "372^94^0^9^2^0^0^0^0^0^0^0^1^818^^0^0^1^0^1^", "373^95^0^9^2^0^0^0^0^0^0^0^1^819^^0^0^1^0^1^", "376^98^0^9^2^0^0^0^0^0^0^0^1^822^^0^0^1^0^1^", "400^119^0^9^2^0^0^0^0^0^0^0^1^1043^^0^0^1^1^1^", "401^119^0^10^2^0^0^0^0^0^0^0^1^1044^^0^0^1^1^2^", "402^120^0^9^2^0^0^0^0^0^0^0^1^1045^^0^0^1^0^1^", "419^131^0^10^2^0^0^0^0^0^0^0^1^1077^^0^0^1^0^2^", "422^135^0^10^2^0^0^0^0^0^0^0^1^779^^0^0^1^1^2^", "423^137^0^10^2^0^0^0^0^0^0^0^1^1080^^0^0^1^0^2^", "442^150^0^10^2^0^0^0^0^0^0^0^1^761^^0^0^1^1^2^", "443^151^0^10^2^0^0^0^0^0^0^0^1^1181^^0^0^1^1^2^", "444^152^0^10^2^0^0^0^0^0^0^0^1^1182^^0^0^1^1^2^" },
            ["WeaponAnimationClasses.txt"] = new[] { "Wield_Blade1H_Epic_Fire_Fire_000.adr^1010^", "Wield_Staff_Keldarain_Keldarain_C_A1_000.adr^1012^" },
        };

        private static readonly Dictionary<uint, uint[]> Systems = new()
        {
            [3762] = new uint[] { 75 }, [3033] = new uint[] { 43, 81 }, [3027] = new uint[] { 37, 89 },
            [3040] = new uint[] { 50, 82 }, [3030] = new uint[] { 40 }, [3029] = new uint[] { 39 },
        };

        public WeaponInputRuleTests()
        {
            Directory.CreateDirectory(folder);
            foreach ((string file, string[] rows) in Tables)
                File.WriteAllLines(Path.Combine(folder, file), new[] { "#*ID^^" }.Concat(rows));
            previousConfig = Shared.GetValue(null);
            Shared.GetSetMethod(true)!.Invoke(null, new object[] { new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["GameDataPath"] = folder }).Build() });
            WeaponInputs.Reset();
            Kinds.SetValue(null, null);
        }

        public void Dispose()
        {
            Shared.GetSetMethod(true)!.Invoke(null, new[] { previousConfig });
            WeaponInputs.Reset();
            Kinds.SetValue(null, null);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }

        private static ItemDefinitionManager.ItemDefinition Weapon(uint id, string model = "") => new()
        {
            Id = id, EquipSlotId = 2, ModelFile = model, InputSystems = Systems.GetValueOrDefault(id) ?? Array.Empty<uint>()
        };

        private static string Slots(uint item)
            => string.Join(" ", WeaponInputs.Of(Weapon(item)).Select(s => $"{s.Button}:{s.CastId}@{s.Step}"));

        // ---- The client's chain: button → cast id ------------------------------------------------------------------------

        [Theory]
        // Staff of Frozen Sorrow: left 1043, its follow-up 1045; right 1044.
        [InlineData(3762u, "Primary:1043@0 Secondary:1044@0 Primary:1045@1")]
        // Overcharged Recon Blaster: two systems; the right mouse has a follow-up (1077, the channelled volley).
        [InlineData(3033u, "Primary:778@0 Primary:822@1 Secondary:779@0 Secondary:1077@1")]
        // Electrified Assault Axe: Rolling Thunder's three leaps are three right-mouse steps.
        [InlineData(3027u, "Primary:760@0 Primary:816@1 Secondary:761@0 Secondary:1181@1 Secondary:1182@2")]
        // Gunslinger's Six-Shooters: Unload has no follow-up state (a held channel); Trick Shot can be used again.
        [InlineData(3040u, "Primary:799@0 Secondary:801@0 Secondary:1080@1")]
        // Flame Bound Bastion.
        [InlineData(3030u, "Primary:769@0 Secondary:770@0 Primary:819@1")]
        // Katana of Crashing Waves (never live): the only kind of weapon with an E (Tertiary) cast.
        [InlineData(3029u, "Primary:766@0 Secondary:767@0 Tertiary:768@0 Primary:818@1")]
        public void Chain_GivesEachButtonsCast(uint item, string expected) => Assert.Equal(expected, Slots(item));

        [Fact]
        public void FollowUpWindow_IsTheStatesDelayAndLength()
        {
            var staff = WeaponInputs.SlotOf(Weapon(3762), 1045);
            Assert.Equal(0.4f, staff.WindowStart);
            Assert.Equal(2.25f, staff.WindowLength);
            var volley = WeaponInputs.SlotOf(Weapon(3033), 1077);
            Assert.Equal(0.45f, volley.WindowStart);
            Assert.Equal(1.85f, volley.WindowLength);
            Assert.Equal(1044u, WeaponInputs.CastOf(Weapon(3762), Button.Secondary));
            Assert.Equal(0u, WeaponInputs.CastOf(Weapon(3762), Button.Tertiary));   // no E with a launch weapon
            Assert.Empty(WeaponInputs.Of(Weapon(3253)));                           // a pick: no input system
        }

        /// <summary>The cast kinds the server reads from the same table: left = Basic, right = Power, tooltip 0 = a follow-up.</summary>
        [Theory]
        [InlineData(1043u, WeaponAttacks.Kind.Basic)]
        [InlineData(1045u, WeaponAttacks.Kind.BasicCombo)]
        [InlineData(1044u, WeaponAttacks.Kind.Power)]
        [InlineData(779u, WeaponAttacks.Kind.Power)]
        [InlineData(1077u, WeaponAttacks.Kind.PowerCombo)]
        [InlineData(1181u, WeaponAttacks.Kind.Power)]
        [InlineData(768u, WeaponAttacks.Kind.Special)]
        public void CastKind_FollowsTheButton(uint cast, WeaponAttacks.Kind kind) => Assert.Equal(kind, WeaponAttacks.KindOf(cast));

        // ---- Kits: which attack each button runs (client texts 573028-576437; wiki weapon pages) -------------------------

        /// <summary>Basic (left) and Power (right) attack names per weapon, and which are charged / held channels.</summary>
        [Theory]
        // item, basic name, power name, basic charged, power charged, basic channel, power channel
        [InlineData(3761u, 574435u, 574436u, false, false, false, false)]   // Wanderer's Blazing Bow: Explosive Shot / Immolating Barrage
        [InlineData(3762u, 574445u, 574447u, true, false, false, false)]    // Staff of Frozen Sorrow: Shatter (charge) / Blizzard
        [InlineData(3036u, 574717u, 574719u, true, false, false, true)]     // Supercooled Power Cannon: Tidal Burst (charge) / Torrent (channel)
        [InlineData(3763u, 574449u, 574451u, false, false, false, false)]   // Falchion of the Inner Spark: Arcing Strike / Terminal Voltage
        [InlineData(3033u, 574721u, 574723u, true, false, false, false)]    // Overcharged Recon Blaster: Feedback (charge) / Shock and Awe
        [InlineData(3764u, 574452u, 574454u, false, true, false, false)]    // Armored Brawler's Blade: Assault / Overrun (charge)
        [InlineData(1293u, 574452u, 574454u, false, true, false, false)]    // Blade of Assault (2015 item, same kit)
        [InlineData(3040u, 574876u, 574878u, false, false, true, false)]    // Gunslinger's Six-Shooters: Unload (channel) / Trick Shot
        [InlineData(3765u, 574456u, 574458u, false, false, false, false)]   // Poison Drenched Daggers: Debilitating Shiv / Contaminate
        [InlineData(3032u, 573028u, 573069u, false, true, false, false)]    // Diabolical Scientist's Raygun: Ooze Bolt / Sludge Storm (charge)
        [InlineData(3766u, 574460u, 574462u, true, false, false, true)]     // Armament of Unrelenting Vengeance: Shadow Rend (charge) / Dark Embrace (channel)
        [InlineData(3030u, 573122u, 573125u, false, false, false, true)]    // Flame Bound Bastion: Cinder Strike / Molten Rebuke (stance channel)
        [InlineData(4091u, 576304u, 576306u, false, false, false, true)]    // Blight-Warden's Pain: Enfeeble / Corrupting Revenge (stance channel)
        [InlineData(3037u, 574884u, 574886u, true, false, false, true)]     // Avenging Champion's Exaltation: Smite (charge) / Consecrate (channel)
        [InlineData(3034u, 574880u, 574881u, false, false, false, false)]   // Spear of Piercing Judgment: Hateseeker / Nightfall
        [InlineData(3767u, 574464u, 574466u, false, false, false, false)]   // Wand of Boundless Light: Brilliance / Cascade
        [InlineData(3027u, 576428u, 576424u, true, false, false, false)]    // Electrified Assault Axe: Overcharge (charge) / Rolling Thunder
        [InlineData(4107u, 576330u, 576332u, true, false, false, false)]    // Staff of the Lightning Caller: Crackling Orb (charge) / Static Bomb
        [InlineData(4097u, 576334u, 576336u, false, true, false, false)]    // Frost Lord's Fury: Fracture / Frozen Domain (charge)
        [InlineData(3031u, 576300u, 576303u, false, true, false, false)]    // Frenzied Slayer's Battle-Axe: Fiery Cleave / Conflagration (charge)
        [InlineData(4089u, 576223u, 576220u, false, true, false, false)]    // Night Herald's Mercy: Cursed Bolt / Agonizing Volley (charge)
        [InlineData(4096u, 576314u, 576316u, false, true, false, false)]    // Crusader's Courage: Sanctify / Righteous Swing (charge)
        [InlineData(3035u, 573199u, 573200u, false, false, false, false)]   // Unflinching Embodiment of Might: Decimate / Obliteration Turn
        public void Kit_LeftIsBasic_RightIsPower(uint item, uint basic, uint power, bool basicCharged, bool powerCharged,
            bool basicChannel, bool powerChannel)
        {
            WeaponCombat.Kit kit = WeaponCombat.Kits[item];
            Assert.Equal(basic, WeaponCombat.AttackFor(kit, WeaponAttacks.Kind.Basic).Name);
            Assert.Equal(basic, WeaponCombat.AttackFor(kit, WeaponAttacks.Kind.BasicCombo).Name);   // the second left click
            Assert.Equal(power, WeaponCombat.AttackFor(kit, WeaponAttacks.Kind.Power).Name);
            Assert.Equal(basicCharged, kit.Basic.Charged);
            Assert.Equal(powerCharged, kit.Power.Charged);
            Assert.Equal(basicChannel, kit.Basic.Channel);
            Assert.Equal(powerChannel, kit.Power.Channel);
            Assert.False(kit.Basic.Charged && kit.Basic.Channel);
            Assert.False(kit.Power.Charged && kit.Power.Channel);
        }

        [Fact]
        public void RightMouseFollowUp_ReconVolley_OthersRepeatThePower()
        {
            WeaponCombat.Attack volley = WeaponCombat.AttackFor(WeaponCombat.Kits[3033], WeaponAttacks.Kind.PowerCombo);
            Assert.True(volley.Channel);
            Assert.False(ReferenceEquals(WeaponCombat.Kits[3033].Power, volley));
            Assert.Equal(574723u, volley.Name);                                     // still "Power Attack: Shock and Awe"
            Assert.Same(WeaponCombat.Kits[3040].Power, WeaponCombat.AttackFor(WeaponCombat.Kits[3040], WeaponAttacks.Kind.PowerCombo));
            // Without its own Special an E cast runs the Power attack (only the never-live weapons have one).
            Assert.Same(WeaponCombat.Kits[3762].Power, WeaponCombat.AttackFor(WeaponCombat.Kits[3762], WeaponAttacks.Kind.Special));
        }

        // ---- Caster info: the hold behaviour, and only the held weapon's casts ----------------------------------------------

        [Theory]
        [InlineData(3762u, 1043u, true, false)]    // Shatter: hold left to charge
        [InlineData(3762u, 1045u, true, false)]    // its follow-up too (the 2.25 s window leaves room for it)
        [InlineData(3762u, 1044u, false, false)]   // Blizzard: instant
        [InlineData(3030u, 770u, false, true)]     // Molten Rebuke: hold right to keep the stance
        [InlineData(3033u, 1077u, false, true)]    // the Recon volley: hold right
        [InlineData(3040u, 799u, false, true)]     // Unload: hold left
        public void CasterInfo_ChargedOrChannelledKind(uint item, uint cast, bool charged, bool channel)
        {
            using var world = new FakeWorld();
            var weapon = Weapon(item);
            WorldSession p = world.Session(0x7001, "Holder");
            var reply = new AbilityCasterInfoReply { AbilityId = cast };
            uint plain = reply.KindHash;
            WeaponCombat.Describe(p, weapon, cast, reply);
            Assert.Equal(charged ? WeaponCombat.ChargedKind : channel ? MovementEffects.ChannelledKind : plain, reply.KindHash);
        }

        [Fact]
        public void AttackInfo_IsSentForTheHeldWeaponsCastsOnly()
        {
            using var world = new FakeWorld();
            FakeWorld.EquipSlots((2, 2));
            world.Define(new ItemDefinitionManager.ItemDefinition
            {
                Id = 3762, EquipSlotId = 2, ModelFile = "Wield_Staff_Keldarain_Keldarain_C_A1_000.adr", InputSystems = new uint[] { 75 }
            });
            WorldSession p = world.Session(0x7002, "Staff", FakeWorld.Item(3762, container: Inventory.EquippedContainer, slot: 2));
            Assert.Equal(3762u, Abilities.HeldWeapon(p)?.Id);
            FakeWorld.Outgoing(p);
            WeaponCombat.SendAttackInfo(p);
            var replies = FakeWorld.Outgoing(p).Where(m => m.Opcode == ZoneMessageOpcode.AbilityCasterInfoReply).ToList();
            Assert.Equal(3, replies.Count);   // 1043, 1044, 1045 — not every weapon cast id in the table
        }

        [Fact]
        public void CastId_IsDescribedWithTheWeaponThatSendsIt()
        {
            using var world = new FakeWorld();
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = 3762, EquipSlotId = 2, InputSystems = new uint[] { 75 } });
            world.Define(new ItemDefinitionManager.ItemDefinition { Id = 3030, EquipSlotId = 2, InputSystems = new uint[] { 40 } });
            Assert.Equal(3762u, WeaponInputs.OwnerOf(1045)?.Id);
            Assert.Equal(3030u, WeaponInputs.OwnerOf(770)?.Id);
            Assert.Null(WeaponInputs.OwnerOf(326));   // Shift's sprint is no weapon cast
        }

        // ---- A held channel waits for the button --------------------------------------------------------------------------

        private static AbilityCastAttempt Cast(uint ability, ulong guid)
        {
            var a = new AbilityCastAttempt();
            FakeWorld.SetProperty(a, nameof(AbilityCastAttempt.AbilityId), ability);
            FakeWorld.SetProperty(a, nameof(AbilityCastAttempt.CastGuid), guid);
            return a;
        }

        [Fact]
        public void BlockingStance_HeldUntilReleased_ThenAnswered()
        {
            using var world = new FakeWorld();
            var bastion = Weapon(3030);
            WorldSession p = world.Session(0x7003, "Bastion");
            AbilityCastAttempt cast = Cast(770, 0xC0FFEE);
            Assert.False(WeaponCombat.Use(p, cast, bastion, 770, 50));      // a stance hits nothing
            Assert.True(WeaponCombat.ChannelOpened(p, cast));                // so the cast is not answered yet
            Assert.True(Statuses.Has(p.PlayerGuid, Status.Block));
            FakeWorld.Outgoing(p);
            Assert.True(WeaponCombat.ReleaseChannel(p));                     // CancelCast: the button came up
            Assert.False(Statuses.Has(p.PlayerGuid, Status.Block));
            var sent = FakeWorld.Outgoing(p).Select(m => m.Opcode).ToList();
            Assert.Contains(ZoneMessageOpcode.AbilityCastSuccess, sent);
            Assert.Contains(ZoneMessageOpcode.AbilityAbilityEnd, sent);
            Assert.False(WeaponCombat.ReleaseChannel(p));                    // nothing left to release
            WeaponCombat.Forget(p);
        }

        /// <summary>
        /// After Molten Rebuke releases, a later cast that reuses the same cast guid (live Bastion packets used 0x4)
        /// must not look "still channelled" — that skipped CastSuccess and locked the client (2026-10-27).
        /// </summary>
        [Fact]
        public void BlockingStance_AfterRelease_SameCastGuidIsNotStillOpen()
        {
            using var world = new FakeWorld();
            var bastion = Weapon(3030);
            WorldSession p = world.Session(0x7004, "BastionReuse");
            const ulong reused = 0x4;
            AbilityCastAttempt stance = Cast(770, reused);
            Assert.False(WeaponCombat.Use(p, stance, bastion, 770, 50));
            Assert.True(WeaponCombat.ChannelOpened(p, stance));
            Assert.True(WeaponCombat.ReleaseChannel(p));
            Assert.False(WeaponCombat.ChannelOpened(p, stance));
            AbilityCastAttempt next = Cast(769, reused);   // Cinder Strike-style basic with the same guid
            Assert.False(WeaponCombat.ChannelOpened(p, next));
            WeaponCombat.Forget(p);
        }
    }
}
