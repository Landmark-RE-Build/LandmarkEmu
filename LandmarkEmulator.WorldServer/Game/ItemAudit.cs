using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Game.Inventory;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Development aid (dev "itemaudit"): sorts every item definition by what the server does with it and writes
    /// ItemAudit.txt next to the server: unhandled item abilities grouped by ability id (with sample item names), then
    /// counts per kind. Which item functions are still missing, at a glance.
    /// </summary>
    public static class ItemAudit
    {
        private static string Name(ItemDefinitionManager.ItemDefinition d) => TextManager.Instance.GetTextForId(d.NameId) ?? $"item {d.Id}";

        private static bool AbilityHandled(uint ability)
            => Abilities.TravelAbilities.Contains(ability) || Consumables.Handles(ability) || Abilities.IsHarvestAbility(ability)
               || ability == Abilities.HeroicMovementAbility;

        public static string Run(string path)
        {
            var kinds = new SortedDictionary<string, int>();
            var unhandled = new SortedDictionary<uint, List<string>>();
            var noUse = new List<string>();
            void Count(string kind) => kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            foreach (ItemDefinitionManager.ItemDefinition d in ItemDefinitionManager.All)
            {
                if (d.Abilities.Count > 0)
                {
                    if (d.EquipSlotId is 2 or 3 or 8 && !d.Abilities.Any(a => Abilities.IsHarvestAbility(a.AbilityId)))
                    {
                        Count("combat gear (abilities = weapon damage)");
                        continue;
                    }
                    // Abilities without an input are passives (type 2/5: a tool's mod stats, the vein-seer overlay): nothing is cast.
                    var missing = d.Abilities.Where(a => a.Input.Length > 0 && !AbilityHandled(a.AbilityId)).ToList();
                    if (missing.Count == 0)
                    {
                        Count("ability handled");
                        continue;
                    }
                    Count("ABILITY UNHANDLED");
                    foreach (var a in missing)
                    {
                        if (!unhandled.TryGetValue(a.AbilityId, out var names))
                            unhandled[a.AbilityId] = names = new List<string>();
                        names.Add($"{d.Id} {Name(d)} [type {a.Type}, slot {d.EquipSlotId}, {a.Category}]");
                    }
                }
                else if (d.PropId != 0)
                    Count("prop");
                else if (RecipeBook.Teaches(d.Id))
                    Count("recipe");
                else if (ItemUses.Handles(d.Id))
                    Count("item use (bags, expansions, designs)");
                else if (Titles.Get(d.Id) != null)
                    Count("title");
                else if (d.EquipSlotId != 0)
                    Count("wearable");
                else
                {
                    Count("no use (materials, currency, keys...)");
                    noUse.Add($"{d.Id} {Name(d)} [cat {string.Join(",", d.Categories)}]");
                }
            }
            var report = new StringBuilder();
            report.AppendLine("Item audit: unhandled abilities (ability id: items)");
            foreach ((uint ability, List<string> names) in unhandled.OrderByDescending(u => u.Value.Count))
            {
                report.AppendLine($"{ability} ({names.Count} items)");
                foreach (string n in names.Take(6))
                    report.AppendLine("    " + n);
            }
            report.AppendLine();
            foreach ((string kind, int n) in kinds)
                report.AppendLine($"{n,6}  {kind}");
            report.AppendLine();
            report.AppendLine("Items with no use here:");
            foreach (string n in noUse)
                report.AppendLine("    " + n);
            File.WriteAllText(path, report.ToString());
            return string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}"));
        }
    }
}
