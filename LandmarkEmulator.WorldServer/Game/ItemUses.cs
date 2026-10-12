using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Items without an ability that do something when used from the bag (right-click "Use" sends Inventory 0x15
    /// ActivateItem with the item guid): Builder's Bags and Chests unpack into the materials their description names,
    /// Bags of Sifted Material give a random handful of ore, gems and rare stone. Items with an ability (potions,
    /// gadgets) are cast instead and handled by <see cref="Consumables"/>.
    /// </summary>
    public static class ItemUses
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>"This bag contains 10,000 Cut Agate." / "This chest contains 50,000 Cut Burled Wood&lt;br/&gt;…"</summary>
        private static readonly Regex contains = new(@"contains ([\d,]+) ([^.<]+)", RegexOptions.Compiled);

        private static Dictionary<uint, (uint Item, uint Count)> unpacks;
        private static Dictionary<string, uint> itemsByName;

        /// <summary>Raw resources by tier, for the sifted bags (surface ores and stones first, deep ones higher).</summary>
        private static readonly uint[][] tiers =
        {
            new uint[] { 34, 35, 47, 48, 51 },   // 1: Copper, Tin, Marble, Amaranthine, Agate
            new uint[] { 36, 50, 57 },           // 2: Iron, Alabaster, Sapphire
            new uint[] { 37, 49, 56, 58 },       // 3: Silver, Obsidian, Emerald, Ruby
            new uint[] { 39, 59 },               // 4: Gold, Diamond
        };

        /// <summary>Sifted bags: item → highest tier inside (from their descriptions).</summary>
        private static readonly Dictionary<uint, int> siftedBags = new()
        {
            [1032] = 2,   // Bag of Sifted Material
            [1026] = 3,   // Roughwork: Tier 1 - 3
            [1029] = 4,   // Tradesman's: Tier 1 - 4
        };

        private static readonly Random random = new();

        /// <summary>Storage expansions: (unlock kind holding the size, start, step, most, inventory container or 0 for designs).</summary>
        private static readonly Dictionary<uint, (string Name, uint Kind, uint Start, uint Step, uint Max, uint Container)> expansions = new()
        {
            [3711] = ("Inventory", Unlocks.BagSize, Inventory.Inventory.DefaultBagSize, 20, Inventory.Inventory.MaxBagSize, Inventory.Inventory.BagContainer),
            [3710] = ("Vault", Unlocks.VaultSize, DefaultVaultSize, 20, 120, VaultContainer),
            [3712] = ("Design", Unlocks.DesignStorage, DefaultDesignStorage, 6_000_000, 48_000_000, uint.MaxValue),
        };

        /// <summary>Vault (container 1) size of a new character, and design storage in voxels.</summary>
        public const uint DefaultVaultSize = 60, VaultContainer = 1, DefaultDesignStorage = 6_000_000;

        /// <summary>Inventory SetSize (0x2D sub 0x17 {u32 container, u32 size}; client "Inventory.IncreasedSize") and
        /// SetVoxelStorage (sub 0x1D {u32 old, u32 new}; "VoxelStorage.IncreasedSize"), reader FUN_142e81590.</summary>
        private const ushort SetSizeSub = 0x17, SetVoxelStorageSub = 0x1D;

        public static uint VaultSizeOf(ulong characterId)
            => Unlocks.Values(characterId, Unlocks.VaultSize).DefaultIfEmpty(DefaultVaultSize).Max();

        public static uint DesignStorageOf(ulong characterId)
            => Unlocks.Values(characterId, Unlocks.DesignStorage).DefaultIfEmpty(DefaultDesignStorage).Max();

        /// <summary>Dungeon Builder's Starter Set (Lumen Station).</summary>
        private const uint DungeonStarterSet = 3760;

        /// <summary>Does <see cref="TryActivate"/> have a use for the item?</summary>
        public static bool Handles(uint itemId)
        {
            if (itemId == DungeonStarterSet)
                return true;
            unpacks ??= BuildUnpacks();
            return expansions.ContainsKey(itemId) || unpacks.ContainsKey(itemId) || siftedBags.ContainsKey(itemId) || DesignFile(itemId) != null;
        }

        /// <summary>Uses an ability-less item; false when it has no use here (the caller tries other uses).</summary>
        public static bool TryActivate(WorldSession session, CharacterItemModel item)
        {
            if (item.ItemDefinitionId == DungeonStarterSet)
            {
                // Getting Started "Cry Havoc!": the set holds the characters its goals use ("Place the Barren
                // Flameblade character from your Props tab" ×2, "Barren Bowman character placed"): two Barren
                // Flameblades (prop item 3162) and a Barren Bowman (3156).
                if (!session.TakeItem(DungeonStarterSet, 1))
                    return true;
                session.GiveItem(3162, 2);
                session.GiveItem(3156, 1);
                log.Info($"{session.CharacterName} opened the Dungeon Builder's Starter Set.");
                return true;
            }
            if (expansions.TryGetValue(item.ItemDefinitionId, out var expansion))
            {
                Expand(session, item, expansion);
                return true;
            }
            unpacks ??= BuildUnpacks();
            if (unpacks.TryGetValue(item.ItemDefinitionId, out var contents))
            {
                Unpack(session, item, contents.Item, contents.Count);
                return true;
            }
            if (siftedBags.TryGetValue(item.ItemDefinitionId, out int topTier))
            {
                OpenSiftedBag(session, item, topTier);
                return true;
            }
            if (DesignFile(item.ItemDefinitionId) is string file)
            {
                LearnDesign(session, item, file);
                return true;
            }
            return false;
        }

        private static void Expand(WorldSession session, CharacterItemModel item,
            (string Name, uint Kind, uint Start, uint Step, uint Max, uint Container) e)
        {
            uint size = Unlocks.Values(session.PlayerGuid, e.Kind).DefaultIfEmpty(e.Start).Max();
            if (size >= e.Max)
            {
                Chat.System(session, $"Your {e.Name} storage is already at its maximum ({e.Max:N0}).");
                return;
            }
            if (!session.TakeItem(item.ItemDefinitionId, 1))
                return;
            uint grown = Math.Min(e.Max, size + e.Step);
            Unlocks.Set(session, e.Kind, grown);
            var output = new List<byte>();
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            if (e.Container == uint.MaxValue)
            {
                w.WriteUShort(SetVoxelStorageSub);
                w.Write(size);
                w.Write(grown);
            }
            else
            {
                if (e.Container == Inventory.Inventory.BagContainer)
                    session.Inventory.SetBagSize(grown);
                w.WriteUShort(SetSizeSub);
                w.Write(e.Container);
                w.Write(grown);
            }
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.InventoryBase, output.ToArray());
            log.Info($"{session.CharacterName}'s {e.Name} storage grew from {size} to {grown}.");
        }

        private static Dictionary<uint, string> designFiles;

        /// <summary>Design items whose template file has another name (only the unambiguous ones).</summary>
        private static readonly Dictionary<string, string> designAliases = new()
        {
            ["Starter Home: Modern Abode"] = "starter_home_modern",
            ["Starter Home: Slate Villa"] = "starter_home_slate_villa",
            ["Starter Home: Sandstone Dojo"] = "starter_home_dojo",
            ["Starter Home: Castle Tower"] = "starter_home_castle",
            ["Starter Home: Brick Warehouse"] = "starter_home_warehouse",
            ["Old designs - arcs 10 to 19"] = "starter_arcs_10_to_19",
            ["Old designs - ovals 10 to 20"] = "starter_ovals_10_to_20",
            ["Old designs - ovals 22 to 30"] = "starter_ovals_22_to_30",
            ["Old designs - ovals 32 to 40"] = "starter_ovals_32_to_40",
            ["Old designs - ovals 42 to 50"] = "starter_ovals_42_to_50",
            ["Old designs - ovals 100"] = "starter_ovals_100",
            ["Architectural Tools - Angled Lines"] = "starter_lines_angled",
            ["Architectural Tools - Pillars"] = "starter_pillars_new",
            ["Architectural Tools - Odd Radius Arches"] = "arches_-_odd",
            ["Architectural Tools - Even Radius Arches"] = "arches_-_even",
            ["Architecture Tools - Odd Radius Arcs"] = "starter_template_-_odd_arcs",
            ["Architecture Tools - Even Radius Arcs"] = "starter_template_-_even_arcs",
            ["Modular Castle Long Room"] = "modular_castle_room_(long)",
            ["Modular Castle Tower Hallway"] = "modular_castle_tall_tower_hallway",
            ["Castle Hallway with Side Doors"] = "castle_hallway_with_entries",
            ["Castle Wall - Short"] = "ramparts_wall_(short)",
            ["Castle Tower"] = "castle_tall_tower",
            ["Castle Tower - Corner"] = "castle_tall_tower_(corner)",
            ["Castle Room - Corner"] = "castle_tower_(corner)",
            ["Castle Room - Four Way"] = "castle_tower_(4_way)",
        };

        /// <summary>Letters and digits only, lower case: "Castle Hallway - Short" and "castle_hallway_(short)" both give "castlehallwayshort".</summary>
        private static string Key(string text) => new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>
        /// Design items ("Castle Hallway - Short", "Design: Graveyard 1"…): the client template (.otd) of the same name.
        /// Items whose name matches no template, or that place a prop, aren't designs. <see cref="designAliases"/> names
        /// the file where item and file names differ.
        /// </summary>
        public static bool IsDesign(uint itemId) => DesignFile(itemId) != null;

        private static string DesignFile(uint itemId)
        {
            if (designFiles == null)
            {
                var byKey = new Dictionary<string, string>();
                if (Directory.Exists(Terrain.VoxelTemplate.Folder))
                    foreach (string path in Directory.GetFiles(Terrain.VoxelTemplate.Folder, "*.otd"))
                    {
                        string key = Key(Path.GetFileNameWithoutExtension(path));
                        byKey.TryAdd(key, path);
                        if (key.EndsWith("v2"))
                            byKey.TryAdd(key[..^2], path);
                    }
                var map = new Dictionary<uint, string>();
                foreach (var definition in ItemDefinitionManager.All)
                {
                    if (definition.PropId != 0 || definition.Abilities.Count > 0)
                        continue;
                    string name = TextManager.Instance.GetTextForId(definition.NameId) ?? "";
                    string key = Key(name.StartsWith("Design:") ? name["Design:".Length..] : name);
                    if (designAliases.TryGetValue(name, out string alias))
                        key = Key(alias);
                    if (key.Length > 0 && byKey.TryGetValue(key, out string file))
                        map[definition.Id] = file;
                }
                designFiles = map;
                log.Info($"{map.Count} design items have a template.");
            }
            return designFiles.GetValueOrDefault(itemId);
        }

        /// <summary>The name text id of the design item a game design came from (by its name), or 0.</summary>
        public static uint DesignNameId(string designName)
        {
            if (designNameIds == null)
            {
                DesignFile(0);
                var map = new Dictionary<string, uint>();
                foreach (uint itemId in designFiles.Keys)
                    if (ItemDefinitionManager.Get(itemId) is { } d)
                    {
                        string name = NameOf(itemId);
                        map.TryAdd(name.StartsWith("Design:") ? name["Design:".Length..].Trim() : name, d.NameId);
                    }
                designNameIds = map;
            }
            return designNameIds.GetValueOrDefault(designName);
        }

        private static Dictionary<string, uint> designNameIds;

        /// <summary>A design item: its template joins the player's designs (Designs tab) and the item is used up.</summary>
        private static void LearnDesign(WorldSession session, CharacterItemModel item, string file)
        {
            Terrain.VoxelTemplate template;
            try
            {
                template = Terrain.VoxelTemplate.Load(file);
            }
            catch (Exception e)
            {
                log.Error(e, $"Design template {file} could not be read.");
                Chat.System(session, "That design could not be read.");
                return;
            }
            string name = NameOf(item.ItemDefinitionId);
            if (name.StartsWith("Design:"))
                name = name["Design:".Length..].Trim();
            if (!Templates.AddFromTemplate(session, name, template))
            {
                Chat.System(session, "Your design storage is full.");
                return;
            }
            session.TakeItem(item.ItemDefinitionId, 1);
            Templates.SendLearned(session, 0, name);
            log.Info($"{session.CharacterName} learned design {name} ({Path.GetFileName(file)}, {template.Cells.Count} cells).");
        }

        private static void Unpack(WorldSession session, CharacterItemModel bag, uint item, uint count)
        {
            bool chest = chestOutfits.ContainsKey(bag.ItemDefinitionId);
            // A chest also needs a slot each for its bonus (a sifted bag, perhaps an outfit).
            if (session.Inventory.SpaceFor(item) < count || (chest && session.Inventory.FreeBagSlots() < 3))
            {
                Chat.System(session, $"You need more room in your inventory to open this ({count:N0} {NameOf(item)}).");
                return;
            }
            if (!session.TakeItem(bag.ItemDefinitionId, 1))
                return;
            session.GiveItem(item, count);
            Chat.System(session, $"You unpack {count:N0} {NameOf(item)}.");
            if (chest)
                GiveChestBonus(session, chestOutfits[bag.ItemDefinitionId]);
            log.Info($"{session.CharacterName} unpacked item {bag.ItemDefinitionId} into {count} × {item}.");
        }

        /// <summary>Bonus resources of a Builder's Chest: the live game added a "Bonus Box"; 0.589 has no such item, so
        /// the bonus is a Tradesman's Bag of Sifted Material (opened like any sifted bag).</summary>
        private const uint ChestBonusBag = 1029;
        private const double ChestOutfitChance = 0.25;

        /// <summary>Builder's Chest → the outfits its description says the bonus may hold.</summary>
        private static readonly Dictionary<uint, uint[]> chestOutfits = new();

        private static void GiveChestBonus(WorldSession session, uint[] outfits)
        {
            session.GiveItem(ChestBonusBag, 1);
            string bonus = NameOf(ChestBonusBag);
            if (outfits.Length > 0 && random.NextDouble() < ChestOutfitChance)
            {
                uint outfit = outfits[random.Next(outfits.Length)];
                session.GiveItem(outfit, 1);
                bonus += " and " + NameOf(outfit);
            }
            Chat.System(session, $"Bonus: {bonus}.");
        }

        /// <summary>
        /// The outfit list of a Builder's Chest description ("Colonist's Battle Armor - Green or Red | Rancher's Outfit -
        /// Black, Blue, or Red …") as items "Name (Colour)"; names that changed by 0.589 ("Gunslinger's Outfit" is now
        /// "Gunslinger's Gear") match by their first word and the colour.
        /// </summary>
        private static uint[] OutfitsIn(string description)
        {
            int at = description.IndexOf("following outfits:", StringComparison.Ordinal);
            if (at < 0)
                return Array.Empty<uint>();
            var found = new List<uint>();
            foreach (string line in Regex.Replace(description[at..], "<[^>]*>", "|").Split('|'))
            {
                int dash = line.IndexOf(" - ", StringComparison.Ordinal);
                if (dash < 0)
                    continue;
                string name = line[..dash].Trim();
                foreach (string colour in Regex.Split(line[(dash + 3)..], @",\s*(?:or\s+)?|\s+or\s+").Select(c => c.Trim()).Where(c => c.Length > 0))
                {
                    uint id = ItemNamed($"{name} ({colour})");
                    if (id == 0)
                    {
                        string first = name.Split(' ')[0];
                        itemsByName.TryGetValue(itemsByName.Keys.FirstOrDefault(k => k.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase)
                            && k.EndsWith($"({colour})", StringComparison.OrdinalIgnoreCase)) ?? "", out id);
                    }
                    if (id != 0)
                        found.Add(id);
                }
            }
            return found.Distinct().ToArray();
        }

        private static void OpenSiftedBag(WorldSession session, CharacterItemModel bag, int topTier)
        {
            // A few different resources, a few of each, weighted towards the lower tiers.
            var found = new Dictionary<uint, uint>();
            int kinds = random.Next(2, 5);
            for (int i = 0; i < kinds; i++)
            {
                int tier = Math.Min(topTier, 1 + (int)Math.Floor(Math.Pow(random.NextDouble(), 1.5) * topTier));
                uint[] pool = tiers[tier - 1];
                uint resource = pool[random.Next(pool.Length)];
                found[resource] = found.GetValueOrDefault(resource) + (uint)random.Next(5, 16) / (uint)tier;
            }
            if (found.Any(f => session.Inventory.SpaceFor(f.Key) < f.Value))
            {
                Chat.System(session, "You need more room in your inventory to open this bag.");
                return;
            }
            if (!session.TakeItem(bag.ItemDefinitionId, 1))
                return;
            foreach ((uint resource, uint count) in found)
                session.GiveItem(resource, Math.Max(1u, count));
            Chat.System(session, "The bag held " + string.Join(", ", found.Select(f => $"{Math.Max(1u, f.Value)} {NameOf(f.Key)}")) + ".");
            log.Info($"{session.CharacterName} opened sifted bag {bag.ItemDefinitionId}.");
        }

        private static string NameOf(uint item)
            => ItemDefinitionManager.Get(item) is { } d ? TextManager.Instance.GetTextForId(d.NameId) ?? $"item {item}" : $"item {item}";

        /// <summary>Item by its exact (English) name; 0 when no item has it.</summary>
        public static uint ItemNamed(string name)
        {
            itemsByName ??= ItemDefinitionManager.All
                .Select(d => (d.Id, Name: TextManager.Instance.GetTextForId(d.NameId) ?? ""))
                .Where(d => d.Name.Length > 0)
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(d => d.Id), StringComparer.OrdinalIgnoreCase);
            return itemsByName.TryGetValue(name.Trim(), out uint id) ? id : 0;
        }

        /// <summary>Builder's Bags and Chests: their description says what they hold.</summary>
        private static Dictionary<uint, (uint, uint)> BuildUnpacks()
        {
            var map = new Dictionary<uint, (uint, uint)>();
            foreach (var definition in ItemDefinitionManager.All)
            {
                string name = TextManager.Instance.GetTextForId(definition.NameId) ?? "";
                if (!name.Contains("Builder's Bag") && !name.Contains("Builder's Chest"))
                    continue;
                Match m = contains.Match(TextManager.Instance.GetTextForId(definition.DescriptionId) ?? "");
                // Older descriptions name processed textiles "Woven Cotton"; in this build they are "Cotton Block".
                string contents = m.Success ? m.Groups[2].Value.Trim() : "";
                uint item = m.Success ? ItemNamed(contents) : 0;
                if (item == 0 && contents.StartsWith("Woven "))
                    item = ItemNamed(contents["Woven ".Length..] + " Block");
                if (item == 0 || !uint.TryParse(m.Groups[1].Value.Replace(",", ""), out uint count))
                {
                    log.Warn($"Builder's bag {definition.Id} ({name}): contents not understood.");
                    continue;
                }
                map[definition.Id] = (item, count);
                if (name.Contains("Builder's Chest"))
                    chestOutfits[definition.Id] = OutfitsIn(TextManager.Instance.GetTextForId(definition.DescriptionId) ?? "");
            }
            log.Info($"Builder's Chest bonus outfits: {chestOutfits.Values.FirstOrDefault()?.Length ?? 0}.");
            log.Info($"{map.Count} builder's bags and chests can be unpacked.");
            return map;
        }
    }
}
