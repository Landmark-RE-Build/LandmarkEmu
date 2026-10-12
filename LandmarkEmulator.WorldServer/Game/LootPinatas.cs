using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Loot pinatas: things that drop into the world and fly into their owner when they walk near — the resource chunks
    /// that pop out of a mined hole, a chopped tree or a picked plant, and the loot bags creatures drop.
    ///
    /// LootPinataBase 0xFB, u8 sub:
    /// - 1 SpawnLoot (S→C, reader FUN_1430775a0, handler FUN_14307a310): u32 loot id, u32 definition (LootPinata.txt:
    ///   model; 5 = loot bag, 21 stone, 79 plain wood, 85 cotton…), vec3, then the vec3 the pinata is created at, f32,
    ///   u64 owner, string, u32 kind (0 keeps the position; 3 = harvest, tells the action manager), u32 ×3.
    /// - 5 PickupLoot (C→S {u32 id, u64 player}) when one reaches its owner (FUN_14307d0f0: auto-scoop in range);
    ///   answered by 3 PickupLootResponse {u32 id, u64 player, bool} which removes it.
    /// - 2 RemoveLoot (S→C {u32 id, u64 player}): the server clears one that was never collected.
    ///
    /// Harvest chunks are only looks (the resources were given at the swing); a loot bag carries its items, given on
    /// pickup. Uncollected pinatas are removed after <see cref="ChunkLinger"/> / <see cref="BagLinger"/>.
    /// </summary>
    public static class LootPinatas
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte SpawnLootSub = 1, RemoveLootSub = 2, PickupLootResponseSub = 3, PickupLootSub = 5;

        /// <summary>LootPinata.txt loot bag (Theme_Loot_Containers_Bag01).</summary>
        public const uint LootBag = 5;
        /// <summary>Fallback chunk for resources without a model of their own (stone).</summary>
        public const uint DefaultChunk = 21;

        private static readonly TimeSpan ChunkLinger = TimeSpan.FromSeconds(10), BagLinger = TimeSpan.FromSeconds(60);

        private static int nextId;

        private sealed record Pending(WorldSession Owner, IReadOnlyList<(uint Item, uint Count)> Contents);

        /// <summary>Pinatas not collected yet, by loot id.</summary>
        private static readonly ConcurrentDictionary<uint, Pending> pending = new();

        /// <summary>Overrides where the name match picks the wrong model (resource item → LootPinata.txt id).</summary>
        private static readonly Dictionary<uint, uint> pinataOverrides = new()
        {
            [44] = 14, [45] = 19, [46] = 21, [76] = 43,
            [34] = 59, [35] = 63, [36] = 61, [37] = 75, [39] = 69,
            [47] = 16, [48] = 12, [49] = 18, [50] = 10, [51] = 23,
            [71] = 79, [72] = 77, [73] = 81,
        };

        private static Dictionary<uint, uint> byItem;

        /// <summary>
        /// The chunk model for a resource item: an override, else the LootPinata.txt model whose name words are all in
        /// the item's name ("Biome_Generic_Loot_Wood_Plain01" → plain, wood; "…Plants_Cotton_Loot01" → cotton;
        /// "…Metals01_Dull_Copper01" → copper), else stone.
        /// </summary>
        public static uint PinataFor(uint item)
        {
            if (pinataOverrides.TryGetValue(item, out uint pinata))
                return pinata;
            byItem ??= BuildNameIndex();
            return byItem.TryGetValue(item, out pinata) ? pinata : DefaultChunk;
        }

        private static readonly HashSet<string> modelNoise = new()
        {
            "biome", "generic", "loot", "plants", "earth", "liquid", "gemstone", "metals", "dull", "shiny", "emmisive",
            "resource", "dme", "adr", "ashfang", "everfrost", "crystalcavern", "junglecavern", "forestcavern",
            "desertcavern", "lavastorm", "succulents", "flowers", "scrub"
        };

        private static HashSet<string> Words(string text)
            => Regex.Split(Regex.Replace(text, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(), "[^a-z]+")
                .Where(w => w.Length > 1 && !modelNoise.Contains(w)).ToHashSet();

        private static Dictionary<uint, uint> BuildNameIndex()
        {
            // Resource models only: ids up to 106 (above are gear models for gear drops).
            var models = GameDataFiles.Rows("LootPinata.txt").Where(r => r.U(0) is > 9 and <= 106)
                .Select(r => (Id: r.U(0), Words: Words(r.S(1)))).Where(m => m.Words.Count > 0).ToList();
            var map = new Dictionary<uint, uint>();
            foreach (var item in Inventory.ItemDefinitionManager.All)
            {
                string name = TextManager.Instance.GetTextForId(item.NameId) ?? "";
                if (name.Length == 0)
                    continue;
                HashSet<string> words = Words(name);
                var best = models.Where(m => m.Words.IsSubsetOf(words)).OrderByDescending(m => m.Words.Count).FirstOrDefault();
                if (best.Id != 0)
                    map[item.Id] = best.Id;
            }
            log.Info($"{map.Count} items have a loot chunk model.");
            return map;
        }

        /// <summary>
        /// PickupLoot from the client: a pinata reached its owner. Gives a bag's contents, then always answers so the
        /// client removes it.
        /// </summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            if (sub != PickupLootSub || body.Length < 4)
                return;
            uint id = BitConverter.ToUInt32(body, 0);
            if (pending.TryRemove(id, out Pending loot) && loot.Owner == session)
                foreach ((uint item, uint count) in loot.Contents)
                {
                    session.GiveItem(item, count);
                    Notify(session, item, count);
                }
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(PickupLootResponseSub);
            w.Write(id);
            w.Write(session.PlayerGuid);
            w.Write(true);
            session.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, output.ToArray());
        }

        /// <summary>
        /// Inventory LootNotification (0x2D, u16 sub 0x1A; reader FUN_142e78620, handler FUN_142e81590 →
        /// ClientPlayerInventoryManager::DisplayLootNotification FUN_142e7f050): {string loot name, u32 quality (its colour
        /// from the client's quality table), u32 count, u32 icon} → the "LootNotificationMultiple" toast
        /// (NotificationWindow:OnShowFormattedMessage) when loot reaches the player.
        /// </summary>
        public static void Notify(WorldSession session, uint item, uint count)
        {
            var def = Inventory.ItemDefinitionManager.Get(item);
            if (def == null || count == 0)
                return;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(0x1A);
            w.Write(Shared.GameTable.Text.TextManager.Instance.GetTextForId(def.NameId) ?? "");
            w.Write(def.Quality);
            w.Write(count);
            w.Write(def.ImageSetId);
            session.EnqueueMessage(ZoneMessageOpcode.InventoryBase, output.ToArray());
        }

        /// <summary>A harvest chunk (looks only): a resource's chunk that flies into the player.</summary>
        public static void SpawnChunk(WorldSession owner, Vector3 position, uint item)
            => Spawn(owner, position, PinataFor(item));

        /// <summary>A loot bag holding items, given to the owner when they collect it.</summary>
        public static void SpawnBag(WorldSession owner, Vector3 position, IReadOnlyList<(uint Item, uint Count)> contents)
        {
            if (contents.Count == 0)
                return;
            Spawn(owner, position, LootBag, contents: contents);
        }

        /// <summary>
        /// Spawns a pinata for one player: SpawnLoot with a new id at the position. Kind 0 keeps the position (kind 3
        /// makes the client zero it).
        /// </summary>
        public static void Spawn(WorldSession owner, Vector3 position, uint pinata, uint kind = 0, uint a = 0, uint b = 0, uint c = 0,
            IReadOnlyList<(uint Item, uint Count)> contents = null)
        {
            uint id = (uint)Interlocked.Increment(ref nextId);
            pending[id] = new Pending(owner, contents ?? Array.Empty<(uint, uint)>());
            Task.Delay(contents is { Count: > 0 } ? BagLinger : ChunkLinger).ContinueWith(_ => RemoveIfLeft(id));

            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(SpawnLootSub);
            w.Write(id);
            w.Write(pinata);
            w.Write(position.X);
            w.Write(position.Y);
            w.Write(position.Z);
            // The pinata is created at these (the first vec3 isn't copied into the create call; verified with a hook).
            w.Write(position.X);
            w.Write(position.Y);
            w.Write(position.Z);
            w.Write(1f);
            w.Write(owner.PlayerGuid);
            w.Write("");
            w.Write(kind);
            w.Write(a);
            w.Write(b);
            w.Write(c);
            owner.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, output.ToArray());
        }

        /// <summary>Clears a pinata its owner never collected (RemoveLoot); a bag's items are lost with it.</summary>
        private static void RemoveIfLeft(uint id)
        {
            if (!pending.TryRemove(id, out Pending loot) || loot.Owner.Disconnected)
                return;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(RemoveLootSub);
            w.Write(id);
            w.Write(loot.Owner.PlayerGuid);
            loot.Owner.EnqueueMessage(ZoneMessageOpcode.LootPinataBase, output.ToArray());
        }
    }
}
