using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// The stats the worn armour gives: maximum life and weapon damage by armour weight (TASKS 5.1; trace
    /// trace/0695/gear-stats-695.md).
    /// <para>
    /// Wiki "Armor Weight" (launch 2016): light armour gives a little life and much damage, medium an even mix, heavy
    /// much life and a little damage. The 25 May 2016 update notes add that the damage formula was retuned for heavy
    /// and medium armour, so every weapon hits a bit harder in them.
    /// </para>
    /// <para>
    /// What the client proves (0.695):
    /// <list type="bullet">
    /// <item>[P] An armour's weight is its item category: 286 "Light Armor", 285 "Medium Armor", 157 "Heavy Armor" (the
    /// live ReferenceData ItemCategoryDefinitions dump, re/out/items_by_category.txt; every one of the 28 chest armours
    /// carries exactly one of them in its live item record, and they agree with the wiki's categories even where the
    /// model file name says otherwise, e.g. Frost Lord's Guard = Medium with a "Heavy_003" model).</item>
    /// <item>[P] The HUD life bar and the character sheet's "Health" line show the Life resource's maximum
    /// (LocalPlayer.Resources.Health "Maximum", OnResourceChanged FUN_143329090 → FUN_142cd0230 → FUN_143b8c310 →
    /// FUN_143217a00), which is the record's MAX_VALUE variable (id 0x2C7CD6E6, value = base + modifier) when the
    /// server sent one, else Resources.txt MAX_VALUE (900 for Life, row 4). See <see cref="CharacterResources"/>.</item>
    /// <item>[P] Weapon attack numbers on the client come from the Damage calculated stat (10): 296 of the 380
    /// AbilityParameterDefinitions StatMultiplier rows read stat 10 (ParameterCalculator FUN_1435079e0: value =
    /// stat × PARAM_2 + PARAM_3).</item>
    /// </list>
    /// The size of each weight's bonus is server-side data that no capture holds, so the numbers below are ours
    /// ([I]), chosen to follow the wiki's small / balanced / large wording: heavy reaches 1800 life, the value the only
    /// live UpdateCalculatedStats capture sent as stat 1 (cStatTypeHealthBonus, text "Life").
    /// </para>
    /// </summary>
    public static class GearStats
    {
        /// <summary>Armour weight (the item categories below).</summary>
        public enum Weight { None, Light, Medium, Heavy }

        /// <summary>Item categories that name an armour's weight ([P] live category dump).</summary>
        public const uint LightArmorCategory = 286, MediumArmorCategory = 285, HeavyArmorCategory = 157;

        /// <summary>EquipmentSlotDefinitions: the chest (body armour) slot.</summary>
        public const uint ChestSlot = 8;

        /// <summary>
        /// The Damage stat's default (cStatTypeDamage, 10): 70 in the live UpdateCalculatedStats capture
        /// (<see cref="DefaultCalculatedStats"/>). The server's base weapon damage per hit is the same number.
        /// </summary>
        public const float BaseDamageStat = 70f;

        /// <summary>
        /// What each weight adds ([I], ours): extra maximum life on top of the Life resource's 900, and the weapon damage
        /// factor. Light 1100 / ×1.30, medium 1450 / ×1.20, heavy 1800 / ×1.10; no chest armour 900 / ×1.00.
        /// </summary>
        private static readonly Dictionary<Weight, (uint Life, float Damage)> bonuses = new()
        {
            [Weight.None] = (0, 1.00f),
            [Weight.Light] = (200, 1.30f),
            [Weight.Medium] = (550, 1.20f),
            [Weight.Heavy] = (900, 1.10f),
        };

        /// <summary>An armour definition's weight from its categories (None for anything that isn't body armour).</summary>
        public static Weight WeightOf(Inventory.ItemDefinitionManager.ItemDefinition definition)
        {
            if (definition == null)
                return Weight.None;
            if (definition.Categories.Contains(HeavyArmorCategory)) return Weight.Heavy;
            if (definition.Categories.Contains(MediumArmorCategory)) return Weight.Medium;
            if (definition.Categories.Contains(LightArmorCategory)) return Weight.Light;
            return Weight.None;
        }

        /// <summary>
        /// The worn body armour: the equipped item in the chest slot with a weight category, its weight and its
        /// condition factor (<see cref="Durability.Strength"/>: half at 0 % condition, as for weapons; [I] ours).
        /// </summary>
        private static (Weight Weight, float Strength) Worn(WorldSession session)
        {
            if (session.Inventory == null)
                return (Weight.None, 1f);
            foreach (var item in session.Inventory.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer))
            {
                var definition = Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId);
                if (definition?.EquipSlotId != ChestSlot)
                    continue;
                Weight weight = WeightOf(definition);
                if (weight != Weight.None)
                    return (weight, Durability.Strength(item));
            }
            return (Weight.None, 1f);
        }

        /// <summary>The player's worn armour weight (dev / logs).</summary>
        public static Weight WornWeight(WorldSession session) => Worn(session).Weight;

        /// <summary>
        /// Maximum life: the Life resource's 900 (Resources.txt row 4, MAX_VALUE) plus the worn armour's bonus (halved
        /// while the armour is at 0 % condition).
        /// </summary>
        public static uint MaxHealth(WorldSession session)
        {
            var (weight, strength) = Worn(session);
            return Health.MaxHealth + (uint)Math.Round(bonuses[weight].Life * strength);
        }

        /// <summary>Weapon damage factor from the worn armour (1 with none; the bonus part halves at 0 % condition).</summary>
        public static float DamageFactor(WorldSession session)
        {
            var (weight, strength) = Worn(session);
            return 1f + (bonuses[weight].Damage - 1f) * strength;
        }

        /// <summary>The Damage calculated stat (10) the client's attack parameters multiply: 70 × the armour factor.</summary>
        public static float DamageStat(WorldSession session) => (float)Math.Round(BaseDamageStat * DamageFactor(session));

        private static readonly object sync = new();

        /// <summary>What each player's client was last told (max life, Damage stat), to send only changes.</summary>
        private static readonly Dictionary<WorldSession, (uint Max, float Damage)> sent = new();

        /// <summary>
        /// World entry: records what the entry packets carry (Life with its maximum, the full calculated stat list), so
        /// later checks only send changes.
        /// </summary>
        public static void Remember(WorldSession session)
        {
            lock (sync)
                sent[session] = (MaxHealth(session), DamageStat(session));
        }

        /// <summary>
        /// Re-checks the armour stats (equip / unequip via <see cref="WeaponCombat.CheckWeapon"/>, every hit taken via
        /// <see cref="Health.Damage"/> for armour that just broke). When the maximum life changed, the life bar is
        /// rescaled (<see cref="Health.MaxChanged"/>); when it or the damage changed, a two-entry UpdateCalculatedStats
        /// (stat 1 Life, stat 10 Damage) goes to the player — the client sets each listed stat on its own
        /// (FUN_142e5d990 → FUN_142e5de80), so a partial list is enough. Nothing is sent when nothing changed.
        /// </summary>
        public static void Refresh(WorldSession session)
        {
            if (session?.Inventory == null)
                return;
            uint max = MaxHealth(session);
            float damage = DamageStat(session);
            (uint Max, float Damage) old;
            lock (sync)
            {
                if (!sent.TryGetValue(session, out old))
                    old = (Health.MaxHealth, BaseDamageStat);
                if (old == (max, damage))
                    return;
                sent[session] = (max, damage);
            }
            if (old.Max != max)
                Health.MaxChanged(session, old.Max, max);
            var stats = new ClientUpdateUpdateCalculatedStats { Guid = session.PlayerGuid };
            stats.Stats.AddRange(Abilities.CalculatedStats(session).Where(s => s.StatId is LifeStat or DamageStatId));
            session.EnqueueMessage(stats);
        }

        /// <summary>CalculatedStatDefinitions: 1 cStatTypeHealthBonus (text "Life"), 10 cStatTypeDamage.</summary>
        public const uint LifeStat = 1, DamageStatId = 10;

        /// <summary>A player left.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                sent.Remove(session);
        }
    }
}
