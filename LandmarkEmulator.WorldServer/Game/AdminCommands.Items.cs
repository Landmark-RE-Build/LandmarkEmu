using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The retail client's /item admin command (0.695 command function FUN_1433acb90; sub-commands add, delete,
    /// undelete, list, check, vault, unvault, random, collect, uncollect, vaultcollect, vaultuncollect, overflow,
    /// unoverflow, buyback, unbuyback). Each sends a Command 0x1E sub with the handler group "Item"; the client prints its
    /// own "Giving … / Deleting … / Requesting …" line first, and the server answers in chat. Layouts [P] from the 0.695
    /// writers (trace/0695/admin-commands-695.md, "Item commands"); the target is the typed name / guid, else the admin
    /// target, else the admin.
    /// </summary>
    public static partial class AdminCommands
    {
        public const ushort AddItemSub = 0x3EA;            // PacketAddItem, writer FUN_14338f8c0
        public const ushort DeleteItemSub = 0x3EB;         // PacketDeleteItem (delete / undelete), FUN_143390000
        public const ushort DeleteItemByIdSub = 0x433;     // PacketDeleteItemById, FUN_143390200 (no command calls it)
        public const ushort GetItemListSub = 0x434;        // PacketGetItemList (/item list), FUN_143390590
        public const ushort GetItemInfoSub = 0x48A;        // PacketGetItemInfo (/item check), FUN_1433903a0
        public const ushort VaultItemSub = 0x4F2;          // PacketVaultItemById (vault / unvault), FUN_1433910c0
        public const ushort RandomItemSub = 0x4F3;         // PacketRandomItem (/item random), FUN_143390d20
        public const ushort CollectionItemSub = 0x4F4;     // PacketCollectionItemById (collect / uncollect), FUN_14338fe00
        public const ushort VaultCollectionItemSub = 0x4FA; // PacketVaultCollectionItemById, FUN_143390ec0
        public const ushort OverflowItemSub = 0x4FF;       // PacketOverflowItemById (overflow / unoverflow), FUN_143390930
        public const ushort BuybackItemSub = 0x500;        // PacketBuybackItemById (buyback / unbuyback), FUN_14338fa60

        /// <summary>Client ContainerId (soe.constants.ContainerId): 4 BUYBACK and 6 VAULT_COLLECTION have no 0.695 data source.</summary>
        private const uint BuybackContainer = 4, VaultCollectionContainer = 6;
        /// <summary>Overflow slots searched for a free one (Inventory's own limit).</summary>
        private const uint OverflowSlots = 500;

        /// <summary>GriefAdmin (handler group "Grief"): {u32 state 1, u32 type, …} — 1 SetDamageLevel {u32}, 2 SetGriefLevel
        /// {u32}, 3 ResetGrief (0.695 FUN_142ee2280 / FUN_142ee21f0, base writer FUN_142ee04e0) [P].</summary>
        public const ushort GriefAdminSub = 0x4DA;

        private static bool IsItemSub(ushort sub) => sub == GriefAdminSub || sub is AddItemSub or DeleteItemSub or DeleteItemByIdSub or GetItemListSub
            or GetItemInfoSub or VaultItemSub or RandomItemSub or CollectionItemSub or VaultCollectionItemSub or OverflowItemSub
            or BuybackItemSub;

        /// <summary>Stacks removed by /item delete, kept for /item undelete while the world runs: guid → (owner, item, count).</summary>
        private static readonly ConcurrentDictionary<ulong, (ulong Owner, uint Item, uint Count)> deletedItems = new();

        private static void HandleItem(WorldSession admin, ushort sub, byte[] body)
        {
            var r = new GamePacketReader(body);
            switch (sub)
            {
                case GriefAdminSub: GriefAdmin(admin, r); break;
                case AddItemSub: AddItem(admin, r); break;
                case DeleteItemSub: DeleteItem(admin, r); break;
                case DeleteItemByIdSub: DeleteItemById(admin, r); break;
                case GetItemListSub: ItemList(admin, r); break;
                case GetItemInfoSub: ItemInfo(admin, r); break;
                case RandomItemSub: RandomItem(admin, r); break;
                case VaultItemSub: MoveById(admin, r, ItemUses.VaultContainer, "vault"); break;
                case CollectionItemSub: MoveById(admin, r, Inventory.Inventory.ResourceContainer, "collection"); break;
                case OverflowItemSub: MoveById(admin, r, Inventory.Inventory.OverflowContainer, "overflow"); break;
                case VaultCollectionItemSub: MoveById(admin, r, VaultCollectionContainer, "vault collection"); break;
                case BuybackItemSub: MoveById(admin, r, BuybackContainer, "buyback"); break;
            }
        }

        /// <summary>
        /// The client's grief commands act on the admin (the packet has no target): set the damage level (the client's
        /// "damagePoints", kept beside the grief points and sent as GriefBase 1), set the grief level (GriefBase 2: the
        /// client prints entered / left level, equipment lock at 4, suspension at 5), or reset (GriefBase 3). The state is
        /// saved with the character either way. See <see cref="Grief"/>.
        /// </summary>
        private static void GriefAdmin(WorldSession admin, GamePacketReader r)
        {
            r.ReadUInt();
            uint type = r.ReadUInt();
            switch (type)
            {
                case 1:
                    Grief.SetPoints(admin, -1, (int)r.ReadUInt());
                    break;
                case 2:
                    Grief.SetLevel(admin, (int)r.ReadUInt());
                    break;
                case 3:
                    Grief.Reset(admin);
                    break;
                default:
                    log.Info($"Admin {admin.CharacterName}: grief admin type {type} not known.");
                    break;
            }
        }

        /// <summary>The command's target: an online player by guid, else the admin (the client already put the admin
        /// target or the admin's own guid in the field).</summary>
        private static WorldSession ItemTarget(WorldSession admin, ulong guid, string what)
        {
            if (NoGuid(guid) || guid == admin.PlayerGuid)
                return admin;
            if (PlayerManager.FindByGuid(guid) is WorldSession target && !target.Disconnected)
                return target;
            Chat.System(admin, $"{what}: no player online with guid {guid}.");
            return null;
        }

        private static string ItemName(uint item)
            => ItemDefinitionManager.Get(item) is { } d ? TextManager.Instance.GetTextForId(d.NameId) ?? $"item {item}" : $"item {item}";

        /// <summary>
        /// /item add &lt;item&gt; [count] [tint] [name/guid] — PacketAddItem 0x3EA {u32 item, u32 tint, u32 count (≥ 1), u64
        /// target, u32, u32, u8} [P] (the last three are 0 from the command). Tints are not kept on items here, so the
        /// tint is reported only.
        /// </summary>
        private static void AddItem(WorldSession admin, GamePacketReader r)
        {
            uint item = r.ReadUInt(), tint = r.ReadUInt(), count = Math.Max(1u, r.ReadUInt());
            ulong guid = r.ReadULong();
            if (ItemTarget(admin, guid, "/item add") is not WorldSession target)
                return;
            if (ItemDefinitionManager.Get(item) == null)
            {
                Chat.System(admin, $"/item add: there is no item {item}.");
                return;
            }
            target.GiveItem(item, count);
            Chat.System(admin, $"Gave {count} × {ItemName(item)} ({item}) to {target.CharacterName}." + (tint != 0 ? $" (Tint {tint} is not kept on items.)" : ""));
            log.Info($"Admin {admin.CharacterName}: /item add {item} ×{count} → {target.CharacterName}.");
        }

        /// <summary>
        /// /item delete|undelete &lt;item guid&gt; [name/guid] — PacketDeleteItem 0x3EB {u64 item guid, u64 target, u8 undelete}
        /// [P]. A deleted stack is remembered until the world restarts so undelete can give it back.
        /// </summary>
        private static void DeleteItem(WorldSession admin, GamePacketReader r)
        {
            ulong itemGuid = r.ReadULong(), guid = r.ReadULong();
            bool undelete = r.ReadByte() != 0;
            if (ItemTarget(admin, guid, undelete ? "/item undelete" : "/item delete") is not WorldSession target)
                return;
            if (undelete)
            {
                if (!deletedItems.TryRemove(itemGuid, out var gone) || gone.Owner != target.PlayerGuid)
                {
                    Chat.System(admin, $"/item undelete: no deleted item {itemGuid} of {target.CharacterName} to restore.");
                    return;
                }
                target.GiveItem(gone.Item, gone.Count);
                Chat.System(admin, $"Restored {gone.Count} × {ItemName(gone.Item)} to {target.CharacterName}.");
                log.Info($"Admin {admin.CharacterName}: /item undelete {itemGuid} → {target.CharacterName}.");
                return;
            }
            CharacterItemModel stack = target.Inventory.Get(itemGuid);
            if (stack == null)
            {
                Chat.System(admin, $"/item delete: {target.CharacterName} has no item with guid {itemGuid}.");
                return;
            }
            uint item = stack.ItemDefinitionId, count = stack.Count;
            target.TakeFromStack(itemGuid, count);
            deletedItems[itemGuid] = (target.PlayerGuid, item, count);
            Chat.System(admin, $"Deleted {count} × {ItemName(item)} (guid {itemGuid}) from {target.CharacterName}; /item undelete {itemGuid} gives it back.");
            log.Info($"Admin {admin.CharacterName}: /item delete {itemGuid} ({item} ×{count}) from {target.CharacterName}.");
        }

        /// <summary>
        /// PacketDeleteItemById 0x433 {u32, u32, u64 target} [P layout]; no 0.695 command builds it (the builder
        /// FUN_1433c7690 has no caller) — read as {item, count} [I] and taken like a quest-item removal.
        /// </summary>
        private static void DeleteItemById(WorldSession admin, GamePacketReader r)
        {
            uint item = r.ReadUInt(), count = Math.Max(1u, r.ReadUInt());
            ulong guid = r.ReadULong();
            if (ItemTarget(admin, guid, "delete item by id") is not WorldSession target)
                return;
            uint have = target.Inventory.CountOf(item);
            uint take = Math.Min(have, count);
            if (take == 0 || !target.TakeItem(item, take))
            {
                Chat.System(admin, $"{target.CharacterName} has no {ItemName(item)} ({item}).");
                return;
            }
            Chat.System(admin, $"Took {take} × {ItemName(item)} from {target.CharacterName}.");
            log.Info($"Admin {admin.CharacterName}: delete item {item} ×{take} from {target.CharacterName}.");
        }

        /// <summary>/item list [name/guid] — PacketGetItemList 0x434 {u64 target, u32} [P]: every stack, by container.</summary>
        private static void ItemList(WorldSession admin, GamePacketReader r)
        {
            ulong guid = r.ReadULong();
            if (ItemTarget(admin, guid, "/item list") is not WorldSession target)
                return;
            List<CharacterItemModel> items = target.Inventory.Items.OrderBy(i => i.Container).ThenBy(i => i.Slot).ToList();
            Chat.System(admin, $"{target.CharacterName}: {items.Count} stacks.");
            // Short lines: a long system line makes the client's notification window fail ("NotificationWindow,
            // ASInvoke, Failed to invoke,, ShowSystemMessage", seen in the 2026-10-17 test); the chat line still shows.
            foreach (var group in items.GroupBy(i => i.Container))
                foreach (var chunk in group.Chunk(6))
                    Chat.System(admin, $"Container {group.Key}: " + string.Join(", ",
                        chunk.Select(i => $"[{i.Slot}] {i.Count} × {ItemName(i.ItemDefinitionId)} ({i.ItemDefinitionId}, guid {i.Id})")));
        }

        /// <summary>/item check &lt;item&gt; [name/guid] — PacketGetItemInfo 0x48A {u64 target, u32 item} [P].</summary>
        private static void ItemInfo(WorldSession admin, GamePacketReader r)
        {
            ulong guid = r.ReadULong();
            uint item = r.ReadUInt();
            if (ItemTarget(admin, guid, "/item check") is not WorldSession target)
                return;
            var stacks = target.Inventory.Items.Where(i => i.ItemDefinitionId == item).ToList();
            Chat.System(admin, $"{target.CharacterName} has {stacks.Sum(s => (long)s.Count)} × {ItemName(item)} ({item})"
                + (stacks.Count > 0 ? ": " + string.Join(", ", stacks.Select(s => $"{s.Count} in container {s.Container} slot {s.Slot} (guid {s.Id})")) : "."));
        }

        /// <summary>
        /// /item random &lt;item&gt; [catalyst -1] [count 1] — PacketRandomItem 0x4F3 {u32 item, i32 catalyst, u32 count} [P].
        /// Live rolled a random version of the item (the catalyst picking the roll table); this build's items have no
        /// rolled variants, so the item itself is given to the admin [I].
        /// </summary>
        private static void RandomItem(WorldSession admin, GamePacketReader r)
        {
            uint item = r.ReadUInt();
            int catalyst = r.ReadInt();
            uint count = Math.Max(1u, r.ReadUInt());
            if (ItemDefinitionManager.Get(item) == null)
            {
                Chat.System(admin, $"/item random: there is no item {item}.");
                return;
            }
            admin.GiveItem(item, count);
            Chat.System(admin, $"Gave {count} × {ItemName(item)} ({item})" + (catalyst >= 0 ? $" (catalyst {catalyst} has no roll table here)." : "."));
        }

        /// <summary>
        /// vault / collect / vaultcollect / overflow / buyback and their un- forms — {u64 item (0 = all), u64 target, u8
        /// into} [P] (subs 0x4F2 / 0x4F4 / 0x4FA / 0x4FF / 0x500). "into" moves the item's bag stacks to that container,
        /// otherwise its stacks there come back to the bag. The item is a stack guid or an item id (the client prints
        /// "item id %d"). Buyback (4) and vault collection (6) have no data source in the 0.695 client, so nothing is
        /// moved there (refused with the reason).
        /// </summary>
        private static void MoveById(WorldSession admin, GamePacketReader r, uint container, string name)
        {
            ulong id = r.ReadULong(), guid = r.ReadULong();
            bool into = r.ReadByte() != 0;
            if (ItemTarget(admin, guid, $"/item {name}") is not WorldSession target)
                return;
            if (container is BuybackContainer or VaultCollectionContainer)
            {
                Chat.System(admin, $"/item {name}: the 0.695 client shows no {name} container (ContainerId {container}), nothing moved.");
                return;
            }
            Inventory.Inventory inventory = target.Inventory;
            uint from = into ? Inventory.Inventory.BagContainer : container, to = into ? container : Inventory.Inventory.BagContainer;
            List<CharacterItemModel> stacks = inventory.Items.Where(i => i.Container == from
                && (id == 0 || i.Id == id || i.ItemDefinitionId == id)).ToList();
            int moved = 0;
            if (container == Inventory.Inventory.ResourceContainer && into)
            {
                // Collections (the resource tab) keep one stack per resource in its fixed slot (InventoryDefinitions):
                // the stack is taken from the bag and added through the normal resource routing.
                foreach (CharacterItemModel stack in stacks.Where(i => Inventory.Inventory.IsResource(i.ItemDefinitionId)).ToList())
                {
                    uint item = stack.ItemDefinitionId, count = stack.Count;
                    target.TakeFromStack(stack.Id, count);
                    target.GiveItem(item, count);
                    moved++;
                }
                Chat.System(admin, $"Moved {moved} of {stacks.Count} stack(s) of {target.CharacterName} into the collection"
                    + (moved < stacks.Count ? " (only resources go there)." : "."));
                return;
            }
            uint capacity = to == Inventory.Inventory.OverflowContainer ? OverflowSlots : inventory.CapacityOf(to);
            foreach (CharacterItemModel stack in stacks)
            {
                uint? slot = null;
                for (uint s = 0; s < capacity && slot == null; s++)
                    if (inventory.At(to, s) == null)
                        slot = s;
                if (slot == null)
                    break;
                foreach (CharacterItemModel changed in inventory.Move(stack, to, slot.Value))
                    target.SendItem(changed, replace: true);
                moved++;
            }
            Chat.System(admin, $"Moved {moved} of {stacks.Count} stack(s) of {target.CharacterName} {(into ? "into" : "out of")} the {name}"
                + (moved < stacks.Count ? " (no free slot for the rest)." : "."));
            log.Info($"Admin {admin.CharacterName}: /item {(into ? "" : "un")}{name} {id} for {target.CharacterName}: {moved} moved.");
        }
    }
}
