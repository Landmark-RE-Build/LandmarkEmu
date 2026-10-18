using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The impact a weapon hit shows on its target. The client ships a full set of impact composite effects
    /// (ActorCompositeEffectDefinitions.xml, trace/composite_effects.tsv): per affinity (Generic, Physical, Fire, Water,
    /// Lightning, Radiance, Shadow, Affliction) a directional slash for blades, an arrow impact for ranged weapons and a
    /// non-directional burst (blunt weapons, spells), each with a _Crit version. Which one a weapon used was server data;
    /// here the affinity comes from the weapon's ambient effect (item u17, e.g. PFX_Weapon_Ambient_Fire_Bow_01) or its
    /// name (wiki weapon pages: Falchion of the Inner Spark = fire, Supercooled Power Cannon = water ...), and the style
    /// from its model (bows / guns → arrow, staffs / wands / blunt → non-directional, other blades → slash).
    ///
    /// PlayerUpdate 0x0B PlayCompositeEffect (reader FUN_142b906d0, handled in PlayerUpdate_Dispatch case 10): u8 target
    /// type (1 = TargetCharacterGuid: 4 × u32 offset, u64 guid; 2 location ...; FUN_142b69910), u64 instance, u32 effect
    /// id, u32 delay ms, u32 duration ms (0 = the effect's own), bool, bool (defaults 1, 1), bool.
    /// </summary>
    public static class HitEffects
    {
        private enum Style { Slash, Arrow, Burst }

        /// <summary>Impact effect ids per affinity: slash, arrow, burst (non-directional).</summary>
        private static readonly Dictionary<string, (uint Slash, uint Arrow, uint Burst)> impacts = new()
        {
            ["Generic"] = (11670, 11697, 11696),
            ["Physical"] = (11670, 11660, 11285),
            ["Fire"] = (11695, 11411, 11694),
            ["Water"] = (11699, 11698, 10114),
            ["Lightning"] = (11554, 11700, 11701),
            ["Radiance"] = (11703, 11702, 11649),
            ["Shadow"] = (11705, 11704, 11681),
            ["Affliction"] = (11637, 11707, 11693),
        };

        /// <summary>Words in a weapon's ambient effect or name that give its affinity.</summary>
        private static readonly (string Word, string Affinity)[] affinityWords =
        {
            ("Fire", "Fire"), ("Flame", "Fire"), ("Blaz", "Fire"), ("Spark", "Fire"), ("Erupt", "Fire"), ("Molten", "Fire"),
            ("Water", "Water"), ("Frost", "Water"), ("Frozen", "Water"), ("Ice", "Water"), ("Supercooled", "Water"),
            ("Lightning", "Lightning"), ("Electri", "Lightning"), ("Storm", "Lightning"), ("Overcharged", "Lightning"),
            ("Radiance", "Radiance"), ("Light", "Radiance"), ("Heavenly", "Radiance"), ("Crusader", "Radiance"),
            ("Champion", "Radiance"), ("Judgement", "Radiance"),
            ("Shadow", "Shadow"), ("Vengeance", "Shadow"), ("Night", "Shadow"),
            ("Affliction", "Affliction"), ("Poison", "Affliction"), ("Blight", "Affliction"), ("Diabolical", "Affliction"),
            ("Physical", "Physical"), ("Earth", "Physical"), ("Bastion", "Physical")
        };

        private static string AffinityOf(Inventory.ItemDefinitionManager.ItemDefinition weapon)
        {
            // The weapon's own affinity first: its kit, else its description (the affinity text 574424 Fire … 574442
            // Radiance; Combat.WeaponCombat.AffinityOf) [P: the item records]. The word guess below got 10 of the 26 launch
            // weapons wrong: "Spark" made the Falchion of the Inner Spark Fire (Lightning), "Light" in "Blight" made
            // Blight-Warden's Pain Radiance (Affliction), "Ice" in "Justice" made Lightbringer's Justice Water (Radiance), and
            // the Earth weapons (Gunslinger's Six-Shooters, Armored Brawler's Blade, Unflinching Embodiment of Might), Spear of
            // Piercing Judgment, Desperado's Deadly Arsenal, Frenzied Slayer's Battle-Axe and Katana of Crashing Waves had
            // none. That fed the Daily "<Affinity> Wielder" goals and "Kills with a(n) <Element> weapon" (an Earth weapon
            // never counted for Earth Wielder) as well as the hit visuals (bughunt-combat.md B11).
            string own = Combat.WeaponCombat.AffinityOf(weapon) switch
            {
                Combat.Affinity.Fire => "Fire", Combat.Affinity.Water => "Water", Combat.Affinity.Lightning => "Lightning",
                Combat.Affinity.Radiance => "Radiance", Combat.Affinity.Shadow => "Shadow", Combat.Affinity.Affliction => "Affliction",
                Combat.Affinity.Earth => "Physical", _ => null
            };
            if (own != null)
                return own;
            string ambient = weapon?.AmbientEffect switch
            {
                11824 => "Fire", 11825 => "Lightning", 11826 => "Physical", _ => null
            };
            string name = weapon == null ? null : TextManager.Instance.GetTextForId(weapon.NameId);
            foreach (string text in new[] { ambient, name })
                if (!string.IsNullOrEmpty(text))
                    foreach ((string word, string affinity) in affinityWords)
                        if (text.Contains(word, System.StringComparison.OrdinalIgnoreCase))
                            return affinity;
            return "Generic";
        }

        private static Style StyleOf(Inventory.ItemDefinitionManager.ItemDefinition weapon, bool ranged)
        {
            string model = weapon?.ModelFile ?? "";
            if (ranged || model.Contains("_Bow_") || model.Contains("_Gun") || model.Contains("Shrinkray"))
                return Style.Arrow;
            if (model.Contains("_Staff_") || model.Contains("_Wand_") || model.Contains("_Blunt_"))
                return Style.Burst;
            return Style.Slash;
        }

        /// <summary>The affinity of the weapon a player holds ("Fire", "Water", "Lightning", "Radiance", "Shadow",
        /// "Affliction", "Physical"; "Generic" when none shows).</summary>
        public static string HeldAffinity(WorldSession player) => AffinityOf(Abilities.HeldWeapon(player));

        /// <summary>The impact effect for a hit with this weapon.</summary>
        public static uint ImpactOf(Inventory.ItemDefinitionManager.ItemDefinition weapon, bool ranged)
        {
            var set = impacts[AffinityOf(weapon)];
            return StyleOf(weapon, ranged) switch { Style.Arrow => set.Arrow, Style.Burst => set.Burst, _ => set.Slash };
        }

        /// <summary>Plays the hit's impact on the target for every viewer.</summary>
        public static void Play(WorldSession attacker, ulong target, IEnumerable<WorldSession> viewers, bool ranged)
        {
            uint effect = ImpactOf(Abilities.HeldWeapon(attacker), ranged);
            byte[] body = Build(target, effect);
            foreach (WorldSession viewer in viewers.Append(attacker).Distinct())
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdatePlayCompositeEffectWire, body);
        }

        /// <summary>Plays one composite effect on a character for these viewers.</summary>
        public static void PlayOn(ulong target, uint effect, IEnumerable<WorldSession> viewers) => PlayOn(target, effect, viewers, 0);

        /// <summary>Plays a composite effect on a character for <paramref name="durationMs"/> (0 = the effect's own length).</summary>
        public static void PlayOn(ulong target, uint effect, IEnumerable<WorldSession> viewers, uint durationMs)
        {
            byte[] body = Build(target, effect, durationMs);
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdatePlayCompositeEffectWire, body);
        }

        /// <summary>
        /// Plays a composite effect at a place (target type 2 TargetLocation, read by FUN_142b6a1b0: vec4 offset, vec4
        /// position, vec4 rotation) for these viewers — lingering areas, explosions at the aimed point.
        /// </summary>
        public static void PlayAt(System.Numerics.Vector3 at, uint effect, IEnumerable<WorldSession> viewers, uint durationMs = 0)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)2);                   // TargetLocation
            for (int i = 0; i < 4; i++)
                w.Write(0f);                    // offset
            w.Write(at.X); w.Write(at.Y); w.Write(at.Z); w.Write(1f);
            w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f);   // rotation (identity)
            w.Write(0ul);                       // instance
            w.Write(effect);
            w.Write(0u);                        // delay ms
            w.Write(durationMs);
            w.Write(true);
            w.Write(true);
            w.Write(false);
            byte[] body = output.ToArray();
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdatePlayCompositeEffectWire, body);
        }

        private static byte[] Build(ulong target, uint effect, uint durationMs = 0)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)1);                   // TargetCharacterGuid
            for (int i = 0; i < 4; i++)
                w.Write(0u);                    // offset
            w.Write(target);
            w.Write(0ul);                       // instance
            w.Write(effect);
            w.Write(0u);                        // delay ms
            w.Write(durationMs);                // duration ms (0 = the effect's own)
            w.Write(true);
            w.Write(true);
            w.Write(false);
            return output.ToArray();
        }
    }
}
